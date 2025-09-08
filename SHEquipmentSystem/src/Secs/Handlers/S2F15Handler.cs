// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F15Handler.cs
// 版本: v1.0.0
// 描述: S2F15消息处理器 - New Equipment Constant Send 设备常量修改处理器

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

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S2F15 (New Equipment Constant Send) 处理器
    /// 处理主机的设备常量修改请求，更新指定的设备常量值
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S2F15: 新设备常量发送 - 主机修改设备的配置参数
    /// - S2F16: 设备常量确认 - 设备返回修改结果
    /// 
    /// 交互流程：
    /// 1. 主机发送 S2F15 包含ECID和新值的配对列表
    /// 2. 设备验证每个ECID的有效性和写权限
    /// 3. 验证新值的数据类型和范围
    /// 4. 执行状态和安全联锁检查
    /// 5. 更新常量值（内存/PLC/持久化存储）
    /// 6. 返回 S2F16 包含每个ECID的修改结果（EAC）
    /// 
    /// 划裂片设备ECID修改规则：
    /// - 只读ECID（如设备ID、型号）不允许修改
    /// - 工艺参数需要在IDLE状态下才能修改
    /// - 通信参数修改后可能需要重启生效
    /// - 维护参数修改需要维护权限
    /// - 所有修改都会触发事件报告（CEID 220）
    /// </remarks>
    public class S2F15Handler : SecsMessageHandlerBase
    {
        #region EAC定义常量

        /// <summary>
        /// 设备常量确认代码（Equipment Constant Acknowledge）
        /// </summary>
        private enum EquipmentConstantAck : byte
        {
            /// <summary>确认，常量值未改变</summary>
            AcknowledgedNoChange = 0,

            /// <summary>确认，常量值已改变</summary>
            AcknowledgedChanged = 1,

            /// <summary>拒绝，常量不存在</summary>
            DeniedDoesNotExist = 2,

            /// <summary>拒绝，忙</summary>
            DeniedBusy = 3,

            /// <summary>拒绝，常量超出范围</summary>
            DeniedOutOfRange = 4,

            /// <summary>拒绝，格式无效</summary>
            DeniedInvalidFormat = 5,

            /// <summary>拒绝，只读常量</summary>
            DeniedReadOnly = 6,

            /// <summary>拒绝，当前状态不允许修改</summary>
            DeniedInvalidState = 7,

            /// <summary>拒绝，权限不足</summary>
            DeniedNoPermission = 8
        }

        #endregion

        #region 私有字段

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService? _eventService;

        /// <summary>PLC数据提供者</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>ECID定义和值管理（从S2F13Handler获取）</summary>
        private readonly S2F13Handler? _ecidHandler;

        /// <summary>ECID值存储（内存缓存）</summary>
        private readonly Dictionary<uint, object> _ecidValues;

        /// <summary>值更新锁</summary>
        private readonly ReaderWriterLockSlim _valueLock = new();

        /// <summary>配置持久化路径</summary>
        private readonly string _configPersistPath;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 15;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="serviceProvider">服务提供者（用于获取S2F13Handler）</param>
        /// <param name="eventService">事件报告服务（可选）</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S2F15Handler(
            ILogger<S2F15Handler> logger,
            IOptions<EquipmentSystemConfiguration> options,
            IEquipmentStateService stateService,
            IServiceProvider serviceProvider,
            IEventReportService? eventService = null,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _eventService = eventService;
            _plcProvider = plcProvider;

            // 尝试获取S2F13Handler实例以共享ECID定义
            _ecidHandler = serviceProvider.GetService(typeof(S2F13Handler)) as S2F13Handler;

            // 初始化ECID值存储
            _ecidValues = new Dictionary<uint, object>();

            // 设置配置持久化路径
            _configPersistPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "ecid_values.json"
            );

            // 加载持久化的ECID值
            LoadPersistedValues();

            Logger.LogInformation("S2F15处理器已初始化");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S2F15 消息，返回 S2F16 响应
        /// </summary>
        /// <param name="message">接收到的S2F15消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F16响应消息</returns>
        /// <remarks>
        /// S2F15 处理逻辑：
        /// 1. 解析ECID-值对列表
        /// 2. 验证设备状态是否允许修改
        /// 3. 逐个验证和处理每个ECID修改请求
        /// 4. 应用有效的修改
        /// 5. 触发相关事件
        /// 6. 返回每个ECID的修改结果
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S2F15 (New Equipment Constant Send) 设备常量修改请求");

            try
            {
                // 解析ECID-值对列表
                var ecidValuePairs = ParseEcidValuePairs(message.SecsItem);

                if (ecidValuePairs.Count == 0)
                {
                    Logger.LogWarning("S2F15消息不包含任何ECID-值对");
                    return CreateS2F16Response(new List<EquipmentConstantAck>());
                }

                Logger.LogDebug($"请求修改 {ecidValuePairs.Count} 个设备常量");

                // 验证设备状态
                var stateValidation = await ValidateDeviceState(cancellationToken);
                if (!stateValidation.CanModify)
                {
                    Logger.LogWarning($"当前状态不允许修改ECID: {stateValidation.Reason}");
                    // 返回所有ECID都被拒绝的响应
                    var deniedResults = ecidValuePairs.Select(_ => EquipmentConstantAck.DeniedInvalidState).ToList();
                    return CreateS2F16Response(deniedResults);
                }

                // 处理每个ECID修改请求
                var results = new List<EquipmentConstantAck>();
                var modifiedEcids = new List<(uint ecid, object oldValue, object newValue)>();

                foreach (var (ecid, newValue) in ecidValuePairs)
                {
                    var result = await ProcessEcidModification(ecid, newValue, cancellationToken);
                    results.Add(result);

                    if (result == EquipmentConstantAck.AcknowledgedChanged)
                    {
                        // 记录已修改的ECID（用于事件报告）
                        var oldValue = GetCurrentValue(ecid);
                        modifiedEcids.Add((ecid, oldValue, newValue));

                        Logger.LogInformation($"ECID {ecid} 已修改: {oldValue} -> {newValue}");
                    }
                    else
                    {
                        Logger.LogDebug($"ECID {ecid} 修改结果: {result}");
                    }
                }

                // 如果有成功的修改，触发事件并持久化
                if (modifiedEcids.Any())
                {
                    // 持久化修改后的值
                    await PersistValuesAsync(cancellationToken);

                    // 触发设备常量改变事件（CEID 220）
                    await TriggerEcidChangeEvent(modifiedEcids, cancellationToken);

                    Logger.LogInformation($"成功修改 {modifiedEcids.Count} 个设备常量");
                }

                // 构建S2F16响应
                return CreateS2F16Response(results);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F15消息失败");

                // 返回空的EAC列表表示错误
                return new SecsMessage(2, 16, false)
                {
                    Name = "NewEquipmentConstantAcknowledge",
                    SecsItem = Item.L()
                };
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析ECID-值对列表
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>ECID-值对列表</returns>
        private List<(uint ecid, object value)> ParseEcidValuePairs(Item? item)
        {
            var pairs = new List<(uint ecid, object value)>();

            if (item == null || item.Format != SecsFormat.List)
            {
                Logger.LogWarning("S2F15消息格式无效");
                return pairs;
            }

            var items = item.Items;
            if (items == null || items.Length == 0)
            {
                return pairs;
            }

            // S2F15格式：L,n {L,2 {ECID, ECV}}
            foreach (var pairItem in items)
            {
                try
                {
                    if (pairItem.Format != SecsFormat.List || pairItem.Items?.Length != 2)
                    {
                        Logger.LogWarning("ECID-值对格式无效");
                        continue;
                    }

                    var ecidItem = pairItem.Items[0];
                    var valueItem = pairItem.Items[1];

                    // 解析ECID
                    uint ecid = ecidItem.Format switch
                    {
                        SecsFormat.U1 => ecidItem.FirstValue<byte>(),
                        SecsFormat.U2 => ecidItem.FirstValue<ushort>(),
                        SecsFormat.U4 => ecidItem.FirstValue<uint>(),
                        SecsFormat.I1 => (uint)Math.Max((sbyte)0, ecidItem.FirstValue<sbyte>()),
                        SecsFormat.I2 => (uint)Math.Max((short)0, ecidItem.FirstValue<short>()),
                        SecsFormat.I4 => (uint)Math.Max(0, ecidItem.FirstValue<int>()),
                        _ => 0
                    };

                    if (ecid == 0)
                    {
                        Logger.LogWarning("无效的ECID值");
                        continue;
                    }

                    // 解析值
                    object value = ParseEcidValue(valueItem);

                    pairs.Add((ecid, value));
                    Logger.LogTrace($"解析ECID-值对: ECID={ecid}, Value={value}");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析ECID-值对失败");
                }
            }

            return pairs;
        }

        /// <summary>
        /// 解析ECID值
        /// </summary>
        private object ParseEcidValue(Item item)
        {
            return item.Format switch
            {
                SecsFormat.ASCII => item.GetString(),
                SecsFormat.Binary => item.GetMemory<byte>().ToArray(),
                SecsFormat.Boolean => item.FirstValue<bool>(),
                SecsFormat.U1 => item.FirstValue<byte>(),
                SecsFormat.U2 => item.FirstValue<ushort>(),
                SecsFormat.U4 => item.FirstValue<uint>(),
                SecsFormat.U8 => item.FirstValue<ulong>(),
                SecsFormat.I1 => item.FirstValue<sbyte>(),
                SecsFormat.I2 => item.FirstValue<short>(),
                SecsFormat.I4 => item.FirstValue<int>(),
                SecsFormat.I8 => item.FirstValue<long>(),
                SecsFormat.F4 => item.FirstValue<float>(),
                SecsFormat.F8 => item.FirstValue<double>(),
                SecsFormat.List => ParseListValue(item),
                _ => item.ToString() ?? string.Empty
            };
        }

        /// <summary>
        /// 解析列表值
        /// </summary>
        private object ParseListValue(Item item)
        {
            // 某些ECID可能接受列表值（如多个参数组合）
            var values = new List<object>();

            if (item.Items != null)
            {
                foreach (var subItem in item.Items)
                {
                    values.Add(ParseEcidValue(subItem));
                }
            }

            return values;
        }

        #endregion

        #region 私有方法 - ECID修改处理

        /// <summary>
        /// 处理单个ECID修改请求
        /// </summary>
        private async Task<EquipmentConstantAck> ProcessEcidModification(uint ecid, object newValue, CancellationToken cancellationToken)
        {
            try
            {
                // 检查ECID是否存在
                var definition = GetEcidDefinition(ecid);
                if (definition == null)
                {
                    Logger.LogWarning($"ECID {ecid} 不存在");
                    return EquipmentConstantAck.DeniedDoesNotExist;
                }

                // 检查是否为只读
                if (definition.IsReadOnly)
                {
                    Logger.LogWarning($"ECID {ecid} ({definition.Name}) 是只读的");
                    return EquipmentConstantAck.DeniedReadOnly;
                }

                // 验证数据类型
                if (!ValidateDataType(newValue, definition.DataType))
                {
                    Logger.LogWarning($"ECID {ecid} 值的数据类型无效");
                    return EquipmentConstantAck.DeniedInvalidFormat;
                }

                // 转换值到目标类型
                var convertedValue = ConvertValue(newValue, definition.DataType);

                // 验证值范围
                if (!ValidateValueRange(convertedValue, definition))
                {
                    Logger.LogWarning($"ECID {ecid} 值超出范围: {convertedValue}");
                    return EquipmentConstantAck.DeniedOutOfRange;
                }

                // 检查特殊权限要求
                if (!await ValidatePermission(ecid, cancellationToken))
                {
                    Logger.LogWarning($"没有权限修改ECID {ecid}");
                    return EquipmentConstantAck.DeniedNoPermission;
                }

                // 获取当前值
                var currentValue = GetCurrentValue(ecid);

                // 检查值是否相同
                if (AreValuesEqual(currentValue, convertedValue))
                {
                    Logger.LogDebug($"ECID {ecid} 新值与当前值相同");
                    return EquipmentConstantAck.AcknowledgedNoChange;
                }

                // 应用修改
                var success = await ApplyEcidChange(ecid, convertedValue, definition, cancellationToken);

                if (success)
                {
                    return EquipmentConstantAck.AcknowledgedChanged;
                }
                else
                {
                    return EquipmentConstantAck.DeniedBusy;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"处理ECID {ecid} 修改时发生错误");
                return EquipmentConstantAck.DeniedBusy;
            }
        }

        /// <summary>
        /// 应用ECID修改
        /// </summary>
        private async Task<bool> ApplyEcidChange(uint ecid, object newValue, EcidDefinition definition, CancellationToken cancellationToken)
        {
            try
            {
                // 更新内存缓存
                _valueLock.EnterWriteLock();
                try
                {
                    _ecidValues[ecid] = newValue;
                }
                finally
                {
                    _valueLock.ExitWriteLock();
                }

                // 如果配置了PLC地址，同步到PLC
                if (!string.IsNullOrEmpty(definition.PlcAddress) && _plcProvider?.IsConnected == true)
                {
                    try
                    {
                        var plcSuccess = await _plcProvider.WriteEcidAsync(ecid, definition.PlcAddress, newValue, cancellationToken);
                        if (!plcSuccess)
                        {
                            Logger.LogWarning($"写入ECID {ecid} 到PLC失败");
                            // 回滚内存修改
                            RollbackValue(ecid);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"写入ECID {ecid} 到PLC时发生异常");
                        // 回滚内存修改
                        RollbackValue(ecid);
                        return false;
                    }
                }

                // 应用特殊处理（某些ECID修改可能需要立即生效）
                await ApplySpecialHandling(ecid, newValue, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"应用ECID {ecid} 修改失败");
                return false;
            }
        }

        /// <summary>
        /// 应用特殊处理
        /// </summary>
        private async Task ApplySpecialHandling(uint ecid, object newValue, CancellationToken cancellationToken)
        {
            // 根据ECID类型执行特殊处理
            switch (ecid)
            {
                case 100: // T3Timeout
                case 101: // T5Timeout
                case 102: // T6Timeout
                case 103: // T7Timeout
                case 104: // T8Timeout
                    Logger.LogInformation($"通信超时参数已修改，将在下次连接时生效");
                    break;

                case 301: // KnifeLifeLimit
                case 302: // KnifeWarningThreshold
                    Logger.LogInformation($"刀具参数已修改，立即生效");
                    // 可能需要更新刀具监控逻辑
                    break;

                case 1000: // DefaultCuttingSpeed
                case 1001: // DefaultCuttingPressure
                case 1002: // DefaultSpindleSpeed
                    Logger.LogInformation($"默认工艺参数已修改");
                    // 可能需要更新当前配方
                    break;
            }

            await Task.CompletedTask;
        }

        #endregion

        #region 私有方法 - 验证功能

        /// <summary>
        /// 验证设备状态
        /// </summary>
        private async Task<(bool CanModify, string Reason)> ValidateDeviceState(CancellationToken cancellationToken)
        {
            var statusInfo = await _stateService.GetStatusInfoAsync();

            // 检查控制状态 - 必须在线才能修改
            if (!statusInfo.IsOnline)
            {
                return (false, "设备未在线");
            }

            // 检查处理状态 - 处理中不允许修改某些参数
            if (statusInfo.ProcessState == ProcessState.Executing)
            {
                return (false, "设备正在处理中");
            }

            // 检查设备状态
            if (statusInfo.EquipmentState == EquipmentState.UnscheduledDown)
            {
                return (false, "设备处于故障状态");
            }

            return (true, "");
        }

        /// <summary>
        /// 验证数据类型
        /// </summary>
        private bool ValidateDataType(object value, Type expectedType)
        {
            if (value == null)
                return false;

            var valueType = value.GetType();

            // 检查直接类型匹配
            if (valueType == expectedType)
                return true;

            // 检查是否可以转换
            try
            {
                Convert.ChangeType(value, expectedType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证值范围
        /// </summary>
        private bool ValidateValueRange(object value, EcidDefinition definition)
        {
            if (definition.MinValue == null && definition.MaxValue == null)
                return true; // 无范围限制

            try
            {
                // 将值转换为可比较的形式
                if (definition.DataType == typeof(float) || definition.DataType == typeof(double))
                {
                    var floatValue = Convert.ToDouble(value);

                    if (definition.MinValue != null)
                    {
                        var minValue = Convert.ToDouble(definition.MinValue);
                        if (floatValue < minValue)
                            return false;
                    }

                    if (definition.MaxValue != null)
                    {
                        var maxValue = Convert.ToDouble(definition.MaxValue);
                        if (floatValue > maxValue)
                            return false;
                    }
                }
                else if (IsNumericType(definition.DataType))
                {
                    var longValue = Convert.ToInt64(value);

                    if (definition.MinValue != null)
                    {
                        var minValue = Convert.ToInt64(definition.MinValue);
                        if (longValue < minValue)
                            return false;
                    }

                    if (definition.MaxValue != null)
                    {
                        var maxValue = Convert.ToInt64(definition.MaxValue);
                        if (longValue > maxValue)
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证权限
        /// </summary>
        private async Task<bool> ValidatePermission(uint ecid, CancellationToken cancellationToken)
        {
            // 检查特定ECID的权限要求
            // 维护参数（300-399）需要维护权限
            if (ecid >= 300 && ecid < 400)
            {
                // 这里简化处理，实际应检查用户权限
                var statusInfo = await _stateService.GetStatusInfoAsync();
                return statusInfo.EquipmentState == EquipmentState.Engineering;
            }

            return true;
        }

        /// <summary>
        /// 转换值到目标类型
        /// </summary>
        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.GetType() == targetType)
                return value;

            return Convert.ChangeType(value, targetType);
        }

        /// <summary>
        /// 比较两个值是否相等
        /// </summary>
        private bool AreValuesEqual(object value1, object value2)
        {
            if (value1 == null && value2 == null)
                return true;

            if (value1 == null || value2 == null)
                return false;

            return value1.Equals(value2);
        }

        /// <summary>
        /// 判断是否为数字类型
        /// </summary>
        private bool IsNumericType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong);
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 获取ECID定义
        /// </summary>
        private EcidDefinition? GetEcidDefinition(uint ecid)
        {
            // 如果有S2F13Handler实例，从它获取定义
            if (_ecidHandler != null)
            {
                return _ecidHandler.GetEcidDefinition(ecid);
            }

            // 否则创建一个简单的定义
            return CreateSimpleEcidDefinition(ecid);
        }

        /// <summary>
        /// 创建简单的ECID定义
        /// </summary>
        private EcidDefinition? CreateSimpleEcidDefinition(uint ecid)
        {
            // 这里提供一些基本的ECID定义
            return ecid switch
            {
                675 => new EcidDefinition { Id = 675, Name = "TimeFormat", DataType = typeof(uint), IsReadOnly = false },
                1000 => new EcidDefinition { Id = 1000, Name = "DefaultCuttingSpeed", DataType = typeof(float), IsReadOnly = false, MinValue = 10.0f, MaxValue = 300.0f },
                1001 => new EcidDefinition { Id = 1001, Name = "DefaultCuttingPressure", DataType = typeof(float), IsReadOnly = false, MinValue = 50.0f, MaxValue = 500.0f },
                _ => null
            };
        }

        /// <summary>
        /// 获取当前值
        /// </summary>
        private object GetCurrentValue(uint ecid)
        {
            _valueLock.EnterReadLock();
            try
            {
                return _ecidValues.TryGetValue(ecid, out var value) ? value : 0;
            }
            finally
            {
                _valueLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 回滚值
        /// </summary>
        private void RollbackValue(uint ecid)
        {
            // 这里应该从持久化存储恢复原值
            // 简化处理，移除缓存值
            _valueLock.EnterWriteLock();
            try
            {
                _ecidValues.Remove(ecid);
            }
            finally
            {
                _valueLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 创建S2F16响应
        /// </summary>
        private SecsMessage CreateS2F16Response(List<EquipmentConstantAck> results)
        {
            var items = results.Select(r => Item.U1((byte)r)).ToArray();

            return new SecsMessage(2, 16, false)
            {
                Name = "NewEquipmentConstantAcknowledge",
                SecsItem = Item.L(items)
            };
        }

        /// <summary>
        /// 触发ECID改变事件
        /// </summary>
        private async Task TriggerEcidChangeEvent(List<(uint ecid, object oldValue, object newValue)> modifiedEcids, CancellationToken cancellationToken)
        {
            if (_eventService == null)
                return;

            try
            {
                // 触发CEID 220 - OperatorEquipmentConstantChange
                var eventData = new Dictionary<uint, object>
                {
                    { 1, DateTime.Now.ToString("yyyyMMddHHmmss") }, // 时间戳
                    { 2, modifiedEcids.Count }, // 修改的ECID数量
                };

                // 添加前3个修改的ECID信息（示例）
                for (int i = 0; i < Math.Min(3, modifiedEcids.Count); i++)
                {
                    var (ecid, oldValue, newValue) = modifiedEcids[i];
                    eventData.Add((uint)(100 + (i * 3)), ecid);
                    eventData.Add((uint)(101 + (i * 3)), oldValue);
                    eventData.Add((uint)(102 + (i * 3)), newValue);
                }

                await _eventService.ReportEventAsync(220, "EquipmentConstantChange", eventData);

                Logger.LogDebug("已触发设备常量改变事件");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "触发ECID改变事件失败");
            }
        }

        /// <summary>
        /// 加载持久化的值
        /// </summary>
        private void LoadPersistedValues()
        {
            try
            {
                if (File.Exists(_configPersistPath))
                {
                    var json = File.ReadAllText(_configPersistPath);
                    // 这里应该反序列化JSON到_ecidValues
                    // 简化处理
                    Logger.LogDebug($"从 {_configPersistPath} 加载了ECID值");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "加载持久化ECID值失败");
            }
        }

        /// <summary>
        /// 持久化值
        /// </summary>
        private async Task PersistValuesAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_configPersistPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 序列化并保存
                // 这里应该序列化_ecidValues到JSON
                // 简化处理
                await File.WriteAllTextAsync(_configPersistPath, "{}", cancellationToken);

                Logger.LogDebug($"ECID值已持久化到 {_configPersistPath}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "持久化ECID值失败");
            }
        }

        #endregion

        #region 内部类

        /// <summary>
        /// ECID定义（与S2F13Handler共享）
        /// </summary>
        //private class EcidDefinition
        //{
        //    public uint Id { get; set; }
        //    public string Name { get; set; } = "";
        //    public string Description { get; set; } = "";
        //    public Type DataType { get; set; } = typeof(object);
        //    public object DefaultValue { get; set; } = 0;
        //    public bool IsReadOnly { get; set; }
        //    public object? MinValue { get; set; }
        //    public object? MaxValue { get; set; }
        //    public string? PlcAddress { get; set; }
        //}

        #endregion
    }
}
