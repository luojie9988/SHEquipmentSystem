using DiceEquipmentSystem.Core.Enums;
using Secs4Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DiceEquipmentSystem.Secs.Models
{
    /// <summary>
    /// SECS连接统计信息 - 完整版本
    /// </summary>
    public class SecsConnectionStatistics
    {
        #region 基础统计

        /// <summary>发送的消息总数</summary>
        public long MessagesSent { get; set; }

        /// <summary>接收的消息总数</summary>
        public long MessagesReceived { get; set; }

        /// <summary>连接建立次数</summary>
        public int ConnectionCount { get; set; }

        /// <summary>断开连接次数</summary>
        public int DisconnectionCount { get; set; }

        /// <summary>系统启动时间</summary>
        public DateTime StartTime { get; set; }

        /// <summary>最后连接时间</summary>
        public DateTime? LastConnectedTime { get; set; }

        /// <summary>最后断开时间</summary>
        public DateTime? LastDisconnectedTime { get; set; }

        /// <summary>当前连接持续时间</summary>
        public TimeSpan CurrentConnectionDuration =>
            LastConnectedTime.HasValue && IsConnected ?
            DateTime.Now - LastConnectedTime.Value :
            TimeSpan.Zero;

        /// <summary>总运行时间</summary>
        public TimeSpan TotalUptime => DateTime.Now - StartTime;

        /// <summary>是否当前已连接</summary>
        public bool IsConnected { get; set; }

        #endregion

        #region 消息统计

        /// <summary>发送成功的消息数</summary>
        public long SuccessfulSentMessages { get; set; }

        /// <summary>发送失败的消息数</summary>
        public long FailedSentMessages { get; set; }

        /// <summary>接收到的Primary消息数</summary>
        public long PrimaryMessagesReceived { get; set; }

        /// <summary>接收到的Secondary消息数</summary>
        public long SecondaryMessagesReceived { get; set; }

        /// <summary>超时的消息数</summary>
        public long TimeoutMessages { get; set; }

        /// <summary>错误消息数</summary>
        public long ErrorMessages { get; set; }

        /// <summary>平均响应时间（毫秒）</summary>
        public double AverageResponseTime { get; set; }

        /// <summary>最大响应时间（毫秒）</summary>
        public long MaxResponseTime { get; set; }

        /// <summary>最小响应时间（毫秒）</summary>
        public long MinResponseTime { get; set; }

        #endregion

        #region HSMS统计

        /// <summary>Select Request发送次数</summary>
        public int SelectRequestsSent { get; set; }

        /// <summary>Select Response接收次数</summary>
        public int SelectResponsesReceived { get; set; }

        /// <summary>Deselect Request发送次数</summary>
        public int DeselectRequestsSent { get; set; }

        /// <summary>Linktest Request发送次数</summary>
        public int LinktestRequestsSent { get; set; }

        /// <summary>Linktest Response接收次数</summary>
        public int LinktestResponsesReceived { get; set; }

        /// <summary>Separate Request发送次数</summary>
        public int SeparateRequestsSent { get; set; }

        #endregion

        #region 错误统计

        /// <summary>总错误数</summary>
        public long TotalErrors { get; set; }

        /// <summary>连接错误数</summary>
        public int ConnectionErrors { get; set; }

        /// <summary>协议错误数</summary>
        public int ProtocolErrors { get; set; }

        /// <summary>超时错误数</summary>
        public int TimeoutErrors { get; set; }

        /// <summary>最后错误信息</summary>
        public string? LastError { get; set; }

        /// <summary>最后错误时间</summary>
        public DateTime? LastErrorTime { get; set; }

        #endregion

        #region 性能指标

        /// <summary>消息发送成功率</summary>
        public double SendSuccessRate =>
            MessagesSent > 0 ? (double)SuccessfulSentMessages / MessagesSent * 100 : 0;

        /// <summary>连接稳定性（连接时间比例）</summary>
        public double ConnectionStability
        {
            get
            {
                var totalTime = TotalUptime.TotalMilliseconds;
                if (totalTime <= 0) return 0;

                var connectedTime = ConnectionHistory
                    .Where(h => h.EndTime.HasValue)
                    .Sum(h => (h.EndTime.Value - h.StartTime).TotalMilliseconds);

                if (IsConnected && LastConnectedTime.HasValue)
                {
                    connectedTime += CurrentConnectionDuration.TotalMilliseconds;
                }

                return connectedTime / totalTime * 100;
            }
        }

        /// <summary>错误率</summary>
        public double ErrorRate =>
            (MessagesSent + MessagesReceived) > 0 ?
            (double)TotalErrors / (MessagesSent + MessagesReceived) * 100 : 0;

        #endregion

        #region 连接历史

        /// <summary>连接历史记录</summary>
        public List<ConnectionHistoryEntry> ConnectionHistory { get; set; } = new();

        /// <summary>每小时消息统计</summary>
        public Dictionary<DateTime, HourlyMessageStats> HourlyStats { get; set; } = new();

        #endregion

        #region 实时数据

        /// <summary>当前会话ID</summary>
        public int? CurrentSessionId { get; set; }

        /// <summary>最后活动时间</summary>
        public DateTime LastActivityTime { get; set; }

        /// <summary>当前连接状态</summary>
        public HsmsConnectionState CurrentState { get; set; }

        #endregion

        /// <summary>
        /// 获取统计摘要
        /// </summary>
        public string GetSummary()
        {
            return $"连接状态: {CurrentState}, " +
                   $"消息: 发送{MessagesSent}/接收{MessagesReceived}, " +
                   $"成功率: {SendSuccessRate:F1}%, " +
                   $"稳定性: {ConnectionStability:F1}%, " +
                   $"运行时间: {TotalUptime:d\\.hh\\:mm\\:ss}";
        }
    }

    /// <summary>
    /// 连接历史条目
    /// </summary>
    public class ConnectionHistoryEntry
    {
        /// <summary>连接开始时间</summary>
        public DateTime StartTime { get; set; }

        /// <summary>连接结束时间</summary>
        public DateTime? EndTime { get; set; }

        /// <summary>连接持续时间</summary>
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.Now - StartTime;

        /// <summary>断开原因</summary>
        public string? DisconnectReason { get; set; }

        /// <summary>是否为主动断开</summary>
        public bool IsActiveDisconnect { get; set; }
    }

    /// <summary>
    /// 每小时消息统计
    /// </summary>
    public class HourlyMessageStats
    {
        /// <summary>时间（小时）</summary>
        public DateTime Hour { get; set; }

        /// <summary>发送消息数</summary>
        public int SentCount { get; set; }

        /// <summary>接收消息数</summary>
        public int ReceivedCount { get; set; }

        /// <summary>错误数</summary>
        public int ErrorCount { get; set; }

        /// <summary>平均响应时间</summary>
        public double AvgResponseTime { get; set; }
    }

    /// <summary>
    /// 实时统计追踪器
    /// </summary>
    public class SecsStatisticsTracker
    {
        private readonly SecsConnectionStatistics _statistics;
        private readonly ConcurrentQueue<ResponseTimeEntry> _responseTimes = new();
        private readonly object _lock = new object();
        private const int MaxResponseTimeEntries = 1000;

        public SecsStatisticsTracker()
        {
            _statistics = new SecsConnectionStatistics
            {
                StartTime = DateTime.Now,
                LastActivityTime = DateTime.Now,
                MinResponseTime = long.MaxValue
            };
        }

        #region 消息统计更新

        /// <summary>
        /// 记录发送消息
        /// </summary>
        public void RecordMessageSent(SecsMessage message, bool success)
        {
            lock (_lock)
            {
                _statistics.MessagesSent++;
                _statistics.LastActivityTime = DateTime.Now;

                if (success)
                {
                    _statistics.SuccessfulSentMessages++;
                }
                else
                {
                    _statistics.FailedSentMessages++;
                    _statistics.TotalErrors++;
                }

                UpdateHourlyStats(DateTime.Now, sent: 1, received: 0, error: success ? 0 : 1);
            }
        }

        /// <summary>
        /// 记录接收消息
        /// </summary>
        public void RecordMessageReceived(SecsMessage message)
        {
            lock (_lock)
            {
                _statistics.MessagesReceived++;
                _statistics.LastActivityTime = DateTime.Now;

                if (message.F % 2 == 1) // Primary message
                {
                    _statistics.PrimaryMessagesReceived++;
                }
                else // Secondary message
                {
                    _statistics.SecondaryMessagesReceived++;
                }

                UpdateHourlyStats(DateTime.Now, sent: 0, received: 1, error: 0);
            }
        }

        /// <summary>
        /// 记录响应时间
        /// </summary>
        public void RecordResponseTime(long responseTimeMs)
        {
            lock (_lock)
            {
                _responseTimes.Enqueue(new ResponseTimeEntry
                {
                    ResponseTime = responseTimeMs,
                    Timestamp = DateTime.Now
                });

                // 限制队列大小
                while (_responseTimes.Count > MaxResponseTimeEntries)
                {
                    _responseTimes.TryDequeue(out _);
                }

                // 更新统计
                _statistics.MaxResponseTime = Math.Max(_statistics.MaxResponseTime, responseTimeMs);
                _statistics.MinResponseTime = Math.Min(_statistics.MinResponseTime, responseTimeMs);

                // 计算平均响应时间
                var responseTimes = _responseTimes.ToArray();
                _statistics.AverageResponseTime = responseTimes.Length > 0 ?
                    responseTimes.Average(rt => rt.ResponseTime) : 0;
            }
        }

        /// <summary>
        /// 记录超时
        /// </summary>
        public void RecordTimeout()
        {
            lock (_lock)
            {
                _statistics.TimeoutMessages++;
                _statistics.TimeoutErrors++;
                _statistics.TotalErrors++;
            }
        }

        /// <summary>
        /// 记录错误
        /// </summary>
        public void RecordError(string errorMessage, Exception? exception = null)
        {
            lock (_lock)
            {
                _statistics.TotalErrors++;
                _statistics.LastError = errorMessage;
                _statistics.LastErrorTime = DateTime.Now;

                if (exception is TimeoutException)
                {
                    _statistics.TimeoutErrors++;
                }
                else
                {
                    _statistics.ProtocolErrors++;
                }
            }
        }

        #endregion

        #region 连接状态更新

        /// <summary>
        /// 记录连接建立
        /// </summary>
        public void RecordConnectionEstablished()
        {
            lock (_lock)
            {
                _statistics.ConnectionCount++;
                _statistics.LastConnectedTime = DateTime.Now;
                _statistics.IsConnected = true;

                _statistics.ConnectionHistory.Add(new ConnectionHistoryEntry
                {
                    StartTime = DateTime.Now
                });
            }
        }

        /// <summary>
        /// 记录连接断开
        /// </summary>
        public void RecordConnectionLost(string reason, bool isActive = false)
        {
            lock (_lock)
            {
                _statistics.DisconnectionCount++;
                _statistics.LastDisconnectedTime = DateTime.Now;
                _statistics.IsConnected = false;

                // 更新最后一个连接历史记录
                var lastEntry = _statistics.ConnectionHistory.LastOrDefault();
                if (lastEntry != null && !lastEntry.EndTime.HasValue)
                {
                    lastEntry.EndTime = DateTime.Now;
                    lastEntry.DisconnectReason = reason;
                    lastEntry.IsActiveDisconnect = isActive;
                }

                if (!isActive)
                {
                    _statistics.ConnectionErrors++;
                    _statistics.TotalErrors++;
                }
            }
        }

        /// <summary>
        /// 更新连接状态
        /// </summary>
        public void UpdateConnectionState(HsmsConnectionState state)
        {
            lock (_lock)
            {
                _statistics.CurrentState = state;
                _statistics.LastActivityTime = DateTime.Now;
            }
        }

        #endregion

        #region HSMS统计更新

        /// <summary>
        /// 记录Select Request
        /// </summary>
        public void RecordSelectRequest()
        {
            lock (_lock)
            {
                _statistics.SelectRequestsSent++;
            }
        }

        /// <summary>
        /// 记录Select Response
        /// </summary>
        public void RecordSelectResponse()
        {
            lock (_lock)
            {
                _statistics.SelectResponsesReceived++;
            }
        }

        /// <summary>
        /// 记录Linktest
        /// </summary>
        public void RecordLinktestRequest()
        {
            lock (_lock)
            {
                _statistics.LinktestRequestsSent++;
            }
        }

        /// <summary>
        /// 记录Linktest Response
        /// </summary>
        public void RecordLinktestResponse()
        {
            lock (_lock)
            {
                _statistics.LinktestResponsesReceived++;
            }
        }

        #endregion

        #region 统计获取

        /// <summary>
        /// 获取统计快照
        /// </summary>
        public SecsConnectionStatistics GetSnapshot()
        {
            lock (_lock)
            {
                return new SecsConnectionStatistics
                {
                    MessagesSent = _statistics.MessagesSent,
                    MessagesReceived = _statistics.MessagesReceived,
                    ConnectionCount = _statistics.ConnectionCount,
                    DisconnectionCount = _statistics.DisconnectionCount,
                    StartTime = _statistics.StartTime,
                    LastConnectedTime = _statistics.LastConnectedTime,
                    LastDisconnectedTime = _statistics.LastDisconnectedTime,
                    IsConnected = _statistics.IsConnected,
                    SuccessfulSentMessages = _statistics.SuccessfulSentMessages,
                    FailedSentMessages = _statistics.FailedSentMessages,
                    PrimaryMessagesReceived = _statistics.PrimaryMessagesReceived,
                    SecondaryMessagesReceived = _statistics.SecondaryMessagesReceived,
                    TimeoutMessages = _statistics.TimeoutMessages,
                    ErrorMessages = _statistics.ErrorMessages,
                    AverageResponseTime = _statistics.AverageResponseTime,
                    MaxResponseTime = _statistics.MaxResponseTime,
                    MinResponseTime = _statistics.MinResponseTime,
                    SelectRequestsSent = _statistics.SelectRequestsSent,
                    SelectResponsesReceived = _statistics.SelectResponsesReceived,
                    DeselectRequestsSent = _statistics.DeselectRequestsSent,
                    LinktestRequestsSent = _statistics.LinktestRequestsSent,
                    LinktestResponsesReceived = _statistics.LinktestResponsesReceived,
                    SeparateRequestsSent = _statistics.SeparateRequestsSent,
                    TotalErrors = _statistics.TotalErrors,
                    ConnectionErrors = _statistics.ConnectionErrors,
                    ProtocolErrors = _statistics.ProtocolErrors,
                    TimeoutErrors = _statistics.TimeoutErrors,
                    LastError = _statistics.LastError,
                    LastErrorTime = _statistics.LastErrorTime,
                    ConnectionHistory = new List<ConnectionHistoryEntry>(_statistics.ConnectionHistory),
                    HourlyStats = new Dictionary<DateTime, HourlyMessageStats>(_statistics.HourlyStats),
                    CurrentSessionId = _statistics.CurrentSessionId,
                    LastActivityTime = _statistics.LastActivityTime,
                    CurrentState = _statistics.CurrentState
                };
            }
        }

        /// <summary>
        /// 重置统计
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                var startTime = _statistics.StartTime;
                var currentState = _statistics.CurrentState;
                var isConnected = _statistics.IsConnected;

                _statistics.MessagesSent = 0;
                _statistics.MessagesReceived = 0;
                _statistics.ConnectionCount = 0;
                _statistics.DisconnectionCount = 0;
                _statistics.SuccessfulSentMessages = 0;
                _statistics.FailedSentMessages = 0;
                _statistics.PrimaryMessagesReceived = 0;
                _statistics.SecondaryMessagesReceived = 0;
                _statistics.TimeoutMessages = 0;
                _statistics.ErrorMessages = 0;
                _statistics.AverageResponseTime = 0;
                _statistics.MaxResponseTime = 0;
                _statistics.MinResponseTime = long.MaxValue;
                _statistics.TotalErrors = 0;
                _statistics.ConnectionErrors = 0;
                _statistics.ProtocolErrors = 0;
                _statistics.TimeoutErrors = 0;
                _statistics.LastError = null;
                _statistics.LastErrorTime = null;
                _statistics.ConnectionHistory.Clear();
                _statistics.HourlyStats.Clear();

                _statistics.StartTime = startTime;
                _statistics.CurrentState = currentState;
                _statistics.IsConnected = isConnected;
                _statistics.LastActivityTime = DateTime.Now;

                _responseTimes.Clear();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 更新每小时统计
        /// </summary>
        private void UpdateHourlyStats(DateTime time, int sent, int received, int error)
        {
            var hour = new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0);

            if (!_statistics.HourlyStats.ContainsKey(hour))
            {
                _statistics.HourlyStats[hour] = new HourlyMessageStats { Hour = hour };
            }

            var stats = _statistics.HourlyStats[hour];
            stats.SentCount += sent;
            stats.ReceivedCount += received;
            stats.ErrorCount += error;

            // 清理旧的小时统计（保留48小时）
            var cutoff = DateTime.Now.AddHours(-48);
            var keysToRemove = _statistics.HourlyStats.Keys.Where(k => k < cutoff).ToList();
            foreach (var key in keysToRemove)
            {
                _statistics.HourlyStats.Remove(key);
            }
        }

        #endregion
    }

    /// <summary>
    /// 响应时间条目
    /// </summary>
    internal class ResponseTimeEntry
    {
        public long ResponseTime { get; set; }
        public DateTime Timestamp { get; set; }
    }
}