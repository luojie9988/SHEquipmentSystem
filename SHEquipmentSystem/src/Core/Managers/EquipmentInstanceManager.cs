// 文件路径: src/Core/Managers/EquipmentInstanceManager.cs
// 版本: v1.0.0
// 描述: 设备实例管理器

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiceEquipmentSystem.Core.Managers
{
    /// <summary>
    /// 设备实例状态
    /// </summary>
    public enum EquipmentInstanceState
    {
        /// <summary>未初始化</summary>
        Uninitialized = 0,
        /// <summary>正在初始化</summary>
        Initializing = 1,
        /// <summary>已初始化</summary>
        Initialized = 2,
        /// <summary>正在启动</summary>
        Starting = 3,
        /// <summary>运行中</summary>
        Running = 4,
        /// <summary>正在停止</summary>
        Stopping = 5,
        /// <summary>已停止</summary>
        Stopped = 6,
        /// <summary>错误</summary>
        Error = 7,
        /// <summary>已禁用</summary>
        Disabled = 8
    }

    /// <summary>
    /// 设备实例信息
    /// </summary>
    public class EquipmentInstanceInfo
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>设备名称</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>设备型号</summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>实例状态</summary>
        public EquipmentInstanceState State { get; set; }

        /// <summary>SECS连接状态</summary>
        public bool SecsConnected { get; set; }

        /// <summary>PLC连接状态</summary>
        public bool PlcConnected { get; set; }

        /// <summary>最后更新时间</summary>
        public DateTime LastUpdateTime { get; set; }

        /// <summary>启动时间</summary>
        public DateTime? StartTime { get; set; }

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>配置信息</summary>
        public EquipmentInstanceConfiguration Configuration { get; set; } = new();

        /// <summary>统计信息</summary>
        public EquipmentInstanceStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// 设备实例统计信息
    /// </summary>
    public class EquipmentInstanceStatistics
    {
        /// <summary>SECS消息发送数</summary>
        public long SecMessagesSent { get; set; }

        /// <summary>SECS消息接收数</summary>
        public long SecsMessagesReceived { get; set; }

        /// <summary>PLC读取次数</summary>
        public long PlcReadCount { get; set; }

        /// <summary>PLC写入次数</summary>
        public long PlcWriteCount { get; set; }

        /// <summary>错误计数</summary>
        public long ErrorCount { get; set; }

        /// <summary>重连次数</summary>
        public long ReconnectCount { get; set; }

        /// <summary>运行时长</summary>
        public TimeSpan Uptime { get; set; }
    }

    /// <summary>
    /// 设备实例事件参数
    /// </summary>
    public class EquipmentInstanceEventArgs : EventArgs
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>旧状态</summary>
        public EquipmentInstanceState OldState { get; set; }

        /// <summary>新状态</summary>
        public EquipmentInstanceState NewState { get; set; }

        /// <summary>消息</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>异常</summary>
        public Exception? Exception { get; set; }

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 设备实例管理器接口
    /// </summary>
    public interface IEquipmentInstanceManager
    {
        /// <summary>实例状态变更事件</summary>
        event EventHandler<EquipmentInstanceEventArgs>? InstanceStateChanged;

        /// <summary>获取所有设备实例信息</summary>
        IEnumerable<EquipmentInstanceInfo> GetAllInstances();

        /// <summary>获取指定设备实例信息</summary>
        EquipmentInstanceInfo? GetInstance(string deviceId);

        /// <summary>获取启用的设备实例</summary>
        IEnumerable<EquipmentInstanceInfo> GetEnabledInstances();

        /// <summary>获取运行中的设备实例</summary>
        IEnumerable<EquipmentInstanceInfo> GetRunningInstances();

        /// <summary>启动所有设备实例</summary>
        Task<bool> StartAllInstancesAsync(CancellationToken cancellationToken = default);

        /// <summary>启动指定设备实例</summary>
        Task<bool> StartInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>停止所有设备实例</summary>
        Task<bool> StopAllInstancesAsync(CancellationToken cancellationToken = default);

        /// <summary>停止指定设备实例</summary>
        Task<bool> StopInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>重启指定设备实例</summary>
        Task<bool> RestartInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>初始化管理器</summary>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>验证配置</summary>
        bool ValidateConfiguration(out List<string> errors);

        /// <summary>获取系统统计信息</summary>
        object GetSystemStatistics();
    }

    /// <summary>
    /// 设备实例管理器实现
    /// </summary>
    public class EquipmentInstanceManager : IEquipmentInstanceManager, IDisposable
    {
        #region 私有字段

        private readonly ILogger<EquipmentInstanceManager> _logger;
        private readonly MultiEquipmentSystemConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, EquipmentInstanceInfo> _instances;
        private readonly SemaphoreSlim _operationSemaphore;
        private bool _disposed;
        private Timer? _healthCheckTimer;

        #endregion

        #region 事件

        /// <summary>
        /// 实例状态变更事件
        /// </summary>
        public event EventHandler<EquipmentInstanceEventArgs>? InstanceStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public EquipmentInstanceManager(
            ILogger<EquipmentInstanceManager> logger,
            IOptions<MultiEquipmentSystemConfiguration> configuration,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _instances = new ConcurrentDictionary<string, EquipmentInstanceInfo>();
            _operationSemaphore = new SemaphoreSlim(1, 1);

            _logger.LogInformation("设备实例管理器已创建");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化管理器
        /// </summary>
        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("正在初始化设备实例管理器...");

                // 验证配置
                if (!ValidateConfiguration(out var errors))
                {
                    _logger.LogError("配置验证失败: {Errors}", string.Join(", ", errors));
                    return false;
                }

                // 创建设备实例信息
                foreach (var config in _configuration.EquipmentInstances.Where(c => c.Enabled))
                {
                    var instanceInfo = new EquipmentInstanceInfo
                    {
                        DeviceId = config.DeviceId,
                        DeviceName = config.DeviceName,
                        ModelName = config.ModelName,
                        State = EquipmentInstanceState.Initialized,
                        Configuration = config,
                        LastUpdateTime = DateTime.Now
                    };

                    _instances.TryAdd(config.DeviceId, instanceInfo);
                    _logger.LogInformation("设备实例已注册: {DeviceId} - {DeviceName}", config.DeviceId, config.DeviceName);
                }

                // 启动健康检查定时器
                StartHealthCheckTimer();

                _logger.LogInformation("设备实例管理器初始化完成，共注册 {Count} 个设备实例", _instances.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化设备实例管理器失败");
                return false;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 获取所有设备实例信息
        /// </summary>
        public IEnumerable<EquipmentInstanceInfo> GetAllInstances()
        {
            return _instances.Values.ToList();
        }

        /// <summary>
        /// 获取指定设备实例信息
        /// </summary>
        public EquipmentInstanceInfo? GetInstance(string deviceId)
        {
            return _instances.TryGetValue(deviceId, out var instance) ? instance : null;
        }

        /// <summary>
        /// 获取启用的设备实例
        /// </summary>
        public IEnumerable<EquipmentInstanceInfo> GetEnabledInstances()
        {
            return _instances.Values.Where(i => i.Configuration.Enabled).ToList();
        }

        /// <summary>
        /// 获取运行中的设备实例
        /// </summary>
        public IEnumerable<EquipmentInstanceInfo> GetRunningInstances()
        {
            return _instances.Values.Where(i => i.State == EquipmentInstanceState.Running).ToList();
        }

        /// <summary>
        /// 启动所有设备实例
        /// </summary>
        public async Task<bool> StartAllInstancesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("开始启动所有设备实例...");

            var enabledInstances = GetEnabledInstances()
                .OrderBy(i => i.Configuration.Priority)
                .ToList();

            var successCount = 0;
            var totalCount = enabledInstances.Count;

            foreach (var instance in enabledInstances)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (await StartInstanceAsync(instance.DeviceId, cancellationToken))
                {
                    successCount++;
                }

                // 启动间隔，避免同时启动过多实例
                await Task.Delay(1000, cancellationToken);
            }

            _logger.LogInformation("设备实例启动完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 启动指定设备实例
        /// </summary>
        public async Task<bool> StartInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_instances.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备实例不存在: {DeviceId}", deviceId);
                return false;
            }

            if (instance.State == EquipmentInstanceState.Running)
            {
                _logger.LogInformation("设备实例已在运行: {DeviceId}", deviceId);
                return true;
            }

            try
            {
                _logger.LogInformation("正在启动设备实例: {DeviceId}", deviceId);
                UpdateInstanceState(instance, EquipmentInstanceState.Starting, "正在启动设备实例");

                // TODO: 在这里启动具体的设备实例服务
                // 这将在后续任务中实现

                instance.StartTime = DateTime.Now;
                UpdateInstanceState(instance, EquipmentInstanceState.Running, "设备实例启动成功");

                _logger.LogInformation("设备实例启动成功: {DeviceId}", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动设备实例失败: {DeviceId}", deviceId);
                UpdateInstanceState(instance, EquipmentInstanceState.Error, $"启动失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 停止所有设备实例
        /// </summary>
        public async Task<bool> StopAllInstancesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("开始停止所有设备实例...");

            var runningInstances = GetRunningInstances()
                .OrderByDescending(i => i.Configuration.Priority)
                .ToList();

            var successCount = 0;
            var totalCount = runningInstances.Count;

            foreach (var instance in runningInstances)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (await StopInstanceAsync(instance.DeviceId, cancellationToken))
                {
                    successCount++;
                }
            }

            _logger.LogInformation("设备实例停止完成: {SuccessCount}/{TotalCount}", successCount, totalCount);
            return successCount == totalCount;
        }

        /// <summary>
        /// 停止指定设备实例
        /// </summary>
        public async Task<bool> StopInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (!_instances.TryGetValue(deviceId, out var instance))
            {
                _logger.LogWarning("设备实例不存在: {DeviceId}", deviceId);
                return false;
            }

            if (instance.State == EquipmentInstanceState.Stopped)
            {
                _logger.LogInformation("设备实例已停止: {DeviceId}", deviceId);
                return true;
            }

            try
            {
                _logger.LogInformation("正在停止设备实例: {DeviceId}", deviceId);
                UpdateInstanceState(instance, EquipmentInstanceState.Stopping, "正在停止设备实例");

                // TODO: 在这里停止具体的设备实例服务
                // 这将在后续任务中实现

                UpdateInstanceState(instance, EquipmentInstanceState.Stopped, "设备实例停止成功");

                _logger.LogInformation("设备实例停止成功: {DeviceId}", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止设备实例失败: {DeviceId}", deviceId);
                UpdateInstanceState(instance, EquipmentInstanceState.Error, $"停止失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 重启指定设备实例
        /// </summary>
        public async Task<bool> RestartInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("正在重启设备实例: {DeviceId}", deviceId);

            if (await StopInstanceAsync(deviceId, cancellationToken))
            {
                // 等待一段时间确保完全停止
                await Task.Delay(2000, cancellationToken);
                return await StartInstanceAsync(deviceId, cancellationToken);
            }

            return false;
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool ValidateConfiguration(out List<string> errors)
        {
            return _configuration.Validate(out errors);
        }

        /// <summary>
        /// 获取系统统计信息
        /// </summary>
        public object GetSystemStatistics()
        {
            var instances = GetAllInstances().ToList();
            var runningCount = instances.Count(i => i.State == EquipmentInstanceState.Running);
            var errorCount = instances.Count(i => i.State == EquipmentInstanceState.Error);

            return new
            {
                TotalInstances = instances.Count,
                RunningInstances = runningCount,
                ErrorInstances = errorCount,
                SystemUptime = DateTime.Now,
                Instances = instances.Select(i => new
                {
                    i.DeviceId,
                    i.DeviceName,
                    State = i.State.ToString(),
                    i.SecsConnected,
                    i.PlcConnected,
                    i.LastUpdateTime,
                    i.StartTime,
                    i.Statistics
                })
            };
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 更新实例状态
        /// </summary>
        private void UpdateInstanceState(EquipmentInstanceInfo instance, EquipmentInstanceState newState, string message, Exception? exception = null)
        {
            var oldState = instance.State;
            instance.State = newState;
            instance.LastUpdateTime = DateTime.Now;

            if (exception != null)
            {
                instance.ErrorMessage = exception.Message;
                instance.Statistics.ErrorCount++;
            }
            else if (newState != EquipmentInstanceState.Error)
            {
                instance.ErrorMessage = null;
            }

            // 触发状态变更事件
            InstanceStateChanged?.Invoke(this, new EquipmentInstanceEventArgs
            {
                DeviceId = instance.DeviceId,
                OldState = oldState,
                NewState = newState,
                Message = message,
                Exception = exception
            });

            _logger.LogInformation("设备实例状态变更: {DeviceId} {OldState} -> {NewState}: {Message}",
                instance.DeviceId, oldState, newState, message);
        }

        /// <summary>
        /// 启动健康检查定时器
        /// </summary>
        private void StartHealthCheckTimer()
        {
            var interval = TimeSpan.FromSeconds(_configuration.System.HealthCheckInterval);
            _healthCheckTimer = new Timer(PerformHealthCheck, null, interval, interval);
            _logger.LogDebug("健康检查定时器已启动，间隔: {Interval}秒", _configuration.System.HealthCheckInterval);
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private async void PerformHealthCheck(object? state)
        {
            try
            {
                var instances = GetAllInstances().ToList();
                foreach (var instance in instances)
                {
                    // 更新运行时长
                    if (instance.StartTime.HasValue && instance.State == EquipmentInstanceState.Running)
                    {
                        instance.Statistics.Uptime = DateTime.Now - instance.StartTime.Value;
                    }

                    // TODO: 在这里执行具体的健康检查逻辑
                    // 例如检查SECS和PLC连接状态
                }

                _logger.LogDebug("健康检查完成，检查了 {Count} 个设备实例", instances.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康检查执行失败");
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

            _healthCheckTimer?.Dispose();
            _operationSemaphore?.Dispose();

            _disposed = true;
            _logger.LogInformation("设备实例管理器已释放");
        }

        #endregion
    }
}