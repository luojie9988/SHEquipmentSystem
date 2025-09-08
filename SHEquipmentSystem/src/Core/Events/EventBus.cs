// 文件路径: src/DiceEquipmentSystem/Core/EventBus.cs
// 版本: v2.0.0
// 描述: 事件总线实现 - 提供高性能的事件发布订阅功能

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Core
{
    /// <summary>
    /// 事件总线实现
    /// 提供线程安全的事件发布订阅机制
    /// </summary>
    public class EventBus : IEventBus
    {
        #region 字段

        private readonly ILogger<EventBus> _logger;
        private readonly ConcurrentDictionary<Type, ConcurrentBag<EventSubscription>> _subscriptions;
        private readonly EventBusOptions _options;
        private readonly SemaphoreSlim _publishSemaphore;
        private readonly EventStatistics _statistics;
        private bool _disposed;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="options">配置选项</param>
        public EventBus(ILogger<EventBus> logger, EventBusOptions? options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new EventBusOptions();
            _subscriptions = new ConcurrentDictionary<Type, ConcurrentBag<EventSubscription>>();
            _publishSemaphore = new SemaphoreSlim(_options.MaxConcurrentPublish, _options.MaxConcurrentPublish);
            _statistics = new EventStatistics();

            _logger.LogInformation("事件总线已初始化 (最大并发: {MaxConcurrent})", _options.MaxConcurrentPublish);
        }

        #endregion

        #region 发布方法

        /// <summary>
        /// 发布事件（异步）
        /// </summary>
        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            ThrowIfDisposed();

            if (eventData == null)
            {
                throw new ArgumentNullException(nameof(eventData));
            }

            var eventType = typeof(TEvent);
            _statistics.IncrementPublished(eventType);

            if (_options.EnableLogging)
            {
                _logger.LogDebug("发布事件: {EventType} (ID: {EventId})", eventType.Name, eventData.EventId);
            }

            // 获取订阅者
            if (!_subscriptions.TryGetValue(eventType, out var subscriptions) || !subscriptions.Any())
            {
                _logger.LogDebug("事件 {EventType} 没有订阅者", eventType.Name);
                return;
            }

            // 获取有效订阅
            var activeSubscriptions = subscriptions.Where(s => !s.IsCancelled).ToList();
            if (activeSubscriptions.Count == 0)
            {
                return;
            }

            // 并发控制
            await _publishSemaphore.WaitAsync(cancellationToken);
            try
            {
                // 并行处理订阅者
                var tasks = new List<Task>();

                foreach (var subscription in activeSubscriptions)
                {
                    tasks.Add(InvokeHandlerAsync(subscription, eventData, cancellationToken));
                }

                // 等待所有处理完成
                if (_options.WaitForHandlers)
                {
                    await Task.WhenAll(tasks);
                }
                else
                {
                    // 火忘模式
                    _ = Task.Run(async () => await Task.WhenAll(tasks), cancellationToken);
                }
            }
            finally
            {
                _publishSemaphore.Release();
            }
        }

        /// <summary>
        /// 发布事件（同步）
        /// </summary>
        public void Publish<TEvent>(TEvent eventData) where TEvent : class, IEvent
        {
            PublishAsync(eventData).GetAwaiter().GetResult();
        }

        #endregion

        #region 订阅方法

        /// <summary>
        /// 订阅事件
        /// </summary>
        public IEventSubscription Subscribe<TEvent>(Action<TEvent> handler)
            where TEvent : class, IEvent
        {
            ThrowIfDisposed();

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var eventType = typeof(TEvent);
            var subscription = new EventSubscription(
                eventType,
                (Action<IEvent>)(data => handler((TEvent)data)),  // 明确指定委托类型
                this);


            AddSubscription(eventType, subscription);

            _statistics.IncrementSubscriptions(eventType);
            _logger.LogDebug("添加订阅: {EventType} (ID: {SubscriptionId})",
                eventType.Name, subscription.SubscriptionId);

            return subscription;
        }

        /// <summary>
        /// 订阅事件（异步处理器）
        /// </summary>
        public IEventSubscription SubscribeAsync<TEvent>(Func<TEvent, Task> handler)
            where TEvent : class, IEvent
        {
            ThrowIfDisposed();

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var eventType = typeof(TEvent);
            var subscription = new EventSubscription(
                eventType,
                (Func<IEvent, Task>)(async data => await handler((TEvent)data)),  // 明确指定委托类型
                this);


            AddSubscription(eventType, subscription);

            _statistics.IncrementSubscriptions(eventType);
            _logger.LogDebug("添加异步订阅: {EventType} (ID: {SubscriptionId})",
                eventType.Name, subscription.SubscriptionId);

            return subscription;
        }

        /// <summary>
        /// 订阅事件（带过滤器）
        /// </summary>
        public IEventSubscription Subscribe<TEvent>(Action<TEvent> handler, Predicate<TEvent> filter)
            where TEvent : class, IEvent
        {
            ThrowIfDisposed();

            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var eventType = typeof(TEvent);
            var subscription = new EventSubscription(
                eventType,
                (Action<IEvent>)(data =>
                {
                    var typedData = (TEvent)data;
                    if (filter(typedData))
                    {
                        handler(typedData);
                    }
                }),
                this);

            AddSubscription(eventType, subscription);

            _statistics.IncrementSubscriptions(eventType);
            _logger.LogDebug("添加条件订阅: {EventType} (ID: {SubscriptionId})",
                eventType.Name, subscription.SubscriptionId);

            return subscription;
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public void Unsubscribe(IEventSubscription subscription)
        {
            if (subscription == null) return;

            subscription.Cancel();
            _statistics.DecrementSubscriptions(subscription.EventType);

            _logger.LogDebug("取消订阅: {EventType} (ID: {SubscriptionId})",
                subscription.EventType.Name, subscription.SubscriptionId);

            // 清理已取消的订阅
            CleanupCancelledSubscriptions(subscription.EventType);
        }

        /// <summary>
        /// 取消特定类型的所有订阅
        /// </summary>
        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
        {
            var eventType = typeof(TEvent);

            if (_subscriptions.TryRemove(eventType, out var subscriptions))
            {
                foreach (var subscription in subscriptions)
                {
                    subscription.Cancel();
                }

                _logger.LogInformation("清除所有 {EventType} 订阅 (数量: {Count})",
                    eventType.Name, subscriptions.Count);
            }
        }

        /// <summary>
        /// 清除所有订阅
        /// </summary>
        public void Clear()
        {
            foreach (var kvp in _subscriptions)
            {
                foreach (var subscription in kvp.Value)
                {
                    subscription.Cancel();
                }
            }

            _subscriptions.Clear();
            _logger.LogInformation("已清除所有事件订阅");
        }

        /// <summary>
        /// 获取订阅者数量
        /// </summary>
        public int GetSubscriberCount<TEvent>() where TEvent : class, IEvent
        {
            var eventType = typeof(TEvent);

            if (_subscriptions.TryGetValue(eventType, out var subscriptions))
            {
                return subscriptions.Count(s => !s.IsCancelled);
            }

            return 0;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 添加订阅
        /// </summary>
        private void AddSubscription(Type eventType, EventSubscription subscription)
        {
            var subscriptions = _subscriptions.GetOrAdd(eventType, _ => new ConcurrentBag<EventSubscription>());
            subscriptions.Add(subscription);
        }

        /// <summary>
        /// 调用处理器
        /// </summary>
        private async Task InvokeHandlerAsync(EventSubscription subscription, IEvent eventData, CancellationToken cancellationToken)
        {
            if (subscription.IsCancelled) return;

            try
            {
                // 创建超时令牌
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_options.HandlerTimeout);

                await Task.Run(() => subscription.InvokeHandler(eventData), cts.Token);

                _statistics.IncrementHandled(eventData.GetType());
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("事件处理超时: {EventType} -> {SubscriptionId}",
                    eventData.GetType().Name, subscription.SubscriptionId);
                _statistics.IncrementTimeout(eventData.GetType());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件处理失败: {EventType} -> {SubscriptionId}",
                    eventData.GetType().Name, subscription.SubscriptionId);
                _statistics.IncrementError(eventData.GetType());

                if (_options.ThrowOnHandlerError)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// 清理已取消的订阅
        /// </summary>
        private void CleanupCancelledSubscriptions(Type eventType)
        {
            if (_subscriptions.TryGetValue(eventType, out var subscriptions))
            {
                var activeSubscriptions = subscriptions.Where(s => !s.IsCancelled).ToList();

                if (activeSubscriptions.Count != subscriptions.Count)
                {
                    _subscriptions[eventType] = new ConcurrentBag<EventSubscription>(activeSubscriptions);

                    _logger.LogDebug("清理 {EventType} 的已取消订阅 (移除: {Count})",
                        eventType.Name, subscriptions.Count - activeSubscriptions.Count);
                }
            }
        }

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventBus));
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            Clear();
            _publishSemaphore?.Dispose();

            _logger.LogInformation("事件总线已释放 (统计: 发布={Published}, 处理={Handled}, 错误={Errors})",
                _statistics.TotalPublished, _statistics.TotalHandled, _statistics.TotalErrors);

            _disposed = true;
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 事件订阅实现
        /// </summary>
        private class EventSubscription : IEventSubscription
        {
            private readonly Delegate _handler;
            private readonly EventBus _eventBus;
            private bool _isCancelled;

            public Guid SubscriptionId { get; }
            public Type EventType { get; }
            public bool IsCancelled => _isCancelled;

            public EventSubscription(Type eventType, Delegate handler, EventBus eventBus)
            {
                SubscriptionId = Guid.NewGuid();
                EventType = eventType;
                _handler = handler;
                _eventBus = eventBus;
            }

            public void InvokeHandler(IEvent eventData)
            {
                if (_isCancelled) return;

                if (_handler is Action<IEvent> action)
                {
                    action(eventData);
                }
                else if (_handler is Func<IEvent, Task> func)
                {
                    func(eventData).GetAwaiter().GetResult();
                }
            }

            public void Cancel()
            {
                _isCancelled = true;
            }

            public void Dispose()
            {
                if (!_isCancelled)
                {
                    _eventBus.Unsubscribe(this);
                }
            }
        }

        /// <summary>
        /// 事件统计
        /// </summary>
        private class EventStatistics
        {
            private readonly ConcurrentDictionary<Type, long> _publishedCount = new();
            private readonly ConcurrentDictionary<Type, long> _handledCount = new();
            private readonly ConcurrentDictionary<Type, long> _errorCount = new();
            private readonly ConcurrentDictionary<Type, long> _timeoutCount = new();
            private readonly ConcurrentDictionary<Type, long> _subscriptionCount = new();

            public long TotalPublished => _publishedCount.Values.Sum();
            public long TotalHandled => _handledCount.Values.Sum();
            public long TotalErrors => _errorCount.Values.Sum();

            public void IncrementPublished(Type eventType)
            {
                _publishedCount.AddOrUpdate(eventType, 1, (_, count) => count + 1);
            }

            public void IncrementHandled(Type eventType)
            {
                _handledCount.AddOrUpdate(eventType, 1, (_, count) => count + 1);
            }

            public void IncrementError(Type eventType)
            {
                _errorCount.AddOrUpdate(eventType, 1, (_, count) => count + 1);
            }

            public void IncrementTimeout(Type eventType)
            {
                _timeoutCount.AddOrUpdate(eventType, 1, (_, count) => count + 1);
            }

            public void IncrementSubscriptions(Type eventType)
            {
                _subscriptionCount.AddOrUpdate(eventType, 1, (_, count) => count + 1);
            }

            public void DecrementSubscriptions(Type eventType)
            {
                _subscriptionCount.AddOrUpdate(eventType, 0, (_, count) => Math.Max(0, count - 1));
            }
        }

        #endregion
    }

    /// <summary>
    /// 事件总线配置选项
    /// </summary>
    public class EventBusOptions
    {
        /// <summary>
        /// 最大并发发布数
        /// </summary>
        public int MaxConcurrentPublish { get; set; } = 10;

        /// <summary>
        /// 处理器超时时间
        /// </summary>
        public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否等待所有处理器完成
        /// </summary>
        public bool WaitForHandlers { get; set; } = true;

        /// <summary>
        /// 处理器错误时是否抛出异常
        /// </summary>
        public bool ThrowOnHandlerError { get; set; } = false;

        /// <summary>
        /// 是否启用日志
        /// </summary>
        public bool EnableLogging { get; set; } = true;
    }
}
