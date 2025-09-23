// 文件路径: src/SHEquipmentSystem/Services/Interfaces/IIdMappingService.cs
// 版本: v1.0.0
// 描述: ID映射服务接口 - 完整定义

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiceEquipmentSystem.Models;
using DiceEquipmentSystem.Models.DTOs;

namespace SHEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// ID映射服务接口
    /// 提供ALID、SVID、CEID、RPTID的映射管理功能
    /// </summary>
    public interface IIdMappingService : IDisposable
    {
        #region ALID映射方法

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
        Task<ApiResponse<object>> GetAlidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm);

        /// <summary>
        /// 根据分类获取ALID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<AlidMappingDto>>> GetAlidMappingsByCategoryAsync(int category);

        /// <summary>
        /// 批量更新ALID监控状态
        /// </summary>
        Task<ApiResponse> UpdateAlidMonitoringStatusAsync(List<uint> alidIds, bool isMonitored);

        #endregion

        #region SVID映射方法

        /// <summary>
        /// 获取所有SVID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<SvidMappingDto>>> GetAllSvidMappingsAsync();

        /// <summary>
        /// 获取指定SVID映射
        /// </summary>
        Task<ApiResponse<SvidMappingDto>> GetSvidMappingAsync(uint svidId);

        /// <summary>
        /// 创建SVID映射
        /// </summary>
        Task<ApiResponse<SvidMappingDto>> CreateSvidMappingAsync(CreateSvidMappingDto dto);

        /// <summary>
        /// 更新SVID映射
        /// </summary>
        Task<ApiResponse<SvidMappingDto>> UpdateSvidMappingAsync(uint svidId, UpdateSvidMappingDto dto);

        /// <summary>
        /// 删除SVID映射
        /// </summary>
        Task<ApiResponse> DeleteSvidMappingAsync(uint svidId);

        /// <summary>
        /// 分页获取SVID映射
        /// </summary>
        Task<ApiResponse<object>> GetSvidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm);

        #endregion

        #region CEID映射方法

        /// <summary>
        /// 获取所有CEID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<CeidMappingDto>>> GetAllCeidMappingsAsync();

        /// <summary>
        /// 创建CEID映射
        /// </summary>
        Task<ApiResponse<CeidMappingDto>> CreateCeidMappingAsync(CreateCeidMappingDto dto);

        /// <summary>
        /// 更新CEID映射
        /// </summary>
        Task<ApiResponse<CeidMappingDto>> UpdateCeidMappingAsync(uint ceidId, UpdateCeidMappingDto dto);

        /// <summary>
        /// 删除CEID映射
        /// </summary>
        Task<ApiResponse> DeleteCeidMappingAsync(uint ceidId);

        #endregion

        #region RPTID映射方法

        /// <summary>
        /// 获取所有RPTID映射
        /// </summary>
        Task<ApiResponse<IEnumerable<RptidMappingDto>>> GetAllRptidMappingsAsync();

        /// <summary>
        /// 创建RPTID映射
        /// </summary>
        Task<ApiResponse<RptidMappingDto>> CreateRptidMappingAsync(CreateRptidMappingDto dto);

        /// <summary>
        /// 更新RPTID的SVID列表
        /// </summary>
        Task<ApiResponse> UpdateRptidSvidsAsync(uint rptidId, List<uint> svidIds);

        /// <summary>
        /// 删除RPTID映射
        /// </summary>
        Task<ApiResponse> DeleteRptidMappingAsync(uint rptidId);

        #endregion

        #region 工具方法

        /// <summary>
        /// 验证PLC地址
        /// </summary>
        Task<ApiResponse<string>> ValidatePlcAddressAsync(string plcAddress);

        /// <summary>
        /// 获取可用的SVID ID
        /// </summary>
        Task<ApiResponse<IEnumerable<uint>>> GetAvailableSvidIdsAsync();

        /// <summary>
        /// 导出配置
        /// </summary>
        Task<ApiResponse<string>> ExportMappingsAsync();

        /// <summary>
        /// 导入配置
        /// </summary>
        Task<ApiResponse> ImportMappingsAsync(string jsonData);

        #endregion
    }
}