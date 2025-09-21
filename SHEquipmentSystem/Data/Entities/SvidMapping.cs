// 文件路径: src/DiceEquipmentSystem/Data/Entities/SvidMapping.cs
using System;
using System.ComponentModel.DataAnnotations;
using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.PLC.Models;

namespace DiceEquipmentSystem.Data.Entities
{
    /// <summary>
    /// SVID映射实体
    /// </summary>
    public class SvidMapping : IAuditableEntity
    {
        public int Id { get; set; }

        [Required]
        public uint SvidId { get; set; }

        [Required]
        [StringLength(100)]
        public string SvidName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string PlcAddress { get; set; } = string.Empty;

        [Required]
        public PlcDataType DataType { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(20)]
        public string? Units { get; set; }

        public bool IsActive { get; set; } = true;

        // 审计字段
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}