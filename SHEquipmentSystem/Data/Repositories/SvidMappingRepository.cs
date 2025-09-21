// 文件路径: src/DiceEquipmentSystem/Data/Repositories/SvidMappingRepository.cs
using DiceEquipmentSystem.Data.Entities;
using DiceEquipmentSystem.PLC.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SHEquipmentSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// SVID映射Repository实现
    /// </summary>
    public class SvidMappingRepository : Repository<SvidMapping>, ISvidMappingRepository
    {
        public SvidMappingRepository(IdMappingDbContext context, ILogger<SvidMappingRepository> logger)
            : base(context, logger)
        {
        }

        public async Task<SvidMapping?> GetBySvidIdAsync(uint svidId)
        {
            try
            {
                return await _dbSet.FirstOrDefaultAsync(x => x.SvidId == svidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SVID mapping by SVID ID {SvidId}", svidId);
                throw;
            }
        }

        public async Task<SvidMapping?> GetByPlcAddressAsync(string plcAddress)
        {
            try
            {
                return await _dbSet.FirstOrDefaultAsync(x => x.PlcAddress == plcAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SVID mapping by PLC address {PlcAddress}", plcAddress);
                throw;
            }
        }

        public async Task<IEnumerable<SvidMapping>> GetActiveAsync()
        {
            try
            {
                return await _dbSet.Where(x => x.IsActive).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active SVID mappings");
                throw;
            }
        }

        public async Task<IEnumerable<SvidMapping>> GetByDataTypeAsync(PlcDataType dataType)
        {
            try
            {
                return await _dbSet.Where(x => x.DataType == dataType).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SVID mappings by data type {DataType}", dataType);
                throw;
            }
        }

        public async Task<bool> IsSvidIdExistsAsync(uint svidId)
        {
            try
            {
                return await _dbSet.AnyAsync(x => x.SvidId == svidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking SVID ID exists {SvidId}", svidId);
                throw;
            }
        }

        public async Task<bool> IsPlcAddressInUseAsync(string plcAddress)
        {
            try
            {
                return await _dbSet.AnyAsync(x => x.PlcAddress == plcAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking PLC address in use {PlcAddress}", plcAddress);
                throw;
            }
        }

        public async Task<bool> IsPlcAddressInUseAsync(string plcAddress, uint excludeSvidId)
        {
            try
            {
                return await _dbSet.AnyAsync(x => x.PlcAddress == plcAddress && x.SvidId != excludeSvidId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking PLC address in use {PlcAddress} excluding {ExcludeSvidId}", plcAddress, excludeSvidId);
                throw;
            }
        }

        public async Task<IEnumerable<uint>> GetAvailableSvidIdsAsync()
        {
            try
            {
                var usedIds = await _dbSet.Select(x => x.SvidId).ToListAsync();
                var allIds = Enumerable.Range(10000, 100).Select(x => (uint)x);
                return allIds.Except(usedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available SVID IDs");
                throw;
            }
        }
    }
}