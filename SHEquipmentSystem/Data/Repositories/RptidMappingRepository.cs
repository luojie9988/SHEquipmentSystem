// 文件路径: src/DiceEquipmentSystem/Data/Repositories/RptidMappingRepository.cs
using DiceEquipmentSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SHEquipmentSystem.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// RPTID映射Repository实现
    /// </summary>
    public class RptidMappingRepository : Repository<RptidMapping>, IRptidMappingRepository
    {
        public RptidMappingRepository(IdMappingDbContext context, ILogger<RptidMappingRepository> logger)
            : base(context, logger)
        {
        }

        public async Task<RptidMapping?> GetByRptidIdAsync(uint rptidId)
        {
            try
            {
                return await _dbSet.FirstOrDefaultAsync(x => x.RptidId == rptidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RPTID mapping by RPTID {RptidId}", rptidId);
                throw;
            }
        }

        public async Task<RptidMapping?> GetByRptidIdWithSvidsAsync(uint rptidId)
        {
            try
            {
                return await _dbSet
                    .Include(x => x.RptidSvidMappings)
                    .ThenInclude(x => x.SvidMapping)
                    .FirstOrDefaultAsync(x => x.RptidId == rptidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RPTID mapping with SVIDs by RPTID {RptidId}", rptidId);
                throw;
            }
        }

        public async Task<IEnumerable<RptidMapping>> GetActiveAsync()
        {
            try
            {
                return await _dbSet.Where(x => x.IsActive).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active RPTID mappings");
                throw;
            }
        }

        public async Task<IEnumerable<RptidMapping>> GetActiveWithSvidsAsync()
        {
            try
            {
                return await _dbSet
                    .Include(x => x.RptidSvidMappings)
                    .ThenInclude(x => x.SvidMapping)
                    .Where(x => x.IsActive)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active RPTID mappings with SVIDs");
                throw;
            }
        }

        public async Task<bool> IsRptidIdExistsAsync(uint rptidId)
        {
            try
            {
                return await _dbSet.AnyAsync(x => x.RptidId == rptidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking RPTID exists {RptidId}", rptidId);
                throw;
            }
        }

        public async Task<bool> UpdateRptidSvidsAsync(uint rptidId, List<uint> svidIds)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var rptidMapping = await GetByRptidIdAsync(rptidId);
                if (rptidMapping == null)
                    return false;

                // 删除现有的SVID关联
                var existingMappings = await _context.RptidSvidMappings
                    .Where(x => x.RptidMappingId == rptidMapping.Id)
                    .ToListAsync();

                _context.RptidSvidMappings.RemoveRange(existingMappings);

                // 添加新的SVID关联
                var newMappings = svidIds.Select((svidId, index) => new RptidSvidMapping
                {
                    RptidMappingId = rptidMapping.Id,
                    SvidId = svidId,
                    SortOrder = index
                }).ToList();

                await _context.RptidSvidMappings.AddRangeAsync(newMappings);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating RPTID SVIDs for RPTID {RptidId}", rptidId);
                throw;
            }
        }

        public async Task<IEnumerable<uint>> GetRptidSvidsAsync(uint rptidId)
        {
            try
            {
                var rptidMapping = await GetByRptidIdAsync(rptidId);
                if (rptidMapping == null)
                    return Enumerable.Empty<uint>();

                return await _context.RptidSvidMappings
                    .Where(x => x.RptidMappingId == rptidMapping.Id)
                    .OrderBy(x => x.SortOrder)
                    .Select(x => x.SvidId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RPTID SVIDs for RPTID {RptidId}", rptidId);
                throw;
            }
        }
    }
}