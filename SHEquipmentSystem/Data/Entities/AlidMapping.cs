// 文件路径: src/DiceEquipmentSystem/Data/Entities/AlidMapping.cs
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using static DiceEquipmentSystem.Services.AlarmServiceImpl;

namespace DiceEquipmentSystem.Data.Entities
{
    /// <summary>
    /// ALID映射实体
    /// </summary>
    public class AlidMapping : IAuditableEntity
    {
        public int Id { get; set; }

        [Required]
        public uint AlidId { get; set; }

        [Required]
        [StringLength(100)]
        public string AlarmName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string PlcAddress { get; set; } = string.Empty;

        [Required]
        public AlarmPriority Priority { get; set; }

        [Required]
        public AlarmCategory Category { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsMonitored { get; set; } = true;

        public bool AutoClearEnabled { get; set; } = false;

        // 审计字段
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}