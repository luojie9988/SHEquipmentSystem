using System;
using System.Collections.Generic;

namespace DiceEquipmentSystem.PLC.Models
{
    /// <summary>
    /// PLC数据块定义
    /// 用于批量读写优化
    /// </summary>
    public class PlcDataBlock
    {
        /// <summary>
        /// 数据块名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 起始地址
        /// </summary>
        public string StartAddress { get; set; }

        /// <summary>
        /// 数据长度（字数）
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 包含的标签列表
        /// </summary>
        public List<PlcTag> Tags { get; set; }

        /// <summary>
        /// 更新周期（毫秒）
        /// </summary>
        public int UpdateInterval { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcDataBlock()
        {
            Tags = new List<PlcTag>();
            UpdateInterval = 200;
            IsEnabled = true;
        }
    }

    /// <summary>
    /// 划裂片设备数据结构
    /// 映射到PLC的完整数据区域
    /// </summary>
    public class DiceDeviceDataBlock
    {
        /// <summary>
        /// 坐标数据块（D100-D199）
        /// </summary>
        public CoordinateDataBlock Coordinates { get; set; }

        /// <summary>
        /// 工艺数据块（D200-D299）
        /// </summary>
        public ProcessDataBlock Process { get; set; }

        /// <summary>
        /// 刀具数据块（D300-D399）
        /// </summary>
        public KnifeDataBlock Knife { get; set; }

        /// <summary>
        /// 材料数据块（D400-D499）
        /// </summary>
        public MaterialDataBlock Material { get; set; }

        /// <summary>
        /// 统计数据块（D500-D599）
        /// </summary>
        public StatisticsDataBlock Statistics { get; set; }

        /// <summary>
        /// 控制命令块（D600-D699）
        /// </summary>
        public ControlDataBlock Control { get; set; }

        /// <summary>
        /// 报警数据块（D700-D799）
        /// </summary>
        public AlarmDataBlock Alarms { get; set; }

        /// <summary>
        /// 状态标志块（M200-M299）
        /// </summary>
        public StatusFlagBlock StatusFlags { get; set; }

        /// <summary>
        /// 控制标志块（M300-M399）
        /// </summary>
        public ControlFlagBlock ControlFlags { get; set; }
    }

    /// <summary>
    /// 坐标数据块
    /// </summary>
    public class CoordinateDataBlock
    {
        public float CurrentX { get; set; }
        public float CurrentY { get; set; }
        public float CurrentZ { get; set; }
        public float CurrentTheta { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }
        public float TargetTheta { get; set; }
    }

    /// <summary>
    /// 工艺数据块
    /// </summary>
    public class ProcessDataBlock
    {
        public float Speed { get; set; }
        public float Pressure { get; set; }
        public float Temperature { get; set; }
        public float SpindleSpeed { get; set; }
        public float CutDepth { get; set; }
        public float FeedRate { get; set; }
    }

    /// <summary>
    /// 刀具数据块
    /// </summary>
    public class KnifeDataBlock
    {
        public int KnifeType { get; set; }
        public int ScribeUsageCount { get; set; }
        public int BreakUsageCount { get; set; }
        public int ScribeLifeLimit { get; set; }
        public int BreakLifeLimit { get; set; }
    }

    /// <summary>
    /// 材料数据块
    /// </summary>
    public class MaterialDataBlock
    {
        public string RecipeId { get; set; } = "";
        public string LotId { get; set; } = "";
        public string WaferId { get; set; } = "";
        public int SlotNumber { get; set; }
        public string CassetteId { get; set; } = "";
        public int MaterialType { get; set; }
    }

    /// <summary>
    /// 统计数据块
    /// </summary>
    public class StatisticsDataBlock
    {
        public int TotalCount { get; set; }
        public int GoodCount { get; set; }
        public int NgCount { get; set; }
        public float YieldRate { get; set; }
        public float CycleTime { get; set; }
        public float UPH { get; set; }
    }

    /// <summary>
    /// 控制数据块
    /// </summary>
    public class ControlDataBlock
    {
        public string RecipeSelect { get; set; } = "";
        public int TargetSlot { get; set; }
        public int ProcessMode { get; set; }
    }

    /// <summary>
    /// 报警数据块
    /// </summary>
    public class AlarmDataBlock
    {
        public int[] AlarmCodes { get; set; } = new int[10];
        public DateTime[] AlarmTimes { get; set; } = new DateTime[10];
    }

    /// <summary>
    /// 状态标志块
    /// </summary>
    public class StatusFlagBlock
    {
        public bool SystemReady { get; set; }
        public bool Processing { get; set; }
        public bool AlarmActive { get; set; }
        public bool DoorOpen { get; set; }
        public bool EMOActive { get; set; }
        public bool AutoMode { get; set; }
        public bool ManualMode { get; set; }
        public bool MaintenanceMode { get; set; }
    }

    /// <summary>
    /// 控制标志块
    /// </summary>
    public class ControlFlagBlock
    {
        public bool StartCommand { get; set; }
        public bool StopCommand { get; set; }
        public bool PauseCommand { get; set; }
        public bool ResumeCommand { get; set; }
        public bool ResetCommand { get; set; }
        public bool SlotMapCommand { get; set; }
        public bool CassetteStartCommand { get; set; }
    }
}
