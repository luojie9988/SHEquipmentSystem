using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 通信确认码
    /// </summary>
    public enum CommAck : byte
    {
        /// <summary>
        /// 接受
        /// </summary>
        Accepted = 0,

        /// <summary>
        /// 拒绝
        /// </summary>
        Denied = 1
    }
}
