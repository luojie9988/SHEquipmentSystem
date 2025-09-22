// 文件路径: src/SHEquipmentSystem/Models/DTOs/AlidMappingDto.cs
// 描述: 完整的 ALID 映射数据传输对象定义

using System;
using System.ComponentModel.DataAnnotations;

namespace DiceEquipmentSystem.Models.DTOs
{
    /// <summary>
    /// ALID映射数据传输对象
    /// </summary>
    public class AlidMappingDto
    {
        /// <summary>
        /// 报警ID
        /// </summary>
        public uint AlidId { get; set; }

        /// <summary>
        /// 报警名称
        /// </summary>
        public string AlarmName { get; set; } = string.Empty;

        /// <summary>
        /// PLC触发地址
        /// </summary>
        public string? TriggerAddress { get; set; }

        /// <summary>
        /// 报警优先级（1-4）
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 优先级名称（只读）
        /// </summary>
        public string PriorityName { get; set; } = string.Empty;

        /// <summary>
        /// 报警分类（1-5）
        /// </summary>
        public int Category { get; set; }

        /// <summary>
        /// 分类名称（只读）
        /// </summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// 报警描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 处理建议
        /// </summary>
        public string? HandlingSuggestion { get; set; }

        /// <summary>
        /// 是否启用监控
        /// </summary>
        public bool IsMonitored { get; set; }

        /// <summary>
        /// 是否自动清除
        /// </summary>
        public bool AutoClear { get; set; }

        /// <summary>
        /// 报警代码（ALCD）
        /// </summary>
        public byte AlarmCode { get; set; }

        /// <summary>
        /// 报警文本模板
        /// </summary>
        public string? AlarmTextTemplate { get; set; }

        /// <summary>
        /// 最后触发时间
        /// </summary>
        public DateTime? LastTriggeredAt { get; set; }

        /// <summary>
        /// 格式化的最后触发时间
        /// </summary>
        public string FormattedLastTriggered { get; set; } = string.Empty;

        /// <summary>
        /// 触发次数
        /// </summary>
        public int TriggerCount { get; set; }

        /// <summary>
        /// 状态描述
        /// </summary>
        public string StatusDescription { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 扩展属性（JSON格式）
        /// </summary>
        public string? ExtendedProperties { get; set; }
    }

    /// <summary>
    /// 创建ALID映射数据传输对象
    /// </summary>
    public class CreateAlidMappingDto
    {
        /// <summary>
        /// 报警ID（必填，范围：12000-12095）
        /// </summary>
        [Required(ErrorMessage = "ALID ID是必填项")]
        [Range(12000, 12095, ErrorMessage = "ALID ID必须在12000-12095范围内")]
        public uint AlidId { get; set; }

        /// <summary>
        /// 报警名称（必填）
        /// </summary>
        [Required(ErrorMessage = "报警名称是必填项")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "报警名称长度必须在2到100个字符之间")]
        public string AlarmName { get; set; } = string.Empty;

        /// <summary>
        /// PLC触发地址（可选）
        /// </summary>
        [StringLength(50, ErrorMessage = "PLC地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确，应为：M100, D200, X1.0等格式")]
        public string? TriggerAddress { get; set; }

        /// <summary>
        /// 报警优先级（1=低，2=中，3=高，4=紧急）
        /// </summary>
        [Required(ErrorMessage = "报警优先级是必填项")]
        [Range(1, 4, ErrorMessage = "报警优先级必须在1-4之间")]
        public int Priority { get; set; } = 2;

        /// <summary>
        /// 报警分类（1=系统，2=硬件，3=工艺，4=安全，5=维护）
        /// </summary>
        [Required(ErrorMessage = "报警分类是必填项")]
        [Range(1, 5, ErrorMessage = "报警分类必须在1-5之间")]
        public int Category { get; set; } = 1;

        /// <summary>
        /// 报警描述
        /// </summary>
        [StringLength(500, ErrorMessage = "报警描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 处理建议
        /// </summary>
        [StringLength(1000, ErrorMessage = "处理建议长度不能超过1000个字符")]
        public string? HandlingSuggestion { get; set; }

        /// <summary>
        /// 是否启用监控（默认启用）
        /// </summary>
        public bool IsMonitored { get; set; } = true;

        /// <summary>
        /// 是否自动清除（默认手动清除）
        /// </summary>
        public bool AutoClear { get; set; } = false;

        /// <summary>
        /// 报警文本模板
        /// </summary>
        [StringLength(200, ErrorMessage = "报警文本模板长度不能超过200个字符")]
        public string? AlarmTextTemplate { get; set; }

        /// <summary>
        /// 扩展属性（JSON格式）
        /// </summary>
        [StringLength(2000, ErrorMessage = "扩展属性长度不能超过2000个字符")]
        public string? ExtendedProperties { get; set; }
    }

    /// <summary>
    /// 更新ALID映射数据传输对象
    /// </summary>
    public class UpdateAlidMappingDto
    {
        /// <summary>
        /// 报警名称（必填）
        /// </summary>
        [Required(ErrorMessage = "报警名称是必填项")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "报警名称长度必须在2到100个字符之间")]
        public string AlarmName { get; set; } = string.Empty;

        /// <summary>
        /// PLC触发地址（可选）
        /// </summary>
        [StringLength(50, ErrorMessage = "PLC地址长度不能超过50个字符")]
        [RegularExpression(@"^[DMXY]\d+(\.\d+)?$", ErrorMessage = "PLC地址格式不正确，应为：M100, D200, X1.0等格式")]
        public string? TriggerAddress { get; set; }

        /// <summary>
        /// 报警优先级（1=低，2=中，3=高，4=紧急）
        /// </summary>
        [Required(ErrorMessage = "报警优先级是必填项")]
        [Range(1, 4, ErrorMessage = "报警优先级必须在1-4之间")]
        public int Priority { get; set; }

        /// <summary>
        /// 报警分类（1=系统，2=硬件，3=工艺，4=安全，5=维护）
        /// </summary>
        [Required(ErrorMessage = "报警分类是必填项")]
        [Range(1, 5, ErrorMessage = "报警分类必须在1-5之间")]
        public int Category { get; set; }

        /// <summary>
        /// 报警描述
        /// </summary>
        [StringLength(500, ErrorMessage = "报警描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 处理建议
        /// </summary>
        [StringLength(1000, ErrorMessage = "处理建议长度不能超过1000个字符")]
        public string? HandlingSuggestion { get; set; }

        /// <summary>
        /// 是否启用监控
        /// </summary>
        public bool IsMonitored { get; set; }

        /// <summary>
        /// 是否自动清除
        /// </summary>
        public bool AutoClear { get; set; }

        /// <summary>
        /// 报警文本模板
        /// </summary>
        [StringLength(200, ErrorMessage = "报警文本模板长度不能超过200个字符")]
        public string? AlarmTextTemplate { get; set; }

        /// <summary>
        /// 扩展属性（JSON格式）
        /// </summary>
        [StringLength(2000, ErrorMessage = "扩展属性长度不能超过2000个字符")]
        public string? ExtendedProperties { get; set; }
    }

    /// <summary>
    /// ALID映射查询参数DTO
    /// </summary>
    public class AlidMappingQueryDto
    {
        /// <summary>
        /// 页码（默认第1页）
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 每页大小（默认10条）
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// 搜索关键词
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// 按分类筛选
        /// </summary>
        public int? Category { get; set; }

        /// <summary>
        /// 按优先级筛选
        /// </summary>
        public int? Priority { get; set; }

        /// <summary>
        /// 按监控状态筛选
        /// </summary>
        public bool? IsMonitored { get; set; }

        /// <summary>
        /// 排序字段（默认按AlidId排序）
        /// </summary>
        public string SortField { get; set; } = "AlidId";

        /// <summary>
        /// 排序方向（asc/desc，默认升序）
        /// </summary>
        public string SortDirection { get; set; } = "asc";
    }

    /// <summary>
    /// ALID映射分页结果DTO
    /// </summary>
    public class AlidMappingPagedResultDto
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public IEnumerable<AlidMappingDto> Data { get; set; } = new List<AlidMappingDto>();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;
    }

    /// <summary>
    /// ALID统计信息DTO
    /// </summary>
    public class AlidStatisticsDto
    {
        /// <summary>
        /// 总ALID数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 监控中的ALID数量
        /// </summary>
        public int MonitoredCount { get; set; }

        /// <summary>
        /// 按分类统计
        /// </summary>
        public Dictionary<string, int> CategoryStatistics { get; set; } = new();

        /// <summary>
        /// 按优先级统计
        /// </summary>
        public Dictionary<string, int> PriorityStatistics { get; set; } = new();

        /// <summary>
        /// 最近24小时触发次数
        /// </summary>
        public int RecentTriggersCount { get; set; }

        /// <summary>
        /// 最常触发的ALID（前5个）
        /// </summary>
        public List<AlidTriggerStatsDto> TopTriggeredAlids { get; set; } = new();
    }

    /// <summary>
    /// ALID触发统计DTO
    /// </summary>
    public class AlidTriggerStatsDto
    {
        /// <summary>
        /// 报警ID
        /// </summary>
        public uint AlidId { get; set; }

        /// <summary>
        /// 报警名称
        /// </summary>
        public string AlarmName { get; set; } = string.Empty;

        /// <summary>
        /// 触发次数
        /// </summary>
        public int TriggerCount { get; set; }

        /// <summary>
        /// 最后触发时间
        /// </summary>
        public DateTime? LastTriggeredAt { get; set; }
    }

    /// <summary>
    /// 批量操作ALID监控状态DTO
    /// </summary>
    public class BatchUpdateAlidMonitoringDto
    {
        /// <summary>
        /// ALID列表
        /// </summary>
        [Required(ErrorMessage = "ALID列表不能为空")]
        [MinLength(1, ErrorMessage = "至少需要选择一个ALID")]
        public List<uint> AlidIds { get; set; } = new();

        /// <summary>
        /// 是否启用监控
        /// </summary>
        public bool IsMonitored { get; set; }

        /// <summary>
        /// 操作原因/备注
        /// </summary>
        [StringLength(200, ErrorMessage = "操作原因长度不能超过200个字符")]
        public string? Reason { get; set; }
    }

    /// <summary>
    /// ALID导入数据DTO
    /// </summary>
    public class ImportAlidMappingDto
    {
        /// <summary>
        /// ALID映射列表
        /// </summary>
        public List<CreateAlidMappingDto> AlidMappings { get; set; } = new();

        /// <summary>
        /// 是否覆盖已存在的映射
        /// </summary>
        public bool OverwriteExisting { get; set; } = false;

        /// <summary>
        /// 导入模式（add=仅添加新的, update=仅更新已存在的, merge=合并）
        /// </summary>
        public string ImportMode { get; set; } = "add";
    }

    /// <summary>
    /// ALID导入结果DTO
    /// </summary>
    public class ImportAlidResultDto
    {
        /// <summary>
        /// 成功导入的数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败的数量
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// 跳过的数量
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 错误详情
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 警告信息
        /// </summary>
        public List<string> Warnings { get; set; } = new();
    }
}