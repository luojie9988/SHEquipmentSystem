// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F2Handler.cs
// 版本: v1.0.0
// 描述: S1F2消息处理器 - On Line Data 在线数据响应处理器

using System;
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
    /// S1F2 (On Line Data) 在线数据处理器
    /// 响应主机的S1F1设备标识查询，返回设备基本信息
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F1: Are You There - 主机发送的设备在线检测请求
    /// - S1F2: On Line Data - 设备返回的在线标识数据
    /// 
    /// 交互流程：
    /// 1. 主机发送S1F1空列表或包含设备ID的请求
    /// 2. 设备验证自身状态和通信能力
    /// 3. 返回S1F2包含设备型号(MDLN)和软件版本(SOFTREV)
    /// 4. 用于主机验证设备身份和确认通信正常
    /// 
    /// 业务价值：
    /// - 提供设备身份验证功能
    /// - 支持主机端的设备在线状态检测
    /// - 在通信异常后验证设备是否正常响应
    /// - 为后续的通信建立(S1F13/F14)提供基础
    /// </remarks>
    public class S1F2Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>设备状态管理服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>设备配置信息</summary>
        private readonly EquipmentConfiguration _equipmentConfig;

        /// <summary>状态变量服务，用于获取设备运行状态</summary>
        private readonly IStatusVariableService _statusService;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 2;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="statusService">状态变量服务</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">参数为空时抛出异常</exception>
        public S1F2Handler(
            ILogger<S1F2Handler> logger,
            IEquipmentStateService stateService,
            IStatusVariableService statusService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _equipmentConfig = options?.Value?.Equipment ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S1F2消息请求，返回在线数据响应
        /// </summary>
        /// <param name="message">接收到的请求消息（通常来自S1F1）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F2在线数据响应消息</returns>
        /// <remarks>
        /// S1F2处理逻辑：
        /// 1. 验证设备是否处于可响应状态
        /// 2. 获取设备当前运行状态信息
        /// 3. 构建包含设备标识的标准响应
        /// 4. 确保MDLN和SOFTREV符合20字符限制
        /// 5. 记录响应发送日志用于诊断
        /// 
        /// 注意：此处理器既可以响应S1F1请求，也可以作为主动发送S1F2的接口
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug("开始处理S1F2在线数据请求");

            try
            {
                // 验证设备状态
                var deviceStatus = await ValidateDeviceStatusAsync();
                if (!deviceStatus.CanRespond)
                {
                    Logger.LogWarning($"设备状态不允许响应S1F2: {deviceStatus.Reason}");
                    return CreateErrorResponse(deviceStatus.Reason);
                }

                // 获取设备运行状态信息
                var runtimeInfo = await CollectDeviceRuntimeInfoAsync();

                // 构建S1F2响应
                var response = CreateS1F2OnlineDataResponse(runtimeInfo);

                Logger.LogInformation($"✅ S1F2响应已生成: MDLN='{runtimeInfo.ModelName}', " +
                                    $"SOFTREV='{runtimeInfo.SoftwareRevision}', " +
                                    $"State={runtimeInfo.CurrentState}");

                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F2在线数据请求时发生异常");
                return CreateErrorResponse("内部处理异常");
            }
        }

        #endregion

        #region 私有方法 - 状态验证

        /// <summary>
        /// 验证设备状态是否可以响应S1F2
        /// </summary>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// 验证条件：
        /// 1. 设备不处于严重错误状态
        /// 2. SECS通信模块正常运行
        /// 3. 基础硬件系统无致命故障
        /// </remarks>
        private async Task<DeviceResponseCapability> ValidateDeviceStatusAsync()
        {
            try
            {
                // 获取当前控制状态
                var controlState = await _stateService.GetControlStateAsync();
                var equipmentState = await _stateService.GetEquipmentStateAsync();

                // 检查是否处于致命错误状态
                if (equipmentState == EquipmentState.Error)
                {
                    return DeviceResponseCapability.CreateDenied("设备处于错误状态");
                }

                // 检查SECS通信状态
                var communicationEstablished = await _stateService.IsCommunicationEstablishedAsync();
                if (!communicationEstablished && controlState == ControlState.EquipmentOffline)
                {
                    Logger.LogDebug("设备离线状态，但仍可响应S1F2在线标识请求");
                }

                return DeviceResponseCapability.CreateAllowed($"设备状态正常，控制状态: {controlState}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证设备响应能力时发生异常");
                return DeviceResponseCapability.CreateDenied($"状态验证异常: {ex.Message}");
            }
        }

        #endregion

        #region 私有方法 - 数据收集

        /// <summary>
        /// 收集设备运行时信息
        /// </summary>
        /// <returns>设备运行时信息</returns>
        /// <remarks>
        /// 收集的信息包括：
        /// 1. 基本标识信息（型号、版本）
        /// 2. 当前运行状态
        /// 3. 关键性能指标
        /// 4. 时间戳信息
        /// </remarks>
        private async Task<DeviceRuntimeInfo> CollectDeviceRuntimeInfoAsync()
        {
            var runtimeInfo = new DeviceRuntimeInfo();

            try
            {
                // 基本设备标识 - 确保符合SEMI标准长度限制
                runtimeInfo.ModelName = TruncateToSemiLimit(_equipmentConfig.ModelName, 20);
                runtimeInfo.SoftwareRevision = TruncateToSemiLimit(_equipmentConfig.SoftwareRevision, 20);
                runtimeInfo.EquipmentName = _equipmentConfig.EquipmentName;

                // 获取当前状态信息
                var statusInfo = await _stateService.GetStatusInfoAsync();
                runtimeInfo.CurrentState = statusInfo.ControlState.ToString();
                runtimeInfo.IsOnline = statusInfo.IsOnline;
                runtimeInfo.IsRemoteEnabled = statusInfo.ControlState == ControlState.OnlineRemote;

                // 获取关键状态变量值
                try
                {
                    // SVID 720 - 控制模式
                    var controlModeValue = await _statusService.GetStatusVariableAsync(720);
                    runtimeInfo.ControlMode = controlModeValue?.ToString() ?? "Unknown";

                    // SVID 721 - 控制状态  
                    var controlStateValue = await _statusService.GetStatusVariableAsync(721);
                    runtimeInfo.ControlState = controlStateValue?.ToString() ?? "Unknown";
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "获取状态变量值时发生异常，使用默认值");
                    runtimeInfo.ControlMode = "Unknown";
                    runtimeInfo.ControlState = "Unknown";
                }

                // 系统时间戳
                runtimeInfo.ResponseTime = DateTime.Now;

                Logger.LogDebug($"设备运行时信息收集完成: " +
                              $"State={runtimeInfo.CurrentState}, " +
                              $"Online={runtimeInfo.IsOnline}, " +
                              $"Remote={runtimeInfo.IsRemoteEnabled}");

                return runtimeInfo;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "收集设备运行时信息时发生异常");

                // 返回最基本的安全信息
                return new DeviceRuntimeInfo
                {
                    ModelName = TruncateToSemiLimit(_equipmentConfig.ModelName, 20),
                    SoftwareRevision = TruncateToSemiLimit(_equipmentConfig.SoftwareRevision, 20),
                    CurrentState = "Unknown",
                    ResponseTime = DateTime.Now
                };
            }
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S1F2在线数据响应消息
        /// </summary>
        /// <param name="runtimeInfo">设备运行时信息</param>
        /// <returns>S1F2响应消息</returns>
        /// <remarks>
        /// S1F2消息结构 (SEMI E5标准):
        /// L,2
        /// 1. &lt;MDLN&gt; type A[20] - 设备型号名称，最大20字符
        /// 2. &lt;SOFTREV&gt; type A[20] - 软件版本号，最大20字符
        /// 
        /// 扩展信息（可选，根据设备需求）：
        /// - 可以在后续版本中扩展为包含更多设备状态的复合响应
        /// - 当前版本严格遵循SEMI E30标准的最小实现
        /// </remarks>
        private SecsMessage CreateS1F2OnlineDataResponse(DeviceRuntimeInfo runtimeInfo)
        {
            try
            {
                var response = new SecsMessage(1, 2, replyExpected: false)
                {
                    Name = "On Line Data",
                    SecsItem = Item.L(
                        Item.A(runtimeInfo.ModelName),        // MDLN - 设备型号
                        Item.A(runtimeInfo.SoftwareRevision)  // SOFTREV - 软件版本
                    )
                };

                Logger.LogDebug($"S1F2响应构建成功: " +
                              $"MDLN='{runtimeInfo.ModelName}' ({runtimeInfo.ModelName.Length}字符), " +
                              $"SOFTREV='{runtimeInfo.SoftwareRevision}' ({runtimeInfo.SoftwareRevision.Length}字符)");

                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "构建S1F2响应时发生异常");
                throw new InvalidOperationException("S1F2响应构建失败", ex);
            }
        }

        /// <summary>
        /// 创建错误响应消息
        /// </summary>
        /// <param name="errorReason">错误原因</param>
        /// <returns>S1F2错误响应消息</returns>
        /// <remarks>
        /// 在设备无法正常响应时，仍需返回基本的S1F2格式
        /// 使用预定义的错误标识，确保主机能够识别异常情况
        /// </remarks>
        private SecsMessage CreateErrorResponse(string errorReason)
        {
            Logger.LogWarning($"创建S1F2错误响应: {errorReason}");

            try
            {
                var errorResponse = new SecsMessage(1, 2, replyExpected: false)
                {
                    Name = "On Line Data (Error)",
                    SecsItem = Item.L(
                        Item.A("ERROR"),    // 错误标识 - 指示设备响应异常
                        Item.A("UNKNOWN")   // 未知版本 - 指示软件状态不明
                    )
                };

                return errorResponse;
            }
            catch (Exception ex)
            {
                Logger.LogCritical(ex, "构建S1F2错误响应失败");

                // 最后兜底：创建最简单的有效S1F2
                return new SecsMessage(1, 2, replyExpected: false)
                {
                    SecsItem = Item.L(Item.A("FAIL"), Item.A("FAIL"))
                };
            }
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 截断字符串到SEMI标准限制长度
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <param name="maxLength">最大允许长度</param>
        /// <returns>符合长度限制的字符串</returns>
        /// <remarks>
        /// SEMI E5标准要求：
        /// - MDLN最大20字符
        /// - SOFTREV最大20字符
        /// 超长字符串会导致设备返回S9F7 Illegal Data错误
        /// </remarks>
        private string TruncateToSemiLimit(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            var truncated = value.Substring(0, maxLength);
            Logger.LogWarning($"字符串长度超限：原始='{value}' ({value.Length}字符) -> " +
                            $"截断='{truncated}' ({maxLength}字符)");

            return truncated;
        }

        /// <summary>
        /// 验证字符串是否符合ASCII要求
        /// </summary>
        /// <param name="value">待验证字符串</param>
        /// <returns>是否为有效ASCII字符串</returns>
        /// <remarks>
        /// SECS-II要求所有A类型数据必须为ASCII字符
        /// 非ASCII字符会导致消息解析错误
        /// </remarks>
        private bool IsValidAsciiString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            foreach (char c in value)
            {
                if (c > 127) // 非ASCII字符
                {
                    Logger.LogWarning($"发现非ASCII字符: '{c}' (0x{(int)c:X2}) 在字符串 '{value}' 中");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 清理字符串，移除非ASCII字符
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <returns>清理后的ASCII字符串</returns>
        private string SanitizeToAscii(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (IsValidAsciiString(value))
                return value;

            var cleaned = new string(value.Where(c => c <= 127).ToArray());
            Logger.LogWarning($"字符串ASCII清理：'{value}' -> '{cleaned}'");

            return cleaned;
        }

        #endregion

        #region 辅助类定义

        /// <summary>
        /// 设备响应能力评估结果
        /// </summary>
        private class DeviceResponseCapability
        {
            /// <summary>是否可以响应</summary>
            public bool CanRespond { get; set; }

            /// <summary>状态说明</summary>
            public string Reason { get; set; } = string.Empty;

            /// <summary>创建允许响应的结果</summary>
            public static DeviceResponseCapability CreateAllowed(string reason) =>
                new() { CanRespond = true, Reason = reason };

            /// <summary>创建拒绝响应的结果</summary>
            public static DeviceResponseCapability CreateDenied(string reason) =>
                new() { CanRespond = false, Reason = reason };
        }

        /// <summary>
        /// 设备运行时信息
        /// </summary>
        private class DeviceRuntimeInfo
        {
            /// <summary>设备型号名称</summary>
            public string ModelName { get; set; } = string.Empty;

            /// <summary>软件版本号</summary>
            public string SoftwareRevision { get; set; } = string.Empty;

            /// <summary>设备名称</summary>
            public string EquipmentName { get; set; } = string.Empty;

            /// <summary>当前状态描述</summary>
            public string CurrentState { get; set; } = string.Empty;

            /// <summary>是否在线</summary>
            public bool IsOnline { get; set; }

            /// <summary>是否启用远程控制</summary>
            public bool IsRemoteEnabled { get; set; }

            /// <summary>控制模式</summary>
            public string ControlMode { get; set; } = string.Empty;

            /// <summary>控制状态</summary>
            public string ControlState { get; set; } = string.Empty;

            /// <summary>响应时间戳</summary>
            public DateTime ResponseTime { get; set; }
        }

        #endregion

        #region 公共方法 - 主动发送接口

        /// <summary>
        /// 主动发送S1F2在线数据（用于设备主动标识）
        /// </summary>
        /// <returns>S1F2消息，可用于主动发送给主机</returns>
        /// <remarks>
        /// 使用场景：
        /// 1. 设备启动后主动向主机标识自己
        /// 2. 网络恢复后重新标识设备身份
        /// 3. 响应主机的设备发现请求
        /// 4. 定期心跳检测中的身份确认
        /// </remarks>
        public async Task<SecsMessage> CreateActiveS1F2MessageAsync()
        {
            Logger.LogDebug("创建主动S1F2在线数据消息");

            try
            {
                // 收集当前设备信息
                var runtimeInfo = await CollectDeviceRuntimeInfoAsync();

                // 创建主动发送的S1F2
                var activeMessage = CreateS1F2OnlineDataResponse(runtimeInfo);

                Logger.LogInformation($"主动S1F2消息创建成功，设备标识: " +
                                    $"{runtimeInfo.ModelName} v{runtimeInfo.SoftwareRevision}");

                return activeMessage;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建主动S1F2消息失败");
                throw new InvalidOperationException("主动S1F2消息创建失败", ex);
            }
        }

        #endregion
    }
}
