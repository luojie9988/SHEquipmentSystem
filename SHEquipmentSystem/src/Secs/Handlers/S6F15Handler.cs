// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S6F15Handler.cs
// 版本: v1.0.0
// 描述: S6F15消息处理器 - Event Report Request 事件报告请求处理器

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
    /// S6F15 (Event Report Request) 处理器
    /// 处理主机的事件报告请求，返回指定事件的关联报告数据
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S6F15: 事件报告请求 - 主机查询特定事件的报告数据
    /// - S6F16: 事件报告数据 - 设备返回事件关联的报告内容
    /// 
    /// 交互流程：
    /// 1. 主机发送S6F15请求特定CEID的报告数据
    /// 2. 设备验证CEID的有效性
    /// 3. 获取该事件关联的报告（通过S2F35配置）
    /// 4. 收集报告中定义的变量数据（通过S2F33配置）
    /// 5. 返回S6F16包含完整的报告数据
    /// 
    /// 使用场景：
    /// - 主机主动查询最近发生的事件详情
    /// - 获取特定事件的当前状态数据
    /// - 验证事件配置是否正确
    /// - 调试和诊断事件报告机制
    /// 
    /// 划裂片设备特点：
    /// - 支持查询所有定义的事件
    /// - 即使事件未启用也可查询
    /// - 返回事件发生时的快照数据
    /// - 支持获取事件的默认报告格式
    /// </remarks>
    public class S6F15Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventService;

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>S2F35处理器（获取事件链接）</summary>
        private readonly S2F35Handler? _linkHandler;

        /// <summary>S2F33处理器（获取报告定义）</summary>
        private readonly S2F33Handler? _reportHandler;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>事件快照缓存（CEID -> 最近的数据快照）</summary>
        private readonly Dictionary<uint, EventSnapshot> _eventSnapshots;

        /// <summary>快照缓存锁</summary>
        private readonly ReaderWriterLockSlim _snapshotLock = new();

        /// <summary>最大快照保留时间（分钟）</summary>
        private const int MaxSnapshotAgeMinutes = 60;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 6;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 15;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="eventService">事件报告服务</param>
        /// <param name="statusService">状态变量服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="serviceProvider">服务提供者</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S6F15Handler(
            ILogger<S6F15Handler> logger,
            IEventReportService eventService,
            IStatusVariableService statusService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            IServiceProvider serviceProvider) : base(logger)
        {
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // 尝试获取相关处理器
            _linkHandler = serviceProvider.GetService(typeof(S2F35Handler)) as S2F35Handler;
            _reportHandler = serviceProvider.GetService(typeof(S2F33Handler)) as S2F33Handler;

            // 初始化事件快照缓存
            _eventSnapshots = new Dictionary<uint, EventSnapshot>();

            // 启动快照清理任务
            StartSnapshotCleanup();

            Logger.LogInformation("S6F15处理器已初始化");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S6F15 消息，返回 S6F16 响应
        /// </summary>
        /// <param name="message">接收到的S6F15消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S6F16响应消息</returns>
        /// <remarks>
        /// S6F15 处理逻辑：
        /// 1. 解析请求的CEID
        /// 2. 验证CEID有效性
        /// 3. 获取事件关联的报告
        /// 4. 收集报告数据
        /// 5. 构建并返回S6F16响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S6F15 (Event Report Request) 事件报告请求");

            try
            {
                // 解析CEID
                var ceid = ParseCeid(message.SecsItem);

                if (ceid == 0)
                {
                    Logger.LogWarning("S6F15消息中CEID无效");
                    return CreateEmptyS6F16Response();
                }

                Logger.LogDebug($"请求事件 {ceid} 的报告数据");

                // 验证CEID有效性
                if (!IsValidCeid(ceid))
                {
                    Logger.LogWarning($"CEID {ceid} 无效或未定义");
                    return CreateEmptyS6F16Response();
                }

                // 获取事件报告数据
                var reportData = await CollectEventReportData(ceid, cancellationToken);

                // 构建S6F16响应
                var s6f16 = CreateS6F16Response(ceid, reportData);

                Logger.LogInformation($"S6F16响应准备就绪，CEID: {ceid}, 报告数: {reportData.Count}");
                return s6f16;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S6F15消息失败");
                return CreateEmptyS6F16Response();
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析CEID（使用公共帮助类）
        /// </summary>
        private static uint ParseCeid(Item? item)
        {
            return SecsItemHelper.ParseCeid(item);
        }

        #endregion

        #region 私有方法 - 数据收集

        /// <summary>
        /// 收集事件报告数据
        /// </summary>
        private async Task<List<ReportData>> CollectEventReportData(
            uint ceid,
            CancellationToken cancellationToken)
        {
            var reportDataList = new List<ReportData>();

            try
            {
                // 获取事件链接的报告
                var linkedReports = GetLinkedReports(ceid);

                if (linkedReports.Count == 0)
                {
                    Logger.LogDebug($"事件 {ceid} 没有链接的报告，返回默认报告");
                    // 返回默认报告
                    linkedReports.Add(GetDefaultReportId(ceid));
                }

                // 检查是否有缓存的快照
                var snapshot = GetEventSnapshot(ceid);

                // 收集每个报告的数据
                foreach (var rptid in linkedReports)
                {
                    var reportData = await CollectReportData(
                        rptid,
                        ceid,
                        snapshot,
                        cancellationToken);

                    reportDataList.Add(reportData);

                    Logger.LogTrace($"已收集报告 {rptid} 的数据");
                }

                // 更新快照
                if (snapshot == null && reportDataList.Any())
                {
                    UpdateEventSnapshot(ceid, reportDataList);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"收集事件 {ceid} 报告数据失败");
            }

            return reportDataList;
        }

        /// <summary>
        /// 收集单个报告数据
        /// </summary>
        private async Task<ReportData> CollectReportData(
            uint rptid,
            uint ceid,
            EventSnapshot? snapshot,
            CancellationToken cancellationToken)
        {
            var reportData = new ReportData
            {
                RptId = rptid,
                Variables = new List<VariableData>()
            };

            try
            {
                // 获取报告定义的变量列表
                var vidList = GetReportVariables(rptid);

                if (vidList.Count == 0)
                {
                    Logger.LogTrace($"报告 {rptid} 没有定义变量");
                    return reportData;
                }

                // 收集每个变量的值
                foreach (var vid in vidList)
                {
                    object value;

                    // 优先从快照获取
                    if (snapshot != null && snapshot.TryGetValue(vid, out var snapshotValue))
                    {
                        value = snapshotValue;
                    }
                    else
                    {
                        // 从实时数据获取
                        value = await GetVariableValue(vid, ceid, cancellationToken);
                    }

                    reportData.Variables.Add(new VariableData
                    {
                        Vid = vid,
                        Value = value
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"收集报告 {rptid} 数据时发生错误");
            }

            return reportData;
        }

        /// <summary>
        /// 获取变量值
        /// </summary>
        private async Task<object> GetVariableValue(
            uint vid,
            uint ceid,
            CancellationToken cancellationToken)
        {
            try
            {
                // 特殊处理某些与事件相关的变量
                var specialValue = GetEventSpecificValue(vid, ceid);
                if (specialValue != null)
                {
                    return specialValue;
                }

                // 从状态变量服务获取
                return await _statusService.GetSvidValueAsync(vid);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"获取变量 {vid} 值失败");
                return GetDefaultValue(vid);
            }
        }

        /// <summary>
        /// 获取事件特定的变量值
        /// </summary>
        private object? GetEventSpecificValue(uint vid, uint ceid)
        {
            // 根据VID返回特定值
            switch (vid)
            {
                case 1: // Clock
                    return DateTime.Now.ToString("yyyyMMddHHmmss");

                case 2: // EventID
                    return ceid;

                case 3: // EventName
                    return GetEventName(ceid);

                default:
                    return null;
            }
        }

        /// <summary>
        /// 获取事件名称
        /// </summary>
        private string GetEventName(uint ceid)
        {
            return ceid switch
            {
                200 => "ControlStateOffline",
                201 => "ControlStateLocal",
                202 => "ControlStateRemote",
                210 => "ProcessStart",
                211 => "ProcessComplete",
                212 => "ProcessAbort",
                220 => "OperatorEquipmentConstantChange",
                230 => "AlarmSet",
                231 => "AlarmClear",
                11000 => "MaterialArrival",
                11001 => "MaterialRemoved",
                11002 => "MapComplete",
                11003 => "PPSelected",
                11100 => "KnifeChange",
                11101 => "KnifeLifeWarning",
                _ => $"Event_{ceid}"
            };
        }

        /// <summary>
        /// 获取变量默认值
        /// </summary>
        private object GetDefaultValue(uint vid)
        {
            // 根据VID返回适当的默认值
            return vid switch
            {
                1 => DateTime.Now.ToString("yyyyMMddHHmmss"),
                < 100 => 0u,
                < 1000 => "",
                _ => 0
            };
        }

        #endregion

        #region 私有方法 - 事件快照管理

        /// <summary>
        /// 获取事件快照
        /// </summary>
        private EventSnapshot? GetEventSnapshot(uint ceid)
        {
            _snapshotLock.EnterReadLock();
            try
            {
                if (_eventSnapshots.TryGetValue(ceid, out var snapshot))
                {
                    // 检查快照是否过期
                    if ((DateTime.Now - snapshot.Timestamp).TotalMinutes <= MaxSnapshotAgeMinutes)
                    {
                        return snapshot;
                    }
                }
                return null;
            }
            finally
            {
                _snapshotLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 更新事件快照
        /// </summary>
        private void UpdateEventSnapshot(uint ceid, List<ReportData> reportDataList)
        {
            var snapshot = new EventSnapshot
            {
                Ceid = ceid,
                Timestamp = DateTime.Now,
                Data = new Dictionary<uint, object>()
            };

            // 收集所有变量数据
            foreach (var report in reportDataList)
            {
                foreach (var variable in report.Variables)
                {
                    snapshot.Data[variable.Vid] = variable.Value;
                }
            }

            _snapshotLock.EnterWriteLock();
            try
            {
                _eventSnapshots[ceid] = snapshot;
                Logger.LogTrace($"已更新事件 {ceid} 的快照");
            }
            finally
            {
                _snapshotLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 启动快照清理任务
        /// </summary>
        private void StartSnapshotCleanup()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));
                    CleanupOldSnapshots();
                }
            });
        }

        /// <summary>
        /// 清理过期快照
        /// </summary>
        private void CleanupOldSnapshots()
        {
            _snapshotLock.EnterWriteLock();
            try
            {
                var expiredCeids = _eventSnapshots
                    .Where(kvp => (DateTime.Now - kvp.Value.Timestamp).TotalMinutes > MaxSnapshotAgeMinutes)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var ceid in expiredCeids)
                {
                    _eventSnapshots.Remove(ceid);
                }

                if (expiredCeids.Any())
                {
                    Logger.LogDebug($"清理了 {expiredCeids.Count} 个过期的事件快照");
                }
            }
            finally
            {
                _snapshotLock.ExitWriteLock();
            }
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 验证CEID有效性
        /// </summary>
        private bool IsValidCeid(uint ceid)
        {
            // 验证CEID是否在支持的范围内
            return ceid switch
            {
                >= 200 and <= 249 => true,      // 系统事件
                >= 11000 and <= 11199 => true,  // 材料和工艺事件
                _ => false
            };
        }

        /// <summary>
        /// 获取事件链接的报告
        /// </summary>
        private List<uint> GetLinkedReports(uint ceid)
        {
            // 从S2F35Handler获取
            if (_linkHandler != null)
            {
                return _linkHandler.GetLinkedReports(ceid);
            }

            // 返回默认报告
            return new List<uint> { GetDefaultReportId(ceid) };
        }

        /// <summary>
        /// 获取报告定义的变量
        /// </summary>
        private List<uint> GetReportVariables(uint rptid)
        {
            // 从S2F33Handler获取
            if (_reportHandler != null)
            {
                return _reportHandler.GetReportVariables(rptid);
            }

            // 返回默认变量列表
            return GetDefaultVariables(rptid);
        }

        /// <summary>
        /// 获取默认报告ID
        /// </summary>
        private uint GetDefaultReportId(uint ceid)
        {
            return ceid switch
            {
                >= 200 and <= 209 => 1,    // 控制状态事件 -> 报告1
                >= 210 and <= 219 => 2,    // 处理状态事件 -> 报告2
                >= 220 and <= 229 => 3,    // 操作员事件 -> 报告3
                >= 230 and <= 239 => 4,    // 报警事件 -> 报告4
                >= 11000 and <= 11099 => 10, // 材料事件 -> 报告10
                >= 11100 and <= 11199 => 11, // 划裂片特定事件 -> 报告11
                _ => 1
            };
        }

        /// <summary>
        /// 获取默认变量列表
        /// </summary>
        private List<uint> GetDefaultVariables(uint rptid)
        {
            return rptid switch
            {
                1 => new List<uint> { 1, 721, 722 },      // Clock, ControlState, ProcessState
                2 => new List<uint> { 1, 210, 211 },      // Clock, ProcessStart, ProcessComplete
                3 => new List<uint> { 1, 220 },           // Clock, OperatorAction
                4 => new List<uint> { 1, 230, 231 },      // Clock, AlarmID, AlarmText
                10 => new List<uint> { 1, 10011, 10012 }, // Clock, CurrentSlot, CassetteID
                11 => new List<uint> { 1, 10007, 10008 }, // Clock, KnifeModel, KnifeUseCount
                _ => new List<uint> { 1 }                 // Clock
            };
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S6F16响应
        /// </summary>
        private SecsMessage CreateS6F16Response(uint ceid, List<ReportData> reportDataList)
        {
            // 构建报告项列表
            var reports = new List<Item>();

            foreach (var reportData in reportDataList)
            {
                var variables = reportData.Variables
                    .Select(v => SecsItemHelper.ConvertToItem(v.Value))
                    .ToList();

                reports.Add(Item.L(
                    Item.U4(reportData.RptId),
                    Item.L(variables)
                ));
            }

            // S6F16格式：
            // L,3
            //   1. U4 DATAID
            //   2. U4 CEID
            //   3. L,n (报告列表)
            //      L,2
            //        1. U4 RPTID
            //        2. L,m (变量值列表)

            return new SecsMessage(6, 16, false)
            {
                Name = "EventReportData",
                SecsItem = Item.L(
                    Item.U4(0),           // DATAID
                    Item.U4(ceid),        // CEID
                    Item.L(reports)       // Reports
                )
            };
        }

        /// <summary>
        /// 创建空的S6F16响应
        /// </summary>
        private SecsMessage CreateEmptyS6F16Response()
        {
            return new SecsMessage(6, 16, false)
            {
                Name = "EventReportData",
                SecsItem = Item.L(
                    Item.U4(0),     // DATAID
                    Item.U4(0),     // CEID = 0 表示无效
                    Item.L()        // 空报告列表
                )
            };
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 事件快照
        /// </summary>
        private class EventSnapshot
        {
            /// <summary>事件ID</summary>
            public uint Ceid { get; set; }

            /// <summary>时间戳</summary>
            public DateTime Timestamp { get; set; }

            /// <summary>数据（VID -> Value）</summary>
            public Dictionary<uint, object> Data { get; set; } = new();

            /// <summary>尝试获取值</summary>
            public bool TryGetValue(uint vid, out object value)
            {
                if (Data.TryGetValue(vid, out var val))
                {
                    value = val;
                    return true;
                }
                value = null!;
                return false;
            }
        }

        /// <summary>
        /// 报告数据
        /// </summary>
        private class ReportData
        {
            /// <summary>报告ID</summary>
            public uint RptId { get; set; }

            /// <summary>变量列表</summary>
            public List<VariableData> Variables { get; set; } = new();
        }

        /// <summary>
        /// 变量数据
        /// </summary>
        private class VariableData
        {
            /// <summary>变量ID</summary>
            public uint Vid { get; set; }

            /// <summary>变量值</summary>
            public object Value { get; set; } = "";
        }

        #endregion

        #region 公共方法（供S6F11Handler使用）

        /// <summary>
        /// 保存事件快照（供S6F11Handler调用）
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="data">事件数据</param>
        public void SaveEventSnapshot(uint ceid, Dictionary<uint, object> data)
        {
            var snapshot = new EventSnapshot
            {
                Ceid = ceid,
                Timestamp = DateTime.Now,
                Data = new Dictionary<uint, object>(data)
            };

            _snapshotLock.EnterWriteLock();
            try
            {
                _eventSnapshots[ceid] = snapshot;
                Logger.LogDebug($"保存了事件 {ceid} 的快照");
            }
            finally
            {
                _snapshotLock.ExitWriteLock();
            }
        }

        #endregion
    }
}
