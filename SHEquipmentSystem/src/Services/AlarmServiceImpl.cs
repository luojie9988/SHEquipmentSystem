using DiceEquipmentSystem.Core.Events;
using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.Secs.Handlers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 报警服务实现类
    /// 提供划裂片设备的报警管理功能，包括报警的设置、清除、查询和状态维护
    /// </summary>
    /// <remarks>
    /// 主要功能：
    /// 1. 报警状态管理：维护所有报警的当前状态
    /// 2. 报警生命周期：处理报警的触发、确认、清除流程
    /// 3. PLC集成：监控PLC报警信号并自动触发报警
    /// 4. 事件通知：报警状态变化时触发相关事件
    /// 5. 历史记录：维护报警历史和统计信息
    /// 6. 优先级管理：根据报警类型确定处理优先级
    /// 
    /// 报警类别：
    /// - 1xxx: 系统报警（急停、安全门等）
    /// - 2xxx: 工艺报警（温度、压力异常等）
    /// - 3xxx: 刀具报警（寿命、断刀等）
    /// - 4xxx: 材料报警（晶圆破损、定位失败等）
    /// - 5xxx: 通信报警（PLC、传感器离线等）
    /// - 6xxx: 维护提醒（保养、校准等）
    /// </remarks>
    public class AlarmServiceImpl : IAlarmService, IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<AlarmServiceImpl> _logger;
        private readonly IEventBus? _eventBus;
        private readonly IPlcDataProvider? _plcProvider;
        private readonly IS5F1Handler? _s5f1Handler;

        /// <summary>活动报警集合（线程安全）</summary>
        private readonly ConcurrentDictionary<uint, AlarmRecord> _activeAlarms;

        /// <summary>报警历史记录</summary>
        private readonly List<AlarmHistoryRecord> _alarmHistory;

        /// <summary>报警定义字典</summary>
        private readonly Dictionary<uint, AlarmDefinition> _alarmDefinitions;

        /// <summary>报警统计信息</summary>
        private readonly AlarmStatistics _statistics;

        /// <summary>PLC报警监控定时器</summary>
        private Timer? _plcMonitorTimer;

        /// <summary>报警清理定时器</summary>
        private Timer? _cleanupTimer;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>历史记录锁</summary>
        private readonly ReaderWriterLockSlim _historyLock = new();

        /// <summary>最大历史记录数</summary>
        private const int MaxHistoryRecords = 10000;

        /// <summary>PLC监控间隔（毫秒）</summary>
        private const int PlcMonitorInterval = 500;

        /// <summary>清理间隔（毫秒）</summary>
        private const int CleanupInterval = 60000; // 1分钟

        #endregion

        #region 事件

        /// <summary>
        /// 报警发生事件
        /// </summary>
        public event EventHandler<Interfaces.AlarmEventArgs>? AlarmOccurred;

        /// <summary>
        /// 报警清除事件
        /// </summary>
        public event EventHandler<Interfaces.AlarmEventArgs>? AlarmCleared;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public AlarmServiceImpl(
            ILogger<AlarmServiceImpl> logger,
            IEventBus? eventBus = null,
            IPlcDataProvider? plcProvider = null,
            IS5F1Handler? s5f1Handler = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus;
            _plcProvider = plcProvider;
            _s5f1Handler = s5f1Handler;

            _activeAlarms = new ConcurrentDictionary<uint, AlarmRecord>();
            _alarmHistory = new List<AlarmHistoryRecord>();
            _alarmDefinitions = new Dictionary<uint, AlarmDefinition>();
            _statistics = new AlarmStatistics();

            // 初始化报警定义
            InitializeAlarmDefinitions();

            _logger.LogInformation($"报警服务已初始化，定义了 {_alarmDefinitions.Count} 个报警");
        }

        #endregion

        #region IHostedService 实现

        /// <summary>
        /// 启动服务
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在启动报警服务...");

            _cancellationTokenSource = new CancellationTokenSource();

            // 启动PLC报警监控
            if (_plcProvider != null)
            {
                _plcMonitorTimer = new Timer(
                    async _ => await MonitorPlcAlarms(),
                    null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(PlcMonitorInterval));
            }

            // 启动报警清理任务
            _cleanupTimer = new Timer(
                async _ => await CleanupExpiredAlarms(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMilliseconds(CleanupInterval));

            _logger.LogInformation("报警服务已启动");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止报警服务...");

            _plcMonitorTimer?.Dispose();
            _cleanupTimer?.Dispose();
            _cancellationTokenSource?.Cancel();

            // 保存当前报警状态
            await SaveAlarmStateAsync();

            _logger.LogInformation("报警服务已停止");
        }

        #endregion

        #region IAlarmService 实现

        /// <summary>
        /// 设置报警
        /// </summary>
        /// <param name="alid">报警ID</param>
        /// <param name="alarmText">报警文本</param>
        /// <summary>
        /// 获取已定义的报警数量
        /// </summary>
        public int GetDefinedAlarmCount()
        {
            return _alarmDefinitions.Count;
        }
        
        /// <summary>
        /// 获取活动报警数量
        /// </summary>
        public int GetActiveAlarmCount()
        {
            return _activeAlarms.Count;
        }
        
        /// <summary>
        /// 初始化默认报警
        /// </summary>
        public async Task InitializeDefaultAlarmsAsync()
        {
            _logger.LogDebug("初始化默认报警定义");
            
            // 默认报警已在InitializeAsync方法中初始化
            // 这里可以添加额外的初始化逻辑
            await Task.CompletedTask;
        }

        public async Task SetAlarmAsync(uint alid, string alarmText)
        {
            try
            {
                // 检查报警是否已定义
                if (!_alarmDefinitions.TryGetValue(alid, out var definition))
                {
                    _logger.LogWarning($"尝试设置未定义的报警: {alid}");
                    definition = CreateDefaultDefinition(alid, alarmText);
                }

                // 检查是否已经激活
                if (_activeAlarms.ContainsKey(alid))
                {
                    _logger.LogDebug($"报警 {alid} 已经激活");
                    return;
                }

                // 创建报警记录
                var record = new AlarmRecord
                {
                    AlarmId = alid,
                    AlarmText = string.IsNullOrEmpty(alarmText) ? definition.Description : alarmText,
                    SetTime = DateTime.Now,
                    Priority = definition.Priority,
                    Category = definition.Category,
                    IsAcknowledged = false
                };

                // 添加到活动报警
                if (_activeAlarms.TryAdd(alid, record))
                {
                    _logger.LogWarning($"报警设置 - ID: {alid}, 文本: {record.AlarmText}");

                    // 更新统计
                    UpdateStatistics(alid, true);

                    // 添加到历史
                    AddToHistory(alid, AlarmAction.Set, record.AlarmText);

                    // 发送S5F1报警报告
                    await SendAlarmReport(alid, 128, record.AlarmText); // 128 = SET

                    // 触发事件
                    await TriggerAlarmEvent(alid, AlarmEventType.Set);

                    // 检查是否需要紧急处理
                    if (definition.Priority == AlarmPriority.Critical)
                    {
                        await HandleCriticalAlarm(alid, record);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"设置报警 {alid} 失败");
                throw;
            }
        }

        /// <summary>
        /// 清除报警
        /// </summary>
        /// <param name="alid">报警ID</param>
        public async Task ClearAlarmAsync(uint alid)
        {
            try
            {
                // 移除活动报警
                if (_activeAlarms.TryRemove(alid, out var record))
                {
                    _logger.LogInformation($"报警清除 - ID: {alid}, 持续时间: {DateTime.Now - record.SetTime}");

                    // 更新统计
                    UpdateStatistics(alid, false);

                    // 添加到历史
                    AddToHistory(alid, AlarmAction.Clear, record.AlarmText);

                    // 发送S5F1报警报告
                    await SendAlarmReport(alid, 0, record.AlarmText); // 0 = CLEAR

                    // 触发事件
                    await TriggerAlarmEvent(alid, AlarmEventType.Clear);
                }
                else
                {
                    _logger.LogDebug($"尝试清除未激活的报警: {alid}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"清除报警 {alid} 失败");
                throw;
            }
        }

        /// <summary>
        /// 获取所有活动报警
        /// </summary>
        public Task<List<AlarmInfo>> GetActiveAlarmsAsync()
        {
            var alarms = _activeAlarms.Values
                .Select(r => new AlarmInfo
                {
                    AlarmId = r.AlarmId,
                    AlarmText = r.AlarmText,
                    SetTime = r.SetTime
                })
                .OrderBy(a => GetAlarmPriority(a.AlarmId))
                .ThenBy(a => a.AlarmId)
                .ToList();

            return Task.FromResult(alarms);
        }

        /// <summary>
        /// 确认报警
        /// </summary>
        public async Task AcknowledgeAlarmAsync(uint alid)
        {
            if (_activeAlarms.TryGetValue(alid, out var record))
            {
                record.IsAcknowledged = true;
                record.AcknowledgeTime = DateTime.Now;

                _logger.LogInformation($"报警已确认 - ID: {alid}");

                // 添加到历史
                AddToHistory(alid, AlarmAction.Acknowledge, record.AlarmText);

                // 触发事件
                await TriggerAlarmEvent(alid, AlarmEventType.Acknowledge);
            }
        }

        /// <summary>
        /// 重置所有报警
        /// </summary>
        public async Task ResetAllAlarmsAsync()
        {
            _logger.LogWarning("正在重置所有报警...");

            var alarmIds = _activeAlarms.Keys.ToList();

            foreach (var alid in alarmIds)
            {
                await ClearAlarmAsync(alid);
            }

            _statistics.Reset();

            _logger.LogInformation($"已重置 {alarmIds.Count} 个报警");
        }

        /// <summary>
        /// 获取报警历史
        /// </summary>
        public Task<List<AlarmHistoryRecord>> GetAlarmHistoryAsync(
            DateTime? startTime = null,
            DateTime? endTime = null,
            uint? alarmId = null)
        {
            _historyLock.EnterReadLock();
            try
            {
                var query = _alarmHistory.AsEnumerable();

                if (startTime.HasValue)
                {
                    query = query.Where(h => h.Timestamp >= startTime.Value);
                }

                if (endTime.HasValue)
                {
                    query = query.Where(h => h.Timestamp <= endTime.Value);
                }

                if (alarmId.HasValue)
                {
                    query = query.Where(h => h.AlarmId == alarmId.Value);
                }

                return Task.FromResult(query.OrderByDescending(h => h.Timestamp).ToList());
            }
            finally
            {
                _historyLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取报警统计信息
        /// </summary>
        public Task<AlarmStatistics> GetStatisticsAsync()
        {
            return Task.FromResult(_statistics.Clone());
        }

        #endregion

        #region 私有方法 - PLC监控

        /// <summary>
        /// 监控PLC报警
        /// </summary>
        /// <summary>
        /// 监控PLC报警
        /// </summary>
        private async Task MonitorPlcAlarms()
        {
            if (_plcProvider == null || !_plcProvider.IsConnected)
            {
                return;
            }

            try
            {
                // 定义PLC报警映射
                var plcAlarmMappings = new Dictionary<string, uint>
        {
            { "M202", 1001 },              // 急停
            { "M203", 1002 },              // 安全门
            { "M210", 1003 },              // 气压低
            { "M211", 1004 },              // 真空异常
            { "M212", 1005 },              // 冷却水异常
            { "M213", 1006 },              // 主轴过载
            { "M214", 1007 },              // 伺服报警
            { "M215", 1008 },              // 电源异常
            { "M220", 2001 },              // 温度高
            { "M221", 2002 },              // 温度低
            { "M222", 2003 },              // 压力高
            { "M223", 2004 },              // 压力低
            { "M230", 3001 },              // 划刀寿命
            { "M231", 3002 },              // 裂刀寿命
            { "M232", 3003 },              // 划刀断刀
            { "M233", 3004 },              // 裂刀断刀
            { "M240", 4001 },              // 晶圆破损
            { "M241", 4002 },              // 晶圆定位失败
            { "M250", 5001 }               // PLC通信错误
        };

                // 逐个读取报警标志
                foreach (var mapping in plcAlarmMappings)
                {
                    var plcAddress = mapping.Key;
                    var alarmId = mapping.Value;

                    // 使用ReadSvidAsync读取布尔值
                    var value = await _plcProvider.ReadSvidAsync(alarmId, plcAddress);
                    bool isActive = false;

                    if (value is bool boolValue)
                    {
                        isActive = boolValue;
                    }
                    else if (value != null)
                    {
                        // 尝试转换其他类型到布尔值
                        isActive = Convert.ToBoolean(value);
                    }

                    await ProcessPlcAlarm(alarmId, isActive);
                }

                // 检查模拟量报警（温度、压力等）
                await CheckAnalogAlarms();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC报警监控异常");
            }
        }

        /// <summary>
        /// 处理PLC报警
        /// </summary>
        private async Task ProcessPlcAlarm(uint alarmId, bool isActive)
        {
            if (isActive && !_activeAlarms.ContainsKey(alarmId))
            {
                // 触发报警
                var alarmText = GetAlarmText(alarmId);
                await SetAlarmAsync(alarmId, alarmText);
            }
            else if (!isActive && _activeAlarms.ContainsKey(alarmId))
            {
                // 清除报警
                await ClearAlarmAsync(alarmId);
            }
        }

        /// <summary>
        /// 检查模拟量报警
        /// </summary>
        private async Task CheckAnalogAlarms()
        {
            if (_plcProvider == null)
            {
                return;
            }

            try
            {
                // 温度检查 - 使用SVID 2003
                var tempValue = await _plcProvider.ReadSvidAsync(2003, "D204");
                float temperature = 0;

                if (tempValue is float floatTemp)
                {
                    temperature = floatTemp;
                }
                else if (tempValue != null)
                {
                    temperature = Convert.ToSingle(tempValue);
                }

                if (temperature > 80)
                {
                    await SetAlarmAsync(2001, $"Temperature too high: {temperature}°C");
                }
                else if (temperature < 10 && temperature > 0)
                {
                    await SetAlarmAsync(2002, $"Temperature too low: {temperature}°C");
                }
                else if (_activeAlarms.ContainsKey(2001))
                {
                    await ClearAlarmAsync(2001);
                }
                else if (_activeAlarms.ContainsKey(2002))
                {
                    await ClearAlarmAsync(2002);
                }

                // 压力检查 - 使用SVID 2002
                var pressureValue = await _plcProvider.ReadSvidAsync(2002, "D202");
                float pressure = 0;

                if (pressureValue is float floatPressure)
                {
                    pressure = floatPressure;
                }
                else if (pressureValue != null)
                {
                    pressure = Convert.ToSingle(pressureValue);
                }

                if (pressure > 100)
                {
                    await SetAlarmAsync(2003, $"Pressure too high: {pressure}kPa");
                }
                else if (pressure < 20 && pressure > 0)
                {
                    await SetAlarmAsync(2004, $"Pressure too low: {pressure}kPa");
                }
                else if (_activeAlarms.ContainsKey(2003))
                {
                    await ClearAlarmAsync(2003);
                }
                else if (_activeAlarms.ContainsKey(2004))
                {
                    await ClearAlarmAsync(2004);
                }

                // 刀具寿命检查 - 使用SVID 3001和3002
                var scribeUsageValue = await _plcProvider.ReadSvidAsync(3001, "D302");
                var scribeLimitValue = await _plcProvider.ReadSvidAsync(3001, "D306");

                int scribeUsage = 0;
                int scribeLimit = 10000;

                if (scribeUsageValue is int intScribeUsage)
                {
                    scribeUsage = intScribeUsage;
                }
                else if (scribeUsageValue != null)
                {
                    scribeUsage = Convert.ToInt32(scribeUsageValue);
                }

                if (scribeLimitValue is int intScribeLimit)
                {
                    scribeLimit = intScribeLimit;
                }
                else if (scribeLimitValue != null)
                {
                    scribeLimit = Convert.ToInt32(scribeLimitValue);
                }

                if (scribeUsage >= scribeLimit)
                {
                    await SetAlarmAsync(3001, $"Scribe knife life expired: {scribeUsage}/{scribeLimit}");
                }
                else if (_activeAlarms.ContainsKey(3001))
                {
                    await ClearAlarmAsync(3001);
                }

                // 裂刀寿命检查
                var breakUsageValue = await _plcProvider.ReadSvidAsync(3002, "D304");
                var breakLimitValue = await _plcProvider.ReadSvidAsync(3002, "D308");

                int breakUsage = 0;
                int breakLimit = 10000;

                if (breakUsageValue is int intBreakUsage)
                {
                    breakUsage = intBreakUsage;
                }
                else if (breakUsageValue != null)
                {
                    breakUsage = Convert.ToInt32(breakUsageValue);
                }

                if (breakLimitValue is int intBreakLimit)
                {
                    breakLimit = intBreakLimit;
                }
                else if (breakLimitValue != null)
                {
                    breakLimit = Convert.ToInt32(breakLimitValue);
                }

                if (breakUsage >= breakLimit)
                {
                    await SetAlarmAsync(3002, $"Break knife life expired: {breakUsage}/{breakLimit}");
                }
                else if (_activeAlarms.ContainsKey(3002))
                {
                    await ClearAlarmAsync(3002);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模拟量报警检查失败");
            }
        }

        /// <summary>
        /// 获取PLC地址
        /// </summary>
        private string GetPlcAddress(string tagName)
        {
            return tagName switch
            {
                "EMO" => "M202",
                "DoorOpen" => "M203",
                "AirPressureLow" => "M210",
                "VacuumError" => "M211",
                "CoolingWaterError" => "M212",
                "SpindleOverload" => "M213",
                "ServoAlarm" => "M214",
                "PowerError" => "M215",
                "TempHigh" => "M220",
                "TempLow" => "M221",
                "PressureHigh" => "M222",
                "PressureLow" => "M223",
                "ScribeKnifeLife" => "M230",
                "BreakKnifeLife" => "M231",
                "ScribeKnifeBroken" => "M232",
                "BreakKnifeBroken" => "M233",
                "WaferBroken" => "M240",
                "WaferAlignFail" => "M241",
                "PlcCommError" => "M250",
                _ => "M200"
            };
        }

        #endregion

        #region 私有方法 - 报警管理

        /// <summary>
        /// 初始化报警定义
        /// </summary>
        private void InitializeAlarmDefinitions()
        {
            // 系统报警 (1xxx)
            AddAlarmDefinition(1001, "EMO_PRESSED", "Emergency stop button pressed",
                AlarmCategory.System, AlarmPriority.Critical);
            AddAlarmDefinition(1002, "SAFETY_DOOR_OPEN", "Safety door is open",
                AlarmCategory.System, AlarmPriority.Critical);
            AddAlarmDefinition(1003, "AIR_PRESSURE_LOW", "Air pressure is too low",
                AlarmCategory.System, AlarmPriority.High);
            AddAlarmDefinition(1004, "VACUUM_ERROR", "Vacuum system error",
                AlarmCategory.System, AlarmPriority.High);
            AddAlarmDefinition(1005, "COOLING_WATER_ERROR", "Cooling water flow error",
                AlarmCategory.System, AlarmPriority.High);
            AddAlarmDefinition(1006, "SPINDLE_OVERLOAD", "Spindle motor overload",
                AlarmCategory.System, AlarmPriority.High);
            AddAlarmDefinition(1007, "SERVO_ALARM", "Servo drive alarm",
                AlarmCategory.System, AlarmPriority.High);
            AddAlarmDefinition(1008, "POWER_ERROR", "Power supply error",
                AlarmCategory.System, AlarmPriority.Critical);

            // 工艺报警 (2xxx)
            AddAlarmDefinition(2001, "TEMPERATURE_HIGH", "Process temperature too high",
                AlarmCategory.Process, AlarmPriority.Medium);
            AddAlarmDefinition(2002, "TEMPERATURE_LOW", "Process temperature too low",
                AlarmCategory.Process, AlarmPriority.Medium);
            AddAlarmDefinition(2003, "PRESSURE_HIGH", "Process pressure too high",
                AlarmCategory.Process, AlarmPriority.Medium);
            AddAlarmDefinition(2004, "PRESSURE_LOW", "Process pressure too low",
                AlarmCategory.Process, AlarmPriority.Medium);
            AddAlarmDefinition(2005, "SPEED_ERROR", "Process speed error",
                AlarmCategory.Process, AlarmPriority.Medium);
            AddAlarmDefinition(2006, "CUT_DEPTH_ERROR", "Cut depth error",
                AlarmCategory.Process, AlarmPriority.Medium);

            // 刀具报警 (3xxx)
            AddAlarmDefinition(3001, "SCRIBE_KNIFE_LIFE_END", "Scribe knife life expired",
                AlarmCategory.Knife, AlarmPriority.Medium);
            AddAlarmDefinition(3002, "BREAK_KNIFE_LIFE_END", "Break knife life expired",
                AlarmCategory.Knife, AlarmPriority.Medium);
            AddAlarmDefinition(3003, "SCRIBE_KNIFE_BROKEN", "Scribe knife broken detected",
                AlarmCategory.Knife, AlarmPriority.Critical);
            AddAlarmDefinition(3004, "BREAK_KNIFE_BROKEN", "Break knife broken detected",
                AlarmCategory.Knife, AlarmPriority.Critical);

            // 材料报警 (4xxx)
            AddAlarmDefinition(4001, "WAFER_BROKEN", "Wafer broken detected",
                AlarmCategory.Material, AlarmPriority.Critical);
            AddAlarmDefinition(4002, "WAFER_ALIGN_FAIL", "Wafer alignment failed",
                AlarmCategory.Material, AlarmPriority.Medium);
            AddAlarmDefinition(4003, "WAFER_ID_READ_FAIL", "Wafer ID read failed",
                AlarmCategory.Material, AlarmPriority.Low);
            AddAlarmDefinition(4004, "CASSETTE_NOT_PRESENT", "Cassette not present",
                AlarmCategory.Material, AlarmPriority.Medium);

            // 通信报警 (5xxx)
            AddAlarmDefinition(5001, "PLC_COMM_ERROR", "PLC communication error",
                AlarmCategory.Communication, AlarmPriority.High);
            AddAlarmDefinition(5002, "SENSOR_OFFLINE", "Sensor offline",
                AlarmCategory.Communication, AlarmPriority.Medium);

            // 维护提醒 (6xxx)
            AddAlarmDefinition(6001, "DAILY_MAINTENANCE_DUE", "Daily maintenance due",
                AlarmCategory.Maintenance, AlarmPriority.Low);
            AddAlarmDefinition(6002, "WEEKLY_MAINTENANCE_DUE", "Weekly maintenance due",
                AlarmCategory.Maintenance, AlarmPriority.Low);
            AddAlarmDefinition(6003, "MONTHLY_MAINTENANCE_DUE", "Monthly maintenance due",
                AlarmCategory.Maintenance, AlarmPriority.Low);
        }

        /// <summary>
        /// 添加报警定义
        /// </summary>
        private void AddAlarmDefinition(uint id, string name, string description,
            AlarmCategory category, AlarmPriority priority)
        {
            _alarmDefinitions[id] = new AlarmDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Category = category,
                Priority = priority
            };
        }

        /// <summary>
        /// 创建默认定义
        /// </summary>
        private AlarmDefinition CreateDefaultDefinition(uint alarmId, string alarmText)
        {
            return new AlarmDefinition
            {
                Id = alarmId,
                Name = $"ALARM_{alarmId}",
                Description = alarmText,
                Category = GetAlarmCategory(alarmId),
                Priority = AlarmPriority.Medium
            };
        }

        /// <summary>
        /// 获取报警文本
        /// </summary>
        private string GetAlarmText(uint alarmId)
        {
            return _alarmDefinitions.TryGetValue(alarmId, out var def)
                ? def.Description
                : $"Alarm {alarmId}";
        }

        /// <summary>
        /// 获取报警优先级
        /// </summary>
        private int GetAlarmPriority(uint alarmId)
        {
            if (_alarmDefinitions.TryGetValue(alarmId, out var def))
            {
                return (int)def.Priority;
            }

            return alarmId switch
            {
                >= 1000 and < 2000 => 1,
                >= 2000 and < 3000 => 3,
                >= 3000 and < 4000 => 2,
                >= 4000 and < 5000 => 3,
                >= 5000 and < 6000 => 2,
                >= 6000 and < 7000 => 4,
                _ => 5
            };
        }

        /// <summary>
        /// 获取报警类别
        /// </summary>
        private AlarmCategory GetAlarmCategory(uint alarmId)
        {
            return alarmId switch
            {
                >= 1000 and < 2000 => AlarmCategory.System,
                >= 2000 and < 3000 => AlarmCategory.Process,
                >= 3000 and < 4000 => AlarmCategory.Knife,
                >= 4000 and < 5000 => AlarmCategory.Material,
                >= 5000 and < 6000 => AlarmCategory.Communication,
                >= 6000 and < 7000 => AlarmCategory.Maintenance,
                _ => AlarmCategory.Other
            };
        }

        #endregion

        #region 私有方法 - 事件和通知

        /// <summary>
        /// 发送报警报告
        /// </summary>
        private async Task SendAlarmReport(uint alarmId, byte alarmCode, string alarmText)
        {
            if (_s5f1Handler != null)
            {
                await _s5f1Handler.SendAlarmReportAsync(alarmId, alarmCode, alarmText);
            }
        }

        /// <summary>
        /// 触发报警事件
        /// </summary>
        private async Task TriggerAlarmEvent(uint alarmId, AlarmEventType eventType)
        {
            // 获取报警文本
            var alarmText = GetAlarmText(alarmId);

            // 触发新的事件（用于接口兼容）
            var eventArgs = new Interfaces.AlarmEventArgs
            {
                AlarmId = alarmId,
                AlarmText = alarmText,
                Timestamp = DateTime.Now
            };

            if (eventType == AlarmEventType.Set)
            {
                AlarmOccurred?.Invoke(this, eventArgs);
            }
            else if (eventType == AlarmEventType.Clear)
            {
                AlarmCleared?.Invoke(this, eventArgs);
            }

            // 原有的EventBus发布逻辑
            if (_eventBus != null)
            {
                var alarmEvent = new AlarmEvent
                {
                    AlarmId = alarmId,
                    EventType = eventType,
                    AlarmText = alarmText,
                    Category = GetAlarmCategory(alarmId)
                };

                await _eventBus.PublishAsync(alarmEvent);
            }
        }

        /// <summary>
        /// 处理严重报警
        /// </summary>
        private async Task HandleCriticalAlarm(uint alarmId, AlarmRecord record)
        {
            _logger.LogCritical($"严重报警触发 - ID: {alarmId}, 文本: {record.AlarmText}");

            // 可以在这里添加特殊处理逻辑
            // 例如：停止设备、发送紧急通知等
            await Task.CompletedTask;
        }

        #endregion

        #region 私有方法 - 历史和统计

        /// <summary>
        /// 添加到历史记录
        /// </summary>
        private void AddToHistory(uint alarmId, AlarmAction action, string alarmText)
        {
            _historyLock.EnterWriteLock();
            try
            {
                var record = new AlarmHistoryRecord
                {
                    AlarmId = alarmId,
                    Action = action,
                    AlarmText = alarmText,
                    Timestamp = DateTime.Now
                };

                _alarmHistory.Add(record);

                // 限制历史记录数量
                if (_alarmHistory.Count > MaxHistoryRecords)
                {
                    _alarmHistory.RemoveAt(0);
                }
            }
            finally
            {
                _historyLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        private void UpdateStatistics(uint alarmId, bool isSet)
        {
            var category = GetAlarmCategory(alarmId);

            if (isSet)
            {
                _statistics.TotalAlarms++;
                _statistics.ActiveAlarms++;
                _statistics.CategoryCounts[category]++;
            }
            else
            {
                _statistics.ActiveAlarms--;
                _statistics.CategoryCounts[category]--;
            }

            _statistics.LastUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// 清理过期报警
        /// </summary>
        private async Task CleanupExpiredAlarms()
        {
            try
            {
                // 清理历史记录（保留最近7天）
                _historyLock.EnterWriteLock();
                try
                {
                    var cutoffTime = DateTime.Now.AddDays(-7);
                    _alarmHistory.RemoveAll(h => h.Timestamp < cutoffTime);
                }
                finally
                {
                    _historyLock.ExitWriteLock();
                }

                // 自动清除某些长时间未处理的低优先级报警
                var expiredAlarms = _activeAlarms.Values
                    .Where(a => a.Priority == AlarmPriority.Low &&
                               DateTime.Now - a.SetTime > TimeSpan.FromHours(24))
                    .ToList();

                foreach (var alarm in expiredAlarms)
                {
                    await ClearAlarmAsync(alarm.AlarmId);
                    _logger.LogInformation($"自动清除过期的低优先级报警: {alarm.AlarmId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期报警失败");
            }
        }

        /// <summary>
        /// 保存报警状态
        /// </summary>
        private async Task SaveAlarmStateAsync()
        {
            try
            {
                // 这里可以实现持久化逻辑
                // 例如：保存到数据库或文件
                _logger.LogInformation($"保存报警状态 - 活动报警: {_activeAlarms.Count}");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存报警状态失败");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _plcMonitorTimer?.Dispose();
            _cleanupTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _historyLock?.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 报警记录
        /// </summary>
        private class AlarmRecord
        {
            public uint AlarmId { get; set; }
            public string AlarmText { get; set; } = "";
            public DateTime SetTime { get; set; }
            public AlarmPriority Priority { get; set; }
            public AlarmCategory Category { get; set; }
            public bool IsAcknowledged { get; set; }
            public DateTime? AcknowledgeTime { get; set; }
        }

        /// <summary>
        /// 报警定义
        /// </summary>
        private class AlarmDefinition
        {
            public uint Id { get; set; }
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public AlarmCategory Category { get; set; }
            public AlarmPriority Priority { get; set; }
            public string PLCAddr { get; set; } = "";
        }

        /// <summary>
        /// 报警历史记录
        /// </summary>
        public class AlarmHistoryRecord
        {
            public uint AlarmId { get; set; }
            public AlarmAction Action { get; set; }
            public string AlarmText { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// 报警统计信息
        /// </summary>
        public class AlarmStatistics
        {
            public int TotalAlarms { get; set; }
            public int ActiveAlarms { get; set; }
            public Dictionary<AlarmCategory, int> CategoryCounts { get; set; }
            public DateTime LastUpdateTime { get; set; }

            public AlarmStatistics()
            {
                CategoryCounts = new Dictionary<AlarmCategory, int>();
                foreach (AlarmCategory category in Enum.GetValues(typeof(AlarmCategory)))
                {
                    CategoryCounts[category] = 0;
                }
            }

            public AlarmStatistics Clone()
            {
                return new AlarmStatistics
                {
                    TotalAlarms = this.TotalAlarms,
                    ActiveAlarms = this.ActiveAlarms,
                    CategoryCounts = new Dictionary<AlarmCategory, int>(this.CategoryCounts),
                    LastUpdateTime = this.LastUpdateTime
                };
            }

            public void Reset()
            {
                TotalAlarms = 0;
                ActiveAlarms = 0;
                foreach (var key in CategoryCounts.Keys.ToList())
                {
                    CategoryCounts[key] = 0;
                }
                LastUpdateTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 报警类别
        /// </summary>
        public enum AlarmCategory
        {
            System,
            Process,
            Knife,
            Material,
            Communication,
            Maintenance,
            Other
        }

        /// <summary>
        /// 报警优先级
        /// </summary>
        public enum AlarmPriority
        {
            Critical = 1,
            High = 2,
            Medium = 3,
            Low = 4
        }

        /// <summary>
        /// 报警动作
        /// </summary>
        public enum AlarmAction
        {
            Set,
            Clear,
            Acknowledge
        }

        /// <summary>
        /// 报警事件类型
        /// </summary>
        public enum AlarmEventType
        {
            Set,
            Clear,
            Acknowledge
        }

        /// <summary>
        /// 报警事件
        /// </summary>
        public class AlarmEvent : IEvent
        {
            public Guid EventId { get; } = Guid.NewGuid();
            public DateTime Timestamp { get; } = DateTime.Now;
            public string? Source => "AlarmService";

            public uint AlarmId { get; set; }
            public AlarmEventType EventType { get; set; }
            public string AlarmText { get; set; } = "";
            public AlarmCategory Category { get; set; }
        }

        #endregion
    }
}
