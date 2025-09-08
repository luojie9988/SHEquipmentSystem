using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 控制模式
    /// </summary>
    public enum ControlMode
    {
        /// <summary>
        /// 离线模式
        /// </summary>
        Offline = 0,

        /// <summary>
        /// 本地模式
        /// </summary>
        Local = 1,

        /// <summary>
        /// 远程模式
        /// </summary>
        Remote = 2
    }
}
