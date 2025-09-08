using System;

namespace DiceEquipmentSystem.Core.Models
{
    /// <summary>
    /// 划裂片设备数据模型
    /// 包含设备的完整状态和工艺数据
    /// </summary>
    public class DiceDeviceData
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public int DeviceId { get; set; }

        #region 坐标数据

        /// <summary>
        /// 当前X坐标（mm）
        /// </summary>
        public float CurrentX { get; set; }

        /// <summary>
        /// 当前Y坐标（mm）
        /// </summary>
        public float CurrentY { get; set; }

        /// <summary>
        /// 当前Z坐标（mm）
        /// </summary>
        public float CurrentZ { get; set; }

        /// <summary>
        /// 当前θ角度（deg）
        /// </summary>
        public float CurrentTheta { get; set; }

        /// <summary>
        /// 目标X坐标（mm）
        /// </summary>
        public float TargetX { get; set; }

        /// <summary>
        /// 目标Y坐标（mm）
        /// </summary>
        public float TargetY { get; set; }

        /// <summary>
        /// 目标Z坐标（mm）
        /// </summary>
        public float TargetZ { get; set; }

        /// <summary>
        /// 目标θ角度（deg）
        /// </summary>
        public float TargetTheta { get; set; }

        #endregion

        #region 工艺数据

        /// <summary>
        /// 处理速度（mm/s）
        /// </summary>
        public float ProcessSpeed { get; set; }

        /// <summary>
        /// 处理压力（kPa）
        /// </summary>
        public float ProcessPressure { get; set; }

        /// <summary>
        /// 处理温度（℃）
        /// </summary>
        public float ProcessTemperature { get; set; }

        /// <summary>
        /// 主轴转速（rpm）
        /// </summary>
        public float SpindleSpeed { get; set; }

        /// <summary>
        /// 切割深度（mm）
        /// </summary>
        public float CutDepth { get; set; }

        /// <summary>
        /// 进给速率（mm/min）
        /// </summary>
        public float FeedRate { get; set; }

        #endregion

        #region 刀具信息

        /// <summary>
        /// 刀具类型（1:划刀 2:裂刀）
        /// </summary>
        public int KnifeType { get; set; }

        /// <summary>
        /// 划刀使用次数
        /// </summary>
        public int ScribeKnifeUsageCount { get; set; }

        /// <summary>
        /// 裂刀使用次数
        /// </summary>
        public int BreakKnifeUsageCount { get; set; }

        /// <summary>
        /// 划刀寿命上限
        /// </summary>
        public int ScribeKnifeLifeLimit { get; set; }

        /// <summary>
        /// 裂刀寿命上限
        /// </summary>
        public int BreakKnifeLifeLimit { get; set; }

        #endregion

        #region 材料信息

        /// <summary>
        /// 当前配方ID
        /// </summary>
        public string CurrentRecipeId { get; set; } = "";

        /// <summary>
        /// 当前批次ID
        /// </summary>
        public string CurrentLotId { get; set; } = "";

        /// <summary>
        /// 当前晶圆ID
        /// </summary>
        public string CurrentWaferId { get; set; } = "";

        /// <summary>
        /// 当前槽位号
        /// </summary>
        public int CurrentSlotNumber { get; set; }

        /// <summary>
        /// Cassette ID
        /// </summary>
        public string CassetteId { get; set; } = "";

        /// <summary>
        /// 材料类型
        /// </summary>
        public int MaterialType { get; set; }

        #endregion

        #region 生产统计

        /// <summary>
        /// 总处理数量
        /// </summary>
        public int TotalProcessedCount { get; set; }

        /// <summary>
        /// 良品数量
        /// </summary>
        public int GoodCount { get; set; }

        /// <summary>
        /// 不良品数量
        /// </summary>
        public int NgCount { get; set; }

        /// <summary>
        /// 良率（%）
        /// </summary>
        public float YieldRate { get; set; }

        /// <summary>
        /// 节拍时间（s）
        /// </summary>
        public float CycleTime { get; set; }

        /// <summary>
        /// 每小时产量（UPH）
        /// </summary>
        public float UPH { get; set; }

        #endregion

        #region 状态信息

        /// <summary>
        /// 系统就绪
        /// </summary>
        public bool SystemReady { get; set; }

        /// <summary>
        /// 处理中
        /// </summary>
        public bool Processing { get; set; }

        /// <summary>
        /// 报警激活
        /// </summary>
        public bool AlarmActive { get; set; }

        /// <summary>
        /// 自动模式
        /// </summary>
        public bool AutoMode { get; set; }

        /// <summary>
        /// 手动模式
        /// </summary>
        public bool ManualMode { get; set; }

        /// <summary>
        /// 维护模式
        /// </summary>
        public bool MaintenanceMode { get; set; }

        #endregion

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// 数据有效性
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// 克隆方法
        /// </summary>
        public DiceDeviceData Clone()
        {
            return (DiceDeviceData)this.MemberwiseClone();
        }
    }
}
