// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F13Handler.cs
// 版本: v1.0.0
// 描述: S2F13消息处理器 - Equipment Constant Request 设备常量请求处理器

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
    /// S2F13 (Equipment Constant Request) 处理器
    /// 处理主机的设备常量请求，返回指定的设备常量值
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S2F13: 设备常量请求 - 主机查询设备的配置参数
    /// - S2F14: 设备常量数据 - 设备返回常量值
    /// 
    /// 交互流程：
    /// 1. 主机发送 S2F13 包含ECID列表（空列表表示查询所有）
    /// 2. 设备验证请求的ECID有效性和访问权限
    /// 3. 从配置或PLC读取常量值
    /// 4. 返回 S2F14 包含设备常量数据
    /// 
    /// 划裂片设备支持的ECID类别：
    /// - 设备配置常量（1-99）：设备型号、版本等
    /// - 通信参数（100-199）：超时设置、重试次数等
    /// - 工艺参数限值（200-299）：速度、压力、温度范围
    /// - 维护参数（300-399）：保养周期、使用限制等
    /// - 划裂片专用参数（1000-1999）：刀具参数、材料规格等
    /// </remarks>
    public class S2F13Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService? _stateService;

        /// <summary>PLC数据提供者</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>ECID定义字典（缓存）</summary>
        private readonly Dictionary<uint, EcidDefinition> _ecidDefinitions;

        /// <summary>ECID值缓存</summary>
        private readonly Dictionary<uint, object> _ecidValues;

        /// <summary>值更新锁</summary>
        private readonly ReaderWriterLockSlim _valueLock = new();

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 13;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="stateService">设备状态服务（可选）</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S2F13Handler(
            ILogger<S2F13Handler> logger,
            IOptions<EquipmentSystemConfiguration> options,
            IEquipmentStateService? stateService = null,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _stateService = stateService;
            _plcProvider = plcProvider;

            // 初始化ECID定义和值
            _ecidDefinitions = InitializeEcidDefinitions();
            _ecidValues = InitializeEcidValues();

            Logger.LogInformation($"S2F13处理器已初始化，定义了 {_ecidDefinitions.Count} 个ECID");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S2F13 消息，返回 S2F14 响应
        /// </summary>
        /// <param name="message">接收到的S2F13消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F14响应消息</returns>
        /// <remarks>
        /// S2F13 处理逻辑：
        /// 1. 解析请求的ECID列表
        /// 2. 空列表表示请求所有ECID
        /// 3. 验证每个ECID的有效性和权限
        /// 4. 读取ECID的当前值
        /// 5. 构建并返回S2F14响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S2F13 (Equipment Constant Request) 设备常量请求");

            try
            {
                // 解析请求的ECID列表
                var requestedEcids = ParseEcidList(message.SecsItem);

                if (requestedEcids.Count == 0)
                {
                    // 空列表表示请求所有ECID
                    Logger.LogDebug("请求所有设备常量");
                    requestedEcids = _ecidDefinitions.Keys.OrderBy(e => e).ToList();
                }
                else
                {
                    Logger.LogDebug($"请求 {requestedEcids.Count} 个设备常量: [{string.Join(", ", requestedEcids)}]");
                }

                // 验证控制状态 - 某些ECID可能需要特定状态才能访问
                var canAccess = await ValidateAccessPermission(cancellationToken);
                if (!canAccess)
                {
                    Logger.LogWarning("当前状态不允许访问设备常量");
                    // 某些实现可能返回空列表，这里我们返回当前值但记录警告
                }

                // 收集ECID值
                var ecidValues = new List<Item>();

                foreach (var ecid in requestedEcids)
                {
                    try
                    {
                        var value = await GetEcidValue(ecid, cancellationToken);
                        ecidValues.Add(value);

                        Logger.LogTrace($"ECID {ecid} = {value}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"获取ECID {ecid} 失败，返回默认值");
                        // 返回错误指示值
                        ecidValues.Add(CreateErrorEcidValue(ecid));
                    }
                }

                // 构建S2F14响应
                var s2f14 = new SecsMessage(2, 14, false)
                {
                    Name = "EquipmentConstantData",
                    SecsItem = Item.L(ecidValues)
                };

                Logger.LogInformation($"S2F14响应准备就绪，包含 {ecidValues.Count} 个设备常量值");
                return s2f14;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F13消息失败");

                // 返回空列表表示错误
                return new SecsMessage(2, 14, false)
                {
                    Name = "EquipmentConstantData",
                    SecsItem = Item.L()
                };
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析ECID列表
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>ECID列表</returns>
        private List<uint> ParseEcidList(Item? item)
        {
            var ecidList = new List<uint>();

            if (item == null || item.Format != SecsFormat.List)
            {
                Logger.LogWarning("S2F13消息格式无效，返回空列表");
                return ecidList;
            }

            var items = item.Items;
            if (items == null || items.Length == 0)
            {
                // 空列表是合法的，表示请求所有ECID
                return ecidList;
            }

            // 解析每个ECID
            foreach (var ecidItem in items)
            {
                try
                {
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

                    if (ecid > 0)
                    {
                        ecidList.Add(ecid);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析ECID项失败");
                }
            }

            return ecidList;
        }

        #endregion

        #region 私有方法 - ECID值获取

        /// <summary>
        /// 获取ECID值
        /// </summary>
        /// <param name="ecid">设备常量ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>ECID值Item</returns>
        private async Task<Item> GetEcidValue(uint ecid, CancellationToken cancellationToken)
        {
            // 检查是否有定义
            if (!_ecidDefinitions.TryGetValue(ecid, out var definition))
            {
                Logger.LogWarning($"未定义的ECID: {ecid}");
                return CreateUndefinedEcidValue(ecid);
            }

            // 优先从缓存获取
            _valueLock.EnterReadLock();
            try
            {
                if (_ecidValues.TryGetValue(ecid, out var cachedValue))
                {
                    return ConvertToItem(cachedValue, definition.DataType);
                }
            }
            finally
            {
                _valueLock.ExitReadLock();
            }

            // 从PLC读取（如果配置了PLC地址）
            if (!string.IsNullOrEmpty(definition.PlcAddress) && _plcProvider?.IsConnected == true)
            {
                try
                {
                    var plcValue = await _plcProvider.ReadSvidAsync(ecid, definition.PlcAddress, cancellationToken);
                    if (plcValue != null)
                    {
                        UpdateCachedValue(ecid, plcValue);
                        return ConvertToItem(plcValue, definition.DataType);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, $"从PLC读取ECID {ecid} 失败");
                }
            }

            // 返回默认值
            return ConvertToItem(definition.DefaultValue, definition.DataType);
        }

        /// <summary>
        /// 将值转换为Item
        /// </summary>
        private Item ConvertToItem(object value, Type dataType)
        {
            try
            {
                // 根据数据类型转换
                if (dataType == typeof(string))
                {
                    return Item.A(value?.ToString() ?? "");
                }
                else if (dataType == typeof(bool))
                {
                    return Item.Boolean(Convert.ToBoolean(value));
                }
                else if (dataType == typeof(byte))
                {
                    return Item.U1(Convert.ToByte(value));
                }
                else if (dataType == typeof(ushort))
                {
                    return Item.U2(Convert.ToUInt16(value));
                }
                else if (dataType == typeof(uint))
                {
                    return Item.U4(Convert.ToUInt32(value));
                }
                else if (dataType == typeof(sbyte))
                {
                    return Item.I1(Convert.ToSByte(value));
                }
                else if (dataType == typeof(short))
                {
                    return Item.I2(Convert.ToInt16(value));
                }
                else if (dataType == typeof(int))
                {
                    return Item.I4(Convert.ToInt32(value));
                }
                else if (dataType == typeof(float))
                {
                    return Item.F4(Convert.ToSingle(value));
                }
                else if (dataType == typeof(double))
                {
                    return Item.F8(Convert.ToDouble(value));
                }
                else
                {
                    // 默认作为字符串处理
                    return Item.A(value?.ToString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"转换值 {value} 到类型 {dataType} 失败");
                return Item.A("");
            }
        }

        /// <summary>
        /// 创建未定义ECID值
        /// </summary>
        private Item CreateUndefinedEcidValue(uint ecid)
        {
            return Item.L(); // 返回空列表表示未定义
        }

        /// <summary>
        /// 创建错误ECID值
        /// </summary>
        private Item CreateErrorEcidValue(uint ecid)
        {
            return Item.L(); // 返回空列表表示错误
        }

        /// <summary>
        /// 更新缓存值
        /// </summary>
        private void UpdateCachedValue(uint ecid, object value)
        {
            _valueLock.EnterWriteLock();
            try
            {
                _ecidValues[ecid] = value;
            }
            finally
            {
                _valueLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 验证访问权限
        /// </summary>
        private async Task<bool> ValidateAccessPermission(CancellationToken cancellationToken)
        {
            if (_stateService == null)
                return true; // 无状态服务时默认允许

            try
            {
                var statusInfo = await _stateService.GetStatusInfoAsync();
                // 某些状态下可能限制ECID访问
                // 这里简化处理，实际可能需要更复杂的权限控制
                return true;
            }
            catch
            {
                return true; // 出错时默认允许
            }
        }

        #endregion

        #region 私有方法 - ECID初始化

        /// <summary>
        /// 初始化ECID定义字典
        /// </summary>
        private Dictionary<uint, EcidDefinition> InitializeEcidDefinitions()
        {
            var definitions = new Dictionary<uint, EcidDefinition>();

            // 设备配置常量
            AddEcidDefinition(definitions, DicerECID.DeviceId,
                "DeviceId", "设备标识", typeof(ushort), _config.Equipment.DeviceId, true);

            AddEcidDefinition(definitions, DicerECID.ModelName,
                "ModelName", "设备型号", typeof(string), _config.Equipment.ModelName, true);

            AddEcidDefinition(definitions, DicerECID.Manufacturer,
                "Manufacturer", "制造商", typeof(string), "AIMFAB", true);

            AddEcidDefinition(definitions, DicerECID.SerialNumber,
                "SerialNumber", "序列号", typeof(string), "SN2024001", true);

            AddEcidDefinition(definitions, DicerECID.SoftwareVersion,
                "SoftwareVersion", "软件版本", typeof(string), _config.Equipment.SoftwareRevision, true);

            AddEcidDefinition(definitions, DicerECID.HardwareVersion,
                "HardwareVersion", "硬件版本", typeof(string), "HW1.0", true);

            AddEcidDefinition(definitions, DicerECID.MaxWaferSize,
                "MaxWaferSize", "最大晶圆尺寸(mm)", typeof(uint), 300u, true);

            AddEcidDefinition(definitions, DicerECID.MinWaferSize,
                "MinWaferSize", "最小晶圆尺寸(mm)", typeof(uint), 100u, true);

            // 通信参数
            AddEcidDefinition(definitions, DicerECID.T3Timeout,
                "T3Timeout", "T3超时时间(ms)", typeof(uint), (uint)_config.Equipment.T3, false);

            AddEcidDefinition(definitions, DicerECID.T5Timeout,
                "T5Timeout", "T5超时时间(ms)", typeof(uint), (uint)_config.Equipment.T5, false);

            AddEcidDefinition(definitions, DicerECID.T6Timeout,
                "T6Timeout", "T6超时时间(ms)", typeof(uint), (uint)_config.Equipment.T6, false);

            AddEcidDefinition(definitions, DicerECID.T7Timeout,
                "T7Timeout", "T7超时时间(ms)", typeof(uint), (uint)_config.Equipment.T7, false);

            AddEcidDefinition(definitions, DicerECID.T8Timeout,
                "T8Timeout", "T8超时时间(ms)", typeof(uint), (uint)_config.Equipment.T8, false);

            AddEcidDefinition(definitions, DicerECID.LinkTestInterval,
                "LinkTestInterval", "LinkTest间隔(ms)", typeof(uint), (uint)_config.Equipment.LinkTestInterval, false);

            AddEcidDefinition(definitions, DicerECID.RetryLimit,
                "RetryLimit", "重试限制次数", typeof(uint), 3u, false);

            AddEcidDefinition(definitions, DicerECID.MaxSpoolSize,
                "MaxSpoolSize", "最大缓存大小(KB)", typeof(uint), 10240u, true);

            // 工艺参数限值
            AddEcidDefinition(definitions, DicerECID.MaxCuttingSpeed,
                "MaxCuttingSpeed", "最大切割速度(mm/s)", typeof(float), 300.0f, false);

            AddEcidDefinition(definitions, DicerECID.MinCuttingSpeed,
                "MinCuttingSpeed", "最小切割速度(mm/s)", typeof(float), 10.0f, false);

            AddEcidDefinition(definitions, DicerECID.MaxCuttingPressure,
                "MaxCuttingPressure", "最大切割压力(kPa)", typeof(float), 500.0f, false);

            AddEcidDefinition(definitions, DicerECID.MinCuttingPressure,
                "MinCuttingPressure", "最小切割压力(kPa)", typeof(float), 50.0f, false);

            AddEcidDefinition(definitions, DicerECID.MaxSpindleSpeed,
                "MaxSpindleSpeed", "最大主轴转速(RPM)", typeof(uint), 60000u, false);

            AddEcidDefinition(definitions, DicerECID.MinSpindleSpeed,
                "MinSpindleSpeed", "最小主轴转速(RPM)", typeof(uint), 1000u, false);

            AddEcidDefinition(definitions, DicerECID.MaxCoolingFlow,
                "MaxCoolingFlow", "最大冷却流量(L/min)", typeof(float), 10.0f, false);

            AddEcidDefinition(definitions, DicerECID.MinCoolingFlow,
                "MinCoolingFlow", "最小冷却流量(L/min)", typeof(float), 1.0f, false);

            AddEcidDefinition(definitions, DicerECID.MaxVacuumPressure,
                "MaxVacuumPressure", "最大真空压力(Pa)", typeof(float), 100000.0f, false);

            AddEcidDefinition(definitions, DicerECID.MinVacuumPressure,
                "MinVacuumPressure", "最小真空压力(Pa)", typeof(float), 10000.0f, false);

            AddEcidDefinition(definitions, DicerECID.MaxTemperature,
                "MaxTemperature", "最大温度(℃)", typeof(float), 80.0f, false);

            AddEcidDefinition(definitions, DicerECID.MinTemperature,
                "MinTemperature", "最小温度(℃)", typeof(float), 15.0f, false);

            // 维护参数
            AddEcidDefinition(definitions, DicerECID.MaintenanceInterval,
                "MaintenanceInterval", "保养间隔(小时)", typeof(uint), 500u, false);

            AddEcidDefinition(definitions, DicerECID.KnifeLifeLimit,
                "KnifeLifeLimit", "刀具寿命限制(次)", typeof(uint), 100000u, false);

            AddEcidDefinition(definitions, DicerECID.KnifeWarningThreshold,
                "KnifeWarningThreshold", "刀具预警阈值(%)", typeof(uint), 80u, false);

            AddEcidDefinition(definitions, DicerECID.CleaningInterval,
                "CleaningInterval", "清洁间隔(小时)", typeof(uint), 24u, false);

            AddEcidDefinition(definitions, DicerECID.CalibrationInterval,
                "CalibrationInterval", "校准间隔(天)", typeof(uint), 90u, false);

            AddEcidDefinition(definitions, DicerECID.FilterLifeLimit,
                "FilterLifeLimit", "过滤器寿命(小时)", typeof(uint), 2000u, false);

            AddEcidDefinition(definitions, DicerECID.LubricationInterval,
                "LubricationInterval", "润滑间隔(小时)", typeof(uint), 100u, false);

            // SEMI标准要求的ECID
            AddEcidDefinition(definitions, DicerECID.TimeFormat,
                "TimeFormat", "时钟格式(0=12小时,1=24小时)", typeof(uint), 1u, false);

            // 划裂片专用参数
            AddEcidDefinition(definitions, DicerECID.DefaultCuttingSpeed,
                "DefaultCuttingSpeed", "默认切割速度(mm/s)", typeof(float), 100.0f, false);

            AddEcidDefinition(definitions, DicerECID.DefaultCuttingPressure,
                "DefaultCuttingPressure", "默认切割压力(kPa)", typeof(float), 200.0f, false);

            AddEcidDefinition(definitions, DicerECID.DefaultSpindleSpeed,
                "DefaultSpindleSpeed", "默认主轴转速(RPM)", typeof(uint), 30000u, false);

            AddEcidDefinition(definitions, DicerECID.KerfWidth,
                "KerfWidth", "切割槽宽度(μm)", typeof(float), 50.0f, false);

            AddEcidDefinition(definitions, DicerECID.ChuckVacuumLevel,
                "ChuckVacuumLevel", "吸盘真空度(kPa)", typeof(float), 80.0f, false);

            AddEcidDefinition(definitions, DicerECID.AlignmentTolerance,
                "AlignmentTolerance", "对准容差(μm)", typeof(float), 5.0f, false);

            AddEcidDefinition(definitions, DicerECID.EdgeExclusion,
                "EdgeExclusion", "边缘排除区(mm)", typeof(float), 3.0f, false);

            AddEcidDefinition(definitions, DicerECID.DicingMode,
                "DicingMode", "切割模式(0=单刀,1=双刀)", typeof(uint), 0u, false);

            AddEcidDefinition(definitions, DicerECID.CoolingMode,
                "CoolingMode", "冷却模式(0=水冷,1=气冷)", typeof(uint), 0u, false);

            AddEcidDefinition(definitions, DicerECID.CleaningMode,
                "CleaningMode", "清洁模式(0=手动,1=自动)", typeof(uint), 1u, false);

            Logger.LogDebug($"初始化了 {definitions.Count} 个ECID定义");
            return definitions;
        }

        /// <summary>
        /// 初始化ECID值
        /// </summary>
        private Dictionary<uint, object> InitializeEcidValues()
        {
            var values = new Dictionary<uint, object>();

            // 从定义中初始化默认值
            foreach (var kvp in _ecidDefinitions)
            {
                values[kvp.Key] = kvp.Value.DefaultValue;
            }

            // 从配置文件覆盖值
            // 这里可以从appsettings.json或其他配置源加载

            return values;
        }

        /// <summary>
        /// 添加ECID定义
        /// </summary>
        private void AddEcidDefinition(
            Dictionary<uint, EcidDefinition> definitions,
            uint ecid,
            string name,
            string description,
            Type dataType,
            object defaultValue,
            bool isReadOnly)
        {
            definitions[ecid] = new EcidDefinition
            {
                Id = ecid,
                Name = name,
                Description = description,
                DataType = dataType,
                DefaultValue = defaultValue,
                IsReadOnly = isReadOnly,
                PlcAddress = GetPlcAddress(ecid)
            };
        }

        /// <summary>
        /// 获取PLC地址（如果配置了映射）
        /// </summary>
        private string? GetPlcAddress(uint ecid)
        {
            // 从配置中查找ECID到PLC地址的映射
            // 这里简化处理，实际应从配置文件读取
            return ecid switch
            {
                DicerECID.DefaultCuttingSpeed => "D1000",
                DicerECID.DefaultCuttingPressure => "D1002",
                DicerECID.DefaultSpindleSpeed => "D1004",
                DicerECID.KnifeLifeLimit => "D1010",
                _ => null
            };
        }

        #endregion

        #region 公共方法（供S2F15Handler使用）

        /// <summary>
        /// 获取ECID定义（供其他Handler使用）
        /// </summary>
        /// <param name="ecid">ECID</param>
        /// <returns>ECID定义，如果不存在返回null</returns>
        public EcidDefinition? GetEcidDefinition(uint ecid)
        {
            return _ecidDefinitions.TryGetValue(ecid, out var definition) ? definition : null;
        }

        /// <summary>
        /// 获取所有ECID列表（供S2F29Handler使用）
        /// </summary>
        /// <returns>ECID列表</returns>
        public List<uint> GetAllEcidList()
        {
            return _ecidDefinitions.Keys.OrderBy(e => e).ToList();
        }

        #endregion
    }
}
