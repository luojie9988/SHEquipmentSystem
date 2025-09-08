using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 设备状态
    /// </summary>
    public enum EquipmentState
    {
        /// <summary>
        /// 未知状态
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 待机
        /// </summary>
        Standby = 1,

        /// <summary>
        /// 生产中
        /// </summary>
        Productive = 2,

        /// <summary>
        /// 工程模式
        /// </summary>
        Engineering = 3,

        /// <summary>
        /// 计划停机
        /// </summary>
        ScheduledDown = 4,

        /// <summary>
        /// 非计划停机
        /// </summary>
        UnscheduledDown = 5,

        /// <summary>
        /// 非计划维护
        /// </summary>
        NonScheduled = 6,

        /// <summary>
        /// 错误
        /// </summary>
        Error = 7
    }
}
