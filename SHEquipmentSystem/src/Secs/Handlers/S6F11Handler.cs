// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S6F11Handler.cs
// 版本: v1.0.0
// 描述: S6F11消息处理器 - Event Report Send 事件报告发送处理器

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Helpers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S6F11 (Event Report Send) 处理器
    /// 设备主动发送事件报告给主机，这是SECS/GEM通信中最重要的消息之一
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S6F11: 事件报告发送 - 设备向主机报告事件发生
    /// - S6F12: 事件报告确认 - 主机确认收到事件报告
    /// 
    /// 交互流程：
    /// 1. 设备检测到事件发生（状态变化、报警、操作完成等）
    /// 2. 检查事件是否已启用（通过S2F37配置）
    /// 3. 获取事件关联的报告（通过S2F35配置）
    /// 4. 收集报告中定义的变量数据（通过S2F33配置）
    /// 5. 构建并发送S6F11消息
    /// 6. 等待主机返回S6F12确认
    /// 7. 如果超时未收到确认，根据配置进行重试或缓存
    /// 
    /// 划裂片设备关键事件：
    /// - 控制状态变化（离线/本地/远程）
    /// - 处理状态变化（空闲/执行/完成）
    /// - 材料处理（到达/移除/开始/结束）
    /// - 报警事件（设置/清除）
    /// - 操作员动作（启动/停止/暂停/恢复）
    /// - 配方事件（选择/启动/完成）
    /// - 划裂片特定（刀具更换/校准完成/维护提醒）
    /// </remarks>
    public class S6F11Handler : SecsMessageHandlerBase, IS6F11Handler
    {
        #region 划裂片设备事件定义

        /// <summary>
        /// 划裂片设备支持的事件ID定义
        /// </summary>
        public static class DicerEvents
        {
            #region 控制状态事件 (200-209)

            /// <summary>控制状态 - 离线</summary>
            public const uint ControlStateOffline = 200;
            /// <summary>控制状态 - 本地</summary>
            public const uint ControlStateLocal = 201;
            /// <summary>控制状态 - 远程</summary>
            public const uint ControlStateRemote = 202;

            #endregion

            #region 处理状态事件 (210-219)

            /// <summary>处理开始</summary>
            public const uint ProcessStart = 210;
            /// <summary>处理完成</summary>
            public const uint ProcessComplete = 211;
            /// <summary>处理中止</summary>
            public const uint ProcessAbort = 212;
            /// <summary>处理暂停</summary>
            public const uint ProcessPause = 213;
            /// <summary>处理恢复</summary>
            public const uint ProcessResume = 214;

            #endregion

            #region 操作员动作事件 (220-229)

            /// <summary>操作员设备常量改变</summary>
            public const uint OperatorEquipmentConstantChange = 220;
            /// <summary>操作员命令执行</summary>
            public const uint OperatorCommandExecuted = 221;

            #endregion

            #region 报警事件 (230-239)

            /// <summary>报警设置</summary>
            public const uint AlarmSet = 230;
            /// <summary>报警清除</summary>
            public const uint AlarmClear = 231;

            #endregion

            #region 通信事件 (240-249)

            /// <summary>消息识别</summary>
            public const uint MessageRecognition = 240;
            /// <summary>通信建立</summary>
            public const uint CommunicationEstablished = 241;
            /// <summary>通信失败</summary>
            public const uint CommunicationFailed = 242;

            #endregion

            #region 材料处理事件 (11000-11099)

            /// <summary>材料到达</summary>
            public const uint MaterialArrival = 11000;
            /// <summary>材料移除</summary>
            public const uint MaterialRemoved = 11001;
            /// <summary>映射完成</summary>
            public const uint MapComplete = 11002;
            /// <summary>配方选择</summary>
            public const uint PPSelected = 11003;
            /// <summary>处理开始</summary>
            public const uint MaterialProcessStart = 11004;
            /// <summary>处理结束</summary>
            public const uint MaterialProcessEnd = 11005;
            /// <summary>槽位跳过</summary>
            public const uint SlotSkipped = 11010;
            /// <summary>槽位映射结束</summary>
            public const uint SlotMapEnd = 11011;
            /// <summary>Frame开始</summary>
            public const uint FrameStart = 11012;
            /// <summary>Frame结束</summary>
            public const uint FrameEnd = 11013;
            /// <summary>Cassette开始</summary>
            public const uint CassetteStart = 11014;
            /// <summary>Cassette结束</summary>
            public const uint CassetteEnd = 11015;

            #endregion

            #region 划裂片特定事件 (11100-11199)

            /// <summary>刀具更换</summary>
            public const uint KnifeChange = 11100;
            /// <summary>刀具寿命警告</summary>
            public const uint KnifeLifeWarning = 11101;
            /// <summary>刀具寿命到期</summary>
            public const uint KnifeLifeExpired = 11102;
            /// <summary>校准开始</summary>
            public const uint CalibrationStart = 11110;
            /// <summary>校准完成</summary>
            public const uint CalibrationComplete = 11111;
            /// <summary>维护提醒</summary>
            public const uint MaintenanceReminder = 11120;
            /// <summary>维护完成</summary>
            public const uint MaintenanceComplete = 11121;
            /// <summary>切割参数改变</summary>
            public const uint CuttingParameterChanged = 11130;
            /// <summary>冷却系统异常</summary>
            public const uint CoolingSystemAbnormal = 11140;
            /// <summary>真空系统异常</summary>
            public const uint VacuumSystemAbnormal = 11141;

            #endregion
        }

        #endregion

        #region 私有字段

        /// <summary>SECS连接管理器</summary>
        private readonly ISecsConnectionManager _connectionManager;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventService;

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>S2F35处理器（获取事件链接）</summary>
        private readonly S2F35Handler? _linkHandler;

        /// <summary>S2F37处理器（检查事件启用）</summary>
        private readonly S2F37Handler? _enableHandler;

        /// <summary>S2F33处理器（获取报告定义）</summary>
        private readonly S2F33Handler? _reportHandler;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>事件队列</summary>
        private readonly Queue<EventData> _eventQueue;

        /// <summary>事件队列锁</summary>
        private readonly object _queueLock = new();

        /// <summary>事件处理任务</summary>
        private Task? _eventProcessingTask;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>最大队列大小</summary>
        private const int MaxQueueSize = 1000;

        /// <summary>重试次数</summary>
        private const int MaxRetryCount = 3;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 6;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 11;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="connectionManager">SECS连接管理器</param>
        /// <param name="eventService">事件报告服务</param>
        /// <param name="statusService">状态变量服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="serviceProvider">服务提供者</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S6F11Handler(
            ILogger<S6F11Handler> logger,
            ISecsConnectionManager connectionManager,
            IEventReportService eventService,
            IStatusVariableService statusService,
            IOptions<EquipmentSystemConfiguration> options,
            IServiceProvider serviceProvider) : base(logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // 尝试获取相关处理器
            _linkHandler = serviceProvider.GetService(typeof(S2F35Handler)) as S2F35Handler;
            _enableHandler = serviceProvider.GetService(typeof(S2F37Handler)) as S2F37Handler;
            _reportHandler = serviceProvider.GetService(typeof(S2F33Handler)) as S2F33Handler;

            // 初始化事件队列
            _eventQueue = new Queue<EventData>();

            // 启动事件处理任务
            StartEventProcessing();

            Logger.LogInformation("S6F11处理器已初始化，事件处理任务已启动");
        }

        #endregion

        #region 公共方法 - IS6F11Handler接口实现

        /// <summary>
        /// 发送事件报告
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <param name="eventName">事件名称</param>
        /// <param name="additionalData">附加数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        public async Task SendEventReportAsync(
            uint ceid,
            string eventName,
            Dictionary<uint, object>? additionalData = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Logger.LogDebug($"准备发送事件报告 - CEID: {ceid}, Name: {eventName}");

                // 检查事件是否启用
                if (!IsEventEnabled(ceid))
                {
                    Logger.LogTrace($"事件 {ceid} 未启用，跳过发送");
                    return;
                }

                // 创建事件数据
                var eventData = new EventData
                {
                    Ceid = ceid,
                    EventName = eventName,
                    Timestamp = DateTime.Now,
                    AdditionalData = additionalData ?? new Dictionary<uint, object>()
                };

                // 添加到队列
                EnqueueEvent(eventData);

                // 如果是高优先级事件，立即处理
                if (IsHighPriorityEvent(ceid))
                {
                    await ProcessEventImmediately(eventData, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"发送事件报告失败 - CEID: {ceid}");
            }
        }

        /// <summary>
        /// 处理接收到的S6F11消息（通常设备不接收S6F11）
        /// </summary>
        public override Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogWarning("设备端收到S6F11消息，这通常不应发生");

            // 返回S6F12确认
            return Task.FromResult<SecsMessage?>(new SecsMessage(6, 12, false)
            {
                Name = "EventReportAcknowledge",
                SecsItem = Item.U1(0) // ACKC6 = 0 (Accepted)
            });
        }

        #endregion

        #region 私有方法 - 事件处理

        /// <summary>
        /// 启动事件处理
        /// </summary>
        private void StartEventProcessing()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _eventProcessingTask = Task.Run(
                () => ProcessEventQueue(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 处理事件队列
        /// </summary>
        private async Task ProcessEventQueue(CancellationToken cancellationToken)
        {
            Logger.LogDebug("事件处理任务已启动");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    EventData? eventData = null;

                    // 获取下一个事件
                    lock (_queueLock)
                    {
                        if (_eventQueue.Count > 0)
                        {
                            eventData = _eventQueue.Dequeue();
                        }
                    }

                    if (eventData != null)
                    {
                        await SendEventReport(eventData, cancellationToken);
                    }
                    else
                    {
                        // 没有事件时等待
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "处理事件队列时发生错误");
                    await Task.Delay(1000, cancellationToken);
                }
            }

            Logger.LogDebug("事件处理任务已停止");
        }

        /// <summary>
        /// 立即处理事件
        /// </summary>
        private async Task ProcessEventImmediately(
            EventData eventData,
            CancellationToken cancellationToken)
        {
            try
            {
                await SendEventReport(eventData, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"立即处理事件 {eventData.Ceid} 失败");
                // 失败的高优先级事件仍然保留在队列中
            }
        }

        /// <summary>
        /// 发送事件报告
        /// </summary>
        private async Task SendEventReport(
            EventData eventData,
            CancellationToken cancellationToken)
        {
            for (int retry = 0; retry < MaxRetryCount; retry++)
            {
                try
                {
                    // 构建S6F11消息
                    var s6f11 = await BuildS6F11Message(eventData, cancellationToken);
                    if (s6f11 == null)
                    {
                        Logger.LogWarning($"无法构建事件 {eventData.Ceid} 的S6F11消息");
                        return;
                    }

                    // 发送消息并等待响应
                    var response = await _connectionManager.SendMessageAsync(s6f11, cancellationToken);

                    if (response != null && response.F == 12)
                    {
                        // 解析S6F12响应
                        var ackc6 = ParseAckc6(response.SecsItem);

                        if (ackc6 == 0)
                        {
                            Logger.LogInformation($"事件 {eventData.Ceid} ({eventData.EventName}) 已成功发送");

                            // 触发事件发送成功的回调
                            await _eventService.OnEventReportSentAsync(eventData.Ceid);
                            return;
                        }
                        else
                        {
                            Logger.LogWarning($"主机拒绝事件 {eventData.Ceid}, ACKC6={ackc6}");
                            return; // 不重试
                        }
                    }
                }
                catch (TimeoutException)
                {
                    Logger.LogWarning($"发送事件 {eventData.Ceid} 超时，重试 {retry + 1}/{MaxRetryCount}");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"发送事件 {eventData.Ceid} 失败");
                }

                if (retry < MaxRetryCount - 1)
                {
                    await Task.Delay(1000 * (retry + 1), cancellationToken);
                }
            }

            // 所有重试都失败，缓存事件
            await CacheFailedEvent(eventData);
        }

        #endregion

        #region 私有方法 - 消息构建

        /// <summary>
        /// 构建S6F11消息
        /// </summary>
        private async Task<SecsMessage?> BuildS6F11Message(
            EventData eventData,
            CancellationToken cancellationToken)
        {
            try
            {
                // 获取事件链接的报告
                var linkedReports = GetLinkedReports(eventData.Ceid);
                if (linkedReports.Count == 0)
                {
                    Logger.LogDebug($"事件 {eventData.Ceid} 没有链接的报告");
                    linkedReports.Add(0); // 添加空报告
                }

                // 收集报告数据
                var reports = new List<Item>();
                foreach (var rptid in linkedReports)
                {
                    var reportData = await CollectReportData(rptid, eventData, cancellationToken);
                    reports.Add(reportData);
                }

                // 构建S6F11消息结构
                // L,3
                //   1. U4 DATAID (数据ID，0表示单个消息)
                //   2. U4 CEID (事件ID)
                //   3. L,n (报告列表)
                //      L,2
                //        1. U4 RPTID (报告ID)
                //        2. L,m (变量列表)
                //           V (变量值)

                var s6f11 = new SecsMessage(6, 11, true)
                {
                    Name = "EventReportSend",
                    SecsItem = Item.L(
                        Item.U4(0),                    // DATAID
                        Item.U4(eventData.Ceid),       // CEID
                        Item.L(reports)                // Reports
                    )
                };

                Logger.LogTrace($"构建S6F11消息完成 - CEID: {eventData.Ceid}, 报告数: {reports.Count}");
                return s6f11;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"构建S6F11消息失败 - CEID: {eventData.Ceid}");
                return null;
            }
        }

        /// <summary>
        /// 收集报告数据
        /// </summary>
        private async Task<Item> CollectReportData(
            uint rptid,
            EventData eventData,
            CancellationToken cancellationToken)
        {
            var variables = new List<Item>();

            try
            {
                // 获取报告定义
                var vidList = GetReportDefinition(rptid);

                if (vidList.Count == 0 && rptid == 0)
                {
                    // 空报告
                    return Item.L(
                        Item.U4(0),  // RPTID = 0
                        Item.L()     // 空变量列表
                    );
                }

                // 收集每个变量的值
                foreach (var vid in vidList)
                {
                    var value = await GetVariableValue(vid, eventData, cancellationToken);
                    variables.Add(value);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"收集报告 {rptid} 数据时发生错误");
            }

            return Item.L(
                Item.U4(rptid),        // RPTID
                Item.L(variables)      // Variable list
            );
        }

        /// <summary>
        /// 获取变量值
        /// </summary>
        private async Task<Item> GetVariableValue(
            uint vid,
            EventData eventData,
            CancellationToken cancellationToken)
        {
            try
            {
                // 优先从事件附加数据获取
                if (eventData.AdditionalData.TryGetValue(vid, out var additionalValue))
                {
                    return SecsItemHelper.ConvertToItem(additionalValue);
                }

                // 从状态变量服务获取
                var value = await _statusService.GetSvidValueAsync(vid);
                return SecsItemHelper.ConvertToItem(value);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"获取变量 {vid} 值失败");
                return Item.A(""); // 返回空字符串作为默认值
            }
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 将事件加入队列
        /// </summary>
        private void EnqueueEvent(EventData eventData)
        {
            lock (_queueLock)
            {
                // 检查队列大小限制
                if (_eventQueue.Count >= MaxQueueSize)
                {
                    Logger.LogWarning($"事件队列已满({MaxQueueSize})，丢弃最旧的事件");
                    _eventQueue.Dequeue();
                }

                _eventQueue.Enqueue(eventData);
                Logger.LogTrace($"事件 {eventData.Ceid} 已加入队列，当前队列大小: {_eventQueue.Count}");
            }
        }

        /// <summary>
        /// 检查事件是否启用
        /// </summary>
        private bool IsEventEnabled(uint ceid)
        {
            // 从S2F37处理器检查
            if (_enableHandler != null)
            {
                return _enableHandler.IsEventEnabled(ceid);
            }

            // 默认启用关键事件
            return IsHighPriorityEvent(ceid);
        }

        /// <summary>
        /// 获取事件链接的报告
        /// </summary>
        private List<uint> GetLinkedReports(uint ceid)
        {
            // 从S2F35处理器获取
            if (_linkHandler != null)
            {
                return _linkHandler.GetLinkedReports(ceid);
            }

            // 返回默认报告
            return new List<uint> { 1 }; // 默认报告ID
        }

        /// <summary>
        /// 获取报告定义
        /// </summary>
        private List<uint> GetReportDefinition(uint rptid)
        {
            // 从S2F33处理器获取
            if (_reportHandler != null)
            {
                return _reportHandler.
                    GetReportVariables(rptid);
            }

            // 返回默认变量列表
            return GetDefaultVariables(rptid);
        }

        /// <summary>
        /// 获取默认变量列表
        /// </summary>
        private List<uint> GetDefaultVariables(uint rptid)
        {
            // 根据报告ID返回默认变量
            return rptid switch
            {
                1 => new List<uint> { 1, 4, 721, 722 },  // Clock, PPExecName, ControlState, ProcessState
                2 => new List<uint> { 1, 720, 721 },      // Clock, ControlMode, ControlState
                3 => new List<uint> { 1 },                // Clock
                10 => new List<uint> { 1, 10012, 10011 }, // Clock, CassetteID, CurrentSlot
                _ => new List<uint> { 1 }                 // Clock
            };
        }

        /// <summary>
        /// 检查是否为高优先级事件
        /// </summary>
        private bool IsHighPriorityEvent(uint ceid)
        {
            return ceid switch
            {
                DicerEvents.AlarmSet => true,
                DicerEvents.AlarmClear => true,
                DicerEvents.ProcessAbort => true,
                DicerEvents.CommunicationFailed => true,
                DicerEvents.KnifeLifeExpired => true,
                DicerEvents.CoolingSystemAbnormal => true,
                DicerEvents.VacuumSystemAbnormal => true,
                _ => false
            };
        }

        /// <summary>
        /// 解析ACKC6值
        /// </summary>
        private byte ParseAckc6(Item? item)
        {
            if (item == null)
                return 255; // 无效值

            try
            {
                return item.Format switch
                {
                    SecsFormat.U1 => item.FirstValue<byte>(),
                    SecsFormat.Binary => item.GetMemory<byte>().ToArray()[0],
                    _ => 255
                };
            }
            catch
            {
                return 255;
            }
        }

        /// <summary>
        /// 缓存失败的事件
        /// </summary>
        private async Task CacheFailedEvent(EventData eventData)
        {
            try
            {
                await _eventService.CacheEventReportAsync(
                    eventData.Ceid,
                    eventData.EventName,
                    eventData.AdditionalData);

                Logger.LogWarning($"事件 {eventData.Ceid} 已缓存，等待重新发送");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"缓存事件 {eventData.Ceid} 失败");
            }
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 事件数据
        /// </summary>
        private class EventData
        {
            /// <summary>事件ID</summary>
            public uint Ceid { get; set; }

            /// <summary>事件名称</summary>
            public string EventName { get; set; } = "";

            /// <summary>时间戳</summary>
            public DateTime Timestamp { get; set; }

            /// <summary>附加数据（VID -> Value）</summary>
            public Dictionary<uint, object> AdditionalData { get; set; } = new();

            /// <summary>重试次数</summary>
            public int RetryCount { get; set; }
        }

        #endregion

        #region 清理

        /// <summary>
        /// 释放资源（重写基类方法）
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                    _eventProcessingTask?.Wait(5000);
                    _cancellationTokenSource?.Dispose();

                    Logger.LogInformation("S6F11处理器已释放");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "释放S6F11处理器时发生错误");
                }
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
