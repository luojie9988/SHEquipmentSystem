// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F33Handler.cs
// 版本: v1.0.0
// 描述: S2F33事件报告定义处理器 - 处理主机的事件报告配置请求

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S2F33 (Define Report) 事件报告定义处理器
    /// 处理主机发送的事件报告配置消息，符合SEMI E5 Event Report标准
    /// </summary>
    /// <remarks>
    /// 功能职责：
    /// 1. 接收并解析主机发送的S2F33报告定义消息
    /// 2. 验证报告ID(RPTID)的唯一性和有效性
    /// 3. 验证报告中包含的变量ID(VID)是否支持
    /// 4. 配置事件报告的数据结构和发送格式
    /// 5. 返回S2F34报告定义确认响应(DRACK代码)
    /// 6. 与事件报告服务集成，管理报告定义生命周期
    /// 
    /// 设计原理：
    /// - 基于SEMI E5 Collection Event和Event Report功能
    /// - 遵循划裂片设备的事件报告规范(14个CEID事件)
    /// - 与DiceHostSystem事件报告管理器完全匹配
    /// - 支持动态报告配置和管理
    /// 
    /// 支持的报告配置功能：
    /// - 创建新的报告定义：指定RPTID和VID列表
    /// - 修改现有报告：更新VID列表或报告参数
    /// - 删除特定报告：设置VID列表为空
    /// - 删除所有报告：发送空的报告列表
    /// - 查询报告状态：验证报告定义是否生效
    /// </remarks>
    public class S2F33Handler : SecsMessageHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// 报告定义确认代码 (DRACK) 定义
        /// 基于SEMI E5标准的报告定义响应代码
        /// </summary>
        public enum DefineReportAck : byte
        {
            /// <summary>已接受</summary>
            /// <remarks>报告定义已接受并生效</remarks>
            Accepted = 0,

            /// <summary>被拒绝</summary>
            /// <remarks>设备拒绝报告定义，原因可能是资源不足或配置冲突</remarks>
            Denied = 1,

            /// <summary>至少一个VID无效</summary>
            /// <remarks>报告中包含不存在或不可访问的变量ID</remarks>
            InvalidVariableId = 2,

            /// <summary>至少一个RPTID无效</summary>
            /// <remarks>报告ID超出有效范围或格式错误</remarks>
            InvalidReportId = 3,

            /// <summary>报告定义过多</summary>
            /// <remarks>超出设备支持的最大报告数量限制</remarks>
            TooManyReports = 4
        }

        /// <summary>
        /// 划裂片设备支持的标准报告ID范围
        /// 基于设备规格定义的报告ID分配策略
        /// </summary>
        public static class DicerReportIds
        {
            /// <summary>系统状态报告ID范围 (1000-1099)</summary>
            public const uint SystemStatusMin = 1000;
            public const uint SystemStatusMax = 1099;

            /// <summary>工艺数据报告ID范围 (2000-2099)</summary>
            public const uint ProcessDataMin = 2000;
            public const uint ProcessDataMax = 2099;

            /// <summary>设备状态报告ID范围 (3000-3099)</summary>
            public const uint EquipmentStatusMin = 3000;
            public const uint EquipmentStatusMax = 3099;

            /// <summary>报警和异常报告ID范围 (4000-4099)</summary>
            public const uint AlarmStatusMin = 4000;
            public const uint AlarmStatusMax = 4099;

            /// <summary>自定义报告ID范围 (5000-5999)</summary>
            public const uint CustomReportMin = 5000;
            public const uint CustomReportMax = 5999;
        }

        #endregion

        #region 私有字段

        /// <summary>
        /// 事件报告服务
        /// 用于管理报告定义和事件触发机制
        /// </summary>
        private readonly IEventReportService _eventReportService;

        /// <summary>
        /// 状态变量服务
        /// 用于验证VID的有效性和数据访问权限
        /// </summary>
        private readonly IStatusVariableService _svidService;

        /// <summary>
        /// 设备状态服务
        /// 用于检查通信和控制状态（SEMI E30合规性）
        /// </summary>
        private readonly IEquipmentStateService _equipmentStateService;

        /// <summary>
        /// 设备配置
        /// 包含事件报告的限制参数和性能配置
        /// </summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>
        /// 配置锁
        /// 防止并发修改事件报告配置
        /// </summary>
        private readonly SemaphoreSlim _configLock;

        /// <summary>
        /// 当前报告定义缓存
        /// 存储活动的事件报告定义
        /// </summary>
        private readonly Dictionary<uint, ReportDefinition> _reportDefinitions;

        #endregion

        #region 属性

        /// <summary>
        /// 消息流号 - Stream 2 (设备控制流)
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号 - Function 33 (报告定义)
        /// </summary>
        public override byte Function => 33;

        /// <summary>
        /// 获取当前定义的报告数量
        /// </summary>
        public int ReportCount => _reportDefinitions.Count;

        /// <summary>
        /// 获取最大支持的报告数量
        /// </summary>
        public int MaxReportCount => _config?.Performance?.MaxEventReports ?? 100;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器，用于记录报告配置过程</param>
        /// <param name="eventReportService">事件报告服务，提供报告管理功能</param>
        /// <param name="svidService">状态变量服务，用于验证VID有效性</param>
        /// <param name="equipmentStateService">设备状态服务，用于检查通信和控制状态</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">当必需的依赖服务为null时抛出</exception>
        public S2F33Handler(
            ILogger<S2F33Handler> logger,
            IEventReportService eventReportService,
            IStatusVariableService svidService,
            IEquipmentStateService equipmentStateService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _eventReportService = eventReportService ?? throw new ArgumentNullException(nameof(eventReportService));
            _svidService = svidService ?? throw new ArgumentNullException(nameof(svidService));
            _equipmentStateService = equipmentStateService ?? throw new ArgumentNullException(nameof(equipmentStateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _configLock = new SemaphoreSlim(1, 1);
            _reportDefinitions = new Dictionary<uint, ReportDefinition>();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 处理S2F33事件报告定义消息
        /// </summary>
        /// <param name="message">接收到的S2F33消息，包含报告定义列表</param>
        /// <param name="cancellationToken">取消令牌，用于支持异步操作取消</param>
        /// <returns>S2F34报告定义确认响应消息</returns>
        /// <remarks>
        /// 处理流程：
        /// 1. 解析S2F33消息，提取报告定义列表
        /// 2. 验证每个报告定义的合法性
        /// 3. 检查VID列表中的变量是否都有效
        /// 4. 更新内部报告定义缓存
        /// 5. 配置事件报告服务的报告结构
        /// 6. 构建并返回S2F34响应消息
        /// 
        /// S2F33消息格式：
        /// L,2
        ///   1. &lt;U4 DATAID&gt; 数据ID (通常为0)
        ///   2. L,n 报告定义列表
        ///      L,2
        ///        1. &lt;U4 RPTID&gt; 报告ID
        ///        2. L,m VID列表
        ///           &lt;U4 VID&gt; 变量ID
        /// 
        /// S2F34响应格式：
        /// &lt;B DRACK&gt; 报告定义确认代码
        /// 
        /// 特殊处理：
        /// - 当报告列表为空时，删除所有现有报告定义
        /// - 当VID列表为空时，删除指定的报告定义
        /// - DATAID通常为0，用于标识报告定义组
        /// </remarks>
        /// <exception cref="InvalidOperationException">当事件报告服务未就绪时</exception>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到S2F33 (Define Report) 事件报告定义请求");

            // ===== 新增：检查通信状态（SEMI E30标准要求） =====
            // 根据SEMI E30标准，只有在通信建立后才能处理S2F33
            try
            {
                // 检查设备状态服务是否可用
                if (_equipmentStateService == null)
                {
                    Logger.LogWarning("拒绝S2F33请求：设备状态服务不可用");
                    return CreateS2F34Response(DefineReportAck.Denied);
                }

                // 检查通信是否已启用
                var commEnabled = await _equipmentStateService.IsCommunicationEnabledAsync();
                if (!commEnabled)
                {
                    Logger.LogWarning("拒绝S2F33请求：通信状态未启用（需要先成功完成S1F13/S1F14）");
                    return CreateS2F34Response(DefineReportAck.Denied);
                }

                // 检查控制状态
                var controlState = await _equipmentStateService.GetControlStateAsync();
                if (controlState == ControlState.EquipmentOffline)
                {
                    Logger.LogWarning("拒绝S2F33请求：设备处于离线状态（需要先成功完成S1F17/S1F18）");
                    return CreateS2F34Response(DefineReportAck.Denied);
                }

                Logger.LogDebug("通信状态检查通过，控制状态: {ControlState}", controlState);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "检查通信状态时发生异常");
                return CreateS2F34Response(DefineReportAck.Denied);
            }
            // ===== 结束新增 =====

            // 使用信号量确保配置串行执行
            await _configLock.WaitAsync(cancellationToken);
            try
            {
                // 步骤1: 解析S2F33消息
                var parseResult = ParseS2F33Message(message);
                if (!parseResult.IsValid)
                {
                    Logger.LogWarning("S2F33消息解析失败: {Reason}", parseResult.ErrorReason);
                    return CreateS2F34Response(parseResult.DrackCode);
                }

                var reportRequests = parseResult.ReportDefinitions!;
                Logger.LogInformation("解析事件报告定义成功: DATAID={DataId}, 报告数量={ReportCount}",
                    parseResult.DataId, reportRequests.Count);

                // 步骤2: 处理删除所有报告的特殊情况
                if (reportRequests.Count == 0)
                {
                    Logger.LogInformation("收到删除所有报告定义的请求");
                    await DeleteAllReportDefinitionsAsync();
                    return CreateS2F34Response(DefineReportAck.Accepted);
                }

                // 步骤3: 验证所有报告定义
                var validationResult = await ValidateAllReportDefinitionsAsync(reportRequests);
                if (!validationResult.IsValid)
                {
                    Logger.LogWarning("报告定义验证失败: {Reason}", validationResult.ErrorReason);
                    return CreateS2F34Response(validationResult.DrackCode);
                }

                // 步骤4: 应用报告定义
                var applyResult = await ApplyReportDefinitionsAsync(reportRequests);
                if (!applyResult.Success)
                {
                    Logger.LogError("应用报告定义失败: {Error}", applyResult.ErrorMessage);
                    return CreateS2F34Response(DefineReportAck.Denied);
                }

                // 步骤5: 更新内部缓存
                UpdateReportDefinitionCache(reportRequests);

                Logger.LogInformation("事件报告定义处理完成，当前活动报告数量: {Count}", _reportDefinitions.Count);
                return CreateS2F34Response(DefineReportAck.Accepted);
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("S2F33处理被取消");
                return CreateS2F34Response(DefineReportAck.Denied);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F33消息时发生未预期的异常");
                return CreateS2F34Response(DefineReportAck.Denied);
            }
            finally
            {
                _configLock.Release();
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S2F33消息内容
        /// </summary>
        /// <param name="message">S2F33消息</param>
        /// <returns>解析结果，包含报告定义列表</returns>
        /// <remarks>
        /// 解析规则：
        /// 1. 消息体必须是包含2个元素的列表
        /// 2. 第一个元素是U4格式的数据ID
        /// 3. 第二个元素是报告定义列表
        /// 4. 每个报告定义包含RPTID和VID列表
        /// 5. 空的报告列表表示删除所有报告
        /// </remarks>
        private ReportParseResult ParseS2F33Message(SecsMessage message)
        {
            try
            {
                if (message.SecsItem == null)
                {
                    return new ReportParseResult
                    {
                        IsValid = false,
                        ErrorReason = "消息体为空",
                        DrackCode = DefineReportAck.Denied
                    };
                }

                if (message.SecsItem.Count != 2)
                {
                    return new ReportParseResult
                    {
                        IsValid = false,
                        ErrorReason = $"消息体格式错误，期望2个元素，实际{message.SecsItem.Count}个",
                        DrackCode = DefineReportAck.Denied
                    };
                }

                var items = message.SecsItem.Items;

                // 解析DATAID
                var dataId = items[0].FirstValue<uint>();

                // 解析报告定义列表
                var reportDefinitions = new List<ReportDefinitionRequest>();

                if (items[1].Count > 0)
                {
                    foreach (var reportItem in items[1].Items)
                    {
                        var reportResult = ParseSingleReportDefinition(reportItem);
                        if (reportResult.IsValid)
                        {
                            reportDefinitions.Add(reportResult.ReportDefinition!);
                        }
                        else
                        {
                            return new ReportParseResult
                            {
                                IsValid = false,
                                ErrorReason = reportResult.ErrorReason,
                                DrackCode = reportResult.DrackCode
                            };
                        }
                    }
                }

                return new ReportParseResult
                {
                    IsValid = true,
                    DataId = dataId,
                    ReportDefinitions = reportDefinitions
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S2F33消息异常");
                return new ReportParseResult
                {
                    IsValid = false,
                    ErrorReason = $"解析异常: {ex.Message}",
                    DrackCode = DefineReportAck.Denied
                };
            }
        }

        /// <summary>
        /// 解析单个报告定义
        /// </summary>
        /// <param name="reportItem">报告项，应包含RPTID和VID列表</param>
        /// <returns>报告定义解析结果</returns>
        /// <remarks>
        /// 报告定义格式：
        /// L,2
        ///   1. &lt;U4 RPTID&gt; 报告ID
        ///   2. L,n VID列表
        ///      &lt;U4 VID&gt; 变量ID
        /// 
        /// 特殊处理：
        /// - VID列表为空(L,0)时表示删除该报告定义
        /// - RPTID=0为无效报告ID
        /// - VID=0为无效变量ID
        /// </remarks>
        private SingleReportParseResult ParseSingleReportDefinition(Item reportItem)
        {
            try
            {
                if (reportItem.Count != 2)
                {
                    return new SingleReportParseResult
                    {
                        IsValid = false,
                        ErrorReason = "报告定义格式错误，必须包含RPTID和VID列表",
                        DrackCode = DefineReportAck.Denied
                    };
                }

                var rptidItem = reportItem.Items[0];
                var vidListItem = reportItem.Items[1];

                // 解析RPTID
                var rptId = rptidItem.FirstValue<uint>();
                if (rptId == 0)
                {
                    return new SingleReportParseResult
                    {
                        IsValid = false,
                        ErrorReason = "报告ID不能为0",
                        DrackCode = DefineReportAck.InvalidReportId
                    };
                }

                // 解析VID列表
                var vidList = new List<uint>();
                if (vidListItem.Count > 0)
                {
                    foreach (var vidItem in vidListItem.Items)
                    {
                        var vid = vidItem.FirstValue<uint>();
                        if (vid == 0)
                        {
                            return new SingleReportParseResult
                            {
                                IsValid = false,
                                ErrorReason = $"变量ID不能为0，报告ID: {rptId}",
                                DrackCode = DefineReportAck.InvalidVariableId
                            };
                        }
                        vidList.Add(vid);
                    }
                }

                var reportDef = new ReportDefinitionRequest
                {
                    ReportId = rptId,
                    VariableIds = vidList,
                    IsDeleteRequest = vidList.Count == 0
                };

                return new SingleReportParseResult
                {
                    IsValid = true,
                    ReportDefinition = reportDef
                };
            }
            catch (Exception ex)
            {
                return new SingleReportParseResult
                {
                    IsValid = false,
                    ErrorReason = $"解析报告定义异常: {ex.Message}",
                    DrackCode = DefineReportAck.Denied
                };
            }
        }

        #endregion

        #region 私有方法 - 验证

        /// <summary>
        /// 验证所有报告定义的有效性
        /// </summary>
        /// <param name="reportRequests">报告定义请求列表</param>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// 验证项目：
        /// 1. 报告ID是否在有效范围内且不重复
        /// 2. VID列表是否包含有效的状态变量
        /// 3. 报告总数是否超出设备限制
        /// 4. 每个报告的VID数量是否合理
        /// </remarks>
        private async Task<ReportValidationResult> ValidateAllReportDefinitionsAsync(List<ReportDefinitionRequest> reportRequests)
        {
            // 验证1: 检查报告数量限制
            var nonDeleteRequests = reportRequests.Where(r => !r.IsDeleteRequest).ToList();
            var totalReportsAfterUpdate = _reportDefinitions.Count + nonDeleteRequests.Count;

            if (totalReportsAfterUpdate > MaxReportCount)
            {
                return new ReportValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"报告定义总数超出限制，最大{MaxReportCount}个",
                    DrackCode = DefineReportAck.TooManyReports
                };
            }

            // 验证2: 检查RPTID重复性
            var rptIds = reportRequests.Select(r => r.ReportId).ToList();
            var duplicateRptIds = rptIds.GroupBy(id => id)
                                       .Where(g => g.Count() > 1)
                                       .Select(g => g.Key)
                                       .ToList();

            if (duplicateRptIds.Count > 0)
            {
                return new ReportValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"存在重复的报告ID: {string.Join(", ", duplicateRptIds)}",
                    DrackCode = DefineReportAck.InvalidReportId
                };
            }

            // 验证3: 检查每个报告定义的详细有效性
            foreach (var reportRequest in reportRequests)
            {
                var singleValidation = await ValidateSingleReportDefinitionAsync(reportRequest);
                if (!singleValidation.IsValid)
                {
                    return new ReportValidationResult
                    {
                        IsValid = false,
                        ErrorReason = $"报告ID {reportRequest.ReportId} 验证失败: {singleValidation.ErrorReason}",
                        DrackCode = singleValidation.DrackCode
                    };
                }
            }

            return new ReportValidationResult { IsValid = true };
        }

        /// <summary>
        /// 验证单个报告定义
        /// </summary>
        /// <param name="reportRequest">单个报告定义请求</param>
        /// <returns>验证结果</returns>
        private async Task<SingleReportValidationResult> ValidateSingleReportDefinitionAsync(ReportDefinitionRequest reportRequest)
        {
            // 验证RPTID范围
            if (!IsValidReportId(reportRequest.ReportId))
            {
                return new SingleReportValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"报告ID超出有效范围: {reportRequest.ReportId}",
                    DrackCode = DefineReportAck.InvalidReportId
                };
            }

            // 如果是删除请求，不需要验证VID
            if (reportRequest.IsDeleteRequest)
            {
                return new SingleReportValidationResult { IsValid = true };
            }

            // 验证VID数量限制
            const int maxVidPerReport = 20;
            if (reportRequest.VariableIds.Count > maxVidPerReport)
            {
                return new SingleReportValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"报告包含的VID数量过多，最大{maxVidPerReport}个",
                    DrackCode = DefineReportAck.InvalidVariableId
                };
            }

            // 验证每个VID的有效性
            var invalidVids = new List<uint>();
            foreach (var vid in reportRequest.VariableIds)
            {
                try
                {
                    var value = await _svidService.GetSvidValueAsync(vid);
                    if (value == null)
                    {
                        invalidVids.Add(vid);
                    }
                }
                catch (Exception)
                {
                    invalidVids.Add(vid);
                }
            }

            if (invalidVids.Count > 0)
            {
                return new SingleReportValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"发现无效的VID: {string.Join(", ", invalidVids)}",
                    DrackCode = DefineReportAck.InvalidVariableId
                };
            }

            return new SingleReportValidationResult { IsValid = true };
        }

        /// <summary>
        /// 检查报告ID是否在有效范围内
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <returns>是否有效</returns>
        private bool IsValidReportId(uint rptId)
        {
            return (rptId >= DicerReportIds.SystemStatusMin && rptId <= DicerReportIds.SystemStatusMax) ||
                   (rptId >= DicerReportIds.ProcessDataMin && rptId <= DicerReportIds.ProcessDataMax) ||
                   (rptId >= DicerReportIds.EquipmentStatusMin && rptId <= DicerReportIds.EquipmentStatusMax) ||
                   (rptId >= DicerReportIds.AlarmStatusMin && rptId <= DicerReportIds.AlarmStatusMax) ||
                   (rptId >= DicerReportIds.CustomReportMin && rptId <= DicerReportIds.CustomReportMax);
        }

        #endregion

        #region 私有方法 - 报告管理

        /// <summary>
        /// 删除所有报告定义
        /// </summary>
        /// <returns>异步任务</returns>
        private async Task DeleteAllReportDefinitionsAsync()
        {
            try
            {
                var reportIds = _reportDefinitions.Keys.ToList();

                foreach (var rptId in reportIds)
                {
                    await _eventReportService.DeleteReportDefinitionAsync(rptId);
                }

                _reportDefinitions.Clear();
                Logger.LogInformation("已删除所有报告定义，数量: {Count}", reportIds.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "删除所有报告定义失败");
            }
        }

        /// <summary>
        /// 应用报告定义到事件报告服务
        /// </summary>
        /// <param name="reportRequests">报告定义请求列表</param>
        /// <returns>应用结果</returns>
        private async Task<ApplyResult> ApplyReportDefinitionsAsync(List<ReportDefinitionRequest> reportRequests)
        {
            try
            {
                foreach (var request in reportRequests)
                {
                    if (request.IsDeleteRequest)
                    {
                        // 删除现有报告定义
                        await _eventReportService.DeleteReportDefinitionAsync(request.ReportId);
                        Logger.LogDebug("删除报告定义: RPTID={RptId}", request.ReportId);
                    }
                    else
                    {
                        // 创建或更新报告定义
                        var reportDef = new ReportDefinition
                        {
                            ReportId = request.ReportId,
                            VariableIds = request.VariableIds.ToList(),
                            CreatedTime = DateTime.Now,
                            LastModifiedTime = DateTime.Now
                        };

                        await _eventReportService.DefineReportAsync(reportDef);
                        Logger.LogDebug("配置报告定义: RPTID={RptId}, VID数量={VidCount}",
                            request.ReportId, request.VariableIds.Count);
                    }
                }

                return new ApplyResult { Success = true };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "应用报告定义失败");
                return new ApplyResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 获取已定义的报告列表
        /// </summary>
        /// <returns>报告ID列表</returns>
        public List<uint> GetDefinedReports()
        {
            return _reportDefinitions.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// 获取报告包含的变量列表
        /// </summary>
        /// <param name="rptid">报告ID</param>
        /// <returns>变量ID列表</returns>
        public List<uint> GetReportVariables(uint rptid)
        {
            if (_reportDefinitions.TryGetValue(rptid, out var definition))
            {
                return definition.VariableIds.ToList();
            }
            return new List<uint>();
        }


        /// <summary>
        /// 更新报告定义缓存
        /// </summary>
        /// <param name="reportRequests">报告定义请求列表</param>
        private void UpdateReportDefinitionCache(List<ReportDefinitionRequest> reportRequests)
        {
            foreach (var request in reportRequests)
            {
                if (request.IsDeleteRequest)
                {
                    // 从缓存中删除
                    _reportDefinitions.Remove(request.ReportId);
                    Logger.LogDebug("从缓存中删除报告: RPTID={RptId}", request.ReportId);
                }
                else
                {
                    // 添加或更新缓存
                    var reportDef = new ReportDefinition
                    {
                        ReportId = request.ReportId,
                        VariableIds = request.VariableIds.ToList(),
                        CreatedTime = DateTime.Now,
                        LastModifiedTime = DateTime.Now
                    };

                    _reportDefinitions[request.ReportId] = reportDef;
                    Logger.LogDebug("更新缓存中的报告: RPTID={RptId}", request.ReportId);
                }
            }
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S2F34报告定义确认响应
        /// </summary>
        /// <param name="ackCode">确认代码</param>
        /// <returns>S2F34响应消息</returns>
        /// <remarks>
        /// S2F34消息格式：
        /// &lt;B DRACK&gt; 报告定义确认代码
        /// 
        /// 确认代码含义：
        /// 0: 已接受
        /// 1: 被拒绝
        /// 2: 至少一个VID无效
        /// 3: 至少一个RPTID无效
        /// 4: 报告定义过多
        /// </remarks>
        private SecsMessage CreateS2F34Response(DefineReportAck ackCode)
        {
            var ackByte = (byte)ackCode;
            var ackDescription = GetDrackDescription(ackCode);

            Logger.LogDebug("创建S2F34响应: DRACK={AckCode} ({Description})", ackByte, ackDescription);

            var response = new SecsMessage(2, 34, false)
            {
                Name = "Define Report Acknowledge",
                SecsItem = Item.B(ackByte)
            };

            return response;
        }

        /// <summary>
        /// 获取DRACK代码的描述
        /// </summary>
        /// <param name="ackCode">确认代码</param>
        /// <returns>代码描述</returns>
        private string GetDrackDescription(DefineReportAck ackCode)
        {
            return ackCode switch
            {
                DefineReportAck.Accepted => "已接受",
                DefineReportAck.Denied => "被拒绝",
                DefineReportAck.InvalidVariableId => "VID无效",
                DefineReportAck.InvalidReportId => "RPTID无效",
                DefineReportAck.TooManyReports => "报告过多",
                _ => $"未知代码: {(byte)ackCode}"
            };
        }

        #endregion

        #region 公共方法 - 状态查询

        /// <summary>
        /// 获取指定报告的定义信息
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <returns>报告定义，如果不存在返回null</returns>
        public ReportDefinition? GetReportDefinition(uint rptId)
        {
            return _reportDefinitions.GetValueOrDefault(rptId);
        }

        /// <summary>
        /// 获取所有活动报告的定义
        /// </summary>
        /// <returns>报告定义列表</returns>
        public IEnumerable<ReportDefinition> GetAllReportDefinitions()
        {
            return _reportDefinitions.Values.OrderBy(r => r.ReportId);
        }

        /// <summary>
        /// 检查指定报告是否已定义
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <returns>是否已定义</returns>
        public bool IsReportDefined(uint rptId)
        {
            return _reportDefinitions.ContainsKey(rptId);
        }

        #endregion

        #region 辅助类和结构

        /// <summary>
        /// 报告定义请求
        /// </summary>
        public class ReportDefinitionRequest
        {
            /// <summary>
            /// 报告ID
            /// </summary>
            public uint ReportId { get; set; }

            /// <summary>
            /// 变量ID列表
            /// </summary>
            public List<uint> VariableIds { get; set; } = new();

            /// <summary>
            /// 是否为删除请求
            /// </summary>
            public bool IsDeleteRequest { get; set; }
        }

        /// <summary>
        /// S2F33解析结果
        /// </summary>
        private class ReportParseResult
        {
            /// <summary>
            /// 解析是否成功
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 数据ID
            /// </summary>
            public uint DataId { get; set; }

            /// <summary>
            /// 解析出的报告定义列表
            /// </summary>
            public List<ReportDefinitionRequest>? ReportDefinitions { get; set; }

            /// <summary>
            /// 错误原因（当IsValid为false时）
            /// </summary>
            public string? ErrorReason { get; set; }

            /// <summary>
            /// 对应的DRACK确认代码
            /// </summary>
            public DefineReportAck DrackCode { get; set; }
        }

        /// <summary>
        /// 单个报告解析结果
        /// </summary>
        private class SingleReportParseResult
        {
            /// <summary>
            /// 解析是否成功
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 解析出的报告定义
            /// </summary>
            public ReportDefinitionRequest? ReportDefinition { get; set; }

            /// <summary>
            /// 错误原因
            /// </summary>
            public string? ErrorReason { get; set; }

            /// <summary>
            /// 对应的DRACK代码
            /// </summary>
            public DefineReportAck DrackCode { get; set; }
        }

        /// <summary>
        /// 报告验证结果
        /// </summary>
        private class ReportValidationResult
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
            /// 对应的DRACK代码
            /// </summary>
            public DefineReportAck DrackCode { get; set; }
        }

        /// <summary>
        /// 单个报告验证结果
        /// </summary>
        private class SingleReportValidationResult
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
            /// 对应的DRACK代码
            /// </summary>
            public DefineReportAck DrackCode { get; set; }
        }

        /// <summary>
        /// 应用结果
        /// </summary>
        private class ApplyResult
        {
            /// <summary>
            /// 是否应用成功
            /// </summary>
            public bool Success { get; set; }

            /// <summary>
            /// 错误信息
            /// </summary>
            public string? ErrorMessage { get; set; }
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

                // 清理报告定义
                _reportDefinitions.Clear();
            }
        }

        #endregion
    }
}
