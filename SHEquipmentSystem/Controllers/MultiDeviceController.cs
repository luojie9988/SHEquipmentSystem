// 文件路径: Controllers/MultiDeviceController.cs
// 版本: v1.0.0
// 描述: 多设备管理控制器

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using DiceEquipmentSystem.Core.Managers;
using DiceEquipmentSystem.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Core.Enums;

namespace SHEquipmentSystem.Controllers
{
    /// <summary>
    /// 设备操作请求模型
    /// </summary>
    public class DeviceOperationRequest
    {
        /// <summary>设备ID</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>操作类型</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>参数</summary>
        public Dictionary<string, object>? Parameters { get; set; }
    }

    /// <summary>
    /// 批量操作请求模型
    /// </summary>
    public class BatchOperationRequest
    {
        /// <summary>设备ID列表</summary>
        public List<string> DeviceIds { get; set; } = new();

        /// <summary>操作类型</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>参数</summary>
        public Dictionary<string, object>? Parameters { get; set; }

        /// <summary>是否并行执行</summary>
        public bool Parallel { get; set; } = true;
    }

    /// <summary>
    /// 多设备管理控制器
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class MultiDeviceController : ControllerBase
    {
        #region 私有字段

        private readonly ILogger<MultiDeviceController> _logger;
        private readonly IEquipmentInstanceService? _equipmentInstanceService;
        private readonly IEquipmentInstanceManager? _instanceManager;
        private readonly IMultiSecsConnectionManager? _secsManager;
        private readonly IMultiPlcDataProviderManager? _plcManager;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public MultiDeviceController(
            ILogger<MultiDeviceController> logger,
            IEquipmentInstanceService? equipmentInstanceService = null,
            IEquipmentInstanceManager? instanceManager = null,
            IMultiSecsConnectionManager? secsManager = null,
            IMultiPlcDataProviderManager? plcManager = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _equipmentInstanceService = equipmentInstanceService;
            _instanceManager = instanceManager;
            _secsManager = secsManager;
            _plcManager = plcManager;

            _logger.LogDebug("多设备控制器已创建");
        }

        #endregion

        #region 系统概览API

        /// <summary>
        /// 获取系统概览
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> GetSystemOverview()
        {
            try
            {
                if (_equipmentInstanceService != null)
                {
                    var overview = _equipmentInstanceService.GetSystemOverview();
                    return Ok(new { success = true, data = overview });
                }
                else
                {
                    // 降级模式 - 使用单独的管理器
                    var devices = _instanceManager?.GetAllInstances()?.ToList() ?? new List<EquipmentInstanceInfo>();
                    var secsStats = _secsManager?.GetConnectionStatistics();
                    var plcStats = _plcManager?.GetConnectionStatistics();

                    var overview = new
                    {
                        System = new
                        {
                            Name = "SH Multi-Equipment System",
                            Version = "1.0.0",
                            StartTime = DateTime.Now.AddHours(-1)
                        },
                        Devices = new
                        {
                            Total = devices.Count,
                            Running = devices.Count(d => d.State == EquipmentInstanceState.Running),
                            Error = devices.Count(d => d.State == EquipmentInstanceState.Error),
                            Stopped = devices.Count(d => d.State == EquipmentInstanceState.Stopped)
                        },
                        Connections = new
                        {
                            Secs = secsStats,
                            Plc = plcStats
                        },
                        DeviceList = devices.Select(d => new
                        {
                            d.DeviceId,
                            d.DeviceName,
                            State = d.State.ToString(),
                            d.SecsConnected,
                            d.PlcConnected,
                            d.LastUpdateTime,
                            d.ErrorMessage
                        })
                    };

                    return Ok(new { success = true, data = overview });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统概览失败");
                return StatusCode(500, new { success = false, message = "获取系统概览失败" });
            }
        }

        /// <summary>
        /// 获取所有设备列表
        /// </summary>
        [HttpGet("devices")]
        public async Task<IActionResult> GetAllDevices()
        {
            try
            {
                if (_equipmentInstanceService != null)
                {
                    var devices = _equipmentInstanceService.GetAllDeviceStates()
                        .Select(d => new
                        {
                            d.DeviceId,
                            DataModel = new
                            {
                                d.DataModel.EquipmentName,
                                d.DataModel.ModelName,
                                d.DataModel.ControlState,
                                d.DataModel.ControlMode
                            },
                            d.LastUpdateTime,
                            d.ErrorMessage,
                            Statistics = d.Statistics
                        });

                    return Ok(new { success = true, data = devices });
                }
                else if (_instanceManager != null)
                {
                    var devices = _instanceManager.GetAllInstances()
                        .Select(d => new
                        {
                            d.DeviceId,
                            d.DeviceName,
                            d.ModelName,
                            State = d.State.ToString(),
                            d.SecsConnected,
                            d.PlcConnected,
                            d.LastUpdateTime,
                            d.ErrorMessage,
                            d.Statistics
                        });

                    return Ok(new { success = true, data = devices });
                }
                else
                {
                    return Ok(new { success = true, data = new List<object>() });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备列表失败");
                return StatusCode(500, new { success = false, message = "获取设备列表失败" });
            }
        }

        /// <summary>
        /// 获取指定设备详情
        /// </summary>
        [HttpGet("devices/{deviceId}")]
        public async Task<IActionResult> GetDeviceDetails(string deviceId)
        {
            try
            {
                if (_equipmentInstanceService != null)
                {
                    var healthStatus = _equipmentInstanceService.GetDeviceHealthStatus(deviceId);
                    var deviceState = _equipmentInstanceService.GetDeviceState(deviceId);

                    if (deviceState == null)
                    {
                        return NotFound(new { success = false, message = "设备不存在" });
                    }

                    var details = new
                    {
                        HealthStatus = healthStatus,
                        DataModel = deviceState.DataModel,
                        StateService = deviceState.StateService != null ? new
                        {
                            IsOnline = await (deviceState.StateService?.IsOnlineAsync() ?? Task.FromResult(false)),
                            IsProcessing = await (deviceState.StateService?.IsProcessingAsync() ?? Task.FromResult(false)),
                            ControlState = await (deviceState.StateService?.GetControlStateAsync() ?? Task.FromResult(ControlState.EquipmentOffline)),
                            ControlMode = await (deviceState.StateService?.GetControlModeAsync() ?? Task.FromResult(ControlMode.Offline))
                        } : null,
                        Statistics = deviceState.Statistics
                    };

                    return Ok(new { success = true, data = details });
                }
                else if (_instanceManager != null)
                {
                    var instance = _instanceManager.GetInstance(deviceId);
                    if (instance == null)
                    {
                        return NotFound(new { success = false, message = "设备实例不存在" });
                    }

                    return Ok(new { success = true, data = instance });
                }
                else
                {
                    return NotFound(new { success = false, message = "服务不可用" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备详情失败: {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "获取设备详情失败" });
            }
        }

        #endregion

        #region 设备控制API

        /// <summary>
        /// 启动设备
        /// </summary>
        [HttpPost("devices/{deviceId}/start")]
        public async Task<IActionResult> StartDevice(string deviceId)
        {
            try
            {
                bool success = false;
                string message = "";

                if (_equipmentInstanceService != null)
                {
                    success = await _equipmentInstanceService.StartDeviceInstanceAsync(deviceId);
                    message = success ? "设备启动成功" : "设备启动失败";
                }
                else if (_instanceManager != null)
                {
                    success = await _instanceManager.StartInstanceAsync(deviceId);
                    message = success ? "设备实例启动成功" : "设备实例启动失败";
                }
                else
                {
                    message = "设备管理服务不可用";
                }

                _logger.LogInformation("启动设备: {DeviceId} - {Result}", deviceId, success ? "成功" : "失败");
                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动设备失败: {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "启动设备失败" });
            }
        }

        /// <summary>
        /// 停止设备
        /// </summary>
        [HttpPost("devices/{deviceId}/stop")]
        public async Task<IActionResult> StopDevice(string deviceId)
        {
            try
            {
                bool success = false;
                string message = "";

                if (_equipmentInstanceService != null)
                {
                    success = await _equipmentInstanceService.StopDeviceInstanceAsync(deviceId);
                    message = success ? "设备停止成功" : "设备停止失败";
                }
                else if (_instanceManager != null)
                {
                    success = await _instanceManager.StopInstanceAsync(deviceId);
                    message = success ? "设备实例停止成功" : "设备实例停止失败";
                }
                else
                {
                    message = "设备管理服务不可用";
                }

                _logger.LogInformation("停止设备: {DeviceId} - {Result}", deviceId, success ? "成功" : "失败");
                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止设备失败: {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "停止设备失败" });
            }
        }

        /// <summary>
        /// 重启设备
        /// </summary>
        [HttpPost("devices/{deviceId}/restart")]
        public async Task<IActionResult> RestartDevice(string deviceId)
        {
            try
            {
                bool success = false;
                string message = "";

                if (_equipmentInstanceService != null)
                {
                    success = await _equipmentInstanceService.RestartDeviceInstanceAsync(deviceId);
                    message = success ? "设备重启成功" : "设备重启失败";
                }
                else if (_instanceManager != null)
                {
                    success = await _instanceManager.RestartInstanceAsync(deviceId);
                    message = success ? "设备实例重启成功" : "设备实例重启失败";
                }
                else
                {
                    message = "设备管理服务不可用";
                }

                _logger.LogInformation("重启设备: {DeviceId} - {Result}", deviceId, success ? "成功" : "失败");
                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重启设备失败: {DeviceId}", deviceId);
                return StatusCode(500, new { success = false, message = "重启设备失败" });
            }
        }

        /// <summary>
        /// 执行设备操作
        /// </summary>
        [HttpPost("devices/operation")]
        public async Task<IActionResult> ExecuteDeviceOperation([FromBody] DeviceOperationRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.Action))
                {
                    return BadRequest(new { success = false, message = "设备ID和操作类型不能为空" });
                }

                bool success = false;
                string message = "";

                switch (request.Action.ToLower())
                {
                    case "start":
                        var startResult = await StartDevice(request.DeviceId);
                        return startResult;

                    case "stop":
                        var stopResult = await StopDevice(request.DeviceId);
                        return stopResult;

                    case "restart":
                        var restartResult = await RestartDevice(request.DeviceId);
                        return restartResult;

                    case "online":
                        success = await RequestDeviceOnline(request.DeviceId, request.Parameters);
                        message = success ? "设备上线成功" : "设备上线失败";
                        break;

                    case "offline":
                        success = await RequestDeviceOffline(request.DeviceId);
                        message = success ? "设备下线成功" : "设备下线失败";
                        break;

                    default:
                        return BadRequest(new { success = false, message = "不支持的操作类型" });
                }

                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行设备操作失败: {DeviceId} - {Action}", request.DeviceId, request.Action);
                return StatusCode(500, new { success = false, message = "执行设备操作失败" });
            }
        }

        /// <summary>
        /// 批量操作设备
        /// </summary>
        [HttpPost("devices/batch-operation")]
        public async Task<IActionResult> ExecuteBatchOperation([FromBody] BatchOperationRequest request)
        {
            try
            {
                if (request.DeviceIds?.Any() != true || string.IsNullOrWhiteSpace(request.Action))
                {
                    return BadRequest(new { success = false, message = "设备ID列表和操作类型不能为空" });
                }

                var results = new List<object>();

                if (request.Parallel)
                {
                    // 并行执行 - 增加并发控制和超时机制
                    using var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
                    using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5分钟超时
                    
                    var tasks = request.DeviceIds.Select(deviceId => ExecuteDeviceOperationAsync(
                        deviceId, 
                        request.Action, 
                        request.Parameters, 
                        semaphore, 
                        cancellationTokenSource.Token
                    )).ToArray();

                    try
                    {
                        var taskResults = await Task.WhenAll(tasks);
                        results.AddRange(taskResults);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("批量操作整体超时: {Action}, DeviceCount: {Count}", request.Action, request.DeviceIds.Count);
                        // 收集已完成的任务结果
                        foreach (var task in tasks)
                        {
                            if (task.IsCompletedSuccessfully)
                            {
                                results.Add(task.Result);
                            }
                            else if (task.IsFaulted || task.IsCanceled)
                            {
                                results.Add(new { DeviceId = "unknown", Success = false, Error = "任务被取消或失败", ExecutionTime = DateTime.UtcNow });
                            }
                        }
                    }
                }
                else
                {
                    // 串行执行
                    foreach (var deviceId in request.DeviceIds)
                    {
                        try
                        {
                            var deviceRequest = new DeviceOperationRequest
                            {
                                DeviceId = deviceId,
                                Action = request.Action,
                                Parameters = request.Parameters
                            };

                            var result = await ExecuteDeviceOperation(deviceRequest);
                            results.Add(new { DeviceId = deviceId, Success = true, Result = result });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "批量操作设备失败: {DeviceId}", deviceId);
                            results.Add(new { DeviceId = deviceId, Success = false, Error = ex.Message });
                        }
                    }
                }

                var successCount = results.Count(r => ((dynamic)r).Success);
                var totalCount = results.Count;

                return Ok(new
                {
                    success = true,
                    message = $"批量操作完成: {successCount}/{totalCount}",
                    summary = new
                    {
                        Total = totalCount,
                        Success = successCount,
                        Failed = totalCount - successCount
                    },
                    results = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行批量操作失败: {Action}", request.Action);
                return StatusCode(500, new { success = false, message = "执行批量操作失败" });
            }
        }

        #endregion

        #region 连接管理API

        /// <summary>
        /// 获取SECS连接状态
        /// </summary>
        [HttpGet("connections/secs")]
        public async Task<IActionResult> GetSecsConnectionStatus()
        {
            try
            {
                if (_secsManager != null)
                {
                    var statistics = _secsManager.GetConnectionStatistics();
                    return Ok(new { success = true, data = statistics });
                }
                else
                {
                    return Ok(new { success = true, data = new { message = "SECS管理器不可用" } });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取SECS连接状态失败");
                return StatusCode(500, new { success = false, message = "获取SECS连接状态失败" });
            }
        }

        /// <summary>
        /// 获取PLC连接状态
        /// </summary>
        [HttpGet("connections/plc")]
        public async Task<IActionResult> GetPlcConnectionStatus()
        {
            try
            {
                if (_plcManager != null)
                {
                    var statistics = _plcManager.GetConnectionStatistics();
                    return Ok(new { success = true, data = statistics });
                }
                else
                {
                    return Ok(new { success = true, data = new { message = "PLC管理器不可用" } });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC连接状态失败");
                return StatusCode(500, new { success = false, message = "获取PLC连接状态失败" });
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 异步执行单个设备操作（用于并行批量操作）
        /// </summary>
        private async Task<object> ExecuteDeviceOperationAsync(
            string deviceId, 
            string action, 
            Dictionary<string, object>? parameters,
            SemaphoreSlim semaphore, 
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var deviceRequest = new DeviceOperationRequest
                {
                    DeviceId = deviceId,
                    Action = action,
                    Parameters = parameters
                };

                var result = await ExecuteDeviceOperationInternal(deviceRequest, cancellationToken);
                return new { DeviceId = deviceId, Success = true, Result = result, ExecutionTime = DateTime.UtcNow };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("批量操作设备超时: {DeviceId}", deviceId);
                return new { DeviceId = deviceId, Success = false, Error = "操作超时", ExecutionTime = DateTime.UtcNow };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量操作设备失败: {DeviceId}", deviceId);
                return new { DeviceId = deviceId, Success = false, Error = ex.Message, ExecutionTime = DateTime.UtcNow };
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 请求设备上线
        /// </summary>
        private async Task<bool> RequestDeviceOnline(string deviceId, Dictionary<string, object>? parameters)
        {
            var stateService = _equipmentInstanceService?.GetDeviceStateService(deviceId);
            if (stateService != null)
            {
                var remote = parameters?.ContainsKey("remote") == true &&
                            Convert.ToBoolean(parameters["remote"]);
                return await stateService.RequestOnlineAsync(remote);
            }
            return false;
        }

        /// <summary>
        /// 请求设备下线
        /// </summary>
        private async Task<bool> RequestDeviceOffline(string deviceId)
        {
            var stateService = _equipmentInstanceService?.GetDeviceStateService(deviceId);
            if (stateService != null)
            {
                return await stateService.RequestOfflineAsync();
            }
            return false;
        }

        /// <summary>
        /// 内部设备操作方法，支持取消令牌
        /// </summary>
        private async Task<object> ExecuteDeviceOperationInternal(DeviceOperationRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.Action))
            {
                throw new ArgumentException("设备ID和操作类型不能为空");
            }

            bool success = false;
            string message = "";

            switch (request.Action.ToLower())
            {
                case "start":
                    success = await StartDeviceInternal(request.DeviceId, cancellationToken);
                    message = success ? "设备启动成功" : "设备启动失败";
                    break;

                case "stop":
                    success = await StopDeviceInternal(request.DeviceId, cancellationToken);
                    message = success ? "设备停止成功" : "设备停止失败";
                    break;

                case "restart":
                    success = await RestartDeviceInternal(request.DeviceId, cancellationToken);
                    message = success ? "设备重启成功" : "设备重启失败";
                    break;

                case "online":
                    success = await RequestDeviceOnline(request.DeviceId, request.Parameters);
                    message = success ? "设备上线成功" : "设备上线失败";
                    break;

                case "offline":
                    success = await RequestDeviceOffline(request.DeviceId);
                    message = success ? "设备下线成功" : "设备下线失败";
                    break;

                default:
                    throw new ArgumentException("不支持的操作类型");
            }

            return new { success, message };
        }

        /// <summary>
        /// 内部启动设备方法（支持取消令牌）
        /// </summary>
        private async Task<bool> StartDeviceInternal(string deviceId, CancellationToken cancellationToken)
        {
            if (_equipmentInstanceService != null)
            {
                return await _equipmentInstanceService.StartDeviceInstanceAsync(deviceId);
            }
            else if (_instanceManager != null)
            {
                return await _instanceManager.StartInstanceAsync(deviceId);
            }
            return false;
        }

        /// <summary>
        /// 内部停止设备方法（支持取消令牌）
        /// </summary>
        private async Task<bool> StopDeviceInternal(string deviceId, CancellationToken cancellationToken)
        {
            if (_equipmentInstanceService != null)
            {
                return await _equipmentInstanceService.StopDeviceInstanceAsync(deviceId);
            }
            else if (_instanceManager != null)
            {
                return await _instanceManager.StopInstanceAsync(deviceId);
            }
            return false;
        }

        /// <summary>
        /// 内部重启设备方法（支持取消令牌）
        /// </summary>
        private async Task<bool> RestartDeviceInternal(string deviceId, CancellationToken cancellationToken)
        {
            if (_equipmentInstanceService != null)
            {
                return await _equipmentInstanceService.RestartDeviceInstanceAsync(deviceId);
            }
            else if (_instanceManager != null)
            {
                return await _instanceManager.RestartInstanceAsync(deviceId);
            }
            return false;
        }

        #endregion
    }
}