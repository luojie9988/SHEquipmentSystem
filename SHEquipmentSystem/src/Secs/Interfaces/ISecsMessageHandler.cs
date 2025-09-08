// 文件路径: src/DiceEquipmentSystem/Secs/Interfaces/ISecsMessageHandler.cs
// 版本: v1.1.0
// 描述: SECS消息处理器接口和基类 - 添加IDisposable支持

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// SECS消息处理器接口
    /// 定义SECS消息处理的标准契约
    /// </summary>
    public interface ISecsMessageHandler : IDisposable
    {
        /// <summary>
        /// 消息流号
        /// </summary>
        byte Stream { get; }

        /// <summary>
        /// 消息功能号
        /// </summary>
        byte Function { get; }

        /// <summary>
        /// 判断是否可以处理指定消息
        /// </summary>
        /// <param name="message">SECS消息</param>
        /// <returns>如果可以处理返回true</returns>
        bool CanHandle(SecsMessage message);

        /// <summary>
        /// 异步处理消息
        /// </summary>
        /// <param name="message">接收到的消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应消息，如果不需要响应则返回null</returns>
        Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 消息处理器基类
    /// 提供通用的消息处理逻辑和资源管理
    /// </summary>
    public abstract class SecsMessageHandlerBase : ISecsMessageHandler
    {
        #region 字段

        /// <summary>
        /// 日志记录器
        /// </summary>
        protected readonly ILogger Logger;

        /// <summary>
        /// 释放标志
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 属性

        /// <summary>
        /// 消息流号
        /// </summary>
        public abstract byte Stream { get; }

        /// <summary>
        /// 消息功能号
        /// </summary>
        public abstract byte Function { get; }

        /// <summary>
        /// 处理器名称
        /// </summary>
        public virtual string Name => $"S{Stream}F{Function}Handler";

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <exception cref="ArgumentNullException">当logger为null时抛出</exception>
        protected SecsMessageHandlerBase(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Logger.LogDebug("{HandlerName} 已创建", Name);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 判断是否可以处理消息
        /// </summary>
        /// <param name="message">SECS消息</param>
        /// <returns>如果可以处理返回true</returns>
        public virtual bool CanHandle(SecsMessage message)
        {
            if (message == null)
            {
                Logger.LogWarning("收到空消息");
                return false;
            }

            var canHandle = message.S == Stream && message.F == Function;

            if (canHandle)
            {
                Logger.LogDebug("{HandlerName} 可以处理 S{Stream}F{Function}", Name, message.S, message.F);
            }

            return canHandle;
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">接收到的消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应消息</returns>
        public abstract Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default);

        #endregion

        #region IDisposable 实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的具体实现
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Logger.LogDebug("{HandlerName} 正在释放资源", Name);

                // 子类可以重写此方法来释放特定资源
                DisposeCore(disposing);
            }

            _disposed = true;
        }

        /// <summary>
        /// 子类重写此方法来释放特定资源
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected virtual void DisposeCore(bool disposing)
        {
            // 基类默认无需释放额外资源
            // 子类根据需要重写此方法
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~SecsMessageHandlerBase()
        {
            Dispose(false);
        }

        #endregion

        #region 受保护的辅助方法

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        /// <exception cref="ObjectDisposedException">当对象已释放时抛出</exception>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <summary>
        /// 记录处理开始
        /// </summary>
        /// <param name="message">处理的消息</param>
        protected void LogHandlingStart(SecsMessage message)
        {
            Logger.LogInformation("[开始处理] {HandlerName} 处理 S{Stream}F{Function} {MessageName}",
                Name, message.S, message.F, message.Name ?? "");
        }

        /// <summary>
        /// 记录处理完成
        /// </summary>
        /// <param name="message">处理的消息</param>
        /// <param name="response">响应消息</param>
        /// <param name="processingTime">处理时间</param>
        protected void LogHandlingComplete(SecsMessage message, SecsMessage? response, TimeSpan processingTime)
        {
            if (response != null)
            {
                Logger.LogInformation("[处理完成] {HandlerName} 生成响应 S{ResponseStream}F{ResponseFunction} (耗时: {Time:F0}ms)",
                    Name, response.S, response.F, processingTime.TotalMilliseconds);
            }
            else
            {
                Logger.LogInformation("[处理完成] {HandlerName} 无响应消息 (耗时: {Time:F0}ms)",
                    Name, processingTime.TotalMilliseconds);
            }
        }

        /// <summary>
        /// 记录处理错误
        /// </summary>
        /// <param name="message">处理的消息</param>
        /// <param name="exception">发生的异常</param>
        protected void LogHandlingError(SecsMessage message, Exception exception)
        {
            Logger.LogError(exception, "[处理错误] {HandlerName} 处理 S{Stream}F{Function} 时发生异常",
                Name, message.S, message.F);
        }

        #endregion
    }
}
