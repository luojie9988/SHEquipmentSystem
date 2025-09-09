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
    /// S5F7 (List Enabled Alarm Request) 启用报警列表请求处理器
    /// 处理主机的启用报警列表查询请求，返回当前已启用的所有报警ID
    /// </summary>
    /// <remarks>
    /// SEMI E5/E30 标准定义：
    /// - S5F7: List Enabled Alarm Request - 主机请求当前启用的报警列表
    /// - S5F8: List Enabled Alarm Data - 设备返回启用的报警ID列表
    /// 
    /// 交互流程：
    /// 1. 主机发送S5F7请求当前启用的报警列表（Header only）
    /// 2. 设备收集所有已启用的报警ID（通过S5F3配置的）
    /// 3. 按照报警类别和优先级排序
    /// 4. 返回S5F8包含启用的报警ID列表
    /// 5. 如果没有启用的报警，返回空列表
    /// 
    /// 划裂片设备报警启用管理：
    /// - 强制报警始终启用（安全相关）
    /// - 可选报警根据配置启用
    /// - 维护提醒默认禁用
    /// - 支持动态启用/禁用（通过S5F3）
    /// - 设备重启后需要重新配置（除强制报警外）
    /// 
    /// 与Host端匹配要点：
    /// - 返回所有已启用的报警ID
    /// - 包括强制报警和可选报警
    /// - 按照逻辑分组排序
    /// - 与S5F3配置保持一致
    /// 
    /// 使用场景：
    /// - Host端初始化时获取报警配置
    /// - 配置变更后验证启用状态
    /// - 定期审计报警配置
    /// - 故障诊断时确认报警启用情况
    /// </remarks>
    public class S5F7Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>S5F3处理器（用于获取启用状态）</summary>
        private readonly S5F3Handler? _s5f3Handler;

        /// <summary>S5F1处理器（备用源）</summary>
        private readonly IS5F1Handler? _s5f1Handler;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>默认启用的报警ID集合（本地缓存）</summary>
        private readonly HashSet<uint> _defaultEnabledAlarms;

        /// <summary>强制启用的报警ID集合</summary>
        private readonly HashSet<uint> _mandatoryAlarms;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 5;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 7;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="s5f3Handler">S5F3处理器（可选）</param>
        /// <param name="s5f1Handler">S5F1处理器（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S5F7Handler(
            ILogger<S5F7Handler> logger,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            S5F3Handler? s5f3Handler = null,
            IS5F1Handler? s5f1Handler = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _s5f3Handler = s5f3Handler;
            _s5f1Handler = s5f1Handler;

            _defaultEnabledAlarms = new HashSet<uint>();
            _mandatoryAlarms = new HashSet<uint>();

            // 初始化默认启用的报警
            InitializeDefaultEnabledAlarms();

            Logger.LogInformation($"S5F7处理器已初始化，默认启用 {_defaultEnabledAlarms.Count} 个报警");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S5F7消息，返回S5F8响应
        /// </summary>
        /// <param name="message">接收到的S5F7消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S5F8响应消息</returns>
        /// <remarks>
        /// S5F7 消息格式:
        /// Header only (无数据项)
        /// 
        /// S5F8 响应格式:
        /// L,n
        ///   1. &lt;ALID1&gt; 启用的报警ID1 (U4)
        ///   2. &lt;ALID2&gt; 启用的报警ID2 (U4)
        ///   ...
        ///   n. &lt;ALIDn&gt; 启用的报警IDn (U4)
        /// 
        /// 特殊情况：
        /// - 如果没有启用的报警，返回空列表
        /// - 报警按照类别和ID排序返回
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S5F7 (List Enabled Alarm Request) 启用报警列表请求");

            try
            {
                // S5F7应该是Header only
                if (message.SecsItem != null && message.SecsItem.Count > 0)
                {
                    Logger.LogWarning("S5F7消息应该是Header only，但收到了数据项");
                }

                // 收集启用的报警ID
                var enabledAlarms = await CollectEnabledAlarms(cancellationToken);

                // 排序报警ID
                var sortedAlarms = SortAlarmIds(enabledAlarms);

                // 构建S5F8响应
                var s5f8 = CreateS5F8Response(sortedAlarms);

                Logger.LogInformation($"返回 {sortedAlarms.Count} 个启用的报警ID");

                // 记录详细信息（调试级别）
                LogEnabledAlarmDetails(sortedAlarms);

                return s5f8;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S5F7消息失败");

                // 返回空列表表示错误
                return new SecsMessage(5, 8, false)
                {
                    Name = "ListEnabledAlarmData",
                    SecsItem = L()
                };
            }
        }

        #endregion

        #region 私有方法 - 报警收集

        /// <summary>
        /// 收集启用的报警ID
        /// </summary>
        private async Task<HashSet<uint>> CollectEnabledAlarms(CancellationToken cancellationToken)
        {
            var enabledAlarms = new HashSet<uint>();

            try
            {
                // 优先从S5F3处理器获取
                if (_s5f3Handler != null)
                {
                    var alarmsFromS5F3 = _s5f3Handler.GetEnabledAlarms();
                    foreach (var alid in alarmsFromS5F3)
                    {
                        enabledAlarms.Add(alid);
                    }

                    Logger.LogDebug($"从S5F3处理器获取到 {enabledAlarms.Count} 个启用的报警");
                }
                // 其次从S5F1处理器获取
                else if (_s5f1Handler != null)
                {
                    var alarmsFromS5F1 = _s5f1Handler.GetEnabledAlarms();
                    foreach (var alid in alarmsFromS5F1)
                    {
                        enabledAlarms.Add(alid);
                    }

                    Logger.LogDebug($"从S5F1处理器获取到 {enabledAlarms.Count} 个启用的报警");
                }
                // 使用默认配置
                else
                {
                    foreach (var alid in _defaultEnabledAlarms)
                    {
                        enabledAlarms.Add(alid);
                    }

                    Logger.LogDebug($"使用默认配置，{enabledAlarms.Count} 个启用的报警");
                }

                // 确保强制报警始终包含
                foreach (var alid in _mandatoryAlarms)
                {
                    enabledAlarms.Add(alid);
                }

                return enabledAlarms;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "收集启用的报警ID失败");

                // 返回至少包含强制报警
                return new HashSet<uint>(_mandatoryAlarms);
            }
        }

        /// <summary>
        /// 排序报警ID
        /// </summary>
        private List<uint> SortAlarmIds(HashSet<uint> alarmIds)
        {
            // 按照类别和ID排序
            var sorted = alarmIds.OrderBy(id => GetAlarmCategory(id))
                                 .ThenBy(id => id)
                                 .ToList();

            return sorted;
        }

        /// <summary>
        /// 获取报警类别（用于排序）
        /// </summary>
        private int GetAlarmCategory(uint alarmId)
        {
            return alarmId switch
            {
                >= 1000 and < 2000 => 1,  // 系统报警
                >= 2000 and < 3000 => 2,  // 工艺报警
                >= 3000 and < 4000 => 3,  // 刀具报警
                >= 4000 and < 5000 => 4,  // 材料报警
                >= 5000 and < 6000 => 5,  // 通信报警
                >= 6000 and < 7000 => 6,  // 维护提醒
                _ => 9                     // 其他
            };
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S5F8响应
        /// </summary>
        private SecsMessage CreateS5F8Response(List<uint> enabledAlarms)
        {
            // 构建ALID列表
            var alidItems = enabledAlarms.Select(alid => (Item)U4(alid)).ToArray();

            return new SecsMessage(5, 8, false)
            {
                Name = "ListEnabledAlarmData",
                SecsItem = alidItems.Length > 0 ? L(alidItems) : L()
            };
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化默认启用的报警
        /// </summary>
        private void InitializeDefaultEnabledAlarms()
        {
            // 强制报警（安全相关，必须启用）
            _mandatoryAlarms.Add(1001); // EMO_PRESSED
            _mandatoryAlarms.Add(1002); // SAFETY_DOOR_OPEN
            _mandatoryAlarms.Add(1003); // AIR_PRESSURE_LOW
            _mandatoryAlarms.Add(1004); // VACUUM_ERROR
            _mandatoryAlarms.Add(1005); // COOLING_WATER_ERROR
            _mandatoryAlarms.Add(1006); // SPINDLE_OVERLOAD
            _mandatoryAlarms.Add(1007); // SERVO_ALARM
            _mandatoryAlarms.Add(1008); // POWER_ERROR
            _mandatoryAlarms.Add(3003); // SCRIBE_KNIFE_BROKEN
            _mandatoryAlarms.Add(3004); // BREAK_KNIFE_BROKEN
            _mandatoryAlarms.Add(4001); // WAFER_BROKEN
            _mandatoryAlarms.Add(5001); // PLC_COMM_ERROR
            _mandatoryAlarms.Add(5005); // NETWORK_ERROR

            // 默认启用的可选报警
            _defaultEnabledAlarms.Add(2001); // TEMPERATURE_HIGH
            _defaultEnabledAlarms.Add(2002); // TEMPERATURE_LOW
            _defaultEnabledAlarms.Add(2003); // PRESSURE_HIGH
            _defaultEnabledAlarms.Add(2004); // PRESSURE_LOW
            _defaultEnabledAlarms.Add(3001); // SCRIBE_KNIFE_LIFE_END
            _defaultEnabledAlarms.Add(3002); // BREAK_KNIFE_LIFE_END
            _defaultEnabledAlarms.Add(4002); // WAFER_ALIGN_FAIL
            _defaultEnabledAlarms.Add(4003); // WAFER_ID_READ_FAIL

            // 将强制报警也加入默认启用集合
            foreach (var alid in _mandatoryAlarms)
            {
                _defaultEnabledAlarms.Add(alid);
            }

            Logger.LogDebug($"初始化完成 - 强制报警: {_mandatoryAlarms.Count}, " +
                          $"默认启用: {_defaultEnabledAlarms.Count}");
        }

        #endregion

        #region 私有方法 - 日志和统计

        /// <summary>
        /// 记录启用报警的详细信息
        /// </summary>
        private void LogEnabledAlarmDetails(List<uint> enabledAlarms)
        {
            if (!Logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            // 按类别统计
            var categoryStats = new Dictionary<string, int>
            {
                { "System", 0 },
                { "Process", 0 },
                { "Knife", 0 },
                { "Material", 0 },
                { "Communication", 0 },
                { "Maintenance", 0 },
                { "Other", 0 }
            };

            foreach (var alid in enabledAlarms)
            {
                var category = GetAlarmCategoryName(alid);
                if (categoryStats.ContainsKey(category))
                {
                    categoryStats[category]++;
                }
            }

            // 记录统计信息
            var statsMessage = string.Join(", ",
                categoryStats.Where(kvp => kvp.Value > 0)
                             .Select(kvp => $"{kvp.Key}: {kvp.Value}"));

            Logger.LogDebug($"启用的报警分布 - {statsMessage}");

            // 记录前10个报警ID（示例）
            if (enabledAlarms.Count > 0)
            {
                var sampleAlarms = string.Join(", ", enabledAlarms.Take(10));
                if (enabledAlarms.Count > 10)
                {
                    sampleAlarms += $", ... (共{enabledAlarms.Count}个)";
                }
                Logger.LogDebug($"启用的报警ID: {sampleAlarms}");
            }
        }

        /// <summary>
        /// 获取报警类别名称
        /// </summary>
        private string GetAlarmCategoryName(uint alarmId)
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

        #region 公共方法

        /// <summary>
        /// 获取启用的报警统计信息
        /// </summary>
        public async Task<EnabledAlarmStatistics> GetEnabledAlarmStatisticsAsync()
        {
            var enabledAlarms = await CollectEnabledAlarms(CancellationToken.None);

            var stats = new EnabledAlarmStatistics
            {
                TotalEnabled = enabledAlarms.Count,
                MandatoryCount = _mandatoryAlarms.Count,
                OptionalCount = enabledAlarms.Count - _mandatoryAlarms.Count
            };

            // 按类别统计
            foreach (var alid in enabledAlarms)
            {
                var category = GetAlarmCategoryName(alid);
                if (!stats.CategoryCounts.ContainsKey(category))
                {
                    stats.CategoryCounts[category] = 0;
                }
                stats.CategoryCounts[category]++;
            }

            // 添加详细的报警ID列表
            stats.SystemAlarms = enabledAlarms.Where(id => id >= 1000 && id < 2000).ToList();
            stats.ProcessAlarms = enabledAlarms.Where(id => id >= 2000 && id < 3000).ToList();
            stats.KnifeAlarms = enabledAlarms.Where(id => id >= 3000 && id < 4000).ToList();
            stats.MaterialAlarms = enabledAlarms.Where(id => id >= 4000 && id < 5000).ToList();
            stats.CommunicationAlarms = enabledAlarms.Where(id => id >= 5000 && id < 6000).ToList();
            stats.MaintenanceAlarms = enabledAlarms.Where(id => id >= 6000 && id < 7000).ToList();

            return stats;
        }

        /// <summary>
        /// 检查特定报警是否启用
        /// </summary>
        public async Task<bool> IsAlarmEnabledAsync(uint alarmId)
        {
            var enabledAlarms = await CollectEnabledAlarms(CancellationToken.None);
            return enabledAlarms.Contains(alarmId);
        }

        /// <summary>
        /// 获取强制报警列表
        /// </summary>
        public IEnumerable<uint> GetMandatoryAlarms()
        {
            return _mandatoryAlarms.ToList();
        }

        /// <summary>
        /// 导出启用报警配置
        /// </summary>
        public async Task<AlarmConfiguration> ExportConfigurationAsync()
        {
            var enabledAlarms = await CollectEnabledAlarms(CancellationToken.None);

            return new AlarmConfiguration
            {
                ExportTime = DateTime.Now,
                DeviceId = _config.Equipment.DeviceId,
                EnabledAlarms = enabledAlarms.OrderBy(x => x).ToList(),
                MandatoryAlarms = _mandatoryAlarms.OrderBy(x => x).ToList(),
                TotalDefined = GetTotalDefinedAlarmCount(),
                TotalEnabled = enabledAlarms.Count
            };
        }

        /// <summary>
        /// 获取总定义的报警数量
        /// </summary>
        private int GetTotalDefinedAlarmCount()
        {
            // 如果有S5F3处理器，从其获取
            if (_s5f3Handler != null)
            {
                return _s5f3Handler.GetDefinedAlarms().Count();
            }

            // 否则返回默认范围的估计值
            return 46; // 基于当前定义的报警数量
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 启用报警统计信息
        /// </summary>
        public class EnabledAlarmStatistics
        {
            /// <summary>总启用数量</summary>
            public int TotalEnabled { get; set; }

            /// <summary>强制报警数量</summary>
            public int MandatoryCount { get; set; }

            /// <summary>可选报警数量</summary>
            public int OptionalCount { get; set; }

            /// <summary>按类别统计</summary>
            public Dictionary<string, int> CategoryCounts { get; set; } = new();

            /// <summary>系统报警列表</summary>
            public List<uint> SystemAlarms { get; set; } = new();

            /// <summary>工艺报警列表</summary>
            public List<uint> ProcessAlarms { get; set; } = new();

            /// <summary>刀具报警列表</summary>
            public List<uint> KnifeAlarms { get; set; } = new();

            /// <summary>材料报警列表</summary>
            public List<uint> MaterialAlarms { get; set; } = new();

            /// <summary>通信报警列表</summary>
            public List<uint> CommunicationAlarms { get; set; } = new();

            /// <summary>维护报警列表</summary>
            public List<uint> MaintenanceAlarms { get; set; } = new();
        }

        /// <summary>
        /// 报警配置导出
        /// </summary>
        public class AlarmConfiguration
        {
            /// <summary>导出时间</summary>
            public DateTime ExportTime { get; set; }

            /// <summary>设备ID</summary>
            public int DeviceId { get; set; }

            /// <summary>启用的报警列表</summary>
            public List<uint> EnabledAlarms { get; set; } = new();

            /// <summary>强制报警列表</summary>
            public List<uint> MandatoryAlarms { get; set; } = new();

            /// <summary>总定义数量</summary>
            public int TotalDefined { get; set; }

            /// <summary>总启用数量</summary>
            public int TotalEnabled { get; set; }
        }

        #endregion
    }
}
