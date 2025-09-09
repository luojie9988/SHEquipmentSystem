using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Timers;

namespace DiceEquipmentSystem.Core.StateMachine;

/// <summary>
/// 生产流程状态机，管理完整的生产流程自动化
/// </summary>
public class ProductionStateMachine : IDisposable
{
    private readonly ILogger<ProductionStateMachine> _logger;
    private readonly IEventReportService _eventService;
    private readonly IAlarmService _alarmService;
    private readonly EnhancedSimulationState _state;
    private readonly Random _random = new();
    private System.Timers.Timer? _stepTimer;
    private System.Timers.Timer? _frameProcessTimer;

    // 事件定义
    public event EventHandler<ProductionStepChangedEventArgs>? StepChanged;
    public event EventHandler<FrameProcessingEventArgs>? FrameStarted;
    public event EventHandler<FrameProcessingEventArgs>? FrameCompleted;
    public event EventHandler<CassetteEventArgs>? CassetteStarted;
    public event EventHandler<CassetteEventArgs>? CassetteCompleted;
    public event EventHandler<RecipeChangedEventArgs>? RecipeChanged;
    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;
    public event EventHandler<SlotMappingEventArgs>? SlotMappingCompleted;

    public ProductionStateMachine(
        ILogger<ProductionStateMachine> logger,
        IEventReportService eventService,
        IAlarmService alarmService)
    {
        _logger = logger;
        _eventService = eventService;
        _alarmService = alarmService;
        _state = new EnhancedSimulationState();
        
        InitializeTimers();
    }

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public EnhancedSimulationState State => _state;

    /// <summary>
    /// 初始化定时器
    /// </summary>
    private void InitializeTimers()
    {
        // 步骤推进定时器（1秒检查一次）
        _stepTimer = new System.Timers.Timer(1000);
        _stepTimer.Elapsed += OnStepTimerElapsed;
        
        // Frame处理定时器（模拟30秒处理时间）
        _frameProcessTimer = new System.Timers.Timer(30000);
        _frameProcessTimer.Elapsed += OnFrameProcessComplete;
        _frameProcessTimer.AutoReset = false;
    }

    /// <summary>
    /// 启动生产流程
    /// </summary>
    public async Task StartProductionAsync()
    {
        if (_state.IsRunning)
        {
            _logger.LogWarning("生产流程已在运行中");
            return;
        }

        _logger.LogInformation("启动生产流程状态机");
        _state.Reset();
        _state.IsRunning = true;
        _state.CurrentStep = ProductionStep.Idle;
        _stepTimer?.Start();
        
        await TransitionToNextStepAsync();
    }

    /// <summary>
    /// 停止生产流程
    /// </summary>
    public async Task StopProductionAsync()
    {
        _logger.LogInformation("停止生产流程状态机");
        _state.IsRunning = false;
        _stepTimer?.Stop();
        _frameProcessTimer?.Stop();
        _state.CurrentStep = ProductionStep.Idle;
        _state.StatusMessage = "生产停止";
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 状态转换逻辑
    /// </summary>
    private async Task TransitionToNextStepAsync()
    {
        if (!_state.IsRunning) return;

        var previousStep = _state.CurrentStep;
        
        switch (_state.CurrentStep)
        {
            case ProductionStep.Idle:
                // 模拟等待上料（随机3-5秒后开始）
                await Task.Delay(_random.Next(3000, 5000));
                await ChangeStepAsync(ProductionStep.LoadingMaterial);
                break;

            case ProductionStep.LoadingMaterial:
                // 模拟上料过程（2秒）
                await Task.Delay(2000);
                SimulateLoadMaterial();
                await ChangeStepAsync(ProductionStep.ReadingBarcode);
                break;

            case ProductionStep.ReadingBarcode:
                // 模拟读码（1秒）
                await Task.Delay(1000);
                SimulateBarcodeReading();
                await ChangeStepAsync(ProductionStep.SwitchingRecipe);
                break;

            case ProductionStep.SwitchingRecipe:
                // 模拟配方切换（2秒）
                await Task.Delay(2000);
                SimulateRecipeSwitch();
                await ChangeStepAsync(ProductionStep.RecipeSwitched);
                break;

            case ProductionStep.RecipeSwitched:
                // 触发配方切换完成事件
                await TriggerRecipeSwitchedEvent();
                await ChangeStepAsync(ProductionStep.ScanningSlots);
                break;

            case ProductionStep.ScanningSlots:
                // 模拟槽位扫描（3秒）
                await Task.Delay(3000);
                SimulateSlotMapping();
                await ChangeStepAsync(ProductionStep.SlotsScanComplete);
                break;

            case ProductionStep.SlotsScanComplete:
                // 触发槽位扫描完成事件
                await TriggerSlotMappingCompleteEvent();
                await ChangeStepAsync(ProductionStep.StartingCassette);
                break;

            case ProductionStep.StartingCassette:
                // 开始Cassette处理
                await StartCassetteProcessing();
                await ChangeStepAsync(ProductionStep.StartingFrame);
                break;

            case ProductionStep.StartingFrame:
                // 开始Frame处理
                await StartFrameProcessing();
                await ChangeStepAsync(ProductionStep.ProcessingFrame);
                break;

            case ProductionStep.ProcessingFrame:
                // Frame处理中（由定时器控制）
                _state.StatusMessage = $"处理Frame {_state.CurrentFrameIndex + 1}/{_state.TotalFramesInCassette}";
                break;

            case ProductionStep.EndingFrame:
                // Frame处理结束
                await EndFrameProcessing();
                await ChangeStepAsync(ProductionStep.CheckNextFrame);
                break;

            case ProductionStep.CheckNextFrame:
                // 检查是否还有Frame
                if (_state.CurrentFrameIndex < _state.TotalFramesInCassette - 1)
                {
                    _state.CurrentFrameIndex++;
                    await ChangeStepAsync(ProductionStep.StartingFrame);
                }
                else
                {
                    await ChangeStepAsync(ProductionStep.UnloadingMaterial);
                }
                break;

            case ProductionStep.UnloadingMaterial:
                // 模拟下料（3秒）
                await Task.Delay(3000);
                SimulateUnloadMaterial();
                await ChangeStepAsync(ProductionStep.UnloadComplete);
                break;

            case ProductionStep.UnloadComplete:
                // 触发下料完成事件
                await TriggerUnloadCompleteEvent();
                _state.CompleteCassette();
                
                // 返回空闲状态，准备下一轮
                await ChangeStepAsync(ProductionStep.Idle);
                break;
        }

        _logger.LogDebug($"状态转换: {previousStep} -> {_state.CurrentStep}");
    }

    /// <summary>
    /// 改变步骤
    /// </summary>
    private async Task ChangeStepAsync(ProductionStep newStep)
    {
        var oldStep = _state.CurrentStep;
        _state.CurrentStep = newStep;
        _state.UpdateStepTime();
        
        StepChanged?.Invoke(this, new ProductionStepChangedEventArgs(oldStep, newStep));
        
        // 继续状态转换
        if (_state.IsRunning && newStep != ProductionStep.ProcessingFrame)
        {
            await TransitionToNextStepAsync();
        }
    }

    /// <summary>
    /// 模拟上料
    /// </summary>
    private void SimulateLoadMaterial()
    {
        var cassetteId = $"CST{DateTime.Now:yyyyMMddHHmmss}";
        _state.StartNewCassette(cassetteId);
        _logger.LogInformation($"上料完成，Cassette ID: {cassetteId}");
    }

    /// <summary>
    /// 模拟条码读取
    /// </summary>
    private void SimulateBarcodeReading()
    {
        _state.CurrentBarcode = $"WFR{DateTime.Now:yyyyMMddHHmmss}";
        _state.CurrentWaferId = _state.CurrentBarcode;
        _state.ScannedBarcodes.Add(_state.CurrentBarcode);
        
        BarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(_state.CurrentBarcode));
        _logger.LogInformation($"条码读取: {_state.CurrentBarcode}");
    }

    /// <summary>
    /// 模拟配方切换
    /// </summary>
    private void SimulateRecipeSwitch()
    {
        var recipes = new[] { "Recipe_A", "Recipe_B", "Recipe_C" };
        _state.CurrentRecipeId = $"RCP_{_random.Next(1, 4):D3}";
        _state.CurrentRecipeName = recipes[_random.Next(recipes.Length)];
        
        // 设置配方参数
        _state.RecipeParameters["Temperature"] = 25.0 + _random.NextDouble() * 5;
        _state.RecipeParameters["Pressure"] = 1.0 + _random.NextDouble() * 0.2;
        _state.RecipeParameters["Speed"] = 100 + _random.Next(50);
        _state.RecipeParameters["Time"] = 30;
        
        RecipeChanged?.Invoke(this, new RecipeChangedEventArgs(
            _state.CurrentRecipeId, 
            _state.CurrentRecipeName,
            _state.RecipeParameters));
            
        _logger.LogInformation($"配方切换: {_state.CurrentRecipeName}");
    }

    /// <summary>
    /// 模拟槽位映射
    /// </summary>
    private void SimulateSlotMapping()
    {
        // 随机生成槽位占用情况（80-100%占用率）
        var occupiedCount = _random.Next(20, 26);
        for (int i = 0; i < 25; i++)
        {
            _state.SlotMapping[i] = i < occupiedCount;
        }
        
        // 打乱顺序
        for (int i = 0; i < 25; i++)
        {
            var j = _random.Next(25);
            (_state.SlotMapping[i], _state.SlotMapping[j]) = (_state.SlotMapping[j], _state.SlotMapping[i]);
        }
        
        _state.OccupiedSlotCount = _state.SlotMapping.Count(s => s);
        _state.TotalFramesInCassette = _state.OccupiedSlotCount;
        
        SlotMappingCompleted?.Invoke(this, new SlotMappingEventArgs(_state.SlotMapping));
        _logger.LogInformation($"槽位扫描完成，占用槽位: {_state.OccupiedSlotCount}/25");
    }

    /// <summary>
    /// 开始Cassette处理
    /// </summary>
    private async Task StartCassetteProcessing()
    {
        _state.ProcessState = ProcessState.Executing;
        CassetteStarted?.Invoke(this, new CassetteEventArgs(_state.CurrentCassetteId!));
        
        await _eventService.TriggerEventAsync(300101, "CassetteStart");
        
        _logger.LogInformation($"开始处理Cassette: {_state.CurrentCassetteId}");
    }

    /// <summary>
    /// 开始Frame处理
    /// </summary>
    private async Task StartFrameProcessing()
    {
        _state.StartFrame(_state.CurrentFrameIndex);
        
        FrameStarted?.Invoke(this, new FrameProcessingEventArgs(
            _state.CurrentFrameId!,
            _state.CurrentFrameIndex));
            
        await _eventService.TriggerEventAsync(300102, "FrameStart");
        
        // 启动Frame处理定时器
        _frameProcessTimer?.Start();
        
        _logger.LogInformation($"开始处理Frame {_state.CurrentFrameIndex + 1}/{_state.TotalFramesInCassette}");
    }

    /// <summary>
    /// 结束Frame处理
    /// </summary>
    private async Task EndFrameProcessing()
    {
        // 模拟良率（95%良品率）
        var isGood = _random.NextDouble() > 0.05;
        _state.EndFrame(isGood);
        
        FrameCompleted?.Invoke(this, new FrameProcessingEventArgs(
            _state.CurrentFrameId!,
            _state.CurrentFrameIndex));
            
        await _eventService.TriggerEventAsync(300103, "FrameEnd");
        
        _logger.LogInformation($"Frame {_state.CurrentFrameIndex + 1} 处理完成，结果: {(isGood ? "PASS" : "FAIL")}");
    }

    /// <summary>
    /// 模拟下料
    /// </summary>
    private void SimulateUnloadMaterial()
    {
        _state.ProcessState = ProcessState.Idle;
        _state.StatusMessage = "下料完成";
        _logger.LogInformation($"下料完成，Cassette ID: {_state.CurrentCassetteId}");
    }

    /// <summary>
    /// 触发配方切换完成事件
    /// </summary>
    private async Task TriggerRecipeSwitchedEvent()
    {
        await _eventService.TriggerEventAsync(300201, "RecipeSwitched");
    }

    /// <summary>
    /// 触发槽位扫描完成事件
    /// </summary>
    private async Task TriggerSlotMappingCompleteEvent()
    {
        await _eventService.TriggerEventAsync(300202, "SlotMappingComplete");
    }

    /// <summary>
    /// 触发下料完成事件
    /// </summary>
    private async Task TriggerUnloadCompleteEvent()
    {
        CassetteCompleted?.Invoke(this, new CassetteEventArgs(_state.CurrentCassetteId!));
        
        await _eventService.TriggerEventAsync(300104, "UnloadComplete");
    }

    /// <summary>
    /// 步骤定时器事件
    /// </summary>
    private void OnStepTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _state.UpdateStepTime();
    }

    /// <summary>
    /// Frame处理完成事件
    /// </summary>
    private async void OnFrameProcessComplete(object? sender, ElapsedEventArgs e)
    {
        if (_state.CurrentStep == ProductionStep.ProcessingFrame)
        {
            await ChangeStepAsync(ProductionStep.EndingFrame);
        }
    }

    /// <summary>
    /// 手动触发远程命令（用于测试）
    /// </summary>
    public async Task<bool> ExecuteRemoteCommandAsync(string command, Dictionary<string, object>? parameters = null)
    {
        _logger.LogInformation($"执行远程命令: {command}");
        
        switch (command.ToUpper())
        {
            case "PP-SELECT":
                if (parameters?.ContainsKey("RecipeId") == true)
                {
                    _state.CurrentRecipeId = parameters["RecipeId"].ToString();
                    SimulateRecipeSwitch();
                    return true;
                }
                break;
                
            case "SCANSLOTMAPPING":
                SimulateSlotMapping();
                await TriggerSlotMappingCompleteEvent();
                return true;
                
            case "CASSETTESTART":
                if (_state.CurrentStep == ProductionStep.SlotsScanComplete ||
                    _state.CurrentStep == ProductionStep.StartingCassette)
                {
                    await StartCassetteProcessing();
                    return true;
                }
                break;
                
            case "FRAMESTART":
                if (_state.CurrentStep == ProductionStep.CheckNextFrame ||
                    _state.CurrentStep == ProductionStep.StartingFrame)
                {
                    await StartFrameProcessing();
                    return true;
                }
                break;
        }
        
        return false;
    }

    public void Dispose()
    {
        _stepTimer?.Stop();
        _stepTimer?.Dispose();
        _frameProcessTimer?.Stop();
        _frameProcessTimer?.Dispose();
    }
}

// 事件参数类
public class ProductionStepChangedEventArgs : EventArgs
{
    public ProductionStep OldStep { get; }
    public ProductionStep NewStep { get; }
    
    public ProductionStepChangedEventArgs(ProductionStep oldStep, ProductionStep newStep)
    {
        OldStep = oldStep;
        NewStep = newStep;
    }
}

public class FrameProcessingEventArgs : EventArgs
{
    public string FrameId { get; }
    public int FrameIndex { get; }
    
    public FrameProcessingEventArgs(string frameId, int frameIndex)
    {
        FrameId = frameId;
        FrameIndex = frameIndex;
    }
}

public class CassetteEventArgs : EventArgs
{
    public string CassetteId { get; }
    
    public CassetteEventArgs(string cassetteId)
    {
        CassetteId = cassetteId;
    }
}

public class RecipeChangedEventArgs : EventArgs
{
    public string RecipeId { get; }
    public string RecipeName { get; }
    public Dictionary<string, object> Parameters { get; }
    
    public RecipeChangedEventArgs(string recipeId, string recipeName, Dictionary<string, object> parameters)
    {
        RecipeId = recipeId;
        RecipeName = recipeName;
        Parameters = parameters;
    }
}

public class BarcodeScannedEventArgs : EventArgs
{
    public string Barcode { get; }
    
    public BarcodeScannedEventArgs(string barcode)
    {
        Barcode = barcode;
    }
}

public class SlotMappingEventArgs : EventArgs
{
    public bool[] SlotMapping { get; }
    
    public SlotMappingEventArgs(bool[] slotMapping)
    {
        SlotMapping = slotMapping;
    }
}
