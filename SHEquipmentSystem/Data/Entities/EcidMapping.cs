// 文件路径: src/DiceEquipmentSystem/Data/Entities/EcidMapping.cs
using System;
using System.ComponentModel.DataAnnotations;
using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.PLC.Models;

namespace DiceEquipmentSystem.Data.Entities
{
    /// <summary>
    /// ECID映射实体
    /// </summary>
    public class EcidMapping : IAuditableEntity
    {
        public int Id { get; set; }

        [Required]
        public uint EcidId { get; set; }

        [Required]
        [StringLength(100)]
        public string ParameterName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string PlcAddress { get; set; } = string.Empty;

        [Required]
        public PlcDataType DataType { get; set; }

        [StringLength(100)]
        public string? DefaultValue { get; set; }

        [StringLength(100)]
        public string? MinValue { get; set; }

        [StringLength(100)]
        public string? MaxValue { get; set; }

        [StringLength(20)]
        public string? Units { get; set; }

        public bool IsReadOnly { get; set; } = false;

        [StringLength(500)]
        public string? Description { get; set; }

        // 审计字段
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}