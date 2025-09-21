// 文件路径: src/DiceEquipmentSystem/Models/DTOs/RptidMappingDto.cs (更新)
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DiceEquipmentSystem.Models.DTOs
{
    public class RptidMappingDto
    {
        public uint RptidId { get; set; }
        public string ReportName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public List<RptidSvidItemDto> SvidItems { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RptidSvidItemDto
    {
        public uint SvidId { get; set; }
        public string SvidName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class CreateRptidMappingDto
    {
        [Required(ErrorMessage = "RPTID ID是必填项")]
        public uint RptidId { get; set; }

        [Required(ErrorMessage = "报告名称是必填项")]
        [StringLength(100, ErrorMessage = "报告名称长度不能超过100个字符")]
        public string ReportName { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public List<uint> SvidIds { get; set; } = new();
    }

    public class UpdateRptidMappingDto
    {
        [Required(ErrorMessage = "报告名称是必填项")]
        [StringLength(100, ErrorMessage = "报告名称长度不能超过100个字符")]
        public string ReportName { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        public bool IsActive { get; set; }

        public List<uint> SvidIds { get; set; } = new();
    }
}