using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Models
{
    /// <summary>
    /// 跟踪配置类
    /// </summary>
    public class TraceConfiguration
    {
        /// <summary>
        /// 跟踪ID
        /// </summary>
        public uint TraceId { get; set; }

        /// <summary>
        /// 采样周期
        /// </summary>
        public TimeSpan SamplingPeriod { get; set; }

        /// <summary>
        /// 总采样数
        /// </summary>
        public uint TotalSamples { get; set; }

        /// <summary>
        /// 报告组大小
        /// </summary>
        public uint ReportGroupSize { get; set; }

        /// <summary>
        /// SVID列表
        /// </summary>
        public List<uint> SvidList { get; set; } = new();

        /// <summary>
        /// 样本类型
        /// </summary>
        public string SampleType { get; set; } = "";

        /// <summary>
        /// 是否为停止命令
        /// </summary>
        public bool IsStopCommand { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;
    }
}
