// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F29Handler.cs
// 版本: v1.0.0
// 描述: S2F29消息处理器 - Equipment Constant Namelist Request 设备常量名称列表请求处理器

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
    /// S2F29 (Equipment Constant Namelist Request) 处理器
    /// 处理主机的设备常量名称列表请求，返回设备支持的所有或指定ECID的名称信息
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S2F29: 设备常量名称列表请求 - 主机查询设备常量的定义信息
    /// - S2F30: 设备常量名称列表 - 设备返回ECID的名称、格式、范围等元数据
    /// 
    /// 交互流程：
    /// 1. 主机发送 S2F29 包含ECID列表（空列表表示查询所有）
    /// 2. 设备验证请求的ECID有效性
    /// 3. 收集ECID的名称、单位、最小值、最大值、默认值等元数据
    /// 4. 返回 S2F30 包含ECID定义信息
    /// 
    /// 与S2F13的区别：
    /// - S2F13/S2F14: 查询/返回ECID的当前值
    /// - S2F29/S2F30: 查询/返回ECID的定义和元数据
    /// 
    /// 划裂片设备ECID元数据包括：
    /// - 名称：人类可读的参数名称
    /// - 格式：数据类型（U1/U2/U4/I1/I2/I4/F4/F8/A）
    /// - 单位：物理单位（mm/s、kPa、RPM等）
    /// - 范围：最小值和最大值
    /// - 默认值：出厂默认值
    /// - 权限：只读/可写属性
    /// </remarks>
    public class S2F29Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>ECID定义字典</summary>
        private readonly Dictionary<uint, EcidMetadata> _ecidMetadata;

        /// <summary>PLC数据提供者（可选）</summary>
        private readonly IPlcDataProvider? _plcProvider;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 2;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 29;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S2F29Handler(
            ILogger<S2F29Handler> logger,
            IOptions<EquipmentSystemConfiguration> options,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _plcProvider = plcProvider;

            // 初始化ECID元数据
            _ecidMetadata = InitializeEcidMetadata();

            Logger.LogInformation($"S2F29处理器已初始化，定义了 {_ecidMetadata.Count} 个ECID元数据");
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S2F29 消息，返回 S2F30 响应
        /// </summary>
        /// <param name="message">接收到的S2F29消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F30响应消息</returns>
        /// <remarks>
        /// S2F29 处理逻辑：
        /// 1. 解析请求的ECID列表
        /// 2. 空列表表示请求所有ECID
        /// 3. 验证每个ECID的有效性
        /// 4. 收集ECID的元数据信息
        /// 5. 构建并返回S2F30响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(
            SecsMessage message,
            CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S2F29 (Equipment Constant Namelist Request) 设备常量名称列表请求");

            try
            {
                // 解析请求的ECID列表
                var requestedEcids = ParseEcidList(message.SecsItem);

                if (requestedEcids.Count == 0)
                {
                    // 空列表表示请求所有ECID
                    Logger.LogDebug("请求所有设备常量名称列表");
                    requestedEcids = _ecidMetadata.Keys.OrderBy(e => e).ToList();
                }
                else
                {
                    Logger.LogDebug($"请求 {requestedEcids.Count} 个设备常量名称: [{string.Join(", ", requestedEcids)}]");
                }

                // 构建响应项列表
                var responseItems = new List<Item>();

                foreach (var ecid in requestedEcids)
                {
                    try
                    {
                        var namelistEntry = await CreateNamelistEntry(ecid, cancellationToken);
                        responseItems.Add(namelistEntry);

                        Logger.LogTrace($"ECID {ecid} 名称信息已添加");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"获取ECID {ecid} 名称信息失败");
                        // 添加空条目表示该ECID无效或不存在
                        responseItems.Add(CreateInvalidEcidEntry(ecid));
                    }
                }

                // 构建S2F30响应
                var s2f30 = new SecsMessage(2, 30, false)
                {
                    Name = "EquipmentConstantNamelistReply",
                    SecsItem = Item.L(responseItems)
                };

                Logger.LogInformation($"S2F30响应准备就绪，包含 {responseItems.Count} 个ECID定义");
                return s2f30;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S2F29消息失败");

                // 返回空列表表示错误
                return new SecsMessage(2, 30, false)
                {
                    Name = "EquipmentConstantNamelistReply",
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
                Logger.LogWarning("S2F29消息格式无效，返回空列表");
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
                    uint ecid = ParseEcidValue(ecidItem);
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

        /// <summary>
        /// 解析ECID值
        /// </summary>
        private uint ParseEcidValue(Item item)
        {
            return item.Format switch
            {
                SecsFormat.U1 => item.FirstValue<byte>(),
                SecsFormat.U2 => item.FirstValue<ushort>(),
                SecsFormat.U4 => item.FirstValue<uint>(),
                SecsFormat.I1 => (uint)Math.Max((sbyte)0, item.FirstValue<sbyte>()),
                SecsFormat.I2 => (uint)Math.Max((short)0, item.FirstValue<short>()),
                SecsFormat.I4 => (uint)Math.Max(0, item.FirstValue<int>()),
                _ => 0
            };
        }

        #endregion

        #region 私有方法 - 名称列表条目构建

        /// <summary>
        /// 创建名称列表条目
        /// </summary>
        /// <param name="ecid">设备常量ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>名称列表条目</returns>
        private async Task<Item> CreateNamelistEntry(uint ecid, CancellationToken cancellationToken)
        {
            // 获取ECID元数据
            if (!_ecidMetadata.TryGetValue(ecid, out var metadata))
            {
                Logger.LogWarning($"ECID {ecid} 未定义");
                return CreateUndefinedEcidEntry(ecid);
            }

            // S2F30 ECID条目格式可以有多种，这里使用扩展格式：
            // L,6
            //   1. U4 ECID - 设备常量ID
            //   2. A ECNAME - 名称
            //   3. A FORMAT - 格式（U1/U2/U4/I1/I2/I4/F4/F8/A/B）
            //   4. A MIN - 最小值（字符串表示）
            //   5. A MAX - 最大值（字符串表示）
            //   6. A DEFAULT - 默认值（字符串表示）
            //   7. A UNITS - 单位
            //   8. BOOL WRITABLE - 是否可写

            var items = new List<Item>
            {
                Item.U4(ecid),                                      // ECID
                Item.A(metadata.Name),                              // 名称
                Item.A(metadata.Format),                            // 格式
                Item.A(metadata.MinValue?.ToString() ?? ""),        // 最小值
                Item.A(metadata.MaxValue?.ToString() ?? ""),        // 最大值
                Item.A(metadata.DefaultValue?.ToString() ?? ""),    // 默认值
                Item.A(metadata.Units ?? ""),                       // 单位
                Item.Boolean(!metadata.IsReadOnly)                  // 是否可写
            };

            // 如果需要从PLC获取实时限值，可以在这里添加
            if (!string.IsNullOrEmpty(metadata.PlcAddress) && _plcProvider?.IsConnected == true)
            {
                await EnrichWithPlcData(metadata, cancellationToken);
            }

            return Item.L(items);
        }

        /// <summary>
        /// 创建无效ECID条目
        /// </summary>
        private Item CreateInvalidEcidEntry(uint ecid)
        {
            return Item.L(
                Item.U4(ecid),
                Item.A("INVALID"),
                Item.A(""),
                Item.A(""),
                Item.A(""),
                Item.A(""),
                Item.A(""),
                Item.Boolean(false)
            );
        }

        /// <summary>
        /// 创建未定义ECID条目
        /// </summary>
        private Item CreateUndefinedEcidEntry(uint ecid)
        {
            return Item.L(
                Item.U4(ecid),
                Item.A($"ECID_{ecid}"),
                Item.A("U4"),
                Item.A(""),
                Item.A(""),
                Item.A("0"),
                Item.A(""),
                Item.Boolean(false)
            );
        }

        /// <summary>
        /// 从PLC补充数据
        /// </summary>
        private async Task EnrichWithPlcData(EcidMetadata metadata, CancellationToken cancellationToken)
        {
            try
            {
                // 可以从PLC读取实时的限值等信息
                await Task.CompletedTask; // 实际实现时替换
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, $"从PLC读取ECID {metadata.Id} 元数据失败");
            }
        }

        #endregion

        #region 私有方法 - ECID元数据初始化

        /// <summary>
        /// 初始化ECID元数据字典
        /// </summary>
        private Dictionary<uint, EcidMetadata> InitializeEcidMetadata()
        {
            var metadata = new Dictionary<uint, EcidMetadata>();

            #region 设备配置常量 (1-99)

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.DeviceId,
                name: "DeviceId",
                format: "U2",
                units: "",
                defaultValue: _config.Equipment.DeviceId,
                isReadOnly: true,
                description: "设备标识号");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.ModelName,
                name: "ModelName",
                format: "A",
                units: "",
                defaultValue: _config.Equipment.ModelName,
                isReadOnly: true,
                description: "设备型号");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.Manufacturer,
                name: "Manufacturer",
                format: "A",
                units: "",
                defaultValue: "AIMFAB",
                isReadOnly: true,
                description: "制造商名称");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.SerialNumber,
                name: "SerialNumber",
                format: "A",
                units: "",
                defaultValue: "SN2024001",
                isReadOnly: true,
                description: "设备序列号");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.SoftwareVersion,
                name: "SoftwareVersion",
                format: "A",
                units: "",
                defaultValue: _config.Equipment.SoftwareRevision,
                isReadOnly: true,
                description: "软件版本");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.HardwareVersion,
                name: "HardwareVersion",
                format: "A",
                units: "",
                defaultValue: "HW1.0",
                isReadOnly: true,
                description: "硬件版本");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.MaxWaferSize,
                name: "MaxWaferSize",
                format: "U4",
                units: "mm",
                minValue: 100,
                maxValue: 450,
                defaultValue: 300,
                isReadOnly: true,
                description: "最大晶圆尺寸");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.MinWaferSize,
                name: "MinWaferSize",
                format: "U4",
                units: "mm",
                minValue: 50,
                maxValue: 200,
                defaultValue: 100,
                isReadOnly: true,
                description: "最小晶圆尺寸");

            #endregion

            #region 通信参数 (100-199)

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.T3Timeout,
                name: "T3Timeout",
                format: "U4",
                units: "ms",
                minValue: 1000,
                maxValue: 120000,
                defaultValue: _config.Equipment.T3,
                isReadOnly: false,
                description: "T3回复超时时间");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.T5Timeout,
                name: "T5Timeout",
                format: "U4",
                units: "ms",
                minValue: 1000,
                maxValue: 60000,
                defaultValue: _config.Equipment.T5,
                isReadOnly: false,
                description: "T5连接分离超时时间");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.T6Timeout,
                name: "T6Timeout",
                format: "U4",
                units: "ms",
                minValue: 1000,
                maxValue: 30000,
                defaultValue: _config.Equipment.T6,
                isReadOnly: false,
                description: "T6控制超时时间");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.T7Timeout,
                name: "T7Timeout",
                format: "U4",
                units: "ms",
                minValue: 1000,
                maxValue: 60000,
                defaultValue: _config.Equipment.T7,
                isReadOnly: false,
                description: "T7未选择超时时间");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.T8Timeout,
                name: "T8Timeout",
                format: "U4",
                units: "ms",
                minValue: 1000,
                maxValue: 30000,
                defaultValue: _config.Equipment.T8,
                isReadOnly: false,
                description: "T8网络超时时间");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.LinkTestInterval,
                name: "LinkTestInterval",
                format: "U4",
                units: "ms",
                minValue: 10000,
                maxValue: 300000,
                defaultValue: _config.Equipment.LinkTestInterval,
                isReadOnly: false,
                description: "连接测试间隔");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.RetryLimit,
                name: "RetryLimit",
                format: "U1",
                units: "",
                minValue: 0,
                maxValue: 10,
                defaultValue: 3,
                isReadOnly: false,
                description: "重试限制次数");

            AddEcidMetadata(metadata, SemiIdDefinitions.Ecid.MaxSpoolSize,
                name: "MaxSpoolSize",
                format: "U4",
                units: "KB",
                minValue: 1024,
                maxValue: 1048576,
                defaultValue: 10240,
                isReadOnly: true,
                description: "最大缓存大小");

            #endregion

            #region 工艺参数限值 (200-299)

            AddEcidMetadata(metadata, 200,
                name: "MaxCuttingSpeed",
                format: "F4",
                units: "mm/s",
                minValue: 10.0f,
                maxValue: 500.0f,
                defaultValue: 300.0f,
                isReadOnly: false,
                description: "最大切割速度");

            AddEcidMetadata(metadata, 201,
                name: "MinCuttingSpeed",
                format: "F4",
                units: "mm/s",
                minValue: 1.0f,
                maxValue: 100.0f,
                defaultValue: 10.0f,
                isReadOnly: false,
                description: "最小切割速度");

            AddEcidMetadata(metadata, 202,
                name: "MaxCuttingPressure",
                format: "F4",
                units: "kPa",
                minValue: 100.0f,
                maxValue: 1000.0f,
                defaultValue: 500.0f,
                isReadOnly: false,
                description: "最大切割压力");

            AddEcidMetadata(metadata, 203,
                name: "MinCuttingPressure",
                format: "F4",
                units: "kPa",
                minValue: 10.0f,
                maxValue: 200.0f,
                defaultValue: 50.0f,
                isReadOnly: false,
                description: "最小切割压力");

            AddEcidMetadata(metadata, 204,
                name: "MaxSpindleSpeed",
                format: "U4",
                units: "RPM",
                minValue: 1000,
                maxValue: 80000,
                defaultValue: 60000,
                isReadOnly: false,
                description: "最大主轴转速");

            AddEcidMetadata(metadata, 205,
                name: "MinSpindleSpeed",
                format: "U4",
                units: "RPM",
                minValue: 100,
                maxValue: 10000,
                defaultValue: 1000,
                isReadOnly: false,
                description: "最小主轴转速");

            #endregion

            #region 维护参数 (300-399)

            AddEcidMetadata(metadata, 300,
                name: "MaintenanceInterval",
                format: "U4",
                units: "hours",
                minValue: 1,
                maxValue: 10000,
                defaultValue: 500,
                isReadOnly: false,
                description: "保养间隔时间");

            AddEcidMetadata(metadata, 301,
                name: "KnifeLifeLimit",
                format: "U4",
                units: "cuts",
                minValue: 1000,
                maxValue: 1000000,
                defaultValue: 100000,
                isReadOnly: false,
                description: "刀具寿命限制");

            AddEcidMetadata(metadata, 302,
                name: "KnifeWarningThreshold",
                format: "U1",
                units: "%",
                minValue: 50,
                maxValue: 95,
                defaultValue: 80,
                isReadOnly: false,
                description: "刀具预警阈值");

            #endregion

            #region SEMI标准要求的ECID (675)

            AddEcidMetadata(metadata, 675,
                name: "TimeFormat",
                format: "U1",
                units: "",
                minValue: 0,
                maxValue: 1,
                defaultValue: 1,
                isReadOnly: false,
                description: "时钟格式(0=12小时制,1=24小时制)");

            #endregion

            #region 划裂片专用参数 (1000-1999)

            AddEcidMetadata(metadata, 1000,
                name: "DefaultCuttingSpeed",
                format: "F4",
                units: "mm/s",
                minValue: 10.0f,
                maxValue: 300.0f,
                defaultValue: 100.0f,
                isReadOnly: false,
                plcAddress: "D1000",
                description: "默认切割速度");

            AddEcidMetadata(metadata, 1001,
                name: "DefaultCuttingPressure",
                format: "F4",
                units: "kPa",
                minValue: 50.0f,
                maxValue: 500.0f,
                defaultValue: 200.0f,
                isReadOnly: false,
                plcAddress: "D1002",
                description: "默认切割压力");

            AddEcidMetadata(metadata, 1002,
                name: "DefaultSpindleSpeed",
                format: "U4",
                units: "RPM",
                minValue: 1000,
                maxValue: 60000,
                defaultValue: 30000,
                isReadOnly: false,
                plcAddress: "D1004",
                description: "默认主轴转速");

            AddEcidMetadata(metadata, 1003,
                name: "KerfWidth",
                format: "F4",
                units: "μm",
                minValue: 10.0f,
                maxValue: 200.0f,
                defaultValue: 50.0f,
                isReadOnly: false,
                description: "切割槽宽度");

            AddEcidMetadata(metadata, 1004,
                name: "ChuckVacuumLevel",
                format: "F4",
                units: "kPa",
                minValue: 50.0f,
                maxValue: 100.0f,
                defaultValue: 80.0f,
                isReadOnly: false,
                description: "吸盘真空度");

            AddEcidMetadata(metadata, 1005,
                name: "AlignmentTolerance",
                format: "F4",
                units: "μm",
                minValue: 1.0f,
                maxValue: 20.0f,
                defaultValue: 5.0f,
                isReadOnly: false,
                description: "对准容差");

            AddEcidMetadata(metadata, 1006,
                name: "EdgeExclusion",
                format: "F4",
                units: "mm",
                minValue: 1.0f,
                maxValue: 10.0f,
                defaultValue: 3.0f,
                isReadOnly: false,
                description: "边缘排除区域");

            AddEcidMetadata(metadata, 1007,
                name: "DicingMode",
                format: "U1",
                units: "",
                minValue: 0,
                maxValue: 2,
                defaultValue: 0,
                isReadOnly: false,
                description: "切割模式(0=单刀,1=双刀,2=步进)");

            AddEcidMetadata(metadata, 1008,
                name: "CoolingMode",
                format: "U1",
                units: "",
                minValue: 0,
                maxValue: 2,
                defaultValue: 0,
                isReadOnly: false,
                description: "冷却模式(0=水冷,1=气冷,2=混合)");

            AddEcidMetadata(metadata, 1009,
                name: "CleaningMode",
                format: "U1",
                units: "",
                minValue: 0,
                maxValue: 2,
                defaultValue: 1,
                isReadOnly: false,
                description: "清洁模式(0=手动,1=自动,2=半自动)");

            #endregion

            Logger.LogDebug($"初始化了 {metadata.Count} 个ECID元数据定义");
            return metadata;
        }

        /// <summary>
        /// 添加ECID元数据
        /// </summary>
        private void AddEcidMetadata(
            Dictionary<uint, EcidMetadata> metadata,
            uint ecid,
            string name,
            string format,
            string units,
            object? minValue = null,
            object? maxValue = null,
            object? defaultValue = null,
            bool isReadOnly = false,
            string? plcAddress = null,
            string? description = null)
        {
            metadata[ecid] = new EcidMetadata
            {
                Id = ecid,
                Name = name,
                Format = format,
                Units = units,
                MinValue = minValue,
                MaxValue = maxValue,
                DefaultValue = defaultValue ?? GetDefaultValueByFormat(format),
                IsReadOnly = isReadOnly,
                PlcAddress = plcAddress,
                Description = description ?? name
            };
        }

        /// <summary>
        /// 根据格式获取默认值
        /// </summary>
        private object GetDefaultValueByFormat(string format)
        {
            return format switch
            {
                "A" => "",
                "B" => new byte[0],
                "BOOL" => false,
                "U1" => (byte)0,
                "U2" => (ushort)0,
                "U4" => 0u,
                "U8" => 0ul,
                "I1" => (sbyte)0,
                "I2" => (short)0,
                "I4" => 0,
                "I8" => 0L,
                "F4" => 0.0f,
                "F8" => 0.0,
                _ => 0
            };
        }

        #endregion

        #region 内部类

        /// <summary>
        /// ECID元数据定义
        /// </summary>
        private class EcidMetadata
        {
            /// <summary>ECID标识</summary>
            public uint Id { get; set; }

            /// <summary>名称</summary>
            public string Name { get; set; } = "";

            /// <summary>格式（U1/U2/U4/I1/I2/I4/F4/F8/A/B）</summary>
            public string Format { get; set; } = "U4";

            /// <summary>单位</summary>
            public string? Units { get; set; }

            /// <summary>最小值</summary>
            public object? MinValue { get; set; }

            /// <summary>最大值</summary>
            public object? MaxValue { get; set; }

            /// <summary>默认值</summary>
            public object? DefaultValue { get; set; }

            /// <summary>是否只读</summary>
            public bool IsReadOnly { get; set; }

            /// <summary>PLC地址（如果来自PLC）</summary>
            public string? PlcAddress { get; set; }

            /// <summary>描述</summary>
            public string? Description { get; set; }
        }

        #endregion
    }
}
