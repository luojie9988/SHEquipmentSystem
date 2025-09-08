// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F37Handler.cs
// 版本: v1.0.0
// 描述: S2F37消息处理器 - Enable/Disable Event Report 事件报告启用/禁用处理器

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Constants;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S2F37 (Enable/Disable Event Report) 处理器
    /// 处理主机的事件报告启用/禁用请求，控制哪些事件在发生时会触发S6F11报告
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S2F37: 启用/禁用事件报告 - 主机控制事件报告的发送
    /// - S2F38: 启用/禁用事件报告确认 - 设备返回操作结果
    /// 
    /// 交互流程：
    /// 1. 主机先通过S2F33定义报告(RPTID)
    /// 2. 主机通过S2F35链接事件(CEID)与报告
    /// 3. 主机发送S2F37启用或禁用特定事件的报告功能
    /// 4. 设备验证CEID有效性和当前链接状态
    /// 5. 更新事件的启用/禁用状态
    /// 6. 返回S2F38确认结果(ERACK)
    /// 7. 只有启用的事件在触发时才会发送S6F11
    /// 
    /// 划裂片设备事件管理规则：
    /// - CEED=TRUE: 启用指定事件的报告
    /// - CEED=FALSE: 禁用指定事件的报告
    /// - 空CEID列表+CEED=TRUE: 启用所有已链接的事件
    /// - 空CEID列表+CEED=FALSE: 禁用所有事件（除强制事件外）
    /// - 某些关键事件（如报警）不能被禁用
    /// - 设备重启后恢复默认启用状态
    /// </remarks>
    public class S2F37Handler : SecsMessageHandlerBase
    {
        #region 确认代码定义

        /// <summary>
        /// 启用/禁用事件报告确认代码 (Enable Report Acknowledge)
        /// </summary>
        private enum EnableReportAck : byte
        {
            /// <summary>接受</summary>
            Accepted = 0,

            /// <summary>拒绝 - 至少一个CEID无效</summary>
            DeniedInvalidCeid = 1,

            /// <summary>拒绝 - 忙</summary>
            DeniedBusy = 2,

            /// <summary>拒绝 - 权限不足</summary>
            DeniedNoPermission = 3,

            /// <summary>拒绝 - 无效状态</summary>
            DeniedInvalidState = 4,

            /// <summary>拒绝 - 事件未链接</summary>
            DeniedNotLinked = 5,

            /// <summary>拒绝 - 强制事件</summary>
            DeniedMandatoryEvent = 6
        }

        #endregion

        #region 事件分类定义

        /// <summary>
        /// 事件优先级分类
        /// </summary>
        private static class EventPriority
        {
            /// <summary>强制事件（不能被禁用）</summary>
            public const string Mandatory = "Mandatory";

            /// <summary>关键事件（默认启用）</summary>
            public const string Critical = "Critical";

            /// <summary>普通事件（可选）</summary>
            public const string Normal = "Normal";

            /// <summary>调试事件（默认禁用）</summary>
            public const string Debug = "Debug";
        }

        #endregion

        #region 私有字段

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>S2F35处理器（获取链接信息）</summary>
        private readonly S2F35Handler? _linkHandler;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>事件启用状态存储（CEID -> 是否启用）</summary>
        private readonly Dictionary<uint, bool> _eventEnabledStatus;

        /// <summary>强制启用的事件（不能被禁用）</summary>
        private readonly HashSet<uint> _mandatoryEvents;

        /// <summary>默认启用的事件</summary>
        private readonly HashSet<uint> _defaultEnabledEvents;

        /// <summary>状态更新锁</summary>
        private readonly ReaderWriterLockSlim _statusLock = new();

        /// <summary>启用状态变更历史</summary>
        private readonly Queue<EnableStatusChange> _changeHistory;

        /// <summary>最大历史记录数</summary>
        private const int MaxHistoryCount = 100;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 37;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="eventService">事件报告服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="serviceProvider">服务提供者</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S2F37Handler(
            ILogger<S2F37Handler> logger,
            IEventReportService eventService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            IServiceProvider serviceProvider) : base(logger)
        {
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // 尝试获取S2F35Handler实例
            _linkHandler = serviceProvider.GetService(typeof(S2F35Handler)) as S2F35Handler;

            // 初始化事件状态存储
            _eventEnabledStatus = new Dictionary<uint, bool>();
            _changeHistory = new Queue<EnableStatusChange>();

            // 初始化事件分类
            _mandatoryEvents = InitializeMandatoryEvents();
            _defaultEnabledEvents = InitializeDefaultEnabledEvents();

            // 设置初始状态
            InitializeEventStatus();

            Logger.LogInformation($"S2F37处理器已初始化，" +
                $"强制事件: {_mandatoryEvents.Count} 个，" +
                $"默认启用事件: {_defaultEnabledEvents.Count} 个");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S2F37 消息，返回 S2F38 响应
        /// </summary>
        /// <param name="message">接收到的S2F37消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F38响应消息</returns>
        /// <remarks>
        /// S2F37 处理逻辑：
        /// 1. 解析启用/禁用请求
        /// 2. 验证设备状态和权限
        /// 3. 验证CEID有效性和链接状态
        /// 4. 检查强制事件限制
        /// 5. 更新事件启用状态
        /// 6. 同步到事件报告服务
        /// 7. 返回确认结果
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S2F37 (Enable/Disable Event Report) 事件报告启用/禁用请求");

            try
            {
                // 解析请求数据
                var (ceed, ceidList) = ParseEnableDisableRequest(message.SecsItem);

                // 记录请求详情
                LogEnableRequest(ceed, ceidList);

                // 验证设备状态
                var stateValidation = await ValidateDeviceState(cancellationToken);
                if (!stateValidation.CanModify)
                {
                    Logger.LogWarning($"当前状态不允许修改事件启用状态: {stateValidation.Reason}");
                    return CreateS2F38Response(EnableReportAck.DeniedInvalidState);
                }

                // 验证权限
                if (!await ValidatePermission(cancellationToken))
                {
                    Logger.LogWarning("权限不足，无法修改事件启用状态");
                    return CreateS2F38Response(EnableReportAck.DeniedNoPermission);
                }

                // 处理启用/禁用请求
                EnableReportAck result;

                if (ceidList.Count == 0)
                {
                    // 空列表表示操作所有事件
                    result = await ProcessAllEvents(ceed, cancellationToken);
                }
                else
                {
                    // 操作指定事件
                    result = await ProcessSpecificEvents(ceed, ceidList, cancellationToken);
                }

                // 如果成功，同步到事件报告服务
                if (result == EnableReportAck.Accepted)
                {
                    await SyncToEventService(cancellationToken);

                    // 记录变更历史
                    RecordChange(ceed, ceidList);

                    Logger.LogInformation($"事件启用状态已更新 - " +
                        $"操作: {(ceed ? "启用" : "禁用")}, " +
                        $"事件数: {(ceidList.Count == 0 ? "全部" : ceidList.Count.ToString())}");
                }

                return CreateS2F38Response(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F37消息失败");
                return CreateS2F38Response(EnableReportAck.DeniedBusy);
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析启用/禁用请求
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>(启用标志, CEID列表)</returns>
        private (bool ceed, List<uint> ceidList) ParseEnableDisableRequest(Item? item)
        {
            if (item == null || item.Format != SecsFormat.List || item.Items?.Length != 2)
            {
                Logger.LogWarning("S2F37消息格式无效");
                return (false, new List<uint>());
            }

            // S2F37格式：L,2 {CEED, L,n {CEID}}
            var ceedItem = item.Items[0];
            var ceidListItem = item.Items[1];

            // 解析CEED (Collection Event Enable/Disable)
            bool ceed = ParseCeed(ceedItem);

            // 解析CEID列表
            var ceidList = ParseCeidList(ceidListItem);

            return (ceed, ceidList);
        }

        /// <summary>
        /// 解析CEED值
        /// </summary>
        private bool ParseCeed(Item item)
        {
            return item.Format switch
            {
                SecsFormat.Boolean => item.FirstValue<bool>(),
                SecsFormat.U1 => item.FirstValue<byte>() != 0,
                SecsFormat.U2 => item.FirstValue<ushort>() != 0,
                SecsFormat.U4 => item.FirstValue<uint>() != 0,
                SecsFormat.I1 => item.FirstValue<sbyte>() != 0,
                SecsFormat.I2 => item.FirstValue<short>() != 0,
                SecsFormat.I4 => item.FirstValue<int>() != 0,
                _ => false
            };
        }

        /// <summary>
        /// 解析CEID列表
        /// </summary>
        private List<uint> ParseCeidList(Item item)
        {
            var ceidList = new List<uint>();

            if (item.Format != SecsFormat.List)
            {
                Logger.LogWarning("CEID列表格式无效");
                return ceidList;
            }

            if (item.Items == null || item.Items.Length == 0)
            {
                // 空列表表示所有事件
                return ceidList;
            }

            foreach (var ceidItem in item.Items)
            {
                try
                {
                    uint ceid = ceidItem.Format switch
                    {
                        SecsFormat.U1 => ceidItem.FirstValue<byte>(),
                        SecsFormat.U2 => ceidItem.FirstValue<ushort>(),
                        SecsFormat.U4 => ceidItem.FirstValue<uint>(),
                        SecsFormat.I1 => (uint)Math.Max((sbyte)0, ceidItem.FirstValue<sbyte>()),
                        SecsFormat.I2 => (uint)Math.Max((short)0, ceidItem.FirstValue<short>()),
                        SecsFormat.I4 => (uint)Math.Max(0, ceidItem.FirstValue<int>()),
                        _ => 0
                    };

                    if (ceid > 0)
                    {
                        ceidList.Add(ceid);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析CEID失败");
                }
            }

            return ceidList;
        }

        #endregion

        #region 私有方法 - 事件处理

        /// <summary>
        /// 处理所有事件的启用/禁用
        /// </summary>
        private async Task<EnableReportAck> ProcessAllEvents(
            bool ceed,
            CancellationToken cancellationToken)
        {
            Logger.LogDebug($"处理所有事件 - 操作: {(ceed ? "启用" : "禁用")}");

            _statusLock.EnterWriteLock();
            try
            {
                // 获取所有已链接的事件
                var linkedEvents = await GetLinkedEvents(cancellationToken);

                if (ceed)
                {
                    // 启用所有已链接的事件
                    foreach (var ceid in linkedEvents)
                    {
                        _eventEnabledStatus[ceid] = true;
                    }

                    Logger.LogDebug($"已启用 {linkedEvents.Count} 个事件");
                }
                else
                {
                    // 禁用所有事件（除强制事件外）
                    foreach (var ceid in linkedEvents)
                    {
                        if (!_mandatoryEvents.Contains(ceid))
                        {
                            _eventEnabledStatus[ceid] = false;
                        }
                    }

                    Logger.LogDebug($"已禁用非强制事件，保留 {_mandatoryEvents.Count} 个强制事件");
                }

                return EnableReportAck.Accepted;
            }
            finally
            {
                _statusLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 处理特定事件的启用/禁用
        /// </summary>
        private async Task<EnableReportAck> ProcessSpecificEvents(
            bool ceed,
            List<uint> ceidList,
            CancellationToken cancellationToken)
        {
            Logger.LogDebug($"处理 {ceidList.Count} 个特定事件 - 操作: {(ceed ? "启用" : "禁用")}");

            // 验证所有CEID
            var validationResult = await ValidateCeids(ceidList, cancellationToken);
            if (validationResult != EnableReportAck.Accepted)
            {
                return validationResult;
            }

            // 检查强制事件限制
            if (!ceed)
            {
                var mandatoryInList = ceidList.Where(c => _mandatoryEvents.Contains(c)).ToList();
                if (mandatoryInList.Any())
                {
                    Logger.LogWarning($"尝试禁用强制事件: [{string.Join(",", mandatoryInList)}]");
                    return EnableReportAck.DeniedMandatoryEvent;
                }
            }

            // 更新事件状态
            _statusLock.EnterWriteLock();
            try
            {
                foreach (var ceid in ceidList)
                {
                    _eventEnabledStatus[ceid] = ceed;
                    Logger.LogTrace($"CEID {ceid} 状态更新为: {(ceed ? "启用" : "禁用")}");
                }

                return EnableReportAck.Accepted;
            }
            finally
            {
                _statusLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 验证CEID列表
        /// </summary>
        private async Task<EnableReportAck> ValidateCeids(
            List<uint> ceidList,
            CancellationToken cancellationToken)
        {
            // 获取已链接的事件
            var linkedEvents = await GetLinkedEvents(cancellationToken);

            foreach (var ceid in ceidList)
            {
                // 验证CEID是否有效
                if (!IsValidCeid(ceid))
                {
                    Logger.LogWarning($"无效的CEID: {ceid}");
                    return EnableReportAck.DeniedInvalidCeid;
                }

                // 验证CEID是否已链接
                if (!linkedEvents.Contains(ceid))
                {
                    Logger.LogWarning($"CEID {ceid} 未链接任何报告");
                    return EnableReportAck.DeniedNotLinked;
                }
            }

            return EnableReportAck.Accepted;
        }

        /// <summary>
        /// 同步到事件报告服务
        /// </summary>
        private async Task SyncToEventService(CancellationToken cancellationToken)
        {
            try
            {
                _statusLock.EnterReadLock();
                var statusCopy = _eventEnabledStatus.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value
                );
                _statusLock.ExitReadLock();

                // 更新事件报告服务的启用状态
                foreach (var kvp in statusCopy)
                {
                    await _eventService.EnableEventAsync(kvp.Key, kvp.Value);
                }

                Logger.LogDebug("事件启用状态已同步到事件报告服务");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步事件启用状态失败");
            }
        }

        #endregion

        #region 私有方法 - 验证功能

        /// <summary>
        /// 验证设备状态
        /// </summary>
        private async Task<(bool CanModify, string Reason)> ValidateDeviceState(
            CancellationToken cancellationToken)
        {
            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 必须在线才能修改
            if (!statusInfo.IsOnline)
            {
                return (false, "设备未在线");
            }

            // 允许在处理中动态修改
            // 某些特殊状态可能需要限制
            if (statusInfo.EquipmentState == EquipmentState.UnscheduledDown)
            {
                // 故障状态下仍允许修改事件配置
                Logger.LogDebug("设备处于故障状态，但允许修改事件配置");
            }

            return (true, "");
        }

        /// <summary>
        /// 验证权限
        /// </summary>
        private async Task<bool> ValidatePermission(CancellationToken cancellationToken)
        {
            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 远程模式下允许Host配置
            if (statusInfo.ControlMode == ControlMode.Remote)
            {
                return true;
            }

            // 本地模式下需要检查操作员权限
            // 这里简化处理
            return statusInfo.ControlMode == ControlMode.Local;
        }

        /// <summary>
        /// 验证CEID有效性
        /// </summary>
        private bool IsValidCeid(uint ceid)
        {
            // 验证CEID是否在支持的范围内
            return ceid switch
            {
                >= 200 and <= 249 => true,      // 系统事件
                >= 11000 and <= 11199 => true,  // 材料和工艺事件
                _ => false
            };
        }

        /// <summary>
        /// 获取已链接的事件
        /// </summary>
        private async Task<HashSet<uint>> GetLinkedEvents(CancellationToken cancellationToken)
        {
            var linkedEvents = new HashSet<uint>();

            // 从S2F35Handler获取链接信息
            if (_linkHandler != null)
            {
                var allLinks = _linkHandler.GetAllEventLinks();
                foreach (var ceid in allLinks.Keys)
                {
                    linkedEvents.Add(ceid);
                }
            }
            else
            {
                // 如果没有链接处理器，使用默认事件列表
                linkedEvents.UnionWith(_defaultEnabledEvents);
                linkedEvents.UnionWith(_mandatoryEvents);
            }

            return linkedEvents;
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化强制事件列表
        /// </summary>
        private HashSet<uint> InitializeMandatoryEvents()
        {
            return new HashSet<uint>
            {
                200, // ControlStateOFFLINE - 控制状态变化必须报告
                201, // ControlStateLOCAL
                202, // ControlStateREMOTE
                230, // AlarmSet - 报警必须报告
                231, // AlarmClear
                240  // MessageRecognition - 消息确认
            };
        }

        /// <summary>
        /// 初始化默认启用的事件
        /// </summary>
        private HashSet<uint> InitializeDefaultEnabledEvents()
        {
            var defaultEnabled = new HashSet<uint>(_mandatoryEvents);

            // 添加其他默认启用的事件
            defaultEnabled.UnionWith(new[]
            {
                301u,// 处理开始
                302u,// 处理结束
                220u, // OperatorEquipmentConstantChange
                11000u, // MaterialArrival
                11001u, // MaterialRemoved
                11002u, // MapComplete
                11003u, // PPSelected
                11004u, // ProcessStart
                11005u, // ProcessEnd
                11011u, // SlotMapEnd
                11012u, // FrameStart
                11013u, // FrameEnd
                11014u  // CST.ST
            });
            return defaultEnabled;
        }

        /// <summary>
        /// 初始化事件状态
        /// </summary>
        private void InitializeEventStatus()
        {
            // 设置默认状态
            foreach (var ceid in _defaultEnabledEvents)
            {
                _eventEnabledStatus[ceid] = true;
            }

            // 其他支持的事件默认禁用
            for (uint ceid = 203; ceid <= 249; ceid++)
            {
                if (!_eventEnabledStatus.ContainsKey(ceid))
                {
                    _eventEnabledStatus[ceid] = false;
                }
            }

            for (uint ceid = 11015; ceid <= 11199; ceid++)
            {
                if (!_eventEnabledStatus.ContainsKey(ceid))
                {
                    _eventEnabledStatus[ceid] = false;
                }
            }
   
            Logger.LogDebug($"初始化事件状态 - " +
                $"启用: {_eventEnabledStatus.Count(kvp => kvp.Value)} 个, " +
                $"禁用: {_eventEnabledStatus.Count(kvp => !kvp.Value)} 个");
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 记录启用请求
        /// </summary>
        private void LogEnableRequest(bool ceed, List<uint> ceidList)
        {
            if (!Logger.IsEnabled(LogLevel.Debug))
                return;

            var operation = ceed ? "启用" : "禁用";

            if (ceidList.Count == 0)
            {
                Logger.LogDebug($"请求{operation}所有事件");
            }
            else
            {
                Logger.LogDebug($"请求{operation} {ceidList.Count} 个事件: [{string.Join(",", ceidList)}]");
            }
        }

        /// <summary>
        /// 记录变更历史
        /// </summary>
        private void RecordChange(bool ceed, List<uint> ceidList)
        {
            var change = new EnableStatusChange
            {
                Timestamp = DateTime.Now,
                Enable = ceed,
                CeidList = ceidList.ToList(),
                Operator = "Host"
            };

            lock (_changeHistory)
            {
                _changeHistory.Enqueue(change);

                // 限制历史记录数量
                while (_changeHistory.Count > MaxHistoryCount)
                {
                    _changeHistory.Dequeue();
                }
            }
        }

        /// <summary>
        /// 创建S2F38响应
        /// </summary>
        private SecsMessage CreateS2F38Response(EnableReportAck erack)
        {
            Logger.LogDebug($"返回启用确认: {erack}");

            return new SecsMessage(2, 38, false)
            {
                Name = "EnableDisableEventReportAcknowledge",
                SecsItem = Item.U1((byte)erack)
            };
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 启用状态变更记录
        /// </summary>
        private class EnableStatusChange
        {
            /// <summary>时间戳</summary>
            public DateTime Timestamp { get; set; }

            /// <summary>启用/禁用</summary>
            public bool Enable { get; set; }

            /// <summary>影响的CEID列表</summary>
            public List<uint> CeidList { get; set; } = new List<uint>();

            /// <summary>操作者</summary>
            public string Operator { get; set; } = "";
        }

        #endregion

        #region 公共方法（供其他Handler使用）

        /// <summary>
        /// 检查事件是否启用（供S6F11使用）
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <returns>是否启用</returns>
        public bool IsEventEnabled(uint ceid)
        {
            _statusLock.EnterReadLock();
            try
            {
                // 强制事件始终启用
                if (_mandatoryEvents.Contains(ceid))
                {
                    return true;
                }

                // 返回存储的状态
                return _eventEnabledStatus.TryGetValue(ceid, out var enabled) && enabled;
            }
            finally
            {
                _statusLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取所有启用的事件（供查询使用）
        /// </summary>
        /// <returns>启用的事件ID列表</returns>
        public List<uint> GetEnabledEvents()
        {
            _statusLock.EnterReadLock();
            try
            {
                return _eventEnabledStatus
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }
            finally
            {
                _statusLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取事件启用状态统计
        /// </summary>
        /// <returns>状态统计信息</returns>
        public (int TotalEvents, int EnabledEvents, int DisabledEvents, int MandatoryEvents) GetStatistics()
        {
            _statusLock.EnterReadLock();
            try
            {
                var total = _eventEnabledStatus.Count;
                var enabled = _eventEnabledStatus.Count(kvp => kvp.Value);
                var disabled = total - enabled;
                var mandatory = _mandatoryEvents.Count;

                return (total, enabled, disabled, mandatory);
            }
            finally
            {
                _statusLock.ExitReadLock();
            }
        }

        #endregion
    }
}
