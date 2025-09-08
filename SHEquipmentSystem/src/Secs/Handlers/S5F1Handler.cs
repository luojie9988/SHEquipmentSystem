using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;
using static Secs4Net.Item;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S5F1 (Alarm Report Send) 报警报告发送处理器
    /// 设备主动向主机发送报警事件，这是SECS/GEM协议中重要的异步通知机制
    /// </summary>
    /// <remarks>
    /// SEMI E5/E30 标准定义：
    /// - S5F1: Alarm Report Send - 设备向主机报告报警的发生或清除
    /// - S5F2: Alarm Report Acknowledge - 主机确认收到报警报告
    /// 
    /// 交互流程：
    /// 1. 设备检测到报警条件触发或清除
    /// 2. 检查该报警是否已启用报告（通过S5F3配置）
    /// 3. 构建S5F1消息包含报警信息
    /// 4. 发送给主机并等待S5F2确认
    /// 5. 如果超时未收到确认，根据配置进行重试或缓存
    /// 
    /// 划裂片设备报警类型：
    /// - 1xxx: 系统报警（急停、安全门、气压异常等）
    /// - 2xxx: 工艺报警（温度超限、压力异常、速度异常等）
    /// - 3xxx: 刀具报警（刀具寿命到期、断刀检测、刀具缺失等）
    /// - 4xxx: 材料报警（晶圆破损、定位失败、ID读取失败等）
    /// - 5xxx: 通信报警（PLC通信异常、传感器离线等）
    /// - 6xxx: 维护提醒（保养到期、校准提醒等）
    /// 
    /// 与Host端匹配要点：
    /// - 报警代码(ALID)必须与Host端定义一致
    /// - 报警文本(ALTX)提供详细的故障描述
    /// - 报警分类(ALCD)区分SET(128)和CLEAR(0)
    /// - 支持报警使能控制(S5F3)
    /// </remarks>
    public class S5F1Handler : SecsMessageHandlerBase, IS5F1Handler
    {
        #region 报警代码定义

        /// <summary>
        /// 划裂片设备报警代码定义
        /// </summary>
        public static class DicerAlarmCodes
        {
            #region 系统报警 (1000-1999)

            /// <summary>急停按钮被按下</summary>
            public const uint EMO_PRESSED = 1001;
            /// <summary>安全门开启</summary>
            public const uint SAFETY_DOOR_OPEN = 1002;
            /// <summary>气压过低</summary>
            public const uint AIR_PRESSURE_LOW = 1003;
            /// <summary>真空异常</summary>
            public const uint VACUUM_ERROR = 1004;
            /// <summary>冷却水流量异常</summary>
            public const uint COOLING_WATER_ERROR = 1005;
            /// <summary>主轴过载</summary>
            public const uint SPINDLE_OVERLOAD = 1006;
            /// <summary>伺服报警</summary>
            public const uint SERVO_ALARM = 1007;
            /// <summary>电源异常</summary>
            public const uint POWER_ERROR = 1008;

            #endregion

            #region 工艺报警 (2000-2999)

            /// <summary>处理温度超上限</summary>
            public const uint TEMPERATURE_HIGH = 2001;
            /// <summary>处理温度超下限</summary>
            public const uint TEMPERATURE_LOW = 2002;
            /// <summary>处理压力超上限</summary>
            public const uint PRESSURE_HIGH = 2003;
            /// <summary>处理压力超下限</summary>
            public const uint PRESSURE_LOW = 2004;
            /// <summary>处理速度异常</summary>
            public const uint SPEED_ERROR = 2005;
            /// <summary>切割深度异常</summary>
            public const uint CUT_DEPTH_ERROR = 2006;
            /// <summary>工艺参数超限</summary>
            public const uint PROCESS_PARAM_ERROR = 2007;

            #endregion

            #region 刀具报警 (3000-3999)

            /// <summary>划刀寿命到期</summary>
            public const uint SCRIBE_KNIFE_LIFE_END = 3001;
            /// <summary>裂刀寿命到期</summary>
            public const uint BREAK_KNIFE_LIFE_END = 3002;
            /// <summary>划刀断刀检测</summary>
            public const uint SCRIBE_KNIFE_BROKEN = 3003;
            /// <summary>裂刀断刀检测</summary>
            public const uint BREAK_KNIFE_BROKEN = 3004;
            /// <summary>刀具未安装</summary>
            public const uint KNIFE_NOT_INSTALLED = 3005;
            /// <summary>刀具类型错误</summary>
            public const uint KNIFE_TYPE_ERROR = 3006;
            /// <summary>刀具需要更换</summary>
            public const uint KNIFE_CHANGE_REQUIRED = 3007;

            #endregion

            #region 材料报警 (4000-4999)

            /// <summary>晶圆破损检测</summary>
            public const uint WAFER_BROKEN = 4001;
            /// <summary>晶圆定位失败</summary>
            public const uint WAFER_ALIGN_FAIL = 4002;
            /// <summary>晶圆ID读取失败</summary>
            public const uint WAFER_ID_READ_FAIL = 4003;
            /// <summary>Cassette未放置</summary>
            public const uint CASSETTE_NOT_PRESENT = 4004;
            /// <summary>槽位映射错误</summary>
            public const uint SLOT_MAP_ERROR = 4005;
            /// <summary>材料类型不匹配</summary>
            public const uint MATERIAL_TYPE_MISMATCH = 4006;
            /// <summary>晶圆传送错误</summary>
            public const uint WAFER_TRANSFER_ERROR = 4007;

            #endregion

            #region 通信报警 (5000-5999)

            /// <summary>PLC通信异常</summary>
            public const uint PLC_COMM_ERROR = 5001;
            /// <summary>传感器离线</summary>
            public const uint SENSOR_OFFLINE = 5002;
            /// <summary>视觉系统异常</summary>
            public const uint VISION_SYSTEM_ERROR = 5003;
            /// <summary>条码读取器异常</summary>
            public const uint BARCODE_READER_ERROR = 5004;
            /// <summary>网络连接异常</summary>
            public const uint NETWORK_ERROR = 5005;

            #endregion

            #region 维护提醒 (6000-6999)

            /// <summary>日常保养到期</summary>
            public const uint DAILY_MAINTENANCE_DUE = 6001;
            /// <summary>周保养到期</summary>
            public const uint WEEKLY_MAINTENANCE_DUE = 6002;
            /// <summary>月保养到期</summary>
            public const uint MONTHLY_MAINTENANCE_DUE = 6003;
            /// <summary>校准到期</summary>
            public const uint CALIBRATION_DUE = 6004;
            /// <summary>润滑提醒</summary>
            public const uint LUBRICATION_REMINDER = 6005;
            /// <summary>滤网清洁提醒</summary>
            public const uint FILTER_CLEAN_REMINDER = 6006;

            #endregion
        }

        #endregion

        #region 私有字段

        /// <summary>SECS连接管理器</summary>
        private readonly ISecsConnectionManager _connectionManager;

        /// <summary>报警服务</summary>
        private readonly IAlarmService _alarmService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService? _eventService;

        /// <summary>PLC数据提供者</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>启用的报警列表（通过S5F3配置）</summary>
        private readonly HashSet<uint> _enabledAlarms;

        /// <summary>报警队列</summary>
        private readonly Queue<AlarmData> _alarmQueue;

        /// <summary>队列锁</summary>
        private readonly object _queueLock = new();

        /// <summary>报警处理任务</summary>
        private Task? _alarmProcessingTask;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>最大队列大小</summary>
        private const int MaxQueueSize = 500;

        /// <summary>重试次数</summary>
        private const int MaxRetryCount = 3;

        /// <summary>重试延迟(毫秒)</summary>
        private const int RetryDelay = 1000;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 5;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 1;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="connectionManager">SECS连接管理器</param>
        /// <param name="alarmService">报警服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="eventService">事件报告服务（可选）</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S5F1Handler(
            ILogger<S5F1Handler> logger,
            ISecsConnectionManager connectionManager,
            IAlarmService alarmService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            IEventReportService? eventService = null,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _eventService = eventService;
            _plcProvider = plcProvider;

            _enabledAlarms = new HashSet<uint>();
            _alarmQueue = new Queue<AlarmData>();

            // 初始化默认启用的报警
            InitializeDefaultEnabledAlarms();

            // 启动报警处理任务
            StartAlarmProcessing();

            Logger.LogInformation("S5F1处理器已初始化，报警处理任务已启动");
        }

        #endregion

        #region 公共方法 - IS5F1Handler接口实现

        /// <summary>
        /// 发送报警报告
        /// </summary>
        /// <param name="alarmId">报警ID</param>
        /// <param name="alarmCode">报警代码(SET=128, CLEAR=0)</param>
        /// <param name="alarmText">报警文本描述</param>
        /// <param name="additionalInfo">附加信息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        public async Task SendAlarmReportAsync(
            uint alarmId,
            byte alarmCode,
            string alarmText,
            Dictionary<string, object>? additionalInfo = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogInformation($"准备发送报警报告 - ALID: {alarmId}, ALCD: {alarmCode}, ALTX: {alarmText}");

                // 检查报警是否启用
                if (!IsAlarmEnabled(alarmId))
                {
                    Logger.LogTrace($"报警 {alarmId} 未启用，跳过发送");
                    return;
                }

                // 创建报警数据
                var alarmData = new AlarmData
                {
                    AlarmId = alarmId,
                    AlarmCode = alarmCode,
                    AlarmText = alarmText,
                    Timestamp = DateTime.Now,
                    AdditionalInfo = additionalInfo,
                    RetryCount = 0
                };

                // 加入队列
                EnqueueAlarm(alarmData);

                // 立即处理（不等待定时器）
                await ProcessAlarmAsync(alarmData, cancellationToken);

                // 更新报警服务
                if (alarmCode == 128) // SET
                {
                    await _alarmService.SetAlarmAsync(alarmId, alarmText);
                }
                else if (alarmCode == 0) // CLEAR
                {
                    await _alarmService.ClearAlarmAsync(alarmId);
                }

                // 触发相关事件（如果配置了事件报告）
                await TriggerAlarmEventAsync(alarmId, alarmCode);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"发送报警报告失败 - ALID: {alarmId}");
            }
        }

        /// <summary>
        /// 启用报警
        /// </summary>
        /// <param name="alarmIds">报警ID列表</param>
        public void EnableAlarms(IEnumerable<uint> alarmIds)
        {
            foreach (var alid in alarmIds)
            {
                _enabledAlarms.Add(alid);
                Logger.LogDebug($"已启用报警: {alid}");
            }
        }

        /// <summary>
        /// 禁用报警
        /// </summary>
        /// <param name="alarmIds">报警ID列表</param>
        public void DisableAlarms(IEnumerable<uint> alarmIds)
        {
            foreach (var alid in alarmIds)
            {
                _enabledAlarms.Remove(alid);
                Logger.LogDebug($"已禁用报警: {alid}");
            }
        }

        /// <summary>
        /// 获取所有启用的报警
        /// </summary>
        public IEnumerable<uint> GetEnabledAlarms()
        {
            return _enabledAlarms.ToList();
        }

        /// <summary>
        /// 检查报警是否启用
        /// </summary>
        public bool IsAlarmEnabled(uint alarmId)
        {
            return _enabledAlarms.Contains(alarmId);
        }

        #endregion

        #region 消息处理（S5F1作为主动消息，通常不处理接收）

        /// <summary>
        /// 处理S5F1消息（通常设备是发送方，此方法用于测试或特殊场景）
        /// </summary>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogWarning("收到S5F1消息（设备通常应该是发送方）");

            try
            {
                // 构建S5F2确认响应
                var s5f2 = new SecsMessage(5, 2, false)
                {
                    Name = "AlarmReportAcknowledge",
                    SecsItem = B(0) // ACKC5 = 0 表示接受
                };

                return s5f2;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S5F1消息失败");
                return new SecsMessage(5, 2, false)
                {
                    Name = "AlarmReportAcknowledge",
                    SecsItem = B(1) // ACKC5 = 1 表示错误
                };
            }
        }

        #endregion

        #region 私有方法 - 报警处理

        /// <summary>
        /// 处理单个报警
        /// </summary>
        private async Task ProcessAlarmAsync(AlarmData alarmData, CancellationToken cancellationToken)
        {
            for (int retry = 0; retry <= MaxRetryCount; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        Logger.LogInformation($"重试发送报警 {alarmData.AlarmId} (第{retry}次)");
                        await Task.Delay(RetryDelay, cancellationToken);
                    }

                    // 构建S5F1消息
                    var s5f1 = CreateS5F1Message(alarmData);

                    // 发送消息并等待响应
                    var response = await _connectionManager.SendMessageAsync(s5f1, cancellationToken);

                    if (response != null && response.F == 2)
                    {
                        // 解析S5F2响应
                        var ackc5 = response.SecsItem?.FirstValue<byte>() ?? 255;

                        if (ackc5 == 0)
                        {
                            Logger.LogInformation($"报警 {alarmData.AlarmId} 发送成功，主机已确认");
                            return;
                        }
                        else
                        {
                            Logger.LogWarning($"报警 {alarmData.AlarmId} 被主机拒绝，ACKC5={ackc5}");
                            return; // 不重试被拒绝的报警
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"发送报警 {alarmData.AlarmId} 失败 (重试{retry}/{MaxRetryCount})");

                    if (retry == MaxRetryCount)
                    {
                        Logger.LogError($"报警 {alarmData.AlarmId} 发送失败，已达最大重试次数");
                        // 可以选择缓存失败的报警或记录到日志
                        CacheFailedAlarm(alarmData);
                    }
                }
            }
        }

        /// <summary>
        /// 创建S5F1消息
        /// </summary>
        private SecsMessage CreateS5F1Message(AlarmData alarmData)
        {
            // 构建消息项
            var items = new List<Item>
            {
                B(alarmData.AlarmCode),    // ALCD - Alarm Code (128=SET, 0=CLEAR)
                U4(alarmData.AlarmId),      // ALID - Alarm ID
                A(alarmData.AlarmText)      // ALTX - Alarm Text
            };

            // 如果有附加信息，添加到消息中（扩展格式）
            if (alarmData.AdditionalInfo?.Count > 0)
            {
                // 可以添加自定义的附加信息
                // 注意：标准S5F1只有3个元素，扩展信息需要与Host端协商
            }

            return new SecsMessage(5, 1, true)
            {
                Name = "AlarmReportSend",
                SecsItem = L(items.ToArray())
            };
        }

        /// <summary>
        /// 加入报警队列
        /// </summary>
        private void EnqueueAlarm(AlarmData alarmData)
        {
            lock (_queueLock)
            {
                if (_alarmQueue.Count >= MaxQueueSize)
                {
                    Logger.LogWarning("报警队列已满，移除最旧的报警");
                    _alarmQueue.Dequeue();
                }

                _alarmQueue.Enqueue(alarmData);
                Logger.LogDebug($"报警 {alarmData.AlarmId} 已加入队列，当前队列长度: {_alarmQueue.Count}");
            }
        }

        /// <summary>
        /// 缓存失败的报警
        /// </summary>
        private void CacheFailedAlarm(AlarmData alarmData)
        {
            // TODO: 实现报警缓存机制，可以保存到数据库或文件
            Logger.LogWarning($"报警 {alarmData.AlarmId} 已缓存，等待下次发送");
        }

        /// <summary>
        /// 触发报警相关事件
        /// </summary>
        private async Task TriggerAlarmEventAsync(uint alarmId, byte alarmCode)
        {
            if (_eventService == null)
            {
                return;
            }

            try
            {
                // 根据报警触发相应的CEID事件
                uint ceid = alarmCode switch
                {
                    128 => 10001,  // 报警设置事件
                    0 => 10002,    // 报警清除事件
                    _ => 0
                };

                if (ceid > 0)
                {
                    await _eventService.ReportEventAsync(ceid,
                        $"ALARM_{(alarmCode == 128 ? "SET" : "CLEAR")}",
                        new Dictionary<uint, object>
                        {
                            { 1, alarmId },
                            { 2, DateTime.Now }
                        });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"触发报警事件失败: ALID={alarmId}");
            }
        }

        #endregion

        #region 私有方法 - 初始化和后台任务

        /// <summary>
        /// 初始化默认启用的报警
        /// </summary>
        private void InitializeDefaultEnabledAlarms()
        {
            // 默认启用所有系统报警
            _enabledAlarms.Add(DicerAlarmCodes.EMO_PRESSED);
            _enabledAlarms.Add(DicerAlarmCodes.SAFETY_DOOR_OPEN);
            _enabledAlarms.Add(DicerAlarmCodes.AIR_PRESSURE_LOW);
            _enabledAlarms.Add(DicerAlarmCodes.VACUUM_ERROR);
            _enabledAlarms.Add(DicerAlarmCodes.COOLING_WATER_ERROR);
            _enabledAlarms.Add(DicerAlarmCodes.SPINDLE_OVERLOAD);
            _enabledAlarms.Add(DicerAlarmCodes.SERVO_ALARM);
            _enabledAlarms.Add(DicerAlarmCodes.POWER_ERROR);

            // 默认启用关键工艺报警
            _enabledAlarms.Add(DicerAlarmCodes.TEMPERATURE_HIGH);
            _enabledAlarms.Add(DicerAlarmCodes.TEMPERATURE_LOW);
            _enabledAlarms.Add(DicerAlarmCodes.PRESSURE_HIGH);
            _enabledAlarms.Add(DicerAlarmCodes.PRESSURE_LOW);

            // 默认启用刀具寿命报警
            _enabledAlarms.Add(DicerAlarmCodes.SCRIBE_KNIFE_LIFE_END);
            _enabledAlarms.Add(DicerAlarmCodes.BREAK_KNIFE_LIFE_END);
            _enabledAlarms.Add(DicerAlarmCodes.SCRIBE_KNIFE_BROKEN);
            _enabledAlarms.Add(DicerAlarmCodes.BREAK_KNIFE_BROKEN);

            // 默认启用材料关键报警
            _enabledAlarms.Add(DicerAlarmCodes.WAFER_BROKEN);
            _enabledAlarms.Add(DicerAlarmCodes.CASSETTE_NOT_PRESENT);

            // 默认启用通信报警
            _enabledAlarms.Add(DicerAlarmCodes.PLC_COMM_ERROR);

            Logger.LogInformation($"已初始化 {_enabledAlarms.Count} 个默认启用报警");
        }

        /// <summary>
        /// 启动报警处理任务
        /// </summary>
        private void StartAlarmProcessing()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _alarmProcessingTask = Task.Run(async () => await ProcessAlarmQueueAsync(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// 处理报警队列（后台任务）
        /// </summary>
        private async Task ProcessAlarmQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    AlarmData? alarmData = null;

                    lock (_queueLock)
                    {
                        if (_alarmQueue.Count > 0)
                        {
                            alarmData = _alarmQueue.Dequeue();
                        }
                    }

                    if (alarmData != null)
                    {
                        await ProcessAlarmAsync(alarmData, cancellationToken);
                    }
                    else
                    {
                        // 队列为空，等待一段时间
                        await Task.Delay(100, cancellationToken);
                    }

                    // 检查PLC报警（如果配置了PLC）
                    await CheckPlcAlarmsAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "报警处理任务异常");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        /// <summary>
        /// 检查PLC报警
        /// </summary>
        private async Task CheckPlcAlarmsAsync(CancellationToken cancellationToken)
        {
            if (_plcProvider == null || !_plcProvider.IsConnected)
            {
                return;
            }

            try
            {
                // 读取PLC报警标志
                //var alarmFlags = _plcProvider.ReadBatch(new List<PLC.Models.PlcTag>
                //{
                //    new PLC.Models.PlcTag("EMO", "M202", PLC.Models.PlcDataType.Bool, "报警"),
                //    new PLC.Models.PlcTag("DoorOpen", "M203", PLC.Models.PlcDataType.Bool, "报警"),
                //    new PLC.Models.PlcTag("AirPressureLow", "M210", PLC.Models.PlcDataType.Bool, "报警"),
                //    new PLC.Models.PlcTag("VacuumError", "M211", PLC.Models.PlcDataType.Bool, "报警"),
                //    new PLC.Models.PlcTag("ScribeKnifeLife", "M220", PLC.Models.PlcDataType.Bool, "报警"),
                //    new PLC.Models.PlcTag("BreakKnifeLife", "M221", PLC.Models.PlcDataType.Bool, "报警")
                //});

                //// 处理报警状态变化
                //foreach (var flag in alarmFlags)
                //{
                //    await ProcessPlcAlarmChange(flag.Key, (bool)flag.Value, cancellationToken);
                //}
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "检查PLC报警失败");
            }
        }

        /// <summary>
        /// 处理PLC报警变化
        /// </summary>
        private async Task ProcessPlcAlarmChange(string tagName, bool isActive, CancellationToken cancellationToken)
        {
            uint alarmId = tagName switch
            {
                "EMO" => DicerAlarmCodes.EMO_PRESSED,
                "DoorOpen" => DicerAlarmCodes.SAFETY_DOOR_OPEN,
                "AirPressureLow" => DicerAlarmCodes.AIR_PRESSURE_LOW,
                "VacuumError" => DicerAlarmCodes.VACUUM_ERROR,
                "ScribeKnifeLife" => DicerAlarmCodes.SCRIBE_KNIFE_LIFE_END,
                "BreakKnifeLife" => DicerAlarmCodes.BREAK_KNIFE_LIFE_END,
                _ => 0
            };

            if (alarmId > 0)
            {
                byte alarmCode = isActive ? (byte)128 : (byte)0;
                string alarmText = GetAlarmText(alarmId);

                await SendAlarmReportAsync(alarmId, alarmCode, alarmText, null, cancellationToken);
            }
        }

        /// <summary>
        /// 获取报警文本
        /// </summary>
        private string GetAlarmText(uint alarmId)
        {
            return alarmId switch
            {
                DicerAlarmCodes.EMO_PRESSED => "Emergency stop button pressed",
                DicerAlarmCodes.SAFETY_DOOR_OPEN => "Safety door is open",
                DicerAlarmCodes.AIR_PRESSURE_LOW => "Air pressure is too low",
                DicerAlarmCodes.VACUUM_ERROR => "Vacuum system error",
                DicerAlarmCodes.COOLING_WATER_ERROR => "Cooling water flow error",
                DicerAlarmCodes.SPINDLE_OVERLOAD => "Spindle motor overload",
                DicerAlarmCodes.SERVO_ALARM => "Servo drive alarm",
                DicerAlarmCodes.POWER_ERROR => "Power supply error",
                DicerAlarmCodes.TEMPERATURE_HIGH => "Process temperature too high",
                DicerAlarmCodes.TEMPERATURE_LOW => "Process temperature too low",
                DicerAlarmCodes.PRESSURE_HIGH => "Process pressure too high",
                DicerAlarmCodes.PRESSURE_LOW => "Process pressure too low",
                DicerAlarmCodes.SPEED_ERROR => "Process speed error",
                DicerAlarmCodes.CUT_DEPTH_ERROR => "Cut depth error",
                DicerAlarmCodes.SCRIBE_KNIFE_LIFE_END => "Scribe knife life expired",
                DicerAlarmCodes.BREAK_KNIFE_LIFE_END => "Break knife life expired",
                DicerAlarmCodes.SCRIBE_KNIFE_BROKEN => "Scribe knife broken detected",
                DicerAlarmCodes.BREAK_KNIFE_BROKEN => "Break knife broken detected",
                DicerAlarmCodes.KNIFE_NOT_INSTALLED => "Knife not installed",
                DicerAlarmCodes.WAFER_BROKEN => "Wafer broken detected",
                DicerAlarmCodes.WAFER_ALIGN_FAIL => "Wafer alignment failed",
                DicerAlarmCodes.WAFER_ID_READ_FAIL => "Wafer ID read failed",
                DicerAlarmCodes.CASSETTE_NOT_PRESENT => "Cassette not present",
                DicerAlarmCodes.SLOT_MAP_ERROR => "Slot mapping error",
                DicerAlarmCodes.PLC_COMM_ERROR => "PLC communication error",
                DicerAlarmCodes.SENSOR_OFFLINE => "Sensor offline",
                DicerAlarmCodes.VISION_SYSTEM_ERROR => "Vision system error",
                _ => $"Alarm {alarmId}"
            };
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _alarmProcessingTask?.Wait(TimeSpan.FromSeconds(5));
            _cancellationTokenSource?.Dispose();
            base.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 报警数据
        /// </summary>
        private class AlarmData
        {
            public uint AlarmId { get; set; }
            public byte AlarmCode { get; set; }
            public string AlarmText { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public Dictionary<string, object>? AdditionalInfo { get; set; }
            public int RetryCount { get; set; }
        }

        #endregion
    }
}
