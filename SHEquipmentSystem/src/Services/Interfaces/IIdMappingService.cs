// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IIdMappingService.cs
using DiceEquipmentSystem.Data.Repositories;
using DiceEquipmentSystem.Models;
using DiceEquipmentSystem.Models.DTOs;
using System.Collections.Generic;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// ID映射配置服务接口
    /// </summary>
    public interface IIdMappingService
    {
        // SVID映射服务
        Task<ApiResponse<IEnumerable<SvidMappingDto>>> GetAllSvidMappingsAsync();
        Task<ApiResponse<SvidMappingDto>> GetSvidMappingAsync(uint svidId);
        Task<ApiResponse<SvidMappingDto>> CreateSvidMappingAsync(CreateSvidMappingDto dto);
        Task<ApiResponse<SvidMappingDto>> UpdateSvidMappingAsync(uint svidId, UpdateSvidMappingDto dto);
        Task<ApiResponse> DeleteSvidMappingAsync(uint svidId);
        Task<ApiResponse<Data.Repositories.PagedResult<SvidMappingDto>>> GetSvidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm = null);

        // CEID映射服务
        Task<ApiResponse<IEnumerable<CeidMappingDto>>> GetAllCeidMappingsAsync();
        Task<ApiResponse<CeidMappingDto>> CreateCeidMappingAsync(CreateCeidMappingDto dto);

        // RPTID映射服务
        Task<ApiResponse<IEnumerable<RptidMappingDto>>> GetAllRptidMappingsAsync();
        Task<ApiResponse<RptidMappingDto>> CreateRptidMappingAsync(CreateRptidMappingDto dto);
        Task<ApiResponse> UpdateRptidSvidsAsync(uint rptidId, List<uint> svidIds);

        // 验证服务
        Task<ApiResponse<string>> ValidatePlcAddressAsync(string plcAddress);
        Task<ApiResponse<IEnumerable<uint>>> GetAvailableSvidIdsAsync();

        // 导入导出
        Task<ApiResponse> ImportMappingsAsync(string jsonData);
        Task<ApiResponse<string>> ExportMappingsAsync();
    }
}