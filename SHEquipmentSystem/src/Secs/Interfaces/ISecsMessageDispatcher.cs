// 文件路径: src/DiceEquipmentSystem/Secs/Interfaces/ISecsMessageDispatcher.cs
// 版本: v1.0.0
// 描述: SECS消息分发器接口定义

using Secs4Net;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// SECS消息分发器接口
    /// 负责将接收到的SECS消息路由到相应的处理器
    /// </summary>
    public interface ISecsMessageDispatcher
    {
        /// <summary>
        /// 分发消息到相应的处理器
        /// </summary>
        /// <param name="message">接收到的SECS消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应消息，如果没有处理器或不需要响应则返回null</returns>
        Task<SecsMessage?> DispatchAsync(SecsMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <param name="stream">流号</param>
        /// <param name="function">功能号</param>
        /// <param name="handler">处理器实例</param>
        void RegisterHandler(byte stream, byte function, ISecsMessageHandler handler);

        /// <summary>
        /// 移除消息处理器
        /// </summary>
        /// <param name="stream">流号</param>
        /// <param name="function">功能号</param>
        void UnregisterHandler(byte stream, byte function);

        /// <summary>
        /// 检查是否有处理器
        /// </summary>
        /// <param name="stream">流号</param>
        /// <param name="function">功能号</param>
        /// <returns>true表示有处理器，false表示没有</returns>
        bool HasHandler(byte stream, byte function);
    }
}
