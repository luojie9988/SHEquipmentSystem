// 文件路径: src/DiceEquipmentSystem/Core/Interfaces/ISystemMonitor.cs
// 版本: v1.0.0
// 描述: 系统监控接口

namespace DiceEquipmentSystem.Core.Interfaces
{
    /// <summary>
    /// 系统监控接口
    /// </summary>
    public interface ISystemMonitor
    {
        /// <summary>
        /// 获取CPU使用率
        /// </summary>
        double GetCpuUsage();

        /// <summary>
        /// 获取内存使用情况
        /// </summary>
        (long Total, long Available, long Used) GetMemoryInfo();

        /// <summary>
        /// 记录性能指标
        /// </summary>
        void RecordMetric(string name, double value);
    }
}
