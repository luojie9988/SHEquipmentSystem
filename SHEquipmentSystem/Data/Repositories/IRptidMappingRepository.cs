// 文件路径: src/DiceEquipmentSystem/Data/Repositories/IRptidMappingRepository.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using DiceEquipmentSystem.Data.Entities;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// RPTID映射Repository接口
    /// </summary>
    public interface IRptidMappingRepository : IRepository<RptidMapping>
    {
        Task<RptidMapping?> GetByRptidIdAsync(uint rptidId);
        Task<RptidMapping?> GetByRptidIdWithSvidsAsync(uint rptidId);
        Task<IEnumerable<RptidMapping>> GetActiveAsync();
        Task<IEnumerable<RptidMapping>> GetActiveWithSvidsAsync();
        Task<bool> IsRptidIdExistsAsync(uint rptidId);
        Task<bool> UpdateRptidSvidsAsync(uint rptidId, List<uint> svidIds);
        Task<IEnumerable<uint>> GetRptidSvidsAsync(uint rptidId);
    }
}