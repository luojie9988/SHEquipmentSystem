// 文件路径: src/DiceEquipmentSystem/Data/Repositories/CeidMappingRepository.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DiceEquipmentSystem.Data.Entities;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// CEID映射Repository接口
    /// </summary>
    public interface ICeidMappingRepository : IRepository<CeidMapping>
    {
        Task<CeidMapping?> GetByCeidIdAsync(uint ceidId);
        Task<IEnumerable<CeidMapping>> GetEnabledAsync();
        Task<bool> IsCeidIdExistsAsync(uint ceidId);
    }

    /// <summary>
    /// CEID映射Repository实现
    /// </summary>
    public class CeidMappingRepository : Repository<CeidMapping>, ICeidMappingRepository
    {
        public CeidMappingRepository(IdMappingDbContext context, ILogger<CeidMappingRepository> logger)
            : base(context, logger)
        {
        }

        public async Task<CeidMapping?> GetByCeidIdAsync(uint ceidId)
        {
            try
            {
                return await _dbSet.FirstOrDefaultAsync(x => x.CeidId == ceidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CEID mapping by CEID ID {CeidId}", ceidId);
                throw;
            }
        }

        public async Task<IEnumerable<CeidMapping>> GetEnabledAsync()
        {
            try
            {
                return await _dbSet.Where(x => x.IsEnabled).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting enabled CEID mappings");
                throw;
            }
        }

        public async Task<bool> IsCeidIdExistsAsync(uint ceidId)
        {
            try
            {
                return await _dbSet.AnyAsync(x => x.CeidId == ceidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking CEID ID exists {CeidId}", ceidId);
                throw;
            }
        }
    }
}