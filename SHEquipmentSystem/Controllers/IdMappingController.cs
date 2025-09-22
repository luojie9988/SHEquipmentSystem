// 文件路径: src/DiceEquipmentSystem/Controllers/IdMappingController.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DiceEquipmentSystem.Models;
using DiceEquipmentSystem.Models.DTOs;
using SHEquipmentSystem.Services.Interfaces;

namespace DiceEquipmentSystem.Controllers
{
    /// <summary>
    /// ID映射配置控制器
    /// </summary>
    //[ApiController]
    //[Route("api/[controller]")]
    public class IdMappingController : Controller
    {
        private readonly IIdMappingService _idMappingService;
        private readonly ILogger<IdMappingController> _logger;

        public IdMappingController(IIdMappingService idMappingService, ILogger<IdMappingController> logger)
        {
           _idMappingService=idMappingService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            _logger.LogInformation("Index");
            return View();
        }
        #region SVID映射API

        /// <summary>
        /// 获取所有SVID映射
        /// </summary>
        [HttpGet("svids")]
        public async Task<ActionResult<ApiResponse<IEnumerable<SvidMappingDto>>>> GetAllSvidMappings()
        {
            var result = await _idMappingService.GetAllSvidMappingsAsync();
            return Ok(result);
        }

        /// <summary>
        /// 获取指定SVID映射
        /// </summary>
        [HttpGet("svids/{svidId}")]
        public async Task<ActionResult<ApiResponse<SvidMappingDto>>> GetSvidMapping(uint svidId)
        {
            var result = await _idMappingService.GetSvidMappingAsync(svidId);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// 创建SVID映射
        /// </summary>
        [HttpPost("svids")]
        public async Task<ActionResult<ApiResponse<SvidMappingDto>>> CreateSvidMapping([FromBody] CreateSvidMappingDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<SvidMappingDto>.CreateFailure("请求数据无效"));
            }

            var result = await _idMappingService.CreateSvidMappingAsync(dto);
            return result.Success ? CreatedAtAction(nameof(GetSvidMapping), new { svidId = dto.SvidId }, result) : BadRequest(result);
        }

        /// <summary>
        /// 更新SVID映射
        /// </summary>
        [HttpPut("svids/{svidId}")]
        public async Task<ActionResult<ApiResponse<SvidMappingDto>>> UpdateSvidMapping(uint svidId, [FromBody] UpdateSvidMappingDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<SvidMappingDto>.CreateFailure("请求数据无效"));
            }

            var result = await _idMappingService.UpdateSvidMappingAsync(svidId, dto);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 删除SVID映射
        /// </summary>
        [HttpDelete("svids/{svidId}")]
        public async Task<ActionResult<ApiResponse>> DeleteSvidMapping(uint svidId)
        {
            var result = await _idMappingService.DeleteSvidMappingAsync(svidId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 分页获取SVID映射
        /// </summary>
        [HttpGet("svids/paged")]
        public async Task<ActionResult<ApiResponse<object>>> GetSvidMappingsPaged(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? searchTerm = null)
        {
            var result = await _idMappingService.GetSvidMappingsPagedAsync(pageNumber, pageSize, searchTerm);
            return Ok(result);
        }

        #endregion

        #region CEID映射API

        /// <summary>
        /// 获取所有CEID映射
        /// </summary>
        [HttpGet("ceids")]
        public async Task<ActionResult<ApiResponse<IEnumerable<CeidMappingDto>>>> GetAllCeidMappings()
        {
            var result = await _idMappingService.GetAllCeidMappingsAsync();
            return Ok(result);
        }

        /// <summary>
        /// 创建CEID映射
        /// </summary>
        [HttpPost("ceids")]
        public async Task<ActionResult<ApiResponse<CeidMappingDto>>> CreateCeidMapping([FromBody] CreateCeidMappingDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<CeidMappingDto>.CreateFailure("请求数据无效"));
            }

            var result = await _idMappingService.CreateCeidMappingAsync(dto);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region RPTID映射API

        /// <summary>
        /// 获取所有RPTID映射
        /// </summary>
        [HttpGet("rptids")]
        public async Task<ActionResult<ApiResponse<IEnumerable<RptidMappingDto>>>> GetAllRptidMappings()
        {
            var result = await _idMappingService.GetAllRptidMappingsAsync();
            return Ok(result);
        }

        /// <summary>
        /// 创建RPTID映射
        /// </summary>
        [HttpPost("rptids")]
        public async Task<ActionResult<ApiResponse<RptidMappingDto>>> CreateRptidMapping([FromBody] CreateRptidMappingDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<RptidMappingDto>.CreateFailure("请求数据无效"));
            }

            var result = await _idMappingService.CreateRptidMappingAsync(dto);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 更新RPTID的SVID列表
        /// </summary>
        [HttpPut("rptids/{rptidId}/svids")]
        public async Task<ActionResult<ApiResponse>> UpdateRptidSvids(uint rptidId, [FromBody] List<uint> svidIds)
        {
            var result = await _idMappingService.UpdateRptidSvidsAsync(rptidId, svidIds);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region 工具API

        /// <summary>
        /// 验证PLC地址
        /// </summary>
        [HttpPost("validate/plc-address")]
        public async Task<ActionResult<ApiResponse<string>>> ValidatePlcAddress([FromBody] string plcAddress)
        {
            var result = await _idMappingService.ValidatePlcAddressAsync(plcAddress);
            return Ok(result);
        }

        /// <summary>
        /// 获取可用的SVID ID
        /// </summary>
        [HttpGet("svids/available-ids")]
        public async Task<ActionResult<ApiResponse<IEnumerable<uint>>>> GetAvailableSvidIds()
        {
            var result = await _idMappingService.GetAvailableSvidIdsAsync();
            return Ok(result);
        }

        /// <summary>
        /// 导出配置
        /// </summary>
        [HttpGet("export")]
        public async Task<ActionResult<ApiResponse<string>>> ExportMappings()
        {
            var result = await _idMappingService.ExportMappingsAsync();
            return Ok(result);
        }

        /// <summary>
        /// 导入配置
        /// </summary>
        [HttpPost("import")]
        public async Task<ActionResult<ApiResponse>> ImportMappings([FromBody] string jsonData)
        {
            var result = await _idMappingService.ImportMappingsAsync(jsonData);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion
    }
}