// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F35Handler.cs
// 版本: v1.0.0
// 描述: S2F35消息处理器 - Link Event Report 事件报告链接处理器

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
    /// S2F35 (Link Event Report) 处理器
    /// 处理主机的事件报告链接请求，建立或修改事件ID(CEID)与报告ID(RPTID)的关联关系
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S2F35: 链接事件报告 - 主机配置事件触发时发送的报告
    /// - S2F36: 链接事件报告确认 - 设备返回链接结果
    /// 
    /// 交互流程：
    /// 1. 主机先通过S2F33定义报告(RPTID)及其包含的变量(VID)
    /// 2. 主机发送S2F35将报告链接到特定事件(CEID)
    /// 3. 设备验证CEID和RPTID的有效性
    /// 4. 建立或更新事件与报告的关联关系
    /// 5. 返回S2F36确认链接结果(LRACK)
    /// 6. 当事件触发时，设备通过S6F11发送关联的报告数据
    /// 
    /// 划裂片设备事件链接规则：
    /// - 一个事件可以链接多个报告
    /// - 一个报告可以被多个事件使用
    /// - 空RPTID列表表示删除该事件的所有链接
    /// - CEID/RPTID列表都为空表示删除所有链接
    /// - 系统事件(200-299)必须保持至少一个报告链接
    /// - 设备重启会清除所有动态链接配置
    /// </remarks>
    public class S2F35Handler : SecsMessageHandlerBase
    {
        #region 链接确认代码定义

        /// <summary>
        /// 链接报告确认代码 (Link Report Acknowledge)
        /// </summary>
        private enum LinkReportAck : byte
        {
            /// <summary>成功接受</summary>
            Accepted = 0,

            /// <summary>拒绝 - 权限不足</summary>
            DeniedInsufficientPrivilege = 1,

            /// <summary>拒绝 - 至少一个CEID无效</summary>
            DeniedInvalidCeid = 2,

            /// <summary>拒绝 - 至少一个RPTID无效</summary>
            DeniedInvalidRptid = 3,

            /// <summary>拒绝 - 已经链接</summary>
            DeniedAlreadyLinked = 4,

            /// <summary>拒绝 - 系统错误</summary>
            DeniedSystemError = 5,

            /// <summary>拒绝 - 无效状态</summary>
            DeniedInvalidState = 6
        }

        #endregion

        #region 划裂片设备事件定义

        /// <summary>
        /// 划裂片设备事件分类
        /// </summary>
        private static class EventCategory
        {
            /// <summary>控制状态事件 (200-209)</summary>
            public const string Control = "Control";

            /// <summary>处理状态事件 (210-219)</summary>
            public const string Process = "Process";

            /// <summary>操作员动作事件 (220-229)</summary>
            public const string Operator = "Operator";

            /// <summary>报警事件 (230-239)</summary>
            public const string Alarm = "Alarm";

            /// <summary>通信事件 (240-249)</summary>
            public const string Communication = "Communication";

            /// <summary>材料处理事件 (11000-11099)</summary>
            public const string Material = "Material";

            /// <summary>工艺事件 (11100-11199)</summary>
            public const string Recipe = "Recipe";
        }

        #endregion

        #region 私有字段

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventService;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>事件链接存储（CEID -> RPTID列表）</summary>
        private readonly Dictionary<uint, HashSet<uint>> _eventLinks;

        /// <summary>系统事件定义（不可删除链接的事件）</summary>
        private readonly HashSet<uint> _systemEvents;

        /// <summary>链接更新锁</summary>
        private readonly ReaderWriterLockSlim _linkLock = new();

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 35;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="eventService">事件报告服务</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="options">设备系统配置</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S2F35Handler(
            ILogger<S2F35Handler> logger,
            IEventReportService eventService,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options) : base(logger)
        {
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // 初始化事件链接存储
            _eventLinks = new Dictionary<uint, HashSet<uint>>();

            // 初始化系统事件列表
            _systemEvents = InitializeSystemEvents();

            // 加载默认链接配置
            LoadDefaultLinks();

            Logger.LogInformation($"S2F35处理器已初始化，定义了 {_systemEvents.Count} 个系统事件");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S2F35 消息，返回 S2F36 响应
        /// </summary>
        /// <param name="message">接收到的S2F35消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F36响应消息</returns>
        /// <remarks>
        /// S2F35 处理逻辑：
        /// 1. 解析事件链接数据
        /// 2. 验证设备状态和权限
        /// 3. 验证CEID和RPTID有效性
        /// 4. 执行链接操作（添加/修改/删除）
        /// 5. 更新事件报告服务
        /// 6. 返回链接确认结果
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S2F35 (Link Event Report) 事件报告链接请求");

            try
            {
                // 解析链接数据
                var linkData = ParseLinkData(message.SecsItem);

                // 记录请求详情
                LogLinkRequest(linkData);

                // 验证设备状态
                var stateValidation = await ValidateDeviceState(cancellationToken);
                if (!stateValidation.CanModify)
                {
                    Logger.LogWarning($"当前状态不允许修改事件链接: {stateValidation.Reason}");
                    return CreateS2F36Response(LinkReportAck.DeniedInvalidState);
                }

                // 验证权限
                if (!await ValidatePermission(cancellationToken))
                {
                    Logger.LogWarning("权限不足，无法修改事件链接");
                    return CreateS2F36Response(LinkReportAck.DeniedInsufficientPrivilege);
                }

                // 处理特殊情况：全部删除
                if (IsDeleteAllRequest(linkData))
                {
                    var deleteAllResult = await ProcessDeleteAllLinks(cancellationToken);
                    return CreateS2F36Response(deleteAllResult);
                }

                // 验证并处理每个事件链接
                var result = await ProcessEventLinks(linkData, cancellationToken);

                // 如果成功，同步到事件报告服务
                if (result == LinkReportAck.Accepted)
                {
                    await SyncToEventService(cancellationToken);
                    Logger.LogInformation("事件链接配置已成功更新");
                }

                return CreateS2F36Response(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F35消息失败");
                return CreateS2F36Response(LinkReportAck.DeniedSystemError);
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析链接数据
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>事件链接数据列表</returns>
        private List<EventLinkData> ParseLinkData(Item? item)
        {
            var linkDataList = new List<EventLinkData>();

            if (item == null || item.Format != SecsFormat.List)
            {
                Logger.LogWarning("S2F35消息格式无效");
                return linkDataList;
            }

            var items = item.Items;
            if (items == null || items.Length == 0)
            {
                // 空列表表示删除所有链接
                return linkDataList;
            }

            // S2F35格式：L,n {L,2 {CEID, L,m {RPTID}}}
            foreach (var linkItem in items)
            {
                try
                {
                    if (linkItem.Format != SecsFormat.List || linkItem.Items?.Length != 2)
                    {
                        Logger.LogWarning("事件链接项格式无效");
                        continue;
                    }

                    var ceidItem = linkItem.Items[0];
                    var rptidListItem = linkItem.Items[1];

                    // 解析CEID
                    uint ceid = ParseCeid(ceidItem);
                    if (ceid == 0)
                    {
                        Logger.LogWarning("无效的CEID值");
                        continue;
                    }

                    // 解析RPTID列表
                    var rptidList = ParseRptidList(rptidListItem);

                    linkDataList.Add(new EventLinkData
                    {
                        Ceid = ceid,
                        RptidList = rptidList
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析事件链接项失败");
                }
            }

            return linkDataList;
        }

        /// <summary>
        /// 解析CEID
        /// </summary>
        private static uint ParseCeid(Item? item)
        {
            return SecsItemHelper.ParseCeid(item);
        }

        /// <summary>
        /// 解析RPTID列表
        /// </summary>
        private List<uint> ParseRptidList(Item item)
        {
            var rptidList = new List<uint>();

            if (item.Format != SecsFormat.List)
            {
                Logger.LogWarning("RPTID列表格式无效");
                return rptidList;
            }

            if (item.Items == null || item.Items.Length == 0)
            {
                // 空RPTID列表表示删除该事件的所有链接
                return rptidList;
            }

            foreach (var rptidItem in item.Items)
            {
                try
                {
                    uint rptid = rptidItem.Format switch
                    {
                        SecsFormat.U1 => rptidItem.FirstValue<byte>(),
                        SecsFormat.U2 => rptidItem.FirstValue<ushort>(),
                        SecsFormat.U4 => rptidItem.FirstValue<uint>(),
                        SecsFormat.I1 => (uint)Math.Max((sbyte)0, rptidItem.FirstValue<sbyte>()),
                        SecsFormat.I2 => (uint)Math.Max((short)0, rptidItem.FirstValue<short>()),
                        SecsFormat.I4 => (uint)Math.Max(0, rptidItem.FirstValue<int>()),
                        _ => 0
                    };

                    if (rptid > 0)
                    {
                        rptidList.Add(rptid);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析RPTID失败");
                }
            }

            return rptidList;
        }

        #endregion

        #region 私有方法 - 链接处理

        /// <summary>
        /// 处理事件链接
        /// </summary>
        private async Task<LinkReportAck> ProcessEventLinks(
            List<EventLinkData> linkDataList,
            CancellationToken cancellationToken)
        {
            // 验证所有CEID和RPTID
            var validationResult = await ValidateLinkData(linkDataList, cancellationToken);
            if (validationResult != LinkReportAck.Accepted)
            {
                return validationResult;
            }

            // 开始更新链接
            _linkLock.EnterWriteLock();
            try
            {
                foreach (var linkData in linkDataList)
                {
                    if (linkData.RptidList.Count == 0)
                    {
                        // 删除事件的所有链接
                        var deleteResult = DeleteEventLinks(linkData.Ceid);
                        if (deleteResult != LinkReportAck.Accepted)
                        {
                            return deleteResult;
                        }

                        Logger.LogDebug($"已删除CEID {linkData.Ceid} 的所有链接");
                    }
                    else
                    {
                        // 设置新的链接（覆盖旧链接）
                        SetEventLinks(linkData.Ceid, linkData.RptidList);

                        Logger.LogDebug($"已设置CEID {linkData.Ceid} 的链接: [{string.Join(",", linkData.RptidList)}]");
                    }
                }

                return LinkReportAck.Accepted;
            }
            finally
            {
                _linkLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 验证链接数据
        /// </summary>
        private async Task<LinkReportAck> ValidateLinkData(
            List<EventLinkData> linkDataList,
            CancellationToken cancellationToken)
        {
            // 验证所有CEID
            foreach (var linkData in linkDataList)
            {
                if (!IsValidCeid(linkData.Ceid))
                {
                    Logger.LogWarning($"无效的CEID: {linkData.Ceid}");
                    return LinkReportAck.DeniedInvalidCeid;
                }
            }

            // 验证所有RPTID
            var allRptids = linkDataList
                .SelectMany(l => l.RptidList)
                .Distinct()
                .ToList();

            foreach (var rptid in allRptids)
            {
                if (!await IsValidRptid(rptid, cancellationToken))
                {
                    Logger.LogWarning($"无效的RPTID: {rptid}");
                    return LinkReportAck.DeniedInvalidRptid;
                }
            }

            return LinkReportAck.Accepted;
        }

        /// <summary>
        /// 设置事件链接
        /// </summary>
        private void SetEventLinks(uint ceid, List<uint> rptidList)
        {
            if (!_eventLinks.ContainsKey(ceid))
            {
                _eventLinks[ceid] = new HashSet<uint>();
            }

            // 清除旧链接并设置新链接
            _eventLinks[ceid].Clear();
            foreach (var rptid in rptidList)
            {
                _eventLinks[ceid].Add(rptid);
            }
        }

        /// <summary>
        /// 删除事件链接
        /// </summary>
        private LinkReportAck DeleteEventLinks(uint ceid)
        {
            // 检查是否为系统事件
            if (_systemEvents.Contains(ceid))
            {
                Logger.LogWarning($"不能删除系统事件 {ceid} 的链接");
                return LinkReportAck.DeniedInsufficientPrivilege;
            }

            // 删除链接
            if (_eventLinks.ContainsKey(ceid))
            {
                _eventLinks.Remove(ceid);
            }

            return LinkReportAck.Accepted;
        }

        /// <summary>
        /// 处理删除所有链接请求
        /// </summary>
        private async Task<LinkReportAck> ProcessDeleteAllLinks(CancellationToken cancellationToken)
        {
            Logger.LogWarning("收到删除所有事件链接的请求");

            _linkLock.EnterWriteLock();
            try
            {
                // 保留系统事件的链接
                var systemEventLinks = _eventLinks
                    .Where(kvp => _systemEvents.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                // 清除所有链接
                _eventLinks.Clear();

                // 恢复系统事件链接
                foreach (var kvp in systemEventLinks)
                {
                    _eventLinks[kvp.Key] = kvp.Value;
                }

                Logger.LogInformation($"已删除所有非系统事件链接，保留了 {systemEventLinks.Count} 个系统事件链接");
                return LinkReportAck.Accepted;
            }
            finally
            {
                _linkLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 同步到事件报告服务
        /// </summary>
        private async Task SyncToEventService(CancellationToken cancellationToken)
        {
            try
            {
                _linkLock.EnterReadLock();
                var linksCopy = _eventLinks.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()
                );
                _linkLock.ExitReadLock();

                // 更新事件报告服务的链接配置
                foreach (var kvp in linksCopy)
                {
                    await _eventService.LinkEventReportAsync(kvp.Key, kvp.Value);
                }

                Logger.LogDebug("事件链接已同步到事件报告服务");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "同步事件链接失败");
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
            // ===== 新增：SEMI E30标准通信状态检查 =====
            // 首先检查通信是否已建立（S1F13/S1F14必须成功）
            var commEnabled = await _stateService.IsCommunicationEnabledAsync();
            if (!commEnabled)
            {
                return (false, "通信未建立（需要先成功完成S1F13/S1F14）");
            }

            // 检查控制状态（S1F17/S1F18必须成功）
            var controlState = await _stateService.GetControlStateAsync();
            if (controlState == ControlState.EquipmentOffline)
            {
                return (false, "设备处于离线状态（需要先成功完成S1F17/S1F18）");
            }
            // ===== 结束新增 =====

            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 必须在线才能修改
            if (!statusInfo.IsOnline)
            {
                return (false, "设备未在线");
            }

            // 处理中可以修改（动态配置）
            // 但某些关键状态可能需要限制
            if (statusInfo.EquipmentState == EquipmentState.UnscheduledDown)
            {
                return (false, "设备处于故障状态");
            }

            return (true, "");
        }

        /// <summary>
        /// 验证权限
        /// </summary>
        private async Task<bool> ValidatePermission(CancellationToken cancellationToken)
        {
            // 检查是否有配置权限
            // 这里简化处理，实际应根据用户权限判断
            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 远程模式下允许Host配置
            if (statusInfo.ControlMode == ControlMode.Remote)
            {
                return true;
            }

            // 本地模式下需要特殊权限
            return false;
        }

        /// <summary>
        /// 验证CEID有效性
        /// </summary>
        private bool IsValidCeid(uint ceid)
        {
            // 验证CEID是否在支持的范围内
            // 划裂片设备支持的CEID范围
            return ceid switch
            {
                >= 200 and <= 249 => true,      // 系统事件
                >= 300 and <= 399 => true,      // 处理事件
                >= 11000 and <= 11199 => true,  // 材料和工艺事件
                _ => false
            };
        }

        /// <summary>
        /// 验证RPTID有效性
        /// </summary>
        private async Task<bool> IsValidRptid(uint rptid, CancellationToken cancellationToken)
        {
            try
            {
                // 从事件报告服务验证RPTID是否已定义
                var definedReports = await _eventService.GetDefinedReportsAsync();
                return definedReports.Contains(rptid);
            }
            catch
            {
                // 如果服务不可用，进行基本范围检查
                return rptid > 0 && rptid <= 65535;
            }
        }

        /// <summary>
        /// 检查是否为删除所有链接的请求
        /// </summary>
        private bool IsDeleteAllRequest(List<EventLinkData> linkDataList)
        {
            return linkDataList.Count == 0;
        }

        #endregion

        #region 私有方法 - 初始化

        /// <summary>
        /// 初始化系统事件列表
        /// </summary>
        private HashSet<uint> InitializeSystemEvents()
        {
            return new HashSet<uint>
            {
                200, // ControlStateOFFLINE
                201, // ControlStateLOCAL
                202, // ControlStateREMOTE
                220, // OperatorEquipmentConstantChange
                240  // MessageRecognition
            };
        }

        /// <summary>
        /// 加载默认链接配置
        /// </summary>
        private void LoadDefaultLinks()
        {
            // 为系统事件设置默认链接
            _eventLinks[200] = new HashSet<uint> { 1 }; // 离线状态报告
            _eventLinks[201] = new HashSet<uint> { 1 }; // 本地状态报告
            _eventLinks[202] = new HashSet<uint> { 1 }; // 远程状态报告
            _eventLinks[220] = new HashSet<uint> { 2 }; // 常量改变报告
            _eventLinks[240] = new HashSet<uint> { 3 }; // 消息确认报告

            // 材料处理事件默认链接
            _eventLinks[11000] = new HashSet<uint> { 10 }; // MaterialArrival
            _eventLinks[11001] = new HashSet<uint> { 11 }; // MaterialRemoved
            _eventLinks[11002] = new HashSet<uint> { 12 }; // MapComplete
            _eventLinks[11003] = new HashSet<uint> { 13 }; // PPSelected
            _eventLinks[11004] = new HashSet<uint> { 14 }; // ProcessStart
            _eventLinks[11005] = new HashSet<uint> { 15 }; // ProcessEnd

            Logger.LogDebug($"已加载 {_eventLinks.Count} 个默认事件链接");
        }

        /// <summary>
        /// 记录链接请求
        /// </summary>
        private void LogLinkRequest(List<EventLinkData> linkDataList)
        {
            if (!Logger.IsEnabled(LogLevel.Debug))
                return;

            if (linkDataList.Count == 0)
            {
                Logger.LogDebug("请求删除所有事件链接");
                return;
            }

            foreach (var linkData in linkDataList)
            {
                if (linkData.RptidList.Count == 0)
                {
                    Logger.LogDebug($"请求删除CEID {linkData.Ceid} 的所有链接");
                }
                else
                {
                    Logger.LogDebug($"请求设置CEID {linkData.Ceid} 链接到RPTID: [{string.Join(",", linkData.RptidList)}]");
                }
            }
        }

        #endregion

        #region 私有方法 - 响应构建

        /// <summary>
        /// 创建S2F36响应
        /// </summary>
        private SecsMessage CreateS2F36Response(LinkReportAck lrack)
        {
            Logger.LogDebug($"返回链接确认: {lrack}");

            return new SecsMessage(2, 36, false)
            {
                Name = "LinkEventReportAcknowledge",
                SecsItem = Item.U1((byte)lrack)
            };
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 事件链接数据
        /// </summary>
        private class EventLinkData
        {
            /// <summary>事件ID</summary>
            public uint Ceid { get; set; }

            /// <summary>报告ID列表</summary>
            public List<uint> RptidList { get; set; } = new List<uint>();
        }

        #endregion

        #region 公共方法（供其他Handler使用）

        /// <summary>
        /// 获取事件的链接报告（供S6F11使用）
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <returns>链接的报告ID列表</returns>
        public List<uint> GetLinkedReports(uint ceid)
        {
            _linkLock.EnterReadLock();
            try
            {
                return _eventLinks.TryGetValue(ceid, out var rptids)
                    ? rptids.ToList()
                    : new List<uint>();
            }
            finally
            {
                _linkLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取所有事件链接（供查询使用）
        /// </summary>
        /// <returns>事件链接字典</returns>
        public Dictionary<uint, List<uint>> GetAllEventLinks()
        {
            _linkLock.EnterReadLock();
            try
            {
                return _eventLinks.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()
                );
            }
            finally
            {
                _linkLock.ExitReadLock();
            }
        }

        #endregion
    }
}
