// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IEquipmentStateService.cs
// 版本: v1.1.0
// 描述: 设备状态服务接口 - 完整版本

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Enums;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 设备状态服务接口
    /// 基于SEMI E30/E58标准的多维状态管理
    /// </summary>
    public interface IEquipmentStateService
    {
        #region 状态获取

        /// <summary>
        /// 获取当前控制状态
        /// </summary>
        /// <returns>控制状态</returns>
        Task<ControlState> GetControlStateAsync();

        /// <summary>
        /// 获取当前控制模式
        /// </summary>
        /// <returns>控制模式</returns>
        Task<ControlMode> GetControlModeAsync();

        /// <summary>
        /// 获取当前处理状态
        /// </summary>
        /// <returns>处理状态</returns>
        Task<ProcessState> GetProcessStateAsync();

        /// <summary>
        /// 获取当前设备状态
        /// </summary>
        /// <returns>设备状态</returns>
        Task<EquipmentState> GetEquipmentStateAsync();

        /// <summary>
        /// 获取完整的设备状态信息
        /// </summary>
        /// <returns>设备状态信息</returns>
        Task<EquipmentStatusInfo> GetStatusInfoAsync();

        #endregion

        #region 状态控制

        /// <summary>
        /// 请求在线
        /// </summary>
        /// <param name="remote">是否远程模式，true=Remote, false=Local</param>
        /// <returns>是否成功</returns>
        Task<bool> RequestOnlineAsync(bool remote = true);

        /// <summary>
        /// 请求离线
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> RequestOfflineAsync();

        /// <summary>
        /// 切换控制模式（Local/Remote）
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> SwitchControlModeAsync();

        /// <summary>
        /// 设置通信建立状态
        /// </summary>
        /// <param name="established">是否已建立</param>
        Task SetCommunicationEstablishedAsync(bool established);

        /// <summary>
        /// 检查是否可以建立通信
        /// </summary>
        /// <returns>是否可以建立</returns>
        Task<bool> CanEstablishCommunicationAsync();

        #endregion

        #region 处理控制

        /// <summary>
        /// 开始处理
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> StartProcessAsync();

        /// <summary>
        /// 暂停处理
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> PauseProcessAsync();

        /// <summary>
        /// 恢复处理
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> ResumeProcessAsync();

        /// <summary>
        /// 停止处理
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> StopProcessAsync();

        /// <summary>
        /// 中止处理
        /// </summary>
        /// <returns>是否成功</returns>
        //Task<bool> AbortProcessAsync();

        /// <summary>
        /// 重置处理状态
        /// </summary>
        /// <returns>是否成功</returns>
        /// <summary>
        /// 完成处理状态初始化
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> CompleteProcessInitializationAsync();

        Task<bool> ResetProcessAsync();

        #endregion

        #region 状态查询

        /// <summary>
        /// 设置当前配方
        /// </summary>
        Task SetCurrentRecipeAsync(string? recipeName);

        /// <summary>
        /// 设置当前材料ID
        /// </summary>
        Task SetCurrentMaterialIdAsync(string? materialId);

        /// <summary>
        /// 更新报警计数
        /// </summary>
        Task UpdateAlarmCountAsync(int count);

        /// <summary>
        /// 检查设备是否在线
        /// </summary>
        /// <returns>是否在线</returns>
        Task<bool> IsOnlineAsync();

        /// <summary>
        /// 检查设备是否处于远程模式
        /// </summary>
        /// <returns>是否远程模式</returns>
        Task<bool> IsRemoteModeAsync();

        /// <summary>
        /// 检查设备是否正在处理
        /// </summary>
        /// <returns>是否正在处理</returns>
        Task<bool> IsProcessingAsync();

        /// <summary>
        /// 检查设备是否空闲
        /// </summary>
        /// <returns>是否空闲</returns>
        Task<bool> IsIdleAsync();

        /// <summary>
        /// 获取状态历史记录
        /// </summary>
        /// <returns>状态历史记录列表</returns>
        IEnumerable<StateHistoryEntry> GetStateHistory();

        /// <summary>
        /// 清除状态历史
        /// </summary>
        void ClearStateHistory();

        /// <summary>
        /// 检查通信是否已建立
        /// </summary>
        /// <returns>true表示通信已建立</returns>
        Task<bool> IsCommunicationEstablishedAsync();

        /// <summary>
        /// 检查通信是否已启用
        /// </summary>
        /// <returns>true表示通信已启用</returns>
        Task<bool> IsCommunicationEnabledAsync();

        #endregion

        #region 事件

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event EventHandler<StateChangeEventArgs>? StateChanged;

        /// <summary>
        /// 控制模式变更事件
        /// </summary>
        event EventHandler<ControlModeChangeEventArgs>? ControlModeChanged;

        /// <summary>
        /// 处理状态变更事件
        /// </summary>
        event EventHandler<ProcessStateChangeEventArgs>? ProcessStateChanged;

        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// 状态变更事件参数
    /// </summary>
    public class StateChangeEventArgs : EventArgs
    {
        /// <summary>
        /// 状态类型（ControlState/ProcessState/EquipmentState）
        /// </summary>
        public string StateType { get; set; } = "";

        /// <summary>
        /// 旧值
        /// </summary>
        public object? OldValue { get; set; }

        /// <summary>
        /// 新值
        /// </summary>
        public object? NewValue { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 变更原因（可选）
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 控制模式变更事件参数
    /// </summary>
    public class ControlModeChangeEventArgs : EventArgs
    {
        /// <summary>
        /// 旧模式
        /// </summary>
        public ControlMode? OldMode { get; set; }

        /// <summary>
        /// 新模式
        /// </summary>
        public ControlMode NewMode { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 变更原因（可选）
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 处理状态变更事件参数
    /// </summary>
    public class ProcessStateChangeEventArgs : EventArgs
    {
        /// <summary>
        /// 旧状态
        /// </summary>
        public ProcessState OldState { get; set; }

        /// <summary>
        /// 新状态
        /// </summary>
        public ProcessState NewState { get; set; }

        /// <summary>
        /// 触发器（可选）
        /// </summary>
        public string? Trigger { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    #endregion

    #region 数据模型

    /// <summary>
    /// 设备状态信息
    /// </summary>
    public class EquipmentStatusInfo
    {
        /// <summary>
        /// 控制状态
        /// </summary>
        public ControlState ControlState { get; set; }

        /// <summary>
        /// 控制模式
        /// </summary>
        public ControlMode ControlMode { get; set; }

        /// <summary>
        /// 处理状态
        /// </summary>
        public ProcessState ProcessState { get; set; }

        /// <summary>
        /// 设备状态
        /// </summary>
        public EquipmentState EquipmentState { get; set; }

        /// <summary>
        /// 通信是否已建立
        /// </summary>
        public bool IsCommunicationEstablished { get; set; }

        /// <summary>
        /// 当前执行的配方名称
        /// </summary>
        public string? CurrentRecipe { get; set; }

        /// <summary>
        /// 当前处理的材料ID
        /// </summary>
        public string? CurrentMaterialId { get; set; }

        /// <summary>
        /// 设备是否可用（可以接受新任务）
        /// </summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>
        /// 当前报警数量
        /// </summary>
        public int AlarmCount { get; set; }

        /// <summary>
        /// 是否在线
        /// </summary>
        public bool IsOnline => ControlState == ControlState.OnlineLocal ||
                                ControlState == ControlState.OnlineRemote;

        /// <summary>
        /// 是否远程模式
        /// </summary>
        public bool IsRemote => ControlState == ControlState.OnlineRemote;

        /// <summary>
        /// 是否正在处理
        /// </summary>
        public bool IsProcessing => ProcessStateHelper.IsProcessing(ProcessState);

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        public override string ToString()
        {
            return $"Control:{ControlState}/{ControlMode}, Process:{ProcessState}, Equipment:{EquipmentState}, " +
                   $"Online:{IsOnline}, Remote:{IsRemote}, Processing:{IsProcessing}, Available:{IsAvailable}";
        }
    }

    /// <summary>
    /// 状态历史记录条目
    /// </summary>
    public class StateHistoryEntry
    {
        /// <summary>
        /// 序号
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 状态类型
        /// </summary>
        public string StateType { get; set; } = "";

        /// <summary>
        /// 旧值
        /// </summary>
        public string OldValue { get; set; } = "";

        /// <summary>
        /// 新值
        /// </summary>
        public string NewValue { get; set; } = "";

        /// <summary>
        /// 变更原因
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// 操作者（可选）
        /// </summary>
        public string? Operator { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 持续时间（毫秒）
        /// </summary>
        public long? DurationMs { get; set; }
    }

    #endregion
}
