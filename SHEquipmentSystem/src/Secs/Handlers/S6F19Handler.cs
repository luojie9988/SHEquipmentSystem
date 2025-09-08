// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S6F19Handler.cs
// 版本: v1.0.0
// 描述: S6F19消息处理器 - Individual Report Request 单个报告请求处理器

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Helpers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S6F19 (Individual Report Request) 处理器
    /// 处理主机的单个报告请求，返回指定报告的当前数据
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S6F19: 单个报告请求 - 主机查询特定报告的当前数据
    /// - S6F20: 单个报告数据 - 设备返回报告包含的变量值
    /// 
    /// 交互流程：
    /// 1. 主机发送S6F19请求特定RPTID的报告数据
    /// 2. 设备验证RPTID的有效性
    /// 3. 获取报告定义的变量列表（通过S2F33配置）
    /// 4. 收集每个变量的当前值
    /// 5. 返回S6F20包含完整的报告数据
    /// 
    /// 使用场景：
    /// - 主机主动查询设备状态
    /// - 获取特定报告的实时数据
    /// - 验证报告配置是否正确
    /// - 周期性数据采集
    /// - 故障诊断和调试
    /// 
    /// 划裂片设备特点：
    /// - 支持查询所有已定义的报告
    /// - 返回实时数据而非历史快照
    /// - 支持预定义报告和动态报告
    /// - 可查询未链接到事件的报告
    /// 
    /// 与S6F15的区别：
    /// - S6F15: 基于事件(CEID)查询关联的报告
    /// - S6F19: 直接基于报告(RPTID)查询
    /// </remarks>
    public class S6F19Handler : SecsMessageHandlerBase
    {
        #region 划裂片设备预定义报告

        /// <summary>
        /// 划裂片设备标准报告定义
        /// </summary>
        private static class StandardReports
        {
            /// <summary>设备状态报告</summary>
            public const uint DeviceStatus = 1;

            /// <summary>处理状态报告</summary>
            public const uint ProcessStatus = 2;

            /// <summary>材料状态报告</summary>
            public const uint MaterialStatus = 10;

            /// <summary>刀具状态报告</summary>
            public const uint KnifeStatus = 20;

            /// <summary>工艺参数报告</summary>
            public const uint ProcessParameters = 30;

            /// <summary>生产统计报告</summary>
            public const uint ProductionStatistics = 40;

            /// <summary>报警状态报告</summary>
            public const uint AlarmStatus = 50;

            /// <summary>维护状态报告</summary>
            public const uint MaintenanceStatus = 60;

            /// <summary>环境参数报告</summary>
            public const uint EnvironmentParameters = 70;

            /// <summary>质量数据报告</summary>
            public const uint QualityData = 80;
        }

        #endregion

        #region 私有字段

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventService;

        /// <summary>S2F33处理器（获取报告定义）</summary>
        private readonly S2F33Handler? _reportHandler;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>预定义报告映射（RPTID -> VID列表）</summary>
        private readonly Dictionary<uint, List<uint>> _predefinedReports;

        /// <summary>数据采集缓存（用于优化性能）</summary>
        private readonly Dictionary<uint, CachedValue> _valueCache;

        /// <summary>缓存锁</summary>
        private readonly ReaderWriterLockSlim _cacheLock = new();

        /// <summary>缓存有效期（毫秒）</summary>
        private const int CacheValidityMs = 1000;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 6;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 19;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="statusService">状态变量服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="eventService">事件报告服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="serviceProvider">服务提供者</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S6F19Handler(
            ILogger<S6F19Handler> logger,
            IStatusVariableService statusService,
            IEquipmentStateService stateService,
            IEventReportService eventService,
            IOptions<EquipmentSystemConfiguration> options,
            IServiceProvider serviceProvider) : base(logger)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // 尝试获取S2F33处理器
            _reportHandler = serviceProvider.GetService(typeof(S2F33Handler)) as S2F33Handler;

            // 初始化预定义报告
            _predefinedReports = InitializePredefinedReports();

            // 初始化缓存
            _valueCache = new Dictionary<uint, CachedValue>();

            Logger.LogInformation($"S6F19处理器已初始化，定义了 {_predefinedReports.Count} 个预定义报告");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S6F19 消息，返回 S6F20 响应
        /// </summary>
        /// <param name="message">接收到的S6F19消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S6F20响应消息</returns>
        /// <remarks>
        /// S6F19 处理逻辑：
        /// 1. 解析请求的RPTID
        /// 2. 验证RPTID有效性
        /// 3. 获取报告定义的变量
        /// 4. 收集变量的当前值
        /// 5. 构建并返回S6F20响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S6F19 (Individual Report Request) 单个报告请求");

            try
            {
                // 解析RPTID
                var rptid = ParseRptid(message.SecsItem);

                if (rptid == 0)
                {
                    Logger.LogWarning("S6F19消息中RPTID无效");
                    return CreateEmptyS6F20Response();
                }

                Logger.LogDebug($"请求报告 {rptid} 的数据");

                // 验证RPTID有效性
                if (!await IsValidRptid(rptid, cancellationToken))
                {
                    Logger.LogWarning($"RPTID {rptid} 无效或未定义");
                    return CreateErrorS6F20Response(rptid);
                }

                // 收集报告数据
                var variableData = await CollectReportData(rptid, cancellationToken);

                // 构建S6F20响应
                var s6f20 = CreateS6F20Response(rptid, variableData);

                Logger.LogInformation($"S6F20响应准备就绪，RPTID: {rptid}, 变量数: {variableData.Count}");

                // 记录报告查询
                await LogReportQuery(rptid, variableData.Count);

                return s6f20;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S6F19消息失败");
                return CreateEmptyS6F20Response();
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析RPTID
        /// </summary>
        private uint ParseRptid(Item? item)
        {
            return SecsItemHelper.ParseUInt(item);
        }

        #endregion

        #region 私有方法 - 数据收集

        /// <summary>
        /// 收集报告数据
        /// </summary>
        private async Task<List<VariableValue>> CollectReportData(
            uint rptid,
            CancellationToken cancellationToken)
        {
            var variableValues = new List<VariableValue>();

            try
            {
                // 获取报告定义的变量列表
                var vidList = await GetReportVariables(rptid, cancellationToken);

                if (vidList.Count == 0)
                {
                    Logger.LogDebug($"报告 {rptid} 没有定义变量");
                    return variableValues;
                }

                Logger.LogTrace($"报告 {rptid} 包含 {vidList.Count} 个变量");

                // 批量收集变量值（优化性能）
                var tasks = vidList.Select(vid => CollectVariableValue(vid, cancellationToken));
                var results = await Task.WhenAll(tasks);

                // 组装结果
                for (int i = 0; i < vidList.Count; i++)
                {
                    variableValues.Add(new VariableValue
                    {
                        Vid = vidList[i],
                        Value = results[i],
                        Name = GetVariableName(vidList[i])
                    });
                }

                // 特殊处理某些报告
                await EnrichReportData(rptid, variableValues, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"收集报告 {rptid} 数据失败");
            }

            return variableValues;
        }

        /// <summary>
        /// 收集单个变量值
        /// </summary>
        private async Task<object> CollectVariableValue(
            uint vid,
            CancellationToken cancellationToken)
        {
            try
            {
                // 检查缓存
                var cachedValue = GetCachedValue(vid);
                if (cachedValue != null)
                {
                    Logger.LogTrace($"变量 {vid} 使用缓存值");
                    return cachedValue;
                }

                // 获取实时值
                var value = await GetVariableValue(vid, cancellationToken);

                // 更新缓存
                UpdateCache(vid, value);

                return value;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"获取变量 {vid} 值失败");
                return GetDefaultValue(vid);
            }
        }

        /// <summary>
        /// 获取变量值
        /// </summary>
        private async Task<object> GetVariableValue(
            uint vid,
            CancellationToken cancellationToken)
        {
            // 特殊变量处理
            var specialValue = GetSpecialVariableValue(vid);
            if (specialValue != null)
            {
                return specialValue;
            }

            // 从状态变量服务获取
            return await _statusService.GetSvidValueAsync(vid);
        }

        /// <summary>
        /// 获取特殊变量值
        /// </summary>
        private object? GetSpecialVariableValue(uint vid)
        {
            return vid switch
            {
                1 => DateTime.Now.ToString("yyyyMMddHHmmss"),     // Clock
                2 => _config.Equipment.ModelName,                  // ModelName
                3 => _config.Equipment.SoftwareRevision,          // SoftwareRevision
                4 => GetCurrentRecipeName(),                      // PPExecName
                5 => GetCurrentMaterialId(),                      // MaterialID
                _ => null
            };
        }

        /// <summary>
        /// 获取当前配方名称
        /// </summary>
        private string GetCurrentRecipeName()
        {
            try
            {
                var statusInfo = _stateService.GetStatusInfoAsync().Result;
                return statusInfo.CurrentRecipe ?? "NONE";
            }
            catch
            {
                return "NONE";
            }
        }

        /// <summary>
        /// 获取当前材料ID
        /// </summary>
        private string GetCurrentMaterialId()
        {
            try
            {
                var statusInfo = _stateService.GetStatusInfoAsync().Result;
                return statusInfo.CurrentMaterialId ?? "NONE";
            }
            catch
            {
                return "NONE";
            }
        }

        /// <summary>
        /// 丰富报告数据
        /// </summary>
        private async Task EnrichReportData(
            uint rptid,
            List<VariableValue> variableValues,
            CancellationToken cancellationToken)
        {
            // 根据报告类型添加额外信息
            switch (rptid)
            {
                case StandardReports.DeviceStatus:
                    await AddDeviceStatusInfo(variableValues, cancellationToken);
                    break;

                case StandardReports.ProcessStatus:
                    await AddProcessStatusInfo(variableValues, cancellationToken);
                    break;

                case StandardReports.KnifeStatus:
                    await AddKnifeStatusInfo(variableValues, cancellationToken);
                    break;

                case StandardReports.ProductionStatistics:
                    await AddProductionStatistics(variableValues, cancellationToken);
                    break;
            }
        }

        /// <summary>
        /// 添加设备状态信息
        /// </summary>
        private async Task AddDeviceStatusInfo(
            List<VariableValue> variableValues,
            CancellationToken cancellationToken)
        {
            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 添加计算字段
            variableValues.Add(new VariableValue
            {
                Vid = 9999,
                Name = "Availability",
                Value = statusInfo.IsAvailable ? 100.0f : 0.0f
            });
        }

        /// <summary>
        /// 添加处理状态信息
        /// </summary>
        private async Task AddProcessStatusInfo(
            List<VariableValue> variableValues,
            CancellationToken cancellationToken)
        {
            // 可以添加处理进度等信息
            await Task.CompletedTask;
        }

        /// <summary>
        /// 添加刀具状态信息
        /// </summary>
        private async Task AddKnifeStatusInfo(
            List<VariableValue> variableValues,
            CancellationToken cancellationToken)
        {
            // 计算刀具寿命百分比
            var useCount = variableValues.FirstOrDefault(v => v.Vid == 10008)?.Value ?? 0;
            var maxCount = variableValues.FirstOrDefault(v => v.Vid == 10009)?.Value ?? 100000;

            if (useCount is uint use && maxCount is uint max && max > 0)
            {
                var lifePercentage = 100.0f - (use * 100.0f / max);
                variableValues.Add(new VariableValue
                {
                    Vid = 10010,
                    Name = "KnifeLifePercentage",
                    Value = lifePercentage
                });
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 添加生产统计
        /// </summary>
        private async Task AddProductionStatistics(
            List<VariableValue> variableValues,
            CancellationToken cancellationToken)
        {
            // 计算良率
            var goodCount = variableValues.FirstOrDefault(v => v.Vid == 10101)?.Value ?? 0;
            var totalCount = variableValues.FirstOrDefault(v => v.Vid == 10100)?.Value ?? 0;

            if (totalCount is uint total && total > 0 && goodCount is uint good)
            {
                var yield = good * 100.0f / total;
                variableValues.Add(new VariableValue
                {
                    Vid = 10103,
                    Name = "YieldRate",
                    Value = yield
                });
            }

            await Task.CompletedTask;
        }

        #endregion

        #region 私有方法 - 缓存管理

        /// <summary>
        /// 获取缓存值
        /// </summary>
        private object? GetCachedValue(uint vid)
        {
            _cacheLock.EnterReadLock();
            try
            {
                if (_valueCache.TryGetValue(vid, out var cached))
                {
                    if ((DateTime.Now - cached.Timestamp).TotalMilliseconds < CacheValidityMs)
                    {
                        return cached.Value;
                    }
                }
                return null;
            }
            finally
            {
                _cacheLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 更新缓存
        /// </summary>
        private void UpdateCache(uint vid, object value)
        {
            _cacheLock.EnterWriteLock();
            try
            {
                _valueCache[vid] = new CachedValue
                {
                    Vid = vid,
                    Value = value,
                    Timestamp = DateTime.Now
                };
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 验证RPTID有效性
        /// </summary>
        private async Task<bool> IsValidRptid(uint rptid, CancellationToken cancellationToken)
        {
            // 检查预定义报告
            if (_predefinedReports.ContainsKey(rptid))
            {
                return true;
            }

            // 从S2F33处理器检查
            if (_reportHandler != null)
            {
                var definedReports = _reportHandler.GetDefinedReports();
                if (definedReports.Contains(rptid))
                {
                    return true;
                }
            }

            // 从事件报告服务检查
            try
            {
                var reports = await _eventService.GetDefinedReportsAsync();
                return reports.Contains(rptid);
            }
            catch
            {
                // 服务不可用时，进行基本范围检查
                return rptid > 0 && rptid <= 65535;
            }
        }

        /// <summary>
        /// 获取报告变量列表
        /// </summary>
        private async Task<List<uint>> GetReportVariables(uint rptid, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                // 检查预定义报告
                if (_predefinedReports.TryGetValue(rptid, out var predefinedVids))
                {
                    return predefinedVids;
                }

                // 从S2F33Handler获取
                if (_reportHandler != null)
                {
                    var vids = _reportHandler.GetReportVariables(rptid);
                    if (vids.Count > 0)
                    {
                        return vids;
                    }
                }

                // 返回默认变量
                return GetDefaultVariables(rptid);
            }, cancellationToken);
        }

        /// <summary>
        /// 获取默认变量列表
        /// </summary>
        private List<uint> GetDefaultVariables(uint rptid)
        {
            return new List<uint> { 1 }; // 默认只返回时钟
        }

        /// <summary>
        /// 获取变量名称
        /// </summary>
        private string GetVariableName(uint vid)
        {
            return vid switch
            {
                1 => "Clock",
                721 => "ControlState",
                722 => "ProcessState",
                10007 => "KnifeModel",
                10008 => "KnifeUseCount",
                10009 => "KnifeMaxCount",
                10100 => "TotalProcessed",
                10101 => "GoodCount",
                10102 => "NGCount",
                _ => $"V{vid}"
            };
        }

        /// <summary>
        /// 获取默认值
        /// </summary>
        private object GetDefaultValue(uint vid)
        {
            return vid switch
            {
                1 => DateTime.Now.ToString("yyyyMMddHHmmss"),
                < 100 => 0u,
                < 1000 => "",
                _ => 0
            };
        }

        /// <summary>
        /// 记录报告查询
        /// </summary>
        private async Task LogReportQuery(uint rptid, int variableCount)
        {
            try
            {
                await _eventService.LogReportQueryAsync(rptid, variableCount);
                Logger.LogDebug($"记录了报告 {rptid} 的查询");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "记录报告查询失败");
            }
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化预定义报告
        /// </summary>
        private Dictionary<uint, List<uint>> InitializePredefinedReports()
        {
            var reports = new Dictionary<uint, List<uint>>();

            // 设备状态报告
            reports[StandardReports.DeviceStatus] = new List<uint>
            {
                1,    // Clock
                721,  // ControlState
                722,  // ProcessState
                723,  // EquipmentState
                4     // PPExecName
            };

            // 处理状态报告
            reports[StandardReports.ProcessStatus] = new List<uint>
            {
                1,     // Clock
                722,   // ProcessState
                11004, // ProcessStart
                11005, // ProcessEnd
                10011  // CurrentSlot
            };

            // 材料状态报告
            reports[StandardReports.MaterialStatus] = new List<uint>
            {
                1,     // Clock
                10011, // CurrentSlot
                10012, // CassetteID
                10013, // FrameCount
                10010  // MaterialCount
            };

            // 刀具状态报告
            reports[StandardReports.KnifeStatus] = new List<uint>
            {
                1,     // Clock
                10007, // KnifeModel
                10008, // KnifeUseCount
                10009  // KnifeMaxCount
            };

            // 工艺参数报告
            reports[StandardReports.ProcessParameters] = new List<uint>
            {
                1,     // Clock
                10020, // CuttingSpeed
                10021, // CuttingPressure
                10022, // SpindleSpeed
                10023, // CoolingFlow
                10024  // VacuumPressure
            };

            // 生产统计报告
            reports[StandardReports.ProductionStatistics] = new List<uint>
            {
                1,     // Clock
                10100, // TotalProcessed
                10101, // GoodCount
                10102, // NGCount
                10104, // UPH
                10109  // OEE
            };

            // 报警状态报告
            reports[StandardReports.AlarmStatus] = new List<uint>
            {
                1,    // Clock
                230,  // AlarmSet
                231   // AlarmClear
            };

            // 维护状态报告
            reports[StandardReports.MaintenanceStatus] = new List<uint>
            {
                1,     // Clock
                300,   // MaintenanceInterval
                10107, // Uptime
                10108, // Downtime
                10105, // MTBF
                10106  // MTTR
            };

            Logger.LogDebug($"初始化了 {reports.Count} 个预定义报告");
            return reports;
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S6F20响应
        /// </summary>
        private SecsMessage CreateS6F20Response(uint rptid, List<VariableValue> variableValues)
        {
            // 构建变量值列表
            var values = variableValues
                .Select(v => SecsItemHelper.ConvertToItem(v.Value))
                .ToList();

            // S6F20格式：
            // L,2
            //   1. U4 RPTID
            //   2. L,n (变量值列表)

            return new SecsMessage(6, 20, false)
            {
                Name = "IndividualReportData",
                SecsItem = Item.L(
                    Item.U4(rptid),
                    Item.L(values)
                )
            };
        }

        /// <summary>
        /// 创建空的S6F20响应
        /// </summary>
        private SecsMessage CreateEmptyS6F20Response()
        {
            return new SecsMessage(6, 20, false)
            {
                Name = "IndividualReportData",
                SecsItem = Item.L(
                    Item.U4(0),  // RPTID = 0 表示无效
                    Item.L()     // 空变量列表
                )
            };
        }

        /// <summary>
        /// 创建错误的S6F20响应
        /// </summary>
        private SecsMessage CreateErrorS6F20Response(uint rptid)
        {
            // 返回请求的RPTID但空数据表示未定义
            return new SecsMessage(6, 20, false)
            {
                Name = "IndividualReportData",
                SecsItem = Item.L(
                    Item.U4(rptid),
                    Item.L()  // 空列表表示报告未定义
                )
            };
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 变量值
        /// </summary>
        private class VariableValue
        {
            /// <summary>变量ID</summary>
            public uint Vid { get; set; }

            /// <summary>变量名称</summary>
            public string Name { get; set; } = "";

            /// <summary>变量值</summary>
            public object Value { get; set; } = "";
        }

        /// <summary>
        /// 缓存值
        /// </summary>
        private class CachedValue
        {
            /// <summary>变量ID</summary>
            public uint Vid { get; set; }

            /// <summary>值</summary>
            public object Value { get; set; } = "";

            /// <summary>时间戳</summary>
            public DateTime Timestamp { get; set; }
        }

        #endregion

        #region 公共方法（供其他Handler使用）

        /// <summary>
        /// 获取报告数据（供S6F23等Handler使用）
        /// </summary>
        /// <param name="rptid">报告ID</param>
        /// <returns>报告数据</returns>
        public async Task<Dictionary<uint, object>> GetReportDataAsync(uint rptid)
        {
            var result = new Dictionary<uint, object>();

            try
            {
                var variableValues = await CollectReportData(rptid, CancellationToken.None);

                foreach (var variable in variableValues)
                {
                    result[variable.Vid] = variable.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"获取报告 {rptid} 数据失败");
            }

            return result;
        }

        /// <summary>
        /// 获取预定义报告列表
        /// </summary>
        /// <returns>预定义报告ID列表</returns>
        public List<uint> GetPredefinedReports()
        {
            return _predefinedReports.Keys.OrderBy(k => k).ToList();
        }

        #endregion
    }
}
