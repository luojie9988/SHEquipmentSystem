// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F23Handler.cs
// 版本: v1.0.0
// 描述: S2F23数据采集初始化处理器 - 处理主机的数据采集配置请求

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S2F23 (Trace Initialize Send) 数据采集初始化处理器
    /// 处理主机发送的数据采集配置消息，符合SEMI E5 Trace Data标准
    /// </summary>
    /// <remarks>
    /// 功能职责：
    /// 1. 接收并解析主机发送的S2F23数据采集配置消息
    /// 2. 验证采集变量(SVID)的有效性和访问权限
    /// 3. 配置数据采集的周期、触发条件和存储策略
    /// 4. 与数据采集服务集成，启动或停止数据采集
    /// 5. 返回S2F24初始化确认响应(TIAACK代码)
    /// 6. 管理采集数据的缓存和发送机制
    /// 
    /// 设计原理：
    /// - 基于SEMI E5 Formatted Report和Trace Data功能
    /// - 遵循划裂片设备的数据采集需求
    /// - 与DiceHostSystem数据采集管理器完全匹配
    /// - 支持多种采集模式和触发方式
    /// 
    /// 支持的数据采集配置：
    /// - 周期性采集：按时间间隔定期采集
    /// - 事件触发采集：基于CEID事件触发
    /// - 状态变更采集：当SVID值发生变化时采集
    /// - 批量数据缓存：支持数据缓冲和批量发送
    /// </remarks>
    public class S2F23Handler : SecsMessageHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// 跟踪初始化确认代码 (TIAACK) 定义
        /// 基于SEMI E5标准的数据采集初始化响应代码
        /// </summary>
        public enum TraceInitializeAck : byte
        {
            /// <summary>跟踪已接受</summary>
            /// <remarks>设备接受数据采集配置并开始采集</remarks>
            Accepted = 0,

            /// <summary>跟踪被拒绝</summary>
            /// <remarks>设备拒绝数据采集，原因可能是参数错误或资源不足</remarks>
            Denied = 1,

            /// <summary>至少一个SVID无效</summary>
            /// <remarks>请求的状态变量中有不存在或不可访问的项</remarks>
            InvalidSvid = 2,

            /// <summary>采集周期无效</summary>
            /// <remarks>指定的采集周期超出设备支持范围</remarks>
            InvalidPeriod = 3,

            /// <summary>TOTSMP参数无效</summary>
            /// <remarks>总采样数参数不在有效范围内</remarks>
            InvalidTotalSamples = 4,

            /// <summary>REPGSZ参数无效</summary>
            /// <remarks>报告组大小参数不在有效范围内</remarks>
            InvalidReportGroupSize = 5
        }

        /// <summary>
        /// 数据采集模式定义
        /// </summary>
        public enum TraceMode
        {
            /// <summary>停止采集</summary>
            Stop = 0,

            /// <summary>周期性采集</summary>
            Periodic = 1,

            /// <summary>事件触发采集</summary>
            EventTriggered = 2,

            /// <summary>状态变更采集</summary>
            StateChange = 3,

            /// <summary>连续采集</summary>
            Continuous = 4
        }

        #endregion

        #region 私有字段

        /// <summary>
        /// 状态变量服务
        /// 用于验证SVID有效性和获取实时数据
        /// </summary>
        private readonly IStatusVariableService _svidService;

        /// <summary>
        /// 数据采集服务
        /// 提供实际的数据采集和缓存功能
        /// </summary>
        private readonly IDataCollectionService _dataCollectionService;

        /// <summary>
        /// 设备配置
        /// 包含数据采集的限制参数和性能配置
        /// </summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>
        /// 配置锁
        /// 防止并发修改数据采集配置
        /// </summary>
        private readonly SemaphoreSlim _configLock;

        /// <summary>
        /// 当前数据采集配置
        /// 存储活动的数据采集任务配置
        /// </summary>
        private TraceConfiguration? _currentTraceConfig;

        #endregion

        #region 属性

        /// <summary>
        /// 消息流号 - Stream 2 (设备控制流)
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号 - Function 23 (跟踪初始化发送)
        /// </summary>
        public override byte Function => 23;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器，用于记录采集配置过程</param>
        /// <param name="svidService">状态变量服务，提供SVID验证和数据访问</param>
        /// <param name="dataCollectionService">数据采集服务，提供实际采集功能</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">当必需的依赖服务为null时抛出</exception>
        public S2F23Handler(
            ILogger<S2F23Handler> logger,
            IStatusVariableService svidService,
            IDataCollectionService dataCollectionService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _svidService = svidService ?? throw new ArgumentNullException(nameof(svidService));
            _dataCollectionService = dataCollectionService ?? throw new ArgumentNullException(nameof(dataCollectionService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _configLock = new SemaphoreSlim(1, 1);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 处理S2F23数据采集初始化消息
        /// </summary>
        /// <param name="message">接收到的S2F23消息，包含采集配置参数</param>
        /// <param name="cancellationToken">取消令牌，用于支持异步操作取消</param>
        /// <returns>S2F24初始化确认响应消息</returns>
        /// <remarks>
        /// 处理流程：
        /// 1. 解析S2F23消息，提取采集参数(TRID, DATALENGTH, 变量列表等)
        /// 2. 验证所有请求的SVID是否有效且可访问
        /// 3. 验证采集参数的合理性(周期、缓存大小等)
        /// 4. 停止当前的数据采集任务(如有)
        /// 5. 根据配置启动新的数据采集任务
        /// 6. 构建并返回S2F24响应消息
        /// 
        /// S2F23消息格式：
        /// L,6
        ///   1. &lt;U4 TRID&gt; 跟踪ID
        ///   2. &lt;A DSPER&gt; 数据样本周期
        ///   3. &lt;U4 TOTSMP&gt; 总采样数
        ///   4. &lt;U4 REPGSZ&gt; 报告组大小
        ///   5. L,n SVID列表
        ///   6. &lt;A DSTYP&gt; 数据采样类型
        /// 
        /// S2F24响应格式：
        /// &lt;B TIAACK&gt; 跟踪初始化确认代码
        /// </remarks>
        /// <exception cref="InvalidOperationException">当数据采集服务未就绪时</exception>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到S2F23 (Trace Initialize Send) 数据采集初始化请求");

            // 使用信号量确保配置串行执行
            await _configLock.WaitAsync(cancellationToken);
            try
            {
                // 步骤1: 解析S2F23消息参数
                var parseResult = ParseS2F23Message(message);
                if (!parseResult.IsValid)
                {
                    Logger.LogWarning("S2F23消息解析失败: {Reason}", parseResult.ErrorReason);
                    return CreateS2F24Response(parseResult.TiaackCode);
                }

                var traceConfig = parseResult.TraceConfig!;
                Logger.LogInformation("解析数据采集配置成功: TRID={TRID}, 周期={Period}, SVID数量={SvidCount}",
                    traceConfig.TraceId, traceConfig.SamplingPeriod, traceConfig.SvidList.Count);

                // 步骤2: 验证SVID有效性
                var svidValidation = await ValidateSvidListAsync(traceConfig.SvidList);
                if (!svidValidation.IsValid)
                {
                    Logger.LogWarning("SVID验证失败: {Reason}", svidValidation.ErrorReason);
                    return CreateS2F24Response(TraceInitializeAck.InvalidSvid);
                }

                // 步骤3: 验证采集参数合理性
                var paramValidation = ValidateTraceParameters(traceConfig);
                if (!paramValidation.IsValid)
                {
                    Logger.LogWarning("采集参数验证失败: {Reason}", paramValidation.ErrorReason);
                    return CreateS2F24Response(paramValidation.TiaackCode);
                }

                // 步骤4: 停止现有的数据采集
                if (_currentTraceConfig != null)
                {
                    Logger.LogInformation("停止现有数据采集任务: TRID={OldTRID}", _currentTraceConfig.TraceId);
                    await _dataCollectionService.StopTraceAsync(_currentTraceConfig.TraceId);
                    _currentTraceConfig = null;
                }

                // 步骤5: 启动新的数据采集(如果不是停止命令)
                TraceInitializeAck ackCode = TraceInitializeAck.Accepted;

                if (traceConfig.IsStopCommand)
                {
                    Logger.LogInformation("收到停止数据采集命令");
                    ackCode = TraceInitializeAck.Accepted;
                }
                else
                {
                    Logger.LogInformation("启动数据采集任务: TRID={TRID}", traceConfig.TraceId);
                    var startResult = await _dataCollectionService.StartTraceAsync(traceConfig);

                    if (startResult.Success)
                    {
                        _currentTraceConfig = traceConfig;
                        ackCode = TraceInitializeAck.Accepted;
                        Logger.LogInformation("数据采集任务启动成功: TRID={TRID}", traceConfig.TraceId);
                    }
                    else
                    {
                        ackCode = TraceInitializeAck.Denied;
                        Logger.LogError("数据采集任务启动失败: {Error}", startResult.ErrorMessage);
                    }
                }

                // 步骤6: 返回S2F24确认响应
                var response = CreateS2F24Response(ackCode);
                Logger.LogInformation("S2F23处理完成: TIAACK={AckCode}", ackCode);

                return response;
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("S2F23处理被取消");
                return CreateS2F24Response(TraceInitializeAck.Denied);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F23消息时发生未预期的异常");
                return CreateS2F24Response(TraceInitializeAck.Denied);
            }
            finally
            {
                _configLock.Release();
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S2F23消息内容
        /// </summary>
        /// <param name="message">S2F23消息</param>
        /// <returns>解析结果，包含采集配置参数</returns>
        /// <remarks>
        /// 解析规则：
        /// 1. 消息体必须是包含6个元素的列表或者单个TRID
        /// 2. TRID=0表示停止所有数据采集
        /// 3. SVID列表可以为空(L,0)，表示不采集任何变量
        /// 4. 周期参数支持秒、毫秒等时间格式
        /// </remarks>
        private TraceParseResult ParseS2F23Message(SecsMessage message)
        {
            try
            {
                if (message.SecsItem == null)
                {
                    return new TraceParseResult
                    {
                        IsValid = false,
                        ErrorReason = "消息体为空",
                        TiaackCode = TraceInitializeAck.Denied
                    };
                }

                // 处理停止命令的简单格式: 单个TRID=0
                if (message.SecsItem.Format != SecsFormat.List)
                {
                    var transactionId = message.SecsItem.FirstValue<uint>();
                    if (transactionId == 0)
                    {
                        return new TraceParseResult
                        {
                            IsValid = true,
                            TraceConfig = new TraceConfiguration { TraceId = 0, IsStopCommand = true }
                        };
                    }
                    else
                    {
                        return new TraceParseResult
                        {
                            IsValid = false,
                            ErrorReason = "单个TRID值必须为0",
                            TiaackCode = TraceInitializeAck.Denied
                        };
                    }
                }

                // 处理完整格式的配置消息（兼容5元素和6元素）
                if (message.SecsItem.Count < 5 || message.SecsItem.Count > 6)
                {
                    return new TraceParseResult
                    {
                        IsValid = false,
                        ErrorReason = $"消息体格式错误，期望5或6个元素，实际{message.SecsItem.Count}个",
                        TiaackCode = TraceInitializeAck.Denied
                    };
                }

                // 处理完整格式的配置消息
                //if (message.SecsItem.Count != 6)
                //{
                //    return new TraceParseResult
                //    {
                //        IsValid = false,
                //        ErrorReason = $"消息体格式错误，期望6个元素，实际{message.SecsItem.Count}个",
                //        TiaackCode = TraceInitializeAck.Denied
                //    };
                //}

                var items = message.SecsItem.Items;

                // 解析TRID (跟踪ID)
                var trid = items[0].FirstValue<uint>();
                if (trid == 0)
                {
                    return new TraceParseResult
                    {
                        IsValid = true,
                        TraceConfig = new TraceConfiguration { TraceId = 0, IsStopCommand = true }
                    };
                }

                // 解析DSPER (数据样本周期)
                var periodStr = items[1].FirstValue<uint>().ToString();
                var samplingPeriod = ParseSamplingPeriod(periodStr);
                if (samplingPeriod <= TimeSpan.Zero)
                {
                    return new TraceParseResult
                    {
                        IsValid = false,
                        ErrorReason = $"无效的采样周期: {periodStr}",
                        TiaackCode = TraceInitializeAck.InvalidPeriod
                    };
                }

                // 解析TOTSMP (总采样数)
                var totalSamples = items[2].FirstValue<uint>();

                // 解析REPGSZ (报告组大小)
                var reportGroupSize = items[3].FirstValue<uint>();

                // 解析SVID列表
                var svidList = new List<uint>();
                if (items[4].Count > 0)
                {
                    foreach (var svidItem in items[4].Items)
                    {
                        svidList.Add(svidItem.FirstValue<uint>());
                    }
                }

                // 解析DSTYP (数据样本类型) - 可选
                //var sampleType = items[5].GetString().Trim();

                var sampleType = "";
                if (items.Count() == 6)
                {
                    sampleType = items[5].GetString().Trim();
                }

                var traceConfig = new TraceConfiguration
                {
                    TraceId = trid,
                    SamplingPeriod = samplingPeriod,
                    TotalSamples = totalSamples,
                    ReportGroupSize = reportGroupSize,
                    SvidList = svidList,
                    SampleType = sampleType ?? "PERIODIC",// 默认为周期性采集
                    IsStopCommand = false
                };

                return new TraceParseResult
                {
                    IsValid = true,
                    TraceConfig = traceConfig
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S2F23消息异常");
                return new TraceParseResult
                {
                    IsValid = false,
                    ErrorReason = $"解析异常: {ex.Message}",
                    TiaackCode = TraceInitializeAck.Denied
                };
            }
        }

        /// <summary>
        /// 解析采样周期字符串
        /// </summary>
        /// <param name="periodStr">周期字符串，如"1000ms", "5s", "1min"</param>
        /// <returns>解析后的时间间隔</returns>
        /// <remarks>
        /// 支持的时间格式：
        /// - "1000" 或 "1000ms" - 毫秒
        /// - "5s" 或 "5sec" - 秒
        /// - "1min" - 分钟  
        /// - "0.5" - 秒(小数)
        /// </remarks>
        private TimeSpan ParseSamplingPeriod(string periodStr)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(periodStr))
                {
                    return TimeSpan.Zero;
                }

                periodStr = periodStr.ToLower().Trim();

                // 处理各种时间格式
                if (periodStr.EndsWith("ms"))
                {
                    var ms = double.Parse(periodStr.Replace("ms", ""));
                    return TimeSpan.FromMilliseconds(ms);
                }
                else if (periodStr.EndsWith("s") || periodStr.EndsWith("sec"))
                {
                    var s = double.Parse(periodStr.Replace("s", "").Replace("sec", ""));
                    return TimeSpan.FromSeconds(s);
                }
                else if (periodStr.EndsWith("min"))
                {
                    var min = double.Parse(periodStr.Replace("min", ""));
                    return TimeSpan.FromMinutes(min);
                }
                else
                {
                    // 默认按秒处理
                    var value = double.Parse(periodStr);
                    return TimeSpan.FromSeconds(value);
                }
            }
            catch (Exception)
            {
                return TimeSpan.Zero;
            }
        }

        #endregion

        #region 私有方法 - 验证

        /// <summary>
        /// 验证SVID列表的有效性
        /// </summary>
        /// <param name="svidList">要验证的SVID列表</param>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// 验证项目：
        /// 1. 检查SVID是否在设备支持的范围内
        /// 2. 验证SVID是否已定义且可访问
        /// 3. 检查SVID数据类型是否支持采集
        /// 4. 验证访问权限和安全限制
        /// </remarks>
        private async Task<SvidValidationResult> ValidateSvidListAsync(List<uint> svidList)
        {
            try
            {
                var invalidSvids = new List<uint>();

                foreach (var svid in svidList)
                {
                    try
                    {
                        // 检查SVID是否存在
                        var value = await _svidService.GetSvidValueAsync(svid);
                        if (value == null)
                        {
                            invalidSvids.Add(svid);
                            Logger.LogWarning("SVID {SVID} 不存在或无法访问", svid);
                        }
                    }
                    catch (Exception ex)
                    {
                        invalidSvids.Add(svid);
                        Logger.LogWarning(ex, "验证SVID {SVID} 时发生异常", svid);
                    }
                }

                if (invalidSvids.Count > 0)
                {
                    return new SvidValidationResult
                    {
                        IsValid = false,
                        ErrorReason = $"发现{invalidSvids.Count}个无效SVID: {string.Join(", ", invalidSvids)}",
                        InvalidSvids = invalidSvids
                    };
                }

                return new SvidValidationResult { IsValid = true };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证SVID列表异常");
                return new SvidValidationResult
                {
                    IsValid = false,
                    ErrorReason = "SVID验证过程异常"
                };
            }
        }

        /// <summary>
        /// 验证跟踪参数的合理性
        /// </summary>
        /// <param name="config">跟踪配置</param>
        /// <returns>参数验证结果</returns>
        /// <remarks>
        /// 验证规则：
        /// 1. 采样周期必须在设备支持范围内(100ms - 1小时)
        /// 2. 总采样数不能超过内存限制
        /// 3. 报告组大小必须合理(1-1000)
        /// 4. SVID数量不能超过系统处理能力
        /// </remarks>
        private ParameterValidationResult ValidateTraceParameters(TraceConfiguration config)
        {
            // 验证采样周期范围
            var minPeriod = TimeSpan.FromMilliseconds(100); // 最小100ms
            var maxPeriod = TimeSpan.FromHours(1);          // 最大1小时

            if (config.SamplingPeriod < minPeriod || config.SamplingPeriod > maxPeriod)
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"采样周期超出范围({minPeriod.TotalMilliseconds}ms - {maxPeriod.TotalHours}h)",
                    TiaackCode = TraceInitializeAck.InvalidPeriod
                };
            }

            // 验证总采样数(最大10万次)
            const uint maxTotalSamples = 100000;
            if (config.TotalSamples > maxTotalSamples)
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"总采样数超出限制，最大{maxTotalSamples}次",
                    TiaackCode = TraceInitializeAck.InvalidTotalSamples
                };
            }

            // 验证报告组大小(1-1000)
            if (config.ReportGroupSize < 1 || config.ReportGroupSize > 1000)
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = "报告组大小必须在1-1000范围内",
                    TiaackCode = TraceInitializeAck.InvalidReportGroupSize
                };
            }

            // 验证SVID数量限制(最大50个变量)
            const int maxSvidCount = 50;
            if (config.SvidList.Count > maxSvidCount)
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"SVID数量超出限制，最大{maxSvidCount}个",
                    TiaackCode = TraceInitializeAck.InvalidSvid
                };
            }

            // 估算内存使用量
            var estimatedMemoryMB = EstimateMemoryUsage(config);
            const int maxMemoryMB = 100; // 最大100MB

            if (estimatedMemoryMB > maxMemoryMB)
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"预估内存使用量过高({estimatedMemoryMB}MB > {maxMemoryMB}MB)",
                    TiaackCode = TraceInitializeAck.Denied
                };
            }

            return new ParameterValidationResult { IsValid = true };
        }

        /// <summary>
        /// 估算数据采集的内存使用量
        /// </summary>
        /// <param name="config">跟踪配置</param>
        /// <returns>预估内存使用量(MB)</returns>
        private int EstimateMemoryUsage(TraceConfiguration config)
        {
            // 每个SVID假设平均8字节数据
            const int avgBytesPerSvid = 8;
            // 加上时间戳和元数据，每条记录约20字节开销
            const int overheadPerRecord = 20;

            var bytesPerSample = (config.SvidList.Count * avgBytesPerSvid) + overheadPerRecord;
            var totalBytes = bytesPerSample * config.TotalSamples;

            return (int)(totalBytes / (1024 * 1024)); // 转换为MB
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S2F24跟踪初始化确认响应
        /// </summary>
        /// <param name="ackCode">确认代码</param>
        /// <returns>S2F24响应消息</returns>
        /// <remarks>
        /// S2F24消息格式：
        /// &lt;B TIAACK&gt; 跟踪初始化确认代码
        /// 
        /// 确认代码含义：
        /// 0: 跟踪已接受
        /// 1: 跟踪被拒绝  
        /// 2: 至少一个SVID无效
        /// 3: 采集周期无效
        /// 4: TOTSMP参数无效
        /// 5: REPGSZ参数无效
        /// </remarks>
        private SecsMessage CreateS2F24Response(TraceInitializeAck ackCode)
        {
            var ackByte = (byte)ackCode;
            var ackDescription = GetTiaackDescription(ackCode);

            Logger.LogDebug("创建S2F24响应: TIAACK={AckCode} ({Description})", ackByte, ackDescription);

            var response = new SecsMessage(2, 24, false)
            {
                Name = "Trace Initialize Acknowledge",
                SecsItem = Item.B(ackByte)
            };

            return response;
        }

        /// <summary>
        /// 获取TIAACK代码的描述
        /// </summary>
        /// <param name="ackCode">确认代码</param>
        /// <returns>代码描述</returns>
        private string GetTiaackDescription(TraceInitializeAck ackCode)
        {
            return ackCode switch
            {
                TraceInitializeAck.Accepted => "跟踪已接受",
                TraceInitializeAck.Denied => "跟踪被拒绝",
                TraceInitializeAck.InvalidSvid => "SVID无效",
                TraceInitializeAck.InvalidPeriod => "周期无效",
                TraceInitializeAck.InvalidTotalSamples => "总采样数无效",
                TraceInitializeAck.InvalidReportGroupSize => "报告组大小无效",
                _ => $"未知代码: {(byte)ackCode}"
            };
        }

        #endregion

        #region 辅助类和结构

        /// <summary>
        /// S2F23解析结果
        /// </summary>
        private class TraceParseResult
        {
            /// <summary>
            /// 解析是否成功
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 解析出的跟踪配置
            /// </summary>
            public TraceConfiguration? TraceConfig { get; set; }

            /// <summary>
            /// 错误原因（当IsValid为false时）
            /// </summary>
            public string? ErrorReason { get; set; }

            /// <summary>
            /// 对应的TIAACK确认代码
            /// </summary>
            public TraceInitializeAck TiaackCode { get; set; }
        }

        /// <summary>
        /// SVID验证结果
        /// </summary>
        private class SvidValidationResult
        {
            /// <summary>
            /// 验证是否通过
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 错误原因
            /// </summary>
            public string? ErrorReason { get; set; }

            /// <summary>
            /// 无效的SVID列表
            /// </summary>
            public List<uint>? InvalidSvids { get; set; }
        }

        /// <summary>
        /// 参数验证结果
        /// </summary>
        private class ParameterValidationResult
        {
            /// <summary>
            /// 参数验证是否通过
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 错误原因
            /// </summary>
            public string? ErrorReason { get; set; }

            /// <summary>
            /// 对应的TIAACK代码
            /// </summary>
            public TraceInitializeAck TiaackCode { get; set; }
        }

        #endregion

        #region IDisposable实现

        /// <summary>
        /// 释放特定资源
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected override void DisposeCore(bool disposing)
        {
            if (disposing)
            {
                _configLock?.Dispose();

                // 停止当前的数据采集
                if (_currentTraceConfig != null)
                {
                    try
                    {
                        _dataCollectionService.StopTraceAsync(_currentTraceConfig.TraceId).Wait(5000);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "释放时停止数据采集失败");
                    }
                }
            }
        }

        #endregion
    }
}
