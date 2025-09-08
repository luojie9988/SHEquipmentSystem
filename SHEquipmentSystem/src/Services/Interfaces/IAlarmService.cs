// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IAlarmService.cs
// 版本: v1.0.0
// 描述: 报警服务接口

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
