// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F17Handler.cs
// 版本: v1.0.0
// 描述: S1F17 Request ON-LINE 处理器

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S1F17 (Request ON-LINE) 处理器
    /// 处理主机的在线请求
    /// </summary>
    public class S1F17Handler : SecsMessageHandlerBase
    {
        private readonly IEquipmentStateService _stateService;
        private readonly IS6F11Handler? _eventHandler;

        public override byte Stream => 1;
        public override byte Function => 17;

        public S1F17Handler(
            ILogger<S1F17Handler> logger,
            IEquipmentStateService stateService,
            IS6F11Handler? eventHandler = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _eventHandler = eventHandler;
        }

        /// <summary>
        /// 处理S1F17在线请求
        /// </summary>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到S1F17 (Request ON-LINE) 在线请求");

            try
            {
                // 检查通信是否已建立
                var statusInfo = await _stateService.GetStatusInfoAsync();

                if (!statusInfo.IsCommunicationEstablished)
                {
                    Logger.LogWarning("通信未建立，拒绝在线请求");
                    return new SecsMessage(1, 18, false)
                    {
                        Name = "OnLineAcknowledge",
                        SecsItem = Item.B(1) // ONLACK = 1: 拒绝
                    };
                }

                // 检查当前状态
                var currentState = statusInfo.ControlState;

                if (currentState == ControlState.OnlineRemote)
                {
                    Logger.LogInformation("设备已经处于OnlineRemote状态");
                    return new SecsMessage(1, 18, false)
                    {
                        Name = "OnLineAcknowledge",
                        SecsItem = Item.B(2) // ONLACK = 2: 已经在线
                    };
                }

                // 切换到在线状态
                var success = await _stateService.RequestOnlineAsync(remote: true);

                if (success)
                {
                    Logger.LogInformation("设备成功切换到OnlineRemote状态");

                    // 发送状态变更事件
                    if (_eventHandler != null)
                    {
                        await _eventHandler.SendEventReportAsync(
                            202, // ControlStateREMOTE
                            "ControlStateREMOTE",
                            null,
                            cancellationToken);
                    }

                    return new SecsMessage(1, 18, false)
                    {
                        Name = "OnLineAcknowledge",
                        SecsItem = Item.B(0) // ONLACK = 0: 接受，进入Remote
                    };
                }
                else
                {
                    Logger.LogError("切换到在线状态失败");
                    return new SecsMessage(1, 18, false)
                    {
                        Name = "OnLineAcknowledge",
                        SecsItem = Item.B(1) // ONLACK = 1: 拒绝
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理S1F17失败");
                return new SecsMessage(1, 18, false)
                {
                    Name = "OnLineAcknowledge",
                    SecsItem = Item.B(1) // ONLACK = 1: 拒绝
                };
            }
        }
    }
}
