// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F13Handler.cs
// 版本: v1.0.0
// 描述: S1F13消息处理器 - Establish Communications Request 建立通信请求处理器

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
    /// S1F13 (Establish Communications Request) 处理器
    /// 处理主机的通信建立请求，返回通信确认响应
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F13: 建立通信请求 - 主机发送的正式通信初始化请求
    /// - S1F14: 建立通信确认 - 设备返回的确认或拒绝响应
    /// 
    /// 交互流程：
    /// 1. 主机发送 S1F13 包含主机的 MDLN 和 SOFTREV
    /// 2. 设备验证通信建立条件
    /// 3. 返回 S1F14 包含 COMMACK 和设备信息
    /// 4. 如果接受，通信状态变为 COMMUNICATING
    /// </remarks>
    public class S1F13Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        private readonly IEquipmentStateService _stateService;
        private readonly EquipmentConfiguration _equipmentConfig;

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
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">参数为空时抛出异常</exception>
        public S1F13Handler(
            ILogger<S1F13Handler> logger,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _equipmentConfig = options?.Value?.Equipment ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S1F13 消息，返回 S1F14 响应
        /// </summary>
        /// <param name="message">接收到的S1F13消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F14响应消息</returns>
        /// <remarks>
        /// S1F13 处理逻辑：
        /// 1. 解析主机标识信息（MDLN, SOFTREV）
        /// 2. 验证设备是否可以建立通信
        /// 3. 构建包含确认码和设备信息的S1F14响应
        /// 4. 更新设备通信状态
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F13 (Establish Communications Request) 建立通信请求");

            try
            {
                // 解析主机信息
                var hostInfo = ParseHostInformation(message.SecsItem);
                Logger.LogInformation($"主机信息: MDLN='{hostInfo.ModelName}', SOFTREV='{hostInfo.SoftwareRevision}'");

                // 验证通信建立条件
                var validationResult = await ValidateCommEstablishment();
                Logger.LogDebug($"通信建立验证结果: {validationResult.CanEstablish} - {validationResult.Reason}");

                // 确定确认码
                var commAck = validationResult.CanEstablish ? CommAck.Accepted : CommAck.Denied;

                // 更新设备状态（如果接受）
                if (commAck == CommAck.Accepted)
                {
                    await _stateService.SetCommunicationEstablishedAsync(true);
                    Logger.LogInformation("✅ 通信建立成功，设备进入 COMMUNICATING 状态");
                }
                else
                {
                    Logger.LogWarning($"❌ 通信建立被拒绝: {validationResult.Reason}");
                }

                // 构建 S1F14 响应
                var response = CreateS1F14Response(commAck);

                Logger.LogDebug($"发送 S1F14 响应: COMMACK={(byte)commAck} ({commAck})");
                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理 S1F13 消息时发生异常");
                return CreateErrorResponse();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 解析主机标识信息
        /// </summary>
        /// <param name="item">消息数据项</param>
        /// <returns>主机信息</returns>
        /// <remarks>
        /// S1F13 消息结构:
        /// L,2
        /// 1. &lt;MDLN&gt; type A - 主机型号名称
        /// 2. &lt;SOFTREV&gt; type A - 主机软件版本
        /// 
        /// 特例：主机可能发送空列表
        /// </remarks>
        private HostInformation ParseHostInformation(Item? item)
        {
            var hostInfo = new HostInformation();

            try
            {
                if (item == null)
                {
                    Logger.LogDebug("S1F13 包含空数据项");
                    return hostInfo;
                }

                if (item.Count == 0)
                {
                    Logger.LogDebug("S1F13 包含空列表（主机特例）");
                    return hostInfo;
                }

                if (item.Count >= 2)
                {
                    // 解析主机型号
                    var mdlnItem = item.Items[0];
                    if (mdlnItem != null)
                    {
                        hostInfo.ModelName = mdlnItem.GetString()?.Trim() ?? "";
                    }

                    // 解析软件版本
                    var softrevItem = item.Items[1];
                    if (softrevItem != null)
                    {
                        hostInfo.SoftwareRevision = softrevItem.GetString()?.Trim() ?? "";
                    }

                    Logger.LogDebug($"成功解析主机信息: MDLN='{hostInfo.ModelName}', SOFTREV='{hostInfo.SoftwareRevision}'");
                }
                else
                {
                    Logger.LogWarning($"S1F13 数据项数量不足: {item.Count}，期望至少2个");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "解析主机信息时发生异常，使用默认值");
            }

            return hostInfo;
        }

        /// <summary>
        /// 验证通信建立条件
        /// </summary>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// 验证项目：
        /// 1. 设备状态是否允许建立通信
        /// 2. 设备是否处于故障或维护状态
        /// 3. 通信系统是否就绪
        /// </remarks>
        private async Task<CommEstablishmentValidation> ValidateCommEstablishment()
        {
            try
            {
                // 检查设备基本状态
                var canEstablish = await _stateService.CanEstablishCommunicationAsync();
                if (!canEstablish)
                {
                    return new CommEstablishmentValidation
                    {
                        CanEstablish = false,
                        Reason = "设备状态不允许建立通信"
                    };
                }

                // 检查设备当前状态信息
                var statusInfo = await _stateService.GetStatusInfoAsync();

                // 如果设备处于故障状态，拒绝通信
                if (statusInfo.EquipmentState == EquipmentState.UnscheduledDown)
                {
                    return new CommEstablishmentValidation
                    {
                        CanEstablish = false,
                        Reason = "设备处于故障状态"
                    };
                }

                // 如果已经建立通信，仍然接受（重新建立）
                if (statusInfo.IsCommunicationEstablished)
                {
                    Logger.LogDebug("通信已建立，将重新建立");
                }

                return new CommEstablishmentValidation
                {
                    CanEstablish = true,
                    Reason = "满足通信建立条件"
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证通信建立条件时发生异常");
                return new CommEstablishmentValidation
                {
                    CanEstablish = false,
                    Reason = $"验证过程异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 创建 S1F14 (Establish Communications Acknowledge) 响应消息
        /// </summary>
        /// <param name="commAck">通信确认码</param>
        /// <returns>S1F14响应消息</returns>
        /// <remarks>
        /// S1F14 消息结构 (SEMI E30标准):
        /// L,2
        /// 1. &lt;COMMACK&gt; type B - 通信确认码 (0=接受, 1=拒绝)
        /// 2. L,2 - 设备信息列表
        ///    1. &lt;MDLN&gt; type A - 设备型号
        ///    2. &lt;SOFTREV&gt; type A - 设备软件版本
        /// 
        /// 注意：MDLN和SOFTREV仅在COMMACK=0时有效
        /// </remarks>
        private SecsMessage CreateS1F14Response(CommAck commAck)
        {
            // 确保设备信息符合SEMI标准长度限制（最大20字符）
            var deviceMdln = TruncateToMaxLength(_equipmentConfig.ModelName, 20);
            var deviceSoftrev = TruncateToMaxLength(_equipmentConfig.SoftwareRevision, 20);

            // 构建设备信息列表
            Item deviceInfoList;
            if (commAck == CommAck.Accepted)
            {
                // 通信接受时提供完整设备信息
                deviceInfoList = Item.L(
                    Item.A(deviceMdln),       // 设备型号
                    Item.A(deviceSoftrev)     // 设备软件版本
                );
                Logger.LogDebug($"包含设备信息: MDLN='{deviceMdln}', SOFTREV='{deviceSoftrev}'");
            }
            else
            {
                // 通信拒绝时返回空列表
                deviceInfoList = Item.L();
                Logger.LogDebug("通信被拒绝，返回空设备信息列表");
            }

            // 构建 S1F14 响应消息
            var s1f14 = new SecsMessage(1, 14, replyExpected: false)
            {
                Name = "Establish Communications Acknowledge",
                SecsItem = Item.L(
                    Item.B((byte)commAck),    // COMMACK
                    deviceInfoList            // 设备信息
                )
            };

            Logger.LogDebug($"构建 S1F14 响应成功: COMMACK={(byte)commAck}");
            return s1f14;
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        /// <returns>错误响应消息</returns>
        private SecsMessage CreateErrorResponse()
        {
            Logger.LogWarning("创建 S1F14 错误响应");

            return new SecsMessage(1, 14, replyExpected: false)
            {
                Name = "Establish Communications Acknowledge (Error)",
                SecsItem = Item.L(
                    Item.B((byte)CommAck.Denied),  // 拒绝
                    Item.L()                       // 空设备信息
                )
            };
        }

        /// <summary>
        /// 截断字符串到指定最大长度
        /// </summary>
        /// <param name="value">原始字符串</param>
        /// <param name="maxLength">最大长度</param>
        /// <returns>截断后的字符串</returns>
        private string TruncateToMaxLength(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= maxLength)
                return value;

            var truncated = value.Substring(0, maxLength);
            Logger.LogWarning($"字符串 '{value}' 超过最大长度 {maxLength}，已截断为 '{truncated}'");
            return truncated;
        }

        #endregion

        #region 辅助类和枚举

        /// <summary>
        /// 主机信息
        /// </summary>
        /// <remarks>
        /// 封装从S1F13消息中解析的主机标识信息
        /// </remarks>
        private class HostInformation
        {
            /// <summary>
            /// 主机型号名称
            /// </summary>
            public string ModelName { get; set; } = "";

            /// <summary>
            /// 主机软件版本
            /// </summary>
            public string SoftwareRevision { get; set; } = "";
        }

        /// <summary>
        /// 通信建立验证结果
        /// </summary>
        /// <remarks>
        /// 封装设备是否可以建立通信的验证结果
        /// </remarks>
        private class CommEstablishmentValidation
        {
            /// <summary>
            /// 是否可以建立通信
            /// </summary>
            public bool CanEstablish { get; set; }

            /// <summary>
            /// 验证结果说明
            /// </summary>
            public string Reason { get; set; } = "";
        }

        #endregion
    }
}
