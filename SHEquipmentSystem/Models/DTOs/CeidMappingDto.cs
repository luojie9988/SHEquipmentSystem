// 文件路径: src/DiceEquipmentSystem/Models/DTOs/CeidMappingDto.cs (更新)
using System;
using System.ComponentModel.DataAnnotations;
using DiceEquipmentSystem.Core.Enums;

namespace DiceEquipmentSystem.Models.DTOs
{
    public class CeidMappingDto
    {
        public uint CeidId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string? TriggerAddress { get; set; }
        public TriggerType TriggerType { get; set; }
        public string TriggerTypeName => TriggerType.ToString();
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CreateCeidMappingDto
    {
        [Required(ErrorMessage = "CEID ID是必填项")]
        [Range(11000, 11018, ErrorMessage = "CEID ID必须在11000-11018范围内")]
        public uint CeidId { get; set; }

        [Required(ErrorMessage = "事件名称是必填项")]
        [StringLength(100, ErrorMessage = "事件名称长度不能超过100个字符")]
        public string EventName { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "触发地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确")]
        public string? TriggerAddress { get; set; }

        [Required(ErrorMessage = "触发类型是必填项")]
        public TriggerType TriggerType { get; set; }

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        public bool IsEnabled { get; set; } = true;
    }

    public class UpdateCeidMappingDto
    {
        [Required(ErrorMessage = "事件名称是必填项")]
        [StringLength(100, ErrorMessage = "事件名称长度不能超过100个字符")]
        public string EventName { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "触发地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确")]
        public string? TriggerAddress { get; set; }

        [Required(ErrorMessage = "触发类型是必填项")]
        public TriggerType TriggerType { get; set; }

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        public bool IsEnabled { get; set; }
    }
}