namespace DiceEquipmentSystem.Core.Enums;

/// <summary>
/// 生产流程步骤枚举，对应规格书2.2节定义的14个生产步骤
/// </summary>
public enum ProductionStep
{
    /// <summary>
    /// 1. 空闲状态，等待操作员上料
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 2. 操作员上料中
    /// </summary>
    LoadingMaterial = 1,

    /// <summary>
    /// 3. 读取二维码/条码
    /// </summary>
    ReadingBarcode = 2,

    /// <summary>
    /// 4. 切换配方（PP-SELECT）
    /// </summary>
    SwitchingRecipe = 3,

    /// <summary>
    /// 5. 配方切换完成事件
    /// </summary>
    RecipeSwitched = 4,

    /// <summary>
    /// 6. Cassette槽位检测（ScanSlotMapping）
    /// </summary>
    ScanningSlots = 5,

    /// <summary>
    /// 7. 槽位检测完成事件
    /// </summary>
    SlotsScanComplete = 6,

    /// <summary>
    /// 8. 开始Cassette处理（CassetteStart）
    /// </summary>
    StartingCassette = 7,

    /// <summary>
    /// 9. Frame循环处理开始（FrameStart）
    /// </summary>
    StartingFrame = 8,

    /// <summary>
    /// 10. Frame处理中
    /// </summary>
    ProcessingFrame = 9,

    /// <summary>
    /// 11. Frame处理结束（FrameEnd）
    /// </summary>
    EndingFrame = 10,

    /// <summary>
    /// 12. 检查是否还有Frame需要处理
    /// </summary>
    CheckNextFrame = 11,

    /// <summary>
    /// 13. 下料中
    /// </summary>
    UnloadingMaterial = 12,

    /// <summary>
    /// 14. 下料完成事件
    /// </summary>
    UnloadComplete = 13
}
