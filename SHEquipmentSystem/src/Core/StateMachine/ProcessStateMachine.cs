// 文件路径: src/DiceEquipmentSystem/Core/StateMachine/ProcessStateMachine.cs
// 版本: v1.0.0
// 描述: 处理状态机 - 管理划裂片设备的工艺流程状态

using System;
using DiceEquipmentSystem.Core.Enums;
using Microsoft.Extensions.Logging;
using Stateless;

namespace DiceEquipmentSystem.Core.StateMachine
{
    /// <summary>
    /// 处理状态机
    /// 管理划裂片设备的工艺流程状态转换
    /// </summary>
    public class ProcessStateMachine
    {
        #region 字段

        private readonly StateMachine<ProcessState, ProcessTrigger> _stateMachine;
        private readonly ILogger<ProcessStateMachine> _logger;
        private readonly object _lockObject = new object();

        #endregion

        #region 属性

        /// <summary>
        /// 当前处理状态
        /// </summary>
        public ProcessState CurrentState
        {
            get
            {
                lock (_lockObject)
                {
                    return _stateMachine.State;
                }
            }
        }

        /// <summary>
        /// 是否可以开始处理
        /// </summary>
        public bool CanStart => ProcessStateHelper.CanStartProcess(CurrentState);

        /// <summary>
        /// 是否正在处理
        /// </summary>
        public bool IsProcessing => ProcessStateHelper.IsProcessing(CurrentState);

        #endregion

        #region 事件

        /// <summary>
        /// 状态变更事件
        /// </summary>
        public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="initialState">初始状态</param>
        public ProcessStateMachine(
            ILogger<ProcessStateMachine> logger,
            ProcessState initialState = ProcessState.Init)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateMachine = new StateMachine<ProcessState, ProcessTrigger>(initialState);

            ConfigureStateMachine();

            _logger.LogInformation($"处理状态机已初始化，初始状态: {initialState}");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 触发状态转换
        /// </summary>
        /// <param name="trigger">触发器</param>
        /// <returns>是否成功触发</returns>
        public bool Fire(ProcessTrigger trigger)
        {
            lock (_lockObject)
            {
                try
                {
                    if (_stateMachine.CanFire(trigger))
                    {
                        _stateMachine.Fire(trigger);
                        _logger.LogDebug($"触发 {trigger} 成功");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"无法触发 {trigger}，当前状态: {CurrentState}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"状态转换失败: {trigger}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 尝试触发状态转换
        /// </summary>
        /// <param name="trigger">触发器</param>
        /// <param name="guard">守卫条件</param>
        /// <returns>是否成功触发</returns>
        public bool TryFire(ProcessTrigger trigger, Func<bool>? guard = null)
        {
            if (guard != null && !guard())
            {
                _logger.LogDebug($"触发 {trigger} 的守卫条件不满足");
                return false;
            }

            return Fire(trigger);
        }

        /// <summary>
        /// 检查是否可以触发指定的转换
        /// </summary>
        /// <param name="trigger">触发器</param>
        /// <returns>是否可以触发</returns>
        public bool CanFire(ProcessTrigger trigger)
        {
            lock (_lockObject)
            {
                return _stateMachine.CanFire(trigger);
            }
        }

        /// <summary>
        /// 获取当前状态允许的触发器
        /// </summary>
        /// <returns>允许的触发器列表</returns>
        public IEnumerable<ProcessTrigger> GetPermittedTriggers()
        {
            lock (_lockObject)
            {
                return _stateMachine.PermittedTriggers;
            }
        }

        /// <summary>
        /// 重置到初始状态
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                // 根据当前状态尝试回到Idle
                var currentState = _stateMachine.State;

                switch (currentState)
                {
                    case ProcessState.Completed:
                    case ProcessState.Aborted:
                        Fire(ProcessTrigger.Reset);
                        break;

                    case ProcessState.Executing:
                    case ProcessState.Paused:
                        Fire(ProcessTrigger.Abort);
                        Fire(ProcessTrigger.AbortFinish);
                        Fire(ProcessTrigger.Reset);
                        break;

                    default:
                        _logger.LogWarning($"无法从 {currentState} 状态重置");
                        break;
                }
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 配置状态机转换规则
        /// 只配置划裂片设备实际使用的状态转换
        /// </summary>
        private void ConfigureStateMachine()
        {
            // Init -> Idle (初始化完成)
            _stateMachine.Configure(ProcessState.Init)
                .Permit(ProcessTrigger.InitComplete, ProcessState.Idle)
                .OnEntry(() => _logger.LogInformation("进入初始化状态"))
                .OnExit(() => _logger.LogInformation("退出初始化状态"));

            // Idle -> Setup (开始设置)
            _stateMachine.Configure(ProcessState.Idle)
                .Permit(ProcessTrigger.StartSetup, ProcessState.Setup)
                .OnEntry(() => _logger.LogInformation("进入空闲状态"));

            // Setup -> Ready (设置完成)
            // Setup -> Aborting (异常中止)
            _stateMachine.Configure(ProcessState.Setup)
                .Permit(ProcessTrigger.SetupComplete, ProcessState.Ready)
                .Permit(ProcessTrigger.Abort, ProcessState.Aborting)
                .OnEntry(() => _logger.LogInformation("进入设置状态"));

            // Ready -> Executing (开始执行)
            // Ready -> Aborting (异常中止)
            _stateMachine.Configure(ProcessState.Ready)
                .Permit(ProcessTrigger.Start, ProcessState.Executing)
                .Permit(ProcessTrigger.Abort, ProcessState.Aborting)
                .OnEntry(() => _logger.LogInformation("进入就绪状态"));

            // Executing -> Paused (暂停)
            // Executing -> Completing (正常完成)
            // Executing -> Aborting (异常中止)
            _stateMachine.Configure(ProcessState.Executing)
                .Permit(ProcessTrigger.Pause, ProcessState.Paused)
                .Permit(ProcessTrigger.Complete, ProcessState.Completing)
                .Permit(ProcessTrigger.Abort, ProcessState.Aborting)
                .OnEntry(() => _logger.LogInformation("进入执行状态"))
                .OnExit(() => _logger.LogInformation("退出执行状态"));

            // Paused -> Executing (恢复)
            // Paused -> Aborting (异常中止)
            _stateMachine.Configure(ProcessState.Paused)
                .Permit(ProcessTrigger.Resume, ProcessState.Executing)
                .Permit(ProcessTrigger.Abort, ProcessState.Aborting)
                .OnEntry(() => _logger.LogInformation("进入暂停状态"));

            // Completing -> Completed
            _stateMachine.Configure(ProcessState.Completing)
                .Permit(ProcessTrigger.CompleteFinish, ProcessState.Completed)
                .OnEntry(() => _logger.LogInformation("进入完成中状态"));

            // Completed -> Idle (重置，准备下一片)
            _stateMachine.Configure(ProcessState.Completed)
                .Permit(ProcessTrigger.Reset, ProcessState.Idle)
                .OnEntry(() => _logger.LogInformation("进入已完成状态"));

            // Aborting -> Aborted
            _stateMachine.Configure(ProcessState.Aborting)
                .Permit(ProcessTrigger.AbortFinish, ProcessState.Aborted)
                .OnEntry(() => _logger.LogInformation("进入中止中状态"));

            // Aborted -> Idle (重置)
            _stateMachine.Configure(ProcessState.Aborted)
                .Permit(ProcessTrigger.Reset, ProcessState.Idle)
                .OnEntry(() => _logger.LogInformation("进入已中止状态"));

            // 配置未使用的状态（保持完整性）
            ConfigureUnusedStates();

            // 订阅状态转换事件
            _stateMachine.OnTransitioned(transition =>
            {
                _logger.LogInformation($"ProcessState转换: {transition.Source} -> {transition.Destination} [{transition.Trigger}]");
                OnStateChanged(transition.Source, transition.Destination, transition.Trigger);
            });

            // 订阅未处理的触发器
            _stateMachine.OnUnhandledTrigger((state, trigger) =>
            {
                _logger.LogWarning($"未处理的触发器: State={state}, Trigger={trigger}");
            });
        }

        /// <summary>
        /// 配置未使用的状态（保持SEMI标准完整性）
        /// </summary>
        private void ConfigureUnusedStates()
        {
            // 这些状态在划裂片设备中较少使用，但为了标准符合性保留

            _stateMachine.Configure(ProcessState.Pause)
                .Permit(ProcessTrigger.PauseComplete, ProcessState.Paused);

            _stateMachine.Configure(ProcessState.Resume)
                .Permit(ProcessTrigger.ResumeComplete, ProcessState.Executing);

            _stateMachine.Configure(ProcessState.Hold)
                .Permit(ProcessTrigger.HoldComplete, ProcessState.Held);

            _stateMachine.Configure(ProcessState.Held)
                .Permit(ProcessTrigger.ReleaseHold, ProcessState.Executing);

            _stateMachine.Configure(ProcessState.Abort)
                .Permit(ProcessTrigger.AbortComplete, ProcessState.Aborting);

            _stateMachine.Configure(ProcessState.Stop)
                .Permit(ProcessTrigger.StopComplete, ProcessState.Stopping);

            _stateMachine.Configure(ProcessState.Stopping)
                .Permit(ProcessTrigger.StopFinish, ProcessState.Stopped);

            _stateMachine.Configure(ProcessState.Stopped)
                .Permit(ProcessTrigger.Reset, ProcessState.Idle);

            _stateMachine.Configure(ProcessState.Complete)
                .Permit(ProcessTrigger.CompleteAck, ProcessState.Completing);
        }

        /// <summary>
        /// 触发状态变更事件
        /// </summary>
        private void OnStateChanged(ProcessState oldState, ProcessState newState, ProcessTrigger trigger)
        {
            StateChanged?.Invoke(this, new ProcessStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Trigger = trigger,
                Timestamp = DateTime.Now
            });
        }

        #endregion
    }

    /// <summary>
    /// 处理状态触发器
    /// </summary>
    public enum ProcessTrigger
    {
        // 基本流程
        InitComplete,      // 初始化完成
        StartSetup,        // 开始设置
        SetupComplete,     // 设置完成
        Start,             // 开始处理

        // 暂停/恢复
        Pause,             // 暂停
        PauseComplete,     // 暂停完成
        Resume,            // 恢复
        ResumeComplete,    // 恢复完成

        // 完成
        Complete,          // 完成
        CompleteAck,       // 完成确认
        CompleteFinish,    // 完成结束

        // 中止
        Abort,             // 中止
        AbortComplete,     // 中止完成
        AbortFinish,       // 中止结束

        // 停止
        Stop,              // 停止
        StopComplete,      // 停止完成
        StopFinish,        // 停止结束

        // 保持
        Hold,              // 保持
        HoldComplete,      // 保持完成
        ReleaseHold,       // 释放保持

        // 重置
        Reset              // 重置到Idle
    }

    /// <summary>
    /// 处理状态变更事件参数
    /// </summary>
    public class ProcessStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 旧状态
        /// </summary>
        public ProcessState OldState { get; set; }

        /// <summary>
        /// 新状态
        /// </summary>
        public ProcessState NewState { get; set; }

        /// <summary>
        /// 触发器
        /// </summary>
        public ProcessTrigger Trigger { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
