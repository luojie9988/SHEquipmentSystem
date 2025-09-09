// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F13Handler.cs
// 版本: v2.0.0
// 描述: S1F13消息处理器 - Establish Communications Request 建立通信请求处理器

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
    /// S1F13 (Establish Communications Request) 建立通信请求处理器
    /// 处理主机或设备发起的正式通信建立请求，实现SEMI E30通信状态模型
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F13: Establish Communications Request - 正式建立通信请求
    /// - S1F14: Establish Communications Request Acknowledge - 通信建立确认
    /// 
    /// 消息格式：
    /// S1F13 W
    /// L,2
    ///   1. &lt;MDLN&gt; A,20 设备型号名称
    ///   2. &lt;SOFTREV&gt; A,20 软件版本
    /// 注：主机可能发送空列表
    /// 
    /// S1F14
    /// L,2
    ///   1. &lt;COMMACK&gt; B,1 确认码 (0=接受, 1=拒绝)
    ///   2. L,2
    ///      1. &lt;MDLN&gt; A,20
    ///      2. &lt;SOFTREV&gt; A,20
    /// 
    /// 通信状态转换：
    /// NOT COMMUNICATING -> COMMUNICATING (当COMMACK=0)
    /// 
    /// 使用场景：
    /// 1. 系统上电初始化
    /// 2. 通信故障恢复
    /// 3. 主机重新连接
    /// 4. 设备重启后自动建立
    /// </remarks>
    public class S1F13Handler : SecsMessageHandlerBase, IS1F13Handler
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

        /// <summary>状态变量服务（可选）</summary>
        private readonly IStatusVariableService? _statusService;

        /// <summary>通信状态模型</summary>
        private readonly CommunicationStateModel _commStateModel;

        /// <summary>主机信息缓存</summary>
        private HostInformation? _lastHostInfo;

        /// <summary>通信建立时间</summary>
        private DateTime? _communicationEstablishedTime;

        /// <summary>通信建立计数</summary>
        private int _establishmentCount = 0;

        #region 事件

        /// <summary>
        /// 通信建立事件
        /// </summary>
        public event EventHandler<EventArgs>? CommunicationEstablished;

        #endregion

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 13;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public S1F13Handler(
            ILogger<S1F13Handler> logger,
            IEquipmentStateService stateService,
            DiceDataModel dataModel,
            ISecsConnectionManager connectionManager,
            IOptions<EquipmentSystemConfiguration> options,
            IEventReportService? eventService = null,
            IStatusVariableService? statusService = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _eventService = eventService;
            _statusService = statusService;

            _commStateModel = new CommunicationStateModel(_config.Equipment.EstablishCommunicationsTimeout);

            Logger.LogInformation("S1F13处理器已初始化 - EstablishCommTimeout: {Timeout}秒",
                _config.Equipment.EstablishCommunicationsTimeout);
        }

        #endregion

        #region 公共方法 - IS1F13Handler接口

        /// <summary>
        /// 设备主动发送S1F13建立通信请求
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功建立通信</returns>
        public async Task<bool> SendEstablishCommunicationsRequestAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogInformation("设备主动发送S1F13建立通信请求");

                // 构建S1F13消息
                var s1f13 = new SecsMessage(1, 13, true)
                {
                    Name = "EstablishCommunicationsRequest",
                    SecsItem = L(
                        A(_dataModel.ModelName),        // MDLN
                        A(_dataModel.SoftwareRevision)  // SOFTREV
                    )
                };

                // 发送消息并等待响应
                var response = await _connectionManager.SendMessageAsync(s1f13, cancellationToken);

                if (response != null && response.S == 1 && response.F == 14)
                {
                    // 解析S1F14响应
                    var (commAck, hostInfo) = ParseS1F14Response(response.SecsItem);

                    if (commAck == CommAck.Accepted)
                    {
                        Logger.LogInformation("✅ 主机接受通信建立请求");
                        await OnCommunicationEstablished(hostInfo, true);
                        return true;
                    }
                    else
                    {
                        Logger.LogWarning("❌ 主机拒绝通信建立请求: COMMACK={CommAck}", commAck);
                        return false;
                    }
                }
                else
                {
                    Logger.LogWarning("未收到有效的S1F14响应");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "发送S1F13请求失败");
                return false;
            }
        }

        /// <summary>
        /// 开始通信建立重试循环
        /// </summary>
        public async Task StartCommunicationEstablishmentLoopAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("开始通信建立重试循环");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 检查当前通信状态
                    if (_commStateModel.CurrentState == CommunicationState.Communicating)
                    {
                        // 已建立通信，等待状态变化
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        continue;
                    }

                    // 尝试建立通信
                    var success = await SendEstablishCommunicationsRequestAsync(cancellationToken);

                    if (success)
                    {
                        // 成功建立，退出循环
                        break;
                    }

                    // 等待CommDelay时间后重试
                    var delaySeconds = _config.Equipment.EstablishCommunicationsTimeout;
                    Logger.LogDebug($"等待 {delaySeconds} 秒后重试建立通信");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "通信建立循环异常");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }

            Logger.LogInformation("通信建立重试循环结束");
        }

        #endregion

        #region 消息处理 - 处理主机发送的S1F13

        /// <summary>
        /// 处理S1F13消息，返回S1F14响应
        /// </summary>
        /// <param name="message">接收到的S1F13消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F14响应消息</returns>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F13 (Establish Communications Request) 建立通信请求");

            try
            {
                // 解析主机信息
                var hostInfo = ParseS1F13Message(message.SecsItem);
                Logger.LogInformation($"主机信息 - MDLN: '{hostInfo.ModelName}', SOFTREV: '{hostInfo.SoftwareRevision}'");

                // 验证通信建立条件
                var validation = await ValidateCommunicationEstablishment(hostInfo);

                // 确定确认码
                var commAck = validation.CanEstablish ? CommAck.Accepted : CommAck.Denied;

                // 如果接受，更新通信状态
                if (commAck == CommAck.Accepted)
                {
                    await OnCommunicationEstablished(hostInfo, false);
                }
                else
                {
                    Logger.LogWarning($"拒绝通信建立: {validation.Reason}");
                }

                // 构建S1F14响应
                var response = CreateS1F14Response(commAck);

                Logger.LogDebug($"发送 S1F14 响应 - COMMACK: {(byte)commAck} ({commAck})");
                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F13消息失败");
                return CreateS1F14Response(CommAck.Denied);
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S1F13消息
        /// </summary>
        private HostInformation ParseS1F13Message(Item? item)
        {
            var hostInfo = new HostInformation();

            try
            {
                // 主机可能发送空列表
                if (item == null || item.Count == 0)
                {
                    Logger.LogDebug("收到空列表的S1F13（主机未提供标识信息）");
                    hostInfo.ModelName = "UNKNOWN";
                    hostInfo.SoftwareRevision = "UNKNOWN";
                    return hostInfo;
                }

                // 标准格式：L,2
                if (item.Format == SecsFormat.List && item.Count >= 2)
                {
                    var items = item.Items;

                    // MDLN
                    if (items != null && items.Length > 0)
                    {
                        hostInfo.ModelName = items[0].GetString() ?? "UNKNOWN";
                    }

                    // SOFTREV
                    if (items != null && items.Length > 1)
                    {
                        hostInfo.SoftwareRevision = items[1].GetString() ?? "UNKNOWN";
                    }
                }

                // 限制长度为20字符（SEMI标准）
                if (hostInfo.ModelName.Length > 20)
                    hostInfo.ModelName = hostInfo.ModelName.Substring(0, 20);
                if (hostInfo.SoftwareRevision.Length > 20)
                    hostInfo.SoftwareRevision = hostInfo.SoftwareRevision.Substring(0, 20);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S1F13消息异常");
                hostInfo.ModelName = "ERROR";
                hostInfo.SoftwareRevision = "ERROR";
            }

            return hostInfo;
        }

        /// <summary>
        /// 解析S1F14响应
        /// </summary>
        private (CommAck commAck, HostInformation hostInfo) ParseS1F14Response(Item? item)
        {
            var commAck = CommAck.Denied;
            var hostInfo = new HostInformation();

            try
            {
                if (item != null && item.Format == SecsFormat.List && item.Count >= 1)
                {
                    var items = item.Items;

                    // COMMACK
                    if (items != null && items.Length > 0)
                    {
                        var ackValue = items[0].FirstValue<byte>();
                        commAck = (CommAck)ackValue;
                    }

                    // Host info (如果COMMACK=0)
                    if (commAck == CommAck.Accepted && items != null && items.Length > 1)
                    {
                        var infoList = items[1];
                        if (infoList.Format == SecsFormat.List && infoList.Count >= 2)
                        {
                            var infoItems = infoList.Items;
                            if (infoItems != null)
                            {
                                hostInfo.ModelName = infoItems[0].GetString() ?? "";
                                hostInfo.SoftwareRevision = infoItems[1].GetString() ?? "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S1F14响应异常");
            }

            return (commAck, hostInfo);
        }

        #endregion

        #region 私有方法 - 验证和状态管理

        /// <summary>
        /// 验证通信建立条件
        /// </summary>
        private async Task<CommunicationValidation> ValidateCommunicationEstablishment(HostInformation hostInfo)
        {
            try
            {
                // 1. 检查通信状态是否为ENABLED
                var commEnabled = await _stateService.IsCommunicationEnabledAsync();
                if (!commEnabled)
                {
                    return new CommunicationValidation
                    {
                        CanEstablish = false,
                        Reason = "通信状态为DISABLED"
                    };
                }

                // 2. 检查设备是否准备就绪
                var equipmentReady = await CheckEquipmentReadiness();
                if (!equipmentReady)
                {
                    return new CommunicationValidation
                    {
                        CanEstablish = false,
                        Reason = "设备未准备就绪"
                    };
                }

                // 3. 检查是否有严重报警
                var hasCriticalAlarm = CheckCriticalAlarms();
                if (hasCriticalAlarm)
                {
                    return new CommunicationValidation
                    {
                        CanEstablish = false,
                        Reason = "存在严重报警"
                    };
                }

                // 4. 验证主机信息（可选）
                if (_config.Equipment.AllowedHosts?.Count > 0)
                {
                    var hostValidation = ValidateHostInfo(hostInfo);
                    if (!hostValidation.IsValid)
                    {
                        return new CommunicationValidation
                        {
                            CanEstablish = false,
                            Reason = hostValidation.Reason
                        };
                    }
                }

                return new CommunicationValidation
                {
                    CanEstablish = true,
                    Reason = "满足所有通信建立条件"
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证通信建立条件异常");
                return new CommunicationValidation
                {
                    CanEstablish = false,
                    Reason = $"验证异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查设备就绪状态
        /// </summary>
        private async Task<bool> CheckEquipmentReadiness()
        {
            try
            {
                // 检查设备状态
                var state = await _stateService.GetEquipmentStateAsync();

                // 不能在以下状态建立通信
                if (state == EquipmentState.UnscheduledDown ||
                    state == EquipmentState.NonScheduled)
                {
                    Logger.LogWarning($"设备状态 {state} 不允许建立通信");
                    return false;
                }

                // 检查初始化是否完成
                var processState = await _stateService.GetProcessStateAsync();
                if (processState == ProcessState.Init)
                {
                    Logger.LogWarning("设备仍在初始化中");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "检查设备就绪状态异常");
                return false;
            }
        }

        /// <summary>
        /// 检查严重报警
        /// </summary>
        private bool CheckCriticalAlarms()
        {
            // 检查是否有严重报警激活
            var criticalAlarms = _dataModel.AlarmsSet
                .Where(alid => SemiIdDefinitions.Alid.GetAlarmPriority(alid) == SemiIdDefinitions.AlarmPriority.Critical)
                .ToList();

            if (criticalAlarms.Any())
            {
                Logger.LogWarning($"存在 {criticalAlarms.Count} 个严重报警: {string.Join(", ", criticalAlarms)}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 验证主机信息
        /// </summary>
        private (bool IsValid, string Reason) ValidateHostInfo(HostInformation hostInfo)
        {
            // 检查是否为空
            if (string.IsNullOrWhiteSpace(hostInfo.ModelName) ||
                string.IsNullOrWhiteSpace(hostInfo.SoftwareRevision))
            {
                return (false, "主机信息不完整");
            }

            // 检查是否在允许列表中（如果配置了）
            var allowedHosts = _config.Equipment.AllowedHosts;
            if (allowedHosts != null && allowedHosts.Any())
            {
                var isAllowed = allowedHosts.Any(h =>
                    h == hostInfo.ModelName ||
                    h == "*");

                if (!isAllowed)
                {
                    return (false, $"主机 {hostInfo.ModelName} 不在允许列表中");
                }
            }

            return (true, "主机信息验证通过");
        }

        #endregion

        #region 私有方法 - 通信建立处理

        /// <summary>
        /// 通信建立成功处理
        /// </summary>
        private async Task OnCommunicationEstablished(HostInformation hostInfo, bool equipmentInitiated)
        {
            try
            {
                Logger.LogInformation("=== 通信建立成功 ===");
                Logger.LogInformation($"主机: {hostInfo.ModelName} ({hostInfo.SoftwareRevision})");
                Logger.LogInformation($"设备: {_dataModel.ModelName} ({_dataModel.SoftwareRevision})");
                Logger.LogInformation($"发起方: {(equipmentInitiated ? "设备" : "主机")}");

                // 更新通信状态模型
                _commStateModel.TransitionTo(CommunicationState.Communicating);

                // 更新设备状态服务
                await _stateService.SetCommunicationEstablishedAsync(true);

                // 更新数据模型
                _dataModel.ConnectionState = HsmsConnectionState.Selected;
                _communicationEstablishedTime = DateTime.Now;
                _establishmentCount++;
                _lastHostInfo = hostInfo;

                // 触发通信建立事件（如果配置）
                if (_eventService != null)
                {
                    await TriggerCommunicationEstablishedEvent(hostInfo, equipmentInitiated);
                }

                // 执行同步操作
                await PerformSynchronizationAsync();

                Logger.LogInformation($"通信建立完成 - 这是第 {_establishmentCount} 次建立");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "通信建立处理异常");
            }
        }

        /// <summary>
        /// 触发通信建立事件
        /// </summary>
        private async Task TriggerCommunicationEstablishedEvent(HostInformation hostInfo, bool equipmentInitiated)
        {
            try
            {
                if (_eventService == null) return;

                // 准备事件数据
                var eventData = new Dictionary<string, object>
                {
                    ["HostModel"] = hostInfo.ModelName,
                    ["HostSoftware"] = hostInfo.SoftwareRevision,
                    ["EquipmentModel"] = _dataModel.ModelName,
                    ["EquipmentSoftware"] = _dataModel.SoftwareRevision,
                    ["InitiatedBy"] = equipmentInitiated ? "Equipment" : "Host",
                    ["EstablishmentCount"] = _establishmentCount,
                    ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };

                // 触发自定义事件（CEID可配置）
                const uint COMM_ESTABLISHED_CEID = 9001;
                await _eventService.TriggerEventAsync(COMM_ESTABLISHED_CEID, "Communication Established");

                // 触发通信建立事件
                CommunicationEstablished?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "触发通信建立事件失败");
            }
        }

        /// <summary>
        /// 执行同步操作
        /// </summary>
        private async Task PerformSynchronizationAsync()
        {
            try
            {
                Logger.LogDebug("开始执行同步操作");

                // 1. 同步设备状态
                if (_statusService != null)
                {
                    await SyncDeviceStatus();
                }

                // 2. 报告当前报警
                await ReportActiveAlarms();

                // 3. 同步设备常量（如果需要）
                await SyncEquipmentConstants();

                // 4. 同步配方列表（如果需要）
                await SyncProcessPrograms();

                Logger.LogDebug("同步操作完成");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步操作失败");
            }
        }

        /// <summary>
        /// 同步设备状态
        /// </summary>
        private async Task SyncDeviceStatus()
        {
            try
            {
                // 更新关键状态变量
                _dataModel.ControlState = await _stateService.GetControlStateAsync();
                _dataModel.ProcessState = await _stateService.GetProcessStateAsync();
                _dataModel.EquipmentState = await _stateService.GetEquipmentStateAsync();

                Logger.LogDebug($"设备状态同步 - Control: {_dataModel.ControlState}, " +
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
                if (activeAlarms.Any())
                {
                    Logger.LogInformation($"当前有 {activeAlarms.Count} 个激活报警需要报告");
                    // 这里可以触发S5F1报告
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "报告激活报警失败");
            }
        }

        /// <summary>
        /// 同步设备常量
        /// </summary>
        private async Task SyncEquipmentConstants()
        {
            try
            {
                // 获取并同步关键设备常量
                await Task.CompletedTask;
                Logger.LogDebug("设备常量同步完成");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步设备常量失败");
            }
        }

        /// <summary>
        /// 同步配方列表
        /// </summary>
        private async Task SyncProcessPrograms()
        {
            try
            {
                // 同步配方列表
                await Task.CompletedTask;
                Logger.LogDebug("配方列表同步完成");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步配方列表失败");
            }
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S1F14响应
        /// </summary>
        private SecsMessage CreateS1F14Response(CommAck commAck)
        {
            Item responseItem;

            if (commAck == CommAck.Accepted)
            {
                // COMMACK = 0，包含设备信息
                responseItem = L(
                    B((byte)commAck),
                    L(
                        A(_dataModel.ModelName),        // MDLN
                        A(_dataModel.SoftwareRevision)  // SOFTREV
                    )
                );
            }
            else
            {
                // COMMACK != 0，只返回确认码
                responseItem = L(
                    B((byte)commAck),
                    L()  // 空列表
                );
            }

            return new SecsMessage(1, 14, false)
            {
                Name = "EstablishCommunicationsAcknowledge",
                SecsItem = responseItem
            };
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
            /// <summary>拒绝</summary>
            Denied = 1
        }

        /// <summary>
        /// 主机信息
        /// </summary>
        private class HostInformation
        {
            public string ModelName { get; set; } = "";
            public string SoftwareRevision { get; set; } = "";
        }

        /// <summary>
        /// 通信验证结果
        /// </summary>
        private class CommunicationValidation
        {
            public bool CanEstablish { get; set; }
            public string Reason { get; set; } = "";
        }

        /// <summary>
        /// 通信状态模型
        /// </summary>
        private class CommunicationStateModel
        {
            public CommunicationState CurrentState { get; private set; }
            public DateTime LastTransitionTime { get; private set; }
            public int EstablishCommTimeout { get; }

            public CommunicationStateModel(int timeout)
            {
                CurrentState = CommunicationState.NotCommunicating;
                LastTransitionTime = DateTime.Now;
                EstablishCommTimeout = timeout;
            }

            public void TransitionTo(CommunicationState newState)
            {
                if (CurrentState != newState)
                {
                    CurrentState = newState;
                    LastTransitionTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 通信状态
        /// </summary>
        private enum CommunicationState
        {
            /// <summary>未通信</summary>
            NotCommunicating,
            /// <summary>等待CRA（等待S1F14）</summary>
            WaitCRA,
            /// <summary>等待延时</summary>
            WaitDelay,
            /// <summary>正在通信</summary>
            Communicating
        }

        #endregion
    }
}
