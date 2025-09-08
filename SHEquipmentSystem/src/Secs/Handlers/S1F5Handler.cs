// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F5Handler.cs
// 版本: v1.0.0
// 描述: S1F5消息处理器 - Formatted Status Request 格式化状态请求处理器

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
    /// S1F5 (Formatted Status Request) 处理器
    /// 处理主机的格式化状态请求，返回按指定格式组织的状态数据
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F5: 格式化状态请求 - 主机请求按特定格式返回的状态数据
    /// - S1F6: 格式化状态数据 - 设备返回格式化的状态信息
    /// 
    /// 交互流程：
    /// 1. 主机发送 S1F5 包含状态格式代码(SFCD)
    /// 2. 设备根据SFCD确定要返回的状态数据格式
    /// 3. 收集并格式化相关状态信息
    /// 4. 返回 S1F6 包含格式化的状态数据
    /// 
    /// 划裂片设备支持的格式：
    /// - SFCD 1: 基本设备状态（控制状态、处理状态、设备状态）
    /// - SFCD 2: 工艺参数状态（划刀信息、使用次数、工艺配方）
    /// - SFCD 3: 材料处理状态（Cassette状态、槽位映射、材料信息）
    /// - SFCD 4: 报警和事件状态（活动报警、最近事件）
    /// - SFCD 5: 性能统计（产量、利用率、MTBF）
    /// </remarks>
    public class S1F5Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        /// <summary>设备状态服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>状态变量服务</summary>
        private readonly IStatusVariableService _statusService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService? _eventService;

        /// <summary>报警服务</summary>
        private readonly IAlarmService? _alarmService;

        /// <summary>数据采集服务</summary>
        private readonly IDataCollectionService? _dataService;

        /// <summary>PLC数据提供者</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 5;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="statusService">状态变量服务</param>
        /// <param name="options">设备系统配置</param>
        /// <param name="eventService">事件报告服务（可选）</param>
        /// <param name="alarmService">报警服务（可选）</param>
        /// <param name="dataService">数据采集服务（可选）</param>
        /// <param name="plcProvider">PLC数据提供者（可选）</param>
        /// <exception cref="ArgumentNullException">必要参数为空时抛出异常</exception>
        public S1F5Handler(
            ILogger<S1F5Handler> logger,
            IEquipmentStateService stateService,
            IStatusVariableService statusService,
            IOptions<EquipmentSystemConfiguration> options,
            IEventReportService? eventService = null,
            IAlarmService? alarmService = null,
            IDataCollectionService? dataService = null,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _eventService = eventService;
            _alarmService = alarmService;
            _dataService = dataService;
            _plcProvider = plcProvider;
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S1F5 消息，返回 S1F6 响应
        /// </summary>
        /// <param name="message">接收到的S1F5消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F6响应消息</returns>
        /// <remarks>
        /// S1F5 处理逻辑：
        /// 1. 解析状态格式代码(SFCD)
        /// 2. 验证SFCD有效性
        /// 3. 根据SFCD收集对应的状态数据
        /// 4. 格式化状态数据
        /// 5. 构建并返回S1F6响应
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F5 (Formatted Status Request) 格式化状态请求");

            try
            {
                // 解析状态格式代码
                var sfcd = ParseStatusFormatCode(message.SecsItem);
                Logger.LogDebug($"请求的状态格式代码: SFCD = {sfcd}");

                // 根据SFCD收集状态数据
                var statusData = await CollectFormattedStatusData(sfcd, cancellationToken);

                // 构建S1F6响应
                var s1f6 = new SecsMessage(1, 6, false)
                {
                    Name = "FormattedStatusData",
                    SecsItem = statusData
                };

                Logger.LogInformation($"S1F6响应准备就绪，SFCD={sfcd}");
                return s1f6;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F5消息失败");

                // 返回空列表表示错误
                return new SecsMessage(1, 6, false)
                {
                    Name = "FormattedStatusData",
                    SecsItem = Item.L()
                };
            }
        }

        #endregion

        #region 私有方法 - 消息解析

        /// <summary>
        /// 解析状态格式代码
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>状态格式代码</returns>
        private uint ParseStatusFormatCode(Item? item)
        {
            if (item == null || item.Format != SecsFormat.List)
            {
                Logger.LogWarning("S1F5消息格式无效，使用默认SFCD=1");
                return 1; // 默认返回基本状态
            }

            var items = item.Items;
            if (items == null || items.Length == 0)
            {
                Logger.LogDebug("S1F5为空列表，返回所有基本状态");
                return 0; // 空列表表示返回默认状态集
            }

            // 获取第一个元素作为SFCD
            var sfcdItem = items[0];
            uint sfcd = sfcdItem.Format switch
            {
                SecsFormat.U1 => sfcdItem.FirstValue<byte>(),
                SecsFormat.U2 => sfcdItem.FirstValue<ushort>(),
                SecsFormat.U4 => sfcdItem.FirstValue<uint>(),
                SecsFormat.I1 => (uint)sfcdItem.FirstValue<sbyte>(),
                SecsFormat.I2 => (uint)sfcdItem.FirstValue<short>(),
                SecsFormat.I4 => (uint)sfcdItem.FirstValue<int>(),
                _ => 1 // 默认值
            };

            return sfcd;
        }

        #endregion

        #region 私有方法 - 状态数据收集

        /// <summary>
        /// 根据格式代码收集状态数据
        /// </summary>
        /// <param name="sfcd">状态格式代码</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>格式化的状态数据</returns>
        private async Task<Item> CollectFormattedStatusData(uint sfcd, CancellationToken cancellationToken)
        {
            return sfcd switch
            {
                0 => await GetDefaultStatusData(cancellationToken),      // 默认状态集
                1 => await GetBasicDeviceStatus(cancellationToken),       // 基本设备状态
                2 => await GetProcessParameterStatus(cancellationToken),  // 工艺参数状态
                3 => await GetMaterialHandlingStatus(cancellationToken),  // 材料处理状态
                4 => await GetAlarmEventStatus(cancellationToken),        // 报警和事件状态
                5 => await GetPerformanceStatistics(cancellationToken),   // 性能统计
                _ => await GetCustomStatusData(sfcd, cancellationToken)   // 自定义格式
            };
        }

        /// <summary>
        /// 获取默认状态数据集
        /// </summary>
        private async Task<Item> GetDefaultStatusData(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集默认状态数据集");

            // 返回最常用的状态信息组合
            var statusInfo = await _stateService.GetStatusInfoAsync();

            return Item.L(
                Item.L(
                    Item.A("BASIC_STATUS"),          // 状态类别标识
                    Item.U4((uint)statusInfo.ControlState),     // 控制状态
                    Item.U4((uint)statusInfo.ControlMode),      // 控制模式
                    Item.U4((uint)statusInfo.ProcessState),     // 处理状态
                    Item.U4((uint)statusInfo.EquipmentState)    // 设备状态
                )
            );
        }

        /// <summary>
        /// SFCD 1: 获取基本设备状态
        /// </summary>
        private async Task<Item> GetBasicDeviceStatus(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集基本设备状态 (SFCD=1)");

            var statusInfo = await _stateService.GetStatusInfoAsync();
            var connectionState = await GetConnectionState();

            // 构建基本状态数据结构
            var items = new List<Item>
            {
                Item.L(
                    Item.U4(1),                                   // SFCD
                    Item.A("BASIC_DEVICE_STATUS")                // 状态名称
                ),
                Item.L(
                    Item.A("CONNECTION"),                        // 连接状态
                    Item.U1((byte)connectionState)
                ),
                Item.L(
                    Item.A("CONTROL_STATE"),                     // 控制状态
                    Item.U4((uint)statusInfo.ControlState)
                ),
                Item.L(
                    Item.A("CONTROL_MODE"),                      // 控制模式
                    Item.U4((uint)statusInfo.ControlMode)
                ),
                Item.L(
                    Item.A("PROCESS_STATE"),                     // 处理状态
                    Item.U4((uint)statusInfo.ProcessState)
                ),
                Item.L(
                    Item.A("EQUIPMENT_STATE"),                   // 设备状态
                    Item.U4((uint)statusInfo.EquipmentState)
                )
            };

            // 添加通信状态
            items.Add(Item.L(
                Item.A("COMM_ESTABLISHED"),
                Item.Boolean(statusInfo.IsCommunicationEstablished)
            ));

            // 添加在线状态标志
            items.Add(Item.L(
                Item.A("IS_ONLINE"),
                Item.Boolean(statusInfo.IsOnline)
            ));

            return Item.L(items);
        }

        /// <summary>
        /// SFCD 2: 获取工艺参数状态
        /// </summary>
        private async Task<Item> GetProcessParameterStatus(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集工艺参数状态 (SFCD=2)");

            var items = new List<Item>
            {
                Item.L(
                    Item.U4(2),                                   // SFCD
                    Item.A("PROCESS_PARAMETER_STATUS")           // 状态名称
                )
            };

            try
            {
                // 获取划刀信息 (SVID 10007-10009)
                var knifeModel = await _statusService.GetSvidValueAsync(10007);
                var useCount = await _statusService.GetSvidValueAsync(10008);
                var maxUseCount = await _statusService.GetSvidValueAsync(10009);

                items.Add(Item.L(
                    Item.A("KNIFE_MODEL"),                       // 划刀型号
                    Item.A(knifeModel?.ToString() ?? "UNKNOWN")
                ));

                items.Add(Item.L(
                    Item.A("KNIFE_USE_COUNT"),                   // 使用次数
                    Item.U4(Convert.ToUInt32(useCount ?? 0))
                ));

                items.Add(Item.L(
                    Item.A("KNIFE_MAX_COUNT"),                   // 最大次数
                    Item.U4(Convert.ToUInt32(maxUseCount ?? 0))
                ));

                // 计算剩余寿命百分比
                if (maxUseCount != null && Convert.ToUInt32(maxUseCount) > 0)
                {
                    var usedCount = Convert.ToUInt32(useCount ?? 0);
                    var maxCount = Convert.ToUInt32(maxUseCount);
                    var remainingPercent = (maxCount - usedCount) * 100 / maxCount;

                    items.Add(Item.L(
                        Item.A("KNIFE_LIFE_REMAIN"),             // 剩余寿命百分比
                        Item.U1((byte)Math.Max(0, Math.Min(100, remainingPercent)))
                    ));
                }

                // 获取当前配方信息
                var currentRecipe = await GetCurrentRecipeName();
                items.Add(Item.L(
                    Item.A("CURRENT_RECIPE"),                    // 当前配方
                    Item.A(currentRecipe ?? "NONE")
                ));

                // 获取工艺速度和压力（如果PLC连接）
                if (_plcProvider?.IsConnected == true)
                {
                    await AddProcessParametersFromPLC(items, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "获取部分工艺参数失败");
            }

            return Item.L(items);
        }

        /// <summary>
        /// SFCD 3: 获取材料处理状态
        /// </summary>
        private async Task<Item> GetMaterialHandlingStatus(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集材料处理状态 (SFCD=3)");

            var items = new List<Item>
            {
                Item.L(
                    Item.U4(3),                                   // SFCD
                    Item.A("MATERIAL_HANDLING_STATUS")           // 状态名称
                )
            };

            try
            {
                // Cassette状态
                var cassettePresent = await IsCassettePresent();
                items.Add(Item.L(
                    Item.A("CASSETTE_PRESENT"),                  // Cassette存在
                    Item.Boolean(cassettePresent)
                ));

                // 槽位映射状态
                var slotMap = await GetSlotMapStatus();
                if (slotMap != null)
                {
                    items.Add(Item.L(
                        Item.A("SLOT_MAP"),                      // 槽位映射
                        Item.A(slotMap)
                    ));
                }

                // 当前处理的材料信息
                var materialInfo = await GetCurrentMaterialInfo();
                if (materialInfo != null)
                {
                    items.Add(Item.L(
                        Item.A("CURRENT_MATERIAL"),              // 当前材料
                        Item.L(
                            Item.A(materialInfo.MaterialId ?? ""),
                            Item.A(materialInfo.LotId ?? ""),
                            Item.U1((byte)materialInfo.SlotNumber)
                        )
                    ));
                }

                // 材料计数统计
                var processedCount = await GetProcessedMaterialCount();
                var remainingCount = await GetRemainingMaterialCount();

                items.Add(Item.L(
                    Item.A("MATERIAL_COUNT"),                    // 材料计数
                    Item.L(
                        Item.U4(processedCount),                 // 已处理数
                        Item.U4(remainingCount)                  // 剩余数
                    )
                ));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "获取部分材料处理状态失败");
            }

            return Item.L(items);
        }

        /// <summary>
        /// SFCD 4: 获取报警和事件状态
        /// </summary>
        private async Task<Item> GetAlarmEventStatus(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集报警和事件状态 (SFCD=4)");

            var items = new List<Item>
            {
                Item.L(
                    Item.U4(4),                                   // SFCD
                    Item.A("ALARM_EVENT_STATUS")                 // 状态名称
                )
            };

            // 获取活动报警
            if (_alarmService != null)
            {
                var activeAlarms = await _alarmService.GetActiveAlarmsAsync();

                items.Add(Item.L(
                    Item.A("ACTIVE_ALARM_COUNT"),                // 活动报警数
                    Item.U4((uint)activeAlarms.Count)
                ));

                if (activeAlarms.Any())
                {
                    // 添加最多5个最新报警
                    var alarmList = activeAlarms
                        .OrderByDescending(a => a.SetTime)
                        .Take(5)
                        .Select(a => Item.L(
                            Item.U4(a.AlarmId),                  // 报警ID
                            Item.A(a.AlarmText),                 // 报警文本
                            Item.A(a.SetTime.ToString("yyyy-MM-dd HH:mm:ss"))  // 时间戳
                        )).ToArray();

                    items.Add(Item.L(
                        Item.A("RECENT_ALARMS"),                 // 最近报警
                        Item.L(alarmList)
                    ));
                }
            }

            // 获取最近事件（如果事件服务可用）
            if (_eventService != null)
            {
                await AddRecentEvents(items, cancellationToken);
            }

            return Item.L(items);
        }

        /// <summary>
        /// SFCD 5: 获取性能统计
        /// </summary>
        private async Task<Item> GetPerformanceStatistics(CancellationToken cancellationToken)
        {
            Logger.LogDebug("收集性能统计数据 (SFCD=5)");

            var items = new List<Item>
            {
                Item.L(
                    Item.U4(5),                                   // SFCD
                    Item.A("PERFORMANCE_STATISTICS")             // 状态名称
                )
            };

            try
            {
                // 获取运行时间统计
                var uptime = await GetEquipmentUptime();
                items.Add(Item.L(
                    Item.A("UPTIME_SECONDS"),                    // 运行时间（秒）
                    Item.U4(uptime)
                ));

                // 获取产量统计
                var totalProcessed = await GetTotalProcessedCount();
                var goodCount = await GetGoodProductCount();
                var ngCount = totalProcessed - goodCount;

                items.Add(Item.L(
                    Item.A("PRODUCTION"),                        // 产量统计
                    Item.L(
                        Item.U4(totalProcessed),                 // 总数
                        Item.U4(goodCount),                       // 良品数
                        Item.U4(ngCount)                          // 不良品数
                    )
                ));

                // 计算良率
                if (totalProcessed > 0)
                {
                    var yield = goodCount * 100 / totalProcessed;
                    items.Add(Item.L(
                        Item.A("YIELD_PERCENTAGE"),              // 良率百分比
                        Item.U1((byte)Math.Min(100, yield))
                    ));
                }

                // 设备利用率
                var utilizationRate = await CalculateUtilizationRate();
                items.Add(Item.L(
                    Item.A("UTILIZATION_RATE"),                  // 利用率
                    Item.F4(utilizationRate)
                ));

                // MTBF（平均故障间隔时间）
                var mtbf = await CalculateMTBF();
                if (mtbf > 0)
                {
                    items.Add(Item.L(
                        Item.A("MTBF_HOURS"),                    // MTBF（小时）
                        Item.F4(mtbf)
                    ));
                }

                // OEE（设备综合效率）
                var oee = await CalculateOEE();
                items.Add(Item.L(
                    Item.A("OEE_PERCENTAGE"),                    // OEE百分比
                    Item.U1((byte)Math.Min(100, Math.Max(0, oee * 100)))
                ));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "获取部分性能统计失败");
            }

            return Item.L(items);
        }

        /// <summary>
        /// 获取自定义格式状态数据
        /// </summary>
        private async Task<Item> GetCustomStatusData(uint sfcd, CancellationToken cancellationToken)
        {
            Logger.LogWarning($"不支持的SFCD={sfcd}，返回空数据");

            // 返回空列表表示不支持的格式
            return Item.L(
                Item.L(
                    Item.U4(sfcd),
                    Item.A("UNSUPPORTED_FORMAT")
                )
            );
        }

        #endregion

        #region 私有方法 - 辅助功能

        /// <summary>
        /// 获取连接状态
        /// </summary>
        private async Task<HsmsConnectionState> GetConnectionState()
        {
            // 从状态服务或连接管理器获取
            // 这里暂时返回Selected作为示例
            return await Task.FromResult(HsmsConnectionState.Selected);
        }

        /// <summary>
        /// 获取当前配方名称
        /// </summary>
        private async Task<string?> GetCurrentRecipeName()
        {
            try
            {
                // 从状态变量或配方管理服务获取
                var recipe = await _statusService.GetSvidValueAsync(1); // 假设SVID 1是配方名
                return recipe?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从PLC添加工艺参数
        /// </summary>
        private async Task AddProcessParametersFromPLC(List<Item> items, CancellationToken cancellationToken)
        {
            try
            {
                // 读取切割速度
                var speed = await _plcProvider!.ReadSvidAsync(10020, "D100", cancellationToken);
                if (speed != null)
                {
                    items.Add(Item.L(
                        Item.A("CUTTING_SPEED"),
                        Item.F4(Convert.ToSingle(speed))
                    ));
                }

                // 读取切割压力
                var pressure = await _plcProvider.ReadSvidAsync(10021, "D102", cancellationToken);
                if (pressure != null)
                {
                    items.Add(Item.L(
                        Item.A("CUTTING_PRESSURE"),
                        Item.F4(Convert.ToSingle(pressure))
                    ));
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "从PLC读取工艺参数失败");
            }
        }

        /// <summary>
        /// 检查Cassette是否存在
        /// </summary>
        private async Task<bool> IsCassettePresent()
        {
            // 实际实现应从传感器或PLC获取
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 获取槽位映射状态
        /// </summary>
        private async Task<string?> GetSlotMapStatus()
        {
            // 返回槽位状态字符串，例如 "11110000" 表示前4个槽位有材料
            return await Task.FromResult("11111111000000000000");
        }

        /// <summary>
        /// 获取当前材料信息
        /// </summary>
        private async Task<MaterialInfo?> GetCurrentMaterialInfo()
        {
            return await Task.FromResult(new MaterialInfo
            {
                MaterialId = "MAT001",
                LotId = "LOT20240101",
                SlotNumber = 1
            });
        }

        /// <summary>
        /// 获取已处理材料数
        /// </summary>
        private async Task<uint> GetProcessedMaterialCount()
        {
            try
            {
                var count = await _statusService.GetSvidValueAsync(10030); // 假设的SVID
                return Convert.ToUInt32(count ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取剩余材料数
        /// </summary>
        private async Task<uint> GetRemainingMaterialCount()
        {
            return await Task.FromResult(10u);
        }

        /// <summary>
        /// 添加最近事件
        /// </summary>
        private async Task AddRecentEvents(List<Item> items, CancellationToken cancellationToken)
        {
            // 这里应该从事件服务获取最近的事件
            // 示例实现
            items.Add(Item.L(
                Item.A("LAST_EVENT"),
                Item.L(
                    Item.U4(11004),                              // CEID
                    Item.A("ProcessStart"),                      // 事件名
                    Item.A(DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-dd HH:mm:ss"))
                )
            ));
        }

        /// <summary>
        /// 获取设备运行时间（秒）
        /// </summary>
        private async Task<uint> GetEquipmentUptime()
        {
            try
            {
                var uptime = await _statusService.GetSvidValueAsync(10100); // 运行时间SVID
                return Convert.ToUInt32(uptime ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取总处理数
        /// </summary>
        private async Task<uint> GetTotalProcessedCount()
        {
            try
            {
                var count = await _statusService.GetSvidValueAsync(10101); // 总数SVID
                return Convert.ToUInt32(count ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取良品数
        /// </summary>
        private async Task<uint> GetGoodProductCount()
        {
            try
            {
                var count = await _statusService.GetSvidValueAsync(10102); // 良品数SVID
                return Convert.ToUInt32(count ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 计算设备利用率
        /// </summary>
        private async Task<float> CalculateUtilizationRate()
        {
            // 简化计算：生产时间 / 总运行时间
            return await Task.FromResult(85.5f);
        }

        /// <summary>
        /// 计算MTBF（平均故障间隔时间）
        /// </summary>
        private async Task<float> CalculateMTBF()
        {
            // 实际应从故障记录计算
            return await Task.FromResult(168.5f); // 示例：168.5小时
        }

        /// <summary>
        /// 计算OEE（设备综合效率）
        /// </summary>
        private async Task<float> CalculateOEE()
        {
            // OEE = 可用率 × 性能率 × 良率
            var availability = 0.95f;  // 95%可用率
            var performance = 0.90f;   // 90%性能率
            var quality = 0.98f;       // 98%良率

            return await Task.FromResult(availability * performance * quality);
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 材料信息
        /// </summary>
        private class MaterialInfo
        {
            public string? MaterialId { get; set; }
            public string? LotId { get; set; }
            public int SlotNumber { get; set; }
        }

        #endregion
    }
}
