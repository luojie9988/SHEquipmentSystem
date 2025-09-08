using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 在线确认码
    /// </summary>
    public enum OnlineAck : byte
    {
        /// <summary>
        /// 接受
        /// </summary>
        Accepted = 0,

        /// <summary>
        /// 拒绝 - 已在线
        /// </summary>
        AlreadyOnline = 1,

        /// <summary>
        /// 拒绝 - 本地模式
        /// </summary>
        LocalMode = 2
    }
}
