// 文件路径: src/DiceEquipmentSystem/Core/Interfaces/IEntity.cs
using System;

namespace DiceEquipmentSystem.Core.Interfaces
{
    /// <summary>
    /// 基础实体接口
    /// </summary>
    public interface IEntity
    {
        int Id { get; set; }
    }

    /// <summary>
    /// 可审计实体接口
    /// </summary>
    public interface IAuditableEntity : IEntity
    {
        DateTime CreatedAt { get; set; }
        DateTime UpdatedAt { get; set; }
        string? CreatedBy { get; set; }
        string? UpdatedBy { get; set; }
    }
}