// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F1Handler.cs
// 版本: v1.0.0
// 描述: S1F1消息处理器 - Are You There 请求处理器

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S1F1 (Are You There) 处理器
    /// 处理主机的通信连接测试请求，返回设备在线数据
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F1: Are You There - 主机发送的连接测试请求
    /// - S1F2: On Line Data - 设备返回的在线标识数据
    /// 
    /// 交互流程：
    /// 1. 主机发送 S1F1 空列表
    /// 2. 设备验证自身状态 
    /// 3. 返回 S1F2 包含 MDLN 和 SOFTREV
    /// </remarks>
    public class S1F1Handler : SecsMessageHandlerBase
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
        public override byte Function => 1;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">参数为空时抛出异常</exception>
        public S1F1Handler(
            ILogger<S1F1Handler> logger,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _equipmentConfig = options?.Value?.Equipment ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S1F1 消息，返回 S1F2 响应
        /// </summary>
        /// <param name="message">接收到的S1F1消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F2响应消息</returns>
        /// <remarks>
        /// S1F1 处理逻辑：
        /// 1. 记录接收到的请求
        /// 2. 验证设备当前状态
        /// 3. 构建包含设备标识信息的S1F2响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug("收到 S1F1 (Are You There) 通信测试请求");

            try
            {
                // 验证消息格式
                if (!ValidateMessage(message))
                {
                    Logger.LogWarning("S1F1 消息格式无效");
                    return CreateErrorResponse();
                }

                // 获取当前设备状态
                var statusInfo = await _stateService.GetStatusInfoAsync();

                Logger.LogDebug($"设备状态: ControlState={statusInfo.ControlState}, " +
                              $"ProcessState={statusInfo.ProcessState}, " +
                              $"IsOnline={statusInfo.IsOnline}");

                // 构建 S1F2 响应
                var response = CreateS1F2Response();

                Logger.LogDebug($"发送 S1F2 响应: MDLN='{_equipmentConfig.ModelName}', " +
                              $"SOFTREV='{_equipmentConfig.SoftwareRevision}'");

                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理 S1F1 消息时发生异常");
                return CreateErrorResponse();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 验证 S1F1 消息格式
        /// </summary>
        /// <param name="message">待验证的消息</param>
        /// <returns>验证结果</returns>
        private bool ValidateMessage(SecsMessage message)
        {
            if (message == null)
            {
                Logger.LogWarning("S1F1 消息为空");
                return false;
            }

            // S1F1 通常包含空列表或无数据项
            if (message.SecsItem != null && message.SecsItem.Count > 0)
            {
                Logger.LogDebug($"S1F1 包含 {message.SecsItem.Count} 个数据项（通常为空）");
            }

            return true;
        }

        /// <summary>
        /// 创建 S1F2 (On Line Data) 响应消息
        /// </summary>
        /// <returns>S1F2响应消息</returns>
        /// <remarks>
        /// S1F2 消息结构 (SEMI E30标准):
        /// L,2
        /// 1. &lt;MDLN&gt; type A - 设备型号名称
        /// 2. &lt;SOFTREV&gt; type A - 软件版本号
        /// </remarks>
        private SecsMessage CreateS1F2Response()
        {
            // 确保配置数据符合SEMI标准长度限制（最大20字符）
            var mdln = TruncateToMaxLength(_equipmentConfig.ModelName, 20);
            var softrev = TruncateToMaxLength(_equipmentConfig.SoftwareRevision, 20);

            // 构建 S1F2 消息
            var s1f2 = new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "On Line Data",
                SecsItem = Item.L(
                    Item.A(mdln),    // MDLN - 设备型号
                    Item.A(softrev)  // SOFTREV - 软件版本
                )
            };

            Logger.LogDebug($"构建 S1F2 响应成功: MDLN='{mdln}', SOFTREV='{softrev}'");
            return s1f2;
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        /// <returns>错误响应消息</returns>
        private SecsMessage CreateErrorResponse()
        {
            Logger.LogWarning("创建 S1F2 错误响应");

            // 返回包含默认值的 S1F2
            return new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "On Line Data (Error)",
                SecsItem = Item.L(
                    Item.A("ERROR"),    // 错误标识
                    Item.A("UNKNOWN")   // 未知版本
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
    }
}
