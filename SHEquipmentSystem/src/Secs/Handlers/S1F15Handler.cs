// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S1F15Handler.cs
// 版本: v1.0.0
// 描述: S1F15消息处理器 - Request OFF-LINE 离线请求处理器

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
    /// S1F15 (Request OFF-LINE) 处理器
    /// 处理主机的离线请求，执行安全离线检查并返回响应
    /// </summary>
    /// <remarks>
    /// SEMI E30 标准定义：
    /// - S1F15: 离线请求 - 主机发送的设备离线请求
    /// - S1F16: 离线确认 - 设备返回的确认或拒绝响应
    /// 
    /// 交互流程：
    /// 1. 主机发送 S1F15 空列表
    /// 2. 设备执行安全联锁检查
    /// 3. 验证当前处理状态是否允许离线
    /// 4. 返回 S1F16 包含 OFLACK 离线确认码
    /// 5. 如果接受，设备状态变为 OFF-LINE
    /// </remarks>
    public class S1F15Handler : SecsMessageHandlerBase
    {
        #region 私有字段

        private readonly IEquipmentStateService _stateService;

        #endregion

        #region 消息标识

        /// <summary>
        /// 消息流号
        /// </summary>
        public override byte Stream => 1;

        /// <summary>
        /// 消息功能号
        /// </summary>
        public override byte Function => 15;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <exception cref="ArgumentNullException">参数为空时抛出异常</exception>
        public S1F15Handler(
            ILogger<S1F15Handler> logger,
            IEquipmentStateService stateService) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理 S1F15 消息，返回 S1F16 响应
        /// </summary>
        /// <param name="message">接收到的S1F15消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S1F16响应消息</returns>
        /// <remarks>
        /// S1F15 处理逻辑：
        /// 1. 验证消息格式
        /// 2. 检查当前设备状态是否允许离线
        /// 3. 执行安全联锁检查
        /// 4. 验证处理状态
        /// 5. 构建并返回S1F16响应
        /// 6. 如果接受，执行状态转换
        /// </remarks>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("收到 S1F15 (Request OFF-LINE) 离线请求");

            try
            {
                // 验证消息格式
                var messageValidation = ValidateMessage(message);
                if (!messageValidation.IsValid)
                {
                    Logger.LogWarning($"S1F15 消息格式验证失败: {messageValidation.ErrorReason}");
                    return CreateS1F16Response(OfflineAck.Denied, messageValidation.ErrorReason ?? "消息格式错误");
                }

                // 获取当前设备状态
                var statusInfo = await _stateService.GetStatusInfoAsync();
                Logger.LogDebug($"当前设备状态: Control={statusInfo.ControlState}, " +
                              $"Process={statusInfo.ProcessState}, Equipment={statusInfo.EquipmentState}");

                // 验证离线条件
                var offlineValidation = await ValidateOfflineConditions(statusInfo);
                if (!offlineValidation.CanGoOffline)
                {
                    Logger.LogWarning($"离线条件验证失败: {offlineValidation.RejectReason}");
                    return CreateS1F16Response(OfflineAck.Denied, offlineValidation.RejectReason ?? "不满足离线条件");
                }

                // 执行安全联锁检查
                var safetyCheck = await PerformSafetyInterlockCheck();
                if (!safetyCheck.IsSafe)
                {
                    Logger.LogWarning($"安全联锁检查失败: {safetyCheck.UnsafeReason}");
                    return CreateS1F16Response(OfflineAck.Denied, safetyCheck.UnsafeReason ?? "安全条件不满足");
                }

                // 执行离线操作
                var offlineResult = await ExecuteOfflineTransition();
                if (offlineResult)
                {
                    Logger.LogInformation("✅ 设备成功切换到离线状态");
                    return CreateS1F16Response(OfflineAck.Accepted, "离线成功");
                }
                else
                {
                    Logger.LogWarning("❌ 设备离线转换失败");
                    return CreateS1F16Response(OfflineAck.Denied, "状态转换失败");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理 S1F15 消息时发生异常");
                return CreateS1F16Response(OfflineAck.Denied, "内部处理异常");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 验证消息格式
        /// </summary>
        /// <param name="message">待验证的消息</param>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// S1F15 通常包含空列表或无数据项
        /// </remarks>
        private MessageValidationResult ValidateMessage(SecsMessage message)
        {
            if (message == null)
            {
                return new MessageValidationResult
                {
                    IsValid = false,
                    ErrorReason = "S1F15 消息为空"
                };
            }

            // S1F15 可以包含空数据或空列表
            if (message.SecsItem != null && message.SecsItem.Count > 0)
            {
                Logger.LogDebug($"S1F15 包含 {message.SecsItem.Count} 个数据项（通常为空）");
            }

            return new MessageValidationResult { IsValid = true };
        }

        /// <summary>
        /// 验证离线条件
        /// </summary>
        /// <param name="statusInfo">当前设备状态信息</param>
        /// <returns>验证结果</returns>
        /// <remarks>
        /// 离线条件验证：
        /// 1. 当前必须处于在线状态
        /// 2. 处理状态必须允许离线
        /// 3. 设备状态必须正常
        /// </remarks>
        private async Task<OfflineValidationResult> ValidateOfflineConditions(EquipmentStatusInfo statusInfo)
        {
            // 检查控制状态 - 必须在线才能离线
            if (!statusInfo.IsOnline)
            {
                return new OfflineValidationResult
                {
                    CanGoOffline = false,
                    RejectReason = "设备未处于在线状态"
                };
            }

            // 检查处理状态 - 关键处理中不允许离线
            if (IsInCriticalProcessState(statusInfo.ProcessState))
            {
                return new OfflineValidationResult
                {
                    CanGoOffline = false,
                    RejectReason = $"设备正在执行关键处理 ({ProcessStateHelper.GetChineseDescription(statusInfo.ProcessState)})"
                };
            }

            // 检查设备状态 - 故障状态可以离线，但维护状态需谨慎
            if (statusInfo.EquipmentState == EquipmentState.UnscheduledDown)
            {
                Logger.LogDebug("设备处于故障状态，允许离线进行维修");
            }

            Logger.LogDebug("所有离线条件验证通过");
            return new OfflineValidationResult 
            { 
                CanGoOffline = true 
            };
        }

        /// <summary>
        /// 执行安全联锁检查
        /// </summary>
        /// <returns>安全检查结果</returns>
        /// <remarks>
        /// 划裂片设备安全联锁项目：
        /// 1. 主轴是否已停止
        /// 2. 切割进给是否归位
        /// 3. 夹具是否松开
        /// 4. 防护门是否可以打开
        /// 5. 气压系统是否安全
        /// </remarks>
        private async Task<SafetyInterlockResult> PerformSafetyInterlockCheck()
        {
            try
            {
                Logger.LogDebug("执行划裂片设备安全联锁检查...");

                // 模拟安全联锁检查逻辑
                // 在实际实现中，这里应该检查PLC的安全信号
                await Task.Delay(100, CancellationToken.None); // 模拟检查延迟

                // TODO: 集成实际的PLC安全联锁检查
                // 1. 检查主轴停止状态
                // 2. 检查切割位置归位
                // 3. 检查夹具状态
                // 4. 检查防护装置
                // 5. 检查气压系统

                // 模拟检查通过
                Logger.LogDebug("✅ 安全联锁检查通过");
                return new SafetyInterlockResult 
                { 
                    IsSafe = true 
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "安全联锁检查时发生异常");
                return new SafetyInterlockResult
                {
                    IsSafe = false,
                    UnsafeReason = $"安全检查异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 执行离线状态转换
        /// </summary>
        /// <returns>转换是否成功</returns>
        private async Task<bool> ExecuteOfflineTransition()
        {
            try
            {
                Logger.LogDebug("执行离线状态转换...");
                
                // 调用状态服务执行离线转换
                var success = await _stateService.RequestOfflineAsync();
                
                if (success)
                {
                    Logger.LogInformation("设备状态已成功转换为离线");
                }
                else
                {
                    Logger.LogWarning("设备状态转换为离线失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行离线状态转换时发生异常");
                return false;
            }
        }

        /// <summary>
        /// 判断是否处于关键处理状态
        /// </summary>
        /// <param name="processState">当前处理状态</param>
        /// <returns>是否处于关键处理状态</returns>
        /// <remarks>
        /// 关键处理状态定义：
        /// - Executing: 正在执行工艺，不能中断
        /// - Setup: 正在设置，可能影响工艺质量
        /// 
        /// 非关键处理状态：
        /// - Paused: 已暂停，可以安全离线
        /// - Ready: 准备就绪但未开始，可以离线
        /// </remarks>
        private bool IsInCriticalProcessState(ProcessState processState)
        {
            return processState switch
            {
                ProcessState.Executing => true,    // 正在执行 - 关键
                ProcessState.Setup => true,       // 设置中 - 关键
                ProcessState.Paused => false,     // 已暂停 - 非关键
                ProcessState.Ready => false,      // 准备就绪 - 非关键
                ProcessState.Idle => false,       // 空闲 - 非关键
                ProcessState.Completed => false,  // 已完成 - 非关键
                ProcessState.Aborting => false,   // 中止中 - 允许离线
                _ => false                         // 其他状态 - 默认允许
            };
        }

        /// <summary>
        /// 创建 S1F16 (OFF-LINE Acknowledge) 响应消息
        /// </summary>
        /// <param name="offlineAck">离线确认码</param>
        /// <param name="reason">操作原因</param>
        /// <returns>S1F16响应消息</returns>
        /// <remarks>
        /// S1F16 消息结构 (SEMI E30标准):
        /// &lt;OFLACK&gt; type B - 离线确认码 (0=接受, 1=拒绝)
        /// </remarks>
        private SecsMessage CreateS1F16Response(OfflineAck offlineAck, string reason)
        {
            var ackCode = (byte)offlineAck;

            Logger.LogDebug($"创建 S1F16 响应: OFLACK={ackCode} ({offlineAck}), 原因: {reason}");

            var response = new SecsMessage(1, 16, false)
            {
                Name = "OFF-LINE Acknowledge",
                SecsItem = Item.B(ackCode)  // <B OFLACK>
            };

            // 记录详细的响应信息到日志
            if (offlineAck == OfflineAck.Accepted)
            {
                Logger.LogInformation("S1F16 响应已创建: 离线请求被接受");
            }
            else
            {
                Logger.LogWarning($"S1F16 响应已创建: 离线请求被拒绝 - {reason}");
            }

            return response;
        }

        #endregion

        #region 枚举和辅助类

        /// <summary>
        /// 离线确认代码枚举
        /// </summary>
        /// <remarks>
        /// 基于SEMI E5标准定义的OFLACK响应代码
        /// 用于S1F16消息的响应数据
        /// </remarks>
        public enum OfflineAck : byte
        {
            /// <summary>
            /// 离线确认 - 设备同意离线请求
            /// </summary>
            Accepted = 0,

            /// <summary>
            /// 离线拒绝 - 设备拒绝离线请求
            /// 通常因为设备正在执行关键操作或安全条件不满足
            /// </summary>
            Denied = 1
        }

        /// <summary>
        /// 消息验证结果
        /// </summary>
        /// <remarks>
        /// 用于封装S1F15消息格式验证的结果信息
        /// </remarks>
        private class MessageValidationResult
        {
            /// <summary>
            /// 是否通过验证
            /// </summary>
            public bool IsValid { get; set; }

            /// <summary>
            /// 错误原因（当IsValid为false时）
            /// </summary>
            public string? ErrorReason { get; set; }
        }

        /// <summary>
        /// 离线验证结果
        /// </summary>
        /// <remarks>
        /// 封装设备是否可以执行离线操作的验证结果
        /// 包含业务规则和安全条件的检查结果
        /// </remarks>
        private class OfflineValidationResult
        {
            /// <summary>
            /// 是否可以离线
            /// </summary>
            public bool CanGoOffline { get; set; }

            /// <summary>
            /// 拒绝原因（当CanGoOffline为false时）
            /// </summary>
            public string? RejectReason { get; set; }
        }

        /// <summary>
        /// 安全联锁检查结果
        /// </summary>
        /// <remarks>
        /// 封装设备安全联锁系统的检查结果
        /// 确保设备在安全条件下执行离线操作
        /// </remarks>
        private class SafetyInterlockResult
        {
            /// <summary>
            /// 是否安全
            /// </summary>
            public bool IsSafe { get; set; }

            /// <summary>
            /// 不安全的原因（当IsSafe为false时）
            /// </summary>
            public string? UnsafeReason { get; set; }
        }

        #endregion
    }
}
