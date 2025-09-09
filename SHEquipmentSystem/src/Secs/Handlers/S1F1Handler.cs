// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F1Handler.cs
// 版本: v1.0.0
// 描述: S1F1 Are You There 消息处理器实现

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Secs4Net;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.Services.Interfaces;
using static Secs4Net.Item;
using DiceEquipmentSystem.Secs.Interfaces;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S1F1 Are You There Request 处理器
    /// 用于响应主机的心跳检测请求
    /// </summary>
    public class S1F1Handler : SecsMessageHandlerBase
    {
        private readonly IEquipmentStateService _stateService;
        private readonly DiceDataModel _dataModel;

        public override byte Stream => 1;
        public override byte Function => 1;

        // 设备信息常量
        private const string MODEL_NAME = "DICE-3000X";
        private const string SOFTWARE_REVISION = "1.0.0";

        public S1F1Handler(
            ILogger<S1F1Handler> logger,
            IEquipmentStateService stateService,
            DiceDataModel dataModel) : base(logger)
        {
            _stateService = stateService;
            _dataModel = dataModel;
        }

        /// <summary>
        /// 处理S1F1消息
        /// </summary>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage request, CancellationToken cancellationToken)
        {
            try
            {
                ThrowIfDisposed();
                LogHandlingStart(request);

                var startTime = DateTime.UtcNow;

                // 验证消息格式
                if (request.SecsItem != null && request.SecsItem.Count > 0)
                {
                    Logger.LogWarning("[S1F1] 收到非空的S1F1消息，忽略内容");
                }

                // 检查设备状态
                var controlState = await _stateService.GetControlStateAsync();
                Logger.LogDebug($"[S1F1] 当前控制状态: {controlState}");

                // 构建S1F2响应
                var response = BuildS1F2Response();

                Logger.LogInformation($"[S1F2] 发送On Line Data: MDLN={MODEL_NAME}, SOFTREV={SOFTWARE_REVISION}");

                var processingTime = DateTime.UtcNow - startTime;
                LogHandlingComplete(request, response, processingTime);

                return response;
            }
            catch (Exception ex)
            {
                LogHandlingError(request, ex);
                // 返回空的S1F2表示错误
                return new SecsMessage(1, 2, replyExpected: false)
                {
                    Name = "OnLineData",
                    SecsItem = L()
                };
            }
        }

        /// <summary>
        /// 构建S1F2响应消息
        /// </summary>
        private SecsMessage BuildS1F2Response()
        {
            // S1F2格式：
            // L[2]
            //   A[n] <MDLN>    设备型号
            //   A[n] <SOFTREV> 软件版本

            var s1f2 = new SecsMessage(1, 2, replyExpected: false)
            {
                Name = "OnLineData",
                SecsItem = L(
                    A(MODEL_NAME),      // MDLN - 设备型号
                    A(SOFTWARE_REVISION) // SOFTREV - 软件版本
                )
            };

            return s1f2;
        }
    }
}
