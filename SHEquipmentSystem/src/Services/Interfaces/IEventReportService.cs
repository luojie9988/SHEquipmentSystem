// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IEventReportService.cs
// 版本: v1.0.0
// 描述: 事件报告服务接口

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 事件报告服务接口
    /// 提供事件报告定义和发送功能
    /// </summary>
    public interface IEventReportService
    {
        /// <summary>
        /// 定义事件报告
        /// </summary>
        /// <param name="reportDef">报告定义</param>
        /// <returns>定义结果</returns>
        Task<bool> DefineReportAsync(ReportDefinition reportDef);

        /// <summary>
        /// 删除报告定义
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <returns>删除结果</returns>
        Task<bool> DeleteReportDefinitionAsync(uint rptId);

        /// <summary>
        /// 发送事件报告
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="data">事件数据</param>
        /// <returns>发送结果</returns>
        Task<bool> ReportEventAsync(uint ceid, params object[] data);

        /// <summary>
        /// 发送事件报告（带名称和附加数据）
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="eventName">事件名称</param>
        /// <param name="additionalData">附加数据</param>
        /// <returns>发送结果</returns>
        Task<bool> ReportEventAsync(uint ceid, string eventName, Dictionary<uint, object>? additionalData);

        /// <summary>
        /// 获取报告定义
        /// </summary>
        /// <param name="rptId">报告ID</param>
        /// <returns>报告定义</returns>
        Task<ReportDefinition?> GetReportDefinitionAsync(uint rptId);

        /// <summary>
        /// 获取所有已定义的报告ID列表
        /// </summary>
        /// <returns>报告ID列表</returns>
        Task<List<uint>> GetDefinedReportsAsync();

        /// <summary>
        /// 链接事件与报告
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="rptIds">报告ID列表</param>
        /// <returns>链接结果</returns>
        Task<bool> LinkEventReportAsync(uint ceid, List<uint> rptIds);

        /// <summary>
        /// 启用或禁用事件
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>操作结果</returns>
        Task<bool> EnableEventAsync(uint ceid, bool enabled);

        /// <summary>
        /// 事件报告发送成功回调
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <returns>异步任务</returns>
        Task OnEventReportSentAsync(uint ceid);

        /// <summary>
        /// 记录报告查询
        /// </summary>
        /// <param name="rptid">报告ID</param>
        /// <param name="variableCount">变量数量</param>
        /// <returns>异步任务</returns>
        Task LogReportQueryAsync(uint rptid, int variableCount);

        /// <summary>
        /// 缓存事件报告（发送失败时）
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="eventName">事件名称</param>
        /// <param name="additionalData">附加数据</param>
        /// <returns>异步任务</returns>
        Task CacheEventReportAsync(uint ceid, string eventName, Dictionary<uint, object>? additionalData);
    }

    /// <summary>
    /// 报告定义类
    /// </summary>
    public class ReportDefinition
    {
        /// <summary>
        /// 报告ID
        /// </summary>
        public uint ReportId { get; set; }

        /// <summary>
        /// 变量ID列表
        /// </summary>
        public List<uint> VariableIds { get; set; } = new();

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime LastModifiedTime { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;
    }
}
