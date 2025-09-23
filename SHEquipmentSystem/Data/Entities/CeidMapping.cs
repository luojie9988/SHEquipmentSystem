// 文件路径: src/DiceEquipmentSystem/Data/Entities/CeidMapping.cs
using System;
using System.ComponentModel.DataAnnotations;
using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.Core.Enums;

namespace DiceEquipmentSystem.Data.Entities
{
    /// <summary>
    /// CEID映射实体 事件
    /// </summary>
    public class CeidMapping : IAuditableEntity
    {
        public int Id { get; set; }

        [Required]
        public uint CeidId { get; set; }

        [Required]
        [StringLength(100)]
        public string EventName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? TriggerAddress { get; set; }

        [Required]
        public TriggerType TriggerType { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsEnabled { get; set; } = true;

        // 审计字段
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}