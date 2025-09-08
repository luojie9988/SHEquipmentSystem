// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IDataCollectionService.cs
// 版本: v1.0.0
// 描述: 数据采集服务接口 - 支持S2F23跟踪初始化功能

using DiceEquipmentSystem.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 数据采集服务接口
    /// 提供设备数据的采集、缓存和发送功能
    /// </summary>
    public interface IDataCollectionService
    {
        /// <summary>
        /// 启动数据跟踪
        /// </summary>
        /// <param name="config">跟踪配置</param>
        /// <returns>启动结果</returns>
        Task<TraceStartResult> StartTraceAsync(TraceConfiguration config);

        /// <summary>
        /// 停止数据跟踪
        /// </summary>
        /// <param name="traceId">跟踪ID</param>
        /// <returns>停止结果</returns>
        Task<TraceStopResult> StopTraceAsync(uint traceId);

        /// <summary>
        /// 获取跟踪状态
        /// </summary>
        /// <param name="traceId">跟踪ID</param>
        /// <returns>跟踪状态</returns>
        Task<TraceStatus> GetTraceStatusAsync(uint traceId);

        /// <summary>
        /// 获取所有活动的跟踪
        /// </summary>
        /// <returns>跟踪配置列表</returns>
        Task<List<TraceConfiguration>> GetActiveTracesAsync();
    }

    /// <summary>
    /// 跟踪启动结果
    /// </summary>
    public class TraceStartResult
    {
        /// <summary>
        /// 是否启动成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 跟踪任务ID
        /// </summary>
        public uint TraceId { get; set; }
    }

    /// <summary>
    /// 跟踪停止结果
    /// </summary>
    public class TraceStopResult
    {
        /// <summary>
        /// 是否停止成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 已采集的样本数
        /// </summary>
        public uint CollectedSamples { get; set; }
    }

    /// <summary>
    /// 跟踪状态
    /// </summary>
    public class TraceStatus
    {
        /// <summary>
        /// 跟踪ID
        /// </summary>
        public uint TraceId { get; set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 已采集的样本数
        /// </summary>
        public uint CollectedSamples { get; set; }

        /// <summary>
        /// 总采样数
        /// </summary>
        public uint TotalSamples { get; set; }

        /// <summary>
        /// 启动时间
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// 最后采样时间
        /// </summary>
        public DateTime? LastSampleTime { get; set; }

        /// <summary>
        /// 进度百分比
        /// </summary>
        public double Progress => TotalSamples > 0 ? (double)CollectedSamples / TotalSamples * 100 : 0;
    }
}
