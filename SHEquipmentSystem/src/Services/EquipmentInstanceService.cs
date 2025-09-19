// 文件路径: src/Services/EquipmentInstanceService.cs
// 版本: v1.0.0
// 描述: 设备实例服务 - 整合SECS和PLC为完整的设备实例

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Managers;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 设备实例运行状态
    /// </summary>
    public class DeviceInstanceState
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>数据模型</summary>
        public DiceDataModel DataModel { get; set; } = new();

        /// <summary>状态服务</summary>
        public IEquipmentStateService? StateService { get; set; }

        /// <summary>数据采集服务</summary>
        public IDataCollectionService? DataCollectionService { get; set; }

        /// <summary>事件报告服务</summary>
        public IEventReportService? EventReportService { get; set; }

        /// <summary>最后更新时间</summary>
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;

        /// <summary>错误信息</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>统计信息</summary>
        public DeviceInstanceStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// 设备实例统计信息
    /// </summary>
    public class DeviceInstanceStatistics
    {
        /// <summary>运行时长</summary>
        public TimeSpan RunTime { get; set; }

        /// <summary>SECS消息数</summary>
        public long SecsMessageCount { get; set; }

        /// <summary>PLC交互次数</summary>
        public long PlcInteractionCount { get; set; }

        /// <summary>状态变更次数</summary>
        public long StateChangeCount { get; set; }

        /// <summary>错误次数</summary>
        public long ErrorCount { get; set; }

        /// <summary>开始时间</summary>
        public DateTime StartTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 设备实例事件参数
    /// </summary>
    public class DeviceInstanceEventArgs : EventArgs
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>事件类型</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>消息</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>相关数据</summary>
        public object? Data { get; set; }
    }

    /// <summary>
    /// 设备实例服务接口
    /// </summary>
    public interface IEquipmentInstanceService
    {
        /// <summary>设备实例事件</summary>
        event EventHandler<DeviceInstanceEventArgs>? DeviceInstanceEvent;

        /// <summary>获取所有设备实例状态</summary>
        IEnumerable<DeviceInstanceState> GetAllDeviceStates();

        /// <summary>获取指定设备实例状态</summary>
        DeviceInstanceState? GetDeviceState(string deviceId);

        /// <summary>获取设备数据模型</summary>
        DiceDataModel? GetDeviceDataModel(string deviceId);

        /// <summary>获取设备状态服务</summary>
        IEquipmentStateService? GetDeviceStateService(string deviceId);

        /// <summary>启动设备实例</summary>
        Task<bool> StartDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>停止设备实例</summary>
        Task<bool> StopDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>重启设备实例</summary>
        Task<bool> RestartDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default);

        /// <summary>更新设备数据</summary>
        Task UpdateDeviceDataAsync(string deviceId);

        /// <summary>获取设备健康状态</summary>
        object GetDeviceHealthStatus(string deviceId);

        /// <summary>获取系统概览</summary>
        object GetSystemOverview();
    }

    /// <summary>
    /// 设备实例服务实现
    /// </summary>
    public class EquipmentInstanceService : IEquipmentInstanceService, IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<EquipmentInstanceService> _logger;
        private readonly MultiEquipmentSystemConfiguration _configuration;
        private readonly IEquipmentInstanceManager _instanceManager;
        private readonly IMultiSecsConnectionManager _secsManager;
        private readonly IMultiPlcDataProviderManager _plcManager;
        private readonly IServiceProvider _serviceProvider;

        private readonly ConcurrentDictionary<string, DeviceInstanceState> _deviceStates;
        private readonly SemaphoreSlim _operationSemaphore;
        private Timer? _dataUpdateTimer;
        private Timer? _healthCheckTimer;
        private bool _disposed;

        #endregion

        #region 事件

        /// <summary>
        /// 设备实例事件
        /// </summary>
        public event EventHandler<DeviceInstanceEventArgs>? DeviceInstanceEvent;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public EquipmentInstanceService(
            ILogger<EquipmentInstanceService> logger,
            IOptions<MultiEquipmentSystemConfiguration> configuration,
            IEquipmentInstanceManager instanceManager,
            IMultiSecsConnectionManager secsManager,
            IMultiPlcDataProviderManager plcManager,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _instanceManager = instanceManager ?? throw new ArgumentNullException(nameof(instanceManager));
            _secsManager = secsManager ?? throw new ArgumentNullException(nameof(secsManager));
            _plcManager = plcManager ?? throw new ArgumentNullException(nameof(plcManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _deviceStates = new ConcurrentDictionary<string, DeviceInstanceState>();
            _operationSemaphore = new SemaphoreSlim(1, 1);

            // 订阅管理器事件
            SubscribeToManagerEvents();

            _logger.LogInformation("设备实例服务已创建");
        }

        #endregion

        #region IHostedService实现

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("设备实例服务正在启动...");

            try
            {
                // 初始化设备实例管理器
                await _instanceManager.InitializeAsync(cancellationToken);

                // 创建设备实例
                await CreateDeviceInstancesAsync(cancellationToken);

                // 启动SECS和PLC连接
                await StartConnectionsAsync(cancellationToken);

                // 启动设备实例
                await _instanceManager.StartAllInstancesAsync(cancellationToken);

                // 启动定时器
                StartTimers();

                _logger.LogInformation("设备实例服务启动完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备实例服务启动失败");
                throw;
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("设备实例服务正在停止...");

            try
            {
                // 停止定时器
                _dataUpdateTimer?.Dispose();
                _healthCheckTimer?.Dispose();

                // 停止设备实例
                await _instanceManager.StopAllInstancesAsync(cancellationToken);

                // 停止连接
                await _secsManager.StopAllConnectionsAsync(cancellationToken);
                await _plcManager.StopAllConnectionsAsync(cancellationToken);

                _logger.LogInformation("设备实例服务停止完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备实例服务停止失败");
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有设备实例状态
        /// </summary>
        public IEnumerable<DeviceInstanceState> GetAllDeviceStates()
        {
            return _deviceStates.Values.ToList();
        }

        /// <summary>
        /// 获取指定设备实例状态
        /// </summary>
        public DeviceInstanceState? GetDeviceState(string deviceId)
        {
            return _deviceStates.TryGetValue(deviceId, out var state) ? state : null;
        }

        /// <summary>
        /// 获取设备数据模型
        /// </summary>
        public DiceDataModel? GetDeviceDataModel(string deviceId)
        {
            return GetDeviceState(deviceId)?.DataModel;
        }

        /// <summary>
        /// 获取设备状态服务
        /// </summary>
        public IEquipmentStateService? GetDeviceStateService(string deviceId)
        {
            return GetDeviceState(deviceId)?.StateService;
        }

        /// <summary>
        /// 启动设备实例
        /// </summary>
        public async Task<bool> StartDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("启动设备实例: {DeviceId}", deviceId);

                // 启动SECS连接
                var secsStarted = await _secsManager.StartConnectionAsync(deviceId, cancellationToken);
                
                // 启动PLC连接
                var plcStarted = await _plcManager.StartConnectionAsync(deviceId, cancellationToken);

                // 启动设备实例管理器中的实例
                var instanceStarted = await _instanceManager.StartInstanceAsync(deviceId, cancellationToken);

                // 初始化设备状态
                if (instanceStarted && _deviceStates.TryGetValue(deviceId, out var deviceState))
                {
                    await InitializeDeviceStateAsync(deviceState, cancellationToken);
                }

                var success = secsStarted && plcStarted && instanceStarted;
                
                if (success)
                {
                    OnDeviceInstanceEvent(deviceId, "DeviceStarted", "设备实例启动成功");
                }
                else
                {
                    OnDeviceInstanceEvent(deviceId, "DeviceStartFailed", 
                        $"设备实例启动失败 - SECS:{secsStarted}, PLC:{plcStarted}, Instance:{instanceStarted}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动设备实例失败: {DeviceId}", deviceId);
                OnDeviceInstanceEvent(deviceId, "DeviceStartError", ex.Message);
                return false;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 停止设备实例
        /// </summary>
        public async Task<bool> StopDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("停止设备实例: {DeviceId}", deviceId);

                // 停止设备实例
                var instanceStopped = await _instanceManager.StopInstanceAsync(deviceId, cancellationToken);

                // 停止连接
                var secsStopped = await _secsManager.StopConnectionAsync(deviceId, cancellationToken);
                var plcStopped = await _plcManager.StopConnectionAsync(deviceId, cancellationToken);

                var success = instanceStopped && secsStopped && plcStopped;

                if (success)
                {
                    OnDeviceInstanceEvent(deviceId, "DeviceStopped", "设备实例停止成功");
                }
                else
                {
                    OnDeviceInstanceEvent(deviceId, "DeviceStopFailed", 
                        $"设备实例停止失败 - SECS:{secsStopped}, PLC:{plcStopped}, Instance:{instanceStopped}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止设备实例失败: {DeviceId}", deviceId);
                OnDeviceInstanceEvent(deviceId, "DeviceStopError", ex.Message);
                return false;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        /// <summary>
        /// 重启设备实例
        /// </summary>
        public async Task<bool> RestartDeviceInstanceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("重启设备实例: {DeviceId}", deviceId);

            var stopped = await StopDeviceInstanceAsync(deviceId, cancellationToken);
            if (stopped)
            {
                // 等待一段时间确保完全停止
                await Task.Delay(2000, cancellationToken);
                return await StartDeviceInstanceAsync(deviceId, cancellationToken);
            }

            return false;
        }

        /// <summary>
        /// 更新设备数据
        /// </summary>
        public async Task UpdateDeviceDataAsync(string deviceId)
        {
            if (!_deviceStates.TryGetValue(deviceId, out var deviceState))
            {
                return;
            }

            try
            {
                // 从PLC读取数据并更新数据模型
                var plcProvider = _plcManager.GetDataProvider(deviceId);
                if (plcProvider?.IsConnected == true)
                {
                    // 更新坐标数据
                    deviceState.DataModel.CurrentX = await plcProvider.ReadFloatAsync("D100");
                    deviceState.DataModel.CurrentY = await plcProvider.ReadFloatAsync("D102");
                    deviceState.DataModel.CurrentZ = await plcProvider.ReadFloatAsync("D104");

                    // 更新工艺参数
                    deviceState.DataModel.ProcessSpeed = await plcProvider.ReadFloatAsync("D200");
                    deviceState.DataModel.ProcessPressure = await plcProvider.ReadFloatAsync("D202");

                    // 更新状态数据
                    var controlMode = await plcProvider.ReadByteAsync("D300");
                    deviceState.DataModel.ControlMode = (ControlMode)controlMode;

                    deviceState.LastUpdateTime = DateTime.Now;
                    deviceState.Statistics.PlcInteractionCount++;
                }

                // 更新状态服务
                if (deviceState.StateService != null)
                {
                    // 这里可以基于PLC数据更新状态机
                    deviceState.Statistics.StateChangeCount++;
                }

                _logger.LogDebug("设备数据更新完成: {DeviceId}", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备数据失败: {DeviceId}", deviceId);
                deviceState.ErrorMessage = ex.Message;
                deviceState.Statistics.ErrorCount++;
            }
        }

        /// <summary>
        /// 获取设备健康状态
        /// </summary>
        public object GetDeviceHealthStatus(string deviceId)
        {
            var deviceState = GetDeviceState(deviceId);
            var instanceInfo = _instanceManager.GetInstance(deviceId);

            if (deviceState == null || instanceInfo == null)
            {
                return new { DeviceId = deviceId, Status = "NotFound" };
            }

            return new
            {
                DeviceId = deviceId,
                DeviceName = instanceInfo.DeviceName,
                InstanceState = instanceInfo.State.ToString(),
                SecsConnected = _secsManager.IsDeviceOnline(deviceId),
                PlcConnected = _plcManager.IsDeviceConnected(deviceId),
                ControlState = deviceState.DataModel.ControlState.ToString(),
                ControlMode = deviceState.DataModel.ControlMode.ToString(),
                LastUpdateTime = deviceState.LastUpdateTime,
                ErrorMessage = deviceState.ErrorMessage,
                Statistics = deviceState.Statistics,
                Uptime = DateTime.Now - deviceState.Statistics.StartTime
            };
        }

        /// <summary>
        /// 获取系统概览
        /// </summary>
        public object GetSystemOverview()
        {
            var allDevices = GetAllDeviceStates().ToList();
            var allInstances = _instanceManager.GetAllInstances().ToList();

            var runningDevices = allInstances.Count(i => i.State == EquipmentInstanceState.Running);
            var totalDevices = allInstances.Count;
            var errorDevices = allInstances.Count(i => i.State == EquipmentInstanceState.Error);

            var secsStats = _secsManager.GetConnectionStatistics();
            var plcStats = _plcManager.GetConnectionStatistics();

            return new
            {
                System = new
                {
                    Name = _configuration.System.SystemName,
                    Version = _configuration.System.SystemVersion,
                    StartTime = DateTime.Now.AddHours(-1) // TODO: 记录实际启动时间
                },
                Devices = new
                {
                    Total = totalDevices,
                    Running = runningDevices,
                    Error = errorDevices,
                    Stopped = totalDevices - runningDevices - errorDevices
                },
                Connections = new
                {
                    Secs = secsStats,
                    Plc = plcStats
                },
                DeviceList = allDevices.Select(d => new
                {
                    d.DeviceId,
                    Instance = allInstances.FirstOrDefault(i => i.DeviceId == d.DeviceId),
                    SecsOnline = _secsManager.IsDeviceOnline(d.DeviceId),
                    PlcConnected = _plcManager.IsDeviceConnected(d.DeviceId),
                    d.LastUpdateTime,
                    d.ErrorMessage
                })
            };
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 订阅管理器事件
        /// </summary>
        private void SubscribeToManagerEvents()
        {
            _instanceManager.InstanceStateChanged += (sender, args) =>
            {
                OnDeviceInstanceEvent(args.DeviceId, "InstanceStateChanged", 
                    $"实例状态从 {args.OldState} 变更为 {args.NewState}");
            };

            _secsManager.ConnectionStateChanged += (sender, args) =>
            {
                OnDeviceInstanceEvent(args.DeviceId, "SecsStateChanged", 
                    $"SECS连接状态从 {args.OldState} 变更为 {args.NewState}");
            };

            _plcManager.ConnectionStateChanged += (sender, args) =>
            {
                OnDeviceInstanceEvent(args.DeviceId, "PlcStateChanged", 
                    $"PLC连接状态变更为 {(args.IsConnected ? "已连接" : "已断开")}");
            };
        }

        /// <summary>
        /// 创建设备实例
        /// </summary>
        private async Task CreateDeviceInstancesAsync(CancellationToken cancellationToken)
        {
            foreach (var config in _configuration.EquipmentInstances.Where(c => c.Enabled))
            {
                try
                {
                    // 添加SECS连接
                    if (config.SecsConfiguration.Enabled)
                    {
                        await _secsManager.AddConnectionAsync(config.DeviceId, config.SecsConfiguration, cancellationToken);
                    }

                    // 添加PLC连接
                    if (config.PlcConfiguration.Enabled)
                    {
                        await _plcManager.AddConnectionAsync(config.DeviceId, config.PlcConfiguration, cancellationToken);
                    }

                    // 创建设备状态
                    var deviceState = new DeviceInstanceState
                    {
                        DeviceId = config.DeviceId,
                        DataModel = CreateDeviceDataModel(config),
                        Statistics = new DeviceInstanceStatistics
                        {
                            StartTime = DateTime.Now
                        }
                    };

                    _deviceStates.TryAdd(config.DeviceId, deviceState);

                    _logger.LogInformation("设备实例已创建: {DeviceId}", config.DeviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "创建设备实例失败: {DeviceId}", config.DeviceId);
                }
            }
        }

        /// <summary>
        /// 创建设备数据模型
        /// </summary>
        private DiceDataModel CreateDeviceDataModel(EquipmentInstanceConfiguration config)
        {
            var dataModel = new DiceDataModel
            {
                DeviceId = config.SecsConfiguration.DeviceId,
                EquipmentName = config.DeviceName,
                ModelName = config.ModelName
            };

            // 根据配置初始化数据模型
            // TODO: 这里可以根据设备特定配置进行初始化

            return dataModel;
        }

        /// <summary>
        /// 启动连接
        /// </summary>
        private async Task StartConnectionsAsync(CancellationToken cancellationToken)
        {
            // 启动SECS连接
            _logger.LogInformation("启动SECS连接...");
            await _secsManager.StartAllConnectionsAsync(cancellationToken);

            // 启动PLC连接
            _logger.LogInformation("启动PLC连接...");
            await _plcManager.StartAllConnectionsAsync(cancellationToken);
        }

        /// <summary>
        /// 初始化设备状态
        /// </summary>
        private async Task InitializeDeviceStateAsync(DeviceInstanceState deviceState, CancellationToken cancellationToken)
        {
            try
            {
                // 创建状态服务
                var loggerFactory = _serviceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                var stateLogger = loggerFactory?.CreateLogger<EquipmentStateService>();
                
                if (stateLogger != null)
                {
                    deviceState.StateService = new EquipmentStateService(stateLogger);
                    await deviceState.StateService.CompleteProcessInitializationAsync();
                }

                // TODO: 创建其他服务实例
                // deviceState.DataCollectionService = ...
                // deviceState.EventReportService = ...

                _logger.LogInformation("设备状态服务初始化完成: {DeviceId}", deviceState.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化设备状态服务失败: {DeviceId}", deviceState.DeviceId);
                deviceState.ErrorMessage = ex.Message;
            }
        }

        /// <summary>
        /// 启动定时器
        /// </summary>
        private void StartTimers()
        {
            // 数据更新定时器（每秒更新一次）
            _dataUpdateTimer = new Timer(async _ => await UpdateAllDeviceDataAsync(), 
                null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

            // 健康检查定时器（每30秒检查一次）
            _healthCheckTimer = new Timer(PerformHealthCheck, 
                null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _logger.LogDebug("定时器已启动");
        }

        /// <summary>
        /// 更新所有设备数据
        /// </summary>
        private async Task UpdateAllDeviceDataAsync()
        {
            var updateTasks = _deviceStates.Keys.Select(deviceId => UpdateDeviceDataAsync(deviceId));
            await Task.WhenAll(updateTasks);
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private void PerformHealthCheck(object? state)
        {
            try
            {
                var devices = GetAllDeviceStates().ToList();
                foreach (var device in devices)
                {
                    // 更新运行时长
                    device.Statistics.RunTime = DateTime.Now - device.Statistics.StartTime;

                    // 检查连接状态
                    var secsOnline = _secsManager.IsDeviceOnline(device.DeviceId);
                    var plcConnected = _plcManager.IsDeviceConnected(device.DeviceId);

                    // 如果连接异常，记录错误
                    if (!secsOnline || !plcConnected)
                    {
                        device.ErrorMessage = $"连接异常 - SECS:{secsOnline}, PLC:{plcConnected}";
                    }
                    else if (!string.IsNullOrEmpty(device.ErrorMessage))
                    {
                        device.ErrorMessage = null; // 清除错误信息
                    }
                }

                _logger.LogDebug("健康检查完成，检查了 {Count} 个设备", devices.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康检查执行失败");
            }
        }

        /// <summary>
        /// 触发设备实例事件
        /// </summary>
        private void OnDeviceInstanceEvent(string deviceId, string eventType, string message)
        {
            DeviceInstanceEvent?.Invoke(this, new DeviceInstanceEventArgs
            {
                DeviceId = deviceId,
                EventType = eventType,
                Message = message,
                Timestamp = DateTime.Now
            });

            _logger.LogInformation("设备事件 [{EventType}] {DeviceId}: {Message}", eventType, deviceId, message);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _logger.LogInformation("正在释放设备实例服务...");

            _dataUpdateTimer?.Dispose();
            _healthCheckTimer?.Dispose();
            _operationSemaphore?.Dispose();

            _disposed = true;
            _logger.LogInformation("设备实例服务已释放");
        }

        #endregion
    }
}