// 文件路径: src/DiceEquipmentSystem/Secs/Communication/DefaultMessageHandler.cs
// 版本: v1.1.0
// 描述: 默认消息处理器 - 修复IDisposable实现

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Secs.Interfaces;
using Microsoft.Extensions.Logging;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Communication
{
    /// <summary>
    /// 默认消息处理器
    /// 用于处理没有特定处理器的消息
    /// </summary>
    public class DefaultMessageHandler : SecsMessageHandlerBase
    {
        /// <summary>
        /// Stream号（0表示处理所有）
        /// </summary>
        public override byte Stream => 0;

        /// <summary>
        /// Function号（0表示处理所有）
        /// </summary>
        public override byte Function => 0;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public DefaultMessageHandler(ILogger<DefaultMessageHandler> logger) : base(logger)
        {
        }

        /// <summary>
        /// 判断是否可以处理消息
        /// </summary>
        /// <param name="message">SECS消息</param>
        /// <returns>默认处理器可以处理所有消息</returns>
        public override bool CanHandle(SecsMessage message)
        {
            return true;
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">接收到的消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应消息或null</returns>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            Logger.LogWarning("使用默认处理器处理 S{Stream}F{Function} ({Name})",
                message.S, message.F, message.Name ?? "Unknown");

            var response = GenerateBasicResponse(message);

            if (response != null)
            {
                Logger.LogDebug("生成默认响应: S{Stream}F{Function}", response.S, response.F);
            }
            else
            {
                Logger.LogDebug("无法生成默认响应，返回null");
            }

            return await Task.FromResult(response);
        }

        /// <summary>
        /// 生成基本响应
        /// </summary>
        /// <param name="message">原始消息</param>
        /// <returns>响应消息或null</returns>
        private SecsMessage? GenerateBasicResponse(SecsMessage message)
        {
            // 如果是主消息，尝试生成对应的辅消息
            if (message.S == 1 && message.F == 1) // S1F1 -> S1F2
            {
                return new SecsMessage(1, 2, false)
                {
                    Name = "On Line Data",
                    SecsItem = Item.L(
                        Item.A("MDLN"),    // 设备型号
                        Item.A("1.0.0")    // 软件版本
                    )
                };
            }

            // 对于其他消息，返回null（无响应）
            return null;
        }
    }
}
