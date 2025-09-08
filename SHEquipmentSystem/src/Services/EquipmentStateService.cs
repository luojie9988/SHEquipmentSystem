// 文件路径: src/DiceEquipmentSystem/Services/EquipmentStateService.cs
// 版本: v1.1.0
// 描述: 设备状态服务 - 修复类型匹配问题

using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Stateless;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 设备状态服务实现
    /// 基于SEMI E30/E58标准的多维状态管理
    /// </summary>
    public class EquipmentStateService : IEquipmentStateService
    {
        #region 字段

        private readonly ILogger<EquipmentStateService> _logger;

        /// <summary>
        /// 控制状态机
        /// </summary>
        private readonly StateMachine<ControlState, ControlStateTrigger> _controlStateMachine;

        /// <summary>
        /// 处理状态机
        /// </summary>
        private readonly ProcessStateMachine _processStateMachine;

        /// <summary>
        /// 设备状态机
        /// </summary>
        private readonly StateMachine<EquipmentState, EquipmentStateTrigger> _equipmentStateMachine;

        /// <summary>
        /// 控制模式
        /// </summary>
        private ControlMode _controlMode = ControlMode.Offline;

        /// <summary>
        /// 通信建立标志
        /// </summary>
        private bool _isCommunicationEstablished = false;

        /// <summary>
        /// 状态锁
        /// </summary>
        private readonly ReaderWriterLockSlim _stateLock = new();

        /// <summary>
        /// 状态历史记录
        /// </summary>
        private readonly ConcurrentQueue<StateHistoryEntry> _stateHistory = new();

        /// <summary>
        /// 历史记录ID计数器
        /// </summary>
        private int _historyIdCounter = 0;

        /// <summary>
        /// 最大历史记录数
        /// </summary>
        private const int MaxHistorySize = 100;

        #endregion

        #region 事件 - 使用接口定义的类型

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <summary>
        /// 控制模式变更事件 - 使用接口定义的类型
        /// </summary>
        public event EventHandler<ControlModeChangeEventArgs>? ControlModeChanged;

        /// <summary>
        /// 处理状态变更事件
        /// </summary>
        public event EventHandler<ProcessStateChangeEventArgs>? ProcessStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="processStateMachine">处理状态机（可选）</param>
        public EquipmentStateService(
            ILogger<EquipmentStateService> logger,
            ProcessStateMachine? processStateMachine = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // 创建或使用提供的处理状态机
            if (processStateMachine != null)
            {
                _processStateMachine = processStateMachine;
            }
            else
            {
                var processLogger = new Logger<ProcessStateMachine>(new LoggerFactory());
                _processStateMachine = new ProcessStateMachine(processLogger);
            }

            // 订阅处理状态变更事件
            _processStateMachine.StateChanged += (sender, e) =>
            {
                OnProcessStateChanged(e.OldState, e.NewState, e.Trigger.ToString());
            };

            // 初始化控制状态机
            _controlStateMachine = CreateControlStateMachine();

            // 初始化设备状态机
            _equipmentStateMachine = CreateEquipmentStateMachine();

            _logger.LogInformation("设备状态服务已初始化");
        }

        #endregion

        #region 公共方法 - 状态获取

        /// <summary>
        /// 获取当前控制状态
        /// </summary>
        public async Task<ControlState> GetControlStateAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    return _controlStateMachine.State;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        /// <summary>
        /// 获取当前控制模式
        /// </summary>
        public async Task<ControlMode> GetControlModeAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    return _controlMode;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        /// <summary>
        /// 获取当前处理状态
        /// </summary>
        public async Task<ProcessState> GetProcessStateAsync()
        {
            return await Task.Run(() => _processStateMachine.CurrentState);
        }

        /// <summary>
        /// 获取当前设备状态
        /// </summary>
        public async Task<EquipmentState> GetEquipmentStateAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    return _equipmentStateMachine.State;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        /// <summary>
        /// 获取完整的设备状态信息 - 使用接口定义的类型
        /// </summary>
        /// <summary>
        /// 获取完整的设备状态信息
        /// </summary>
        public async Task<EquipmentStatusInfo> GetStatusInfoAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    // 判断设备是否可用
                    bool isAvailable = DetermineAvailability();

                    return new EquipmentStatusInfo
                    {
                        ControlState = _controlStateMachine.State,
                        ControlMode = _controlMode,
                        ProcessState = _processStateMachine.CurrentState,
                        EquipmentState = _equipmentStateMachine.State,
                        IsCommunicationEstablished = _isCommunicationEstablished,
                        CurrentRecipe = GetCurrentRecipe(),
                        CurrentMaterialId = GetCurrentMaterialId(),
                        IsAvailable = isAvailable,
                        AlarmCount = GetActiveAlarmCount(),
                        Timestamp = DateTime.Now
                    };
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        #endregion

        #region 公共方法 - 状态控制

        /// <summary>
        /// 请求在线
        /// </summary>
        /// <param name="remote">是否远程模式</param>
        public async Task<bool> RequestOnlineAsync(bool remote = true)
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    var currentState = _controlStateMachine.State;
                    var oldMode = _controlMode;

                    // 检查是否可以切换到在线
                    if (currentState != ControlState.EquipmentOffline)
                    {
                        _logger.LogWarning($"无法从 {currentState} 状态切换到在线");
                        return false;
                    }

                    // 检查通信是否已建立
                    if (!_isCommunicationEstablished)
                    {
                        _logger.LogWarning("通信未建立，无法切换到在线");
                        return false;
                    }

                    // 触发状态转换
                    if (remote)
                    {
                        _controlStateMachine.Fire(ControlStateTrigger.GoOnlineRemote);
                        _controlMode = ControlMode.Remote;
                        _logger.LogInformation("设备切换到在线远程模式");
                    }
                    else
                    {
                        _controlStateMachine.Fire(ControlStateTrigger.GoOnlineLocal);
                        _controlMode = ControlMode.Local;
                        _logger.LogInformation("设备切换到在线本地模式");
                    }

                    // 触发控制模式变更事件
                    OnControlModeChanged(oldMode, _controlMode, "RequestOnline");

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "请求在线失败");
                    return false;
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 请求离线
        /// </summary>
        public async Task<bool> RequestOfflineAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    var currentState = _controlStateMachine.State;
                    var oldMode = _controlMode;

                    // 检查是否可以切换到离线
                    if (currentState == ControlState.EquipmentOffline)
                    {
                        _logger.LogInformation("设备已经处于离线状态");
                        return true;
                    }

                    // 触发状态转换
                    _controlStateMachine.Fire(ControlStateTrigger.GoOffline);
                    _controlMode = ControlMode.Offline;

                    _logger.LogInformation("设备切换到离线模式");

                    // 触发控制模式变更事件
                    OnControlModeChanged(oldMode, _controlMode, "RequestOffline");

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "请求离线失败");
                    return false;
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 切换控制模式（Local/Remote）
        /// </summary>
        public async Task<bool> SwitchControlModeAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    var currentState = _controlStateMachine.State;
                    var oldMode = _controlMode;

                    switch (currentState)
                    {
                        case ControlState.OnlineLocal:
                            _controlStateMachine.Fire(ControlStateTrigger.SwitchToRemote);
                            _controlMode = ControlMode.Remote;
                            _logger.LogInformation("切换到远程控制模式");
                            break;

                        case ControlState.OnlineRemote:
                            _controlStateMachine.Fire(ControlStateTrigger.SwitchToLocal);
                            _controlMode = ControlMode.Local;
                            _logger.LogInformation("切换到本地控制模式");
                            break;

                        default:
                            _logger.LogWarning($"当前状态 {currentState} 不支持模式切换");
                            return false;
                    }

                    // 触发控制模式变更事件
                    OnControlModeChanged(oldMode, _controlMode, "SwitchMode");

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "切换控制模式失败");
                    return false;
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 设置通信建立状态
        /// </summary>
        public async Task SetCommunicationEstablishedAsync(bool established)
        {
            await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    var oldMode = _controlMode;
                    _isCommunicationEstablished = established;
                    _logger.LogInformation($"通信状态设置为: {(established ? "已建立" : "未建立")}");

                    if (!established && _controlStateMachine.State != ControlState.EquipmentOffline)
                    {
                        // 如果通信断开，自动切换到离线
                        _logger.LogWarning("通信断开，自动切换到离线模式");
                        _controlStateMachine.Fire(ControlStateTrigger.CommunicationLost);
                        _controlMode = ControlMode.Offline;
                        OnControlModeChanged(oldMode, _controlMode, "CommunicationLost");
                    }
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 检查通信是否已建立
        /// </summary>
        /// <returns>true表示通信已建立</returns>
        public async Task<bool> IsCommunicationEstablishedAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    return _isCommunicationEstablished;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        /// <summary>
        /// 检查是否可以建立通信
        /// </summary>
        public async Task<bool> CanEstablishCommunicationAsync()
        {
            return await Task.Run(() =>
            {
                _stateLock.EnterReadLock();
                try
                {
                    // 检查设备是否处于可以建立通信的状态
                    var equipmentState = _equipmentStateMachine.State;

                    // 如果设备处于维护或故障状态，不允许建立通信
                    if (equipmentState == EquipmentState.UnscheduledDown)
                    {
                        _logger.LogWarning("设备处于故障状态，无法建立通信");
                        return false;
                    }

                    return true;
                }
                finally
                {
                    _stateLock.ExitReadLock();
                }
            });
        }

        #endregion

        #region 公共方法 - 处理状态控制

        /// <summary>
        /// 开始处理
        /// </summary>
        public async Task<bool> StartProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 检查控制状态
                    if (_controlStateMachine.State != ControlState.OnlineRemote)
                    {
                        _logger.LogWarning("只有在远程控制模式下才能开始处理");
                        return false;
                    }

                    // 检查处理状态
                    if (!ProcessStateHelper.CanStartProcess(_processStateMachine.CurrentState))
                    {
                        _logger.LogWarning($"当前处理状态 {_processStateMachine.CurrentState} 不允许开始处理");
                        return false;
                    }

                    // 触发处理开始
                    if (_processStateMachine.CurrentState == ProcessState.Idle)
                    {
                        _processStateMachine.Fire(ProcessTrigger.StartSetup);
                        _processStateMachine.Fire(ProcessTrigger.SetupComplete);
                        _processStateMachine.Fire(ProcessTrigger.Start);
                    }
                    else if (_processStateMachine.CurrentState == ProcessState.Ready)
                    {
                        _processStateMachine.Fire(ProcessTrigger.Start);
                    }

                    // 更新设备状态
                    _stateLock.EnterWriteLock();
                    try
                    {
                        _equipmentStateMachine.Fire(EquipmentStateTrigger.StartProduction);
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    _logger.LogInformation("处理已开始");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "开始处理失败");
                    return false;
                }
            });
        }

        /// <summary>
        /// 暂停处理
        /// </summary>
        public async Task<bool> PauseProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_processStateMachine.CurrentState != ProcessState.Executing)
                    {
                        _logger.LogWarning("只有在执行状态下才能暂停");
                        return false;
                    }

                    _processStateMachine.Fire(ProcessTrigger.Pause);
                    _logger.LogInformation("处理已暂停");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "暂停处理失败");
                    return false;
                }
            });
        }

        /// <summary>
        /// 恢复处理
        /// </summary>
        public async Task<bool> ResumeProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_processStateMachine.CurrentState != ProcessState.Paused)
                    {
                        _logger.LogWarning("只有在暂停状态下才能恢复");
                        return false;
                    }

                    _processStateMachine.Fire(ProcessTrigger.Resume);
                    _logger.LogInformation("处理已恢复");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "恢复处理失败");
                    return false;
                }
            });
        }

        /// <summary>
        /// 停止处理
        /// </summary>
        public async Task<bool> StopProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var currentState = _processStateMachine.CurrentState;

                    if (!ProcessStateHelper.IsProcessing(currentState))
                    {
                        _logger.LogWarning("当前没有正在执行的处理");
                        return false;
                    }

                    // 触发中止
                    _processStateMachine.Fire(ProcessTrigger.Abort);

                    // 更新设备状态
                    _stateLock.EnterWriteLock();
                    try
                    {
                        _equipmentStateMachine.Fire(EquipmentStateTrigger.StopProduction);
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    _logger.LogInformation("处理已停止");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止处理失败");
                    return false;
                }
            });
        }

        /// <summary>
        /// 中止处理
        /// </summary>
        public async Task<bool> AbortProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var currentState = _processStateMachine.CurrentState;

                    // 检查是否可以中止
                    if (!ProcessStateHelper.IsProcessing(currentState) &&
                        currentState != ProcessState.Ready &&
                        currentState != ProcessState.Setup)
                    {
                        _logger.LogWarning($"当前状态 {currentState} 不允许中止");
                        return false;
                    }

                    // 触发中止
                    _processStateMachine.Fire(ProcessTrigger.Abort);

                    // 如果需要，触发后续状态
                    if (_processStateMachine.CurrentState == ProcessState.Aborting)
                    {
                        _processStateMachine.Fire(ProcessTrigger.AbortFinish);
                    }

                    // 更新设备状态
                    _stateLock.EnterWriteLock();
                    try
                    {
                        if (_equipmentStateMachine.State == EquipmentState.Productive)
                        {
                            _equipmentStateMachine.Fire(EquipmentStateTrigger.StopProduction);
                        }
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    _logger.LogInformation("处理已中止");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "中止处理失败");
                    return false;
                }
            });
        }

        /// <summary>
        /// 重置处理状态
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> ResetProcessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var currentState = _processStateMachine.CurrentState;

                    // 检查当前状态是否允许重置
                    if (currentState == ProcessState.Idle)
                    {
                        _logger.LogInformation("处理状态已经是空闲状态");
                        return true;
                    }

                    // 只有在完成或中止状态下才能重置
                    if (currentState != ProcessState.Completed &&
                        currentState != ProcessState.Aborted)
                    {
                        _logger.LogWarning($"当前状态 {currentState} 不允许重置，只有Completed或Aborted状态才能重置");
                        return false;
                    }

                    // 触发重置
                    _processStateMachine.Fire(ProcessTrigger.Reset);

                    // 更新设备状态到Standby
                    _stateLock.EnterWriteLock();
                    try
                    {
                        if (_equipmentStateMachine.State == EquipmentState.Productive)
                        {
                            _equipmentStateMachine.Fire(EquipmentStateTrigger.StopProduction);
                        }
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    _logger.LogInformation("处理状态已重置到空闲状态");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重置处理状态失败");
                    return false;
                }
            });
        }

        #endregion

        #region 公共方法 - 状态查询

        /// <summary>
        /// 检查设备是否在线
        /// </summary>
        public async Task<bool> IsOnlineAsync()
        {
            var state = await GetControlStateAsync();
            return state == ControlState.OnlineLocal || state == ControlState.OnlineRemote;
        }

        /// <summary>
        /// 检查设备是否处于远程模式
        /// </summary>
        public async Task<bool> IsRemoteModeAsync()
        {
            var state = await GetControlStateAsync();
            return state == ControlState.OnlineRemote;
        }

        /// <summary>
        /// 检查设备是否正在处理
        /// </summary>
        public async Task<bool> IsProcessingAsync()
        {
            var state = await GetProcessStateAsync();
            return ProcessStateHelper.IsProcessing(state);
        }

        /// <summary>
        /// 检查设备是否空闲
        /// </summary>
        public async Task<bool> IsIdleAsync()
        {
            var state = await GetProcessStateAsync();
            return state == ProcessState.Idle;
        }

        /// <summary>
        /// 获取状态历史记录 - 使用接口定义的类型
        /// </summary>
        public IEnumerable<StateHistoryEntry> GetStateHistory()
        {
            return _stateHistory.ToArray();
        }

        /// <summary>
        /// 清除状态历史
        /// </summary>
        public void ClearStateHistory()
        {
            while (_stateHistory.TryDequeue(out _))
            {
                // 清空队列
            }
            _logger.LogDebug("状态历史已清除");
        }

        #endregion

        #region 私有方法 - 状态机创建

        /// <summary>
        /// 创建控制状态机
        /// </summary>
        private StateMachine<ControlState, ControlStateTrigger> CreateControlStateMachine()
        {
            var machine = new StateMachine<ControlState, ControlStateTrigger>(ControlState.EquipmentOffline);

            // 配置状态转换
            machine.Configure(ControlState.EquipmentOffline)
                .Permit(ControlStateTrigger.GoOnlineLocal, ControlState.OnlineLocal)
                .Permit(ControlStateTrigger.GoOnlineRemote, ControlState.OnlineRemote)
                .Permit(ControlStateTrigger.AttemptOnline, ControlState.AttemptOnline);

            machine.Configure(ControlState.AttemptOnline)
                .Permit(ControlStateTrigger.GoOnlineLocal, ControlState.OnlineLocal)
                .Permit(ControlStateTrigger.GoOnlineRemote, ControlState.OnlineRemote)
                .Permit(ControlStateTrigger.GoOffline, ControlState.EquipmentOffline);

            machine.Configure(ControlState.OnlineLocal)
                .Permit(ControlStateTrigger.GoOffline, ControlState.EquipmentOffline)
                .Permit(ControlStateTrigger.SwitchToRemote, ControlState.OnlineRemote)
                .Permit(ControlStateTrigger.CommunicationLost, ControlState.EquipmentOffline);

            machine.Configure(ControlState.OnlineRemote)
                .Permit(ControlStateTrigger.GoOffline, ControlState.EquipmentOffline)
                .Permit(ControlStateTrigger.SwitchToLocal, ControlState.OnlineLocal)
                .Permit(ControlStateTrigger.CommunicationLost, ControlState.EquipmentOffline);

            machine.Configure(ControlState.HostOffline)
                .Permit(ControlStateTrigger.HostOnline, ControlState.OnlineRemote);

            // 订阅状态变更事件
            machine.OnTransitioned(transition =>
            {
                _logger.LogInformation($"控制状态转换: {transition.Source} -> {transition.Destination} [{transition.Trigger}]");
                RecordStateChange("ControlState", transition.Source, transition.Destination);
                OnStateChanged("ControlState", transition.Source, transition.Destination);
            });

            return machine;
        }

        /// <summary>
        /// 创建设备状态机
        /// </summary>
        private StateMachine<EquipmentState, EquipmentStateTrigger> CreateEquipmentStateMachine()
        {
            var machine = new StateMachine<EquipmentState, EquipmentStateTrigger>(EquipmentState.Standby);

            // 配置状态转换
            machine.Configure(EquipmentState.Standby)
                .Permit(EquipmentStateTrigger.StartProduction, EquipmentState.Productive)
                .Permit(EquipmentStateTrigger.EnterEngineering, EquipmentState.Engineering)
                .Permit(EquipmentStateTrigger.ScheduledDowntime, EquipmentState.ScheduledDown)
                .Permit(EquipmentStateTrigger.Failure, EquipmentState.UnscheduledDown);

            machine.Configure(EquipmentState.Productive)
                .Permit(EquipmentStateTrigger.StopProduction, EquipmentState.Standby)
                .Permit(EquipmentStateTrigger.Failure, EquipmentState.UnscheduledDown)
                .Permit(EquipmentStateTrigger.ScheduledDowntime, EquipmentState.ScheduledDown);

            machine.Configure(EquipmentState.Engineering)
                .Permit(EquipmentStateTrigger.ExitEngineering, EquipmentState.Standby)
                .Permit(EquipmentStateTrigger.Failure, EquipmentState.UnscheduledDown);

            machine.Configure(EquipmentState.ScheduledDown)
                .Permit(EquipmentStateTrigger.MaintenanceComplete, EquipmentState.Standby);

            machine.Configure(EquipmentState.UnscheduledDown)
                .Permit(EquipmentStateTrigger.RepairComplete, EquipmentState.Standby);

            machine.Configure(EquipmentState.NonScheduled)
                .Permit(EquipmentStateTrigger.Resume, EquipmentState.Standby);

            // 订阅状态变更事件
            machine.OnTransitioned(transition =>
            {
                _logger.LogInformation($"设备状态转换: {transition.Source} -> {transition.Destination} [{transition.Trigger}]");
                RecordStateChange("EquipmentState", transition.Source, transition.Destination);
                OnStateChanged("EquipmentState", transition.Source, transition.Destination);
            });

            return machine;
        }

        #endregion

        #region 私有方法 - 辅助

        /// <summary>
        /// 记录状态变更
        /// </summary>
        private void RecordStateChange(string stateType, object oldValue, object newValue)
        {
            var entry = new StateHistoryEntry
            {
                Id = Interlocked.Increment(ref _historyIdCounter),
                StateType = stateType,
                OldValue = oldValue?.ToString() ?? "null",
                NewValue = newValue?.ToString() ?? "null",
                Timestamp = DateTime.Now
            };

            _stateHistory.Enqueue(entry);

            // 限制历史记录大小
            while (_stateHistory.Count > MaxHistorySize)
            {
                _stateHistory.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 触发状态变更事件
        /// </summary>
        private void OnStateChanged(string stateType, object? oldValue, object? newValue)
        {
            StateChanged?.Invoke(this, new StateChangeEventArgs
            {
                StateType = stateType,
                OldValue = oldValue,
                NewValue = newValue,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 触发控制模式变更事件 - 使用接口定义的类型
        /// </summary>
        private void OnControlModeChanged(ControlMode? oldMode, ControlMode newMode, string? reason = null)
        {
            ControlModeChanged?.Invoke(this, new ControlModeChangeEventArgs
            {
                OldMode = oldMode,
                NewMode = newMode,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 触发处理状态变更事件
        /// </summary>
        private void OnProcessStateChanged(ProcessState oldState, ProcessState newState, string? trigger = null)
        {
            ProcessStateChanged?.Invoke(this, new ProcessStateChangeEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Trigger = trigger,
                Timestamp = DateTime.Now
            });
        }

        #endregion

        #region 私有方法 - 状态信息辅助

        /// <summary>
        /// 当前配方名称（内部存储）
        /// </summary>
        private string? _currentRecipe;

        /// <summary>
        /// 当前材料ID（内部存储）
        /// </summary>
        private string? _currentMaterialId;

        /// <summary>
        /// 活动报警计数
        /// </summary>
        private int _activeAlarmCount = 0;

        /// <summary>
        /// 获取当前配方名称
        /// </summary>
        private string? GetCurrentRecipe()
        {
            return _currentRecipe ?? "NONE";
        }

        /// <summary>
        /// 获取当前材料ID
        /// </summary>
        private string? GetCurrentMaterialId()
        {
            return _currentMaterialId ?? "NONE";
        }

        /// <summary>
        /// 获取活动报警数量
        /// </summary>
        private int GetActiveAlarmCount()
        {
            return _activeAlarmCount;
        }

        /// <summary>
        /// 判断设备是否可用
        /// </summary>
        private bool DetermineAvailability()
        {
            // 设备可用的条件：
            // 1. 在线状态
            // 2. 非故障状态
            // 3. 不在处理中或可以接受新任务

            bool isOnline = _controlStateMachine.State == ControlState.OnlineLocal ||
                           _controlStateMachine.State == ControlState.OnlineRemote;

            bool isNotFaulted = _equipmentStateMachine.State != EquipmentState.UnscheduledDown;

            bool canAcceptWork = _processStateMachine.CurrentState == ProcessState.Idle ||
                                 _processStateMachine.CurrentState == ProcessState.Ready;

            return isOnline && isNotFaulted && canAcceptWork;
        }

        /// <summary>
        /// 设置当前配方
        /// </summary>
        public async Task SetCurrentRecipeAsync(string? recipeName)
        {
            await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    _currentRecipe = recipeName;
                    _logger.LogInformation($"当前配方设置为: {recipeName ?? "NONE"}");
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 设置当前材料ID
        /// </summary>
        public async Task SetCurrentMaterialIdAsync(string? materialId)
        {
            await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    _currentMaterialId = materialId;
                    _logger.LogInformation($"当前材料ID设置为: {materialId ?? "NONE"}");
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 更新报警计数
        /// </summary>
        public async Task UpdateAlarmCountAsync(int count)
        {
            await Task.Run(() =>
            {
                _stateLock.EnterWriteLock();
                try
                {
                    _activeAlarmCount = Math.Max(0, count);
                    _logger.LogDebug($"活动报警数更新为: {_activeAlarmCount}");
                }
                finally
                {
                    _stateLock.ExitWriteLock();
                }
            });
        }

        #endregion
    }

    #region 辅助类 - 移除重复定义，使用接口中的定义

    /// <summary>
    /// 控制状态触发器
    /// </summary>
    public enum ControlStateTrigger
    {
        GoOffline,
        AttemptOnline,
        GoOnlineLocal,
        GoOnlineRemote,
        SwitchToLocal,
        SwitchToRemote,
        CommunicationLost,
        HostOnline,
        HostOffline
    }

    /// <summary>
    /// 设备状态触发器
    /// </summary>
    public enum EquipmentStateTrigger
    {
        StartProduction,
        StopProduction,
        EnterEngineering,
        ExitEngineering,
        ScheduledDowntime,
        MaintenanceComplete,
        Failure,
        RepairComplete,
        Resume
    }

    #endregion
}
