// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IEqpEventManager.cs
// 版本: v1.0.0
// 描述: 设备端事件管理器接口

using System;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 设备端事件管理器接口
    /// 负责管理和发送SECS/GEM事件报告
    /// </summary>
    /// <remarks>
    /// 基于SEMI E30标准的事件管理功能：
    /// 1. 事件报告发送（S6F11）
    /// 2. 事件启用/禁用管理
    /// 3. 报告配置管理
    /// 4. 事件数据收集和格式化
    /// </remarks>
    public interface IEqpEventManager
    {
        /// <summary>
        /// 报告事件到主机
        /// </summary>
        /// <param name="ceid">收集事件ID</param>
        /// <param name="eventData">事件相关数据</param>
        /// <returns>事件发送任务</returns>
        Task ReportEventAsync(uint ceid, params object[] eventData);

        /// <summary>
        /// 启用或禁用事件
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>操作结果</returns>
        Task<bool> SetEventEnabledAsync(uint ceid, bool enabled);

        /// <summary>
        /// 检查事件是否已启用
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <returns>是否启用</returns>
        bool IsEventEnabled(uint ceid);
    }
}
