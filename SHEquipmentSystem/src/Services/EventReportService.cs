// 文件路径: src/DiceEquipmentSystem/Services/EventReportService.cs
// 版本: v1.0.0
// 描述: 事件报告服务实现

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 事件报告服务实现
    /// </summary>
    public class EventReportService : IEventReportService
    {
        private readonly ILogger<EventReportService> _logger;
        private readonly ConcurrentDictionary<uint, ReportDefinition> _reportDefinitions;
        private readonly ConcurrentDictionary<uint, List<uint>> _eventLinkages;
        private readonly ConcurrentDictionary<uint, bool> _eventEnabled;
        private readonly Queue<CachedEventReport> _cachedReports;
        private readonly object _cacheLock = new();
        private readonly ConcurrentDictionary<uint, EventDefinition> _eventDefinitions;

        public EventReportService(ILogger<EventReportService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reportDefinitions = new ConcurrentDictionary<uint, ReportDefinition>();
            _eventLinkages = new ConcurrentDictionary<uint, List<uint>>();
            _eventEnabled = new ConcurrentDictionary<uint, bool>();
            _cachedReports = new Queue<CachedEventReport>();
            _eventDefinitions = new ConcurrentDictionary<uint, EventDefinition>();
        }

        /// <summary>
        /// 定义事件报告
        /// </summary>
        /// <summary>
        /// 获取已定义的事件数量
        /// </summary>
        public int GetDefinedEventCount()
        {
            return _eventDefinitions.Count;
        }
        
        /// <summary>
        /// 获取已启用的事件数量
        /// </summary>
        public int GetEnabledEventCount()
        {
            return _eventDefinitions.Count(kvp => kvp.Value.IsEnabled);
        }
        
        /// <summary>
        /// 初始化默认事件
        /// </summary>
        public async Task InitializeDefaultEventsAsync()
        {
            _logger.LogDebug("初始化默认事件定义");
            
            // 默认事件定义
            var defaultEvents = new Dictionary<uint, (string name, bool enabled)>
            {
                { 100, ("ProcessStateChange", true) },
                { 101, ("EquipmentStateChange", true) },
                { 200, ("ProcessProgramStart", true) },
                { 201, ("ProcessProgramEnd", true) },
                { 300, ("AlarmSet", true) },
                { 301, ("AlarmClear", true) },
                { 302, ("MaterialReceived", true) },
                { 303, ("MaterialRemoved", true) }
            };
            
            foreach (var evt in defaultEvents)
            {
                _eventDefinitions[evt.Key] = new EventDefinition
                {
                    EventId = evt.Key,
                    EventName = evt.Value.name,
                    IsEnabled = evt.Value.enabled
                };
            }
            
            await Task.CompletedTask;
        }

        public Task<bool> DefineReportAsync(ReportDefinition reportDef)
        {
            try
            {
                reportDef.CreatedTime = DateTime.Now;
                reportDef.LastModifiedTime = DateTime.Now;
                _reportDefinitions[reportDef.ReportId] = reportDef;
                _logger.LogDebug($"定义报告 {reportDef.ReportId}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"定义报告 {reportDef.ReportId} 失败");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 删除报告定义
        /// </summary>
        public Task<bool> DeleteReportDefinitionAsync(uint rptId)
        {
            var result = _reportDefinitions.TryRemove(rptId, out _);
            if (result)
            {
                _logger.LogDebug($"删除报告定义 {rptId}");
            }
            return Task.FromResult(result);
        }

        /// <summary>
        /// 发送事件报告
        /// </summary>
        public Task<bool> ReportEventAsync(uint ceid, params object[] data)
        {
            _logger.LogDebug($"报告事件 {ceid}");
            // 实际实现应该通过S6F11Handler发送
            return Task.FromResult(true);
        }

        /// <summary>
        /// 发送事件报告（带名称和附加数据）
        /// </summary>
        public Task<bool> ReportEventAsync(uint ceid, string eventName, Dictionary<uint, object>? additionalData)
        {
            _logger.LogDebug($"报告事件 {ceid} ({eventName})");
            // 实际实现应该通过S6F11Handler发送
            return Task.FromResult(true);
        }

        /// <summary>
        /// 获取报告定义
        /// </summary>
        public Task<ReportDefinition?> GetReportDefinitionAsync(uint rptId)
        {
            _reportDefinitions.TryGetValue(rptId, out var definition);
            return Task.FromResult(definition);
        }

        /// <summary>
        /// 获取所有已定义的报告ID列表
        /// </summary>
        public Task<List<uint>> GetDefinedReportsAsync()
        {
            var reports = _reportDefinitions.Keys.OrderBy(k => k).ToList();
            return Task.FromResult(reports);
        }

        /// <summary>
        /// 链接事件与报告
        /// </summary>
        public Task<bool> LinkEventReportAsync(uint ceid, List<uint> rptIds)
        {
            _eventLinkages[ceid] = rptIds;
            _logger.LogDebug($"链接事件 {ceid} 到报告 [{string.Join(",", rptIds)}]");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 启用或禁用事件
        /// </summary>
        public Task<bool> EnableEventAsync(uint ceid, bool enabled)
        {
            _eventEnabled[ceid] = enabled;
            _logger.LogDebug($"事件 {ceid} {(enabled ? "启用" : "禁用")}");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 事件报告发送成功回调
        /// </summary>
        public Task OnEventReportSentAsync(uint ceid)
        {
            _logger.LogDebug($"事件 {ceid} 报告已发送");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 记录报告查询
        /// </summary>
        public Task LogReportQueryAsync(uint rptid, int variableCount)
        {
            _logger.LogDebug($"查询报告 {rptid}，包含 {variableCount} 个变量");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 缓存事件报告
        /// </summary>
        public Task CacheEventReportAsync(uint ceid, string eventName, Dictionary<uint, object>? additionalData)
        {
            lock (_cacheLock)
            {
                var cachedReport = new CachedEventReport
                {
                    Ceid = ceid,
                    EventName = eventName,
                    AdditionalData = additionalData ?? new Dictionary<uint, object>(),
                    Timestamp = DateTime.Now
                };

                _cachedReports.Enqueue(cachedReport);

                // 限制缓存大小
                while (_cachedReports.Count > 100)
                {
                    _cachedReports.Dequeue();
                }

                _logger.LogDebug($"缓存事件报告 {ceid} ({eventName})");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 触发事件
        /// </summary>
        public async Task TriggerEventAsync(uint ceid, string? eventName = null)
        {
            _logger.LogDebug($"触发事件 {ceid} ({eventName ?? "未命名"})");
            
            // 检查事件是否启用
            if (!_eventEnabled.TryGetValue(ceid, out var enabled) || !enabled)
            {
                _logger.LogDebug($"事件 {ceid} 未启用，跳过");
                return;
            }

            // 获取链接的报告
            if (_eventLinkages.TryGetValue(ceid, out var reportIds))
            {
                _logger.LogDebug($"事件 {ceid} 链接到报告: [{string.Join(",", reportIds)}]");
            }

            // 触发事件报告
            await ReportEventAsync(ceid, eventName ?? $"Event_{ceid}", null);
        }

        /// <summary>
        /// 发送事件报告（异步队列处理）
        /// </summary>
        public async Task SendEventReportAsync(
            uint ceid,
            string eventName,
            Dictionary<uint, object>? additionalData = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            _logger.LogDebug($"发送事件报告 {ceid} ({eventName})");

            // 检查事件是否启用
            if (!_eventEnabled.TryGetValue(ceid, out var enabled) || !enabled)
            {
                _logger.LogDebug($"事件 {ceid} 未启用，跳过发送");
                return;
            }

            // 异步发送事件报告
            await Task.Run(async () =>
            {
                try
                {
                    await ReportEventAsync(ceid, eventName, additionalData);
                    await OnEventReportSentAsync(ceid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"发送事件报告 {ceid} 失败");
                    // 失败时缓存
                    await CacheEventReportAsync(ceid, eventName, additionalData);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 缓存的事件报告
        /// </summary>
        private class CachedEventReport
        {
            public uint Ceid { get; set; }
            public string EventName { get; set; } = "";
            public Dictionary<uint, object> AdditionalData { get; set; } = new();
            public DateTime Timestamp { get; set; }
        }
    }

    /// <summary>
    /// 事件定义
    /// </summary>
    internal class EventDefinition
    {
        public uint EventId { get; set; }
        public string EventName { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }
}
