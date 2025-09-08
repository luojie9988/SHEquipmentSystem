// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F11Handler.cs
// 版本: v1.0.0
// 描述: S1F11消息处理器 - Status Variable Namelist Request 状态变量名称列表请求处理器

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
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
    /// S1F11 (Status Variable Namelist Request) 处理器
    /// 处理主机的状态变量名称列表请求，返回设备支持的所有或指定SVID的名称信息
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F11: 状态变量名称列表请求 - 主机查询状态变量的定义信息
    /// - S1F12: 状态变量名称列表 - 设备返回SVID的名称和定义
    /// 
    /// 交互流程：
    /// 1. 主机发送 S1F11 包含SVID列表（空列表表示查询所有）
    /// 2. 设备验证请求的SVID有效性
    /// 3. 收集SVID的名称、单位、描述等元数据
    /// 4. 返回 S1F12 包含SVID定义信息
    /// 
    /// 划裂片设备支持的SVID类别：
    /// - 标准SVID（1-999）：SEMI标准定义的通用变量
    /// - 控制状态SVID（720-721）：控制模式和状态
    /// - 设备特定SVID（10000-19999）：划裂片专用变量
    /// - 性能统计SVID（20000-29999）：生产和质量统计
    /// </remarks>
    public class S1F11Handler : SecsMessageHandlerBase
    {
        #region SVID定义常量

        /// <summary>
        /// 划裂片设备SVID定义
        /// </summary>
        private static class DicerSVID
        {
            // SEMI标准SVID
            public const uint Clock = 1;                    // 时钟
            public const uint ControlState = 4;             // 控制状态（实际使用721）
            public const uint PPExecName = 5;               // 正在执行的配方名
            public const uint StatusVariables = 6;          // 状态变量集合

            // 控制相关SVID（规格书定义）
            public const uint ControlMode = 720;            // 控制模式
            public const uint ControlStateActual = 721;     // 控制状态（实际）
            public const uint ProcessState = 722;           // 处理状态
            public const uint EquipmentState = 723;         // 设备状态

            // 划裂片设备特定SVID
            public const uint KnifeModel = 10007;           // 划刀/裂刀型号
            public const uint KnifeUseCount = 10008;        // 划刀/裂刀使用次数
            public const uint KnifeMaxCount = 10009;        // 划刀/裂刀最大使用次数
            public const uint MaterialCount = 10010;        // 材料计数
            public const uint CurrentSlot = 10011;          // 当前槽位号
            public const uint CassetteID = 10012;           // Cassette ID
            public const uint FrameCount = 10013;           // Frame计数

            // 工艺参数SVID
            public const uint CuttingSpeed = 10020;         // 切割速度
            public const uint CuttingPressure = 10021;      // 切割压力
            public const uint SpindleSpeed = 10022;         // 主轴转速
            public const uint CoolingFlow = 10023;          // 冷却流量
            public const uint VacuumPressure = 10024;       // 真空压力

            // 性能统计SVID
            public const uint TotalProcessed = 10100;       // 总处理数
            public const uint GoodCount = 10101;            // 良品数
            public const uint NGCount = 10102;              // 不良品数
            public const uint YieldRate = 10103;            // 良率
            public const uint UPH = 10104;                  // 每小时产量
            public const uint MTBF = 10105;                 // 平均故障间隔
            public const uint MTTR = 10106;                 // 平均修复时间
            public const uint Uptime = 10107;               // 运行时间
            public const uint Downtime = 10108;             // 停机时间
            public const uint OEE = 10109;                  // 设备综合效率
        }

        #endregion

        #region 私有字段

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>PLC数据提供者（可选）</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>SVID定义字典（缓存）</summary>
        private readonly Dictionary<uint, SvidDefinition> _svidDefinitions;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

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
        /// <param name="statusService">状态变量服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S1F11Handler(
            ILogger<S1F11Handler> logger,
            IStatusVariableService statusService,
            IOptions<EquipmentSystemConfiguration> options,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _plcProvider = plcProvider;

            // 初始化SVID定义
            _svidDefinitions = InitializeSvidDefinitions();

            Logger.LogInformation($"S1F11处理器已初始化，定义了 {_svidDefinitions.Count} 个SVID");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S1F11 消息，返回 S1F12 响应
        /// </summary>
        /// <param name="message">接收到的S1F11消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F12响应消息</returns>
        /// <remarks>
        /// S1F11 处理逻辑：
        /// 1. 解析请求的SVID列表
        /// 2. 空列表表示请求所有SVID
        /// 3. 验证每个SVID的有效性
        /// 4. 收集SVID的名称和元数据
        /// 5. 构建并返回S1F12响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F11 (Status Variable Namelist Request) 状态变量名称列表请求");

            try
            {
                // 解析请求的SVID列表
                var requestedSvids = ParseSvidList(message.SecsItem);

                if (requestedSvids.Count == 0)
                {
                    // 空列表表示请求所有SVID
                    Logger.LogDebug("请求所有状态变量名称列表");
                    requestedSvids = await _statusService.GetAllSvidListAsync();

                    // 确保包含所有定义的SVID
                    foreach (var svid in _svidDefinitions.Keys)
                    {
                        if (!requestedSvids.Contains(svid))
                        {
                            requestedSvids.Add(svid);
                        }
                    }

                    requestedSvids = requestedSvids.OrderBy(s => s).ToList();
                }
                else
                {
                    Logger.LogDebug($"请求 {requestedSvids.Count} 个状态变量名称: [{string.Join(", ", requestedSvids)}]");
                }

                // 构建响应项列表
                var responseItems = new List<Item>();

                foreach (var svid in requestedSvids)
                {
                    try
                    {
                        var svidInfo = await GetSvidNamelistEntry(svid, cancellationToken);
                        responseItems.Add(svidInfo);

                        Logger.LogTrace($"SVID {svid} 名称信息已添加");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"获取SVID {svid} 名称信息失败");
                        // 添加空条目表示该SVID无效或不存在
                        responseItems.Add(CreateInvalidSvidEntry(svid));
                    }
                }

                // 构建S1F12响应
                var s1f12 = new SecsMessage(1, 12, false)
                {
                    Name = "StatusVariableNamelistReply",
                    SecsItem = Item.L(responseItems)
                };

                Logger.LogInformation($"S1F12响应准备就绪，包含 {responseItems.Count} 个SVID定义");
                return s1f12;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F11消息失败");

                // 返回空列表表示错误
                return new SecsMessage(1, 12, false)
                {
                    Name = "StatusVariableNamelistReply",
                    SecsItem = Item.L()
                };
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析SVID列表
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>SVID列表</returns>
        private List<uint> ParseSvidList(Item? item)
        {
            var svidList = new List<uint>();

            if (item == null || item.Format != SecsFormat.List)
            {
                Logger.LogWarning("S1F11消息格式无效，返回空列表");
                return svidList;
            }

            var items = item.Items;
            if (items == null || items.Length == 0)
            {
                // 空列表是合法的，表示请求所有SVID
                return svidList;
            }

            // 解析每个SVID
            foreach (var svidItem in items)
            {
                try
                {
                    uint svid = svidItem.Format switch
                    {
                        SecsFormat.U1 => svidItem.FirstValue<byte>(),
                        SecsFormat.U2 => svidItem.FirstValue<ushort>(),
                        SecsFormat.U4 => svidItem.FirstValue<uint>(),
                        SecsFormat.I1 => (uint)Math.Max((sbyte)0, svidItem.FirstValue<sbyte>()),
                        SecsFormat.I2 => (uint)Math.Max((short)0, svidItem.FirstValue<short>()),
                        SecsFormat.I4 => (uint)Math.Max(0, svidItem.FirstValue<int>()),
                        _ => 0
                    };

                    if (svid > 0)
                    {
                        svidList.Add(svid);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "解析SVID项失败");
                }
            }

            return svidList;
        }

        #endregion

        #region 私有方法 - SVID信息构建

        /// <summary>
        /// 获取SVID名称列表条目
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>SVID名称列表条目</returns>
        private async Task<Item> GetSvidNamelistEntry(uint svid, CancellationToken cancellationToken)
        {
            // 优先从定义字典获取
            if (_svidDefinitions.TryGetValue(svid, out var definition))
            {
                return CreateSvidEntry(svid, definition);
            }

            // 从配置文件映射获取
            if (_config.SvidMapping.TryGetValue(svid, out var plcAddress))
            {
                var dynamicDef = await CreateDynamicSvidDefinition(svid, plcAddress, cancellationToken);
                return CreateSvidEntry(svid, dynamicDef);
            }

            // 尝试从状态服务获取
            var svidExists = await CheckSvidExists(svid);
            if (svidExists)
            {
                var genericDef = CreateGenericSvidDefinition(svid);
                return CreateSvidEntry(svid, genericDef);
            }

            // 返回未定义的SVID条目
            return CreateUndefinedSvidEntry(svid);
        }

        /// <summary>
        /// 创建SVID条目
        /// </summary>
        /// <param name="svid">SVID</param>
        /// <param name="definition">SVID定义</param>
        /// <returns>SVID条目Item</returns>
        private Item CreateSvidEntry(uint svid, SvidDefinition definition)
        {
            // S1F12 SVID条目格式：
            // L,3
            //   1. U4 SVID
            //   2. A SVNAME (状态变量名称)
            //   3. A UNITS (单位，可选)

            var items = new List<Item>
            {
                Item.U4(svid),                                  // SVID
                Item.A(definition.Name)                         // 名称
            };

            // 添加单位（如果有）
            if (!string.IsNullOrEmpty(definition.Units))
            {
                items.Add(Item.A(definition.Units));
            }
            else
            {
                items.Add(Item.A(""));                          // 空单位
            }

            return Item.L(items);
        }

        /// <summary>
        /// 创建无效SVID条目
        /// </summary>
        /// <param name="svid">SVID</param>
        /// <returns>无效SVID条目</returns>
        private Item CreateInvalidSvidEntry(uint svid)
        {
            return Item.L(
                Item.U4(svid),
                Item.A("INVALID"),
                Item.A("")
            );
        }

        /// <summary>
        /// 创建未定义SVID条目
        /// </summary>
        /// <param name="svid">SVID</param>
        /// <returns>未定义SVID条目</returns>
        private Item CreateUndefinedSvidEntry(uint svid)
        {
            return Item.L(
                Item.U4(svid),
                Item.A($"SVID_{svid}"),                         // 默认名称
                Item.A("")                                       // 无单位
            );
        }

        /// <summary>
        /// 创建动态SVID定义
        /// </summary>
        private async Task<SvidDefinition> CreateDynamicSvidDefinition(uint svid, string plcAddress, CancellationToken cancellationToken)
        {
            var definition = new SvidDefinition
            {
                Id = svid,
                Name = $"PLC_{plcAddress}",
                Description = $"PLC地址 {plcAddress} 映射的变量",
                DataType = typeof(uint),
                Units = "",
                Category = "PLC"
            };

            // 如果PLC连接，尝试获取更多信息
            if (_plcProvider?.IsConnected == true)
            {
                try
                {
                    // 可以从PLC读取变量描述等元数据
                    await Task.CompletedTask; // 实际实现时替换
                }
                catch
                {
                    // 忽略PLC读取错误
                }
            }

            return definition;
        }

        /// <summary>
        /// 创建通用SVID定义
        /// </summary>
        private SvidDefinition CreateGenericSvidDefinition(uint svid)
        {
            // 根据SVID范围推断类别
            var category = svid switch
            {
                < 1000 => "Standard",                           // SEMI标准
                >= 10000 and < 20000 => "Equipment",           // 设备特定
                >= 20000 and < 30000 => "Statistics",          // 统计数据
                _ => "Custom"                                    // 自定义
            };

            return new SvidDefinition
            {
                Id = svid,
                Name = $"SV_{svid}",
                Description = $"状态变量 {svid}",
                DataType = typeof(object),
                Units = "",
                Category = category
            };
        }

        /// <summary>
        /// 检查SVID是否存在
        /// </summary>
        private async Task<bool> CheckSvidExists(uint svid)
        {
            try
            {
                var allSvids = await _statusService.GetAllSvidListAsync();
                return allSvids.Contains(svid);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 私有方法 - SVID定义初始化

        /// <summary>
        /// 初始化SVID定义字典
        /// </summary>
        /// <returns>SVID定义字典</returns>
        private Dictionary<uint, SvidDefinition> InitializeSvidDefinitions()
        {
            var definitions = new Dictionary<uint, SvidDefinition>();

            // SEMI标准SVID
            AddSvidDefinition(definitions, DicerSVID.Clock,
                "Clock", "设备时钟", typeof(string), "yyyy-MM-dd HH:mm:ss", "Standard");

            AddSvidDefinition(definitions, DicerSVID.PPExecName,
                "PPExecName", "正在执行的配方名", typeof(string), "", "Standard");

            // 控制状态SVID（规格书定义）
            AddSvidDefinition(definitions, DicerSVID.ControlMode,
                "ControlMode", "控制模式", typeof(uint), "", "Control");

            AddSvidDefinition(definitions, DicerSVID.ControlStateActual,
                "ControlState", "控制状态", typeof(uint), "", "Control");

            AddSvidDefinition(definitions, DicerSVID.ProcessState,
                "ProcessState", "处理状态", typeof(uint), "", "Control");

            AddSvidDefinition(definitions, DicerSVID.EquipmentState,
                "EquipmentState", "设备状态", typeof(uint), "", "Control");

            // 划裂片设备特定SVID
            AddSvidDefinition(definitions, DicerSVID.KnifeModel,
                "KnifeModel", "划刀/裂刀型号", typeof(string), "", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.KnifeUseCount,
                "KnifeUseCount", "划刀/裂刀使用次数", typeof(uint), "次", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.KnifeMaxCount,
                "KnifeMaxCount", "划刀/裂刀最大使用次数", typeof(uint), "次", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.MaterialCount,
                "MaterialCount", "材料计数", typeof(uint), "片", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.CurrentSlot,
                "CurrentSlot", "当前槽位号", typeof(uint), "", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.CassetteID,
                "CassetteID", "Cassette标识", typeof(string), "", "Equipment");

            AddSvidDefinition(definitions, DicerSVID.FrameCount,
                "FrameCount", "Frame计数", typeof(uint), "个", "Equipment");

            // 工艺参数SVID
            AddSvidDefinition(definitions, DicerSVID.CuttingSpeed,
                "CuttingSpeed", "切割速度", typeof(float), "mm/s", "Process");

            AddSvidDefinition(definitions, DicerSVID.CuttingPressure,
                "CuttingPressure", "切割压力", typeof(float), "kPa", "Process");

            AddSvidDefinition(definitions, DicerSVID.SpindleSpeed,
                "SpindleSpeed", "主轴转速", typeof(uint), "RPM", "Process");

            AddSvidDefinition(definitions, DicerSVID.CoolingFlow,
                "CoolingFlow", "冷却流量", typeof(float), "L/min", "Process");

            AddSvidDefinition(definitions, DicerSVID.VacuumPressure,
                "VacuumPressure", "真空压力", typeof(float), "Pa", "Process");

            // 性能统计SVID
            AddSvidDefinition(definitions, DicerSVID.TotalProcessed,
                "TotalProcessed", "总处理数", typeof(uint), "片", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.GoodCount,
                "GoodCount", "良品数", typeof(uint), "片", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.NGCount,
                "NGCount", "不良品数", typeof(uint), "片", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.YieldRate,
                "YieldRate", "良率", typeof(float), "%", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.UPH,
                "UPH", "每小时产量", typeof(float), "片/小时", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.MTBF,
                "MTBF", "平均故障间隔时间", typeof(float), "小时", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.MTTR,
                "MTTR", "平均修复时间", typeof(float), "小时", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.Uptime,
                "Uptime", "设备运行时间", typeof(uint), "秒", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.Downtime,
                "Downtime", "设备停机时间", typeof(uint), "秒", "Statistics");

            AddSvidDefinition(definitions, DicerSVID.OEE,
                "OEE", "设备综合效率", typeof(float), "%", "Statistics");

            Logger.LogDebug($"初始化了 {definitions.Count} 个SVID定义");
            return definitions;
        }

        /// <summary>
        /// 添加SVID定义
        /// </summary>
        private void AddSvidDefinition(
            Dictionary<uint, SvidDefinition> definitions,
            uint svid,
            string name,
            string description,
            Type dataType,
            string units,
            string category)
        {
            definitions[svid] = new SvidDefinition
            {
                Id = svid,
                Name = name,
                Description = description,
                DataType = dataType,
                Units = units,
                Category = category,
                DefaultValue = GetDefaultValue(dataType)
            };
        }

        /// <summary>
        /// 获取数据类型的默认值
        /// </summary>
        private object GetDefaultValue(Type dataType)
        {
            if (dataType == typeof(string))
                return "";
            if (dataType == typeof(float))
                return 0.0f;
            if (dataType == typeof(uint))
                return 0u;
            if (dataType == typeof(int))
                return 0;
            if (dataType == typeof(bool))
                return false;

            return 0;
        }

        #endregion

        #region 内部类

        /// <summary>
        /// SVID定义
        /// </summary>
        private class SvidDefinition
        {
            /// <summary>
            /// SVID标识
            /// </summary>
            public uint Id { get; set; }

            /// <summary>
            /// SVID名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// SVID描述
            /// </summary>
            public string Description { get; set; } = "";

            /// <summary>
            /// 数据类型
            /// </summary>
            public Type DataType { get; set; } = typeof(object);

            /// <summary>
            /// 单位
            /// </summary>
            public string Units { get; set; } = "";

            /// <summary>
            /// 类别
            /// </summary>
            public string Category { get; set; } = "";

            /// <summary>
            /// 默认值
            /// </summary>
            public object DefaultValue { get; set; } = 0;

            /// <summary>
            /// 最小值（可选）
            /// </summary>
            public object? MinValue { get; set; }

            /// <summary>
            /// 最大值（可选）
            /// </summary>
            public object? MaxValue { get; set; }

            /// <summary>
            /// 是否为只读
            /// </summary>
            public bool IsReadOnly { get; set; } = true;

            /// <summary>
            /// PLC地址（如果来自PLC）
            /// </summary>
            public string? PlcAddress { get; set; }
        }

        #endregion
    }
}
