using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Constants
{
    /// <summary>
    /// ECID定义
    /// </summary>
    public class EcidDefinition
    {
        /// <summary>
        /// ECID标识
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// ECID名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// ECID描述
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType { get; set; } = typeof(object);

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; } = 0;

        /// <summary>
        /// 是否为只读
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// 最小值（可选）
        /// </summary>
        public object? MinValue { get; set; }

        /// <summary>
        /// 最大值（可选）
        /// </summary>
        public object? MaxValue { get; set; }

        /// <summary>
        /// PLC地址（如果来自PLC）
        /// </summary>
        public string? PlcAddress { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string? Units { get; set; }
    }
}
