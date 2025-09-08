// 文件路径: src/DiceEquipmentSystem/Core/SystemMonitor.cs
// 版本: v1.0.0
// 描述: 系统监控实现

using System;
using System.Diagnostics;
using DiceEquipmentSystem.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Core.Models
{
    /// <summary>
    /// 系统监控实现
    /// </summary>
    public class SystemMonitor : ISystemMonitor
    {
        private readonly ILogger<SystemMonitor> _logger;
        private readonly Process _process;

        public SystemMonitor(ILogger<SystemMonitor> logger)
        {
            _logger = logger;
            _process = Process.GetCurrentProcess();
        }

        public double GetCpuUsage()
        {
            // 简化实现
            return 0.0;
        }

        public (long Total, long Available, long Used) GetMemoryInfo()
        {
            var workingSet = _process.WorkingSet64;
            return (0, 0, workingSet);
        }

        public void RecordMetric(string name, double value)
        {
            _logger.LogDebug($"Metric: {name} = {value}");
        }
    }
}
