﻿// 文件路径: SHEquipmentSystem/Services/Interfaces/IIdMappingService.cs
using DiceEquipmentSystem.Models.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiceEquipmentSystem.Models;
using System.Linq.Dynamic.Core;

namespace SHEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// ID映射配置服务接口
    /// </summary>
    public interface IIdMappingService
    {
        #region ALID映射服务接口

        /// <summary>
        /// 获取所有ALID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<AlidMappingDto>>> GetAllAlidMappingsAsync();

        /// <summary>
        /// 获取指定ALID映射
        /// </summary>
        Task<ApiResponse<AlidMappingDto>> GetAlidMappingAsync(uint alidId);

        /// <summary>
        /// 创建ALID映射
        /// </summary>
        Task<ApiResponse<AlidMappingDto>> CreateAlidMappingAsync(CreateAlidMappingDto dto);

        /// <summary>
        /// 更新ALID映射
        /// </summary>
        Task<ApiResponse<AlidMappingDto>> UpdateAlidMappingAsync(uint alidId, UpdateAlidMappingDto dto);

        /// <summary>
        /// 删除ALID映射
        /// </summary>
        Task<ApiResponse> DeleteAlidMappingAsync(uint alidId);

        /// <summary>
        /// 分页获取ALID映射
        /// </summary>
        Task<ApiResponse<PagedResult<AlidMappingDto>>> GetAlidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm = null);

        /// <summary>
        /// 根据分类获取ALID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<AlidMappingDto>>> GetAlidMappingsByCategoryAsync(int category);

        /// <summary>
        /// 批量更新ALID监控状态
        /// </summary>
        Task<ApiResponse> UpdateAlidMonitoringStatusAsync(List<uint> alidIds, bool isMonitored);

#endregion
        // SVID映射服务
        Task<ApiResponse<IEnumerable<SvidMappingDto>>> GetAllSvidMappingsAsync();
        Task<ApiResponse<SvidMappingDto>> GetSvidMappingAsync(uint svidId);
        Task<ApiResponse<SvidMappingDto>> CreateSvidMappingAsync(CreateSvidMappingDto dto);
        Task<ApiResponse<SvidMappingDto>> UpdateSvidMappingAsync(uint svidId, UpdateSvidMappingDto dto);
        Task<ApiResponse> DeleteSvidMappingAsync(uint svidId);
        Task<ApiResponse<PagedResult<SvidMappingDto>>> GetSvidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm = null);

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