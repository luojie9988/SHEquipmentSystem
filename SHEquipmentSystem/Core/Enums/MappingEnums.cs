// 文件路径: src/DiceEquipmentSystem/Core/Enums/MappingEnums.cs
using System;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 触发器类型枚举
    /// </summary>
    public enum TriggerType
    {
        /// <summary>上升沿触发</summary>
        RisingEdge = 0,
        /// <summary>下降沿触发</summary>
        FallingEdge = 1,
        /// <summary>值变化触发</summary>
        ValueChange = 2,
        /// <summary>高电平触发</summary>
        HighLevel = 3,
        /// <summary>低电平触发</summary>
        LowLevel = 4
    }

    /// <summary>
    /// 报警优先级枚举
    /// </summary>
    //public enum AlarmPriority
    //{
    //    /// <summary>低优先级</summary>
    //    Low = 0,
    //    /// <summary>中等优先级</summary>
    //    Medium = 1,
    //    /// <summary>高优先级</summary>
    //    High = 2,
    //    /// <summary>紧急</summary>
    //    Critical = 3
    //}

    /// <summary>
    /// 报警分类枚举
    /// </summary>
    //public enum AlarmCategory
    //{
    //    /// <summary>系统报警</summary>
    //    System = 0,
    //    /// <summary>设备报警</summary>
    //    Equipment = 1,
    //    /// <summary>工艺报警</summary>
    //    Process = 2,
    //    /// <summary>安全报警</summary>
    //    Safety = 3,
    //    /// <summary>维护报警</summary>
    //    Maintenance = 4
    //}
}