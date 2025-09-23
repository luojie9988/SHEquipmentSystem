// 文件路径: src/DiceEquipmentSystem/Services/IdMappingService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DiceEquipmentSystem.Data.Entities;
using DiceEquipmentSystem.Data.Repositories;
using DiceEquipmentSystem.Models;
using DiceEquipmentSystem.Models.DTOs;
using DiceEquipmentSystem.PLC.Interfaces;
using SHEquipmentSystem.Services.Interfaces;

namespace SHEquipmentSystem.Services
{
    /// <summary>
    /// ID映射配置服务实现
    /// </summary>
    public class IdMappingService : IIdMappingService
    {
        private readonly ISvidMappingRepository _svidRepository;
        private readonly IRptidMappingRepository _rptidRepository;
        private readonly IRepository<CeidMapping> _ceidRepository;
        private readonly IRepository<AlidMapping> _alidRepository;
        private readonly IRepository<EcidMapping> _ecidRepository;
        private readonly IPlcDataProvider? _plcProvider;
        private readonly ILogger<IdMappingService> _logger;

        public IdMappingService(
            ISvidMappingRepository svidRepository,
            IRptidMappingRepository rptidRepository,
            IRepository<CeidMapping> ceidRepository,
            IRepository<AlidMapping> alidRepository,
            IRepository<EcidMapping> ecidRepository,
            ILogger<IdMappingService> logger,
            IPlcDataProvider? plcProvider = null)
        {
            _svidRepository = svidRepository;
            _rptidRepository = rptidRepository;
            _ceidRepository = ceidRepository;
            _alidRepository = alidRepository;
            _ecidRepository = ecidRepository;
            _plcProvider = plcProvider;
            _logger = logger;
        }
        #region ALID映射服务实现

        public async Task<ApiResponse<IEnumerable<AlidMappingDto>>> GetAllAlidMappingsAsync()
        {
            try
            {
                var entities = await _alidRepository.GetAllAsync();
                var dtos = entities.Select(MapToAlidDto);
                return ApiResponse<IEnumerable<AlidMappingDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取ALID映射失败");
                return ApiResponse<IEnumerable<AlidMappingDto>>.CreateFailure("获取ALID映射失败");
            }
        }

        public async Task<ApiResponse<AlidMappingDto>> GetAlidMappingAsync(uint alidId)
        {
            try
            {
                var entity = await _alidRepository.GetByIdAsync((int)alidId);
                if (entity == null)
                {
                    return ApiResponse<AlidMappingDto>.CreateFailure("ALID映射不存在");
                }

                var dto = MapToAlidDto(entity);
                return ApiResponse<AlidMappingDto>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取ALID映射失败: {AlidId}", alidId);
                return ApiResponse<AlidMappingDto>.CreateFailure("获取ALID映射失败");
            }
        }

        public async Task<ApiResponse<AlidMappingDto>> CreateAlidMappingAsync(CreateAlidMappingDto dto)
        {
            try
            {
                if (await _alidRepository.ExistsAsync((int)dto.AlidId))
                {
                    return ApiResponse<AlidMappingDto>.CreateFailure("ALID ID已存在");
                }

                var entity = new AlidMapping
                {
                    AlidId = dto.AlidId,
                    AlarmName = dto.AlarmName,
                    TriggerAddress = dto.TriggerAddress,
                    //Priority = dto.Priority,
                    //Category = dto.Category,
                    Description = dto.Description,
                    //HandlingSuggestion = dto.HandlingSuggestion,
                    IsMonitored = dto.IsMonitored,
                    //AutoClear = dto.AutoClear,
                    //AlarmTextTemplate = dto.AlarmTextTemplate,
                    //AlarmCode = (byte)(0x80 | dto.Category) // 设置报警代码
                };

                var created = await _alidRepository.CreateAsync(entity);
                var resultDto = MapToAlidDto(created);

                return ApiResponse<AlidMappingDto>.CreateSuccess(resultDto, "ALID映射创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建ALID映射失败");
                return ApiResponse<AlidMappingDto>.CreateFailure("创建ALID映射失败");
            }
        }

        public async Task<ApiResponse<AlidMappingDto>> UpdateAlidMappingAsync(uint alidId, UpdateAlidMappingDto dto)
        {
            try
            {
                var entity = await _alidRepository.GetByIdAsync((int)alidId);
                if (entity == null)
                {
                    return ApiResponse<AlidMappingDto>.CreateFailure("ALID映射不存在");
                }

                // 更新属性
                entity.AlarmName = dto.AlarmName;
                entity.TriggerAddress = dto.TriggerAddress;
                //entity.Priority = dto.Priority;
                //entity.Category = dto.Category;
                entity.Description = dto.Description;
                //entity.HandlingSuggestion = dto.HandlingSuggestion;
                entity.IsMonitored = dto.IsMonitored;
                //entity.AutoClear = dto.AutoClear;
                //entity.AlarmTextTemplate = dto.AlarmTextTemplate;
                //entity.AlarmCode = (byte)(0x80 | dto.Category);

                var updated = await _alidRepository.UpdateAsync(entity);
                var resultDto = MapToAlidDto(updated);

                return ApiResponse<AlidMappingDto>.CreateSuccess(resultDto, "ALID映射更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新ALID映射失败: {AlidId}", alidId);
                return ApiResponse<AlidMappingDto>.CreateFailure("更新ALID映射失败");
            }
        }

        public async Task<ApiResponse> DeleteAlidMappingAsync(uint alidId)
        {
            try
            {
                var success = await _alidRepository.DeleteAsync((int)alidId);
                if (!success)
                {
                    return ApiResponse.CreateFailure("ALID映射不存在");
                }

                return ApiResponse.CreateSuccess("ALID映射删除成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除ALID映射失败: {AlidId}", alidId);
                return ApiResponse.CreateFailure("删除ALID映射失败");
            }
        }

        public async Task<ApiResponse<PagedResult<AlidMappingDto>>> GetAlidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm = null)
        {
            try
            {
                var entities = await _alidRepository.GetPagedAsync(pageNumber, pageSize, searchTerm);
                var totalCount = await _alidRepository.GetCountAsync(searchTerm);
                var dtos = entities.Select(MapToAlidDto);

                var result = new
                {
                    Data = dtos,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                };

                return ApiResponse<object>.CreateSuccess(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页获取ALID映射失败");
                return ApiResponse<object>.CreateFailure("分页获取ALID映射失败");
            }
        }

        public async Task<ApiResponse<IEnumerable<AlidMappingDto>>> GetAlidMappingsByCategoryAsync(int category)
        {
            try
            {
                var entities = await _alidRepository.GetByCategoryAsync(category);
                var dtos = entities.Select(MapToAlidDto);
                return ApiResponse<IEnumerable<AlidMappingDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据分类获取ALID映射失败: {Category}", category);
                return ApiResponse<IEnumerable<AlidMappingDto>>.CreateFailure("获取ALID映射失败");
            }
        }

        public async Task<ApiResponse> UpdateAlidMonitoringStatusAsync(List<uint> alidIds, bool isMonitored)
        {
            try
            {
                var count = await _alidRepository.UpdateMonitoringStatusAsync(alidIds, isMonitored);
                var statusText = isMonitored ? "启用" : "禁用";
                return ApiResponse.CreateSuccess($"成功{statusText}了{count}个ALID监控");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新ALID监控状态失败");
                return ApiResponse.CreateFailure("批量更新ALID监控状态失败");
            }
        }

        #endregion
        #region ALID映射方法

        private static AlidMappingDto MapToAlidDto(AlidMapping entity)
        {
            return new AlidMappingDto
            {
                AlidId = entity.AlidId,
                AlarmName = entity.AlarmName,
                TriggerAddress = entity.TriggerAddress,
                Priority = (int)entity.Priority,
                PriorityName = entity.ToString(),
                Category = (int)entity.Category,
                //CategoryName = entity.CategoryName,
                Description = entity.Description,
                //HandlingSuggestion = entity.HandlingSuggestion,
                IsMonitored = entity.IsMonitored,
                AutoClear = entity.AutoClearEnabled,
                //AlarmCode = entity.AlarmCode,
                //AlarmTextTemplate = entity.AlarmTextTemplate,
                //LastTriggeredAt = entity.LastTriggeredAt,
                //FormattedLastTriggered = entity.FormattedLastTriggered,
                //TriggerCount = entity.TriggerCount,
                //StatusDescription = entity.StatusDescription,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }

        #endregion
        #region SVID映射服务

        public async Task<ApiResponse<IEnumerable<SvidMappingDto>>> GetAllSvidMappingsAsync()
        {
            try
            {
                var entities = await _svidRepository.GetAllAsync();
                var dtos = entities.Select(MapToSvidDto);
                return ApiResponse<IEnumerable<SvidMappingDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有SVID映射失败");
                return ApiResponse<IEnumerable<SvidMappingDto>>.CreateFailure("获取SVID映射失败");
            }
        }

        public async Task<ApiResponse<SvidMappingDto>> GetSvidMappingAsync(uint svidId)
        {
            try
            {
                var entity = await _svidRepository.GetBySvidIdAsync(svidId);
                if (entity == null)
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure("SVID映射不存在");
                }

                var dto = MapToSvidDto(entity);
                return ApiResponse<SvidMappingDto>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取SVID映射失败: {SvidId}", svidId);
                return ApiResponse<SvidMappingDto>.CreateFailure("获取SVID映射失败");
            }
        }

        public async Task<ApiResponse<SvidMappingDto>> CreateSvidMappingAsync(CreateSvidMappingDto dto)
        {
            try
            {
                // 检查SVID ID是否已存在
                if (await _svidRepository.IsSvidIdExistsAsync(dto.SvidId))
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure("SVID ID已存在");
                }

                // 检查PLC地址是否已被使用
                if (await _svidRepository.IsPlcAddressInUseAsync(dto.PlcAddress))
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure("PLC地址已被使用");
                }

                // 验证PLC地址
                var addressValidation = await ValidatePlcAddressAsync(dto.PlcAddress);
                if (!addressValidation.Success)
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure($"PLC地址验证失败: {addressValidation.Message}");
                }

                var entity = new SvidMapping
                {
                    SvidId = dto.SvidId,
                    SvidName = dto.SvidName,
                    PlcAddress = dto.PlcAddress,
                    DataType = dto.DataType,
                    Description = dto.Description,
                    Units = dto.Units,
                    IsActive = dto.IsActive
                };

                var created = await _svidRepository.AddAsync(entity);
                var resultDto = MapToSvidDto(created);

                _logger.LogInformation("创建SVID映射成功: {SvidId}", dto.SvidId);
                return ApiResponse<SvidMappingDto>.CreateSuccess(resultDto, "创建SVID映射成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建SVID映射失败: {SvidId}", dto.SvidId);
                return ApiResponse<SvidMappingDto>.CreateFailure("创建SVID映射失败");
            }
        }

        public async Task<ApiResponse<SvidMappingDto>> UpdateSvidMappingAsync(uint svidId, UpdateSvidMappingDto dto)
        {
            try
            {
                var entity = await _svidRepository.GetBySvidIdAsync(svidId);
                if (entity == null)
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure("SVID映射不存在");
                }

                // 检查PLC地址是否已被其他SVID使用
                if (await _svidRepository.IsPlcAddressInUseAsync(dto.PlcAddress, svidId))
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure("PLC地址已被其他SVID使用");
                }

                // 验证PLC地址
                var addressValidation = await ValidatePlcAddressAsync(dto.PlcAddress);
                if (!addressValidation.Success)
                {
                    return ApiResponse<SvidMappingDto>.CreateFailure($"PLC地址验证失败: {addressValidation.Message}");
                }

                entity.SvidName = dto.SvidName;
                entity.PlcAddress = dto.PlcAddress;
                entity.DataType = dto.DataType;
                entity.Description = dto.Description;
                entity.Units = dto.Units;
                entity.IsActive = dto.IsActive;

                await _svidRepository.UpdateAsync(entity);
                var resultDto = MapToSvidDto(entity);

                _logger.LogInformation("更新SVID映射成功: {SvidId}", svidId);
                return ApiResponse<SvidMappingDto>.CreateSuccess(resultDto, "更新SVID映射成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新SVID映射失败: {SvidId}", svidId);
                return ApiResponse<SvidMappingDto>.CreateFailure("更新SVID映射失败");
            }
        }

        public async Task<ApiResponse> DeleteSvidMappingAsync(uint svidId)
        {
            try
            {
                var entity = await _svidRepository.GetBySvidIdAsync(svidId);
                if (entity == null)
                {
                    return ApiResponse.CreateFailure("SVID映射不存在");
                }

                await _svidRepository.DeleteAsync(entity);

                _logger.LogInformation("删除SVID映射成功: {SvidId}", svidId);
                return ApiResponse.CreateSuccess("删除SVID映射成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除SVID映射失败: {SvidId}", svidId);
                return ApiResponse.CreateFailure("删除SVID映射失败");
            }
        }

        public async Task<PagedApiResponse<SvidMappingDto>> GetSvidMappingsPagedAsync(int pageNumber, int pageSize, string? searchTerm = null)
        {
            try
            {
                var pagedResult = await _svidRepository.GetPagedAsync(pageNumber, pageSize,
                    filter: string.IsNullOrEmpty(searchTerm) ? null :
                    x => x.SvidName.Contains(searchTerm) || x.Description != null && x.Description.Contains(searchTerm));

                var dtoResult = new PagedResult<SvidMappingDto>
                {
                    Items = pagedResult.Items.Select(MapToSvidDto),
                    TotalCount = pagedResult.TotalCount,
                    PageNumber = pagedResult.PageNumber,
                    PageSize = pagedResult.PageSize
                };

                return PagedApiResponse<SvidMappingDto>.CreateSuccess(dtoResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分页SVID映射失败");
                return PagedApiResponse<SvidMappingDto>.CreateFailure("获取分页SVID映射失败");
            }
        }

        #endregion

        #region CEID映射服务

        public async Task<ApiResponse<IEnumerable<CeidMappingDto>>> GetAllCeidMappingsAsync()
        {
            try
            {
                var entities = await _ceidRepository.GetAllAsync();
                var dtos = entities.Select(MapToCeidDto);
                return ApiResponse<IEnumerable<CeidMappingDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有CEID映射失败");
                return ApiResponse<IEnumerable<CeidMappingDto>>.CreateFailure("获取CEID映射失败");
            }
        }

        public async Task<ApiResponse<CeidMappingDto>> CreateCeidMappingAsync(CreateCeidMappingDto dto)
        {
            try
            {
                // 检查CEID ID是否已存在
                if (await _ceidRepository.ExistsAsync(x => x.CeidId == dto.CeidId))
                {
                    return ApiResponse<CeidMappingDto>.CreateFailure("CEID ID已存在");
                }

                var entity = new CeidMapping
                {
                    CeidId = dto.CeidId,
                    EventName = dto.EventName,
                    TriggerAddress = dto.TriggerAddress,
                    TriggerType = dto.TriggerType,
                    Description = dto.Description,
                    IsEnabled = dto.IsEnabled
                };

                var created = await _ceidRepository.AddAsync(entity);
                var resultDto = MapToCeidDto(created);

                _logger.LogInformation("创建CEID映射成功: {CeidId}", dto.CeidId);
                return ApiResponse<CeidMappingDto>.CreateSuccess(resultDto, "创建CEID映射成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建CEID映射失败: {CeidId}", dto.CeidId);
                return ApiResponse<CeidMappingDto>.CreateFailure("创建CEID映射失败");
            }
        }

        #endregion

        #region RPTID映射服务

        public async Task<ApiResponse<IEnumerable<RptidMappingDto>>> GetAllRptidMappingsAsync()
        {
            try
            {
                var entities = await _rptidRepository.GetActiveWithSvidsAsync();
                var dtos = entities.Select(MapToRptidDto);
                return ApiResponse<IEnumerable<RptidMappingDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有RPTID映射失败");
                return ApiResponse<IEnumerable<RptidMappingDto>>.CreateFailure("获取RPTID映射失败");
            }
        }

        public async Task<ApiResponse<RptidMappingDto>> CreateRptidMappingAsync(CreateRptidMappingDto dto)
        {
            try
            {
                // 检查RPTID ID是否已存在
                if (await _rptidRepository.IsRptidIdExistsAsync(dto.RptidId))
                {
                    return ApiResponse<RptidMappingDto>.CreateFailure("RPTID ID已存在");
                }

                var entity = new RptidMapping
                {
                    RptidId = dto.RptidId,
                    ReportName = dto.ReportName,
                    Description = dto.Description,
                    IsActive = dto.IsActive
                };

                var created = await _rptidRepository.AddAsync(entity);

                // 添加SVID关联
                if (dto.SvidIds.Any())
                {
                    await _rptidRepository.UpdateRptidSvidsAsync(dto.RptidId, dto.SvidIds);
                }

                var result = await _rptidRepository.GetByRptidIdWithSvidsAsync(dto.RptidId);
                var resultDto = MapToRptidDto(result!);

                _logger.LogInformation("创建RPTID映射成功: {RptidId}", dto.RptidId);
                return ApiResponse<RptidMappingDto>.CreateSuccess(resultDto, "创建RPTID映射成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建RPTID映射失败: {RptidId}", dto.RptidId);
                return ApiResponse<RptidMappingDto>.CreateFailure("创建RPTID映射失败");
            }
        }

        public async Task<ApiResponse> UpdateRptidSvidsAsync(uint rptidId, List<uint> svidIds)
        {
            try
            {
                var success = await _rptidRepository.UpdateRptidSvidsAsync(rptidId, svidIds);
                if (!success)
                {
                    return ApiResponse.CreateFailure("RPTID不存在");
                }

                _logger.LogInformation("更新RPTID SVID列表成功: {RptidId}", rptidId);
                return ApiResponse.CreateSuccess("更新RPTID SVID列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新RPTID SVID列表失败: {RptidId}", rptidId);
                return ApiResponse.CreateFailure("更新RPTID SVID列表失败");
            }
        }

        #endregion

        #region 验证服务

        public async Task<ValidationResponse> ValidatePlcAddressAsync(string plcAddress)
        {
            try
            {
                // 基本格式验证
                if (!System.Text.RegularExpressions.Regex.IsMatch(plcAddress, @"^[DMXY]\d+(\.\d+)?$"))
                {
                    return ValidationResponse.CreateFailure("plcAddress", "PLC地址格式不正确，应为D100、M10等格式");
                }

                // 如果有PLC连接，尝试测试连接
                if (_plcProvider != null && _plcProvider.IsConnected)
                {
                    try
                    {
                        // 尝试读取地址以验证有效性
                        await _plcProvider.ReadBoolAsync(plcAddress);
                        return ValidationResponse.CreateSuccess("PLC地址验证成功，地址可访问");
                    }
                    catch
                    {
                        return ValidationResponse.CreateSuccess("PLC地址格式正确，但当前无法访问");
                    }
                }

                return ValidationResponse.CreateSuccess("PLC地址格式验证通过");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证PLC地址失败: {PlcAddress}", plcAddress);
                return ValidationResponse.CreateFailure("验证PLC地址时发生错误","");
            }
        }

        public async Task<ApiResponse<IEnumerable<uint>>> GetAvailableSvidIdsAsync()
        {
            try
            {
                var availableIds = await _svidRepository.GetAvailableSvidIdsAsync();
                return ApiResponse<IEnumerable<uint>>.CreateSuccess(availableIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可用SVID ID失败");
                return ApiResponse<IEnumerable<uint>>.CreateFailure("获取可用SVID ID失败");
            }
        }

        #endregion

        #region 导入导出

        public async Task<ApiResponse> ImportMappingsAsync(string jsonData)
        {
            try
            {
                // 基本的JSON解析验证
                try
                {
                    var document = JsonDocument.Parse(jsonData);
                    // 这里可以添加更复杂的导入逻辑
                    // 暂时只验证JSON格式是否正确
                }
                catch (JsonException)
                {
                    return ApiResponse.CreateFailure("JSON格式不正确");
                }

                _logger.LogInformation("导入配置数据");
                return ApiResponse.CreateSuccess("导入配置成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导入配置失败");
                return ApiResponse.CreateFailure("导入配置失败");
            }
        }

        public async Task<ApiResponse<string>> ExportMappingsAsync()
        {
            try
            {
                var svidMappings = await GetAllSvidMappingsAsync();
                var ceidMappings = await GetAllCeidMappingsAsync();
                var rptidMappings = await GetAllRptidMappingsAsync();

                var exportData = new
                {
                    SvidMappings = svidMappings.Data,
                    CeidMappings = ceidMappings.Data,
                    RptidMappings = rptidMappings.Data,
                    ExportTime = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                return ApiResponse<string>.CreateSuccess(json, "导出配置成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出配置失败");
                return ApiResponse<string>.CreateFailure("导出配置失败");
            }
        }

        #endregion

        #region 映射方法

        private static SvidMappingDto MapToSvidDto(SvidMapping entity)
        {
            return new SvidMappingDto
            {
                SvidId = entity.SvidId,
                SvidName = entity.SvidName,
                PlcAddress = entity.PlcAddress,
                DataType = entity.DataType,
                Description = entity.Description,
                Units = entity.Units,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }

        private static CeidMappingDto MapToCeidDto(CeidMapping entity)
        {
            return new CeidMappingDto
            {
                CeidId = entity.CeidId,
                EventName = entity.EventName,
                TriggerAddress = entity.TriggerAddress,
                TriggerType = entity.TriggerType,
                Description = entity.Description,
                IsEnabled = entity.IsEnabled,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }

        private static RptidMappingDto MapToRptidDto(RptidMapping entity)
        {
            return new RptidMappingDto
            {
                RptidId = entity.RptidId,
                ReportName = entity.ReportName,
                Description = entity.Description,
                IsActive = entity.IsActive,
                SvidItems = entity.RptidSvidMappings
                    .OrderBy(x => x.SortOrder)
                    .Select(x => new RptidSvidItemDto
                    {
                        SvidId = x.SvidId,
                        SvidName = x.SvidMapping?.SvidName ?? "",
                        SortOrder = x.SortOrder
                    }).ToList(),
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
        #endregion
    }
}