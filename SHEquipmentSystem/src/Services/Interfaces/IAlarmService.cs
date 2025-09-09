// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IAlarmService.cs
// 版本: v1.0.0
// 描述: 报警服务接口

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 报警服务接口
    /// </summary>
    public interface IAlarmService
    {
        /// <summary>
        /// 设置报警
        /// </summary>
        Task SetAlarmAsync(uint alid, string alarmText);

        /// <summary>
        /// 清除报警
        /// </summary>
        Task ClearAlarmAsync(uint alid);

        /// <summary>
        /// 获取所有活动报警
        /// </summary>
        Task<List<AlarmInfo>> GetActiveAlarmsAsync();

        /// <summary>
        /// 报警发生事件
        /// </summary>
        event EventHandler<AlarmEventArgs>? AlarmOccurred;

        /// <summary>
        /// 报警清除事件
        /// </summary>
        event EventHandler<AlarmEventArgs>? AlarmCleared;

        /// <summary>
        /// 获取已定义的报警数量
        /// </summary>
        /// <returns>报警数量</returns>
        int GetDefinedAlarmCount();

        /// <summary>
        /// 获取当前激活的报警数量
        /// </summary>
        /// <returns>激活的报警数量</returns>
        int GetActiveAlarmCount();

        /// <summary>
        /// 初始化默认报警
        /// </summary>
        /// <returns>异步任务</returns>
        Task InitializeDefaultAlarmsAsync();
    }

    /// <summary>
    /// 报警事件参数
    /// </summary>
    public class AlarmEventArgs : EventArgs
    {
        /// <summary>
        /// 报警ID
        /// </summary>
        public uint AlarmId { get; set; }

        /// <summary>
        /// 报警文本
        /// </summary>
        public string AlarmText { get; set; } = "";

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 报警信息
    /// </summary>
    public class AlarmInfo
    {
        public uint AlarmId { get; set; }
        public string AlarmText { get; set; } = "";
        public DateTime SetTime { get; set; }
    }
}
