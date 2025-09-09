using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;
using static Secs4Net.Item;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S5F5 (List Alarms Request) 报警列表请求处理器
    /// 处理主机的报警列表查询请求，返回设备当前所有活动的报警信息
    /// </summary>
    /// <remarks>
    /// SEMI E5/E30 标准定义：
    /// - S5F5: List Alarms Request - 主机请求当前活动报警列表
    /// - S5F6: List Alarm Data - 设备返回活动报警的详细信息
    /// 
    /// 交互流程：
    /// 1. 主机发送S5F5请求当前活动报警列表（可指定特定ALID或请求所有）
    /// 2. 设备收集指定或所有的活动报警信息
    /// 3. 按照请求的顺序组织报警数据
    /// 4. 返回S5F6包含报警详细信息（ALCD/ALID/ALTX）
    /// 5. 如果没有活动报警，返回空列表
    /// 
    /// 划裂片设备报警查询特点：
    /// - 支持查询所有活动报警（空ALID列表）
    /// - 支持查询特定报警状态（指定ALID列表）
    /// - 返回报警的当前状态（SET/CLEAR）
    /// - 包含报警发生时间和持续时间
    /// - 优先返回高级别报警
    /// 
    /// 与Host端匹配要点：
    /// - 报警按照严重程度排序返回
    /// - 包含完整的报警描述文本
    /// - 支持批量查询和单个查询
    /// - 实时反映当前报警状态
    /// </remarks>
    public class S5F5Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>报警服务</summary>
        private readonly IAlarmService _alarmService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>S5F3处理器（用于获取报警定义）</summary>
        private readonly S5F3Handler? _s5f3Handler;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>活动报警缓存</summary>
        private readonly Dictionary<uint, AlarmRecord> _activeAlarms;

        /// <summary>报警缓存锁</summary>
        private readonly ReaderWriterLockSlim _alarmsLock = new();

        /// <summary>最大返回报警数量</summary>
        private const int MaxAlarmsToReturn = 100;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 5;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 5;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="alarmService">报警服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="s5f3Handler">S5F3处理器（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S5F5Handler(
            ILogger<S5F5Handler> logger,
            IAlarmService alarmService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            S5F3Handler? s5f3Handler = null) : base(logger)
        {
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _s5f3Handler = s5f3Handler;

            _activeAlarms = new Dictionary<uint, AlarmRecord>();

            Logger.LogInformation("S5F5处理器已初始化");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S5F5消息，返回S5F6响应
        /// </summary>
        /// <param name="message">接收到的S5F5消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S5F6响应消息</returns>
        /// <remarks>
        /// S5F5 消息格式:
        /// 特殊情况：
        /// - 空列表：返回所有活动报警
        /// - 指定ALID列表：返回指定报警的状态（即使未激活）
        /// 
        /// S5F6 响应格式:
        /// L,n
        ///   1. L,3
        ///      1. &lt;ALCD1&gt; 报警代码 (B,1) 128=SET, 0=CLEAR
        ///      2. &lt;ALID1&gt; 报警ID (U4)
        ///      3. &lt;ALTX1&gt; 报警文本 (A,n)
        ///   ...
        ///   n. L,3
        ///      1. &lt;ALCDn&gt;
        ///      2. &lt;ALIDn&gt;
        ///      3. &lt;ALTXn&gt;
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S5F5 (List Alarms Request) 报警列表请求");

            try
            {
                // 解析请求的报警ID列表
                var requestedAlarmIds = ParseS5F5Message(message.SecsItem);

                Logger.LogDebug($"请求的报警数量: {(requestedAlarmIds.Count == 0 ? "所有" : requestedAlarmIds.Count.ToString())}");

                // 获取报警数据
                var alarmData = await CollectAlarmData(requestedAlarmIds, cancellationToken);

                // 构建S5F6响应
                var s5f6 = CreateS5F6Response(alarmData);

                Logger.LogInformation($"返回 {alarmData.Count} 个报警状态");

                return s5f6;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S5F5消息失败");

                // 返回空列表表示错误
                return new SecsMessage(5, 6, false)
                {
                    Name = "ListAlarmData",
                    SecsItem = L()
                };
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S5F5消息
        /// </summary>
        private List<uint> ParseS5F5Message(Item? item)
        {
            var alarmIds = new List<uint>();

            try
            {
                // S5F5可以是空列表（请求所有报警）
                if (item == null || (item.Format == SecsFormat.List && item.Count == 0))
                {
                    Logger.LogDebug("请求所有活动报警");
                    return alarmIds; // 返回空列表表示请求所有
                }

                // 解析指定的ALID列表
                if (item.Format == SecsFormat.List)
                {
                    foreach (var alidItem in item.Items ?? Array.Empty<Item>())
                    {
                        if (alidItem.Format == SecsFormat.U4)
                        {
                            alarmIds.Add(alidItem.FirstValue<uint>());
                        }
                        else if (alidItem.Format == SecsFormat.U2)
                        {
                            alarmIds.Add(alidItem.FirstValue<ushort>());
                        }
                        else if (alidItem.Format == SecsFormat.U1)
                        {
                            alarmIds.Add(alidItem.FirstValue<byte>());
                        }
                        else
                        {
                            Logger.LogWarning($"不支持的ALID格式: {alidItem.Format}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("S5F5消息格式无效，期望列表类型");
                }

                if (alarmIds.Count > 0)
                {
                    Logger.LogDebug($"请求特定报警: {string.Join(", ", alarmIds)}");
                }

                return alarmIds;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S5F5消息异常");
                return new List<uint>();
            }
        }

        /// <summary>
        /// 创建S5F6响应
        /// </summary>
        private SecsMessage CreateS5F6Response(List<AlarmInfo> alarmData)
        {
            var alarmItems = new List<Item>();

            foreach (var alarm in alarmData)
            {
                var alarmItem = L(
                    B(alarm.AlarmCode),      // ALCD - 报警代码
                    U4(alarm.AlarmId),        // ALID - 报警ID
                    A(alarm.AlarmText)        // ALTX - 报警文本
                );
                alarmItems.Add(alarmItem);
            }

            return new SecsMessage(5, 6, false)
            {
                Name = "ListAlarmData",
                SecsItem = alarmItems.Count > 0 ? L(alarmItems.ToArray()) : L()
            };
        }

        #endregion

        #region 私有方法 - 报警数据收集

        /// <summary>
        /// 收集报警数据
        /// </summary>
        private async Task<List<AlarmInfo>> CollectAlarmData(
            List<uint> requestedAlarmIds, CancellationToken cancellationToken)
        {
            var alarmData = new List<AlarmInfo>();

            try
            {
                // 从报警服务获取活动报警
                var activeAlarms = await _alarmService.GetActiveAlarmsAsync();

                // 更新内部缓存
                UpdateAlarmCache(activeAlarms);

                if (requestedAlarmIds.Count == 0)
                {
                    // 请求所有活动报警
                    alarmData = GetAllActiveAlarms(activeAlarms);
                }
                else
                {
                    // 请求特定报警
                    alarmData = GetSpecificAlarms(requestedAlarmIds, activeAlarms);
                }

                // 按照优先级排序（系统报警优先）
                alarmData = SortAlarmsByPriority(alarmData);

                // 限制返回数量
                if (alarmData.Count > MaxAlarmsToReturn)
                {
                    Logger.LogWarning($"活动报警数量 {alarmData.Count} 超过限制 {MaxAlarmsToReturn}，将截断");
                    alarmData = alarmData.Take(MaxAlarmsToReturn).ToList();
                }

                return alarmData;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "收集报警数据失败");
                return alarmData;
            }
        }

        /// <summary>
        /// 获取所有活动报警
        /// </summary>
        private List<AlarmInfo> GetAllActiveAlarms(List<Services.Interfaces.AlarmInfo> activeAlarms)
        {
            var result = new List<AlarmInfo>();

            foreach (var alarm in activeAlarms)
            {
                result.Add(new AlarmInfo
                {
                    AlarmId = alarm.AlarmId,
                    AlarmCode = 128, // SET - 活动报警
                    AlarmText = EnrichAlarmText(alarm.AlarmId, alarm.AlarmText, alarm.SetTime)
                });
            }

            Logger.LogDebug($"返回所有 {result.Count} 个活动报警");
            return result;
        }

        /// <summary>
        /// 获取特定报警
        /// </summary>
        private List<AlarmInfo> GetSpecificAlarms(
            List<uint> requestedAlarmIds,
            List<Services.Interfaces.AlarmInfo> activeAlarms)
        {
            var result = new List<AlarmInfo>();
            var activeAlarmDict = activeAlarms.ToDictionary(a => a.AlarmId);

            foreach (var alarmId in requestedAlarmIds)
            {
                if (activeAlarmDict.TryGetValue(alarmId, out var activeAlarm))
                {
                    // 报警处于激活状态
                    result.Add(new AlarmInfo
                    {
                        AlarmId = alarmId,
                        AlarmCode = 128, // SET
                        AlarmText = EnrichAlarmText(alarmId, activeAlarm.AlarmText, activeAlarm.SetTime)
                    });
                }
                else if (IsDefinedAlarm(alarmId))
                {
                    // 报警已定义但未激活
                    result.Add(new AlarmInfo
                    {
                        AlarmId = alarmId,
                        AlarmCode = 0, // CLEAR
                        AlarmText = GetAlarmDescription(alarmId)
                    });
                }
                else
                {
                    // 未定义的报警ID
                    Logger.LogWarning($"请求的报警ID {alarmId} 未定义");
                }
            }

            Logger.LogDebug($"返回 {result.Count} 个特定报警状态");
            return result;
        }

        /// <summary>
        /// 按优先级排序报警
        /// </summary>
        private List<AlarmInfo> SortAlarmsByPriority(List<AlarmInfo> alarms)
        {
            return alarms.OrderBy(a => GetAlarmPriority(a.AlarmId))
                         .ThenBy(a => a.AlarmId)
                         .ToList();
        }

        /// <summary>
        /// 获取报警优先级（数字越小优先级越高）
        /// </summary>
        private int GetAlarmPriority(uint alarmId)
        {
            return alarmId switch
            {
                >= 1000 and < 2000 => 1,  // 系统报警 - 最高优先级
                >= 3003 and <= 3004 => 2, // 断刀报警 - 高优先级
                >= 4001 and <= 4001 => 2, // 晶圆破损 - 高优先级
                >= 5001 and <= 5001 => 2, // PLC通信错误 - 高优先级
                >= 2000 and < 3000 => 3,  // 工艺报警 - 中优先级
                >= 3000 and < 4000 => 4,  // 刀具报警 - 中优先级
                >= 4000 and < 5000 => 5,  // 材料报警 - 中优先级
                >= 5000 and < 6000 => 6,  // 通信报警 - 低优先级
                >= 6000 and < 7000 => 7,  // 维护提醒 - 最低优先级
                _ => 9                     // 其他
            };
        }

        /// <summary>
        /// 丰富报警文本（添加时间和持续时间信息）
        /// </summary>
        private string EnrichAlarmText(uint alarmId, string baseText, DateTime setTime)
        {
            var duration = DateTime.Now - setTime;
            var durationText = FormatDuration(duration);

            return $"{baseText} [Active for {durationText}]";
        }

        /// <summary>
        /// 格式化持续时间
        /// </summary>
        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
            {
                return $"{(int)duration.TotalDays}d {duration.Hours}h";
            }
            else if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            }
            else if (duration.TotalMinutes >= 1)
            {
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            }
            else
            {
                return $"{(int)duration.TotalSeconds}s";
            }
        }

        #endregion

        #region 私有方法 - 报警定义

        /// <summary>
        /// 检查是否为已定义的报警
        /// </summary>
        private bool IsDefinedAlarm(uint alarmId)
        {
            // 如果有S5F3处理器，从其获取定义
            if (_s5f3Handler != null)
            {
                return _s5f3Handler.GetDefinedAlarms().Contains(alarmId);
            }

            // 否则使用默认范围判断
            return alarmId switch
            {
                >= 1000 and < 7000 => true,  // 标准报警范围
                _ => false
            };
        }

        /// <summary>
        /// 获取报警描述
        /// </summary>
        private string GetAlarmDescription(uint alarmId)
        {
            return alarmId switch
            {
                // 系统报警 (1000-1999)
                1001 => "Emergency stop button pressed",
                1002 => "Safety door is open",
                1003 => "Air pressure is too low",
                1004 => "Vacuum system error",
                1005 => "Cooling water flow error",
                1006 => "Spindle motor overload",
                1007 => "Servo drive alarm",
                1008 => "Power supply error",

                // 工艺报警 (2000-2999)
                2001 => "Process temperature too high",
                2002 => "Process temperature too low",
                2003 => "Process pressure too high",
                2004 => "Process pressure too low",
                2005 => "Process speed error",
                2006 => "Cut depth error",
                2007 => "Process parameter error",

                // 刀具报警 (3000-3999)
                3001 => "Scribe knife life expired",
                3002 => "Break knife life expired",
                3003 => "Scribe knife broken detected",
                3004 => "Break knife broken detected",
                3005 => "Knife not installed",
                3006 => "Knife type error",
                3007 => "Knife change required",

                // 材料报警 (4000-4999)
                4001 => "Wafer broken detected",
                4002 => "Wafer alignment failed",
                4003 => "Wafer ID read failed",
                4004 => "Cassette not present",
                4005 => "Slot mapping error",
                4006 => "Material type mismatch",
                4007 => "Wafer transfer error",

                // 通信报警 (5000-5999)
                5001 => "PLC communication error",
                5002 => "Sensor offline",
                5003 => "Vision system error",
                5004 => "Barcode reader error",
                5005 => "Network error",

                // 维护提醒 (6000-6999)
                6001 => "Daily maintenance due",
                6002 => "Weekly maintenance due",
                6003 => "Monthly maintenance due",
                6004 => "Calibration due",
                6005 => "Lubrication reminder",
                6006 => "Filter clean reminder",

                // 默认
                _ => $"Alarm {alarmId}"
            };
        }

        #endregion

        #region 私有方法 - 缓存管理

        /// <summary>
        /// 更新报警缓存
        /// </summary>
        private void UpdateAlarmCache(List<Services.Interfaces.AlarmInfo> activeAlarms)
        {
            _alarmsLock.EnterWriteLock();
            try
            {
                // 清除旧缓存
                _activeAlarms.Clear();

                // 添加新的活动报警
                foreach (var alarm in activeAlarms)
                {
                    _activeAlarms[alarm.AlarmId] = new AlarmRecord
                    {
                        AlarmId = alarm.AlarmId,
                        AlarmText = alarm.AlarmText,
                        SetTime = alarm.SetTime,
                        LastUpdateTime = DateTime.Now
                    };
                }

                Logger.LogDebug($"更新报警缓存，当前活动报警数: {_activeAlarms.Count}");
            }
            finally
            {
                _alarmsLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 从缓存获取报警记录
        /// </summary>
        private AlarmRecord? GetAlarmFromCache(uint alarmId)
        {
            _alarmsLock.EnterReadLock();
            try
            {
                return _activeAlarms.TryGetValue(alarmId, out var record) ? record : null;
            }
            finally
            {
                _alarmsLock.ExitReadLock();
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取当前活动报警数量
        /// </summary>
        public int GetActiveAlarmCount()
        {
            _alarmsLock.EnterReadLock();
            try
            {
                return _activeAlarms.Count;
            }
            finally
            {
                _alarmsLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取活动报警摘要
        /// </summary>
        public Dictionary<string, int> GetAlarmSummary()
        {
            _alarmsLock.EnterReadLock();
            try
            {
                var summary = new Dictionary<string, int>
                {
                    { "System", 0 },
                    { "Process", 0 },
                    { "Knife", 0 },
                    { "Material", 0 },
                    { "Communication", 0 },
                    { "Maintenance", 0 }
                };

                foreach (var alarm in _activeAlarms.Values)
                {
                    var category = GetAlarmCategory(alarm.AlarmId);
                    if (summary.ContainsKey(category))
                    {
                        summary[category]++;
                    }
                }

                return summary;
            }
            finally
            {
                _alarmsLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取报警类别
        /// </summary>
        private string GetAlarmCategory(uint alarmId)
        {
            return alarmId switch
            {
                >= 1000 and < 2000 => "System",
                >= 2000 and < 3000 => "Process",
                >= 3000 and < 4000 => "Knife",
                >= 4000 and < 5000 => "Material",
                >= 5000 and < 6000 => "Communication",
                >= 6000 and < 7000 => "Maintenance",
                _ => "Other"
            };
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            _alarmsLock?.Dispose();
            base.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 报警信息
        /// </summary>
        private class AlarmInfo
        {
            public uint AlarmId { get; set; }
            public byte AlarmCode { get; set; }
            public string AlarmText { get; set; } = "";
        }

        /// <summary>
        /// 报警记录
        /// </summary>
        private class AlarmRecord
        {
            public uint AlarmId { get; set; }
            public string AlarmText { get; set; } = "";
            public DateTime SetTime { get; set; }
            public DateTime LastUpdateTime { get; set; }
        }

        #endregion
    }
}
