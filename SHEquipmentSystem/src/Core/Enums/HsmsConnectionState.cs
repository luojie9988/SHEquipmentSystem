using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// HSMS连接状态
    /// </summary>
    public enum HsmsConnectionState
    {
        /// <summary>
        /// 未连接
        /// </summary>
        NotConnected = 0,

        /// <summary>
        /// 正在连接
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// 已连接但未选中(NotSelected)
        /// </summary>
        Connected = 2,

        /// <summary>
        /// 已选中（通信建立）
        /// </summary>
        Selected = 3,

        /// <summary>
        /// 正在断开连接
        /// </summary>
        Disconnecting = 4,

        /// <summary>
        /// 连接错误
        /// </summary>
        Error = 5,

        /// <summary>
        /// 未启用
        /// </summary>
        NotEnabled = 6,

        /// <summary>
        /// 重连
        /// </summary>
        Retry = 7
    }
}
