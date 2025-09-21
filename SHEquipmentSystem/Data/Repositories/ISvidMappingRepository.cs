// 文件路径: src/DiceEquipmentSystem/Data/Repositories/ISvidMappingRepository.cs
using DiceEquipmentSystem.Data.Entities;
using SHEquipmentSystem.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// SVID映射Repository接口
    /// </summary>
    public interface ISvidMappingRepository : IRepository<SvidMapping>
    {
        Task<SvidMapping?> GetBySvidIdAsync(uint svidId);
        Task<SvidMapping?> GetByPlcAddressAsync(string plcAddress);
        Task<IEnumerable<SvidMapping>> GetActiveAsync();
        Task<IEnumerable<SvidMapping>> GetByDataTypeAsync(PLC.Models.PlcDataType dataType);
        Task<bool> IsSvidIdExistsAsync(uint svidId);
        Task<bool> IsPlcAddressInUseAsync(string plcAddress);
        Task<bool> IsPlcAddressInUseAsync(string plcAddress, uint excludeSvidId);
        Task<IEnumerable<uint>> GetAvailableSvidIdsAsync();
    }
}