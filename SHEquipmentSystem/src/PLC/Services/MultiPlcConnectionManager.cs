using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// 多台PLC连接管理器
    /// 支持同时管理多台PLC设备的连接、配置和状态监控
    /// </summary>
    public class MultiPlcConnectionManager : IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<MultiPlcConnectionManager> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// PLC连接实例字典 - 线程安全
        /// </summary>
        private readonly ConcurrentDictionary<string, PlcConnectionInstance> _plcConnections;

        /// <summary>
        /// PLC配置字典
        /// </summary>
        private readonly ConcurrentDictionary<string, PlcConfiguration> _plcConfigurations;

        /// <summary>
        /// 连接状态监控定时器
        /// </summary>
        private readonly Dictionary<string, Timer> _healthCheckTimers;

        /// <summary>
        /// 配置更新锁
        /// </summary>
        private readonly ReaderWriterLockSlim _configLock = new();

        /// <summary>
        /// 取消令牌源
        /// </summary>
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region 事件定义

        /// <summary>
        /// PLC连接状态变化事件
        /// </summary>
        public event EventHandler<PlcConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// PLC配置更新事件
        /// </summary>
        public event EventHandler<PlcConfigurationEventArgs> ConfigurationUpdated;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public MultiPlcConnectionManager(
            ILogger<MultiPlcConnectionManager> logger,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _plcConnections = new ConcurrentDictionary<string, PlcConnectionInstance>();
            _plcConfigurations = new ConcurrentDictionary<string, PlcConfiguration>();
            _healthCheckTimers = new Dictionary<string, Timer>();

            LoadConfigurations();
        }

        #endregion

        #region IHostedService 实现

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在启动多台PLC连接管理器...");

            _cancellationTokenSource = new CancellationTokenSource();

            // 初始化所有PLC连接
            await InitializeAllPlcConnectionsAsync();

            // 启动健康检查
            StartHealthCheckTimers();

            _logger.LogInformation($"多台PLC连接管理器已启动，管理 {_plcConnections.Count} 台PLC设备");
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止多台PLC连接管理器...");

            // 停止健康检查
            StopHealthCheckTimers();

            // 断开所有PLC连接
            await DisconnectAllPlcAsync();

            _cancellationTokenSource?.Cancel();

            _logger.LogInformation("多台PLC连接管理器已停止");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有PLC配置
        /// </summary>
        public Dictionary<string, PlcConfiguration> GetAllPlcConfigurations()
        {
            _configLock.EnterReadLock();
            try
            {
                return new Dictionary<string, PlcConfiguration>(_plcConfigurations);
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取指定PLC配置
        /// </summary>
        public PlcConfiguration GetPlcConfiguration(string plcId)
        {
            if (string.IsNullOrWhiteSpace(plcId))
                throw new ArgumentException("PLC ID不能为空", nameof(plcId));

            return _plcConfigurations.TryGetValue(plcId, out var config) ? config : null;
        }

        /// <summary>
        /// 更新PLC配置
        /// </summary>
        public async Task<bool> UpdatePlcConfigurationAsync(string plcId, PlcConfiguration newConfig)
        {
            if (string.IsNullOrWhiteSpace(plcId))
                throw new ArgumentException("PLC ID不能为空", nameof(plcId));

            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            try
            {
                // 验证配置
                if (!ValidateConfiguration(newConfig))
                {
                    _logger.LogWarning($"PLC配置验证失败: {plcId}");
                    return false;
                }

                _configLock.EnterWriteLock();
                try
                {
                    // 更新配置
                    _plcConfigurations.AddOrUpdate(plcId, newConfig, (key, oldValue) => newConfig);

                    // 如果连接已存在，重新初始化
                    if (_plcConnections.TryGetValue(plcId, out var existingConnection))
                    {
                        await existingConnection.DisconnectAsync();
                        await InitializePlcConnectionAsync(plcId, newConfig);
                    }

                    _logger.LogInformation($"PLC配置已更新: {plcId}");

                    // 触发配置更新事件
                    ConfigurationUpdated?.Invoke(this, new PlcConfigurationEventArgs(plcId, newConfig));

                    return true;
                }
                finally
                {
                    _configLock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新PLC配置失败: {plcId}");
                return false;
            }
        }

        /// <summary>
        /// 添加新的PLC配置
        /// </summary>
        public async Task<bool> AddPlcConfigurationAsync(string plcId, PlcConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(plcId))
                throw new ArgumentException("PLC ID不能为空", nameof(plcId));

            if (_plcConfigurations.ContainsKey(plcId))
            {
                _logger.LogWarning($"PLC ID已存在: {plcId}");
                return false;
            }

            return await UpdatePlcConfigurationAsync(plcId, config);
        }

        /// <summary>
        /// 移除PLC配置
        /// </summary>
        public async Task<bool> RemovePlcConfigurationAsync(string plcId)
        {
            if (string.IsNullOrWhiteSpace(plcId))
                throw new ArgumentException("PLC ID不能为空", nameof(plcId));

            try
            {
                _configLock.EnterWriteLock();
                try
                {
                    // 断开连接
                    if (_plcConnections.TryRemove(plcId, out var connection))
                    {
                        await connection.DisconnectAsync();
                        connection.Dispose();
                    }

                    // 停止健康检查
                    if (_healthCheckTimers.TryGetValue(plcId, out var timer))
                    {
                        timer?.Dispose();
                        _healthCheckTimers.Remove(plcId);
                    }

                    // 移除配置
                    _plcConfigurations.TryRemove(plcId, out _);

                    _logger.LogInformation($"PLC配置已移除: {plcId}");
                    return true;
                }
                finally
                {
                    _configLock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"移除PLC配置失败: {plcId}");
                return false;
            }
        }

        /// <summary>
        /// 获取所有PLC连接状态
        /// </summary>
        public Dictionary<string, PlcConnectionStatus> GetAllConnectionStatus()
        {
            var statusDict = new Dictionary<string, PlcConnectionStatus>();

            foreach (var kvp in _plcConnections)
            {
                statusDict[kvp.Key] = new PlcConnectionStatus
                {
                    PlcId = kvp.Key,
                    IsConnected = kvp.Value.IsConnected,
                    LastConnectTime = kvp.Value.LastConnectTime,
                    LastDisconnectTime = kvp.Value.LastDisconnectTime,
                    ConnectionCount = kvp.Value.ConnectionCount,
                    ErrorCount = kvp.Value.ErrorCount,
                    LastErrorMessage = kvp.Value.LastErrorMessage
                };
            }

            return statusDict;
        }

        /// <summary>
        /// 获取指定PLC连接状态
        /// </summary>
        public PlcConnectionStatus GetConnectionStatus(string plcId)
        {
            if (_plcConnections.TryGetValue(plcId, out var connection))
            {
                return new PlcConnectionStatus
                {
                    PlcId = plcId,
                    IsConnected = connection.IsConnected,
                    LastConnectTime = connection.LastConnectTime,
                    LastDisconnectTime = connection.LastDisconnectTime,
                    ConnectionCount = connection.ConnectionCount,
                    ErrorCount = connection.ErrorCount,
                    LastErrorMessage = connection.LastErrorMessage
                };
            }

            return null;
        }

        /// <summary>
        /// 连接指定PLC
        /// </summary>
        public async Task<bool> ConnectPlcAsync(string plcId)
        {
            if (_plcConnections.TryGetValue(plcId, out var connection))
            {
                return await connection.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            }

            _logger.LogWarning($"未找到指定的PLC连接: {plcId}");
            return false;
        }

        /// <summary>
        /// 断开指定PLC
        /// </summary>
        public async Task<bool> DisconnectPlcAsync(string plcId)
        {
            if (_plcConnections.TryGetValue(plcId, out var connection))
            {
                await connection.DisconnectAsync();
                return true;
            }

            _logger.LogWarning($"未找到指定的PLC连接: {plcId}");
            return false;
        }

        /// <summary>
        /// 重新连接指定PLC
        /// </summary>
        public async Task<bool> ReconnectPlcAsync(string plcId)
        {
            if (_plcConnections.TryGetValue(plcId, out var connection))
            {
                await connection.DisconnectAsync();
                await Task.Delay(1000); // 等待一秒再重连
                return await connection.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            }

            _logger.LogWarning($"未找到指定的PLC连接: {plcId}");
            return false;
        }

        /// <summary>
        /// 测试PLC连接
        /// </summary>
        public async Task<PlcTestResult> TestPlcConnectionAsync(string plcId)
        {
            var result = new PlcTestResult { PlcId = plcId, TestTime = DateTime.Now };

            try
            {
                if (_plcConnections.TryGetValue(plcId, out var connection))
                {
                    var startTime = DateTime.Now;
                    result.IsSuccess = await connection.TestConnectionAsync();
                    result.ResponseTime = DateTime.Now - startTime;
                    result.Message = result.IsSuccess ? "连接测试成功" : "连接测试失败";
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = $"未找到指定的PLC连接: {plcId}";
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"连接测试异常: {ex.Message}";
                _logger.LogError(ex, $"PLC连接测试失败: {plcId}");
            }

            return result;
        }

        /// <summary>
        /// 获取可用的PLC列表（已连接的）
        /// </summary>
        public List<string> GetAvailablePlcIds()
        {
            return _plcConnections
                .Where(kvp => kvp.Value.IsConnected)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// 获取PLC连接实例（用于数据读写）
        /// </summary>
        public PlcConnectionInstance GetPlcConnection(string plcId)
        {
            return _plcConnections.TryGetValue(plcId, out var connection) ? connection : null;
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool ValidateConfiguration(PlcConfiguration config)
        {
            if (config == null) return false;

            if (string.IsNullOrWhiteSpace(config.IpAddress))
            {
                _logger.LogWarning("PLC IP地址不能为空");
                return false;
            }

            if (config.Port <= 0 || config.Port > 65535)
            {
                _logger.LogWarning($"PLC端口号无效: {config.Port}");
                return false;
            }

            if (config.ConnectTimeout < 1000)
            {
                _logger.LogWarning($"连接超时时间太短: {config.ConnectTimeout}ms");
                return false;
            }

            return true;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载所有PLC配置
        /// </summary>
        private void LoadConfigurations()
        {
            try
            {
                var plcSection = _configuration.GetSection("PLCs");

                if (!plcSection.Exists())
                {
                    _logger.LogWarning("未找到PLCs配置节，将使用默认PLC配置");
                    LoadDefaultConfiguration();
                    return;
                }

                foreach (var plcConfig in plcSection.GetChildren())
                {
                    var plcId = plcConfig.Key;
                    var config = plcConfig.Get<PlcConfiguration>();

                    if (config != null && ValidateConfiguration(config))
                    {
                        _plcConfigurations[plcId] = config;
                        _logger.LogInformation($"已加载PLC配置: {plcId} - {config.IpAddress}:{config.Port}");
                    }
                    else
                    {
                        _logger.LogWarning($"PLC配置无效，跳过: {plcId}");
                    }
                }

                _logger.LogInformation($"共加载 {_plcConfigurations.Count} 个PLC配置");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载PLC配置失败");
                LoadDefaultConfiguration();
            }
        }

        /// <summary>
        /// 加载默认配置（兼容单PLC配置）
        /// </summary>
        private void LoadDefaultConfiguration()
        {
            var defaultConfig = new PlcConfiguration
            {
                IpAddress = _configuration["PLC:IpAddress"] ?? "192.168.1.10",
                Port = _configuration.GetValue("PLC:Port", 6000),
                NetworkNumber = _configuration.GetValue<byte>("PLC:NetworkNumber", 0),
                StationNumber = _configuration.GetValue<byte>("PLC:StationNumber", 0),
                ConnectTimeout = _configuration.GetValue("PLC:ConnectTimeout", 5000),
                ReceiveTimeout = _configuration.GetValue("PLC:ReceiveTimeout", 3000),
                MaxRetryCount = _configuration.GetValue("PLC:MaxRetryCount", 3),
                EnableAutoReconnect = _configuration.GetValue("PLC:EnableAutoReconnect", true),
                ReconnectInterval = _configuration.GetValue("PLC:ReconnectInterval", 5000)
            };

            _plcConfigurations["Default"] = defaultConfig;
            _logger.LogInformation($"已加载默认PLC配置: {defaultConfig.IpAddress}:{defaultConfig.Port}");
        }

        /// <summary>
        /// 初始化所有PLC连接
        /// </summary>
        private async Task InitializeAllPlcConnectionsAsync()
        {
            var initializeTasks = new List<Task>();

            foreach (var kvp in _plcConfigurations)
            {
                initializeTasks.Add(InitializePlcConnectionAsync(kvp.Key, kvp.Value));
            }

            await Task.WhenAll(initializeTasks);
        }

        /// <summary>
        /// 初始化单个PLC连接
        /// </summary>
        private async Task InitializePlcConnectionAsync(string plcId, PlcConfiguration config)
        {
            try
            {
                var connection = new PlcConnectionInstance(plcId, config, _logger);

                // 订阅连接状态变化事件
                connection.ConnectionStatusChanged += OnPlcConnectionStatusChanged;

                _plcConnections.AddOrUpdate(plcId, connection, (key, oldValue) =>
                {
                    oldValue?.Dispose();
                    return connection;
                });

                // 尝试连接
                await connection.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

                _logger.LogInformation($"PLC连接已初始化: {plcId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"初始化PLC连接失败: {plcId}");
            }
        }

        /// <summary>
        /// 启动健康检查定时器
        /// </summary>
        private void StartHealthCheckTimers()
        {
            foreach (var plcId in _plcConnections.Keys)
            {
                var timer = new Timer(
                    async _ => await PerformHealthCheckAsync(plcId),
                    null,
                    TimeSpan.FromSeconds(10), // 10秒后开始
                    TimeSpan.FromSeconds(30)  // 每30秒检查一次
                );

                _healthCheckTimers[plcId] = timer;
            }

            _logger.LogInformation($"已启动 {_healthCheckTimers.Count} 个PLC健康检查定时器");
        }

        /// <summary>
        /// 停止健康检查定时器
        /// </summary>
        private void StopHealthCheckTimers()
        {
            foreach (var timer in _healthCheckTimers.Values)
            {
                timer?.Dispose();
            }

            _healthCheckTimers.Clear();
            _logger.LogInformation("已停止所有PLC健康检查定时器");
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private async Task PerformHealthCheckAsync(string plcId)
        {
            try
            {
                if (_plcConnections.TryGetValue(plcId, out var connection))
                {
                    if (!connection.IsConnected &&
                        _plcConfigurations.TryGetValue(plcId, out var config) &&
                        config.EnableAutoReconnect)
                    {
                        _logger.LogInformation($"检测到PLC断开连接，尝试自动重连: {plcId}");
                        await connection.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PLC健康检查失败: {plcId}");
            }
        }

        /// <summary>
        /// 断开所有PLC连接
        /// </summary>
        private async Task DisconnectAllPlcAsync()
        {
            var disconnectTasks = new List<Task>();

            foreach (var connection in _plcConnections.Values)
            {
                disconnectTasks.Add(connection.DisconnectAsync());
            }

            await Task.WhenAll(disconnectTasks);

            foreach (var connection in _plcConnections.Values)
            {
                connection?.Dispose();
            }

            _plcConnections.Clear();
        }

        /// <summary>
        /// PLC连接状态变化事件处理
        /// </summary>
        private void OnPlcConnectionStatusChanged(object sender, PlcConnectionStatusEventArgs e)
        {
            _logger.LogInformation($"PLC连接状态变化: {e.PlcId} - {(e.IsConnected ? "已连接" : "已断开")}");

            // 转发事件
            ConnectionStatusChanged?.Invoke(this, e);
        }

        #endregion

        #region IDisposable 实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                StopHealthCheckTimers();

                foreach (var connection in _plcConnections.Values)
                {
                    connection?.Dispose();
                }

                _plcConnections.Clear();
                _configLock?.Dispose();
                _cancellationTokenSource?.Dispose();

                _disposed = true;
            }
        }

        #endregion
    }

    #region 辅助类和数据结构

    /// <summary>
    /// PLC配置信息（扩展版）
    /// </summary>
    public class PlcConfiguration
    {
        /// <summary>IP地址</summary>
        public string IpAddress { get; set; } = "192.168.1.10";

        /// <summary>端口号</summary>
        public int Port { get; set; } = 6000;

        /// <summary>网络号</summary>
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>站号</summary>
        public byte StationNumber { get; set; } = 0;

        /// <summary>连接超时(毫秒)</summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>接收超时(毫秒)</summary>
        public int ReceiveTimeout { get; set; } = 3000;

        /// <summary>最大重试次数</summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>是否启用自动重连</summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>重连间隔(毫秒)</summary>
        public int ReconnectInterval { get; set; } = 5000;

        /// <summary>PLC描述</summary>
        public string Description { get; set; } = "";

        /// <summary>PLC类型</summary>
        public string PlcType { get; set; } = "Mitsubishi";

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>优先级（用于负载均衡）</summary>
        public int Priority { get; set; } = 1;
    }

    /// <summary>
    /// PLC连接状态
    /// </summary>
    public class PlcConnectionStatus
    {
        public string PlcId { get; set; }
        public bool IsConnected { get; set; }
        public DateTime? LastConnectTime { get; set; }
        public DateTime? LastDisconnectTime { get; set; }
        public int ConnectionCount { get; set; }
        public int ErrorCount { get; set; }
        public string LastErrorMessage { get; set; }
        public TimeSpan? Uptime => LastConnectTime.HasValue && IsConnected
            ? DateTime.Now - LastConnectTime.Value
            : null;
    }

    /// <summary>
    /// PLC测试结果
    /// </summary>
    public class PlcTestResult
    {
        public string PlcId { get; set; }
        public bool IsSuccess { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string Message { get; set; }
        public DateTime TestTime { get; set; }
    }

    /// <summary>
    /// PLC连接状态变化事件参数
    /// </summary>
    public class PlcConnectionStatusEventArgs : EventArgs
    {
        public string PlcId { get; set; }
        public bool IsConnected { get; set; }
        public DateTime EventTime { get; set; }
        public string Message { get; set; }

        public PlcConnectionStatusEventArgs(string plcId, bool isConnected, string message = null)
        {
            PlcId = plcId;
            IsConnected = isConnected;
            EventTime = DateTime.Now;
            Message = message;
        }
    }

    /// <summary>
    /// PLC配置变化事件参数
    /// </summary>
    public class PlcConfigurationEventArgs : EventArgs
    {
        public string PlcId { get; set; }
        public PlcConfiguration Configuration { get; set; }
        public DateTime EventTime { get; set; }

        public PlcConfigurationEventArgs(string plcId, PlcConfiguration configuration)
        {
            PlcId = plcId;
            Configuration = configuration;
            EventTime = DateTime.Now;
        }
    }

    #endregion
}