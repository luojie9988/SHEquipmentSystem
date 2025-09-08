// 文件路径: src/DiceEquipmentSystem/Core/StateMachine/EqpMultiStateManager.cs
// 版本: v2.0.0
// 描述: 设备端多维状态管理器 - 管理六个维度的状态协调和转换

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Events;
using DiceEquipmentSystem.Core.Models;
using Microsoft.Extensions.Logging;
using Stateless;

namespace DiceEquipmentSystem.Core.StateMachine
{
    /// <summary>
    /// 设备端多维状态管理器
    /// 协调管理HSMS连接状态、控制状态、控制模式、处理状态、设备状态
    /// 基于SEMI E30/E37标准实现，与Host端完全对应
    /// </summary>
    public class EqpMultiStateManager : IDisposable
    {
        #region 私有字段

        private readonly ILogger<EqpMultiStateManager> _logger;
        private readonly ILoggerFactory? _loggerFactory;

        /// <summary>
        /// HSMS连接状态机
        /// </summary>
        private readonly StateMachine<HsmsConnectionState, HsmsTrigger> _hsmsStateMachine;

        /// <summary>
        /// 控制状态机
        /// </summary>
        private readonly StateMachine<ControlState, ControlTrigger> _controlStateMachine;

        /// <summary>
        /// 设备状态机
        /// </summary>
        private readonly StateMachine<EquipmentState, EquipmentTrigger> _equipmentStateMachine;

        /// <summary>
        /// 处理状态机（使用现有的ProcessStateMachine）
        /// </summary>
        private readonly ProcessStateMachine _processStateMachine;

        /// <summary>
        /// 控制模式
        /// </summary>
        private ControlMode _controlMode = ControlMode.Offline;

        /// <summary>
        /// 状态同步锁
        /// </summary>
        private readonly ReaderWriterLockSlim _stateLock = new();

        /// <summary>
        /// 状态历史记录
        /// </summary>
        private readonly ConcurrentQueue<EqpStateChangeRecord> _stateHistory = new();

        /// <summary>
        /// 通信是否已建立标志
        /// </summary>
        private bool _isCommunicationEstablished;

        /// <summary>
        /// 最后状态更新时间
        /// </summary>
        private DateTime _lastStateUpdateTime = DateTime.Now;

        #endregion

        #region 触发器定义

        /// <summary>
        /// HSMS连接触发器
        /// </summary>
        private enum HsmsTrigger
        {
            Connect,           // 连接建立
            Select,            // 选中通信
            Deselect,          // 取消选中
            Disconnect,        // 断开连接
            ConnectionLost     // 连接丢失
        }

        /// <summary>
        /// 控制状态触发器
        /// </summary>
        private enum ControlTrigger
        {
            EstablishCommunication,  // 建立通信 S1F13
            GoOnlineLocal,          // 进入本地在线
            GoOnlineRemote,         // 进入远程在线
            GoOffline,              // 离线
            SwitchToLocal,          // 切换到本地
            SwitchToRemote,         // 切换到远程
            HostOffline             // 主机离线
        }

        /// <summary>
        /// 设备状态触发器
        /// </summary>
        private enum EquipmentTrigger
        {
            StartProduction,     // 开始生产
            StopProduction,      // 停止生产
            EnterMaintenance,    // 进入维护
            ExitMaintenance,     // 退出维护
            ErrorOccur,          // 发生错误
            ErrorClear,          // 清除错误
            Schedule,            // 计划停机
            Unschedule          // 非计划停机
        }

        #endregion

        #region 属性

        /// <summary>
        /// 当前HSMS连接状态
        /// </summary>
        public HsmsConnectionState ConnectionState
        {
            get
            {
                _stateLock.EnterReadLock();
                try { return _hsmsStateMachine.State; }
                finally { _stateLock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// 当前控制状态
        /// </summary>
        public ControlState ControlState
        {
            get
            {
                _stateLock.EnterReadLock();
                try { return _controlStateMachine.State; }
                finally { _stateLock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// 当前控制模式
        /// </summary>
        public ControlMode ControlMode
        {
            get
            {
                _stateLock.EnterReadLock();
                try { return _controlMode; }
                finally { _stateLock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// 当前处理状态
        /// </summary>
        public ProcessState ProcessState => _processStateMachine.CurrentState;

        /// <summary>
        /// 当前设备状态
        /// </summary>
        public EquipmentState EquipmentState
        {
            get
            {
                _stateLock.EnterReadLock();
                try { return _equipmentStateMachine.State; }
                finally { _stateLock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// 是否已连接并选中
        /// </summary>
        public bool IsConnected => ConnectionState == HsmsConnectionState.Selected;

        /// <summary>
        /// 是否在线
        /// </summary>
        public bool IsOnline => ControlState == ControlState.OnlineLocal ||
                                ControlState == ControlState.OnlineRemote;

        /// <summary>
        /// 是否可远程控制
        /// </summary>
        public bool IsRemoteEnabled => ControlState == ControlState.OnlineRemote;

        /// <summary>
        /// 是否正在处理
        /// </summary>
        public bool IsProcessing => ProcessStateHelper.IsProcessing(ProcessState);

        #endregion

        #region 事件

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<EqpStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        public event EventHandler<ConnectionStateChangedEvent>? ConnectionStateChanged;

        /// <summary>
        /// 控制状态变更事件
        /// </summary>
        public event EventHandler<ControlStateChangedEvent>? ControlStateChanged;

        /// <summary>
        /// 处理状态变更事件
        /// </summary>
        public event EventHandler<ProcessStateChangedEvent>? ProcessStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="processStateMachine">处理状态机（可选，否则创建新实例）</param>
        public EqpMultiStateManager(
            ILogger<EqpMultiStateManager> logger,
            ILoggerFactory? loggerFactory = null,
            ProcessStateMachine? processStateMachine = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loggerFactory = loggerFactory;

            // 使用提供的处理状态机或创建新的
            if (processStateMachine != null)
            {
                _processStateMachine = processStateMachine;
            }
            else if (_loggerFactory != null)
            {
                // 如果提供了LoggerFactory，使用它创建ProcessStateMachine的logger
                var processLogger = _loggerFactory.CreateLogger<ProcessStateMachine>();
                _processStateMachine = new ProcessStateMachine(processLogger, ProcessState.Init);
            }
            else
            {
                // 如果没有LoggerFactory，创建一个临时的
                var tempLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var processLogger = tempLoggerFactory.CreateLogger<ProcessStateMachine>();
                _processStateMachine = new ProcessStateMachine(processLogger, ProcessState.Init);
            }

            // 初始化HSMS连接状态机
            _hsmsStateMachine = new StateMachine<HsmsConnectionState, HsmsTrigger>(
                HsmsConnectionState.NotConnected);
            ConfigureHsmsStateMachine();

            // 初始化控制状态机
            _controlStateMachine = new StateMachine<ControlState, ControlTrigger>(
                ControlState.EquipmentOffline);
            ConfigureControlStateMachine();

            // 初始化设备状态机
            _equipmentStateMachine = new StateMachine<EquipmentState, EquipmentTrigger>(
                EquipmentState.Unknown);
            ConfigureEquipmentStateMachine();

            // 订阅处理状态机事件
            _processStateMachine.StateChanged += OnProcessStateChanged;

            _logger.LogInformation("EQP多维状态管理器已初始化");
        }

        #endregion

        #region HSMS连接状态机配置

        /// <summary>
        /// 配置HSMS连接状态机
        /// 基于SEMI E37标准的状态转换规则
        /// </summary>
        private void ConfigureHsmsStateMachine()
        {
            // NotConnected -> Connected (TCP连接建立)
            _hsmsStateMachine.Configure(HsmsConnectionState.NotConnected)
                .Permit(HsmsTrigger.Connect, HsmsConnectionState.Connected)
                .OnEntry(() => LogStateChange("HSMS", "NotConnected", "进入未连接状态"));

            // Connected -> Selected (Select.req/rsp成功)
            // Connected -> NotConnected (连接断开)
            _hsmsStateMachine.Configure(HsmsConnectionState.Connected)
                .Permit(HsmsTrigger.Select, HsmsConnectionState.Selected)
                .Permit(HsmsTrigger.Disconnect, HsmsConnectionState.NotConnected)
                .Permit(HsmsTrigger.ConnectionLost, HsmsConnectionState.NotConnected)
                .OnEntry(() => LogStateChange("HSMS", "Connected", "TCP连接已建立"))
                .OnExit(() => _logger.LogDebug("退出Connected状态"));

            // Selected -> Connected (Deselect或Separate)
            // Selected -> NotConnected (连接丢失)
            _hsmsStateMachine.Configure(HsmsConnectionState.Selected)
                .Permit(HsmsTrigger.Deselect, HsmsConnectionState.Connected)
                .Permit(HsmsTrigger.Disconnect, HsmsConnectionState.NotConnected)
                .Permit(HsmsTrigger.ConnectionLost, HsmsConnectionState.NotConnected)
                .OnEntry(() => {
                    LogStateChange("HSMS", "Selected", "通信已选中，可以交换SECS消息");
                    // 通信选中时自动尝试建立通信
                    Task.Run(() => TryEstablishCommunication());
                })
                .OnExit(() => {
                    _logger.LogWarning("退出Selected状态，通信中断");
                    // 级联处理：通信中断时控制状态回到离线
                    HandleConnectionLost();
                });

            // 订阅状态转换事件
            _hsmsStateMachine.OnTransitioned(transition =>
            {
                RaiseConnectionStateChanged(transition.Source, transition.Destination);
                RecordStateChange("HSMS", transition.Source.ToString(),
                    transition.Destination.ToString(), transition.Trigger.ToString());
            });
        }

        #endregion

        #region 控制状态机配置

        /// <summary>
        /// 配置控制状态机
        /// 基于SEMI E30标准的控制状态模型
        /// </summary>
        private void ConfigureControlStateMachine()
        {
            // EquipmentOffline -> AttemptOnline (S1F13建立通信)
            _controlStateMachine.Configure(ControlState.EquipmentOffline)
                .Permit(ControlTrigger.EstablishCommunication, ControlState.AttemptOnline)
                .OnEntry(() => {
                    LogStateChange("Control", "EquipmentOffline", "设备离线状态");
                    _controlMode = ControlMode.Offline;
                });

            // AttemptOnline -> HostOffline (通信建立成功)
            // AttemptOnline -> EquipmentOffline (通信建立失败)
            _controlStateMachine.Configure(ControlState.AttemptOnline)
                .Permit(ControlTrigger.HostOffline, ControlState.HostOffline)
                .Permit(ControlTrigger.GoOffline, ControlState.EquipmentOffline)
                .OnEntry(() => LogStateChange("Control", "AttemptOnline", "尝试建立通信"));

            // HostOffline -> OnlineLocal (S1F17本地在线)
            // HostOffline -> OnlineRemote (S1F17远程在线)
            // HostOffline -> EquipmentOffline (S1F15离线请求)
            _controlStateMachine.Configure(ControlState.HostOffline)
                .Permit(ControlTrigger.GoOnlineLocal, ControlState.OnlineLocal)
                .Permit(ControlTrigger.GoOnlineRemote, ControlState.OnlineRemote)
                .Permit(ControlTrigger.GoOffline, ControlState.EquipmentOffline)
                .OnEntry(() => {
                    LogStateChange("Control", "HostOffline", "主机离线状态");
                    _isCommunicationEstablished = true;
                });

            // OnlineLocal -> OnlineRemote (切换到远程)
            // OnlineLocal -> HostOffline (离线请求)
            _controlStateMachine.Configure(ControlState.OnlineLocal)
                .Permit(ControlTrigger.SwitchToRemote, ControlState.OnlineRemote)
                .Permit(ControlTrigger.GoOffline, ControlState.HostOffline)
                .OnEntry(() => {
                    LogStateChange("Control", "OnlineLocal", "在线本地控制");
                    _controlMode = ControlMode.Local;
                    RaiseCEID200(); // 控制状态变更事件
                });

            // OnlineRemote -> OnlineLocal (切换到本地)
            // OnlineRemote -> HostOffline (离线请求)
            _controlStateMachine.Configure(ControlState.OnlineRemote)
                .Permit(ControlTrigger.SwitchToLocal, ControlState.OnlineLocal)
                .Permit(ControlTrigger.GoOffline, ControlState.HostOffline)
                .OnEntry(() => {
                    LogStateChange("Control", "OnlineRemote", "在线远程控制");
                    _controlMode = ControlMode.Remote;
                    RaiseCEID201(); // 控制模式变更事件
                });

            // 订阅状态转换事件
            _controlStateMachine.OnTransitioned(transition =>
            {
                RaiseControlStateChanged(transition.Source, transition.Destination);
                RecordStateChange("Control", transition.Source.ToString(),
                    transition.Destination.ToString(), transition.Trigger.ToString());
            });
        }

        #endregion

        #region 设备状态机配置

        /// <summary>
        /// 配置设备状态机
        /// 管理设备的运行状态
        /// </summary>
        private void ConfigureEquipmentStateMachine()
        {
            // Unknown -> Standby (初始化完成)
            _equipmentStateMachine.Configure(EquipmentState.Unknown)
                .Permit(EquipmentTrigger.ErrorClear, EquipmentState.Standby)
                .OnEntry(() => LogStateChange("Equipment", "Unknown", "未知状态"));

            // Standby <-> Productive
            _equipmentStateMachine.Configure(EquipmentState.Standby)
                .Permit(EquipmentTrigger.StartProduction, EquipmentState.Productive)
                .Permit(EquipmentTrigger.EnterMaintenance, EquipmentState.Engineering)
                .Permit(EquipmentTrigger.Schedule, EquipmentState.ScheduledDown)
                .Permit(EquipmentTrigger.ErrorOccur, EquipmentState.UnscheduledDown)
                .OnEntry(() => LogStateChange("Equipment", "Standby", "待机状态"));

            // Productive -> Standby/Error
            _equipmentStateMachine.Configure(EquipmentState.Productive)
                .Permit(EquipmentTrigger.StopProduction, EquipmentState.Standby)
                .Permit(EquipmentTrigger.ErrorOccur, EquipmentState.UnscheduledDown)
                .Permit(EquipmentTrigger.Schedule, EquipmentState.ScheduledDown)
                .OnEntry(() => {
                    LogStateChange("Equipment", "Productive", "生产中");
                    RaiseCEID11001(); // 生产开始事件
                })
                .OnExit(() => RaiseCEID11002()); // 生产结束事件

            // Engineering (工程/维护模式)
            _equipmentStateMachine.Configure(EquipmentState.Engineering)
                .Permit(EquipmentTrigger.ExitMaintenance, EquipmentState.Standby)
                .Permit(EquipmentTrigger.ErrorOccur, EquipmentState.UnscheduledDown)
                .OnEntry(() => LogStateChange("Equipment", "Engineering", "工程维护模式"));

            // ScheduledDown (计划停机)
            _equipmentStateMachine.Configure(EquipmentState.ScheduledDown)
                .Permit(EquipmentTrigger.Unschedule, EquipmentState.Standby)
                .OnEntry(() => LogStateChange("Equipment", "ScheduledDown", "计划停机"));

            // UnscheduledDown (非计划停机/错误)
            _equipmentStateMachine.Configure(EquipmentState.UnscheduledDown)
                .Permit(EquipmentTrigger.ErrorClear, EquipmentState.Standby)
                .OnEntry(() => LogStateChange("Equipment", "UnscheduledDown", "非计划停机"));
        }

        #endregion

        #region 公共方法 - HSMS连接控制

        /// <summary>
        /// 处理HSMS连接建立
        /// </summary>
        public async Task OnHsmsConnectedAsync()
        {
            await FireTriggerAsync(_hsmsStateMachine, HsmsTrigger.Connect, "HSMS连接");
        }

        /// <summary>
        /// 处理HSMS选中
        /// </summary>
        public async Task OnHsmsSelectedAsync()
        {
            await FireTriggerAsync(_hsmsStateMachine, HsmsTrigger.Select, "HSMS选中");
        }

        /// <summary>
        /// 处理HSMS断开
        /// </summary>
        public async Task OnHsmsDisconnectedAsync()
        {
            await FireTriggerAsync(_hsmsStateMachine, HsmsTrigger.ConnectionLost, "HSMS断开");
        }

        #endregion

        #region 公共方法 - 控制状态管理

        /// <summary>
        /// 建立通信 (S1F13/F14)
        /// </summary>
        /// <param name="commAck">通信确认码</param>
        public async Task<bool> EstablishCommunicationAsync(CommAck commAck)
        {
            try
            {
                if (commAck == CommAck.Accepted)
                {
                    await FireTriggerAsync(_controlStateMachine,
                        ControlTrigger.EstablishCommunication, "建立通信");

                    // 通信建立后自动进入HostOffline
                    await Task.Delay(100);
                    await FireTriggerAsync(_controlStateMachine,
                        ControlTrigger.HostOffline, "进入HostOffline");

                    _logger.LogInformation("通信建立成功，进入HostOffline状态");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"通信建立被拒绝: {commAck}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "建立通信失败");
                return false;
            }
        }

        /// <summary>
        /// 处理在线请求响应 (S1F17/F18)
        /// </summary>
        /// <param name="onlineMode">请求的在线模式（0=远程，其他=本地）</param>
        /// <returns>在线确认码</returns>
        public async Task<OnlineAck> ProcessOnlineRequestAsync(byte onlineMode)
        {
            try
            {
                _stateLock.EnterWriteLock();
                try
                {
                    // 检查当前状态是否允许上线
                    if (ControlState != ControlState.HostOffline)
                    {
                        if (IsOnline)
                        {
                            _logger.LogWarning("已经在线，拒绝重复的在线请求");
                            return OnlineAck.AlreadyOnline;
                        }
                        else
                        {
                            _logger.LogWarning($"当前状态 {ControlState} 不允许上线");
                            return OnlineAck.LocalMode;
                        }
                    }

                    // 根据请求的模式触发状态转换
                    var trigger = onlineMode == 0 ?
                        ControlTrigger.GoOnlineRemote :
                        ControlTrigger.GoOnlineLocal;

                    if (_controlStateMachine.CanFire(trigger))
                    {
                        _controlStateMachine.Fire(trigger);
                        _logger.LogInformation($"成功切换到在线模式: {(onlineMode == 0 ? "Remote" : "Local")}");
                        return OnlineAck.Accepted;
                    }
                    else
                    {
                        _logger.LogWarning($"无法从 {ControlState} 触发 {trigger}");
                        return OnlineAck.LocalMode;
                    }
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理在线请求失败");
                return OnlineAck.LocalMode;
            }
        }

        /// <summary>
        /// 处理离线请求 (S1F15/F16)
        /// </summary>
        public async Task<bool> ProcessOfflineRequestAsync()
        {
            try
            {
                // 检查是否可以离线
                if (!IsOnline && ControlState != ControlState.HostOffline)
                {
                    _logger.LogWarning("设备已经离线");
                    return true;
                }

                // 如果正在处理，不允许离线
                if (IsProcessing)
                {
                    _logger.LogWarning("设备正在处理中，不能离线");
                    return false;
                }

                await FireTriggerAsync(_controlStateMachine,
                    ControlTrigger.GoOffline, "离线请求");

                _logger.LogInformation("设备已离线");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理离线请求失败");
                return false;
            }
        }

        /// <summary>
        /// 切换控制模式
        /// </summary>
        public async Task<bool> SwitchControlModeAsync(ControlMode targetMode)
        {
            try
            {
                if (!IsOnline)
                {
                    _logger.LogWarning("设备不在线，无法切换控制模式");
                    return false;
                }

                if (_controlMode == targetMode)
                {
                    _logger.LogDebug($"已经处于 {targetMode} 模式");
                    return true;
                }

                var trigger = targetMode == ControlMode.Remote ?
                    ControlTrigger.SwitchToRemote :
                    ControlTrigger.SwitchToLocal;

                await FireTriggerAsync(_controlStateMachine, trigger, $"切换到{targetMode}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"切换控制模式到 {targetMode} 失败");
                return false;
            }
        }

        #endregion

        #region 公共方法 - 处理控制

        /// <summary>
        /// 开始处理（修复版）
        /// </summary>
        public async Task<bool> StartProcessAsync()
        {
            return await Task.Run(() =>
            {
                if (!IsRemoteEnabled)
                {
                    _logger.LogWarning("非远程模式，不能启动处理");
                    return false;
                }

                if (!_processStateMachine.CanStart)
                {
                    _logger.LogWarning($"当前处理状态 {ProcessState} 不允许启动");
                    return false;
                }

                // 使用Fire方法触发状态转换
                bool success = false;

                // 根据当前状态触发相应的转换
                if (_processStateMachine.CurrentState == ProcessState.Idle)
                {
                    success = _processStateMachine.Fire(ProcessTrigger.StartSetup);
                    if (success)
                    {
                        success = _processStateMachine.Fire(ProcessTrigger.SetupComplete);
                        if (success)
                        {
                            success = _processStateMachine.Fire(ProcessTrigger.Start);
                        }
                    }
                }
                else if (_processStateMachine.CurrentState == ProcessState.Ready)
                {
                    success = _processStateMachine.Fire(ProcessTrigger.Start);
                }

                if (success)
                {
                    // 设备状态切换到生产
                    _stateLock.EnterWriteLock();
                    try
                    {
                        if (_equipmentStateMachine.CanFire(EquipmentTrigger.StartProduction))
                        {
                            _equipmentStateMachine.Fire(EquipmentTrigger.StartProduction);
                        }
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }
                }

                return success;
            });
        }

        /// <summary>
        /// 停止处理（修复版）
        /// </summary>
        public async Task<bool> StopProcessAsync()
        {
            return await Task.Run(() =>
            {
                bool success = _processStateMachine.Fire(ProcessTrigger.Abort);

                if (success)
                {
                    // 触发后续状态转换
                    if (_processStateMachine.CurrentState == ProcessState.Aborting)
                    {
                        _processStateMachine.Fire(ProcessTrigger.AbortFinish);
                    }

                    // 设备状态切换到待机
                    _stateLock.EnterWriteLock();
                    try
                    {
                        if (_equipmentStateMachine.CanFire(EquipmentTrigger.StopProduction))
                        {
                            _equipmentStateMachine.Fire(EquipmentTrigger.StopProduction);
                        }
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }
                }

                return success;
            });
        }

        /// <summary>
        /// 暂停处理（修复版）
        /// </summary>
        public async Task<bool> PauseProcessAsync()
        {
            return await Task.Run(() =>
            {
                if (_processStateMachine.CurrentState != ProcessState.Executing)
                {
                    _logger.LogWarning("只有在执行状态下才能暂停");
                    return false;
                }

                return _processStateMachine.Fire(ProcessTrigger.Pause);
            });
        }

        /// <summary>
        /// 恢复处理（修复版）
        /// </summary>
        public async Task<bool> ResumeProcessAsync()
        {
            return await Task.Run(() =>
            {
                if (_processStateMachine.CurrentState != ProcessState.Paused)
                {
                    _logger.LogWarning("只有在暂停状态下才能恢复");
                    return false;
                }

                return _processStateMachine.Fire(ProcessTrigger.Resume);
            });
        }

        #endregion

        #region 公共方法 - 状态查询

        /// <summary>
        /// 获取完整状态快照
        /// </summary>
        public EqpCompleteState GetCompleteState()
        {
            _stateLock.EnterReadLock();
            try
            {
                return new EqpCompleteState
                {
                    // 基础状态
                    ConnectionState = _hsmsStateMachine.State,
                    ControlState = _controlStateMachine.State,
                    ControlMode = _controlMode,
                    ProcessState = _processStateMachine.CurrentState,
                    EquipmentState = _equipmentStateMachine.State,

                    // 派生属性
                    IsConnected = IsConnected,
                    IsOnline = IsOnline,
                    IsRemoteEnabled = IsRemoteEnabled,
                    IsProcessing = IsProcessing,
                    IsCommunicationEstablished = _isCommunicationEstablished,

                    // 时间戳
                    LastUpdateTime = _lastStateUpdateTime,
                    Timestamp = DateTime.Now
                };
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取状态历史记录
        /// </summary>
        /// <param name="maxCount">最大记录数</param>
        public EqpStateChangeRecord[] GetStateHistory(int maxCount = 100)
        {
            return _stateHistory.Take(Math.Min(maxCount, _stateHistory.Count)).ToArray();
        }

        /// <summary>
        /// 检查状态一致性
        /// </summary>
        public bool ValidateStateConsistency()
        {
            _stateLock.EnterReadLock();
            try
            {
                // 规则1: 如果未连接，控制状态必须是离线
                if (ConnectionState != HsmsConnectionState.Selected &&
                    ControlState != ControlState.EquipmentOffline)
                {
                    _logger.LogWarning($"状态不一致: 未连接但控制状态为 {ControlState}");
                    return false;
                }

                // 规则2: 如果在线，必须已建立通信
                if (IsOnline && !_isCommunicationEstablished)
                {
                    _logger.LogWarning("状态不一致: 在线但通信未建立");
                    return false;
                }

                // 规则3: 远程模式必须在线
                if (_controlMode == ControlMode.Remote && !IsOnline)
                {
                    _logger.LogWarning("状态不一致: 远程模式但不在线");
                    return false;
                }

                // 规则4: 生产中设备状态必须是Productive
                if (IsProcessing && EquipmentState != EquipmentState.Productive)
                {
                    _logger.LogWarning($"状态不一致: 正在处理但设备状态为 {EquipmentState}");
                    return false;
                }

                return true;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 触发状态机转换（带锁）
        /// </summary>
        private async Task FireTriggerAsync<TState, TTrigger>(
            StateMachine<TState, TTrigger> stateMachine,
            TTrigger trigger,
            string description) where TState : struct where TTrigger : struct
        {
            await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    if (stateMachine.CanFire(trigger))
                    {
                        stateMachine.Fire(trigger);
                        _lastStateUpdateTime = DateTime.Now;
                    }
                    else
                    {
                        _logger.LogWarning($"无法触发 {trigger}: 当前状态 {stateMachine.State}");
                    }
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 尝试建立通信
        /// </summary>
        private async Task TryEstablishCommunication()
        {
            await Task.Delay(500); // 短暂延迟

            if (ControlState == ControlState.EquipmentOffline)
            {
                _logger.LogInformation("HSMS已选中，自动尝试建立通信");
                // 这里应该触发S1F13消息发送
                // 实际实现中由外部服务处理
            }
        }

        /// <summary>
        /// 处理连接丢失
        /// </summary>
        private void HandleConnectionLost()
        {
            _stateLock.EnterWriteLock();
            try
            {
                // 级联重置控制状态
                if (ControlState != ControlState.EquipmentOffline)
                {
                    _controlStateMachine.Fire(ControlTrigger.GoOffline);
                }

                // 重置通信标志
                _isCommunicationEstablished = false;

                // 如果正在处理，触发中止
                if (IsProcessing)
                {
                    // 同步触发中止
                    _processStateMachine.Fire(ProcessTrigger.Abort);
                    if (_processStateMachine.CurrentState == ProcessState.Aborting)
                    {
                        _processStateMachine.Fire(ProcessTrigger.AbortFinish);
                    }
                }

                _logger.LogWarning("连接丢失，状态已重置");
            }
            finally
            {
                _stateLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 记录状态变更
        /// </summary>
        private void RecordStateChange(string stateType, string oldValue, string newValue, string trigger)
        {
            var record = new EqpStateChangeRecord
            {
                Timestamp = DateTime.Now,
                StateType = stateType,
                OldValue = oldValue,
                NewValue = newValue,
                Trigger = trigger
            };

            _stateHistory.Enqueue(record);

            // 保持历史记录在限定大小
            while (_stateHistory.Count > 1000)
            {
                _stateHistory.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 记录状态变更日志
        /// </summary>
        private void LogStateChange(string category, string state, string message)
        {
            _logger.LogInformation($"[{category}] -> {state}: {message}");
        }

        #endregion

        #region 事件触发方法

        /// <summary>
        /// 触发连接状态变更事件
        /// </summary>
        private void RaiseConnectionStateChanged(HsmsConnectionState oldState, HsmsConnectionState newState)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEvent(oldState, newState));
        }

        /// <summary>
        /// 触发控制状态变更事件
        /// </summary>
        private void RaiseControlStateChanged(ControlState oldState, ControlState newState)
        {
            ControlStateChanged?.Invoke(this, new ControlStateChangedEvent
            {
                OldState = oldState,
                NewState = newState,
                ControlMode = _controlMode
            });
        }

        /// <summary>
        /// 处理处理状态变更事件
        /// </summary>
        private void OnProcessStateChanged(object? sender, ProcessStateChangedEventArgs e)
        {
            ProcessStateChanged?.Invoke(this, new ProcessStateChangedEvent(e.OldState, e.NewState));

            // 修复：直接使用ToString()，因为ProcessTrigger是枚举类型
            RecordStateChange("Process", e.OldState.ToString(), e.NewState.ToString(), e.Trigger.ToString());
        }

        /// <summary>
        /// 触发CEID事件
        /// </summary>
        private void RaiseCEID200() => RaiseEvent("CEID", 200, "控制状态变更");
        private void RaiseCEID201() => RaiseEvent("CEID", 201, "控制模式变更");
        private void RaiseCEID11001() => RaiseEvent("CEID", 11001, "处理开始");
        private void RaiseCEID11002() => RaiseEvent("CEID", 11002, "处理完成");

        private void RaiseEvent(string type, int id, string description)
        {
            _logger.LogInformation($"[{type}:{id}] {description}");
            // 实际实现中，这里应该触发S6F11事件报告
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _processStateMachine.StateChanged -= OnProcessStateChanged;
            _stateLock?.Dispose();
            _disposed = true;

            _logger.LogInformation("EQP多维状态管理器已释放");
        }

        #endregion
    }

    #region 辅助类

    /// <summary>
    /// 设备完整状态
    /// </summary>
    public class EqpCompleteState
    {
        public HsmsConnectionState ConnectionState { get; set; }
        public ControlState ControlState { get; set; }
        public ControlMode ControlMode { get; set; }
        public ProcessState ProcessState { get; set; }
        public EquipmentState EquipmentState { get; set; }

        public bool IsConnected { get; set; }
        public bool IsOnline { get; set; }
        public bool IsRemoteEnabled { get; set; }
        public bool IsProcessing { get; set; }
        public bool IsCommunicationEstablished { get; set; }

        public DateTime LastUpdateTime { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 状态变更记录
    /// </summary>
    public class EqpStateChangeRecord
    {
        public DateTime Timestamp { get; set; }
        public string StateType { get; set; } = "";
        public string OldValue { get; set; } = "";
        public string NewValue { get; set; } = "";
        public string Trigger { get; set; } = "";
    }

    /// <summary>
    /// 状态变更事件参数
    /// </summary>
    public class EqpStateChangedEventArgs : EventArgs
    {
        public string StateType { get; set; } = "";
        public object? OldState { get; set; }
        public object? NewState { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 控制状态变更事件
    /// </summary>
    public class ControlStateChangedEvent : EventBase
    {
        public ControlState OldState { get; set; }
        public ControlState NewState { get; set; }
        public ControlMode ControlMode { get; set; }

        public ControlStateChangedEvent() : base("StateManager") { }
    }

    #endregion
}
