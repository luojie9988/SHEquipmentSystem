// 文件路径: src/DiceEquipmentSystem/Models/DTOs/SvidMappingDto.cs
using System;
using System.ComponentModel.DataAnnotations;
using DiceEquipmentSystem.PLC.Models;

namespace DiceEquipmentSystem.Models.DTOs
{
    /// <summary>
    /// SVID映射查询DTO
    /// </summary>
    public class SvidMappingDto
    {
        public uint SvidId { get; set; }
        public string SvidName { get; set; } = string.Empty;
        public string PlcAddress { get; set; } = string.Empty;
        public PlcDataType DataType { get; set; }
        public string DataTypeName => DataType.ToString();
        public string? Description { get; set; }
        public string? Units { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// SVID映射创建DTO
    /// </summary>
    public class CreateSvidMappingDto
    {
        [Required(ErrorMessage = "SVID ID是必填项")]
        [Range(10000, 10099, ErrorMessage = "SVID ID必须在10000-10099范围内")]
        public uint SvidId { get; set; }

        [Required(ErrorMessage = "SVID名称是必填项")]
        [StringLength(100, ErrorMessage = "SVID名称长度不能超过100个字符")]
        public string SvidName { get; set; } = string.Empty;

        [Required(ErrorMessage = "PLC地址是必填项")]
        [StringLength(50, ErrorMessage = "PLC地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确")]
        public string PlcAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "数据类型是必填项")]
        public PlcDataType DataType { get; set; }

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        [StringLength(20, ErrorMessage = "单位长度不能超过20个字符")]
        public string? Units { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// SVID映射更新DTO
    /// </summary>
    public class UpdateSvidMappingDto
    {
        [Required(ErrorMessage = "SVID名称是必填项")]
        [StringLength(100, ErrorMessage = "SVID名称长度不能超过100个字符")]
        public string SvidName { get; set; } = string.Empty;

        [Required(ErrorMessage = "PLC地址是必填项")]
        [StringLength(50, ErrorMessage = "PLC地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确")]
        public string PlcAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "数据类型是必填项")]
        public PlcDataType DataType { get; set; }

        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        [StringLength(20, ErrorMessage = "单位长度不能超过20个字符")]
        public string? Units { get; set; }

        public bool IsActive { get; set; }
    }
}