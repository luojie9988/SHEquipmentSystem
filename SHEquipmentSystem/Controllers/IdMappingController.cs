// 文件路径: Controllers/IdMappingController.cs
using DiceEquipmentSystem.Data;
using DiceEquipmentSystem.Data.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Controllers
{
    /// <summary>
    /// ID映射配置控制器
    /// 提供SVID、ALID、CEID、RPTID的完整CRUD操作
    /// </summary>
    public class IdMappingController : Controller
    {
        private readonly IdMappingDbContext _context;
        private readonly ILogger<IdMappingController> _logger;

        public IdMappingController(IdMappingDbContext context, ILogger<IdMappingController> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region 视图页面

        /// <summary>
        /// ID映射管理主页面
        /// </summary>
        public IActionResult Index()
        {
            _logger.LogInformation("访问ID映射管理页面");
            return View();
        }

        #endregion

        #region SVID映射API

        /// <summary>
        /// 获取所有SVID映射
        /// </summary>
        [HttpGet]
        [Route("IdMapping/svids")]
        public async Task<IActionResult> GetAllSvidMappings()
        {
            try
            {
                var mappings = await _context.SvidMappings
                    .OrderBy(s => s.SvidId)
                    .ToListAsync();

                return Ok(new { success = true, data = mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取SVID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 获取指定SVID映射
        /// </summary>
        [HttpGet]
        [Route("IdMapping/svids/{svidId}")]
        public async Task<IActionResult> GetSvidMapping(uint svidId)
        {
            try
            {
                var mapping = await _context.SvidMappings
                    .FirstOrDefaultAsync(s => s.SvidId == svidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "SVID映射不存在" });

                return Ok(new { success = true, data = mapping });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取SVID映射失败: {SvidId}", svidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 创建SVID映射
        /// </summary>
        [HttpPost]
        [Route("IdMapping/svids")]
        public async Task<IActionResult> CreateSvidMapping([FromBody] SvidMapping mapping)
        {
            try
            {
                // 检查SVID是否已存在
                var exists = await _context.SvidMappings
                    .AnyAsync(s => s.SvidId == mapping.SvidId);

                if (exists)
                    return BadRequest(new { success = false, message = "SVID ID已存在" });

                mapping.CreatedAt = DateTime.Now;
                mapping.UpdatedAt = DateTime.Now;

                _context.SvidMappings.Add(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("创建SVID映射成功: {SvidId}", mapping.SvidId);
                return Ok(new { success = true, data = mapping, message = "SVID映射创建成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建SVID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 更新SVID映射
        /// </summary>
        [HttpPut]
        [Route("IdMapping/svids/{svidId}")]
        public async Task<IActionResult> UpdateSvidMapping(uint svidId, [FromBody] SvidMapping updatedMapping)
        {
            try
            {
                var mapping = await _context.SvidMappings
                    .FirstOrDefaultAsync(s => s.SvidId == svidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "SVID映射不存在" });
                if (updatedMapping==null)
                {
                    return NotFound();
                }
                // 更新字段
                mapping.SvidName = updatedMapping.SvidName;
                mapping.PlcAddress = updatedMapping.PlcAddress;
                mapping.DataType = updatedMapping.DataType;
                mapping.Description = updatedMapping.Description;
                mapping.Units = updatedMapping.Units;
                mapping.IsActive = updatedMapping.IsActive;
                mapping.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("更新SVID映射成功: {SvidId}", svidId);
                return Ok(new { success = true, data = mapping, message = "SVID映射更新成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新SVID映射失败: {SvidId}", svidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 删除SVID映射
        /// </summary>
        [HttpDelete]
        [Route("IdMapping/svids/{svidId}")]
        public async Task<IActionResult> DeleteSvidMapping(uint svidId)
        {
            try
            {
                var mapping = await _context.SvidMappings
                    .FirstOrDefaultAsync(s => s.SvidId == svidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "SVID映射不存在" });

                _context.SvidMappings.Remove(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("删除SVID映射成功: {SvidId}", svidId);
                return Ok(new { success = true, message = "SVID映射删除成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除SVID映射失败: {SvidId}", svidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region ALID映射API

        /// <summary>
        /// 获取所有ALID映射
        /// </summary>
        [HttpGet]
        [Route("IdMapping/alids")]
        public async Task<IActionResult> GetAllAlidMappings()
        {
            try
            {
                var mappings = await _context.AlidMappings
                    .OrderBy(a => a.AlidId)
                    .ToListAsync();

                return Ok(new { success = true, data = mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取ALID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 创建ALID映射
        /// </summary>
        [HttpPost]
        [Route("IdMapping/alids")]
        public async Task<IActionResult> CreateAlidMapping([FromBody] AlidMapping mapping)
        {
            try
            {
                // 检查ALID是否已存在
                var exists = await _context.AlidMappings
                    .AnyAsync(a => a.AlidId == mapping.AlidId);

                if (exists)
                    return BadRequest(new { success = false, message = "ALID ID已存在" });

                mapping.CreatedAt = DateTime.Now;
                mapping.UpdatedAt = DateTime.Now;

                _context.AlidMappings.Add(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("创建ALID映射成功: {AlidId}", mapping.AlidId);
                return Ok(new { success = true, data = mapping, message = "ALID映射创建成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建ALID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 更新ALID映射
        /// </summary>
        [HttpPut]
        [Route("IdMapping/alids/{alidId}")]
        public async Task<IActionResult> UpdateAlidMapping(uint alidId, [FromBody] AlidMapping updatedMapping)
        {
            try
            {
                var mapping = await _context.AlidMappings
                    .FirstOrDefaultAsync(a => a.AlidId == alidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "ALID映射不存在" });

                // 更新字段
                mapping.AlarmName = updatedMapping.AlarmName;
                mapping.TriggerAddress = updatedMapping.TriggerAddress;
                mapping.Priority = updatedMapping.Priority;
                mapping.Category = updatedMapping.Category;
                mapping.Description = updatedMapping.Description;
               // mapping.HandlingSuggestion = updatedMapping.HandlingSuggestion;
                mapping.IsMonitored = updatedMapping.IsMonitored;
                //mapping.AutoClear = updatedMapping.AutoClear;
               // mapping.AlarmTextTemplate = updatedMapping.AlarmTextTemplate;
                mapping.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("更新ALID映射成功: {AlidId}", alidId);
                return Ok(new { success = true, data = mapping, message = "ALID映射更新成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新ALID映射失败: {AlidId}", alidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 删除ALID映射
        /// </summary>
        [HttpDelete]
        [Route("IdMapping/alids/{alidId}")]
        public async Task<IActionResult> DeleteAlidMapping(uint alidId)
        {
            try
            {
                var mapping = await _context.AlidMappings
                    .FirstOrDefaultAsync(a => a.AlidId == alidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "ALID映射不存在" });

                _context.AlidMappings.Remove(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("删除ALID映射成功: {AlidId}", alidId);
                return Ok(new { success = true, message = "ALID映射删除成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除ALID映射失败: {AlidId}", alidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region CEID映射API

        /// <summary>
        /// 获取所有CEID映射
        /// </summary>
        [HttpGet]
        [Route("IdMapping/ceids")]
        public async Task<IActionResult> GetAllCeidMappings()
        {
            try
            {
                var mappings = await _context.CeidMappings
                    .OrderBy(c => c.CeidId)
                    .ToListAsync();

                return Ok(new { success = true, data = mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取CEID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 创建CEID映射
        /// </summary>
        [HttpPost]
        [Route("IdMapping/ceids")]
        public async Task<IActionResult> CreateCeidMapping([FromBody] CeidMapping mapping)
        {
            try
            {
                // 检查CEID是否已存在
                var exists = await _context.CeidMappings
                    .AnyAsync(c => c.CeidId == mapping.CeidId);

                if (exists)
                    return BadRequest(new { success = false, message = "CEID ID已存在" });

                mapping.CreatedAt = DateTime.Now;
                mapping.UpdatedAt = DateTime.Now;

                _context.CeidMappings.Add(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("创建CEID映射成功: {CeidId}", mapping.CeidId);
                return Ok(new { success = true, data = mapping, message = "CEID映射创建成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建CEID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 更新CEID映射
        /// </summary>
        [HttpPut]
        [Route("IdMapping/ceids/{ceidId}")]
        public async Task<IActionResult> UpdateCeidMapping(uint ceidId, [FromBody] CeidMapping updatedMapping)
        {
            try
            {
                var mapping = await _context.CeidMappings
                    .FirstOrDefaultAsync(c => c.CeidId == ceidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "CEID映射不存在" });

                // 更新字段
                mapping.EventName = updatedMapping.EventName;
                mapping.TriggerAddress = updatedMapping.TriggerAddress;
                mapping.Description = updatedMapping.Description;
                mapping.IsEnabled = updatedMapping.IsEnabled;
                mapping.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("更新CEID映射成功: {CeidId}", ceidId);
                return Ok(new { success = true, data = mapping, message = "CEID映射更新成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新CEID映射失败: {CeidId}", ceidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 删除CEID映射
        /// </summary>
        [HttpDelete]
        [Route("IdMapping/ceids/{ceidId}")]
        public async Task<IActionResult> DeleteCeidMapping(uint ceidId)
        {
            try
            {
                var mapping = await _context.CeidMappings
                    .FirstOrDefaultAsync(c => c.CeidId == ceidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "CEID映射不存在" });

                _context.CeidMappings.Remove(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("删除CEID映射成功: {CeidId}", ceidId);
                return Ok(new { success = true, message = "CEID映射删除成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除CEID映射失败: {CeidId}", ceidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region RPTID映射API

        /// <summary>
        /// 获取所有RPTID映射
        /// </summary>
        [HttpGet]
        [Route("IdMapping/rptids")]
        public async Task<IActionResult> GetAllRptidMappings()
        {
            try
            {
                //var mappings = await _context.RptidMappings
                //    .Include(r => r.RptidId)
                //    //.ThenInclude(rs => rs.RptidMappingId)
                //    .OrderBy(r => r.RptidId)
                //    .ToListAsync();
                var mappings = await _context.RptidMappings.ToListAsync();
                return Ok(new { success = true, data = mappings });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取RPTID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 创建RPTID映射
        /// </summary>
        [HttpPost]
        [Route("IdMapping/rptids")]
        public async Task<IActionResult> CreateRptidMapping([FromBody] RptidMapping mapping)
        {
            try
            {
                // 检查RPTID是否已存在
                var exists = await _context.RptidMappings
                    .AnyAsync(r => r.RptidId == mapping.RptidId);

                if (exists)
                    return BadRequest(new { success = false, message = "RPTID ID已存在" });

                mapping.CreatedAt = DateTime.Now;
                mapping.UpdatedAt = DateTime.Now;

                _context.RptidMappings.Add(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("创建RPTID映射成功: {RptidId}", mapping.RptidId);
                return Ok(new { success = true, data = mapping, message = "RPTID映射创建成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建RPTID映射失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 更新RPTID映射
        /// </summary>
        [HttpPut]
        [Route("IdMapping/rptids/{rptidId}")]
        public async Task<IActionResult> UpdateRptidMapping(uint rptidId, [FromBody] RptidMapping updatedMapping)
        {
            try
            {
                var mapping = await _context.RptidMappings
                    .FirstOrDefaultAsync(r => r.RptidId == rptidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "RPTID映射不存在" });

                // 更新字段
                mapping.ReportName = updatedMapping.ReportName;
                mapping.Description = updatedMapping.Description;
                mapping.IsActive = updatedMapping.IsActive;
                mapping.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                _logger.LogInformation("更新RPTID映射成功: {RptidId}", rptidId);
                return Ok(new { success = true, data = mapping, message = "RPTID映射更新成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新RPTID映射失败: {RptidId}", rptidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 删除RPTID映射
        /// </summary>
        [HttpDelete]
        [Route("IdMapping/rptids/{rptidId}")]
        public async Task<IActionResult> DeleteRptidMapping(uint rptidId)
        {
            try
            {
                var mapping = await _context.RptidMappings
                    .FirstOrDefaultAsync(r => r.RptidId == rptidId);

                if (mapping == null)
                    return NotFound(new { success = false, message = "RPTID映射不存在" });

                _context.RptidMappings.Remove(mapping);
                await _context.SaveChangesAsync();

                _logger.LogInformation("删除RPTID映射成功: {RptidId}", rptidId);
                return Ok(new { success = true, message = "RPTID映射删除成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除RPTID映射失败: {RptidId}", rptidId);
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 验证PLC地址格式
        /// </summary>
        [HttpPost]
        [Route("IdMapping/validate-plc-address")]
        public IActionResult ValidatePlcAddress([FromBody] dynamic request)
        {
            try
            {
                string address = request.address;

                if (string.IsNullOrEmpty(address))
                    return Ok(new { success = false, message = "PLC地址不能为空" });

                // 简单的PLC地址格式验证（可根据实际需求调整）
                var isValid = System.Text.RegularExpressions.Regex.IsMatch(address, @"^[DMXY]\d+(\.\d+)?$");

                return Ok(new
                {
                    success = isValid,
                    message = isValid ? "PLC地址格式正确" : "PLC地址格式不正确，应为：D100、M10、X1.0等格式"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证PLC地址失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 导出配置
        /// </summary>
        [HttpGet]
        [Route("IdMapping/export")]
        public async Task<IActionResult> ExportConfiguration()
        {
            try
            {
                var config = new
                {
                    SvidMappings = await _context.SvidMappings.ToListAsync(),
                    AlidMappings = await _context.AlidMappings.ToListAsync(),
                    CeidMappings = await _context.CeidMappings.ToListAsync(),
                    RptidMappings = await _context.RptidMappings.ToListAsync(),
                    ExportTime = DateTime.Now
                };

                return Ok(new { success = true, data = config });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出配置失败");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        #endregion
    }
}