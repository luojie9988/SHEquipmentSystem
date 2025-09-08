using System;
using System.Collections.Generic;
using System.Linq;
using DiceEquipmentSystem.PLC.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.PLC.Mapping
{
    /// <summary>
    /// PLC数据映射器
    /// 负责管理PLC地址与业务数据的映射关系
    /// </summary>
    public class PlcDataMapper
    {
        #region 私有字段

        private readonly ILogger<PlcDataMapper> _logger;
        private readonly Dictionary<string, PlcTag> _tagsByName;
        private readonly Dictionary<string, PlcTag> _tagsByAddress;
        private readonly List<PlcTag> _allTags;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcDataMapper(ILogger<PlcDataMapper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tagsByName = new Dictionary<string, PlcTag>();
            _tagsByAddress = new Dictionary<string, PlcTag>();
            _allTags = new List<PlcTag>();

            InitializeDefaultMappings();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 根据名称获取标签
        /// </summary>
        public PlcTag? GetTagByName(string name)
        {
            return _tagsByName.TryGetValue(name, out var tag) ? tag : null;
        }

        /// <summary>
        /// 根据地址获取标签
        /// </summary>
        public PlcTag? GetTagByAddress(string address)
        {
            return _tagsByAddress.TryGetValue(address, out var tag) ? tag : null;
        }

        /// <summary>
        /// 获取所有活动标签
        /// </summary>
        public List<PlcTag> GetAllActiveTags()
        {
            return _allTags.Where(t => t.IsActive).ToList();
        }

        /// <summary>
        /// 获取指定组的标签
        /// </summary>
        public List<PlcTag> GetTagsByGroup(string group)
        {
            return _allTags.Where(t => t.Group == group && t.IsActive).ToList();
        }

        /// <summary>
        /// 添加标签映射
        /// </summary>
        public void AddTag(PlcTag tag)
        {
            if (_tagsByName.ContainsKey(tag.Name))
            {
                _logger.LogWarning($"标签名称已存在: {tag.Name}");
                return;
            }

            _tagsByName[tag.Name] = tag;
            _tagsByAddress[tag.Address] = tag;
            _allTags.Add(tag);

            _logger.LogDebug($"添加标签映射: {tag.Name} -> {tag.Address}");
        }

        /// <summary>
        /// 批量添加标签
        /// </summary>
        public void AddTags(IEnumerable<PlcTag> tags)
        {
            foreach (var tag in tags)
            {
                AddTag(tag);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化默认映射
        /// 根据划裂片设备的数据结构定义PLC地址映射
        /// </summary>
        private void InitializeDefaultMappings()
        {
            // 坐标数据映射
            var coordinateTags = new[]
            {
                new PlcTag("CurrentX", "D100", PlcDataType.Float, "坐标", "当前X坐标", "mm"),
                new PlcTag("CurrentY", "D102", PlcDataType.Float, "坐标", "当前Y坐标", "mm"),
                new PlcTag("CurrentZ", "D104", PlcDataType.Float, "坐标", "当前Z坐标", "mm"),
                new PlcTag("CurrentTheta", "D106", PlcDataType.Float, "坐标", "当前θ角度", "deg"),
                new PlcTag("TargetX", "D110", PlcDataType.Float, "坐标", "目标X坐标", "mm"),
                new PlcTag("TargetY", "D112", PlcDataType.Float, "坐标", "目标Y坐标", "mm"),
                new PlcTag("TargetZ", "D114", PlcDataType.Float, "坐标", "目标Z坐标", "mm"),
                new PlcTag("TargetTheta", "D116", PlcDataType.Float, "坐标", "目标θ角度", "deg")
            };

            // 工艺参数映射
            var processTags = new[]
            {
                new PlcTag("ProcessSpeed", "D200", PlcDataType.Float, "工艺", "处理速度", "mm/s"),
                new PlcTag("ProcessPressure", "D202", PlcDataType.Float, "工艺", "处理压力", "kPa"),
                new PlcTag("ProcessTemperature", "D204", PlcDataType.Float, "工艺", "处理温度", "℃"),
                new PlcTag("SpindleSpeed", "D206", PlcDataType.Float, "工艺", "主轴转速", "rpm"),
                new PlcTag("CutDepth", "D208", PlcDataType.Float, "工艺", "切割深度", "mm"),
                new PlcTag("FeedRate", "D210", PlcDataType.Float, "工艺", "进给速率", "mm/min")
            };

            // 划刀信息映射
            var knifeTags = new[]
            {
                new PlcTag("KnifeType", "D300", PlcDataType.Int32, "刀具", "刀具类型"),
                new PlcTag("ScribeKnifeUsageCount", "D302", PlcDataType.Int32, "刀具", "划刀使用次数"),
                new PlcTag("BreakKnifeUsageCount", "D304", PlcDataType.Int32, "刀具", "裂刀使用次数"),
                new PlcTag("ScribeKnifeLifeLimit", "D306", PlcDataType.Int32, "刀具", "划刀寿命上限"),
                new PlcTag("BreakKnifeLifeLimit", "D308", PlcDataType.Int32, "刀具", "裂刀寿命上限"),
                new PlcTag("KnifeChangeRequired", "M100", PlcDataType.Bool, "刀具", "需要更换刀具")
            };

            // 材料信息映射
            var materialTags = new[]
            {
                new PlcTag("CurrentRecipeId", "D400", PlcDataType.String, "材料", "当前配方ID", length: 20),
                new PlcTag("CurrentLotId", "D420", PlcDataType.String, "材料", "当前批次ID", length: 20),
                new PlcTag("CurrentWaferId", "D440", PlcDataType.String, "材料", "当前晶圆ID", length: 20),
                new PlcTag("CurrentSlotNumber", "D460", PlcDataType.Int32, "材料", "当前槽位号"),
                new PlcTag("CassetteId", "D462", PlcDataType.String, "材料", "Cassette ID", length: 20),
                new PlcTag("MaterialType", "D482", PlcDataType.Int32, "材料", "材料类型")
            };

            // 生产统计映射
            var statisticsTags = new[]
            {
                new PlcTag("TotalProcessedCount", "D500", PlcDataType.Int32, "统计", "总处理数量"),
                new PlcTag("GoodCount", "D502", PlcDataType.Int32, "统计", "良品数量"),
                new PlcTag("NgCount", "D504", PlcDataType.Int32, "统计", "不良品数量"),
                new PlcTag("YieldRate", "D506", PlcDataType.Float, "统计", "良率", "%"),
                new PlcTag("CycleTime", "D508", PlcDataType.Float, "统计", "节拍时间", "s"),
                new PlcTag("UPH", "D510", PlcDataType.Float, "统计", "每小时产量")
            };

            // 状态标志映射
            var statusTags = new[]
            {
                new PlcTag("SystemReady", "M200", PlcDataType.Bool, "状态", "系统就绪"),
                new PlcTag("Processing", "M201", PlcDataType.Bool, "状态", "处理中"),
                new PlcTag("AlarmActive", "M202", PlcDataType.Bool, "状态", "报警激活"),
                new PlcTag("DoorOpen", "M203", PlcDataType.Bool, "状态", "门开启"),
                new PlcTag("EMOActive", "M204", PlcDataType.Bool, "状态", "急停激活"),
                new PlcTag("AutoMode", "M205", PlcDataType.Bool, "状态", "自动模式"),
                new PlcTag("ManualMode", "M206", PlcDataType.Bool, "状态", "手动模式"),
                new PlcTag("MaintenanceMode", "M207", PlcDataType.Bool, "状态", "维护模式")
            };

            // 控制命令映射
            var controlTags = new[]
            {
                new PlcTag("StartCommand", "M300", PlcDataType.Bool, "控制", "启动命令"),
                new PlcTag("StopCommand", "M301", PlcDataType.Bool, "控制", "停止命令"),
                new PlcTag("PauseCommand", "M302", PlcDataType.Bool, "控制", "暂停命令"),
                new PlcTag("ResumeCommand", "M303", PlcDataType.Bool, "控制", "恢复命令"),
                new PlcTag("ResetCommand", "M304", PlcDataType.Bool, "控制", "复位命令"),
                new PlcTag("RecipeSelectCommand", "D600", PlcDataType.String, "控制", "配方选择命令", length: 20),
                new PlcTag("SlotMapCommand", "M305", PlcDataType.Bool, "控制", "槽位映射命令"),
                new PlcTag("CassetteStartCommand", "M306", PlcDataType.Bool, "控制", "Cassette启动命令")
            };

            // 报警代码映射
            var alarmTags = new[]
            {
                new PlcTag("AlarmCode1", "D700", PlcDataType.Int32, "报警", "报警代码1"),
                new PlcTag("AlarmCode2", "D702", PlcDataType.Int32, "报警", "报警代码2"),
                new PlcTag("AlarmCode3", "D704", PlcDataType.Int32, "报警", "报警代码3"),
                new PlcTag("AlarmCode4", "D706", PlcDataType.Int32, "报警", "报警代码4"),
                new PlcTag("AlarmCode5", "D708", PlcDataType.Int32, "报警", "报警代码5")
            };

            // 添加所有标签
            AddTags(coordinateTags);
            AddTags(processTags);
            AddTags(knifeTags);
            AddTags(materialTags);
            AddTags(statisticsTags);
            AddTags(statusTags);
            AddTags(controlTags);
            AddTags(alarmTags);

            _logger.LogInformation($"已初始化{_allTags.Count}个PLC标签映射");
        }

        #endregion
    }
}
