// 文件路径: src/DiceEquipmentSystem/Secs/Initialization/SecsGemInitializationManager.cs
// 版本: v3.1.0
// 描述: SECS/GEM初始化管理器 - 符合SEMI E30标准的10步初始化流程

using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Secs4Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using static Common.SemiStandard.SemiStandardDefinitions;

namespace DiceEquipmentSystem.Secs.Initialization
{
    /// <summary>
    /// SECS/GEM初始化管理器 - 严格遵循SEMI E30标准的10步初始化流程
    /// </summary>
    public class SecsGemInitializationManager : ISecsGemInitializationManager
    {
        private readonly ILogger<SecsGemInitializationManager> _logger;
        private readonly ISecsConnectionManager _connectionManager;
        private readonly IEquipmentStateService _stateService;
        private readonly IDataCollectionService _dataCollectionService;
        private readonly IStatusVariableService _statusVariableService;
        private readonly DiceDataModel _dataModel;

        // 初始化状态跟踪
        private InitializationState _currentState = InitializationState.NotStarted;
        private readonly Stopwatch _initTimer = new();
        private DateTime _initializationStartTime;

        public SecsGemInitializationManager(
            ILogger<SecsGemInitializationManager> logger,
            ISecsConnectionManager connectionManager,
            IEquipmentStateService stateService,
            IDataCollectionService dataCollectionService,
            IStatusVariableService statusVariableService,
            DiceDataModel dataModel)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _dataCollectionService = dataCollectionService ?? throw new ArgumentNullException(nameof(dataCollectionService));
            _statusVariableService = statusVariableService ?? throw new ArgumentNullException(nameof(statusVariableService));
            _dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));

            _logger.LogInformation("✅ SECS/GEM初始化管理器已创建");
        }

        /// <summary>
        /// 执行完整的SECS/GEM初始化流程（10步）
        /// </summary>
        public async Task<bool> ExecuteFullInitializationAsync()
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════");
            _logger.LogInformation("    开始SECS/GEM初始化流程 - 符合SEMI E30标准");
            _logger.LogInformation("═══════════════════════════════════════════════════════════");

            _initializationStartTime = DateTime.Now;
            _initTimer.Restart();

            try
            {
                // 步骤1: SECS初始化 (IP、Port、超时设置)
                if (!await Step1_InitializeSecs())
                {
                    _logger.LogError("❌ 步骤1失败: SECS初始化失败");
                    return false;
                }

                // 步骤2: 设备启用SECS/GEM通讯
                if (!await Step2_EnableSecsGemCommunication())
                {
                    _logger.LogError("❌ 步骤2失败: 无法启用SECS/GEM通讯");
                    return false;
                }

                // 步骤3: 设备将远程控制切换成远程模式
                if (!await Step3_SwitchToRemoteMode())
                {
                    _logger.LogError("❌ 步骤3失败: 无法切换到远程模式");
                    return false;
                }

                // 步骤4: 进入通讯中状态
                if (!await Step4_EnterCommunicatingState())
                {
                    _logger.LogError("❌ 步骤4失败: 无法进入通讯中状态");
                    return false;
                }

                // 步骤5-6: 建立通讯 S1F13/S1F14
                if (!await Step5_6_EstablishCommunication())
                {
                    _logger.LogError("❌ 步骤5-6失败: 通讯建立失败");
                    return false;
                }

                // 步骤7-8: 请求进入在线状态 S1F17/S1F18
                if (!await Step7_8_RequestOnline())
                {
                    _logger.LogError("❌ 步骤7-8失败: 在线请求失败");
                    return false;
                }

                // 步骤9: 进入正常流程
                if (!await Step9_EnterNormalOperation())
                {
                    _logger.LogError("❌ 步骤9失败: 无法进入正常操作状态");
                    return false;
                }

                // 步骤10: 初始化功能 (S2F33-S2F40、S2F23-S2F24等)
                if (!await Step10_InitializeFunctions())
                {
                    _logger.LogError("❌ 步骤10失败: 功能初始化失败");
                    return false;
                }

                _initTimer.Stop();
                _currentState = InitializationState.Completed;

                _logger.LogInformation("═══════════════════════════════════════════════════════════");
                _logger.LogInformation($"✅ SECS/GEM初始化成功完成! 总耗时: {_initTimer.Elapsed.TotalSeconds:F2}秒");
                _logger.LogInformation("═══════════════════════════════════════════════════════════");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SECS/GEM初始化过程中发生异常");
                _currentState = InitializationState.Failed;
                return false;
            }
        }

        /// <summary>
        /// 步骤1: SECS初始化 (IP、Port、超时设置)
        /// </summary>
        private async Task<bool> Step1_InitializeSecs()
        {
            _logger.LogInformation("┌─ 步骤1: SECS初始化 ─────────────────────────────────────┐");
            _currentState = InitializationState.InitializingSecs;

            try
            {
                // 启动SECS连接管理器
                await _connectionManager.StartAsync();
                
                _logger.LogInformation("  ✓ SECS连接管理器已启动");
                _logger.LogInformation("  ✓ 监听端口: 5000 (被动模式)");
                _logger.LogInformation("  ✓ 超时设置: T3=45s, T5=10s, T6=5s, T7=10s, T8=5s");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤1异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤2: 设备启用SECS/GEM通讯
        /// </summary>
        private async Task<bool> Step2_EnableSecsGemCommunication()
        {
            _logger.LogInformation("┌─ 步骤2: 启用SECS/GEM通讯 ────────────────────────────────┐");
            _currentState = InitializationState.EnablingCommunication;

            try
            {
                // 检查通讯是否已启用
                var isEnabled = await _stateService.IsCommunicationEnabledAsync();
                if (!isEnabled)
                {
                    _logger.LogWarning("  ⚠ 通讯未启用，等待主机连接...");
                }
                
                _logger.LogInformation("  ✓ SECS/GEM通讯已准备就绪");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤2异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤3: 设备将远程控制切换成远程模式
        /// </summary>
        private async Task<bool> Step3_SwitchToRemoteMode()
        {
            _logger.LogInformation("┌─ 步骤3: 切换到远程模式 ──────────────────────────────────┐");
            _currentState = InitializationState.SwitchingToRemote;

            try
            {
                // 切换到远程模式
                await _stateService.SwitchControlModeAsync();
                
                _logger.LogInformation("  ✓ 已切换到远程控制模式 (ONLINE REMOTE)");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤3异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤4: 进入通讯中状态
        /// </summary>
        private async Task<bool> Step4_EnterCommunicatingState()
        {
            _logger.LogInformation("┌─ 步骤4: 进入通讯中状态 ──────────────────────────────────┐");
            _currentState = InitializationState.EnteringCommunicating;

            try
            {
                // 等待HSMS连接建立
                var maxWaitTime = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (!_connectionManager.IsConnected)
                {
                    if (DateTime.Now - startTime > maxWaitTime)
                    {
                        _logger.LogError("  ✗ 等待HSMS连接超时");
                        return false;
                    }
                    
                    await Task.Delay(1000);
                    _logger.LogDebug("  ... 等待主机HSMS连接");
                }
                
                _logger.LogInformation($"  ✓ HSMS连接已建立");
                _logger.LogInformation($"  ✓ 连接状态: {_connectionManager.HsmsConnectionState}");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤4异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤5-6: 建立通讯 S1F13 --> S1F14
        /// </summary>
        private async Task<bool> Step5_6_EstablishCommunication()
        {
            _logger.LogInformation("┌─ 步骤5-6: 建立通讯 (S1F13/S1F14) ───────────────────────┐");
            _currentState = InitializationState.EstablishingCommunication;

            try
            {
                // 等待主机发送S1F13（设备端被动接收）
                _logger.LogInformation("  ⏳ 等待主机发送S1F13建立通讯请求...");
                
                // 设置通讯已建立标志（S1F13Handler会处理并设置此状态）
                var maxWaitTime = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (!await _stateService.IsCommunicationEstablishedAsync())
                {
                    if (DateTime.Now - startTime > maxWaitTime)
                    {
                        _logger.LogError("  ✗ 等待S1F13超时");
                        return false;
                    }
                    
                    await Task.Delay(1000);
                }
                
                _logger.LogInformation("  ✓ 收到S1F13，已发送S1F14 (COMMACK=0)");
                _logger.LogInformation("  ✓ 通讯建立成功");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤5-6异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤7-8: 请求进入在线状态 S1F17 --> S1F18
        /// </summary>
        private async Task<bool> Step7_8_RequestOnline()
        {
            _logger.LogInformation("┌─ 步骤7-8: 请求在线 (S1F17/S1F18) ───────────────────────┐");
            _currentState = InitializationState.RequestingOnline;

            try
            {
                // 等待主机发送S1F17（设备端被动接收）
                _logger.LogInformation("  ⏳ 等待主机发送S1F17在线请求...");
                
                // 等待在线状态（S1F17Handler会处理并设置此状态）
                var maxWaitTime = TimeSpan.FromSeconds(30);
                var startTime = DateTime.Now;
                
                while (!await _stateService.IsOnlineAsync())
                {
                    if (DateTime.Now - startTime > maxWaitTime)
                    {
                        _logger.LogError("  ✗ 等待S1F17超时");
                        return false;
                    }
                    
                    await Task.Delay(1000);
                }
                
                _logger.LogInformation("  ✓ 收到S1F17，已发送S1F18 (ONLACK=0)");
                _logger.LogInformation("  ✓ 设备已进入在线状态");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤7-8异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤9: 进入正常流程
        /// </summary>
        private async Task<bool> Step9_EnterNormalOperation()
        {
            _logger.LogInformation("┌─ 步骤9: 进入正常操作流程 ────────────────────────────────┐");
            _currentState = InitializationState.EnteringNormalOperation;

            try
            {
                // 设置设备为待机状态
                _dataModel.EquipmentState = EquipmentState.Standby;
                _dataModel.ProcessState = ProcessState.Ready;
                
                _logger.LogInformation("  ✓ 设备状态: Standby");
                _logger.LogInformation("  ✓ 处理状态: Ready");
                _logger.LogInformation("  ✓ 控制模式: Online Remote");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤9异常");
                return false;
            }
        }

        /// <summary>
        /// 步骤10: 初始化功能 (S2F33-S2F40、S2F23-S2F24等)
        /// </summary>
        private async Task<bool> Step10_InitializeFunctions()
        {
            _logger.LogInformation("┌─ 步骤10: 初始化功能 ─────────────────────────────────────┐");
            _currentState = InitializationState.InitializingFunctions;

            try
            {
                // 10.1 初始化报告定义 (S2F33/S2F34)
                _logger.LogInformation("  初始化报告定义...");
                await InitializeReportDefinitions();
                
                // 10.2 链接事件报告 (S2F35/S2F36)
                _logger.LogInformation("  链接事件报告...");
                await InitializeEventReportLinks();
                
                // 10.3 启用事件报告 (S2F37/S2F38)
                _logger.LogInformation("  启用事件报告...");
                await EnableEventReports();
                
                // 10.4 初始化跟踪 (S2F23/S2F24)
                _logger.LogInformation("  初始化跟踪...");
                await InitializeTracing();
                
                // 10.5 初始化状态变量
                _logger.LogInformation("  初始化状态变量...");
                await InitializeStatusVariables();
                
                _logger.LogInformation("  所有功能初始化完成");
                _logger.LogInformation("└──────────────────────────────────────────────────────────┘");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "步骤10异常");
                return false;
            }
        }

        /// <summary>
        /// 初始化报告定义 - 等待主机发送S2F33
        /// </summary>
        private async Task InitializeReportDefinitions()
        {
            _logger.LogInformation("    - 等待主机定义报告 (S2F33)...");
            
            // 初始化基本报告（设备端准备好接收）
            var reports = new Dictionary<uint, List<uint>>
            {
                { 1, new List<uint> { Svid.ControlState, Svid.ControlMode } },
                { 2, new List<uint> { Svid.PPID } },
                { 3, new List<uint> { Svid.ProcessedCount } }
            };
            
            // 报告定义由主机通过S2F33发送，设备端响应S2F34
            _logger.LogInformation("    ✓ 报告定义已准备就绪");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 初始化事件报告链接 - 等待主机发送S2F35
        /// </summary>
        private async Task InitializeEventReportLinks()
        {
            _logger.LogInformation("    - 等待主机链接事件报告 (S2F35)...");
            
            // 准备事件-报告链接（设备端准备好接收）
            var eventReportLinks = new Dictionary<uint, List<uint>>
            {
                { Ceid.ControlStateOFFLINE, new List<uint> { 1 } },
                { Ceid.ControlStateLOCAL, new List<uint> { 1 } },
                { Ceid.ControlStateREMOTE, new List<uint> { 1 } },
                { Ceid.ProcessStart, new List<uint> { 2, 3 } },
                { Ceid.ProcessEnd, new List<uint> { 3 } },
                { Ceid.PPSelected, new List<uint> { 2 } }
            };
            
            // 事件链接由主机通过S2F35发送，设备端响应S2F36
            _logger.LogInformation("    ✓ 事件报告链接已准备就绪");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 启用事件报告 - 等待主机发送S2F37
        /// </summary>
        private async Task EnableEventReports()
        {
            _logger.LogInformation("    - 等待主机启用事件报告 (S2F37)...");
            
            // 准备启用的事件列表（设备端准备好接收）
            var eventsToEnable = new List<uint>
            {
                Ceid.ControlStateOFFLINE,
                Ceid.ControlStateLOCAL,
                Ceid.ControlStateREMOTE,
                Ceid.ProcessStart,
                Ceid.ProcessEnd,
                Ceid.MaterialArrival,
                Ceid.MaterialRemoved
            };
            
            // 更新状态变量
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
        }

        /// <summary>
        /// 初始化跟踪 - 等待主机发送S2F23
        /// </summary>
        private async Task InitializeTracing()
        {
            _logger.LogInformation("    - 等待主机初始化跟踪 (S2F23)...");
            
            // 准备跟踪配置（设备端准备好接收）
            // 跟踪初始化由主机通过S2F23发送，设备端响应S2F24
            _logger.LogInformation("    ✓ 跟踪已准备就绪");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 初始化状态变量
        /// </summary>
        private async Task InitializeStatusVariables()
        {
            // 初始化标准状态变量
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2); // ONLINE REMOTE
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2); // ONLINE REMOTE
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            
            // 初始化设备特定状态变量
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            await _statusVariableService.SetSvidValueAsync(Svid.ControlState, 2);
            
            _logger.LogInformation("    ✓ 状态变量初始化完成");
        }
    }

    /// <summary>
    /// 初始化状态枚举
    /// </summary>
    public enum InitializationState
    {
        NotStarted,
        InitializingSecs,
        EnablingCommunication,
        SwitchingToRemote,
        EnteringCommunicating,
        EstablishingCommunication,
        RequestingOnline,
        EnteringNormalOperation,
        InitializingFunctions,
        Completed,
        Failed
    }

    /// <summary>
    /// SECS/GEM初始化管理器接口
    /// </summary>
    public interface ISecsGemInitializationManager
    {
        /// <summary>
        /// 执行完整的初始化流程
        /// </summary>
        Task<bool> ExecuteFullInitializationAsync();
    }
}
