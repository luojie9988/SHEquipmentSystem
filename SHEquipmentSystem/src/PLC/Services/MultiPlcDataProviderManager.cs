// 文件路径: src/PLC/Services/MultiPlcDataProviderManager.cs
// 版本: v1.0.0
// 描述: 多实例PLC数据提供者管理器

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SHEquipmentSystem.PLC.Services;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC连接实例信息
    /// </summary>
    public class PlcConnectionInstance
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>PLC数据提供者</summary>
        public IPlcDataProvider DataProvider { get; set; } = null!;

        /// <summary>配置信息</summary>
        public PlcInstanceConfiguration Configuration { get; set; } = new();

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>创建时间</summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>最后活动时间</summary>
        public DateTime LastActivityTime { get; set; } = DateTime.Now;

        /// <summary>数据映射器</summary>
        public PlcDataMapper? DataMapper { get; set; }
    }

    /// <summary>
    /// PLC连接状态变更事件参数
    /// </summary>
    public class PlcConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>是否连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 多实例PLC数据提供者管理器接口
    /// </summary>
    public interface IMultiPlcDataProviderManager
    {
        /// <summary>连接状态变更事件</summary>
        event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>获取所有连接实例</summary>
        IEnumerable<PlcConnectionInstance> GetAllConnections();

        /// <summary>获取指定设备的数据提供者</summary>
        IPlcDataProvider? GetDataProvider(string deviceId);

        /// <summary>添加连接实例</summary>
        Task<bool> AddConnectionAsync(string deviceId, PlcInstanceConfiguration configuration, CancellationToken cancellationToken = default);

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

        /// <summary>检查设备是否连接</summary>
        bool IsDeviceConnected(string deviceId);

        /// <summary>获取连接统计信息</summary>
        object GetConnectionStatistics();

        /// <summary>读取设备SVID值</summary>
        Task<object?> ReadSvidAsync(string deviceId, uint svid, string address, CancellationToken cancellationToken = default);

        /// <summary>写入设备ECID值</summary>
        Task<bool> WriteEcidAsync(string deviceId, uint ecid, string address, object value, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 多实例PLC数据提供者管理器实现
    /// </summary>
    public class MultiPlcDataProviderManager : IMultiPlcDataProviderManager, IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<MultiPlcDataProviderManager> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, PlcConnectionInstance> _connections;
        private readonly SemaphoreSlim _operationSemaphore;
        private bool _disposed;
        private Timer? _healthCheckTimer;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        public event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public MultiPlcDataProviderManager(
            ILogger<MultiPlcDataProviderManager> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _connections = new ConcurrentDictionary<string, PlcConnectionInstance>();
            _operationSemaphore = new SemaphoreSlim(1, 1);

            _logger.LogInformation("多实例PLC数据提供者管理器已创建");
        }

        #endregion

        #region IHostedService实现

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("多实例PLC数据提供者管理器正在启动...");

            // 启动健康检查定时器
            _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _logger.LogInformation("多实例PLC数据提供者管理器启动完成");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("多实例PLC数据提供者管理器正在停止...");

            _healthCheckTimer?.Dispose();
            await StopAllConnectionsAsync(cancellationToken);

            _logger.LogInformation("多实例PLC数据提供者管理器停止完成");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有连接实例
        /// </summary>
        public IEnumerable<PlcConnectionInstance> GetAllConnections()
        {
            return _connections.Values.ToList();
        }

        /// <summary>
        /// 获取指定设备的数据提供者
        /// </summary>
        public IPlcDataProvider? GetDataProvider(string deviceId)
        {
            return _connections.TryGetValue(deviceId, out var instance) ? instance.DataProvider : null;
        }

        /// <summary>
        /// 添加连接实例
        /// </summary>
        public async Task<bool> AddConnectionAsync(string deviceId, PlcInstanceConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_connections.ContainsKey(deviceId))
                {
                    _logger.LogWarning("设备 {DeviceId} 的PLC连接已存在", deviceId);
                    return false;
                }

                _logger.LogInformation("正在添加设备 {DeviceId} 的PLC连接", deviceId);

                // 创建设备特定的数据提供者
                var dataProvider = await CreateDataProviderAsync(deviceId, configuration);
                if (dataProvider == null)
                {
                    _logger.LogError("创建设备 {DeviceId} 的PLC数据提供者失败", deviceId);
                    return false;
                }

                // 创建数据映射器
                var dataMapper = CreateDataMapper(deviceId, configuration);

                var instance = new PlcConnectionInstance
                {
                    DeviceId = deviceId,
                    DataProvider = dataProvider,
                    Configuration = configuration,
                    Enabled = configuration.Enabled,
                    DataMapper = dataMapper
                };

                _connections.TryAdd(deviceId, instance);
                _logger.LogInformation("设备 {DeviceId} 的PLC连接已添加", deviceId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加设备 {DeviceId} 的PLC连接失败", deviceId);
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
                    _logger.LogWarning("设备 {DeviceId} 的PLC连接不存在", deviceId);
                    return false;
                }

                _logger.LogInformation("正在移除设备 {DeviceId} 的PLC连接", deviceId);

                // 停止并释放连接
                await instance.DataProvider.DisconnectAsync();
                if (instance.DataProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _logger.LogInformation("设备 {DeviceId} 的PLC连接已移除", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除设备 {DeviceId} 的PLC连接失败", deviceId);
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
            _logger.LogInformation("开始启动所有PLC连接...");

            var enabledConnections = _connections.Values.Where(c => c.Enabled).ToList();
            var successCount = 0;
            var totalCount = enabledConnections.Count;

            var startTasks = enabledConnections.Select(async connection =>
            {
                try
                {
                    var connected = await connection.DataProvider.ConnectAsync(cancellationToken);
                    if (connected)
                    {
                        connection.LastActivityTime = DateTime.Now;
                        OnConnectionStateChanged(connection.DeviceId, true, null);
                        _logger.LogInformation("设备 {DeviceId} 的PLC连接启动成功", connection.DeviceId);
                        Interlocked.Increment(ref successCount);
                        return true;
                    }
                    else
                    {
                        OnConnectionStateChanged(connection.DeviceId, false, "连接失败");
                        _logger.LogWarning("设备 {DeviceId} 的PLC连接启动失败", connection.DeviceId);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    OnConnectionStateChanged(connection.DeviceId, false, ex.Message);
                    _logger.LogError(ex, "设备 {DeviceId} 的PLC连接启动异常", connection.DeviceId);
                    return false;
                }
            });

            await Task.WhenAll(startTasks);

            _logger.LogInformation("PLC连接启动完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 启动指定连接
        /// </summary>
        public async Task<bool> StartConnectionAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的PLC连接不存在", deviceId);
                return false;
            }

            try
            {
                _logger.LogInformation("正在启动设备 {DeviceId} 的PLC连接", deviceId);
                var connected = await instance.DataProvider.ConnectAsync(cancellationToken);
                
                if (connected)
                {
                    instance.LastActivityTime = DateTime.Now;
                    OnConnectionStateChanged(deviceId, true, null);
                    _logger.LogInformation("设备 {DeviceId} 的PLC连接启动成功", deviceId);
                    return true;
                }
                else
                {
                    OnConnectionStateChanged(deviceId, false, "连接失败");
                    _logger.LogWarning("设备 {DeviceId} 的PLC连接启动失败", deviceId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnConnectionStateChanged(deviceId, false, ex.Message);
                _logger.LogError(ex, "设备 {DeviceId} 的PLC连接启动异常", deviceId);
                return false;
            }
        }

        /// <summary>
        /// 停止所有连接
        /// </summary>
        public async Task<bool> StopAllConnectionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("开始停止所有PLC连接...");

            var connections = _connections.Values.ToList();
            var successCount = 0;
            var totalCount = connections.Count;

            var stopTasks = connections.Select(async connection =>
            {
                try
                {
                    await connection.DataProvider.DisconnectAsync();
                    OnConnectionStateChanged(connection.DeviceId, false, "主动断开");
                    _logger.LogInformation("设备 {DeviceId} 的PLC连接停止成功", connection.DeviceId);
                    Interlocked.Increment(ref successCount);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备 {DeviceId} 的PLC连接停止失败", connection.DeviceId);
                    return false;
                }
            });

            await Task.WhenAll(stopTasks);

            _logger.LogInformation("PLC连接停止完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 停止指定连接
        /// </summary>
        public async Task<bool> StopConnectionAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的PLC连接不存在", deviceId);
                return false;
            }

            try
            {
                _logger.LogInformation("正在停止设备 {DeviceId} 的PLC连接", deviceId);
                await instance.DataProvider.DisconnectAsync();
                OnConnectionStateChanged(deviceId, false, "主动断开");
                _logger.LogInformation("设备 {DeviceId} 的PLC连接停止成功", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备 {DeviceId} 的PLC连接停止失败", deviceId);
                return false;
            }
        }

        /// <summary>
        /// 检查设备是否连接
        /// </summary>
        public bool IsDeviceConnected(string deviceId)
        {
            return _connections.TryGetValue(deviceId, out var instance) && instance.DataProvider.IsConnected;
        }

        /// <summary>
        /// 读取设备SVID值
        /// </summary>
        public async Task<object?> ReadSvidAsync(string deviceId, uint svid, string address, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的PLC连接不存在", deviceId);
                return null;
            }

            try
            {
                var result = await instance.DataProvider.ReadSvidAsync(svid, address, cancellationToken);
                instance.LastActivityTime = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取设备 {DeviceId} SVID {Svid} 失败", deviceId, svid);
                return null;
            }
        }

        /// <summary>
        /// 写入设备ECID值
        /// </summary>
        public async Task<bool> WriteEcidAsync(string deviceId, uint ecid, string address, object value, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备 {DeviceId} 的PLC连接不存在", deviceId);
                return false;
            }

            try
            {
                var result = await instance.DataProvider.WriteEcidAsync(ecid, address, value, cancellationToken);
                instance.LastActivityTime = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入设备 {DeviceId} ECID {Ecid} 失败", deviceId, ecid);
                return false;
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        public object GetConnectionStatistics()
        {
            var connections = GetAllConnections().ToList();
            var connectedCount = connections.Count(c => c.DataProvider.IsConnected);
            var totalCount = connections.Count;

            return new
            {
                TotalConnections = totalCount,
                ConnectedConnections = connectedCount,
                DisconnectedConnections = totalCount - connectedCount,
                Connections = connections.Select(c => new
                {
                    c.DeviceId,
                    IsConnected = c.DataProvider.IsConnected,
                    //IsSimulation = c.DataProvider.IsSimulationMode,
                    c.LastActivityTime,
                    c.CreatedTime,
                    //Statistics = c.DataProvider.Statistics,
                    Configuration = new
                    {
                        c.Configuration.IpAddress,
                        c.Configuration.Port,
                        c.Configuration.PlcType,
                        c.Configuration.UseSimulation
                    }
                })
            };
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 创建PLC数据提供者
        /// </summary>
        private async Task<IPlcDataProvider?> CreateDataProviderAsync(string deviceId, PlcInstanceConfiguration configuration)
        {
            try
            {
                // 创建设备特定的配置
                var deviceConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["PLC:UseSimulation"] = configuration.UseSimulation.ToString(),
                        ["PLC:IpAddress"] = configuration.IpAddress,
                        ["PLC:Port"] = configuration.Port.ToString(),
                        ["PLC:NetworkNumber"] = configuration.NetworkNumber.ToString(),
                        ["PLC:StationNumber"] = configuration.StationNumber.ToString(),
                        ["PLC:ConnectTimeout"] = configuration.Connection.ConnectTimeout.ToString(),
                        ["PLC:ReceiveTimeout"] = configuration.Connection.ReceiveTimeout.ToString(),
                        ["PLC:PollingInterval"] = configuration.Connection.PollingInterval.ToString(),
                        ["PLC:MaxRetryCount"] = configuration.Connection.MaxRetryCount.ToString(),
                        ["PLC:EnableAutoReconnect"] = configuration.Connection.EnableAutoReconnect.ToString(),
                        ["PLC:ReconnectInterval"] = configuration.Connection.ReconnectInterval.ToString()
                    })
                    .Build();

                // 创建设备特定的日志记录器
                var loggerFactory = _serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                var deviceLogger = loggerFactory?.CreateLogger($"PlcDataProvider.{deviceId}");

                if (deviceLogger == null)
                {
                    _logger.LogError("无法创建设备 {DeviceId} 的日志记录器", deviceId);
                    return null;
                }

                // 创建数据映射器
                var dataMapper = CreateDataMapper(deviceId, configuration);

                // 创建PLC数据提供者实例
                var dataProvider = new PlcDataProviderImpl((ILogger<PlcDataProviderImpl>)deviceLogger, deviceConfig, dataMapper);

                return dataProvider;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建设备 {DeviceId} 的PLC数据提供者失败", deviceId);
                return null;
            }
        }

        /// <summary>
        /// 创建数据映射器
        /// </summary>
        private PlcDataMapper CreateDataMapper(string deviceId, PlcInstanceConfiguration configuration)
        {
            try
            {
                // 从服务提供者获取ILogger<PlcDataMapper>实例
                var loggerFactory = _serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                var logger = loggerFactory?.CreateLogger<PlcDataMapper>();

                if (logger == null)
                {
                    _logger.LogError("无法创建设备 {DeviceId} 的数据映射器日志记录器", deviceId);
                    throw new InvalidOperationException("无法获取ILogger<PlcDataMapper>实例");
                }

                // 创建数据映射器实例
                var dataMapper = new PlcDataMapper(logger);

                // TODO: 根据配置初始化映射规则
                // 可以从配置文件或数据库加载设备特定的映射规则

                return dataMapper;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建设备 {DeviceId} 的数据映射器失败", deviceId);
                throw;
            }
        }

        /// <summary>
        /// 连接状态变更事件处理
        /// </summary>
        private void OnConnectionStateChanged(string deviceId, bool isConnected, string? errorMessage)
        {
            _logger.LogInformation("设备 {DeviceId} PLC连接状态变更: {Status} {Error}", 
                deviceId, isConnected ? "已连接" : "已断开", errorMessage ?? "");

            ConnectionStateChanged?.Invoke(this, new PlcConnectionStateChangedEventArgs
            {
                DeviceId = deviceId,
                IsConnected = isConnected,
                ErrorMessage = errorMessage,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private async void PerformHealthCheck(object? state)
        {
            try
            {
                var connections = GetAllConnections().ToList();
                foreach (var connection in connections)
                {
                    // 检查连接状态
                    var wasConnected = connection.DataProvider.IsConnected;
                    // TODO: 这里可以添加更详细的健康检查逻辑
                    
                    // 如果启用了自动重连且连接断开，尝试重连
                    if (!wasConnected && connection.Configuration.Connection.EnableAutoReconnect)
                    {
                        _logger.LogInformation("尝试重连设备 {DeviceId} 的PLC", connection.DeviceId);
                        try
                        {
                            await connection.DataProvider.ConnectAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "设备 {DeviceId} PLC自动重连失败", connection.DeviceId);
                        }
                    }
                }

                _logger.LogDebug("PLC健康检查完成，检查了 {Count} 个连接", connections.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC健康检查执行失败");
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

            _logger.LogInformation("正在释放多实例PLC数据提供者管理器...");

            _healthCheckTimer?.Dispose();

            var stopTask = StopAllConnectionsAsync();
            stopTask.Wait(TimeSpan.FromSeconds(30));

            foreach (var instance in _connections.Values)
            {
                if (instance.DataProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _connections.Clear();
            _operationSemaphore?.Dispose();

            _disposed = true;
            _logger.LogInformation("多实例PLC数据提供者管理器已释放");
        }

        #endregion
    }
}