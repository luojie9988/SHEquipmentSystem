// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F3Handler.cs
// 版本: v2.0.0
// 描述: S1F3消息处理器 - Selected Equipment Status Request 状态变量请求处理器

using System;
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
    /// S1F3 (Selected Equipment Status Request) 状态变量请求处理器
    /// 处理主机的状态变量查询请求，返回指定SVID的当前值
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F3: Selected Equipment Status Request - 主机请求特定状态变量
    /// - S1F4: Selected Equipment Status Data - 设备返回状态变量值
    /// 
    /// 消息格式：
    /// S1F3 W
    /// L,n
    ///   1. &lt;SVID1&gt; U4
    ///   ...
    ///   n. &lt;SVID1&gt; U4
    /// 注：空列表表示请求所有SVID
    /// 
    /// S1F4
    /// L,n
    ///   1. &lt;SV1&gt; (根据SVID类型返回对应格式)
    ///   ...
    ///   n. &lt;SVn&gt;
    /// 
    /// 划裂片设备SVID支持：
    /// - 标准SVID (280-721): 6个
    /// - 设备特定SVID (10001-10016): 16个
    /// - 共计22个SVID定义
    /// 
    /// 与Host端匹配要点：
    /// - SVID必须与Host端定义一致
    /// - 数据格式必须正确（L/A/U1/I2/I4等）
    /// - 无效SVID返回空列表
    /// - 支持批量查询优化
    /// </remarks>
    public class S1F3Handler : SecsMessageHandlerBase, IS1F3Handler
    {
        #region 私有字段

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>设备数据模型</summary>
        private readonly DiceDataModel _dataModel;

        /// <summary>PLC数据提供者（可选）</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>SVID定义映射表</summary>
        private readonly Dictionary<uint, SvidDefinition> _svidDefinitions;

        /// <summary>SVID值缓存</summary>
        private readonly SvidValueCache _valueCache;

        /// <summary>性能统计</summary>
        private readonly SvidQueryStatistics _statistics;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 3;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public S1F3Handler(
            ILogger<S1F3Handler> logger,
            IStatusVariableService statusService,
            DiceDataModel dataModel,
            IEquipmentStateService stateService,
            IOptions<EquipmentSystemConfiguration> options,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _dataModel = dataModel ?? throw new ArgumentNullException(nameof(dataModel));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _plcProvider = plcProvider;

            _svidDefinitions = InitializeSvidDefinitions();
            _valueCache = new SvidValueCache(TimeSpan.FromSeconds(1));
            _statistics = new SvidQueryStatistics();

            Logger.LogInformation($"S1F3处理器已初始化 - 支持 {_svidDefinitions.Count} 个SVID");
        }

        #endregion

        #region 公共方法 - IS1F3Handler接口

        /// <summary>
        /// 查询单个SVID值
        /// </summary>
        public async Task<Item> GetSvidValueAsync(uint svid, CancellationToken cancellationToken = default)
        {
            try
            {
                // 检查缓存
                if (_valueCache.TryGetValue(svid, out var cachedValue))
                {
                    _statistics.RecordCacheHit(svid);
                    return cachedValue;
                }

                // 获取新值
                var value = await GetSvidValueInternalAsync(svid, cancellationToken);

                // 更新缓存
                _valueCache.SetValue(svid, value);
                _statistics.RecordQuery(svid);

                return value;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"获取SVID {svid} 值失败");
                return L(); // 返回空列表表示错误
            }
        }

        /// <summary>
        /// 批量查询SVID值
        /// </summary>
        public async Task<Dictionary<uint, Item>> GetSvidValuesAsync(
            IEnumerable<uint> svids,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<uint, Item>();

            foreach (var svid in svids)
            {
                result[svid] = await GetSvidValueAsync(svid, cancellationToken);
            }

            return result;
        }

        /// <summary>
        /// 获取所有SVID值
        /// </summary>
        public async Task<Dictionary<uint, Item>> GetAllSvidValuesAsync(CancellationToken cancellationToken = default)
        {
            var allSvids = SemiIdDefinitions.Svid.AllSvids;
            return await GetSvidValuesAsync(allSvids, cancellationToken);
        }

        /// <summary>
        /// 获取SVID定义信息
        /// </summary>
        public SvidDefinitionInfo? GetSvidDefinition(uint svid)
        {
            if (_svidDefinitions.TryGetValue(svid, out var definition))
            {
                return new SvidDefinitionInfo
                {
                    Svid = definition.Svid,
                    Name = definition.Name,
                    Description = definition.Description,
                    DataType = definition.DataType,
                    Units = definition.Units,
                    Category = definition.Category
                };
            }
            return null;
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S1F3消息，返回S1F4响应
        /// </summary>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F3 (Selected Equipment Status Request) 状态变量请求");

            try
            {
                // 解析请求的SVID列表
                var requestedSvids = ParseS1F3Message(message.SecsItem);

                if (requestedSvids.Count == 0)
                {
                    Logger.LogDebug("请求所有SVID");
                    requestedSvids = SemiIdDefinitions.Svid.AllSvids.ToList();
                }
                else
                {
                    Logger.LogDebug($"请求 {requestedSvids.Count} 个SVID: {string.Join(", ", requestedSvids)}");
                }

                // 获取SVID值
                var svidValues = await CollectSvidValues(requestedSvids, cancellationToken);

                // 构建S1F4响应
                var response = CreateS1F4Response(svidValues);

                // 记录统计
                _statistics.RecordBatchQuery(requestedSvids.Count, svidValues.Count);

                Logger.LogDebug($"返回 {svidValues.Count} 个SVID值");
                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F3消息失败");
                return CreateErrorResponse();
            }
        }

        #endregion

        #region 私有方法 - SVID定义初始化

        /// <summary>
        /// 初始化SVID定义
        /// </summary>
        private Dictionary<uint, SvidDefinition> InitializeSvidDefinitions()
        {
            var definitions = new Dictionary<uint, SvidDefinition>();

            // SEMI标准SVID (280-721)
            definitions[SemiIdDefinitions.Svid.EventsEnabled] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.EventsEnabled,
                Name = "EventsEnabled",
                Description = "启用的事件列表",
                DataType = "L",
                Category = SvidCategory.Standard,
                GetValue = () => L(_dataModel.EventsEnabled.Select(id => U4(id)).ToArray())
            };

            definitions[SemiIdDefinitions.Svid.AlarmsEnabled] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.AlarmsEnabled,
                Name = "AlarmsEnabled",
                Description = "启用的报警列表",
                DataType = "L",
                Category = SvidCategory.Standard,
                GetValue = () => L(_dataModel.AlarmsEnabled.Select(id => U4(id)).ToArray())
            };

            definitions[SemiIdDefinitions.Svid.AlarmsSet] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.AlarmsSet,
                Name = "AlarmsSet",
                Description = "当前激活的报警列表",
                DataType = "L",
                Category = SvidCategory.Standard,
                GetValue = () => L(_dataModel.AlarmsSet.Select(id => U4(id)).ToArray())
            };

            definitions[SemiIdDefinitions.Svid.Clock] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.Clock,
                Name = "Clock",
                Description = "当前时钟",
                DataType = "A",
                Category = SvidCategory.Standard,
                GetValue = () => A(DateTime.Now.ToString("yyyyMMddHHmmssff"))
            };

            definitions[SemiIdDefinitions.Svid.ControlMode] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.ControlMode,
                Name = "ControlMode",
                Description = "控制模式",
                DataType = "U1",
                Category = SvidCategory.Standard,
                GetValue = () => U1((byte)_dataModel.ControlMode)
            };

            definitions[SemiIdDefinitions.Svid.ControlState] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.ControlState,
                Name = "ControlState",
                Description = "控制状态",
                DataType = "U1",
                Category = SvidCategory.Standard,
                GetValue = () => U1((byte)_dataModel.ControlState)
            };

            // 设备特定SVID (10001-10016)
            definitions[SemiIdDefinitions.Svid.PortID] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.PortID,
                Name = "PortID",
                Description = "端口ID",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.PortID)
            };

            definitions[SemiIdDefinitions.Svid.CassetteID] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.CassetteID,
                Name = "CassetteID",
                Description = "Cassette ID",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.CassetteID)
            };

            definitions[SemiIdDefinitions.Svid.LotID] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.LotID,
                Name = "LotID",
                Description = "批次ID",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.LotID)
            };

            definitions[SemiIdDefinitions.Svid.PPID] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.PPID,
                Name = "PPID",
                Description = "工艺程序ID",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.PPID)
            };

            definitions[SemiIdDefinitions.Svid.CassetteSlotMap] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.CassetteSlotMap,
                Name = "CassetteSlotMap",
                Description = "Cassette槽位映射",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.CassetteSlotMap)
            };

            definitions[SemiIdDefinitions.Svid.ProcessedCount] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.ProcessedCount,
                Name = "ProcessedCount",
                Description = "已处理数量",
                DataType = "I2",
                Category = SvidCategory.Process,
                GetValue = () => I2(_dataModel.ProcessedCount)
            };

            definitions[SemiIdDefinitions.Svid.KnifeModel] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.KnifeModel,
                Name = "KnifeModel",
                Description = "划刀/裂刀型号",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.KnifeModel)
            };

            definitions[SemiIdDefinitions.Svid.UseNO] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.UseNO,
                Name = "UseNO",
                Description = "划刀/裂刀使用次数",
                DataType = "I4",
                Category = SvidCategory.Equipment,
                GetValue = () => I4(_dataModel.UseNO)
            };

            definitions[SemiIdDefinitions.Svid.UseMAXNO] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.UseMAXNO,
                Name = "UseMAXNO",
                Description = "划刀/裂刀最大使用次数",
                DataType = "I4",
                Category = SvidCategory.Equipment,
                GetValue = () => I4(_dataModel.UseMAXNO)
            };

            definitions[SemiIdDefinitions.Svid.ProgressBar] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.ProgressBar,
                Name = "ProgressBar",
                Description = "当前bar进度",
                DataType = "I2",
                Category = SvidCategory.Process,
                GetValue = () => I2(_dataModel.ProgressBar)
            };

            definitions[SemiIdDefinitions.Svid.BARNO] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.BARNO,
                Name = "BARNO",
                Description = "当前Frame下的BAR条总数",
                DataType = "I2",
                Category = SvidCategory.Process,
                GetValue = () => I2(_dataModel.BARNO)
            };

            definitions[SemiIdDefinitions.Svid.CurrentBAR] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.CurrentBAR,
                Name = "CurrentBar",
                Description = "当前动作中的BAR数",
                DataType = "I2",
                Category = SvidCategory.Process,
                GetValue = () => I2(_dataModel.CurrentBar)
            };

            definitions[SemiIdDefinitions.Svid.RFID] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.RFID,
                Name = "RFID",
                Description = "RFID内容",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.RFID)
            };

            definitions[SemiIdDefinitions.Svid.QRContent] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.QRContent,
                Name = "QRContent",
                Description = "扫码内容",
                DataType = "A",
                Category = SvidCategory.Equipment,
                GetValue = () => A(_dataModel.QRContent)
            };

            definitions[SemiIdDefinitions.Svid.GetFrameLY] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.GetFrameLY,
                Name = "GetFrameLY",
                Description = "取环所在层",
                DataType = "I2",
                Category = SvidCategory.Equipment,
                GetValue = () => I2(_dataModel.GetFrameLY)
            };

            definitions[SemiIdDefinitions.Svid.PutFrameLY] = new SvidDefinition
            {
                Svid = SemiIdDefinitions.Svid.PutFrameLY,
                Name = "PutFrameLY",
                Description = "放环所在层",
                DataType = "I2",
                Category = SvidCategory.Equipment,
                GetValue = () => I2(_dataModel.PutFrameLY)
            };

            return definitions;
        }

        #endregion

        #region 私有方法 - SVID值获取

        /// <summary>
        /// 内部获取SVID值
        /// </summary>
        private async Task<Item> GetSvidValueInternalAsync(uint svid, CancellationToken cancellationToken)
        {
            try
            {
                // 检查是否为定义的SVID
                if (!_svidDefinitions.TryGetValue(svid, out var definition))
                {
                    Logger.LogWarning($"未定义的SVID: {svid}");
                    return L(); // 返回空列表表示未定义
                }

                // 如果需要从PLC读取
                if (definition.RequiresPlcRead && _plcProvider != null)
                {
                    return await GetSvidValueFromPlcAsync(svid, definition, cancellationToken);
                }

                // 从数据模型获取
                if (definition.GetValue != null)
                {
                    return definition.GetValue();
                }

                // 使用数据模型的通用方法
                return _dataModel.GetSvidValue(svid);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"获取SVID {svid} 值失败");
                return L(); // 返回空列表表示错误
            }
        }

        /// <summary>
        /// 从PLC获取SVID值
        /// </summary>
        private async Task<Item> GetSvidValueFromPlcAsync(
            uint svid,
            SvidDefinition definition,
            CancellationToken cancellationToken)
        {
            if (_plcProvider == null || string.IsNullOrEmpty(definition.PlcAddress))
            {
                Logger.LogWarning($"SVID {svid} 需要PLC读取但配置不完整");
                return L();
            }

            try
            {
                // 根据数据类型读取PLC
                switch (definition.DataType)
                {
                    case "I2":
                        var shortValue = await _plcProvider.ReadInt16Async(definition.PlcAddress, cancellationToken);
                        return I2(shortValue);

                    case "I4":
                        var intValue = await _plcProvider.ReadInt32Async(definition.PlcAddress, cancellationToken);
                        return I4(intValue);

                    case "F4":
                        var floatValue = await _plcProvider.ReadFloatAsync(definition.PlcAddress, cancellationToken);
                        return F4(floatValue);

                    case "A":
                        var stringValue = await _plcProvider.ReadStringAsync(definition.PlcAddress, 20, cancellationToken);
                        return A(stringValue);

                    case "U1":
                        var byteValue = await _plcProvider.ReadByteAsync(definition.PlcAddress, cancellationToken);
                        return U1(byteValue);

                    case "U2":
                        var ushortValue = await _plcProvider.ReadUInt16Async(definition.PlcAddress, cancellationToken);
                        return U2(ushortValue);

                    case "U4":
                        var uintValue = await _plcProvider.ReadUInt32Async(definition.PlcAddress, cancellationToken);
                        return U4(uintValue);

                    case "BOOLEAN":
                        var boolValue = await _plcProvider.ReadBoolAsync(definition.PlcAddress, cancellationToken);
                        return Boolean(boolValue);

                    default:
                        Logger.LogWarning($"不支持的PLC数据类型: {definition.DataType}");
                        return L();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"从PLC读取SVID {svid} 失败 - 地址: {definition.PlcAddress}");
                return L();
            }
        }

        /// <summary>
        /// 批量收集SVID值
        /// </summary>
        private async Task<List<Item>> CollectSvidValues(List<uint> svids, CancellationToken cancellationToken)
        {
            var values = new List<Item>();

            // 批量优化：先检查缓存
            var uncachedSvids = new List<uint>();
            foreach (var svid in svids)
            {
                if (_valueCache.TryGetValue(svid, out var cachedValue))
                {
                    values.Add(cachedValue);
                    _statistics.RecordCacheHit(svid);
                }
                else
                {
                    uncachedSvids.Add(svid);
                }
            }

            // 批量读取未缓存的值
            if (uncachedSvids.Any())
            {
                Logger.LogDebug($"批量读取 {uncachedSvids.Count} 个未缓存的SVID");

                // 如果有PLC地址，尝试批量读取
                if (_plcProvider != null)
                {
                    await BatchReadFromPlcAsync(uncachedSvids, values, cancellationToken);
                }
                else
                {
                    // 逐个读取
                    foreach (var svid in uncachedSvids)
                    {
                        var value = await GetSvidValueInternalAsync(svid, cancellationToken);
                        values.Add(value);
                        _valueCache.SetValue(svid, value);
                    }
                }
            }

            return values;
        }

        /// <summary>
        /// 批量从PLC读取
        /// </summary>
        private async Task BatchReadFromPlcAsync(
            List<uint> svids,
            List<Item> values,
            CancellationToken cancellationToken)
        {
            // 按PLC地址分组以优化读取
            var plcGroups = svids
                .Where(svid => _svidDefinitions.ContainsKey(svid) &&
                              _svidDefinitions[svid].RequiresPlcRead)
                .GroupBy(svid => GetPlcAreaFromAddress(_svidDefinitions[svid].PlcAddress));

            foreach (var group in plcGroups)
            {
                try
                {
                    // 批量读取同一区域的数据
                    Logger.LogDebug($"批量读取PLC区域 {group.Key}");

                    foreach (var svid in group)
                    {
                        var value = await GetSvidValueInternalAsync(svid, cancellationToken);
                        values.Add(value);
                        _valueCache.SetValue(svid, value);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"批量读取PLC区域 {group.Key} 失败");

                    // 失败时返回空值
                    foreach (var svid in group)
                    {
                        values.Add(L());
                    }
                }
            }

            // 处理非PLC的SVID
            var nonPlcSvids = svids.Where(svid =>
                !_svidDefinitions.ContainsKey(svid) ||
                !_svidDefinitions[svid].RequiresPlcRead);

            foreach (var svid in nonPlcSvids)
            {
                var value = await GetSvidValueInternalAsync(svid, cancellationToken);
                values.Add(value);
                _valueCache.SetValue(svid, value);
            }
        }

        /// <summary>
        /// 从PLC地址获取区域
        /// </summary>
        private string GetPlcAreaFromAddress(string? address)
        {
            if (string.IsNullOrEmpty(address)) return "UNKNOWN";

            // 假设地址格式为 "DB100.DBX0.0" 或 "MW100"
            var parts = address.Split('.');
            return parts.Length > 0 ? parts[0] : "UNKNOWN";
        }

        #endregion

        #region 私有方法 - 消息处理

        /// <summary>
        /// 解析S1F3消息
        /// </summary>
        private List<uint> ParseS1F3Message(Item? item)
        {
            var svids = new List<uint>();

            try
            {
                // 空消息或空列表表示请求所有SVID
                if (item == null || item.Count == 0)
                {
                    Logger.LogDebug("收到空列表，将返回所有SVID");
                    return svids;
                }

                // 解析SVID列表
                if (item.Format == SecsFormat.List)
                {
                    foreach (var svidItem in item.Items ?? Array.Empty<Item>())
                    {
                        uint svid = 0;

                        switch (svidItem.Format)
                        {
                            case SecsFormat.U4:
                                svid = svidItem.FirstValue<uint>();
                                break;
                            case SecsFormat.U2:
                                svid = svidItem.FirstValue<ushort>();
                                break;
                            case SecsFormat.U1:
                                svid = svidItem.FirstValue<byte>();
                                break;
                            case SecsFormat.I4:
                                svid = (uint)svidItem.FirstValue<int>();
                                break;
                            case SecsFormat.I2:
                                svid = (uint)svidItem.FirstValue<short>();
                                break;
                            default:
                                Logger.LogWarning($"不支持的SVID格式: {svidItem.Format}");
                                continue;
                        }

                        if (svid > 0)
                        {
                            svids.Add(svid);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S1F3消息异常");
            }

            return svids;
        }

        /// <summary>
        /// 创建S1F4响应
        /// </summary>
        private SecsMessage CreateS1F4Response(List<Item> svidValues)
        {
            return new SecsMessage(1, 4, false)
            {
                Name = "SelectedEquipmentStatusData",
                SecsItem = L(svidValues.ToArray())
            };
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        private SecsMessage CreateErrorResponse()
        {
            // 返回空列表表示错误
            return new SecsMessage(1, 4, false)
            {
                Name = "SelectedEquipmentStatusData",
                SecsItem = L()
            };
        }

        #endregion

        #region 内部类型定义

        /// <summary>
        /// SVID定义
        /// </summary>
        private class SvidDefinition
        {
            public uint Svid { get; set; }
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string DataType { get; set; } = "";
            public string? Units { get; set; }
            public SvidCategory Category { get; set; }
            public bool RequiresPlcRead { get; set; }
            public string? PlcAddress { get; set; }
            public Func<Item>? GetValue { get; set; }
        }

        /// <summary>
        /// SVID分类
        /// </summary>
        private enum SvidCategory
        {
            /// <summary>标准SVID</summary>
            Standard,
            /// <summary>设备SVID</summary>
            Equipment,
            /// <summary>工艺SVID</summary>
            Process,
            /// <summary>材料SVID</summary>
            Material,
            /// <summary>维护SVID</summary>
            Maintenance
        }

        /// <summary>
        /// SVID值缓存
        /// </summary>
        private class SvidValueCache
        {
            private readonly Dictionary<uint, CachedValue> _cache = new();
            private readonly TimeSpan _expiration;
            private readonly object _lock = new();

            public SvidValueCache(TimeSpan expiration)
            {
                _expiration = expiration;
            }

            public bool TryGetValue(uint svid, out Item value)
            {
                lock (_lock)
                {
                    if (_cache.TryGetValue(svid, out var cached))
                    {
                        if (DateTime.Now - cached.Timestamp < _expiration)
                        {
                            value = cached.Value;
                            return true;
                        }
                        else
                        {
                            _cache.Remove(svid);
                        }
                    }
                }

                value = L();
                return false;
            }

            public void SetValue(uint svid, Item value)
            {
                lock (_lock)
                {
                    _cache[svid] = new CachedValue
                    {
                        Value = value,
                        Timestamp = DateTime.Now
                    };
                }
            }

            private class CachedValue
            {
                public Item Value { get; set; } = L();
                public DateTime Timestamp { get; set; }
            }
        }

        /// <summary>
        /// SVID查询统计
        /// </summary>
        private class SvidQueryStatistics
        {
            private readonly Dictionary<uint, int> _queryCount = new();
            private readonly Dictionary<uint, int> _cacheHits = new();
            private int _totalQueries;
            private int _totalCacheHits;
            private int _batchQueries;
            private readonly object _lock = new();

            public void RecordQuery(uint svid)
            {
                lock (_lock)
                {
                    _totalQueries++;
                    if (!_queryCount.ContainsKey(svid))
                        _queryCount[svid] = 0;
                    _queryCount[svid]++;
                }
            }

            public void RecordCacheHit(uint svid)
            {
                lock (_lock)
                {
                    _totalCacheHits++;
                    if (!_cacheHits.ContainsKey(svid))
                        _cacheHits[svid] = 0;
                    _cacheHits[svid]++;
                }
            }

            public void RecordBatchQuery(int requested, int returned)
            {
                lock (_lock)
                {
                    _batchQueries++;
                    _totalQueries += requested;
                }
            }

            public double GetCacheHitRate()
            {
                lock (_lock)
                {
                    return _totalQueries > 0 ? (double)_totalCacheHits / _totalQueries : 0;
                }
            }

            public List<uint> GetTopQueriedSvids(int count = 10)
            {
                lock (_lock)
                {
                    return _queryCount
                        .OrderByDescending(kv => kv.Value)
                        .Take(count)
                        .Select(kv => kv.Key)
                        .ToList();
                }
            }
        }

        #endregion
    }

    #region 公共类型定义

    /// <summary>
    /// SVID定义信息
    /// </summary>
    public class SvidDefinitionInfo
    {
        public uint Svid { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string DataType { get; set; } = "";
        public string? Units { get; set; }
        public object Category { get; set; } = "";
    }

    #endregion
}
