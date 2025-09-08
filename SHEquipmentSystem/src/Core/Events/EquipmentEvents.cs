// 文件路径: src/DiceEquipmentSystem/Core/Events/EquipmentEvents.cs
// 版本: v1.0.0
// 描述: 设备相关事件定义

using System;
using DiceEquipmentSystem.Core.Enums;

namespace DiceEquipmentSystem.Core.Events
{
    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public class ConnectionStateChangedEvent : EventBase
    {
        public HsmsConnectionState OldState { get; }
        public HsmsConnectionState NewState { get; }
        public string? Reason { get; }

        public ConnectionStateChangedEvent(
            HsmsConnectionState oldState,
            HsmsConnectionState newState,
            string? reason = null) : base("ConnectionManager")
        {
            OldState = oldState;
            NewState = newState;
            Reason = reason;
        }
    }

    /// <summary>
    /// 处理状态变更事件
    /// </summary>
    public class ProcessStateChangedEvent : EventBase
    {
        public ProcessState OldState { get; }
        public ProcessState NewState { get; }

        public ProcessStateChangedEvent(
            ProcessState oldState,
            ProcessState newState) : base("ProcessStateMachine")
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    /// <summary>
    /// 报警发生事件
    /// </summary>
    public class AlarmOccurredEvent : EventBase
    {
        public uint AlarmId { get; }
        public string AlarmText { get; }
        public AlarmSeverity Severity { get; }

        public AlarmOccurredEvent(
            uint alarmId,
            string alarmText,
            AlarmSeverity severity = AlarmSeverity.Warning) : base("AlarmService")
        {
            AlarmId = alarmId;
            AlarmText = alarmText;
            Severity = severity;
        }
    }

    /// <summary>
    /// 数据采集事件
    /// </summary>
    public class DataCollectedEvent : EventBase
    {
        public uint ReportId { get; }
        public Dictionary<uint, object> Data { get; }

        public DataCollectedEvent(uint reportId, Dictionary<uint, object> data)
            : base("DataCollectionService")
        {
            ReportId = reportId;
            Data = data;
        }
    }

    /// <summary>
    /// 报警严重程度
    /// </summary>
    public enum AlarmSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }
}
