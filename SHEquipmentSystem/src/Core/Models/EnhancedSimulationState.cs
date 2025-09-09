using DiceEquipmentSystem.Core.Enums;

namespace DiceEquipmentSystem.Core.Models;

/// <summary>
/// 增强的生产模拟状态，包含完整的生产流程状态信息
/// </summary>
public class EnhancedSimulationState
{
    // 基础状态信息
    public bool IsRunning { get; set; }
    public ProductionStep CurrentStep { get; set; } = ProductionStep.Idle;
    public DateTime LastUpdateTime { get; set; } = DateTime.Now;
    public TimeSpan StepElapsedTime { get; set; } = TimeSpan.Zero;

    // Cassette处理信息
    public string? CurrentCassetteId { get; set; }
    public int TotalFramesInCassette { get; set; } = 25; // 每个Cassette包含25片Frame
    public int CurrentFrameIndex { get; set; } = 0;
    public int ProcessedFrameCount { get; set; } = 0;

    // Frame处理信息
    public string? CurrentFrameId { get; set; }
    public DateTime? FrameStartTime { get; set; }
    public DateTime? FrameEndTime { get; set; }
    public TimeSpan FrameProcessingTime { get; set; } = TimeSpan.FromSeconds(30); // 标准处理时间30秒/片

    // 配方信息
    public string? CurrentRecipeId { get; set; }
    public string? CurrentRecipeName { get; set; }
    public Dictionary<string, object> RecipeParameters { get; set; } = new();

    // 条码信息
    public string? CurrentWaferId { get; set; }
    public string? CurrentBarcode { get; set; }
    public List<string> ScannedBarcodes { get; set; } = new();

    // 槽位映射信息
    public bool[] SlotMapping { get; set; } = new bool[25]; // 25个槽位的占用状态
    public int OccupiedSlotCount { get; set; }

    // 生产统计
    public int TotalCassettesProcessed { get; set; }
    public int TotalFramesProcessed { get; set; }
    public int TotalGoodFrames { get; set; }
    public int TotalBadFrames { get; set; }
    public double YieldRate => TotalFramesProcessed > 0 
        ? (double)TotalGoodFrames / TotalFramesProcessed * 100 : 0;

    // 设备状态
    public ProcessState ProcessState { get; set; } = ProcessState.Idle;
    public string StatusMessage { get; set; } = "设备空闲";

    // 时间统计
    public DateTime? ProductionStartTime { get; set; }
    public DateTime? ProductionEndTime { get; set; }
    public TimeSpan TotalProductionTime { get; set; }
    public TimeSpan TotalIdleTime { get; set; }
    public double Utilization => TotalProductionTime.TotalSeconds > 0
        ? (TotalProductionTime.TotalSeconds - TotalIdleTime.TotalSeconds) / TotalProductionTime.TotalSeconds * 100 : 0;

    // 报警信息
    public bool HasAlarm { get; set; }
    public string? CurrentAlarmId { get; set; }
    public string? CurrentAlarmText { get; set; }

    /// <summary>
    /// 重置为初始状态
    /// </summary>
    public void Reset()
    {
        IsRunning = false;
        CurrentStep = ProductionStep.Idle;
        CurrentCassetteId = null;
        CurrentFrameIndex = 0;
        ProcessedFrameCount = 0;
        CurrentFrameId = null;
        FrameStartTime = null;
        FrameEndTime = null;
        CurrentRecipeId = null;
        CurrentRecipeName = null;
        RecipeParameters.Clear();
        CurrentWaferId = null;
        CurrentBarcode = null;
        ScannedBarcodes.Clear();
        SlotMapping = new bool[25];
        OccupiedSlotCount = 0;
        ProcessState = ProcessState.Idle;
        StatusMessage = "设备空闲";
        HasAlarm = false;
        CurrentAlarmId = null;
        CurrentAlarmText = null;
    }

    /// <summary>
    /// 开始新的Cassette处理
    /// </summary>
    public void StartNewCassette(string cassetteId)
    {
        CurrentCassetteId = cassetteId;
        CurrentFrameIndex = 0;
        ProcessedFrameCount = 0;
        ScannedBarcodes.Clear();
        ProductionStartTime = DateTime.Now;
    }

    /// <summary>
    /// 开始Frame处理
    /// </summary>
    public void StartFrame(int frameIndex)
    {
        CurrentFrameIndex = frameIndex;
        CurrentFrameId = $"{CurrentCassetteId}_Frame{frameIndex:D2}";
        FrameStartTime = DateTime.Now;
        StatusMessage = $"正在处理Frame {frameIndex + 1}/{TotalFramesInCassette}";
    }

    /// <summary>
    /// 结束Frame处理
    /// </summary>
    public void EndFrame(bool isGood = true)
    {
        FrameEndTime = DateTime.Now;
        ProcessedFrameCount++;
        TotalFramesProcessed++;
        
        if (isGood)
            TotalGoodFrames++;
        else
            TotalBadFrames++;
    }

    /// <summary>
    /// 完成Cassette处理
    /// </summary>
    public void CompleteCassette()
    {
        TotalCassettesProcessed++;
        ProductionEndTime = DateTime.Now;
        if (ProductionStartTime.HasValue)
        {
            TotalProductionTime += (ProductionEndTime.Value - ProductionStartTime.Value);
        }
    }

    /// <summary>
    /// 更新步骤时间
    /// </summary>
    public void UpdateStepTime()
    {
        var now = DateTime.Now;
        StepElapsedTime = now - LastUpdateTime;
        LastUpdateTime = now;
    }
}
