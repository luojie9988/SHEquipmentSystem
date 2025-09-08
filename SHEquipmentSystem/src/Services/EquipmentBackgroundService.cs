// 文件路径: src/DiceEquipmentSystem/Services/EquipmentBackgroundService.cs
// 版本: v1.0.0
// 描述: 设备后台服务

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 设备后台服务
    /// 负责周期性任务和状态维护
    /// </summary>
    public class EquipmentBackgroundService : BackgroundService
    {
        private readonly ILogger<EquipmentBackgroundService> _logger;
        private readonly IEquipmentStateService _stateService;
        private readonly IStatusVariableService _svidService;

        public EquipmentBackgroundService(
            ILogger<EquipmentBackgroundService> logger,
            IEquipmentStateService stateService,
            IStatusVariableService svidService)
        {
            _logger = logger;
            _stateService = stateService;
            _svidService = svidService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("设备后台服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 更新时钟变量
                    await _svidService.SetSvidValueAsync(14, DateTime.Now.ToString("yyyyMMddHHmmss"));

                    // 可以添加其他周期性任务

                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "后台服务执行异常");
                }
            }

            _logger.LogInformation("设备后台服务已停止");
        }
    }
}
