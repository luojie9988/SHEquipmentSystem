// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S5F1Handler.cs
// 版本: v2.0.0
// 描述: S5F1消息处理器 - Alarm Report Send 报警报告发送处理器

using System;
using System.Collections.Concurrent;
using Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Models;
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
    /// 设备主动向主机发送报警事件，实现SECS/GEM协议中的异步报警通知机制
    /// </summary>
    /// <remarks>
    /// SEMI E5/E30 标准定义：
    /// - S5F1: Alarm Report Send - 设备向主机报告报警的发生或清除
    /// - S5F2: Alarm Report Acknowledge - 主机确认收到报警报告
    /// 
    /// 消息格式：
    /// S5F1 W
    /// L,3
    ///   1. &lt;ALCD&gt; B,1 报警代码 (bit8=1:SET, bit8=0:CLEAR, bit7-1:类别)
    ///   2. &lt;ALID&gt; U4  报警ID
    ///   3. &lt;ALTX&gt; A,n 报警文本(最多120字符)
    /// 
    /// S5F2
    /// &lt;ACKC5&gt; B,1 确认代码 (0=成功, >0=错误)
    /// 
    /// 划裂片设备报警实现：
    /// - 支持96个ALID定义（12000-12095）
    /// - 报警分类：系统/轴限位/视觉/材料/维护
    /// - 自动报警缓存和重试机制
    /// - 与S5F3启用控制联动
    /// </remarks>
    public class S5F1Handler : SecsMessageHandlerBase, IS5F1Handler
    {
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

        /// <summary>数据模型</summary>
        private readonly DiceDataModel _dataModel;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>启用的报警ID集合</summary>
        private readonly ConcurrentDictionary<uint, bool> _enabledAlarms;

        /// <summary>报警状态缓存</summary>
        private readonly ConcurrentDictionary<uint, AlarmState> _alarmStates;

        /// <summary>报警发送队列</summary>
        private readonly ConcurrentQueue<AlarmReport> _alarmQueue;

        /// <summary>报警处理信号量</summary>
        private readonly SemaphoreSlim _alarmSemaphore;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>报警处理任务</summary>
        private Task? _alarmProcessingTask;

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
        public S5F1Handler(
            ILogger<S5F1Handler> logger,
            ISecsConnectionManager connectionManager,
            IAlarmService alarmService,
            IEquipmentStateService stateService,
            DiceDataModel dataModel,
            IOptions<EquipmentSystemConfiguration> options,
            IEventReportService? eventService = null,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _eventService = eventService;
            _plcProvider = plcProvider;

            _enabledAlarms = new ConcurrentDictionary<uint, bool>();
            _alarmStates = new ConcurrentDictionary<uint, AlarmState>();
            _alarmQueue = new ConcurrentQueue<AlarmReport>();
            _alarmSemaphore = new SemaphoreSlim(0);

            // 初始化默认启用的报警
            InitializeDefaultEnabledAlarms();

            // 订阅报警服务事件
            SubscribeAlarmEvents();

            // 启动报警处理任务
            StartAlarmProcessing();

            Logger.LogInformation("S5F1处理器已初始化，共启用 {Count} 个报警", _enabledAlarms.Count);
        }

        #endregion

        #region 公共方法 - IS5F1Handler接口实现

        /// <summary>
        /// 发送报警报告
        /// </summary>
        /// <param name="alarmId">报警ID</param>
        /// <param name="alarmCode">报警代码(128=SET, 0=CLEAR)</param>
        /// <param name="alarmText">报警文本描述</param>
        /// <param name="additionalInfo">附加信息</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task SendAlarmReportAsync(
            uint alarmId,
            byte alarmCode,
            string alarmText,
            Dictionary<string, object>? additionalInfo = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 验证报警ID
                if (!SemiIdDefinitions.Validator.IsValidAlid(alarmId))
                {
                    Logger.LogWarning($"无效的报警ID: {alarmId}");
                    return;
                }

                // 检查报警是否启用
                if (!IsAlarmEnabled(alarmId))
                {
                    Logger.LogDebug($"报警 {alarmId} 未启用，跳过发送");
                    return;
                }

                // 确保报警文本不超过120字符
                if (alarmText.Length > 120)
                {
                    alarmText = alarmText.Substring(0, 117) + "...";
                }

                // 创建报警报告
                var report = new AlarmReport
                {
                    AlarmId = alarmId,
                    AlarmCode = alarmCode,
                    AlarmText = alarmText,
                    Timestamp = DateTime.Now,
                    AdditionalInfo = additionalInfo
                };

                // 更新报警状态
                UpdateAlarmState(alarmId, alarmCode);

                // 如果连接正常，立即发送
                if (_connectionManager.IsConnected)
                {
                    await SendAlarmReportInternalAsync(report, cancellationToken);
                }
                else
                {
                    // 否则加入队列
                    EnqueueAlarmReport(report);
                    Logger.LogWarning($"连接断开，报警 {alarmId} 已加入队列");
                }
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
            foreach (var alarmId in alarmIds)
            {
                if (SemiIdDefinitions.Validator.IsValidAlid(alarmId))
                {
                    _enabledAlarms[alarmId] = true;
                    Logger.LogDebug($"启用报警: {alarmId}");
                }
            }

            // 更新数据模型
            UpdateDataModelAlarmsEnabled();
        }

        /// <summary>
        /// 禁用报警
        /// </summary>
        /// <param name="alarmIds">报警ID列表</param>
        public void DisableAlarms(IEnumerable<uint> alarmIds)
        {
            foreach (var alarmId in alarmIds)
            {
                // 检查是否为强制报警
                if (IsMandatoryAlarm(alarmId))
                {
                    Logger.LogWarning($"报警 {alarmId} 为强制报警，不能禁用");
                    continue;
                }

                _enabledAlarms[alarmId] = false;
                Logger.LogDebug($"禁用报警: {alarmId}");
            }

            // 更新数据模型
            UpdateDataModelAlarmsEnabled();
        }

        /// <summary>
        /// 获取所有启用的报警
        /// </summary>
        public IEnumerable<uint> GetEnabledAlarms()
        {
            return _enabledAlarms.Where(kv => kv.Value).Select(kv => kv.Key);
        }

        /// <summary>
        /// 检查报警是否启用
        /// </summary>
        public bool IsAlarmEnabled(uint alarmId)
        {
            return _enabledAlarms.TryGetValue(alarmId, out var enabled) && enabled;
        }

        /// <summary>
        /// 处理接收到的S5F1消息
        /// </summary>
        /// <param name="message">接收到的消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应消息</returns>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogDebug("接收到S5F1消息（报警请求）");

                // 解析消息内容
                // S5F1: W
                //   L,3
                //     1. <ALCD> <B[1]> 报警代码
                //     2. <ALID> <U4[1]> 报警ID
                //     3. <ALTX> <A[120]> 报警文本
                var list = message.SecsItem;
                if (list == null || list.Count != 3)
                {
                    Logger.LogWarning("S5F1消息格式错误");
                    // 返回S5F2拒绝
                    return new SecsMessage(5, 2, false)
                    {
                        Name = "AlarmReportAcknowledge",
                        SecsItem = Item.B(1) // ACKC5 = 1 (Error)
                    };
                }

                var alcd = list.Items[0].FirstValue<byte>();
                var alid = list.Items[1].FirstValue<uint>();
                var altx = list.Items[2].GetString();

                // 处理报警
                await HandleAlarmFromHostAsync(alid, alcd, altx, cancellationToken);

                // 返回S5F2确认
                return new SecsMessage(5, 2, false)
                {
                    Name = "AlarmReportAcknowledge",
                    SecsItem = Item.B(0) // ACKC5 = 0 (Accepted)
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S5F1消息失败");
                return new SecsMessage(5, 2, false)
                {
                    Name = "AlarmReportAcknowledge",
                    SecsItem = Item.B(1) // ACKC5 = 1 (Error)
                };
            }
        }

        /// <summary>
        /// 处理来自主机的报警请求
        /// </summary>
        private async Task HandleAlarmFromHostAsync(
            uint alarmId,
            byte alarmCode,
            string alarmText,
            CancellationToken cancellationToken)
        {
            // 记录报警
            Logger.LogInformation($"主机报警请求 - ALID: {alarmId}, Code: {alarmCode}, Text: {alarmText}");

            // 通知报警服务
            if (alarmCode >= 128)
            {
                await _alarmService.SetAlarmAsync(alarmId, alarmText);
            }
            else
            {
                await _alarmService.ClearAlarmAsync(alarmId);
            }

            // 触发事件通知
            if (_eventService != null)
            {
                await _eventService.SendEventReportAsync(
                    alarmCode >= 128 ? 230u : 231u, // AlarmSet or AlarmClear
                    alarmCode >= 128 ? "AlarmSet" : "AlarmClear",
                    new Dictionary<uint, object> { { alarmId, alarmText } },
                    cancellationToken);
            }
        }

        #endregion

        #region 消息处理 - 用于处理来自PLC或其他源的报警触发

        /// <summary>
        /// 处理报警触发（来自PLC或内部检测）
        /// </summary>
        public async Task HandleAlarmTriggerAsync(uint alarmId, bool isSet, string? customText = null)
        {
            try
            {
                // 获取报警定义
                var alarmDef = GetAlarmDefinition(alarmId);
                if (alarmDef == null)
                {
                    Logger.LogWarning($"未找到报警定义: {alarmId}");
                    return;
                }

                // 确定报警代码
                byte alarmCode = (byte)(isSet ? 128 + alarmDef.Category : alarmDef.Category);

                // 确定报警文本
                string alarmText = customText ?? alarmDef.Description;

                // 收集附加信息
                var additionalInfo = CollectAlarmAdditionalInfo(alarmId);

                // 发送报警报告
                await SendAlarmReportAsync(alarmId, alarmCode, alarmText, additionalInfo);

                // 触发事件报告（如果启用）
                if (_eventService != null && isSet)
                {
                    var ceid = GetAlarmEventId(alarmId, isSet);
                    if (ceid.HasValue)
                    {
                        await _eventService.TriggerEventAsync(ceid.Value, $"Alarm {(isSet ? "Set" : "Clear")} - ID: {alarmId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"处理报警触发失败 - ALID: {alarmId}");
            }
        }

        #endregion

        #region 私有方法 - 报警处理

        /// <summary>
        /// 内部发送报警报告
        /// </summary>
        private async Task<bool> SendAlarmReportInternalAsync(AlarmReport report, CancellationToken cancellationToken)
        {
            try
            {
                // 构建S5F1消息
                var s5f1 = new SecsMessage(5, 1, true)
                {
                    Name = "AlarmReportSend",
                    SecsItem = L(
                        B(report.AlarmCode),                    // ALCD
                        U4(report.AlarmId),                      // ALID
                        A(report.AlarmText)                      // ALTX
                    )
                };

                Logger.LogInformation($"发送S5F1报警报告 - ALID: {report.AlarmId}, " +
                                     $"代码: 0x{report.AlarmCode:X2}, 文本: {report.AlarmText}");

                // 发送消息并等待响应
                var response = await _connectionManager.SendMessageAsync(s5f1, cancellationToken);

                if (response != null && response.S == 5 && response.F == 2)
                {
                    // 解析ACKC5
                    var ackc5 = response.SecsItem?.FirstValue<byte>() ?? 1;

                    if (ackc5 == 0)
                    {
                        Logger.LogDebug($"报警报告已确认 - ALID: {report.AlarmId}");
                        return true;
                    }
                    else
                    {
                        Logger.LogWarning($"报警报告被拒绝 - ALID: {report.AlarmId}, ACKC5: {ackc5}");
                        return false;
                    }
                }
                else
                {
                    Logger.LogWarning($"未收到S5F2响应 - ALID: {report.AlarmId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"发送报警报告异常 - ALID: {report.AlarmId}");
                return false;
            }
        }

        /// <summary>
        /// 报警处理任务
        /// </summary>
        private async Task ProcessAlarmQueueAsync()
        {
            Logger.LogInformation("报警处理任务已启动");

            while (!_cancellationTokenSource?.Token.IsCancellationRequested ?? false)
            {
                try
                {
                    // 等待信号或超时（用于定期检查队列）
                    await _alarmSemaphore.WaitAsync(TimeSpan.FromSeconds(10),
                        _cancellationTokenSource?.Token ?? CancellationToken.None);

                    // 处理队列中的报警
                    while (_alarmQueue.TryDequeue(out var report))
                    {
                        if (_connectionManager.IsConnected)
                        {
                            var success = await SendAlarmReportInternalAsync(report,
                                _cancellationTokenSource?.Token ?? CancellationToken.None);

                            if (!success && report.RetryCount < 3)
                            {
                                // 重试
                                report.RetryCount++;
                                await Task.Delay(1000);
                                _alarmQueue.Enqueue(report);
                                _alarmSemaphore.Release();
                            }
                        }
                        else
                        {
                            // 连接断开，重新入队
                            _alarmQueue.Enqueue(report);
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "报警处理任务异常");
                }
            }

            Logger.LogInformation("报警处理任务已停止");
        }

        #endregion

        #region 私有方法 - 初始化和配置

        /// <summary>
        /// 初始化默认启用的报警
        /// </summary>
        private void InitializeDefaultEnabledAlarms()
        {
            // 启用所有系统报警（12000-12018）
            for (uint i = 12000; i <= 12018; i++)
            {
                _enabledAlarms[i] = true;
            }

            // 启用关键轴报警
            _enabledAlarms[12019] = true;  // Y轴负限位
            _enabledAlarms[12020] = true;  // Y轴正限位
            _enabledAlarms[12021] = true;  // Z轴负限位
            _enabledAlarms[12022] = true;  // Z轴正限位
            _enabledAlarms[12023] = true;  // X轴负限位
            _enabledAlarms[12024] = true;  // X轴正限位

            // 启用安全相关报警
            _enabledAlarms[12030] = true;  // 气压低
            _enabledAlarms[12037] = true;  // 门开

            Logger.LogDebug($"初始化默认启用 {_enabledAlarms.Count} 个报警");
        }

        /// <summary>
        /// 订阅报警服务事件
        /// </summary>
        private void SubscribeAlarmEvents()
        {
            if (_alarmService != null)
            {
                _alarmService.AlarmOccurred += OnAlarmOccurred;
                _alarmService.AlarmCleared += OnAlarmCleared;
            }
        }

        /// <summary>
        /// 启动报警处理
        /// </summary>
        private void StartAlarmProcessing()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _alarmProcessingTask = Task.Run(() => ProcessAlarmQueueAsync());
        }

        #endregion

        #region 私有方法 - 报警定义和分类

        /// <summary>
        /// 获取报警定义
        /// </summary>
        private AlarmDefinition? GetAlarmDefinition(uint alarmId)
        {
            // 根据ALID获取报警分类和描述
            var category = SemiIdDefinitions.Alid.GetAlarmCategory(alarmId);
            var priority = SemiIdDefinitions.Alid.GetAlarmPriority(alarmId);

            // 构建报警定义
            return new AlarmDefinition
            {
                AlarmId = alarmId,
                Category = GetAlarmCategoryCode(category),
                Priority = (Common.SemiStandard.SemiStandardDefinitions.AlarmPriority)priority,
                Description = GetAlarmDescription(alarmId)
            };
        }

        /// <summary>
        /// 获取报警分类代码
        /// </summary>
        private byte GetAlarmCategoryCode(string category)
        {
            return category switch
            {
                "系统报警" => 1,     // Personal safety
                "轴报警" => 2,       // Equipment safety
                "视觉报警" => 3,     // Parameter control warning
                "材料报警" => 4,     // Parameter control error
                "裂片轴报警" => 5,   // Irrecoverable error
                "层叠放报警" => 6,   // Equipment status warning
                "层错环报警" => 7,   // Attention flags
                _ => 8              // Other categories
            };
        }

        /// <summary>
        /// 获取报警描述
        /// </summary>
        private string GetAlarmDescription(uint alarmId)
        {
            // 这里应该从配置或资源文件中获取
            // 暂时返回基于ID的默认描述
            return alarmId switch
            {
                12000 => "设备急停",
                12001 => "执行完成",
                12002 => "划刀连锁（切刀位置低）",
                12003 => "Y轴电机故障",
                12004 => "Z轴电机故障",
                12005 => "X轴电机故障",
                12006 => "θ轴电机故障",
                _ => $"报警 {alarmId}"
            };
        }

        /// <summary>
        /// 检查是否为强制报警
        /// </summary>
        private bool IsMandatoryAlarm(uint alarmId)
        {
            // 系统安全相关报警为强制报警
            return alarmId == 12000 ||  // 急停
                   alarmId == 12037 ||  // 门开
                   alarmId == 12030;    // 气压低
        }

        #endregion

        #region 私有方法 - 状态管理

        /// <summary>
        /// 更新报警状态
        /// </summary>
        private void UpdateAlarmState(uint alarmId, byte alarmCode)
        {
            var isSet = (alarmCode & 0x80) != 0;
            _alarmStates[alarmId] = new AlarmState
            {
                AlarmId = alarmId,
                IsSet = isSet,
                LastChangeTime = DateTime.Now
            };

            // 更新数据模型
            UpdateDataModelAlarmsSet();
        }

        /// <summary>
        /// 更新数据模型中的启用报警列表
        /// </summary>
        private void UpdateDataModelAlarmsEnabled()
        {
            _dataModel.AlarmsEnabled = GetEnabledAlarms().ToList();
        }

        /// <summary>
        /// 更新数据模型中的激活报警列表
        /// </summary>
        private void UpdateDataModelAlarmsSet()
        {
            var activeAlarms = _alarmStates
                .Where(kv => kv.Value.IsSet)
                .Select(kv => kv.Key)
                .ToList();

            _dataModel.AlarmsSet = activeAlarms;
        }

        /// <summary>
        /// 收集报警附加信息
        /// </summary>
        private Dictionary<string, object> CollectAlarmAdditionalInfo(uint alarmId)
        {
            var info = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["ControlState"] = _dataModel.ControlState.ToString(),
                ["ProcessState"] = _dataModel.ProcessState.ToString()
            };

            // 根据报警类型添加相关信息
            if (alarmId >= 12003 && alarmId <= 12006)
            {
                // 轴报警，添加坐标信息
                info["CurrentX"] = _dataModel.CurrentX;
                info["CurrentY"] = _dataModel.CurrentY;
                info["CurrentZ"] = _dataModel.CurrentZ;
                info["CurrentTheta"] = _dataModel.CurrentTheta;
            }

            return info;
        }

        /// <summary>
        /// 获取报警事件ID
        /// </summary>
        private uint? GetAlarmEventId(uint alarmId, bool isSet)
        {
            // 报警事件ID = 20000 + ALID * 2 + (isSet ? 0 : 1)
            // 这是一个示例映射，实际应该从配置中获取
            return 20000 + (alarmId * 2) + (uint)(isSet ? 0 : 1);
        }

        /// <summary>
        /// 将报警加入队列
        /// </summary>
        private void EnqueueAlarmReport(AlarmReport report)
        {
            _alarmQueue.Enqueue(report);
            _alarmSemaphore.Release();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 报警发生事件处理
        /// </summary>
        private void OnAlarmOccurred(object? sender, AlarmEventArgs e)
        {
            _ = HandleAlarmTriggerAsync(e.AlarmId, true, e.AlarmText);
        }

        /// <summary>
        /// 报警清除事件处理
        /// </summary>
        private void OnAlarmCleared(object? sender, AlarmEventArgs e)
        {
            _ = HandleAlarmTriggerAsync(e.AlarmId, false, e.AlarmText);
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            // 停止报警处理任务
            _cancellationTokenSource?.Cancel();
            _alarmProcessingTask?.Wait(TimeSpan.FromSeconds(5));

            // 取消订阅
            if (_alarmService != null)
            {
                _alarmService.AlarmOccurred -= OnAlarmOccurred;
                _alarmService.AlarmCleared -= OnAlarmCleared;
            }

            _cancellationTokenSource?.Dispose();
            _alarmSemaphore?.Dispose();

            base.Dispose();

            Logger.LogInformation("S5F1处理器已释放资源");
        }

        #endregion

        #region 内部类型定义

        /// <summary>
        /// 报警报告
        /// </summary>
        private class AlarmReport
        {
            public uint AlarmId { get; set; }
            public byte AlarmCode { get; set; }
            public string AlarmText { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public Dictionary<string, object>? AdditionalInfo { get; set; }
            public int RetryCount { get; set; }
        }

        /// <summary>
        /// 报警状态
        /// </summary>
        private class AlarmState
        {
            public uint AlarmId { get; set; }
            public bool IsSet { get; set; }
            public DateTime LastChangeTime { get; set; }
        }

        /// <summary>
        /// 报警定义
        /// </summary>
        private class AlarmDefinition
        {
            public uint AlarmId { get; set; }
            public byte Category { get; set; }
            public Common.SemiStandard.SemiStandardDefinitions.AlarmPriority Priority { get; set; }
            public string Description { get; set; } = "";
        }

        #endregion
    }

    #region 事件参数

    #endregion
}
