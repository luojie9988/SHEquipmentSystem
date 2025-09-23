// 文件路径: src/DiceEquipmentSystem/Data/Entities/RptidMapping.cs
using DiceEquipmentSystem.Core.Interfaces;
using SHEquipmentSystem.Data.Entities;
using SHEquipmentSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Principal;

namespace DiceEquipmentSystem.Data.Entities
{
    /// <summary>
    /// RPTID映射实体
    /// </summary>
    public class RptidMapping : IAuditableEntity
    {
        public int Id { get; set; }

        [Required]
        public uint RptidId { get; set; }

        [Required]
        [StringLength(100)]
        public string ReportName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        // 导航属性
        public virtual ICollection<RptidSvidMapping> RptidSvidMappings { get; set; } = new List<RptidSvidMapping>();

        // 审计字段
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }

 
}