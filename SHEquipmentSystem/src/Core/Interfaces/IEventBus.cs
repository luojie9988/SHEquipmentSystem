// 文件路径: src/DiceEquipmentSystem/Core/Interfaces/IEventBus.cs
// 版本: v2.0.0
// 描述: 事件总线接口 - 定义事件发布订阅功能

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Interfaces
{
    /// <summary>
    /// 事件总线接口
    /// 提供解耦的事件发布订阅机制
    /// </summary>
    /// <remarks>
    /// 用于系统内部各组件之间的事件通信：
    /// 1. 状态变更通知
    /// 2. 报警事件传播
    /// 3. 数据更新通知
    /// 4. 系统事件广播
    /// </remarks>
    public interface IEventBus : IDisposable
    {
        /// <summary>
        /// 发布事件（异步）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="eventData">事件数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>异步任务</returns>
        Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent;

        /// <summary>
        /// 发布事件（同步）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="eventData">事件数据</param>
        void Publish<TEvent>(TEvent eventData)
            where TEvent : class, IEvent;

        /// <summary>
        /// 订阅事件（强类型处理器）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="handler">事件处理器</param>
        /// <returns>订阅令牌，用于取消订阅</returns>
        IEventSubscription Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IEvent;

        /// <summary>
        /// 订阅事件（异步处理器）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="handler">异步事件处理器</param>
        /// <returns>订阅令牌</returns>
        IEventSubscription SubscribeAsync<TEvent>(Func<TEvent, Task> handler)
            where TEvent : class, IEvent;

        /// <summary>
        /// 订阅事件（带过滤器）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="handler">事件处理器</param>
        /// <param name="filter">过滤条件</param>
        /// <returns>订阅令牌</returns>
        IEventSubscription Subscribe<TEvent>(Action<TEvent> handler, Predicate<TEvent> filter)
            where TEvent : class, IEvent;

        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <param name="subscription">订阅令牌</param>
        void Unsubscribe(IEventSubscription subscription);

        /// <summary>
        /// 取消特定类型的所有订阅
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        void UnsubscribeAll<TEvent>() where TEvent : class, IEvent;

        /// <summary>
        /// 清除所有订阅
        /// </summary>
        void Clear();

        /// <summary>
        /// 获取订阅者数量
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <returns>订阅者数量</returns>
        int GetSubscriberCount<TEvent>() where TEvent : class, IEvent;
    }

    /// <summary>
    /// 事件基础接口
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        Guid EventId { get; }

        /// <summary>
        /// 事件时间戳
        /// </summary>
        DateTime Timestamp { get; }

        /// <summary>
        /// 事件源
        /// </summary>
        string? Source { get; }
    }

    /// <summary>
    /// 事件订阅接口
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
        /// <summary>
        /// 订阅ID
        /// </summary>
        Guid SubscriptionId { get; }

        /// <summary>
        /// 事件类型
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// 是否已取消
        /// </summary>
        bool IsCancelled { get; }

        /// <summary>
        /// 取消订阅
        /// </summary>
        void Cancel();
    }
}
