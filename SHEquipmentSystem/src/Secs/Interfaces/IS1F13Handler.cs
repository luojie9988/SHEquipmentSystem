using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// S1F13处理器接口
    /// </summary>
    public interface IS1F13Handler
    {
        /// <summary>
        /// 通信建立事件
        /// </summary>
        event EventHandler<EventArgs>? CommunicationEstablished;
        /// <summary>
        /// 发送建立通信请求
        /// </summary>
        Task<bool> SendEstablishCommunicationsRequestAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始通信建立循环
        /// </summary>
        Task StartCommunicationEstablishmentLoopAsync(CancellationToken cancellationToken = default);
    }

}
