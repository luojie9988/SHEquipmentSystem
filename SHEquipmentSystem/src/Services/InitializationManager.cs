using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.Secs.Handlers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 设备初始化管理器
    /// 统一管理SEMI E30标准的10步初始化流程
    /// </summary>
    public class InitializationManager : IInitializationManager
    {
        private readonly ILogger<InitializationManager> _logger;
        private readonly IEquipmentStateService _stateService;
        private readonly ISecsConnectionManager _connectionManager;
        private readonly IStatusVariableService _svidService;
        private readonly IEventReportService _eventService;
        private readonly IAlarmService _alarmService;
        private readonly IS1F13Handler _s1f13Handler;
        
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private InitializationState _currentState = InitializationState.NotStarted;
        private DateTime _initializationStartTime;
        private DateTime? _initializationCompleteTime;
        
        /// <summary>
        /// 初始化状态枚举
        /// </summary>
        public enum InitializationState
        {
            NotStarted,               // 未开始
            SecsInitializing,         // SECS初始化中
            ProcessInitializing,      // 处理状态初始化中
            CommunicationEnabling,    // 启用通信中
            WaitingForConnection,     // 等待连接
            WaitingForS1F13,         // 等待S1F13
            WaitingForS1F17,         // 等待S1F17
            InitializingFunctions,    // 初始化功能
            Completed,               // 完成
            Failed                   // 失败
        }
        
        /// <summary>
        /// 初始化进度事件
        /// </summary>
        public event EventHandler<InitializationProgressEventArgs>? InitializationProgress;
        
        /// <summary>
        /// 初始化完成事件
        /// </summary>
        public event EventHandler<InitializationCompleteEventArgs>? InitializationComplete;
        
        public InitializationManager(
            ILogger<InitializationManager> logger,
            IEquipmentStateService stateService,
            ISecsConnectionManager connectionManager,
            IStatusVariableService svidService,
            IEventReportService eventService,
            IAlarmService alarmService,
            IS1F13Handler s1f13Handler)
        {
            _logger = logger;
            _stateService = stateService;
            _connectionManager = connectionManager;
            _svidService = svidService;
            _eventService = eventService;
            _alarmService = alarmService;
            _s1f13Handler = s1f13Handler;
        }
        
        /// <summary>
        /// 获取当前初始化状态
        /// </summary>
        public InitializationState CurrentState => _currentState;
        
        /// <summary>
        /// 获取初始化是否完成
        /// </summary>
        public bool IsInitialized => _currentState == InitializationState.Completed;
        
        /// <summary>
        /// 获取初始化进度百分比
        /// </summary>
        public int GetInitializationProgress()
        {
            return _currentState switch
            {
                InitializationState.NotStarted => 0,
                InitializationState.SecsInitializing => 10,
                InitializationState.ProcessInitializing => 20,
                InitializationState.CommunicationEnabling => 30,
                InitializationState.WaitingForConnection => 40,
                InitializationState.WaitingForS1F13 => 50,
                InitializationState.WaitingForS1F17 => 70,
                InitializationState.InitializingFunctions => 90,
                InitializationState.Completed => 100,
                InitializationState.Failed => -1,
                _ => 0
            };
        }
        
        /// <summary>
        /// 执行完整的初始化流程
        /// 根据SEMI E30标准2.1节的10步初始化流程
        /// </summary>
        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (_currentState == InitializationState.Completed)
                {
                    _logger.LogInformation("设备已经初始化完成");
                    return true;
                }
                
                _logger.LogInformation("================== 开始设备初始化流程 ==================");
                _initializationStartTime = DateTime.Now;
                
                // 步骤1: SECS初始化（IP、Port、超时设置）
                if (!await Step1_InitializeSecs(cancellationToken))
                    return false;
                
                // 步骤2: 完成设备初始化（ProcessState从Init转换到Idle）
                if (!await Step2_CompleteProcessInitialization(cancellationToken))
                    return false;
                
                // 步骤3: 启用SECS/GEM通信
                if (!await Step3_EnableCommunication(cancellationToken))
                    return false;
                
                // 步骤4: 设备切换到远程模式（如果需要）
                if (!await Step4_SwitchToRemoteMode(cancellationToken))
                    return false;
                
                // 步骤5: 进入通信状态（等待HSMS连接）
                if (!await Step5_WaitForConnection(cancellationToken))
                    return false;
                
                // 步骤6-7: 建立通信 S1F13/S1F14
                if (!await Step6_WaitForS1F13(cancellationToken))
                    return false;
                
                // 步骤8-9: 请求在线 S1F17/S1F18
                if (!await Step8_WaitForS1F17(cancellationToken))
                    return false;
                
                // 步骤10: 初始化功能（S2F33-S2F40、S2F23-S2F24等）
                if (!await Step10_InitializeFunctions(cancellationToken))
                    return false;
                
                // 初始化完成
                _currentState = InitializationState.Completed;
                _initializationCompleteTime = DateTime.Now;
                var duration = _initializationCompleteTime.Value - _initializationStartTime;
                
                _logger.LogInformation("================== 设备初始化完成 ==================");
                _logger.LogInformation($"初始化耗时: {duration.TotalSeconds:F2}秒");
                
                // 触发完成事件
                OnInitializationComplete(new InitializationCompleteEventArgs
                {
                    Success = true,
                    Duration = duration,
                    Message = "设备初始化成功完成"
                });
                
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("设备初始化被取消");
                _currentState = InitializationState.Failed;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设备初始化失败");
                _currentState = InitializationState.Failed;
                
                OnInitializationComplete(new InitializationCompleteEventArgs
                {
                    Success = false,
                    Duration = DateTime.Now - _initializationStartTime,
                    Message = $"设备初始化失败: {ex.Message}"
                });
                
                return false;
            }
            finally
            {
                _initializationLock.Release();
            }
        }
        
        /// <summary>
        /// 步骤1: SECS初始化
        /// </summary>
        private async Task<bool> Step1_InitializeSecs(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.SecsInitializing;
            OnInitializationProgress("步骤1: SECS初始化", 10);
            
            _logger.LogInformation("步骤1: SECS初始化（IP、Port、超时设置）");
            
            try
            {
                // SECS连接参数已在配置文件中设置
                // 这里只需要验证配置是否正确
                var configObj = _connectionManager.GetConfiguration();
                if (configObj is EquipmentSystemConfiguration config)
                {
                    _logger.LogInformation($"  - 设备ID: {config.Equipment.DeviceId}");
                    _logger.LogInformation($"  - 模式: Passive");
                    _logger.LogInformation($"  - 监听端口: {config.Equipment.Port}");
                    _logger.LogInformation($"  - 超时设置: T3={config.Equipment.T3}s, T5={config.Equipment.T5}s, T6={config.Equipment.T6}s, T7={config.Equipment.T7}s");
                }
                else
                {
                    _logger.LogWarning("无法获取SECS配置信息");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SECS初始化失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤2: 完成处理状态初始化
        /// </summary>
        private async Task<bool> Step2_CompleteProcessInitialization(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.ProcessInitializing;
            OnInitializationProgress("步骤2: 完成设备初始化", 20);
            
            _logger.LogInformation("步骤2: 完成设备初始化");
            
            try
            {
                // 获取当前处理状态
                var processState = await _stateService.GetProcessStateAsync();
                _logger.LogInformation($"  - 当前处理状态: {processState}");
                
                // 如果处理状态还在Init，完成初始化
                if (processState == ProcessState.Init)
                {
                    bool success = await _stateService.CompleteProcessInitializationAsync();
                    if (success)
                    {
                        var newState = await _stateService.GetProcessStateAsync();
                        _logger.LogInformation($"  ✓ 处理状态初始化完成: {processState} -> {newState}");
                    }
                    else
                    {
                        _logger.LogError("  ✗ 处理状态初始化失败");
                        return false;
                    }
                }
                else
                {
                    _logger.LogInformation($"  - 处理状态已经初始化: {processState}");
                }
                
                // 验证各个状态维度
                var equipmentState = await _stateService.GetEquipmentStateAsync();
                var controlState = await _stateService.GetControlStateAsync();
                var controlMode = await _stateService.GetControlModeAsync();
                
                _logger.LogInformation($"  - 设备运行状态: {equipmentState}");
                _logger.LogInformation($"  - 设备控制状态: {controlState}");
                _logger.LogInformation($"  - 控制模式: {controlMode}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成设备初始化失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤3: 启用SECS/GEM通信
        /// </summary>
        private async Task<bool> Step3_EnableCommunication(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.CommunicationEnabling;
            OnInitializationProgress("步骤3: 启用SECS/GEM通信", 30);
            
            _logger.LogInformation("步骤3: 启用SECS/GEM通信");
            
            try
            {
                // 检查通信是否已启用
                var isEnabled = await _stateService.IsCommunicationEnabledAsync();
                
                if (isEnabled)
                {
                    _logger.LogInformation("  ✓ SECS/GEM通信已启用，设备可以接受S1F13建立通信请求");
                }
                else
                {
                    _logger.LogWarning("  ⚠ SECS/GEM通信未启用，可能是设备处于故障状态");
                    
                    // 尝试恢复到可通信状态
                    var equipmentState = await _stateService.GetEquipmentStateAsync();
                    if (equipmentState == EquipmentState.UnscheduledDown)
                    {
                        _logger.LogError("  ✗ 设备处于UnscheduledDown状态，无法启用通信");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启用SECS/GEM通信失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤4: 切换到远程模式（可选）
        /// </summary>
        private async Task<bool> Step4_SwitchToRemoteMode(CancellationToken cancellationToken)
        {
            OnInitializationProgress("步骤4: 检查控制模式", 35);
            
            _logger.LogInformation("步骤4: 检查控制模式");
            
            try
            {
                var controlMode = await _stateService.GetControlModeAsync();
                _logger.LogInformation($"  - 当前控制模式: {controlMode}");
                
                // 设备端在被动模式下等待主机请求，不主动切换模式
                _logger.LogInformation("  - 设备保持当前模式，等待主机请求");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查控制模式失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤5: 等待HSMS连接
        /// </summary>
        private async Task<bool> Step5_WaitForConnection(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.WaitingForConnection;
            OnInitializationProgress("步骤5: 等待HSMS连接", 40);
            
            _logger.LogInformation("步骤5: 进入通信状态（等待HSMS连接）");
            
            try
            {
                // 启动SECS连接服务（被动模式监听）
                if (!_connectionManager.IsConnected)
                {
                    _logger.LogInformation("  - 启动SECS监听服务...");
                    await _connectionManager.StartAsync();
                }
                
                // 等待连接建立（最多等待30秒）
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (!_connectionManager.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    if (DateTime.Now - startTime > timeout)
                    {
                        _logger.LogWarning("  ⚠ 等待HSMS连接超时（30秒）");
                        // 不返回失败，继续等待
                        break;
                    }
                    
                    await Task.Delay(1000, cancellationToken);
                }
                
                if (_connectionManager.IsConnected)
                {
                    _logger.LogInformation("  ✓ HSMS连接已建立");
                }
                else
                {
                    _logger.LogInformation("  - 继续等待主机连接...");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "等待HSMS连接失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤6-7: 等待S1F13/S1F14通信建立
        /// </summary>
        private async Task<bool> Step6_WaitForS1F13(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.WaitingForS1F13;
            OnInitializationProgress("步骤6-7: 等待通信建立", 50);
            
            _logger.LogInformation("步骤6-7: 等待S1F13建立通信请求");
            
            try
            {
                // 检查通信是否已建立
                if (await _stateService.IsCommunicationEstablishedAsync())
                {
                    _logger.LogInformation("  ✓ 通信已经建立");
                    return true;
                }
                
                // 注册S1F13处理完成事件
                var tcs = new TaskCompletionSource<bool>();
                EventHandler<EventArgs>? handler = null;
                
                handler = (sender, args) =>
                {
                    _logger.LogInformation("  ✓ 收到S1F13，通信建立成功");
                    tcs.TrySetResult(true);
                };
                
                _s1f13Handler.CommunicationEstablished += handler;
                
                try
                {
                    // 等待S1F13（最多等待60秒）
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(60));
                    
                    _logger.LogInformation("  - 等待主机发送S1F13...");
                    
                    var result = await tcs.Task.WaitAsync(cts.Token);
                    
                    if (result)
                    {
                        // 设置通信已建立状态
                        await _stateService.SetCommunicationEstablishedAsync(true);
                        return true;
                    }
                    
                    return false;
                }
                finally
                {
                    _s1f13Handler.CommunicationEstablished -= handler;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("  ⚠ 等待S1F13超时或被取消");
                // 不返回失败，设备继续运行
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "等待S1F13失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤8-9: 等待S1F17/S1F18在线请求
        /// </summary>
        private async Task<bool> Step8_WaitForS1F17(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.WaitingForS1F17;
            OnInitializationProgress("步骤8-9: 等待在线请求", 70);
            
            _logger.LogInformation("步骤8-9: 等待S1F17在线请求");
            
            try
            {
                // 检查是否已经在线
                if (await _stateService.IsOnlineAsync())
                {
                    _logger.LogInformation("   设备已经在线");
                    return true;
                }
                
                // 等待S1F17（最多等待30秒）
                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (!await _stateService.IsOnlineAsync() && !cancellationToken.IsCancellationRequested)
                {
                    if (DateTime.Now - startTime > timeout)
                    {
                        _logger.LogWarning("   等待S1F17超时（30秒）");
                        // 不返回失败，设备可以离线运行
                        break;
                    }
                    
                    await Task.Delay(1000, cancellationToken);
                }
                
                var controlState = await _stateService.GetControlStateAsync();
                _logger.LogInformation($"  - 当前控制状态: {controlState}");
                
                if (controlState == ControlState.OnlineRemote || controlState == ControlState.OnlineLocal)
                {
                    _logger.LogInformation("   设备已进入在线状态");
                }
                else
                {
                    _logger.LogInformation("   设备保持离线状态，等待主机请求");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "等待S1F17失败");
                return false;
            }
        }
        
        /// <summary>
        /// 步骤10: 初始化功能
        /// </summary>
        private async Task<bool> Step10_InitializeFunctions(CancellationToken cancellationToken)
        {
            _currentState = InitializationState.InitializingFunctions;
            OnInitializationProgress("步骤10: 初始化功能", 90);
            
            _logger.LogInformation("步骤10: 初始化功能（等待S2F33-S2F40、S2F23-S2F24等）");
            
            try
            {
                // 这些功能由主机端主动发起，设备端被动响应
                // 设备端只需要确保相关服务已准备好
                
                _logger.LogInformation("  - 状态变量服务: 就绪");
                _logger.LogInformation($"    已定义 {_svidService.GetDefinedSvidCount()} 个SVID");
                
                _logger.LogInformation("  - 事件报告服务: 就绪");
                _logger.LogInformation($"    已定义 {_eventService.GetDefinedEventCount()} 个事件");
                
                _logger.LogInformation("  - 报警服务: 就绪");
                _logger.LogInformation($"    已定义 {_alarmService.GetDefinedAlarmCount()} 个报警");
                
                _logger.LogInformation("   所有功能服务就绪，等待主机初始化命令");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化功能失败");
                return false;
            }
        }
        
        /// <summary>
        /// 重置初始化状态
        /// </summary>
        public async Task ResetAsync()
        {
            await _initializationLock.WaitAsync();
            try
            {
                _logger.LogInformation("重置初始化状态");
                _currentState = InitializationState.NotStarted;
                _initializationCompleteTime = null;
                
                // 重置通信状态
                await _stateService.SetCommunicationEstablishedAsync(false);
                
                // 如果需要，可以重置其他状态
            }
            finally
            {
                _initializationLock.Release();
            }
        }
        
        /// <summary>
        /// 获取初始化状态信息
        /// </summary>
        public InitializationStatus GetStatus()
        {
            return new InitializationStatus
            {
                State = _currentState,
                Progress = GetInitializationProgress(),
                StartTime = _initializationStartTime,
                CompleteTime = _initializationCompleteTime,
                IsInitialized = IsInitialized,
                CurrentStep = GetCurrentStepDescription()
            };
        }
        
        private string GetCurrentStepDescription()
        {
            return _currentState switch
            {
                InitializationState.NotStarted => "未开始",
                InitializationState.SecsInitializing => "SECS初始化",
                InitializationState.ProcessInitializing => "处理状态初始化",
                InitializationState.CommunicationEnabling => "启用通信",
                InitializationState.WaitingForConnection => "等待HSMS连接",
                InitializationState.WaitingForS1F13 => "等待通信建立(S1F13)",
                InitializationState.WaitingForS1F17 => "等待在线请求(S1F17)",
                InitializationState.InitializingFunctions => "初始化功能",
                InitializationState.Completed => "初始化完成",
                InitializationState.Failed => "初始化失败",
                _ => "未知状态"
            };
        }
        
        private void OnInitializationProgress(string message, int progress)
        {
            _logger.LogDebug($"初始化进度: {progress}% - {message}");
            InitializationProgress?.Invoke(this, new InitializationProgressEventArgs
            {
                Message = message,
                Progress = progress,
                State = _currentState
            });
        }
        
        private void OnInitializationComplete(InitializationCompleteEventArgs args)
        {
            InitializationComplete?.Invoke(this, args);
        }
    }
    
    /// <summary>
    /// 初始化进度事件参数
    /// </summary>
    public class InitializationProgressEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public int Progress { get; set; }
        public InitializationManager.InitializationState State { get; set; }
    }
    
    /// <summary>
    /// 初始化完成事件参数
    /// </summary>
    public class InitializationCompleteEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 初始化状态信息
    /// </summary>
    public class InitializationStatus
    {
        public InitializationManager.InitializationState State { get; set; }
        public int Progress { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? CompleteTime { get; set; }
        public bool IsInitialized { get; set; }
        public string CurrentStep { get; set; } = string.Empty;
    }
}
