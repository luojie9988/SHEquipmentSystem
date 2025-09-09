// 版本: v3.4.2
// 描述: SECS/GEM通信状态管理器 - 确保符合SEMI E30标准的状态转换

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DiceEquipmentSystem.Core.Managers
{
    /// <summary>
    /// SECS/GEM通信状态管理器
    /// 确保符合SEMI E30标准的状态转换
    /// </summary>
    /// <remarks>
    /// 根据SEMI E30标准，通信初始化必须严格按照以下顺序进行：
    /// 1. HSMS连接建立
    /// 2. HSMS Select完成
    /// 3. S1F13/S1F14通信建立（COMMACK=0）
    /// 4. S1F17/S1F18在线请求（ONLACK=0或2）
    /// 5. S2F33-S2F40等初始化功能
    /// 
    /// 只有在前面的步骤成功完成后，才能进行后续步骤
    /// </remarks>
    public class CommunicationStateManager
    {
        #region 枚举定义

        /// <summary>
        /// 通信阶段枚举
        /// 按照SEMI E30标准定义的通信建立流程
        /// </summary>
        public enum CommunicationPhase
        {
            /// <summary>
            /// 未连接 - 初始状态，无TCP/IP连接
            /// </summary>
            NotConnected = 0,

            /// <summary>
            /// HSMS已连接 - TCP/IP连接已建立，但未进行HSMS Select
            /// </summary>
            HsmsConnected = 1,

            /// <summary>
            /// HSMS已选择 - HSMS Select.req/rsp已完成
            /// </summary>
            HsmsSelected = 2,

            /// <summary>
            /// 通信已建立 - S1F13/S1F14成功（COMMACK=0）
            /// 此状态后才能处理S1F17
            /// </summary>
            Communicating = 3,

            /// <summary>
            /// 在线 - S1F17/S1F18成功（ONLACK=0或2）
            /// 此状态后才能处理S2Fxx初始化消息
            /// </summary>
            Online = 4,

            /// <summary>
            /// 初始化完成 - S2F33/S2F35/S2F37/S2F23等初始化功能完成
            /// 设备进入正常工作状态
            /// </summary>
            Initialized = 5
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 设备通信状态信息
        /// </summary>
        public class DeviceCommunicationState
        {
            /// <summary>
            /// 设备ID
            /// </summary>
            public int DeviceId { get; set; }

            /// <summary>
            /// 当前通信阶段
            /// </summary>
            public CommunicationPhase Phase { get; set; }

            /// <summary>
            /// 最后更新时间
            /// </summary>
            public DateTime LastUpdateTime { get; set; }

            /// <summary>
            /// 通信建立时间
            /// </summary>
            public DateTime? CommunicationEstablishedTime { get; set; }

            /// <summary>
            /// 在线时间
            /// </summary>
            public DateTime? OnlineTime { get; set; }

            /// <summary>
            /// 初始化完成时间
            /// </summary>
            public DateTime? InitializedTime { get; set; }

            /// <summary>
            /// 是否可以处理S1F17（需要通信已建立）
            /// </summary>
            public bool CanProcessS1F17 => Phase >= CommunicationPhase.Communicating;

            /// <summary>
            /// 是否可以处理S2F33等初始化消息（需要在线）
            /// </summary>
            public bool CanProcessS2F33 => Phase >= CommunicationPhase.Online;

            /// <summary>
            /// 是否可以处理S2F35（需要在线）
            /// </summary>
            public bool CanProcessS2F35 => Phase >= CommunicationPhase.Online;

            /// <summary>
            /// 是否可以处理S2F37（需要在线）
            /// </summary>
            public bool CanProcessS2F37 => Phase >= CommunicationPhase.Online;

            /// <summary>
            /// 是否可以处理S2F23（需要在线）
            /// </summary>
            public bool CanProcessS2F23 => Phase >= CommunicationPhase.Online;

            /// <summary>
            /// 是否可以处理正常操作消息（需要初始化完成）
            /// </summary>
            public bool CanProcessNormalOperations => Phase >= CommunicationPhase.Initialized;

            /// <summary>
            /// 获取状态描述
            /// </summary>
            public string GetStatusDescription()
            {
                return Phase switch
                {
                    CommunicationPhase.NotConnected => "未连接",
                    CommunicationPhase.HsmsConnected => "HSMS已连接（等待Select）",
                    CommunicationPhase.HsmsSelected => "HSMS已选择（等待S1F13）",
                    CommunicationPhase.Communicating => "通信已建立（等待S1F17）",
                    CommunicationPhase.Online => "在线（可执行初始化）",
                    CommunicationPhase.Initialized => "初始化完成（正常运行）",
                    _ => $"未知状态: {Phase}"
                };
            }
        }

        /// <summary>
        /// 状态转换验证结果
        /// </summary>
        public class StateTransitionResult
        {
            /// <summary>
            /// 是否允许转换
            /// </summary>
            public bool IsAllowed { get; set; }

            /// <summary>
            /// 拒绝原因
            /// </summary>
            public string Reason { get; set; } = string.Empty;

            /// <summary>
            /// 当前阶段
            /// </summary>
            public CommunicationPhase CurrentPhase { get; set; }

            /// <summary>
            /// 目标阶段
            /// </summary>
            public CommunicationPhase TargetPhase { get; set; }
        }

        #endregion

        #region 私有字段

        /// <summary>
        /// 设备状态字典（线程安全）
        /// </summary>
        private readonly ConcurrentDictionary<int, DeviceCommunicationState> _deviceStates = new();

        /// <summary>
        /// 状态变更事件处理器
        /// </summary>
        public event EventHandler<StateChangedEventArgs>? StateChanged;

        #endregion

        #region 公共方法 - 状态查询

        /// <summary>
        /// 检查是否可以处理指定的SECS消息
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="stream">消息流号</param>
        /// <param name="function">消息功能号</param>
        /// <returns>是否可以处理</returns>
        public bool CanProcessMessage(int deviceId, byte stream, byte function)
        {
            if (!_deviceStates.TryGetValue(deviceId, out var state))
            {
                // 设备未注册，默认不允许处理
                return false;
            }

            // 根据SEMI E30标准检查消息处理权限
            return (stream, function) switch
            {
                // S1F1总是可以处理（Are You There）
                (1, 1) => true,
                (1, 2) => true,
                
                // S1F13总是可以处理（建立通信）
                (1, 13) => true,
                (1, 14) => true,
                
                // S1F15/S1F16请求离线总是可以处理
                (1, 15) => true,
                (1, 16) => true,
                
                // S1F17需要通信已建立
                (1, 17) => state.CanProcessS1F17,
                (1, 18) => state.CanProcessS1F17,
                
                // S2Fxx初始化消息需要在线
                (2, 13) => state.CanProcessS2F33,  // Equipment Constant Request
                (2, 15) => state.CanProcessS2F33,  // New Equipment Constant
                (2, 17) => state.CanProcessS2F33,  // Date Time Request
                (2, 23) => state.CanProcessS2F23,  // Trace Initialize
                (2, 31) => state.CanProcessS2F33,  // Date Time Set
                (2, 33) => state.CanProcessS2F33,  // Define Report
                (2, 35) => state.CanProcessS2F35,  // Link Event Report
                (2, 37) => state.CanProcessS2F37,  // Enable/Disable Event
                (2, 41) => state.CanProcessNormalOperations,  // Host Command
                
                // S5Fxx报警消息需要通信建立
                (5, _) => state.Phase >= CommunicationPhase.Communicating,
                
                // S6Fxx数据采集消息需要在线
                (6, _) => state.Phase >= CommunicationPhase.Online,
                
                // S7Fxx过程程序管理需要在线
                (7, _) => state.Phase >= CommunicationPhase.Online,
                
                // S9Fxx错误消息总是可以处理
                (9, _) => true,
                
                // S10Fxx终端消息需要在线
                (10, _) => state.Phase >= CommunicationPhase.Online,
                
                // 其他消息需要在线
                _ => state.Phase >= CommunicationPhase.Online
            };
        }

        /// <summary>
        /// 获取设备的当前通信阶段
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>通信阶段，如果设备未注册返回NotConnected</returns>
        public CommunicationPhase GetPhase(int deviceId)
        {
            return _deviceStates.TryGetValue(deviceId, out var state) 
                ? state.Phase 
                : CommunicationPhase.NotConnected;
        }

        /// <summary>
        /// 获取设备的通信状态信息
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <returns>状态信息，如果设备未注册返回null</returns>
        public DeviceCommunicationState? GetState(int deviceId)
        {
            return _deviceStates.GetValueOrDefault(deviceId);
        }

        /// <summary>
        /// 获取所有设备的状态信息
        /// </summary>
        /// <returns>设备状态字典</returns>
        public Dictionary<int, DeviceCommunicationState> GetAllStates()
        {
            return new Dictionary<int, DeviceCommunicationState>(_deviceStates);
        }

        #endregion

        #region 公共方法 - 状态更新

        /// <summary>
        /// 更新设备的通信阶段
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="phase">目标阶段</param>
        /// <returns>状态转换结果</returns>
        public StateTransitionResult UpdatePhase(int deviceId, CommunicationPhase phase)
        {
            var currentState = _deviceStates.GetValueOrDefault(deviceId);
            var currentPhase = currentState?.Phase ?? CommunicationPhase.NotConnected;

            // 验证状态转换是否合法
            var validationResult = ValidateTransition(currentPhase, phase);
            if (!validationResult.IsAllowed)
            {
                return validationResult;
            }

            // 执行状态更新
            var newState = _deviceStates.AddOrUpdate(deviceId,
                new DeviceCommunicationState 
                { 
                    DeviceId = deviceId, 
                    Phase = phase, 
                    LastUpdateTime = DateTime.Now,
                    CommunicationEstablishedTime = phase >= CommunicationPhase.Communicating ? DateTime.Now : null,
                    OnlineTime = phase >= CommunicationPhase.Online ? DateTime.Now : null,
                    InitializedTime = phase >= CommunicationPhase.Initialized ? DateTime.Now : null
                },
                (key, existing) => 
                {
                    existing.Phase = phase;
                    existing.LastUpdateTime = DateTime.Now;
                    
                    // 更新时间戳
                    if (phase >= CommunicationPhase.Communicating && !existing.CommunicationEstablishedTime.HasValue)
                        existing.CommunicationEstablishedTime = DateTime.Now;
                    if (phase >= CommunicationPhase.Online && !existing.OnlineTime.HasValue)
                        existing.OnlineTime = DateTime.Now;
                    if (phase >= CommunicationPhase.Initialized && !existing.InitializedTime.HasValue)
                        existing.InitializedTime = DateTime.Now;
                    
                    return existing;
                });

            // 触发状态变更事件
            OnStateChanged(deviceId, currentPhase, phase);

            return new StateTransitionResult
            {
                IsAllowed = true,
                CurrentPhase = currentPhase,
                TargetPhase = phase
            };
        }

        /// <summary>
        /// 重置设备状态到未连接
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        public void ResetDevice(int deviceId)
        {
            if (_deviceStates.TryRemove(deviceId, out var removedState))
            {
                OnStateChanged(deviceId, removedState.Phase, CommunicationPhase.NotConnected);
            }
        }

        /// <summary>
        /// 清除所有设备状态
        /// </summary>
        public void ClearAll()
        {
            _deviceStates.Clear();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 验证状态转换是否合法
        /// </summary>
        /// <param name="currentPhase">当前阶段</param>
        /// <param name="targetPhase">目标阶段</param>
        /// <returns>验证结果</returns>
        private StateTransitionResult ValidateTransition(CommunicationPhase currentPhase, CommunicationPhase targetPhase)
        {
            // 允许向后退（断开连接等）
            if (targetPhase < currentPhase)
            {
                return new StateTransitionResult
                {
                    IsAllowed = true,
                    CurrentPhase = currentPhase,
                    TargetPhase = targetPhase
                };
            }

            // 不允许跳跃式前进（必须按顺序）
            if (targetPhase > currentPhase + 1)
            {
                return new StateTransitionResult
                {
                    IsAllowed = false,
                    Reason = $"不允许从{currentPhase}直接跳转到{targetPhase}，必须按顺序进行",
                    CurrentPhase = currentPhase,
                    TargetPhase = targetPhase
                };
            }

            // 验证特定转换的前置条件
            switch (targetPhase)
            {
                case CommunicationPhase.HsmsSelected:
                    if (currentPhase != CommunicationPhase.HsmsConnected)
                    {
                        return new StateTransitionResult
                        {
                            IsAllowed = false,
                            Reason = "必须先建立HSMS连接",
                            CurrentPhase = currentPhase,
                            TargetPhase = targetPhase
                        };
                    }
                    break;

                case CommunicationPhase.Communicating:
                    if (currentPhase != CommunicationPhase.HsmsSelected)
                    {
                        return new StateTransitionResult
                        {
                            IsAllowed = false,
                            Reason = "必须先完成HSMS Select",
                            CurrentPhase = currentPhase,
                            TargetPhase = targetPhase
                        };
                    }
                    break;

                case CommunicationPhase.Online:
                    if (currentPhase != CommunicationPhase.Communicating)
                    {
                        return new StateTransitionResult
                        {
                            IsAllowed = false,
                            Reason = "必须先建立通信（S1F13/S1F14）",
                            CurrentPhase = currentPhase,
                            TargetPhase = targetPhase
                        };
                    }
                    break;

                case CommunicationPhase.Initialized:
                    if (currentPhase != CommunicationPhase.Online)
                    {
                        return new StateTransitionResult
                        {
                            IsAllowed = false,
                            Reason = "必须先进入在线状态（S1F17/S1F18）",
                            CurrentPhase = currentPhase,
                            TargetPhase = targetPhase
                        };
                    }
                    break;
            }

            return new StateTransitionResult
            {
                IsAllowed = true,
                CurrentPhase = currentPhase,
                TargetPhase = targetPhase
            };
        }

        /// <summary>
        /// 触发状态变更事件
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="oldPhase">旧阶段</param>
        /// <param name="newPhase">新阶段</param>
        private void OnStateChanged(int deviceId, CommunicationPhase oldPhase, CommunicationPhase newPhase)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs
            {
                DeviceId = deviceId,
                OldPhase = oldPhase,
                NewPhase = newPhase,
                Timestamp = DateTime.Now
            });
        }

        #endregion

        #region 事件参数类

        /// <summary>
        /// 状态变更事件参数
        /// </summary>
        public class StateChangedEventArgs : EventArgs
        {
            /// <summary>
            /// 设备ID
            /// </summary>
            public int DeviceId { get; set; }

            /// <summary>
            /// 旧阶段
            /// </summary>
            public CommunicationPhase OldPhase { get; set; }

            /// <summary>
            /// 新阶段
            /// </summary>
            public CommunicationPhase NewPhase { get; set; }

            /// <summary>
            /// 时间戳
            /// </summary>
            public DateTime Timestamp { get; set; }
        }

        #endregion
    }
}
