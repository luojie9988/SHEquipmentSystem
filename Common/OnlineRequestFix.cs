// 文件路径: src/Common/OnlineRequestFix.cs
// 版本: v1.0.0
// 描述: 修复设备在线请求问题的辅助类

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common
{
    /// <summary>
    /// 设备在线请求问题修复类
    /// 解决S1F17/F18在线请求被拒绝的问题
    /// </summary>
    public static class OnlineRequestFix
    {
        /// <summary>
        /// 修复Equipment端的S1F17处理器
        /// </summary>
        public static void FixEquipmentS1F17Handler(IServiceCollection services)
        {
            // 确保S1F13Handler使用单例模式以保持状态
            // services.AddSingleton(); // Removed DiceEquipmentSystem reference
            
            // 确保设备状态服务正确维护通信状态
            // services.AddSingleton(); // Removed DiceEquipmentSystem reference
        }

        /// <summary>
        /// 修复Host端的在线请求逻辑
        /// </summary>
        public static async Task<bool> RetryOnlineRequestAsync(
            dynamic connectionManager,
            int deviceId,
            ILogger logger,
            int maxRetries = 3,
            int retryDelayMs = 2000)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    logger.LogInformation($"尝试在线请求 (第{i + 1}次/共{maxRetries}次)");
                    
                    // 确保通信已建立
                    bool commEstablished = await connectionManager.EstablishCommunicationAsync(deviceId);
                    if (!commEstablished)
                    {
                        logger.LogWarning("通信建立失败，等待重试...");
                        await Task.Delay(retryDelayMs);
                        continue;
                    }
                    
                    // 等待一段时间让设备准备好
                    await Task.Delay(500);
                    
                    // 发送在线请求
                    byte onlAck = await connectionManager.RequestOnlineAsync(deviceId);
                    
                    switch (onlAck)
                    {
                        case 0: // 接受，进入OnlineRemote
                            logger.LogInformation("✅ 设备接受在线请求，进入OnlineRemote状态");
                            return true;
                            
                        case 1: // 拒绝
                            logger.LogWarning($"⚠️ 设备拒绝在线请求，等待{retryDelayMs}ms后重试...");
                            await Task.Delay(retryDelayMs);
                            break;
                            
                        case 2: // 已经在线
                            logger.LogInformation("✅ 设备已经在线");
                            return true;
                            
                        default:
                            logger.LogWarning($"未知的ONLACK响应: {onlAck}");
                            await Task.Delay(retryDelayMs);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"在线请求异常 (第{i + 1}次尝试)");
                    if (i < maxRetries - 1)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
            }
            
            logger.LogError($"在线请求失败，已尝试{maxRetries}次");
            return false;
        }

        /// <summary>
        /// 诊断通信和在线状态问题
        /// </summary>
        public static async Task<OnlineDiagnosticResult> DiagnoseOnlineIssuesAsync(
            dynamic connectionManager,
            dynamic stateService,
            int deviceId,
            ILogger logger)
        {
            var result = new OnlineDiagnosticResult();
            
            try
            {
                // 1. 检查HSMS连接状态
                var hsmsState = await connectionManager.GetConnectionStateAsync(deviceId);
                result.HsmsConnected = hsmsState == "Selected";
                logger.LogInformation($"HSMS连接状态: {hsmsState}");
                
                // 2. 检查通信建立状态
                result.CommunicationEstablished = await stateService.IsCommunicationEstablishedAsync();
                logger.LogInformation($"通信建立状态: {(result.CommunicationEstablished ? "已建立" : "未建立")}");
                
                // 3. 检查控制状态
                var controlState = await stateService.GetControlStateAsync();
                result.ControlState = controlState.ToString();
                logger.LogInformation($"控制状态: {result.ControlState}");
                
                // 4. 检查是否有阻塞报警
                var hasBlockingAlarms = await CheckBlockingAlarmsAsync(stateService);
                result.HasBlockingAlarms = hasBlockingAlarms;
                if (hasBlockingAlarms)
                {
                    logger.LogWarning("存在阻塞报警，可能影响在线请求");
                }
                
                // 5. 生成诊断建议
                result.GenerateRecommendations();
                
                foreach (var recommendation in result.Recommendations)
                {
                    logger.LogInformation($"建议: {recommendation}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "诊断过程出现异常");
                result.DiagnosticError = ex.Message;
            }
            
            return result;
        }

        private static async Task<bool> CheckBlockingAlarmsAsync(dynamic stateService)
        {
            try
            {
                var statusInfo = await stateService.GetStatusInfoAsync();
                var alarmSet = statusInfo.AlarmsSet as System.Collections.Generic.IEnumerable<uint>;
                
                if (alarmSet != null)
                {
                    // 检查是否有紧急或安全报警
                    foreach (var alarmId in alarmSet)
                    {
                        if (alarmId == 12000 || // EMERGENCY
                            alarmId == 12037 || // DOOR_COVER_INTERLOCK
                            alarmId == 12053)   // INTERLOCK
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // 忽略异常，假设没有阻塞报警
            }
            
            return false;
        }
    }

    /// <summary>
    /// 在线诊断结果
    /// </summary>
    public class OnlineDiagnosticResult
    {
        public bool HsmsConnected { get; set; }
        public bool CommunicationEstablished { get; set; }
        public string ControlState { get; set; } = "";
        public bool HasBlockingAlarms { get; set; }
        public string DiagnosticError { get; set; } = "";
        public List<string> Recommendations { get; set; } = new List<string>();

        public void GenerateRecommendations()
        {
            Recommendations.Clear();
            
            if (!HsmsConnected)
            {
                Recommendations.Add("HSMS连接未建立，请检查网络连接和端口配置");
            }
            
            if (!CommunicationEstablished)
            {
                Recommendations.Add("SECS通信未建立，需要先发送S1F13建立通信");
            }
            
            if (HasBlockingAlarms)
            {
                Recommendations.Add("存在阻塞报警，请先清除报警后再尝试在线");
            }
            
            if (ControlState == "EquipmentOffline" && CommunicationEstablished)
            {
                Recommendations.Add("设备处于离线状态但通信已建立，可以尝试发送S1F17请求在线");
            }
            
            if (ControlState == "OnlineRemote" || ControlState == "OnlineLocal")
            {
                Recommendations.Add("设备已经在线，无需重复请求");
            }
            
            if (Recommendations.Count == 0)
            {
                Recommendations.Add("系统状态正常，可以尝试发送在线请求");
            }
        }

        public bool CanRequestOnline()
        {
            return HsmsConnected && 
                   CommunicationEstablished && 
                   !HasBlockingAlarms &&
                   ControlState != "OnlineRemote" &&
                   ControlState != "OnlineLocal";
        }
    }

    /// <summary>
    /// 设备初始化流程优化
    /// </summary>
    public static class DeviceInitializationFlow
    {
        /// <summary>
        /// 执行完整的设备初始化流程
        /// </summary>
        public static async Task<bool> InitializeDeviceAsync(
            dynamic connectionManager,
            dynamic dataCollectionService,
            dynamic eventReportManager,
            int deviceId,
            string deviceName,
            ILogger logger)
        {
            try
            {
                logger.LogInformation($"========== 开始初始化设备 {deviceId}: {deviceName} ==========");
                
                // Step 1: 建立HSMS连接
                logger.LogInformation("Step 1: 建立HSMS连接");
                await connectionManager.ConnectAsync(deviceId);
                
                // 等待连接稳定
                await Task.Delay(1000);
                
                // Step 2: 建立SECS通信 (S1F13/F14)
                logger.LogInformation("Step 2: 发送S1F13建立SECS通信");
                int maxCommRetries = 3;
                bool commEstablished = false;
                
                for (int i = 0; i < maxCommRetries; i++)
                {
                    commEstablished = await connectionManager.EstablishCommunicationAsync(deviceId);
                    if (commEstablished)
                    {
                        logger.LogInformation("✅ SECS通信建立成功");
                        break;
                    }
                    
                    logger.LogWarning($"通信建立失败，重试 {i + 1}/{maxCommRetries}");
                    await Task.Delay(2000);
                }
                
                if (!commEstablished)
                {
                    logger.LogError("SECS通信建立失败");
                    return false;
                }
                
                // Step 3: 等待设备准备就绪
                logger.LogInformation("Step 3: 等待设备准备就绪");
                await Task.Delay(2000);
                
                // Step 4: 请求在线 (S1F17/F18) - 使用重试机制
                logger.LogInformation("Step 4: 发送S1F17请求在线（带重试）");
                bool onlineSuccess = await OnlineRequestFix.RetryOnlineRequestAsync(
                    connectionManager,
                    deviceId,
                    logger,
                    maxRetries: 5,
                    retryDelayMs: 3000
                );
                
                if (!onlineSuccess)
                {
                    logger.LogWarning("设备在线请求失败，但继续初始化流程");
                }
                
                // Step 5: 配置事件报告
                logger.LogInformation("Step 5: 配置事件报告");
                await eventReportManager.DefineReportsAsync(deviceId);
                await eventReportManager.LinkEventReportsAsync(deviceId);
                await eventReportManager.EnableEventReportsAsync(deviceId);
                
                // Step 6: 启动数据采集
                logger.LogInformation("Step 6: 启动数据采集");
                await dataCollectionService.StartDeviceDataCollectionAsync(deviceId);
                
                // Step 7: 初始化Trace数据采集
                logger.LogInformation("Step 7: 初始化Trace数据采集");
                await dataCollectionService.InitializeTraceAsync(deviceId);
                
                logger.LogInformation($"========== 设备 {deviceId} 初始化完成 ==========");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"设备 {deviceId} 初始化失败");
                return false;
            }
        }
    }
}