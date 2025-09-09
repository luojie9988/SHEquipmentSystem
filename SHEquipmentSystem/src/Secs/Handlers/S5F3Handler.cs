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
    /// S5F3 (Enable/Disable Alarm Send) 启用/禁用报警发送处理器
    /// 处理主机的报警启用/禁用请求，控制哪些报警在发生时会触发S5F1报告
    /// </summary>
    /// <remarks>
    /// SEMI E5/E30 标准定义：
    /// - S5F3: Enable/Disable Alarm Send - 主机控制报警报告的发送
    /// - S5F4: Enable/Disable Alarm Acknowledge - 设备返回操作结果
    /// 
    /// 交互流程：
    /// 1. 主机发送S5F3包含报警控制代码(ALED)和报警ID列表(ALID)
    /// 2. 设备验证ALID的有效性
    /// 3. 根据ALED更新报警的启用/禁用状态
    /// 4. 返回S5F4确认结果(ACKC5)
    /// 5. 后续该报警发生时，根据启用状态决定是否发送S5F1
    /// 
    /// ALED控制代码：
    /// - 0x00: 禁用列表中的报警
    /// - 0x80 (128): 启用列表中的报警
    /// - 0x81 (129): 禁用所有报警，然后启用列表中的报警
    /// - 0x82 (130): 启用所有报警，然后禁用列表中的报警
    /// 
    /// 划裂片设备报警管理策略：
    /// - 默认启用所有安全相关报警（不可禁用）
    /// - 工艺报警可根据需要启用/禁用
    /// - 维护提醒类报警默认禁用
    /// - 支持批量配置和单个配置
    /// - 配置持久化到内存（重启后需要重新配置）
    /// 
    /// 与Host端匹配要点：
    /// - 支持所有ALED控制模式
    /// - 空ALID列表的特殊处理
    /// - 无效ALID的错误处理
    /// - 与S5F1处理器共享启用状态
    /// </remarks>
    public class S5F3Handler : SecsMessageHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// 报警启用/禁用控制代码 (ALED) 定义
        /// </summary>
        public enum AlarmEnableCode : byte
        {
            /// <summary>禁用列表中的报警</summary>
            DisableList = 0x00,

            /// <summary>启用列表中的报警</summary>
            EnableList = 0x80,

            /// <summary>禁用所有，然后启用列表中的报警</summary>
            DisableAllThenEnableList = 0x81,

            /// <summary>启用所有，然后禁用列表中的报警</summary>
            EnableAllThenDisableList = 0x82
        }

        /// <summary>
        /// 报警确认代码 (ACKC5) 定义
        /// </summary>
        public enum AlarmAcknowledge : byte
        {
            /// <summary>已接受</summary>
            Accepted = 0,

            /// <summary>错误，不接受</summary>
            Error = 1,

            /// <summary>至少一个ALID无效</summary>
            InvalidAlarmId = 2
        }

        #endregion

        #region 私有字段

        /// <summary>S5F1处理器引用（用于共享报警启用状态）</summary>
        private readonly IS5F1Handler? _s5f1Handler;

        /// <summary>报警服务</summary>
        private readonly IAlarmService _alarmService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService? _eventService;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>所有定义的报警ID集合</summary>
        private readonly HashSet<uint> _definedAlarmIds;

        /// <summary>不可禁用的报警ID集合（安全相关）</summary>
        private readonly HashSet<uint> _mandatoryAlarmIds;

        /// <summary>当前启用的报警ID集合</summary>
        private readonly HashSet<uint> _enabledAlarmIds;

        /// <summary>状态锁</summary>
        private readonly ReaderWriterLockSlim _stateLock = new();

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 5;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 3;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="alarmService">报警服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="s5f1Handler">S5F1处理器（可选）</param>
        /// <param name="eventService">事件报告服务（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S5F3Handler(
            ILogger<S5F3Handler> logger,
            IAlarmService alarmService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            IS5F1Handler? s5f1Handler = null,
            IEventReportService? eventService = null) : base(logger)
        {
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _s5f1Handler = s5f1Handler;
            _eventService = eventService;

            _definedAlarmIds = new HashSet<uint>();
            _mandatoryAlarmIds = new HashSet<uint>();
            _enabledAlarmIds = new HashSet<uint>();

            // 初始化报警定义
            InitializeAlarmDefinitions();

            Logger.LogInformation($"S5F3处理器已初始化，定义了 {_definedAlarmIds.Count} 个报警，" +
                                $"其中 {_mandatoryAlarmIds.Count} 个为强制启用");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S5F3消息，返回S5F4响应
        /// </summary>
        /// <param name="message">接收到的S5F3消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S5F4响应消息</returns>
        /// <remarks>
        /// S5F3 消息格式:
        /// L,2
        ///   1. &lt;ALED&gt;   报警启用/禁用代码 (B,1)
        ///   2. L,n      报警ID列表
        ///      1. &lt;ALID1&gt;
        ///      ...
        ///      n. &lt;ALIDn&gt;
        /// 
        /// 特殊情况：
        /// - 空ALID列表时，根据ALED操作所有报警
        /// - 无效的ALID将导致返回错误码
        /// - 强制报警不能被禁用
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S5F3 (Enable/Disable Alarm Send) 报警启用/禁用请求");

            try
            {
                // 解析消息
                var (aled, alarmIds, isValid) = ParseS5F3Message(message.SecsItem);

                if (!isValid)
                {
                    Logger.LogWarning("S5F3消息格式无效");
                    return CreateS5F4Response(AlarmAcknowledge.Error);
                }

                Logger.LogDebug($"报警控制请求 - ALED: 0x{aled:X2}, ALID数量: {alarmIds.Count}");

                // 验证报警ID的有效性
                var validationResult = ValidateAlarmIds(alarmIds);
                if (!validationResult.IsValid)
                {
                    Logger.LogWarning($"存在无效的报警ID: {string.Join(", ", validationResult.InvalidIds)}");
                    return CreateS5F4Response(AlarmAcknowledge.InvalidAlarmId);
                }

                // 执行报警启用/禁用操作
                var operationResult = await ExecuteAlarmControl(aled, alarmIds, cancellationToken);

                if (operationResult.Success)
                {
                    Logger.LogInformation($"报警控制操作成功 - ALED: 0x{aled:X2}, " +
                                        $"影响的报警数: {operationResult.AffectedCount}");

                    // 触发配置变更事件
                    await TriggerConfigurationChangeEvent(aled, alarmIds);

                    return CreateS5F4Response(AlarmAcknowledge.Accepted);
                }
                else
                {
                    Logger.LogError($"报警控制操作失败: {operationResult.ErrorMessage}");
                    return CreateS5F4Response(AlarmAcknowledge.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S5F3消息失败");
                return CreateS5F4Response(AlarmAcknowledge.Error);
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析S5F3消息
        /// </summary>
        private (byte aled, List<uint> alarmIds, bool isValid) ParseS5F3Message(Item? item)
        {
            try
            {
                if (item == null || item.Format != SecsFormat.List || item.Count != 2)
                {
                    Logger.LogWarning("S5F3消息结构无效");
                    return (0, new List<uint>(), false);
                }

                // 解析ALED
                var aledItem = item.Items?[0];
                if (aledItem == null || aledItem.Format != SecsFormat.Binary)
                {
                    Logger.LogWarning("ALED格式无效");
                    return (0, new List<uint>(), false);
                }
                var aled = aledItem.FirstValue<byte>();

                // 解析ALID列表
                var alarmIds = new List<uint>();
                var alidListItem = item.Items?[1];

                if (alidListItem != null && alidListItem.Format == SecsFormat.List)
                {
                    foreach (var alidItem in alidListItem.Items ?? Array.Empty<Item>())
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
                            return (0, new List<uint>(), false);
                        }
                    }
                }

                return (aled, alarmIds, true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S5F3消息异常");
                return (0, new List<uint>(), false);
            }
        }

        /// <summary>
        /// 创建S5F4响应
        /// </summary>
        private SecsMessage CreateS5F4Response(AlarmAcknowledge ackc5)
        {
            return new SecsMessage(5, 4, false)
            {
                Name = "EnableDisableAlarmAcknowledge",
                SecsItem = B((byte)ackc5)
            };
        }

        #endregion

        #region 私有方法 - 报警控制

        /// <summary>
        /// 执行报警控制操作
        /// </summary>
        private async Task<AlarmControlResult> ExecuteAlarmControl(
            byte aled, List<uint> alarmIds, CancellationToken cancellationToken)
        {
            try
            {
                _stateLock.EnterWriteLock();

                var affectedCount = 0;
                var operation = (AlarmEnableCode)aled;

                Logger.LogDebug($"执行报警控制 - 操作: {operation}");

                switch (operation)
                {
                    case AlarmEnableCode.DisableList:
                        affectedCount = DisableAlarms(alarmIds);
                        break;

                    case AlarmEnableCode.EnableList:
                        affectedCount = EnableAlarms(alarmIds);
                        break;

                    case AlarmEnableCode.DisableAllThenEnableList:
                        DisableAllAlarms();
                        affectedCount = EnableAlarms(alarmIds);
                        break;

                    case AlarmEnableCode.EnableAllThenDisableList:
                        EnableAllAlarms();
                        affectedCount = DisableAlarms(alarmIds);
                        break;

                    default:
                        return new AlarmControlResult
                        {
                            Success = false,
                            ErrorMessage = $"不支持的ALED代码: 0x{aled:X2}"
                        };
                }

                // 同步到S5F1处理器
                if (_s5f1Handler != null)
                {
                    SyncToS5F1Handler();
                }

                // 记录当前状态
                LogCurrentAlarmState();

                return new AlarmControlResult
                {
                    Success = true,
                    AffectedCount = affectedCount
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行报警控制异常");
                return new AlarmControlResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                if (_stateLock.IsWriteLockHeld)
                {
                    _stateLock.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// 启用报警
        /// </summary>
        private int EnableAlarms(List<uint> alarmIds)
        {
            int count = 0;

            // 如果列表为空，启用所有定义的报警
            var targetAlarms = alarmIds.Count == 0 ? _definedAlarmIds.ToList() : alarmIds;

            foreach (var alid in targetAlarms)
            {
                if (_definedAlarmIds.Contains(alid) && !_enabledAlarmIds.Contains(alid))
                {
                    _enabledAlarmIds.Add(alid);
                    count++;
                    Logger.LogDebug($"已启用报警: {alid} - {GetAlarmName(alid)}");
                }
            }

            return count;
        }

        /// <summary>
        /// 禁用报警
        /// </summary>
        private int DisableAlarms(List<uint> alarmIds)
        {
            int count = 0;

            // 如果列表为空，禁用所有可禁用的报警
            var targetAlarms = alarmIds.Count == 0 ? _definedAlarmIds.ToList() : alarmIds;

            foreach (var alid in targetAlarms)
            {
                // 检查是否为强制报警（不可禁用）
                if (_mandatoryAlarmIds.Contains(alid))
                {
                    Logger.LogWarning($"报警 {alid} 为强制报警，不能禁用");
                    continue;
                }

                if (_definedAlarmIds.Contains(alid) && _enabledAlarmIds.Contains(alid))
                {
                    _enabledAlarmIds.Remove(alid);
                    count++;
                    Logger.LogDebug($"已禁用报警: {alid} - {GetAlarmName(alid)}");
                }
            }

            return count;
        }

        /// <summary>
        /// 启用所有报警
        /// </summary>
        private void EnableAllAlarms()
        {
            _enabledAlarmIds.Clear();
            foreach (var alid in _definedAlarmIds)
            {
                _enabledAlarmIds.Add(alid);
            }
            Logger.LogDebug($"已启用所有 {_enabledAlarmIds.Count} 个报警");
        }

        /// <summary>
        /// 禁用所有报警
        /// </summary>
        private void DisableAllAlarms()
        {
            _enabledAlarmIds.Clear();

            // 重新添加强制报警
            foreach (var alid in _mandatoryAlarmIds)
            {
                _enabledAlarmIds.Add(alid);
            }

            Logger.LogDebug($"已禁用所有可禁用报警，保留 {_mandatoryAlarmIds.Count} 个强制报警");
        }

        /// <summary>
        /// 同步到S5F1处理器
        /// </summary>
        private void SyncToS5F1Handler()
        {
            if (_s5f1Handler == null)
            {
                return;
            }

            try
            {
                // 获取当前S5F1处理器中启用的报警
                var currentEnabled = _s5f1Handler.GetEnabledAlarms().ToHashSet();

                // 找出需要启用的报警
                var toEnable = _enabledAlarmIds.Except(currentEnabled).ToList();
                if (toEnable.Count > 0)
                {
                    _s5f1Handler.EnableAlarms(toEnable);
                }

                // 找出需要禁用的报警
                var toDisable = currentEnabled.Except(_enabledAlarmIds).ToList();
                if (toDisable.Count > 0)
                {
                    _s5f1Handler.DisableAlarms(toDisable);
                }

                Logger.LogDebug($"已同步报警状态到S5F1处理器 - 启用: {toEnable.Count}, 禁用: {toDisable.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步报警状态到S5F1处理器失败");
            }
        }

        #endregion

        #region 私有方法 - 验证

        /// <summary>
        /// 验证报警ID的有效性
        /// </summary>
        private AlarmValidationResult ValidateAlarmIds(List<uint> alarmIds)
        {
            var result = new AlarmValidationResult { IsValid = true };

            // 空列表是有效的（表示所有报警）
            if (alarmIds.Count == 0)
            {
                return result;
            }

            foreach (var alid in alarmIds)
            {
                if (!_definedAlarmIds.Contains(alid))
                {
                    result.IsValid = false;
                    result.InvalidIds.Add(alid);
                    Logger.LogWarning($"无效的报警ID: {alid}");
                }
            }

            return result;
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化报警定义
        /// </summary>
        private void InitializeAlarmDefinitions()
        {
            // 系统报警 (1000-1999)
            AddAlarmDefinition(1001, "EMO_PRESSED", true);
            AddAlarmDefinition(1002, "SAFETY_DOOR_OPEN", true);
            AddAlarmDefinition(1003, "AIR_PRESSURE_LOW", true);
            AddAlarmDefinition(1004, "VACUUM_ERROR", true);
            AddAlarmDefinition(1005, "COOLING_WATER_ERROR", true);
            AddAlarmDefinition(1006, "SPINDLE_OVERLOAD", true);
            AddAlarmDefinition(1007, "SERVO_ALARM", true);
            AddAlarmDefinition(1008, "POWER_ERROR", true);

            // 工艺报警 (2000-2999)
            AddAlarmDefinition(2001, "TEMPERATURE_HIGH", false);
            AddAlarmDefinition(2002, "TEMPERATURE_LOW", false);
            AddAlarmDefinition(2003, "PRESSURE_HIGH", false);
            AddAlarmDefinition(2004, "PRESSURE_LOW", false);
            AddAlarmDefinition(2005, "SPEED_ERROR", false);
            AddAlarmDefinition(2006, "CUT_DEPTH_ERROR", false);
            AddAlarmDefinition(2007, "PROCESS_PARAM_ERROR", false);

            // 刀具报警 (3000-3999)
            AddAlarmDefinition(3001, "SCRIBE_KNIFE_LIFE_END", false);
            AddAlarmDefinition(3002, "BREAK_KNIFE_LIFE_END", false);
            AddAlarmDefinition(3003, "SCRIBE_KNIFE_BROKEN", true);
            AddAlarmDefinition(3004, "BREAK_KNIFE_BROKEN", true);
            AddAlarmDefinition(3005, "KNIFE_NOT_INSTALLED", false);
            AddAlarmDefinition(3006, "KNIFE_TYPE_ERROR", false);
            AddAlarmDefinition(3007, "KNIFE_CHANGE_REQUIRED", false);

            // 材料报警 (4000-4999)
            AddAlarmDefinition(4001, "WAFER_BROKEN", true);
            AddAlarmDefinition(4002, "WAFER_ALIGN_FAIL", false);
            AddAlarmDefinition(4003, "WAFER_ID_READ_FAIL", false);
            AddAlarmDefinition(4004, "CASSETTE_NOT_PRESENT", false);
            AddAlarmDefinition(4005, "SLOT_MAP_ERROR", false);
            AddAlarmDefinition(4006, "MATERIAL_TYPE_MISMATCH", false);
            AddAlarmDefinition(4007, "WAFER_TRANSFER_ERROR", false);

            // 通信报警 (5000-5999)
            AddAlarmDefinition(5001, "PLC_COMM_ERROR", true);
            AddAlarmDefinition(5002, "SENSOR_OFFLINE", false);
            AddAlarmDefinition(5003, "VISION_SYSTEM_ERROR", false);
            AddAlarmDefinition(5004, "BARCODE_READER_ERROR", false);
            AddAlarmDefinition(5005, "NETWORK_ERROR", true);

            // 维护提醒 (6000-6999)
            AddAlarmDefinition(6001, "DAILY_MAINTENANCE_DUE", false);
            AddAlarmDefinition(6002, "WEEKLY_MAINTENANCE_DUE", false);
            AddAlarmDefinition(6003, "MONTHLY_MAINTENANCE_DUE", false);
            AddAlarmDefinition(6004, "CALIBRATION_DUE", false);
            AddAlarmDefinition(6005, "LUBRICATION_REMINDER", false);
            AddAlarmDefinition(6006, "FILTER_CLEAN_REMINDER", false);

            // 默认启用所有强制报警和部分重要报警
            foreach (var alid in _mandatoryAlarmIds)
            {
                _enabledAlarmIds.Add(alid);
            }

            // 默认启用部分非强制但重要的报警
            _enabledAlarmIds.Add(3001); // SCRIBE_KNIFE_LIFE_END
            _enabledAlarmIds.Add(3002); // BREAK_KNIFE_LIFE_END
            _enabledAlarmIds.Add(4002); // WAFER_ALIGN_FAIL

            Logger.LogInformation($"报警定义初始化完成 - 总数: {_definedAlarmIds.Count}, " +
                                $"强制: {_mandatoryAlarmIds.Count}, " +
                                $"默认启用: {_enabledAlarmIds.Count}");
        }

        /// <summary>
        /// 添加报警定义
        /// </summary>
        private void AddAlarmDefinition(uint alarmId, string name, bool mandatory)
        {
            _definedAlarmIds.Add(alarmId);

            if (mandatory)
            {
                _mandatoryAlarmIds.Add(alarmId);
            }
        }

        /// <summary>
        /// 获取报警名称
        /// </summary>
        private string GetAlarmName(uint alarmId)
        {
            return alarmId switch
            {
                1001 => "EMO_PRESSED",
                1002 => "SAFETY_DOOR_OPEN",
                1003 => "AIR_PRESSURE_LOW",
                1004 => "VACUUM_ERROR",
                1005 => "COOLING_WATER_ERROR",
                1006 => "SPINDLE_OVERLOAD",
                1007 => "SERVO_ALARM",
                1008 => "POWER_ERROR",
                2001 => "TEMPERATURE_HIGH",
                2002 => "TEMPERATURE_LOW",
                2003 => "PRESSURE_HIGH",
                2004 => "PRESSURE_LOW",
                2005 => "SPEED_ERROR",
                2006 => "CUT_DEPTH_ERROR",
                2007 => "PROCESS_PARAM_ERROR",
                3001 => "SCRIBE_KNIFE_LIFE_END",
                3002 => "BREAK_KNIFE_LIFE_END",
                3003 => "SCRIBE_KNIFE_BROKEN",
                3004 => "BREAK_KNIFE_BROKEN",
                3005 => "KNIFE_NOT_INSTALLED",
                3006 => "KNIFE_TYPE_ERROR",
                3007 => "KNIFE_CHANGE_REQUIRED",
                4001 => "WAFER_BROKEN",
                4002 => "WAFER_ALIGN_FAIL",
                4003 => "WAFER_ID_READ_FAIL",
                4004 => "CASSETTE_NOT_PRESENT",
                4005 => "SLOT_MAP_ERROR",
                4006 => "MATERIAL_TYPE_MISMATCH",
                4007 => "WAFER_TRANSFER_ERROR",
                5001 => "PLC_COMM_ERROR",
                5002 => "SENSOR_OFFLINE",
                5003 => "VISION_SYSTEM_ERROR",
                5004 => "BARCODE_READER_ERROR",
                5005 => "NETWORK_ERROR",
                6001 => "DAILY_MAINTENANCE_DUE",
                6002 => "WEEKLY_MAINTENANCE_DUE",
                6003 => "MONTHLY_MAINTENANCE_DUE",
                6004 => "CALIBRATION_DUE",
                6005 => "LUBRICATION_REMINDER",
                6006 => "FILTER_CLEAN_REMINDER",
                _ => $"ALARM_{alarmId}"
            };
        }

        #endregion

        #region 私有方法 - 事件和日志

        /// <summary>
        /// 触发配置变更事件
        /// </summary>
        private async Task TriggerConfigurationChangeEvent(byte aled, List<uint> alarmIds)
        {
            if (_eventService == null)
            {
                return;
            }

            try
            {
                // CEID 10003: 报警配置变更事件
                await _eventService.ReportEventAsync(10003,
                    "ALARM_CONFIG_CHANGE",
                    new Dictionary<uint, object>
                    {
                        { 1, aled },
                        { 2, alarmIds.Count },
                        { 3, _enabledAlarmIds.Count },
                        { 4, DateTime.Now }
                    });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "触发配置变更事件失败");
            }
        }

        /// <summary>
        /// 记录当前报警状态
        /// </summary>
        private void LogCurrentAlarmState()
        {
            try
            {
                _stateLock.EnterReadLock();

                var enabledCount = _enabledAlarmIds.Count;
                var disabledCount = _definedAlarmIds.Count - enabledCount;

                Logger.LogInformation($"当前报警状态 - 启用: {enabledCount}, 禁用: {disabledCount}");

                // 详细日志（调试级别）
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    var enabledList = string.Join(", ", _enabledAlarmIds.OrderBy(x => x).Take(10));
                    if (_enabledAlarmIds.Count > 10)
                    {
                        enabledList += $", ... (共{_enabledAlarmIds.Count}个)";
                    }
                    Logger.LogDebug($"启用的报警: {enabledList}");
                }
            }
            finally
            {
                if (_stateLock.IsReadLockHeld)
                {
                    _stateLock.ExitReadLock();
                }
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有定义的报警
        /// </summary>
        public IEnumerable<uint> GetDefinedAlarms()
        {
            _stateLock.EnterReadLock();
            try
            {
                return _definedAlarmIds.ToList();
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取当前启用的报警
        /// </summary>
        public IEnumerable<uint> GetEnabledAlarms()
        {
            _stateLock.EnterReadLock();
            try
            {
                return _enabledAlarmIds.ToList();
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取强制报警
        /// </summary>
        public IEnumerable<uint> GetMandatoryAlarms()
        {
            _stateLock.EnterReadLock();
            try
            {
                return _mandatoryAlarmIds.ToList();
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 检查报警是否启用
        /// </summary>
        public bool IsAlarmEnabled(uint alarmId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _enabledAlarmIds.Contains(alarmId);
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 检查报警是否为强制
        /// </summary>
        public bool IsAlarmMandatory(uint alarmId)
        {
            _stateLock.EnterReadLock();
            try
            {
                return _mandatoryAlarmIds.Contains(alarmId);
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            _stateLock?.Dispose();
            base.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 报警验证结果
        /// </summary>
        private class AlarmValidationResult
        {
            public bool IsValid { get; set; }
            public List<uint> InvalidIds { get; set; } = new();
        }

        /// <summary>
        /// 报警控制结果
        /// </summary>
        private class AlarmControlResult
        {
            public bool Success { get; set; }
            public int AffectedCount { get; set; }
            public string? ErrorMessage { get; set; }
        }

        #endregion
    }
}
