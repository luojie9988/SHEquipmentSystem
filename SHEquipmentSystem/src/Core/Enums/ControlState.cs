using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 设备控制状态 - SEMI E30标准
    /// </summary>
    public enum ControlState
    {
        /// <summary>
        /// 设备离线
        /// </summary>
        EquipmentOffline = 1,

        /// <summary>
        /// 尝试在线
        /// </summary>
        AttemptOnline = 2,

        /// <summary>
        /// 主机离线
        /// </summary>
        HostOffline = 3,

        /// <summary>
        /// 在线本地控制
        /// </summary>
        OnlineLocal = 4,

        /// <summary>
        /// 在线远程控制
        /// </summary>
        OnlineRemote = 5
    }
}
