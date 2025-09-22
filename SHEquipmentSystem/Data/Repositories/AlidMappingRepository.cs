// 文件路径: src/SHEquipmentSystem/Data/Repositories/AlidMappingRepository.cs
// 描述: ALID映射仓储实现

using DiceEquipmentSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using static DiceEquipmentSystem.Services.AlarmServiceImpl;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// ALID映射仓储实现
    /// </summary>
    public class AlidMappingRepository : IAlidMappingRepository
    {
        private readonly IdMappingDbContext _context;

        public AlidMappingRepository(IdMappingDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IEnumerable<AlidMapping>> GetAllAsync()
        {
            return await _context.AlidMappings
                .OrderBy(x => x.AlidId)
                .ToListAsync();
        }

        public async Task<AlidMapping?> GetByIdAsync(uint alidId)
        {
            return await _context.AlidMappings
                .FirstOrDefaultAsync(x => x.AlidId == alidId);
        }

        public async Task<AlidMapping> CreateAsync(AlidMapping entity)
        {
            _context.AlidMappings.Add(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<AlidMapping> UpdateAsync(AlidMapping entity)
        {
            _context.AlidMappings.Update(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<bool> DeleteAsync(uint alidId)
        {
            var entity = await GetByIdAsync(alidId);
            if (entity == null) return false;

            _context.AlidMappings.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(uint alidId)
        {
            return await _context.AlidMappings
                .AnyAsync(x => x.AlidId == alidId);
        }

        public async Task<IEnumerable<AlidMapping>> GetByCategoryAsync(int category)
        {
            return await _context.AlidMappings
                .Where(x => x.Category == (AlarmCategory)category)
                .OrderBy(x => x.AlidId)
                .ToListAsync();
        }

        public async Task<IEnumerable<AlidMapping>> GetByPriorityAsync(int priority)
        {
            return await _context.AlidMappings
                .Where(x => x.Priority == (AlarmPriority)priority)
                .OrderBy(x => x.AlidId)
                .ToListAsync();
        }

        public async Task<IEnumerable<AlidMapping>> GetMonitoredAsync()
        {
            return await _context.AlidMappings
                .Where(x => x.IsMonitored)
                .OrderBy(x => x.AlidId)
                .ToListAsync();
        }

        public async Task<AlidMapping?> GetByTriggerAddressAsync(string triggerAddress)
        {
            if (string.IsNullOrWhiteSpace(triggerAddress))
                return null;

            return await _context.AlidMappings
                .FirstOrDefaultAsync(x => x.TriggerAddress == triggerAddress);
        }

        public async Task<IEnumerable<AlidMapping>> GetPagedAsync(int pageNumber, int pageSize, string? searchTerm = null)
        {
            var query = _context.AlidMappings.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(x =>
                    x.AlarmName.Contains(searchTerm) ||
                    (x.Description != null && x.Description.Contains(searchTerm)) ||
                    (x.TriggerAddress != null && x.TriggerAddress.Contains(searchTerm)));
            }

            return await query
                .OrderBy(x => x.AlidId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetCountAsync(string? searchTerm = null)
        {
            var query = _context.AlidMappings.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(x =>
                    x.AlarmName.Contains(searchTerm) ||
                    (x.Description != null && x.Description.Contains(searchTerm)) ||
                    (x.TriggerAddress != null && x.TriggerAddress.Contains(searchTerm)));
            }

            return await query.CountAsync();
        }

        public async Task<int> UpdateMonitoringStatusAsync(List<uint> alidIds, bool isMonitored)
        {
            var entities = await _context.AlidMappings
                .Where(x => alidIds.Contains(x.AlidId))
                .ToListAsync();

            foreach (var entity in entities)
            {
                entity.IsMonitored = isMonitored;
            }

            return await _context.SaveChangesAsync();
        }
    }
}