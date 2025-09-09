// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F14Handler.cs
// 版本: v2.0.0
// 描述: S1F14消息处理器 - Establish Communications Request Acknowledge 建立通信确认处理器

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;
using static Secs4Net.Item;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S1F14 (Establish Communications Request Acknowledge) 建立通信确认处理器
    /// 处理设备发送S1F13后收到的主机确认响应，完成通信建立握手
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F13: Establish Communications Request - 建立通信请求
    /// - S1F14: Establish Communications Request Acknowledge - 建立通信确认
    /// 
    /// 消息格式：
    /// S1F14
    /// L,2
    ///   1. &lt;COMMACK&gt; B,1 确认码
    ///      - 0 = Accept (接受)
    ///      - 1 = Denied (拒绝)
    ///   2. L,2 (仅当COMMACK=0时有效)
    ///      1. &lt;MDLN&gt; A,20 主机型号
    ///      2. &lt;SOFTREV&gt; A,20 主机软件版本
    /// 
    /// 通信状态机：
    /// - WAIT CRA -> COMMUNICATING (当COMMACK=0)
    /// - WAIT CRA -> WAIT DELAY (当COMMACK!=0或超时)
    /// 
    /// 与S1F13配合场景：
    /// 1. 设备发送S1F13，主机回复S1F14（设备主动）
    /// 2. 主机发送S1F13，设备回复S1F14（主机主动）
    /// 3. 双方同时发送S1F13，各自回复S1F14（同时建立）
    /// </remarks>
    public class S1F14Handler : SecsMessageHandlerBase, IS1F14Handler
    {
        #region 私有字段

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>设备数据模型</summary>
        private readonly DiceDataModel _dataModel;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>通信管理器</summary>
        private readonly ISecsConnectionManager _connectionManager;

        /// <summary>事件服务（可选）</summary>
        private readonly IEventReportService? _eventService;

        /// <summary>S1F13处理器（用于协调）</summary>
        private readonly IS1F13Handler? _s1f13Handler;

        /// <summary>通信建立状态追踪</summary>
        private readonly CommunicationEstablishmentTracker _establishmentTracker;

        /// <summary>最后收到的主机信息</summary>
        private HostInformation? _lastHostInfo;

        /// <summary>通信建立统计</summary>
        private readonly CommunicationStatistics _statistics;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 14;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public S1F14Handler(
            ILogger<S1F14Handler> logger,
            IEquipmentStateService stateService,
            DiceDataModel dataModel,
            ISecsConnectionManager connectionManager,
            IOptions<EquipmentSystemConfiguration> options,
            IEventReportService? eventService = null,
            IS1F13Handler? s1f13Handler = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _eventService = eventService;
            _s1f13Handler = s1f13Handler;

            _establishmentTracker = new CommunicationEstablishmentTracker();
            _statistics = new CommunicationStatistics();

            Logger.LogInformation("S1F14处理器已初始化");
        }

        #endregion

        #region 公共方法 - IS1F14Handler接口

        /// <summary>
        /// 发送S1F14确认响应（作为对主机S1F13的回复）
        /// </summary>
        /// <param name="accept">是否接受通信建立</param>
        /// <param name="targetDeviceId">目标设备ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        public async Task<bool> SendCommunicationAcknowledgeAsync(
            bool accept,
            ushort targetDeviceId = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var commAck = accept ? CommAck.Accepted : CommAck.Denied;
                Logger.LogInformation($"发送S1F14确认响应 - COMMACK: {(byte)commAck} ({commAck})");

                // 构建S1F14消息
                var s1f14 = CreateS1F14Message(commAck);

                // 发送消息（S1F14是响应消息，不期待回复）
                await _connectionManager.SendMessageAsync(s1f14, cancellationToken);

                // 如果接受，更新通信状态
                if (accept)
                {
                    await OnCommunicationAccepted();
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "发送S1F14确认响应失败");
                return false;
            }
        }

        /// <summary>
        /// 获取通信建立统计信息
        /// </summary>
        public CommunicationStatisticsInfo GetStatistics()
        {
            return _statistics.GetSnapshot();
        }

        /// <summary>
        /// 重置通信建立统计
        /// </summary>
        public void ResetStatistics()
        {
            _statistics.Reset();
            Logger.LogInformation("通信建立统计已重置");
        }

        #endregion

        #region 消息处理 - 处理主机发送的S1F14（作为设备S1F13的响应）

        /// <summary>
        /// 处理S1F14消息（设备发送S1F13后收到的主机响应）
        /// </summary>
        /// <param name="message">接收到的S1F14消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>null（S1F14是响应消息，不需要再回复）</returns>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F14 (Establish Communications Acknowledge) 建立通信确认");

            try
            {
                // 解析S1F14消息
                var (commAck, hostInfo) = ParseS1F14Message(message.SecsItem);

                Logger.LogInformation($"主机响应 - COMMACK: {(byte)commAck} ({commAck})");

                // 验证是否有未完成的S1F13请求
                if (!_establishmentTracker.HasPendingRequest())
                {
                    Logger.LogWarning("收到意外的S1F14（没有待处理的S1F13请求）");
                    _statistics.RecordUnexpectedResponse();
                    return null;
                }

                // 处理确认结果
                if (commAck == CommAck.Accepted)
                {
                    await HandleCommunicationAccepted(hostInfo);
                }
                else
                {
                    await HandleCommunicationDenied(commAck);
                }

                // S1F14是响应消息，不需要回复
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F14消息失败");
                _statistics.RecordError(ex);
                return null;
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S1F14消息
        /// </summary>
        private (CommAck commAck, HostInformation hostInfo) ParseS1F14Message(Item? item)
        {
            var commAck = CommAck.Denied;
            var hostInfo = new HostInformation();

            try
            {
                if (item == null || item.Format != SecsFormat.List)
                {
                    Logger.LogWarning("S1F14消息格式无效");
                    return (commAck, hostInfo);
                }

                var items = item.Items;
                if (items == null || items.Length < 1)
                {
                    Logger.LogWarning("S1F14消息缺少必要元素");
                    return (commAck, hostInfo);
                }

                // 解析COMMACK
                var commAckItem = items[0];
                if (commAckItem.Format == SecsFormat.Binary)
                {
                    var ackValue = commAckItem.FirstValue<byte>();
                    commAck = (CommAck)ackValue;
                    Logger.LogDebug($"解析COMMACK: {ackValue} ({commAck})");
                }

                // 如果COMMACK=0且有第二个元素，解析主机信息
                if (commAck == CommAck.Accepted && items.Length > 1)
                {
                    var hostInfoItem = items[1];
                    if (hostInfoItem.Format == SecsFormat.List && hostInfoItem.Count >= 2)
                    {
                        var infoItems = hostInfoItem.Items;
                        if (infoItems != null && infoItems.Length >= 2)
                        {
                            // MDLN
                            hostInfo.ModelName = infoItems[0].GetString() ?? "UNKNOWN";
                            // SOFTREV
                            hostInfo.SoftwareRevision = infoItems[1].GetString() ?? "UNKNOWN";

                            // 限制长度（SEMI标准最大20字符）
                            if (hostInfo.ModelName.Length > 20)
                                hostInfo.ModelName = hostInfo.ModelName.Substring(0, 20);
                            if (hostInfo.SoftwareRevision.Length > 20)
                                hostInfo.SoftwareRevision = hostInfo.SoftwareRevision.Substring(0, 20);

                            Logger.LogDebug($"主机信息 - MDLN: '{hostInfo.ModelName}', SOFTREV: '{hostInfo.SoftwareRevision}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S1F14消息异常");
            }

            return (commAck, hostInfo);
        }

        /// <summary>
        /// 创建S1F14消息
        /// </summary>
        private SecsMessage CreateS1F14Message(CommAck commAck)
        {
            Item messageItem;

            if (commAck == CommAck.Accepted)
            {
                // 接受：包含设备信息
                messageItem = L(
                    B((byte)commAck),
                    L(
                        A(_dataModel.ModelName),        // MDLN
                        A(_dataModel.SoftwareRevision)  // SOFTREV
                    )
                );
            }
            else
            {
                // 拒绝：只有确认码
                messageItem = L(
                    B((byte)commAck),
                    L()  // 空列表
                );
            }

            return new SecsMessage(1, 14, false)
            {
                Name = "EstablishCommunicationsAcknowledge",
                SecsItem = messageItem
            };
        }

        #endregion

        #region 私有方法 - 通信建立处理

        /// <summary>
        /// 处理通信建立被接受
        /// </summary>
        private async Task HandleCommunicationAccepted(HostInformation hostInfo)
        {
            try
            {
                Logger.LogInformation("✅ 主机接受通信建立请求");
                Logger.LogInformation($"主机信息 - 型号: {hostInfo.ModelName}, 版本: {hostInfo.SoftwareRevision}");

                // 记录成功
                _establishmentTracker.RecordSuccess();
                _statistics.RecordAccepted();
                _lastHostInfo = hostInfo;

                // 更新通信状态
                await UpdateCommunicationState(true);

                // 触发通信建立成功事件
                await TriggerCommunicationEstablishedEvent(hostInfo);

                // 执行同步操作
                await PerformPostEstablishmentSync();

                Logger.LogInformation($"通信建立完成 - 成功次数: {_statistics.AcceptedCount}, " +
                                     $"成功率: {_statistics.GetSuccessRate():P2}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理通信建立接受失败");
            }
        }

        /// <summary>
        /// 处理通信建立被拒绝
        /// </summary>
        private async Task HandleCommunicationDenied(CommAck commAck)
        {
            try
            {
                Logger.LogWarning($"❌ 主机拒绝通信建立请求 - COMMACK: {(byte)commAck}");

                // 记录失败
                _establishmentTracker.RecordFailure(commAck);
                _statistics.RecordDenied(commAck);

                // 更新通信状态
                await UpdateCommunicationState(false);

                // 分析拒绝原因
                var reason = AnalyzeDenialReason(commAck);
                Logger.LogWarning($"拒绝原因分析: {reason}");

                // 触发通信建立失败事件
                await TriggerCommunicationDeniedEvent(commAck, reason);

                // 决定重试策略
                await DetermineRetryStrategy();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理通信建立拒绝失败");
            }
        }

        /// <summary>
        /// 处理通信被接受（作为S1F14发送方）
        /// </summary>
        private async Task OnCommunicationAccepted()
        {
            try
            {
                Logger.LogInformation("设备接受主机的通信建立请求");

                // 更新状态
                await UpdateCommunicationState(true);

                // 记录统计
                _statistics.RecordAcceptedAsSender();

                // 执行同步
                await PerformPostEstablishmentSync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理通信接受失败");
            }
        }

        #endregion

        #region 私有方法 - 状态更新

        /// <summary>
        /// 更新通信状态
        /// </summary>
        private async Task UpdateCommunicationState(bool established)
        {
            try
            {
                // 更新状态服务
                await _stateService.SetCommunicationEstablishedAsync(established);

                // 更新数据模型
                if (established)
                {
                    _dataModel.ConnectionState = HsmsConnectionState.Selected;
                    Logger.LogDebug("通信状态更新为: COMMUNICATING");
                }
                else
                {
                    _dataModel.ConnectionState = HsmsConnectionState.Connected;
                    Logger.LogDebug("通信状态更新为: NOT COMMUNICATING");
                }

                // 更新控制状态（如果需要）
                await UpdateControlStateIfNeeded(established);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新通信状态失败");
            }
        }

        /// <summary>
        /// 根据需要更新控制状态
        /// </summary>
        private async Task UpdateControlStateIfNeeded(bool communicationEstablished)
        {
            if (!communicationEstablished)
            {
                // 通信断开时，可能需要转到离线状态
                var currentControl = await _stateService.GetControlStateAsync();
                if (currentControl == ControlState.OnlineRemote ||
                    currentControl == ControlState.OnlineLocal)
                {
                    Logger.LogWarning("通信断开，控制状态可能需要调整");
                }
            }
        }

        #endregion

        #region 私有方法 - 同步操作

        /// <summary>
        /// 执行通信建立后的同步操作
        /// </summary>
        private async Task PerformPostEstablishmentSync()
        {
            try
            {
                Logger.LogDebug("开始执行通信建立后同步");

                // 1. 同步设备状态
                await SyncDeviceStatus();

                // 2. 报告当前报警
                await ReportActiveAlarms();

                // 3. 同步事件配置
                await SyncEventConfiguration();

                // 4. 同步设备常量
                await SyncEquipmentConstants();

                Logger.LogDebug("通信建立后同步完成");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行同步操作失败");
            }
        }

        /// <summary>
        /// 同步设备状态
        /// </summary>
        private async Task SyncDeviceStatus()
        {
            try
            {
                // 更新所有关键状态变量
                _dataModel.ControlState = await _stateService.GetControlStateAsync();
                _dataModel.ProcessState = await _stateService.GetProcessStateAsync();
                _dataModel.EquipmentState = await _stateService.GetEquipmentStateAsync();

                // 更新时钟
                _dataModel.Clock = DateTime.Now.ToString("yyyyMMddHHmmssff");

                Logger.LogDebug($"设备状态同步完成 - Control: {_dataModel.ControlState}, " +
                               $"Process: {_dataModel.ProcessState}, " +
                               $"Equipment: {_dataModel.EquipmentState}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步设备状态失败");
            }
        }

        /// <summary>
        /// 报告激活的报警
        /// </summary>
        private async Task ReportActiveAlarms()
        {
            try
            {
                var activeAlarms = _dataModel.AlarmsSet;
                if (activeAlarms != null && activeAlarms.Count > 0)
                {
                    Logger.LogInformation($"报告 {activeAlarms.Count} 个激活报警");

                    // 这里应该调用S5F1Handler发送报警
                    foreach (var alarmId in activeAlarms)
                    {
                        Logger.LogDebug($"激活报警: {alarmId}");
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "报告激活报警失败");
            }
        }

        /// <summary>
        /// 同步事件配置
        /// </summary>
        private async Task SyncEventConfiguration()
        {
            try
            {
                // 确保关键事件已启用
                var criticalEvents = new uint[]
                {
                   SemiIdDefinitions.Ceid.ProcessStart,
                   SemiIdDefinitions.Ceid.ProcessEnd,
                   SemiIdDefinitions.Ceid.ControlStateOFFLINE,
                   SemiIdDefinitions.Ceid.ControlStateLOCAL,
                   SemiIdDefinitions.Ceid.ControlStateREMOTE
                };

                Logger.LogDebug($"确认 {criticalEvents.Length} 个关键事件已配置");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步事件配置失败");
            }
        }

        /// <summary>
        /// 同步设备常量
        /// </summary>
        private async Task SyncEquipmentConstants()
        {
            try
            {
                // 同步关键设备常量
                Logger.LogDebug("同步设备常量");

                // EstablishCommunicationsTimeout
                var timeout = _config.Equipment.EstablishCommunicationsTimeout;
                Logger.LogDebug($"EstablishCommunicationsTimeout: {timeout}秒");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步设备常量失败");
            }
        }

        #endregion

        #region 私有方法 - 事件触发

        /// <summary>
        /// 触发通信建立成功事件
        /// </summary>
        private async Task TriggerCommunicationEstablishedEvent(HostInformation hostInfo)
        {
            try
            {
                if (_eventService == null) return;

                var eventData = new Dictionary<string, object>
                {
                    ["EventType"] = "CommunicationEstablished",
                    ["HostModel"] = hostInfo.ModelName,
                    ["HostSoftware"] = hostInfo.SoftwareRevision,
                    ["DeviceModel"] = _dataModel.ModelName,
                    ["DeviceSoftware"] = _dataModel.SoftwareRevision,
                    ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    ["EstablishmentCount"] = _statistics.AcceptedCount
                };

                // 使用自定义CEID
                const uint COMM_ESTABLISHED_EVENT = 9001;
                await _eventService.TriggerEventAsync(COMM_ESTABLISHED_EVENT, "Communication Established");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "触发通信建立事件失败");
            }
        }

        /// <summary>
        /// 触发通信建立失败事件
        /// </summary>
        private async Task TriggerCommunicationDeniedEvent(CommAck commAck, string reason)
        {
            try
            {
                if (_eventService == null) return;

                var eventData = new Dictionary<string, object>
                {
                    ["EventType"] = "CommunicationDenied",
                    ["COMMACK"] = (byte)commAck,
                    ["Reason"] = reason,
                    ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    ["DenialCount"] = _statistics.DeniedCount
                };

                // 使用自定义CEID
                const uint COMM_DENIED_EVENT = 9002;
                await _eventService.TriggerEventAsync(COMM_DENIED_EVENT, "Communication Denied");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "触发通信拒绝事件失败");
            }
        }

        #endregion

        #region 私有方法 - 策略和分析

        /// <summary>
        /// 分析拒绝原因
        /// </summary>
        private string AnalyzeDenialReason(CommAck commAck)
        {
            return commAck switch
            {
                CommAck.Denied => "通用拒绝 - 主机当前不接受通信建立",
                CommAck.MDLNMismatch => "设备型号不匹配",
                CommAck.SOFTREVMismatch => "软件版本不兼容",
                CommAck.NotReady => "主机未准备就绪",
                CommAck.AlreadyEstablished => "通信已建立",
                _ => $"未知拒绝原因: {(byte)commAck}"
            };
        }

        /// <summary>
        /// 决定重试策略
        /// </summary>
        private async Task DetermineRetryStrategy()
        {
            try
            {
                var failureCount = _establishmentTracker.GetConsecutiveFailures();

                if (failureCount >= 3)
                {
                    Logger.LogWarning($"连续失败 {failureCount} 次，考虑延长重试间隔");

                    // 可以触发告警或通知操作员
                    if (failureCount >= 5)
                    {
                        Logger.LogError("通信建立持续失败，需要人工介入");
                    }
                }

                // 通知S1F13Handler调整重试策略
                if (_s1f13Handler != null)
                {
                    // 这里可以调用S1F13Handler的方法调整重试参数
                    await Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "决定重试策略失败");
            }
        }

        #endregion

        #region 内部类型定义

        /// <summary>
        /// 通信确认码
        /// </summary>
        private enum CommAck : byte
        {
            /// <summary>接受</summary>
            Accepted = 0,
            /// <summary>通用拒绝</summary>
            Denied = 1,
            /// <summary>MDLN不匹配</summary>
            MDLNMismatch = 2,
            /// <summary>SOFTREV不匹配</summary>
            SOFTREVMismatch = 3,
            /// <summary>未就绪</summary>
            NotReady = 4,
            /// <summary>已建立</summary>
            AlreadyEstablished = 5
        }

        /// <summary>
        /// 主机信息
        /// </summary>
        private class HostInformation
        {
            public string ModelName { get; set; } = "";
            public string SoftwareRevision { get; set; } = "";
            public DateTime ReceivedTime { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// 通信建立追踪器
        /// </summary>
        private class CommunicationEstablishmentTracker
        {
            private DateTime? _lastRequestTime;
            private int _consecutiveFailures;
            private readonly object _lock = new();

            public bool HasPendingRequest()
            {
                lock (_lock)
                {
                    if (_lastRequestTime == null) return false;

                    // 30秒内的请求认为是待处理的
                    var elapsed = DateTime.Now - _lastRequestTime.Value;
                    return elapsed.TotalSeconds < 30;
                }
            }

            public void RecordRequest()
            {
                lock (_lock)
                {
                    _lastRequestTime = DateTime.Now;
                }
            }

            public void RecordSuccess()
            {
                lock (_lock)
                {
                    _lastRequestTime = null;
                    _consecutiveFailures = 0;
                }
            }

            public void RecordFailure(CommAck reason)
            {
                lock (_lock)
                {
                    _lastRequestTime = null;
                    _consecutiveFailures++;
                }
            }

            public int GetConsecutiveFailures()
            {
                lock (_lock)
                {
                    return _consecutiveFailures;
                }
            }
        }

        /// <summary>
        /// 通信统计
        /// </summary>
        private class CommunicationStatistics
        {
            private int _acceptedCount;
            private int _deniedCount;
            private int _unexpectedCount;
            private int _errorCount;
            private int _acceptedAsSenderCount;
            private DateTime _startTime = DateTime.Now;
            private readonly object _lock = new();

            public int AcceptedCount => _acceptedCount;
            public int DeniedCount => _deniedCount;

            public void RecordAccepted()
            {
                lock (_lock) { _acceptedCount++; }
            }

            public void RecordDenied(CommAck reason)
            {
                lock (_lock) { _deniedCount++; }
            }

            public void RecordAcceptedAsSender()
            {
                lock (_lock) { _acceptedAsSenderCount++; }
            }

            public void RecordUnexpectedResponse()
            {
                lock (_lock) { _unexpectedCount++; }
            }

            public void RecordError(Exception ex)
            {
                lock (_lock) { _errorCount++; }
            }

            public double GetSuccessRate()
            {
                lock (_lock)
                {
                    var total = _acceptedCount + _deniedCount;
                    return total > 0 ? (double)_acceptedCount / total : 0;
                }
            }

            public CommunicationStatisticsInfo GetSnapshot()
            {
                lock (_lock)
                {
                    return new CommunicationStatisticsInfo
                    {
                        AcceptedCount = _acceptedCount,
                        DeniedCount = _deniedCount,
                        UnexpectedCount = _unexpectedCount,
                        ErrorCount = _errorCount,
                        AcceptedAsSenderCount = _acceptedAsSenderCount,
                        SuccessRate = GetSuccessRate(),
                        Uptime = DateTime.Now - _startTime
                    };
                }
            }

            public void Reset()
            {
                lock (_lock)
                {
                    _acceptedCount = 0;
                    _deniedCount = 0;
                    _unexpectedCount = 0;
                    _errorCount = 0;
                    _acceptedAsSenderCount = 0;
                    _startTime = DateTime.Now;
                }
            }
        }

        #endregion
    }

    #region 公共类型定义

    /// <summary>
    /// 通信统计信息
    /// </summary>
    public class CommunicationStatisticsInfo
    {
        /// <summary>接受次数</summary>
        public int AcceptedCount { get; set; }

        /// <summary>拒绝次数</summary>
        public int DeniedCount { get; set; }

        /// <summary>意外响应次数</summary>
        public int UnexpectedCount { get; set; }

        /// <summary>错误次数</summary>
        public int ErrorCount { get; set; }

        /// <summary>作为发送方被接受次数</summary>
        public int AcceptedAsSenderCount { get; set; }

        /// <summary>成功率</summary>
        public double SuccessRate { get; set; }

        /// <summary>运行时间</summary>
        public TimeSpan Uptime { get; set; }
    }

    #endregion

    #region 接口定义

    /// <summary>
    /// S1F14处理器接口
    /// </summary>
    public interface IS1F14Handler
    {
        /// <summary>
        /// 发送通信确认
        /// </summary>
        Task<bool> SendCommunicationAcknowledgeAsync(
            bool accept,
            ushort targetDeviceId = 0,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取统计信息
        /// </summary>
        CommunicationStatisticsInfo GetStatistics();

        /// <summary>
        /// 重置统计
        /// </summary>
        void ResetStatistics();
    }

    #endregion
}
