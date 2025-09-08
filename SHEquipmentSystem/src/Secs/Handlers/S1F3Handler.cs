// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F3Handler.cs
// 版本: v1.0.0
// 描述: S1F3消息处理器 - 处理主机的状态变量请求

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
    /// S1F3 (Selected Equipment Status Request) 处理器
    /// 处理主机请求的状态变量(SVID)
    /// </summary>
    public class S1F3Handler : SecsMessageHandlerBase
    {
        private readonly IStatusVariableService _statusService;
        private readonly IPlcDataProvider? _plcProvider;
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 3;

        /// <summary>
        /// 构造函数
        /// </summary>
        public S1F3Handler(
            ILogger<S1F3Handler> logger,
            IStatusVariableService statusService,
            IOptions<EquipmentSystemConfiguration> options,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _plcProvider = plcProvider;
        }

        /// <summary>
        /// 处理S1F3消息，返回S1F4响应
        /// </summary>
        /// <param name="message">S1F3消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F4响应消息</returns>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogDebug("处理S1F3消息 - 状态变量请求");

            try
            {
                // 解析请求的SVID列表
                var svidList = ParseSvidList(message.SecsItem);

                if (svidList.Count == 0)
                {
                    // 空列表表示请求所有SVID
                    Logger.LogDebug("请求所有状态变量");
                    svidList = await _statusService.GetAllSvidListAsync();
                }
                else
                {
                    Logger.LogDebug($"请求 {svidList.Count} 个状态变量: [{string.Join(", ", svidList)}]");
                }

                // 获取状态变量值
                var responseItems = new List<Item>();

                foreach (var svid in svidList)
                {
                    try
                    {
                        var value = await GetSvidValueAsync(svid, cancellationToken);
                        responseItems.Add(value);
                        Logger.LogTrace($"SVID {svid} = {value}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"获取SVID {svid} 失败，返回默认值");
                        responseItems.Add(Item.L()); // 返回空列表表示无效
                    }
                }

                // 构建S1F4响应
                var s1f4 = new SecsMessage(1, 4, false)
                {
                    Name = "SelectedEquipmentStatusData",
                    SecsItem = Item.L(responseItems)
                };

                Logger.LogDebug($"S1F4响应准备就绪，包含 {responseItems.Count} 个状态值");
                return s1f4;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F3消息失败");

                // 返回空的S1F4表示错误
                return new SecsMessage(1, 4, false)
                {
                    Name = "SelectedEquipmentStatusData",
                    SecsItem = Item.L()
                };
            }
        }

        /// <summary>
        /// 解析SVID列表
        /// </summary>
        private List<uint> ParseSvidList(Item? item)
        {
            var svidList = new List<uint>();

            if (item == null || item.Count == 0)
            {
                return svidList; // 空列表
            }

            // 支持多种格式的SVID列表
            for (int i = 0; i < item.Count; i++)
            {
                try
                {
                    var svidItem = item.Items[i];

                    // 尝试不同的数据类型
                    if (svidItem.Format == SecsFormat.U1)
                    {
                        svidList.Add(svidItem.FirstValue<byte>());
                    }
                    else if (svidItem.Format == SecsFormat.U2)
                    {
                        svidList.Add(svidItem.FirstValue<ushort>());
                    }
                    else if (svidItem.Format == SecsFormat.U4)
                    {
                        svidList.Add(svidItem.FirstValue<uint>());
                    }
                    else
                    {
                        Logger.LogWarning($"不支持的SVID格式: {svidItem.Format}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"解析第 {i} 个SVID失败");
                }
            }

            return svidList;
        }

        /// <summary>
        /// 获取SVID值
        /// </summary>
        private async Task<Item> GetSvidValueAsync(uint svid, CancellationToken cancellationToken)
        {
            // 首先尝试从PLC获取数据（如果配置了PLC）
            if (_plcProvider != null && _config.SvidMapping.ContainsKey(svid))
            {
                try
                {
                    var plcAddress = _config.SvidMapping[svid];
                    var plcValue = await _plcProvider.ReadSvidAsync(svid, plcAddress, cancellationToken);
                    if (plcValue != null)
                    {
                        return ConvertToSecsItem(svid, plcValue);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"从PLC读取SVID {svid} 失败，使用默认值");
                }
            }

            // 从状态服务获取值
            var value = await _statusService.GetSvidValueAsync(svid);
            return ConvertToSecsItem(svid, value);
        }

        /// <summary>
        /// 将值转换为SECS Item
        /// </summary>
        private Item ConvertToSecsItem(uint svid, object value)
        {
            // 根据SVID确定数据类型
            return svid switch
            {
                // 控制状态变量 (720-724)
                720 => Item.U1((byte)(value ?? 2)),    // ControlMode
                721 => Item.U1((byte)(value ?? 5)),    // ControlState (OnlineRemote)
                722 => Item.A(GetProcessStateString(value)),    // ProcessState
                723 => Item.A(value?.ToString() ?? "STANDBY"), // EquipmentState
                724 => Item.U1((byte)(value ?? 1)),    // DataSyncState

                // 位置数据 (10010-10013)
                10010 => Item.F4(Convert.ToSingle(value ?? 0f)),  // X坐标
                10011 => Item.F4(Convert.ToSingle(value ?? 0f)),  // Y坐标
                10012 => Item.F4(Convert.ToSingle(value ?? 0f)),  // Z坐标
                10013 => Item.F4(Convert.ToSingle(value ?? 0f)),  // θ角度

                // 刀具信息 (10007-10009)
                10007 => Item.A(value?.ToString() ?? "BLADE-001"),  // 刀具型号
                10008 => Item.U4(Convert.ToUInt32(value ?? 0)),     // 使用次数
                10009 => Item.U4(Convert.ToUInt32(value ?? 10000)), // 最大使用次数

                // 材料信息 (1101, 1204, 18)
                1101 => Item.A(value?.ToString() ?? ""),     // LotID
                1204 => Item.A(value?.ToString() ?? ""),     // WaferID
                18 => Item.A(value?.ToString() ?? ""),       // PPID (RecipeID)

                // 时钟
                14 => Item.A(DateTime.Now.ToString("yyyyMMddHHmmss")), // Clock

                // 默认返回
                _ => Item.U4(0)
            };
        }

        /// <summary>
        /// 获取ProcessState的标准字符串表示
        /// </summary>
        private string GetProcessStateString(object value)
        {
            if (value is ProcessState state)
            {
                return ProcessStateHelper.ToSecsString(state);
            }
            else if (value is int intValue && Enum.IsDefined(typeof(ProcessState), intValue))
            {
                return ProcessStateHelper.ToSecsString((ProcessState)intValue);
            }
            else if (value is string strValue)
            {
                return strValue.ToUpper();
            }

            // 默认返回IDLE
            return "IDLE";
        }
    }
}
