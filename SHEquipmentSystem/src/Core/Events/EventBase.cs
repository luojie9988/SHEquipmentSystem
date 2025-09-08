// 文件路径: src/DiceEquipmentSystem/Core/Events/EventBase.cs
// 版本: v1.0.0
// 描述: 事件基类

using System;
using DiceEquipmentSystem.Core.Interfaces;

namespace DiceEquipmentSystem.Core.Events
{
    /// <summary>
    /// 事件基类
    /// </summary>
    public abstract class EventBase : IEvent
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        public Guid EventId { get; }

        /// <summary>
        /// 事件时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 事件源
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        protected EventBase(string? source = null)
        {
            EventId = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
            Source = source;
        }
    }
}
