using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SHEquipmentSystem.Data.Entities
{
    /// <summary>
    /// RPTID-SVID关联映射实体
    /// </summary>
    public class RptidSvidMapping : IEntity
    {
        public int Id { get; set; }

        [Required]
        public int RptidMappingId { get; set; }

        [Required]
        public uint SvidId { get; set; }

        public int SortOrder { get; set; } = 0;


        // 导航属性
        public virtual RptidMapping RptidMapping { get; set; } = null!;
        public virtual SvidMapping SvidMapping { get; set; } = null!;
    }
}
