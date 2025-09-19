// 文件路径: src/Secs/Communication/MultiSecsConnectionManager.cs
// 版本: v1.0.0
// 描述: 多实例SECS连接管理器

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using Microsoft.Extensions.Logging;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Communication
{
    /// <summary>
    /// SECS连接实例信息
    /// </summary>
    public class SecsConnectionInstance
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>连接管理器</summary>
        public ISecsConnectionManager ConnectionManager { get; set; } = null!;

        /// <summary>配置信息</summary>
        public SecsInstanceConfiguration Configuration { get; set; } = new();

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>创建时间</summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>最后活动时间</summary>
        public DateTime LastActivityTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 多实例SECS连接管理器接口
    /// </summary>
    public interface IMultiSecsConnectionManager
    {
        /// <summary>连接状态变更事件</summary>
        event EventHandler<MultiSecsConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>获取所有连接实例</summary>
        IEnumerable<SecsConnectionInstance> GetAllConnections();

        /// <summary>获取指定设备的连接管理器</summary>
        ISecsConnectionManager? GetConnectionManager(string deviceId);

        /// <summary>添加连接实例</summary>
        Task<bool> AddConnectionAsync(string deviceId, SecsInstanceConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>移除连接实例</summary>
        Task<bool> RemoveConnectionAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>启动所有连接</summary>
        Task<bool> StartAllConnectionsAsync(CancellationToken cancellationToken = default);

        /// <summary>启动指定连接</summary>
        Task<bool> StartConnectionAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>停止所有连接</summary>
        Task<bool> StopAllConnectionsAsync(CancellationToken cancellationToken = default);

        /// <summary>停止指定连接</summary>
        Task<bool> StopConnectionAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>获取连接统计信息</summary>
        object GetConnectionStatistics();

        /// <summary>检查设备是否在线</summary>
        bool IsDeviceOnline(string deviceId);

        /// <summary>发送消息到指定设备</summary>
        Task<SecsMessage?> SendMessageAsync(string deviceId, SecsMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 多实例连接状态变更事件参数
    /// </summary>
    public class MultiSecsConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>旧状态</summary>
        public HsmsConnectionState OldState { get; set; }

        /// <summary>新状态</summary>
        public HsmsConnectionState NewState { get; set; }

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 多实例SECS连接管理器实现
    /// </summary>
    public class MultiSecsConnectionManager : IMultiSecsConnectionManager, IDisposable
    {
        #region 私有字段

        private readonly ILogger<MultiSecsConnectionManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISecsMessageDispatcher? _messageDispatcher;
        private readonly ConcurrentDictionary<string, SecsConnectionInstance> _connections;
        private readonly SemaphoreSlim _operationSemaphore;
        private bool _disposed;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        public event EventHandler<MultiSecsConnectionStateChangedEventArgs>? ConnectionStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public MultiSecsConnectionManager(
            ILogger<MultiSecsConnectionManager> logger,
            IServiceProvider serviceProvider,
            ISecsMessageDispatcher? messageDispatcher = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _messageDispatcher = messageDispatcher;

            _connections = new ConcurrentDictionary<string, SecsConnectionInstance>();
            _operationSemaphore = new SemaphoreSlim(1, 1);

            _logger.LogInformation("多实例SECS连接管理器已创建");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有连接实例
        /// </summary>
        public IEnumerable<SecsConnectionInstance> GetAllConnections()
        {
            return _connections.Values.ToList();
        }

        /// <summary>
        /// 获取指定设备的连接管理器
        /// </summary>
        public ISecsConnectionManager? GetConnectionManager(string deviceId)
        {
            return _connections.TryGetValue(deviceId, out var instance) ? instance.ConnectionManager : null;
        }

        /// <summary>
        /// 添加连接实例
        /// </summary>
        public async Task<bool> AddConnectionAsync(string deviceId, SecsInstanceConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_connections.ContainsKey(deviceId))
                {
                    _logger.LogWarning("设备 {DeviceId} 的SECS连接已存在", deviceId);
                    return false;
                }

                _logger.LogInformation("正在添加设备 {DeviceId} 的SECS连接", deviceId);

                // 创建设备特定的连接管理器
                var connectionManager = CreateConnectionManager(deviceId, configuration);
                if (connectionManager == null)
                {
                    _logger.LogError("创建设备 {DeviceId} 的SECS连接管理器失败", deviceId);
                    return false;
                }

                // 订阅连接状态变更事件
                connectionManager.ConnectionStateChanged += (sender, args) =>
                {
                    OnConnectionStateChanged(deviceId, args.OldState, args.NewState);
                };

                var instance = new SecsConnectionInstance
                {
                    DeviceId = deviceId,
                    ConnectionManager = connectionManager,
                    Configuration = configuration,
                    Enabled = configuration.Enabled
                };

                _connections.TryAdd(deviceId, instance);
                _logger.LogInformation("设备 {DeviceId} 的SECS连接已添加", deviceId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加设备 {DeviceId} 的SECS连接失败", deviceId);
                return false;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 移除连接实例
        /// </summary>
        public async Task<bool> RemoveConnectionAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (!_connections.TryRemove(deviceId, out var instance))
                {
                    _logger.LogWarning("设备 {DeviceId} 的SECS连接不存在", deviceId);
                    return false;
                }

                _logger.LogInformation("正在移除设备 {DeviceId} 的SECS连接", deviceId);

                // 停止并释放连接
                await instance.ConnectionManager.StopAsync(cancellationToken);
                if (instance.ConnectionManager is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _logger.LogInformation("设备 {DeviceId} 的SECS连接已移除", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除设备 {DeviceId} 的SECS连接失败", deviceId);
                return false;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 启动所有连接
        /// </summary>
        public async Task<bool> StartAllConnectionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("开始启动所有SECS连接...");

            var enabledConnections = _connections.Values.Where(c => c.Enabled).ToList();
            var successCount = 0;
            var totalCount = enabledConnections.Count;

            var startTasks = enabledConnections.Select(async connection =>
            {
                try
                {
                    await connection.ConnectionManager.StartAsync(cancellationToken);
                    connection.LastActivityTime = DateTime.Now;
                    _logger.LogInformation("设备 {DeviceId} 的SECS连接启动成功", connection.DeviceId);
                    Interlocked.Increment(ref successCount);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备 {DeviceId} 的SECS连接启动失败", connection.DeviceId);
                    return false;
                }
            });

            await Task.WhenAll(startTasks);

            _logger.LogInformation("SECS连接启动完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 启动指定连接
        /// </summary>
        public async Task<bool> StartConnectionAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的SECS连接不存在", deviceId);
                return false;
            }

            try
            {
                _logger.LogInformation("正在启动设备 {DeviceId} 的SECS连接", deviceId);
                await instance.ConnectionManager.StartAsync(cancellationToken);
                instance.LastActivityTime = DateTime.Now;
                _logger.LogInformation("设备 {DeviceId} 的SECS连接启动成功", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备 {DeviceId} 的SECS连接启动失败", deviceId);
                return false;
            }
        }

        /// <summary>
        /// 停止所有连接
        /// </summary>
        public async Task<bool> StopAllConnectionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("开始停止所有SECS连接...");

            var connections = _connections.Values.ToList();
            var successCount = 0;
            var totalCount = connections.Count;

            var stopTasks = connections.Select(async connection =>
            {
                try
                {
                    await connection.ConnectionManager.StopAsync(cancellationToken);
                    _logger.LogInformation("设备 {DeviceId} 的SECS连接停止成功", connection.DeviceId);
                    Interlocked.Increment(ref successCount);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备 {DeviceId} 的SECS连接停止失败", connection.DeviceId);
                    return false;
                }
            });

            await Task.WhenAll(stopTasks);

            _logger.LogInformation("SECS连接停止完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 停止指定连接
        /// </summary>
        public async Task<bool> StopConnectionAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的SECS连接不存在", deviceId);
                return false;
            }

            try
            {
                _logger.LogInformation("正在停止设备 {DeviceId} 的SECS连接", deviceId);
                await instance.ConnectionManager.StopAsync(cancellationToken);
                _logger.LogInformation("设备 {DeviceId} 的SECS连接停止成功", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备 {DeviceId} 的SECS连接停止失败", deviceId);
                return false;
            }
        }

        /// <summary>
        /// 检查设备是否在线
        /// </summary>
        public bool IsDeviceOnline(string deviceId)
        {
            return _connections.TryGetValue(deviceId, out var instance) && instance.ConnectionManager.IsSelected;
        }

        /// <summary>
        /// 发送消息到指定设备
        /// </summary>
        public async Task<SecsMessage?> SendMessageAsync(string deviceId, SecsMessage message, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的SECS连接不存在", deviceId);
                return null;
            }

            try
            {
                var reply = await instance.ConnectionManager.SendMessageAsync(message, cancellationToken);
                instance.LastActivityTime = DateTime.Now;
                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向设备 {DeviceId} 发送SECS消息失败", deviceId);
                return null;
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        public object GetConnectionStatistics()
        {
            var connections = GetAllConnections().ToList();
            var onlineCount = connections.Count(c => c.ConnectionManager.IsSelected);
            var totalCount = connections.Count;

            return new
            {
                TotalConnections = totalCount,
                OnlineConnections = onlineCount,
                OfflineConnections = totalCount - onlineCount,
                Connections = connections.Select(c => new
                {
                    c.DeviceId,
                    IsOnline = c.ConnectionManager.IsSelected,
                    IsConnected = c.ConnectionManager.IsConnected,
                    State = c.ConnectionManager.HsmsConnectionState.ToString(),
                    LastConnectedTime = c.ConnectionManager.LastConnectedTime,
                    c.LastActivityTime,
                    c.CreatedTime,
                    Statistics = c.ConnectionManager.GetConnectionStatistics()
                })
            };
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 创建连接管理器
        /// </summary>
        private ISecsConnectionManager? CreateConnectionManager(string deviceId, SecsInstanceConfiguration configuration)
        {
            try
            {
                // 创建设备特定的配置
                var equipmentConfig = new EquipmentSystemConfiguration
                {
                    Equipment = new EquipmentConfiguration
                    {
                        DeviceId = configuration.DeviceId,
                        IpAddress = configuration.IpAddress,
                        Port = configuration.Port,
                        IsActive = configuration.IsActive,
                        T3 = configuration.Timeouts.T3,
                        T5 = configuration.Timeouts.T5,
                        T6 = configuration.Timeouts.T6,
                        T7 = configuration.Timeouts.T7,
                        T8 = configuration.Timeouts.T8,
                        LinkTestInterval = configuration.Timeouts.LinkTestInterval
                    }
                };

                // 创建设备特定的日志记录器
                var loggerFactory = _serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                var deviceLogger = loggerFactory?.CreateLogger($"SecsConnection.{deviceId}");

                if (deviceLogger == null)
                {
                    _logger.LogError("无法创建设备 {DeviceId} 的日志记录器", deviceId);
                    return null;
                }

                // 创建连接管理器实例
                var connectionManager = new SecsConnectionManager(
                    (ILogger<SecsConnectionManager>)deviceLogger,
                    Microsoft.Extensions.Options.Options.Create(equipmentConfig),
                    _messageDispatcher);

                return connectionManager;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建设备 {DeviceId} 的连接管理器失败", deviceId);
                return null;
            }
        }

        /// <summary>
        /// 连接状态变更事件处理
        /// </summary>
        private void OnConnectionStateChanged(string deviceId, HsmsConnectionState oldState, HsmsConnectionState newState)
        {
            _logger.LogInformation("设备 {DeviceId} SECS连接状态变更: {OldState} -> {NewState}", deviceId, oldState, newState);

            ConnectionStateChanged?.Invoke(this, new MultiSecsConnectionStateChangedEventArgs
            {
                DeviceId = deviceId,
                OldState = oldState,
                NewState = newState,
                Timestamp = DateTime.Now
            });
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _logger.LogInformation("正在释放多实例SECS连接管理器...");

            var stopTask = StopAllConnectionsAsync();
            stopTask.Wait(TimeSpan.FromSeconds(30));

            foreach (var instance in _connections.Values)
            {
                if (instance.ConnectionManager is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _connections.Clear();
            _operationSemaphore?.Dispose();

            _disposed = true;
            _logger.LogInformation("多实例SECS连接管理器已释放");
        }

        #endregion
    }
}