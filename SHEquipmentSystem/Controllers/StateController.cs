using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

// 真实的项目服务接口
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Services.Interfaces;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.PLC.Interfaces;

namespace SHEquipmentSystem.Controllers
{
    public class StateController : Controller
    {
        private readonly ILogger<StateController> _logger;

        // 主要服务 - EquipmentStateService
        private readonly IEquipmentStateService? _stateService;

        // 可选的辅助服务
        private readonly ISecsConnectionManager? _secsConnectionManager;
        private readonly IPlcDataProvider? _plcDataProvider;

        // 降级数据模拟（当服务不可用时使用）
        private static readonly FallbackStateData _fallbackData = new FallbackStateData();

        public StateController(
            ILogger<StateController> logger,
            IEquipmentStateService? stateService = null,
            ISecsConnectionManager? secsConnectionManager = null,
            IPlcDataProvider? plcDataProvider = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateService = stateService;
            _secsConnectionManager = secsConnectionManager;
            _plcDataProvider = plcDataProvider;

            LogServiceAvailability();
        }

        /// <summary>
        /// 状态管理主页
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// 获取当前完整状态信息
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCurrentState()
        {
            try
            {
                object state;

                if (_stateService != null)
                {
                    // 使用真实的设备状态服务
                    state = await GetStateFromEquipmentStateService();
                }
                else
                {
                    // 降级到模拟数据
                    state = GetStateFromFallbackData();
                }

                return Ok(new { success = true, data = state });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前状态失败");
                return StatusCode(500, new { success = false, message = "获取状态失败" });
            }
        }

        /// <summary>
        /// 获取状态历史记录
        /// </summary>
        [HttpGet]
        public IActionResult GetStateHistory()
        {
            try
            {
                List<object> history;

                if (_stateService != null)
                {
                    // 使用真实的状态服务
                    var stateHistory = _stateService.GetStateHistory();
                    history = stateHistory
                        .OrderByDescending(h => h.Timestamp)
                        .Take(50)
                        .Select(h => new
                        {
                            Timestamp = h.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                            StateType = h.StateType,
                            FromState = h.OldValue,
                            ToState = h.NewValue,
                            Trigger = h.Reason,
                            TimestampRaw = h.Timestamp
                        })
                        .Cast<object>()
                        .ToList();
                }
                else
                {
                    // 使用降级数据
                    history = _fallbackData.StateHistory
                        .OrderByDescending(h => h.Timestamp)
                        .Take(50)
                        .Select(h => new
                        {
                            Timestamp = h.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                            StateType = h.StateType,
                            FromState = h.FromState,
                            ToState = h.ToState,
                            Trigger = h.Trigger,
                            TimestampRaw = h.Timestamp
                        })
                        .Cast<object>()
                        .ToList();
                }

                return Ok(new { success = true, data = history });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取状态历史失败");
                return StatusCode(500, new { success = false, message = "获取状态历史失败" });
            }
        }

        /// <summary>
        /// 执行控制状态转换
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ChangeControlState([FromBody] StateChangeRequest request)
        {
            try
            {
                bool success = false;
                string message = "";

                if (_stateService != null)
                {
                    // 使用真实的状态服务
                    switch (request.Action?.ToLower())
                    {
                        case "goonlinelocal":
                            success = await _stateService.RequestOnlineAsync(false); // Local模式
                            message = success ? "成功切换到本地在线模式" : "切换到本地在线模式失败";
                            break;

                        case "goonlineremote":
                            success = await _stateService.RequestOnlineAsync(true); // Remote模式
                            message = success ? "成功切换到远程在线模式" : "切换到远程在线模式失败";
                            break;

                        case "gooffline":
                            success = await _stateService.RequestOfflineAsync();
                            message = success ? "成功切换到离线模式" : "切换到离线模式失败";
                            break;

                        case "switchtolocal":
                        case "switchtoremote":
                            success = await _stateService.SwitchControlModeAsync();
                            message = success ? "成功切换控制模式" : "切换控制模式失败";
                            break;

                        default:
                            return BadRequest(new { success = false, message = "不支持的操作" });
                    }
                }
                else
                {
                    // 使用降级数据模拟
                    var result = _fallbackData.ChangeControlState(request.Action);
                    success = result.success;
                    message = result.message;
                }

                _logger.LogInformation($"控制状态转换: {request.Action} - {(success ? "成功" : "失败")}");
                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行状态转换失败: {request.Action}");
                return StatusCode(500, new { success = false, message = "状态转换失败" });
            }
        }

        /// <summary>
        /// 执行处理状态转换
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ChangeProcessState([FromBody] StateChangeRequest request)
        {
            try
            {
                bool success = false;
                string message = "";

                if (_stateService != null)
                {
                    // 使用真实的状态服务
                    switch (request.Action?.ToLower())
                    {
                        case "start":
                            success = await _stateService.StartProcessAsync();
                            message = success ? "处理已开始" : "启动处理失败";
                            break;

                        case "pause":
                            success = await _stateService.PauseProcessAsync();
                            message = success ? "处理已暂停" : "暂停处理失败";
                            break;

                        case "resume":
                            success = await _stateService.ResumeProcessAsync();
                            message = success ? "处理已恢复" : "恢复处理失败";
                            break;

                        case "stop":
                            success = await _stateService.StopProcessAsync();
                            message = success ? "处理已停止" : "停止处理失败";
                            break;

                        case "abort":
                            // 使用StopProcessAsync作为中止操作的实现
                            success = await _stateService.StopProcessAsync();
                            message = success ? "处理已中止" : "中止处理失败";
                            break;

                        case "reset":
                            success = await _stateService.ResetProcessAsync();
                            message = success ? "处理状态已重置" : "重置处理状态失败";
                            break;

                        default:
                            return BadRequest(new { success = false, message = "不支持的处理操作" });
                    }
                }
                else
                {
                    // 使用降级数据模拟
                    var result = _fallbackData.ChangeProcessState(request.Action);
                    success = result.success;
                    message = result.message;
                }

                _logger.LogInformation($"处理状态转换: {request.Action} - {(success ? "成功" : "失败")}");
                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行处理状态转换失败: {request.Action}");
                return StatusCode(500, new { success = false, message = "处理状态转换失败" });
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        [HttpGet]
        public IActionResult GetConnectionStatistics()
        {
            try
            {
                object stats;

                if (_secsConnectionManager != null)
                {
                    // 使用真实的连接管理器
                    var connectionStats = _secsConnectionManager.GetConnectionStatistics();

                    stats = new
                    {
                        ConnectionCount = connectionStats.connectionCount,
                        MessagesSent = connectionStats.messagesSent,
                        MessagesReceived = connectionStats.messagesReceived,
                        LastConnectedTime = _secsConnectionManager.LastConnectedTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                        //LastDisconnectedTime = _secsConnectionManager.LastDisconnectedTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                        Uptime = CalculateUptime(_secsConnectionManager.LastConnectedTime)
                    };
                }
                else
                {
                    // 使用降级数据
                    var fallbackStats = _fallbackData.Statistics;
                    stats = new
                    {
                        ConnectionCount = fallbackStats.ConnectionCount,
                        MessagesSent = fallbackStats.MessagesSent,
                        MessagesReceived = fallbackStats.MessagesReceived,
                        LastConnectedTime = fallbackStats.LastConnectedTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                        //LastDisconnectedTime = fallbackStats.LastDisconnectedTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                        Uptime = fallbackStats.Uptime?.ToString(@"dd\.hh\:mm\:ss")
                    };
                }

                return Ok(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取连接统计失败");
                return StatusCode(500, new { success = false, message = "获取连接统计失败" });
            }
        }

        /// <summary>
        /// 模拟连接状态变更（仅在SECS服务不可用时使用）
        /// </summary>
        [HttpPost]
        public IActionResult SimulateConnection([FromBody] SimulateRequest request)
        {
            try
            {
                if (_secsConnectionManager != null)
                {
                    return BadRequest(new { success = false, message = "真实连接管理器可用时不支持模拟操作" });
                }

                var result = _fallbackData.SimulateConnection(request.Action);

                _logger.LogInformation($"模拟连接操作: {request.Action} - {(result.success ? "成功" : "失败")}");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模拟连接状态变更失败");
                return StatusCode(500, new { success = false, message = "操作失败" });
            }
        }

        /// <summary>
        /// 获取系统服务状态
        /// </summary>
        [HttpGet]
        public IActionResult GetSystemStatus()
        {
            try
            {
                var systemStatus = new
                {
                    Services = new
                    {
                        StateService = _stateService != null ? "Available" : "Not Available",
                        SecsConnectionManager = _secsConnectionManager != null ? "Available" : "Not Available",
                        PlcDataProvider = _plcDataProvider != null ? "Available" : "Not Available"
                    },
                    PlcConnection = _plcDataProvider?.IsConnected ?? false,
                    SecsConnection = _secsConnectionManager?.IsConnected ?? false,
                    CommunicationEstablished = GetCommunicationStatus(),
                    Mode = GetOperatingMode(),
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                return Ok(new { success = true, data = systemStatus });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统状态失败");
                return StatusCode(500, new { success = false, message = "获取系统状态失败" });
            }
        }

        #region 私有辅助方法

        private void LogServiceAvailability()
        {
            _logger.LogInformation("状态控制器服务可用性:");
            _logger.LogInformation($"  - 设备状态服务: {(_stateService != null ? "可用" : "不可用")}");
            _logger.LogInformation($"  - SECS连接管理器: {(_secsConnectionManager != null ? "可用" : "不可用")}");
            _logger.LogInformation($"  - PLC数据提供者: {(_plcDataProvider != null ? "可用" : "不可用")}");
        }

        private async Task<object> GetStateFromEquipmentStateService()
        {
            // 通过EquipmentStateService获取完整状态信息
            var statusInfo = await _stateService!.GetStatusInfoAsync();
            var controlState = await _stateService.GetControlStateAsync();
            var controlMode = await _stateService.GetControlModeAsync();
            var processState = await _stateService.GetProcessStateAsync();
            var equipmentState = await _stateService.GetEquipmentStateAsync();

            // 获取连接状态（如果SECS服务可用则使用，否则根据通信建立状态推断）
            var connectionState = GetConnectionStateFromStatus(statusInfo.IsCommunicationEstablished);

            return new
            {
                // HSMS连接状态
                ConnectionState = new
                {
                    Current = connectionState.ToString(),
                    IsConnected = statusInfo.IsCommunicationEstablished,
                    Description = GetConnectionStateDescription(connectionState)
                },

                // 控制状态
                ControlState = new
                {
                    Current = controlState.ToString(),
                    Mode = controlMode.ToString(),
                    IsOnline = controlState == ControlState.OnlineLocal || controlState == ControlState.OnlineRemote,
                    IsRemoteEnabled = controlState == ControlState.OnlineRemote,
                    Description = GetControlStateDescription(controlState)
                },

                // 处理状态
                ProcessState = new
                {
                    Current = processState.ToString(),
                    IsProcessing = IsProcessingState(processState),
                    Description = GetProcessStateDescription(processState)
                },

                // 设备状态
                EquipmentState = new
                {
                    Current = equipmentState.ToString(),
                    Description = GetEquipmentStateDescription(equipmentState)
                },

                // 通信建立状态
                CommunicationEstablished = statusInfo.IsCommunicationEstablished,

                // 时间戳
                Timestamp = DateTime.Now,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private object GetStateFromFallbackData()
        {
            return new
            {
                // HSMS连接状态
                ConnectionState = new
                {
                    Current = _fallbackData.ConnectionState.ToString(),
                    IsConnected = _fallbackData.ConnectionState == HsmsConnectionState.Selected,
                    Description = GetConnectionStateDescription(_fallbackData.ConnectionState)
                },

                // 控制状态
                ControlState = new
                {
                    Current = _fallbackData.ControlState.ToString(),
                    Mode = _fallbackData.ControlMode.ToString(),
                    IsOnline = _fallbackData.ControlState == ControlState.OnlineLocal || _fallbackData.ControlState == ControlState.OnlineRemote,
                    IsRemoteEnabled = _fallbackData.ControlState == ControlState.OnlineRemote,
                    Description = GetControlStateDescription(_fallbackData.ControlState)
                },

                // 处理状态
                ProcessState = new
                {
                    Current = _fallbackData.ProcessState.ToString(),
                    IsProcessing = _fallbackData.ProcessState == ProcessState.Executing,
                    Description = GetProcessStateDescription(_fallbackData.ProcessState)
                },

                // 设备状态
                EquipmentState = new
                {
                    Current = _fallbackData.EquipmentState.ToString(),
                    Description = GetEquipmentStateDescription(_fallbackData.EquipmentState)
                },

                // 通信建立状态
                CommunicationEstablished = _fallbackData.ConnectionState == HsmsConnectionState.Selected,

                // 时间戳
                Timestamp = DateTime.Now,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private HsmsConnectionState GetConnectionStateFromStatus(bool isCommunicationEstablished)
        {
            if (_secsConnectionManager != null)
            {
                return _secsConnectionManager.HsmsConnectionState;
            }

            // 根据通信建立状态推断连接状态
            return isCommunicationEstablished ? HsmsConnectionState.Selected : HsmsConnectionState.NotConnected;
        }

        private bool GetCommunicationStatus()
        {
            if (_stateService != null)
            {
                return _stateService.IsCommunicationEstablishedAsync().Result;
            }
            return _fallbackData.ConnectionState == HsmsConnectionState.Selected;
        }

        private string GetOperatingMode()
        {
            if (_stateService != null)
            {
                return "Production (EquipmentStateService)";
            }
            else
            {
                return "Simulation (Fallback Data)";
            }
        }

        private string? CalculateUptime(DateTime? lastConnectedTime)
        {
            if (lastConnectedTime.HasValue)
            {
                var uptime = DateTime.Now - lastConnectedTime.Value;
                return uptime.ToString(@"dd\.hh\:mm\:ss");
            }
            return null;
        }

        private bool IsProcessingState(ProcessState state)
        {
            return state == ProcessState.Executing || state == ProcessState.Pause ||
                   state == ProcessState.Stopping || state == ProcessState.Aborting;
        }

        // 状态描述方法（与之前相同）
        private string GetConnectionStateDescription(HsmsConnectionState state)
        {
            return state switch
            {
                HsmsConnectionState.NotConnected => "未连接",
                HsmsConnectionState.Connecting => "正在连接",
                HsmsConnectionState.Connected => "已连接，等待选中",
                HsmsConnectionState.Selected => "已选中，可通信",
                HsmsConnectionState.Disconnecting => "正在断开",
                HsmsConnectionState.Error => "连接错误",
                HsmsConnectionState.NotEnabled => "未启用",
                HsmsConnectionState.Retry => "重连中",
                _ => "未知状态"
            };
        }

        private string GetControlStateDescription(ControlState state)
        {
            return state switch
            {
                ControlState.EquipmentOffline => "设备离线",
                ControlState.AttemptOnline => "尝试在线",
                ControlState.HostOffline => "主机离线",
                ControlState.OnlineLocal => "在线本地控制",
                ControlState.OnlineRemote => "在线远程控制",
                _ => "未知状态"
            };
        }

        private string GetProcessStateDescription(ProcessState state)
        {
            return state switch
            {
                ProcessState.Init => "初始化中",
                ProcessState.Idle => "空闲",
                ProcessState.Setup => "准备中",
                ProcessState.Executing => "执行中",
                ProcessState.Pause => "暂停中",
                ProcessState.Paused => "已暂停",
                ProcessState.Stopping => "停止中",
                ProcessState.Aborting => "中止中",
                ProcessState.Completed => "已完成",
                ProcessState.Aborted => "已中止",
                _ => "未知状态"
            };
        }

        private string GetEquipmentStateDescription(EquipmentState state)
        {
            return state switch
            {
                EquipmentState.Unknown => "未知",
                EquipmentState.Standby => "待机",
                EquipmentState.Productive => "生产中",
                EquipmentState.Engineering => "工程模式",
                EquipmentState.ScheduledDown => "计划停机",
                EquipmentState.UnscheduledDown => "非计划停机",
                _ => "未知状态"
            };
        }

        #endregion
    }

    #region 数据模型（保持与之前相同）

    public class StateChangeRequest
    {
        public string? Action { get; set; }
        public string? Parameter { get; set; }
        public string? Reason { get; set; }
    }

    public class SimulateRequest
    {
        public string? Action { get; set; }
    }

    public class StateHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string StateType { get; set; } = "";
        public string FromState { get; set; } = "";
        public string ToState { get; set; } = "";
        public string Trigger { get; set; } = "";
    }

    public class ConnectionStatistics
    {
        public int ConnectionCount { get; set; }
        public int MessagesSent { get; set; }
        public int MessagesReceived { get; set; }
        public DateTime? LastConnectedTime { get; set; }
        public DateTime? LastDisconnectedTime { get; set; }
        public TimeSpan? Uptime => LastConnectedTime.HasValue ? DateTime.Now - LastConnectedTime.Value : null;
    }

    /// <summary>
    /// 降级状态数据（当真实服务不可用时使用）
    /// </summary>
    public class FallbackStateData
    {
        public HsmsConnectionState ConnectionState { get; set; } = HsmsConnectionState.NotConnected;
        public ControlState ControlState { get; set; } = ControlState.EquipmentOffline;
        public ControlMode ControlMode { get; set; } = ControlMode.Offline;
        public ProcessState ProcessState { get; set; } = ProcessState.Idle;
        public EquipmentState EquipmentState { get; set; } = EquipmentState.Standby;

        public List<StateHistoryEntry> StateHistory { get; set; } = new List<StateHistoryEntry>();
        public ConnectionStatistics Statistics { get; set; } = new ConnectionStatistics();

        public FallbackStateData()
        {
            // 初始化一些示例历史记录
            AddStateHistory("System", "Unknown", "Initialized", "SystemStart");
            AddStateHistory("EquipmentState", "Unknown", "Standby", "Initialize");
            AddStateHistory("ProcessState", "Unknown", "Idle", "Initialize");
        }

        public void AddStateHistory(string stateType, string fromState, string toState, string trigger)
        {
            StateHistory.Add(new StateHistoryEntry
            {
                Timestamp = DateTime.Now,
                StateType = stateType,
                FromState = fromState,
                ToState = toState,
                Trigger = trigger
            });

            if (StateHistory.Count > 100)
            {
                StateHistory.RemoveAt(0);
            }
        }

        public (bool success, string message) ChangeControlState(string? action)
        {
            var oldState = ControlState;

            switch (action?.ToLower())
            {
                case "goonlinelocal":
                    if (ConnectionState == HsmsConnectionState.Selected)
                    {
                        ControlState = ControlState.OnlineLocal;
                        ControlMode = ControlMode.Local;
                        AddStateHistory("ControlState", oldState.ToString(), ControlState.ToString(), action);
                        return (true, "成功切换到本地在线模式");
                    }
                    return (false, "连接未建立，无法切换到在线模式");

                case "goonlineremote":
                    if (ConnectionState == HsmsConnectionState.Selected)
                    {
                        ControlState = ControlState.OnlineRemote;
                        ControlMode = ControlMode.Remote;
                        AddStateHistory("ControlState", oldState.ToString(), ControlState.ToString(), action);
                        return (true, "成功切换到远程在线模式");
                    }
                    return (false, "连接未建立，无法切换到在线模式");

                case "gooffline":
                    ControlState = ControlState.EquipmentOffline;
                    ControlMode = ControlMode.Offline;
                    AddStateHistory("ControlState", oldState.ToString(), ControlState.ToString(), action);
                    return (true, "成功切换到离线模式");

                default:
                    return (false, "不支持的操作");
            }
        }

        public (bool success, string message) ChangeProcessState(string? action)
        {
            var oldState = ProcessState;

            switch (action?.ToLower())
            {
                case "start":
                    if (ProcessState == ProcessState.Idle)
                    {
                        ProcessState = ProcessState.Executing;
                        AddStateHistory("ProcessState", oldState.ToString(), ProcessState.ToString(), action);
                        return (true, "处理已开始");
                    }
                    return (false, "当前状态不允许开始处理");

                case "pause":
                    if (ProcessState == ProcessState.Executing)
                    {
                        ProcessState = ProcessState.Paused;
                        AddStateHistory("ProcessState", oldState.ToString(), ProcessState.ToString(), action);
                        return (true, "处理已暂停");
                    }
                    return (false, "当前状态不允许暂停");

                case "stop":
                    if (ProcessState == ProcessState.Executing || ProcessState == ProcessState.Paused)
                    {
                        ProcessState = ProcessState.Completed;
                        AddStateHistory("ProcessState", oldState.ToString(), ProcessState.ToString(), action);
                        return (true, "处理已停止");
                    }
                    return (false, "当前状态不允许停止");

                case "reset":
                    if (ProcessState == ProcessState.Completed || ProcessState == ProcessState.Aborted)
                    {
                        ProcessState = ProcessState.Idle;
                        AddStateHistory("ProcessState", oldState.ToString(), ProcessState.ToString(), action);
                        return (true, "处理状态已重置");
                    }
                    return (false, "当前状态不允许重置");

                default:
                    return (false, "不支持的处理操作");
            }
        }

        public (bool success, string message) SimulateConnection(string? action)
        {
            var oldState = ConnectionState;

            switch (action?.ToLower())
            {
                case "connect":
                    ConnectionState = HsmsConnectionState.Selected;
                    Statistics.ConnectionCount++;
                    Statistics.LastConnectedTime = DateTime.Now;
                    AddStateHistory("ConnectionState", oldState.ToString(), ConnectionState.ToString(), action);
                    return (true, "连接状态已更新");

                case "disconnect":
                    ConnectionState = HsmsConnectionState.NotConnected;
                    ControlState = ControlState.EquipmentOffline;
                    ControlMode = ControlMode.Offline;
                    Statistics.LastDisconnectedTime = DateTime.Now;
                    AddStateHistory("ConnectionState", oldState.ToString(), ConnectionState.ToString(), action);
                    return (true, "连接已断开");

                default:
                    return (false, "不支持的模拟操作");
            }
        }
    }

    #endregion
}