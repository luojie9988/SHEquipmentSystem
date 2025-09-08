using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.PLC.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SHEquipmentSystem.Models;
using System.Text.Json;
using DiceEquipmentSystem.PLC.Services;
using SHEquipmentSystem.ViewModels;
namespace SHEquipmentSystem.Controllers
{
    public class ConfigController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IPlcDataProvider _plcProvider;
        private readonly ILogger<ConfigController> _logger;
        private readonly EquipmentSystemConfiguration _equipmentConfig;

        public ConfigController(
            IConfiguration configuration,
            IPlcDataProvider plcProvider,
            ILogger<ConfigController> logger,
            IOptions<EquipmentSystemConfiguration> equipmentOptions)
        {
            _configuration = configuration;
            _plcProvider = plcProvider;
            _logger = logger;
            _equipmentConfig = equipmentOptions.Value;
        }

        #region 页面Action

        /// <summary>
        /// 配置总览页面
        /// </summary>
        public IActionResult Index()
        {
            var viewModel = new ConfigOverviewViewModel
            {
                PLCStatus = _plcProvider?.IsConnected ?? false,
                EquipmentStatus = GetEquipmentStatus(),
                LastUpdateTime = DateTime.Now
            };

            return View(viewModel);
        }

        /// <summary>
        /// PLC配置页面
        /// </summary>
        public IActionResult PLCConfig()
        {
            var plcConfig = _configuration.GetSection("PLC").Get<PlcConfiguration>() ?? new PlcConfiguration();
            
            var viewModel = new PLCConfigViewModel
            {
                PLCConfig = plcConfig,
                IsConnected = _plcProvider?.IsConnected ?? false,
                ConnectionStatus = GetConnectionStatusText(),
                ValidationErrors = new List<string>()
            };

            return View(viewModel);
        }

        /// <summary>
        /// 设备配置页面
        /// </summary>
        public IActionResult EquipmentConfig()
        {
            var viewModel = new EquipmentConfigViewModel
            {
                EquipmentConfig = _equipmentConfig,
                SystemStatus = GetSystemStatusDictionary(),
                ValidationErrors = new List<string>()
            };

            return View(viewModel);
        }

        #endregion

        #region API Action

        /// <summary>
        /// 获取PLC配置
        /// </summary>
        [HttpGet]
        public IActionResult GetPLCConfig()
        {
            try
            {
                var plcConfig = _configuration.GetSection("PLC").Get<PlcConfiguration>();
                return Json(plcConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC配置失败");
                return BadRequest(new { message = "获取PLC配置失败" });
            }
        }

        /// <summary>
        /// 保存PLC配置
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SavePLCConfig([FromBody] PlcConfiguration config)
        {
            try
            {
                // 验证配置
                var validationResult = ValidatePLCConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return BadRequest(new { message = "配置验证失败", errors = validationResult.Errors });
                }

                // 保存配置
                await SaveConfigurationAsync("PLC", config);
                
                _logger.LogInformation("PLC配置保存成功");
                return Ok(new { message = "PLC配置保存成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存PLC配置失败");
                return BadRequest(new { message = "保存PLC配置失败" });
            }
        }

        /// <summary>
        /// 测试PLC连接
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestPLCConnection([FromBody] PlcConfiguration config)
        {
            try
            {
                // 测试连接逻辑
                var isConnected = await TestConnectionAsync(config);
                
                return Ok(new 
                { 
                    connected = isConnected,
                    message = isConnected ? "连接成功" : "连接失败",
                    timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试PLC连接失败");
                return Ok(new 
                { 
                    connected = false, 
                    error = ex.Message,
                    timestamp = DateTime.Now
                });
            }
        }

        /// <summary>
        /// 获取设备配置
        /// </summary>
        [HttpGet]
        public IActionResult GetEquipmentConfig()
        {
            try
            {
                return Json(_equipmentConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备配置失败");
                return BadRequest(new { message = "获取设备配置失败" });
            }
        }

        /// <summary>
        /// 保存设备配置
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveEquipmentConfig([FromBody] EquipmentSystemConfiguration config)
        {
            try
            {
                // 验证配置
                var validationResult = ValidateEquipmentConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return BadRequest(new { message = "配置验证失败", errors = validationResult.Errors });
                }

                // 保存配置
                await SaveConfigurationAsync("EquipmentSystem", config);
                
                _logger.LogInformation("设备配置保存成功");
                return Ok(new { message = "设备配置保存成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设备配置失败");
                return BadRequest(new { message = "保存设备配置失败" });
            }
        }

        /// <summary>
        /// 验证配置
        /// </summary>
        [HttpPost]
        public IActionResult ValidateConfig([FromBody] EquipmentSystemConfiguration config)
        {
            try
            {
                var result = ValidateEquipmentConfiguration(config);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证配置失败");
                return BadRequest(new { message = "验证配置失败" });
            }
        }

        /// <summary>
        /// 导出配置
        /// </summary>
        [HttpGet]
        public IActionResult ExportConfig(string format = "json")
        {
            try
            {
                var config = new
                {
                    PLC = _configuration.GetSection("PLC").Get<PlcConfiguration>(),
                    EquipmentSystem = _equipmentConfig,
                    ExportTime = DateTime.Now
                };

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                var fileName = $"config_export_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出配置失败");
                return BadRequest(new { message = "导出配置失败" });
            }
        }

        #endregion

        #region 私有方法

        private string GetConnectionStatusText()
        {
            return _plcProvider?.IsConnected == true ? "已连接" : "未连接";
        }

        private string GetEquipmentStatus()
        {
            return "正常"; // 实际应用中从设备状态服务获取
        }

        private Dictionary<string, object> GetSystemStatusDictionary()
        {
            return new Dictionary<string, object>
            {
                ["PLCConnected"] = _plcProvider?.IsConnected ?? false,
                ["SystemTime"] = DateTime.Now,
                ["Version"] = _equipmentConfig.Equipment.SoftwareRevision,
                ["DeviceId"] = _equipmentConfig.Equipment.DeviceId
            };
        }

        private ValidationResult ValidatePLCConfiguration(PlcConfiguration config)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(config.IpAddress))
                result.Errors.Add("IP地址不能为空");
            
            if (config.Port <= 0 || config.Port > 65535)
                result.Errors.Add("端口号无效");
            
            if (config.ConnectionTimeout < 1000)
                result.Errors.Add("连接超时时间不能少于1000ms");

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        private ValidationResult ValidateEquipmentConfiguration(EquipmentSystemConfiguration config)
        {
            var result = new ValidationResult();

            if (config.Equipment.DeviceId <= 0)
                result.Errors.Add("设备ID必须大于0");
            
            if (string.IsNullOrWhiteSpace(config.Equipment.ModelName))
                result.Errors.Add("型号名称不能为空");
            
            if (config.Equipment.Port <= 0 || config.Equipment.Port > 65535)
                result.Errors.Add("设备端口号无效");

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        private async Task<bool> TestConnectionAsync(PlcConfiguration config)
        {
            // 模拟连接测试
            await Task.Delay(1000);
            return !string.IsNullOrEmpty(config.IpAddress) && config.Port > 0;
        }

        private async Task SaveConfigurationAsync(string section, object config)
        {
            // 实际实现中应该更新配置文件
            await Task.CompletedTask;
            _logger.LogInformation($"配置节 {section} 已保存");
        }

        #endregion
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}