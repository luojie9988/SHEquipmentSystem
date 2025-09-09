using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Services;

/// <summary>
/// 生产服务实现，管理生产流程状态机
/// </summary>
public class ProductionService : IProductionService, IDisposable
{
    private readonly ILogger<ProductionService> _logger;
    private readonly IEventReportService _eventService;
    private readonly IAlarmService _alarmService;
    private readonly ProductionStateMachine _stateMachine;
    private readonly List<ProductionRecord> _productionHistory = new();
    private RecipeInfo? _currentRecipe;
    private DateTime _serviceStartTime;
    private bool _isPaused;

    public ProductionService(
        ILogger<ProductionService> logger,
        IEventReportService eventService,
        IAlarmService alarmService)
    {
        _logger = logger;
        _eventService = eventService;
        _alarmService = alarmService;
        
        // 创建状态机
        // Use the same logger for the state machine
        ILogger<ProductionStateMachine> stateMachineLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ProductionStateMachine>.Instance;
        // var stateMachineLogger = logger.CreateLogger<ProductionStateMachine>();
        _stateMachine = new ProductionStateMachine(stateMachineLogger, eventService, alarmService);
        
        // 订阅状态机事件
        SubscribeToStateMachineEvents();
        
        _serviceStartTime = DateTime.Now;
        
        _logger.LogInformation("生产服务已初始化");
    }

    /// <summary>
    /// 获取生产状态机实例
    /// </summary>
    public ProductionStateMachine StateMachine => _stateMachine;

    /// <summary>
    /// 订阅状态机事件
    /// </summary>
    private void SubscribeToStateMachineEvents()
    {
        _stateMachine.StepChanged += OnStepChanged;
        _stateMachine.FrameStarted += OnFrameStarted;
        _stateMachine.FrameCompleted += OnFrameCompleted;
        _stateMachine.CassetteStarted += OnCassetteStarted;
        _stateMachine.CassetteCompleted += OnCassetteCompleted;
        _stateMachine.RecipeChanged += OnRecipeChanged;
        _stateMachine.BarcodeScanned += OnBarcodeScanned;
        _stateMachine.SlotMappingCompleted += OnSlotMappingCompleted;
    }

    /// <summary>
    /// 获取当前生产状态
    /// </summary>
    public EnhancedSimulationState GetCurrentState()
    {
        return _stateMachine.State;
    }

    /// <summary>
    /// 启动生产
    /// </summary>
    public async Task<bool> StartProductionAsync()
    {
        try
        {
            _logger.LogInformation("启动生产流程");
            _isPaused = false;
            await _stateMachine.StartProductionAsync();
            
            // 触发生产开始事件
            await _eventService.TriggerEventAsync(300001, "PRODUCTION_START");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动生产失败");
            return false;
        }
    }

    /// <summary>
    /// 停止生产
    /// </summary>
    public async Task<bool> StopProductionAsync()
    {
        try
        {
            _logger.LogInformation("停止生产流程");
            await _stateMachine.StopProductionAsync();
            
            // 触发生产停止事件
            await _eventService.TriggerEventAsync(300002, "PRODUCTION_STOP");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止生产失败");
            return false;
        }
    }

    /// <summary>
    /// 暂停生产
    /// </summary>
    public async Task<bool> PauseProductionAsync()
    {
        try
        {
            if (_isPaused)
            {
                _logger.LogWarning("生产已处于暂停状态");
                return false;
            }
            
            _logger.LogInformation("暂停生产流程");
            _isPaused = true;
            
            // 这里可以添加暂停逻辑
            await _eventService.TriggerEventAsync(300003, "PRODUCTION_PAUSE");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "暂停生产失败");
            return false;
        }
    }

    /// <summary>
    /// 恢复生产
    /// </summary>
    public async Task<bool> ResumeProductionAsync()
    {
        try
        {
            if (!_isPaused)
            {
                _logger.LogWarning("生产未处于暂停状态");
                return false;
            }
            
            _logger.LogInformation("恢复生产流程");
            _isPaused = false;
            
            // 这里可以添加恢复逻辑
            await _eventService.TriggerEventAsync(300004, "PRODUCTION_RESUME");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复生产失败");
            return false;
        }
    }

    /// <summary>
    /// 执行远程命令
    /// </summary>
    public async Task<bool> ExecuteRemoteCommandAsync(string command, Dictionary<string, object>? parameters = null)
    {
        try
        {
            _logger.LogInformation($"执行远程命令: {command}");
            return await _stateMachine.ExecuteRemoteCommandAsync(command, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"执行远程命令失败: {command}");
            return false;
        }
    }

    /// <summary>
    /// 获取生产统计信息
    /// </summary>
    public ProductionStatistics GetStatistics()
    {
        var state = _stateMachine.State;
        var runTime = DateTime.Now - _serviceStartTime;
        
        // 计算UPH (Units Per Hour)
        double uph = 0;
        if (runTime.TotalHours > 0)
        {
            uph = state.TotalFramesProcessed / runTime.TotalHours;
        }
        
        // 计算OEE (Overall Equipment Effectiveness)
        // OEE = Availability × Performance × Quality
        double availability = state.Utilization / 100.0;
        double performance = Math.Min(uph / 120.0, 1.0); // 假设标准UPH为120
        double quality = state.YieldRate / 100.0;
        double oee = availability * performance * quality * 100;
        
        return new ProductionStatistics
        {
            TotalCassettes = state.TotalCassettesProcessed,
            TotalFrames = state.TotalFramesProcessed,
            GoodFrames = state.TotalGoodFrames,
            BadFrames = state.TotalBadFrames,
            YieldRate = state.YieldRate,
            UPH = uph,
            OEE = oee,
            TotalRunTime = runTime,
            TotalIdleTime = state.TotalIdleTime,
            Utilization = state.Utilization
        };
    }

    /// <summary>
    /// 获取当前配方信息
    /// </summary>
    public RecipeInfo? GetCurrentRecipe()
    {
        return _currentRecipe;
    }

    /// <summary>
    /// 切换配方
    /// </summary>
    public async Task<bool> SwitchRecipeAsync(string recipeId)
    {
        try
        {
            _logger.LogInformation($"切换配方: {recipeId}");
            
            var parameters = new Dictionary<string, object>
            {
                ["RecipeId"] = recipeId
            };
            
            return await _stateMachine.ExecuteRemoteCommandAsync("PP-SELECT", parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"切换配方失败: {recipeId}");
            return false;
        }
    }

    /// <summary>
    /// 获取生产历史记录
    /// </summary>
    public List<ProductionRecord> GetProductionHistory(DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = _productionHistory.AsEnumerable();
        
        if (startTime.HasValue)
            query = query.Where(r => r.StartTime >= startTime.Value);
            
        if (endTime.HasValue)
            query = query.Where(r => r.EndTime <= endTime.Value);
            
        return query.OrderByDescending(r => r.StartTime).ToList();
    }

    // 事件处理方法
    private void OnStepChanged(object? sender, ProductionStepChangedEventArgs e)
    {
        _logger.LogDebug($"生产步骤变更: {e.OldStep} -> {e.NewStep}");
    }

    private void OnFrameStarted(object? sender, FrameProcessingEventArgs e)
    {
        _logger.LogInformation($"Frame开始处理: {e.FrameId} (索引: {e.FrameIndex})");
    }

    private void OnFrameCompleted(object? sender, FrameProcessingEventArgs e)
    {
        _logger.LogInformation($"Frame处理完成: {e.FrameId}");
    }

    private void OnCassetteStarted(object? sender, CassetteEventArgs e)
    {
        _logger.LogInformation($"Cassette开始处理: {e.CassetteId}");
    }

    private void OnCassetteCompleted(object? sender, CassetteEventArgs e)
    {
        _logger.LogInformation($"Cassette处理完成: {e.CassetteId}");
        
        // 添加到历史记录
        var state = _stateMachine.State;
        var record = new ProductionRecord
        {
            CassetteId = e.CassetteId,
            RecipeId = state.CurrentRecipeId ?? "",
            StartTime = state.ProductionStartTime ?? DateTime.Now,
            EndTime = state.ProductionEndTime ?? DateTime.Now,
            TotalFrames = state.ProcessedFrameCount,
            GoodFrames = state.TotalGoodFrames,
            BadFrames = state.TotalBadFrames,
            Yield = state.YieldRate,
            ProcessTime = state.TotalProductionTime
        };
        
        _productionHistory.Add(record);
        
        // 保留最近100条记录
        if (_productionHistory.Count > 100)
        {
            _productionHistory.RemoveAt(0);
        }
    }

    private void OnRecipeChanged(object? sender, RecipeChangedEventArgs e)
    {
        _logger.LogInformation($"配方已切换: {e.RecipeName} (ID: {e.RecipeId})");
        
        _currentRecipe = new RecipeInfo
        {
            RecipeId = e.RecipeId,
            RecipeName = e.RecipeName,
            Parameters = e.Parameters,
            LoadTime = DateTime.Now,
            ProcessedCount = 0
        };
    }

    private void OnBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        _logger.LogInformation($"条码扫描: {e.Barcode}");
    }

    private void OnSlotMappingCompleted(object? sender, SlotMappingEventArgs e)
    {
        var occupiedCount = e.SlotMapping.Count(s => s);
        _logger.LogInformation($"槽位映射完成，占用槽位: {occupiedCount}/25");
    }

    public void Dispose()
    {
        _stateMachine?.Dispose();
    }
}
