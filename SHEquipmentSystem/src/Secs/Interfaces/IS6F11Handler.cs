// 文件路径: src/DiceEquipmentSystem/Secs/Interfaces/IS6F11Handler.cs
// 版本: v1.0.0
// 描述: S6F11处理器接口定义

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// S6F11处理器接口
    /// 定义事件报告发送的标准接口
    /// </summary>
    public interface IS6F11Handler
    {
        /// <summary>
        /// 发送事件报告
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="eventName">事件名称</param>
        /// <param name="additionalData">附加数据（VID -> Value）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        Task SendEventReportAsync(
            uint ceid,
            string eventName,
            Dictionary<uint, object>? additionalData = null,
            CancellationToken cancellationToken = default);
    }
}
