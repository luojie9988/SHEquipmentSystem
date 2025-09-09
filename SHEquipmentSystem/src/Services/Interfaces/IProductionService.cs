using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Core.StateMachine;

namespace DiceEquipmentSystem.Services.Interfaces;

/// <summary>
/// 生产服务接口，管理生产流程状态机
/// </summary>
public interface IProductionService
{
    /// <summary>
    /// 获取生产状态机实例
    /// </summary>
    ProductionStateMachine StateMachine { get; }
    
    /// <summary>
    /// 获取当前生产状态
    /// </summary>
    EnhancedSimulationState GetCurrentState();
    
    /// <summary>
    /// 启动生产
    /// </summary>
    Task<bool> StartProductionAsync();
    
    /// <summary>
    /// 停止生产
    /// </summary>
    Task<bool> StopProductionAsync();
    
    /// <summary>
    /// 暂停生产
    /// </summary>
    Task<bool> PauseProductionAsync();
    
    /// <summary>
    /// 恢复生产
    /// </summary>
    Task<bool> ResumeProductionAsync();
    
    /// <summary>
    /// 执行远程命令
    /// </summary>
    Task<bool> ExecuteRemoteCommandAsync(string command, Dictionary<string, object>? parameters = null);
    
    /// <summary>
    /// 获取生产统计信息
    /// </summary>
    ProductionStatistics GetStatistics();
    
    /// <summary>
    /// 获取当前配方信息
    /// </summary>
    RecipeInfo? GetCurrentRecipe();
    
    /// <summary>
    /// 切换配方
    /// </summary>
    Task<bool> SwitchRecipeAsync(string recipeId);
    
    /// <summary>
    /// 获取生产历史记录
    /// </summary>
    List<ProductionRecord> GetProductionHistory(DateTime? startTime = null, DateTime? endTime = null);
}

/// <summary>
/// 生产统计信息
/// </summary>
public class ProductionStatistics
{
    public int TotalCassettes { get; set; }
    public int TotalFrames { get; set; }
    public int GoodFrames { get; set; }
    public int BadFrames { get; set; }
    public double YieldRate { get; set; }
    public double UPH { get; set; } // Units Per Hour
    public double OEE { get; set; } // Overall Equipment Effectiveness
    public TimeSpan TotalRunTime { get; set; }
    public TimeSpan TotalIdleTime { get; set; }
    public double Utilization { get; set; }
}

/// <summary>
/// 配方信息
/// </summary>
public class RecipeInfo
{
    public string RecipeId { get; set; } = string.Empty;
    public string RecipeName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime LoadTime { get; set; }
    public int ProcessedCount { get; set; }
}

/// <summary>
/// 生产记录
/// </summary>
public class ProductionRecord
{
    public string CassetteId { get; set; } = string.Empty;
    public string RecipeId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalFrames { get; set; }
    public int GoodFrames { get; set; }
    public int BadFrames { get; set; }
    public double Yield { get; set; }
    public TimeSpan ProcessTime { get; set; }
}
