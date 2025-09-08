using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// S5F1处理器接口
    /// </summary>
    public interface IS5F1Handler
    {
        /// <summary>
        /// 发送报警报告
        /// </summary>
        Task SendAlarmReportAsync(uint alarmId, byte alarmCode, string alarmText,
            Dictionary<string, object>? additionalInfo = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 启用报警
        /// </summary>
        void EnableAlarms(IEnumerable<uint> alarmIds);

        /// <summary>
        /// 禁用报警
        /// </summary>
        void DisableAlarms(IEnumerable<uint> alarmIds);

        /// <summary>
        /// 获取所有启用的报警
        /// </summary>
        IEnumerable<uint> GetEnabledAlarms();

        /// <summary>
        /// 检查报警是否启用
        /// </summary>
        bool IsAlarmEnabled(uint alarmId);
    }
}
