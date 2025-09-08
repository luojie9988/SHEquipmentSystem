using System;

namespace DiceEquipmentSystem.PLC.Models
{
    /// <summary>
    /// PLC标签定义
    /// 描述PLC地址与业务数据的映射关系
    /// </summary>
    public class PlcTag
    {
        /// <summary>
        /// 标签名称（唯一标识）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// PLC地址
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public PlcDataType DataType { get; set; }

        /// <summary>
        /// 标签组（用于分类管理）
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// 标签描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 数据长度（字符串类型使用）
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 是否活动（是否参与周期性采集）
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// 最后更新的值
        /// </summary>
        public object? LastValue { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcTag(string name, string address, PlcDataType dataType,
            string group = "", string description = "", string unit = "", int length = 0)
        {
            Name = name;
            Address = address;
            DataType = dataType;
            Group = group;
            Description = description;
            Unit = unit;
            Length = length;
        }
    }

    /// <summary>
    /// PLC数据类型枚举
    /// </summary>
    public enum PlcDataType
    {
        /// <summary>布尔型</summary>
        Bool,

        /// <summary>16位整数</summary>
        Int16,

        /// <summary>32位整数</summary>
        Int32,

        /// <summary>单精度浮点数</summary>
        Float,

        /// <summary>双精度浮点数</summary>
        Double,

        /// <summary>字符串</summary>
        String
    }
}
