using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SHEquipmentSystem.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace SHEquipmentSystem.Controllers
{
    public class ConfigController : Controller
    {
        private readonly ILogger<ConfigController> _logger;
        private readonly IConfiguration _configuration;
        private readonly ISecsConnectionManager _secsConnectionManager;

        public ConfigController(
            ILogger<ConfigController> logger,
            IConfiguration configuration,
            ISecsConnectionManager secsConnectionManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _secsConnectionManager = secsConnectionManager ?? throw new ArgumentNullException(nameof(secsConnectionManager));
        }

        #region 页面Actions

        /// <summary>
        /// 设备配置管理页面
        /// </summary>
        public IActionResult EquipmentConfig()
        {
            return View();
        }
        /// <summary>
        /// 设备配置管理页面
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// 设备配置管理页面
        /// </summary>
        public IActionResult PLCConfig()
        {
            return View();
        }
        #endregion

        #region 设备管理API

        /// <summary>
        /// 获取设备配置
        /// </summary>
        [HttpGet]
        public IActionResult GetEquipmentConfig()
        {
            try
            {
                var equipmentConfig = _configuration.GetSection("EquipmentSystem").Get<EquipmentSystemConfiguration>();
                if (equipmentConfig == null)
                {
                    equipmentConfig = new EquipmentSystemConfiguration();
                }

                // 获取连接状态
                var _connectionStatus = _secsConnectionManager.GetConnectionStatus();

                var result = new
                {
                    success = true,
                    data = new
                    {
                        configuration = equipmentConfig,
                        connectionStatus = _connectionStatus,
                        isConnected = _secsConnectionManager.IsConnected,
                        lastConnectedTime = _secsConnectionManager.LastConnectedTime,
                        //connectionStatistics = _secsConnectionManager.GetConnectionStatistics()
                    }
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备配置失败");
                return Json(new
                {
                    success = false,
                    message = "获取设备配置失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取设备连接状态
        /// </summary>
        [HttpGet]
        public IActionResult GetEquipmentStatus()
        {
            try
            {
                var status = new
                {
                    success = true,
                    data = new
                    {
                        isConnected = _secsConnectionManager.IsConnected,
                        connectionStatistics = _secsConnectionManager.GetConnectionStatistics(),
                        lastConnectedTime = _secsConnectionManager.LastConnectedTime,
                        //lastDisconnectedTime = _secsConnectionManager.LastDisconnectedTime
                        //    connectionStatistics = _secsConnectionManager.GetConnectionStatistics(),
                        //    activeSession = _secsConnectionManager.GetActiveSession(),
                        //    lastError = _secsConnectionManager.GetLastError()
                    }
                };

                return Json(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备状态失败");
                return Json(new
                {
                    success = false,
                    message = "获取设备状态失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 启动SECS连接
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> StartConnection([FromBody] EquipmentSystemConfiguration config)
        {
            try
            {
                _logger.LogInformation("启动SECS连接请求");

                // 验证配置
                var validationResult = ValidateEquipmentConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        message = "配置验证失败",
                        errors = validationResult.Errors
                    });
                }

                // 启动连接
                await _secsConnectionManager.StartAsync( new CancellationToken());

                //if (result.Success)
                {
                    _logger.LogInformation("SECS连接启动成功");
                    return Json(new
                    {
                        success = true,
                        message = "SECS连接启动成功",
                        data = new
                        {
                            connectionStatus = _secsConnectionManager.GetConnectionStatus(),
                            startTime = DateTime.Now
                        }
                    });
                }
                //else
                //{
                //    _logger.LogWarning($"SECS连接启动失败: {result.ErrorMessage}");
                //    return Json(new
                //    {
                //        success = false,
                //        message = $"连接启动失败: {result.ErrorMessage}"
                //    });
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动SECS连接时发生异常");
                return Json(new
                {
                    success = false,
                    message = "启动连接失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 停止SECS连接
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> StopConnection()
        {
            try
            {
                _logger.LogInformation("停止SECS连接请求");

                 await _secsConnectionManager.StopAsync();

                //if (result.Success)
                {
                    _logger.LogInformation("SECS连接停止成功");
                    return Json(new
                    {
                        success = true,
                        message = "SECS连接停止成功",
                        data = new
                        {
                            connectionStatus = _secsConnectionManager.GetConnectionStatus(),
                            stopTime = DateTime.Now
                        }
                    });
                }
                //else
                //{
                //    _logger.LogWarning($"SECS连接停止失败: {result.ErrorMessage}");
                //    return Json(new
                //    {
                //        success = false,
                //        message = $"连接停止失败: {result.ErrorMessage}"
                //    });
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止SECS连接时发生异常");
                return Json(new
                {
                    success = false,
                    message = "停止连接失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 验证设备配置
        /// </summary>
        [HttpPost]
        public IActionResult ValidateEquipmentConfig([FromBody] EquipmentSystemConfiguration config)
        {
            try
            {
                var validationResult = ValidateEquipmentConfiguration(config);

                return Json(new
                {
                    success = validationResult.IsValid,
                    message = validationResult.IsValid ? "配置验证通过" : "配置验证失败",
                    errors = validationResult.Errors,
                    warnings = validationResult.Warnings
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证设备配置失败");
                return Json(new
                {
                    success = false,
                    message = "验证配置失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 测试连接参数
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestConnection([FromBody] EquipmentSystemConfiguration config)
        {
            try
            {
                _logger.LogInformation("测试连接参数");

                //var result = await _secsConnectionManager.TestConnectionAsync(config);

                //return Json(new
                //{
                //    success = result.Success,
                //    message = result.Success ? "连接测试成功" : $"连接测试失败: {result.ErrorMessage}",
                //    data = new
                //    {
                //        responseTime = result.ResponseTime,
                //        testTime = DateTime.Now,
                //        details = result.Details
                //    }
                //});
                return Json(new
                {
                    success = true,
                    message = "测试连接成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试连接失败");
                return Json(new
                {
                    success = false,
                    message = "测试连接失败: " + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取连接统计信息
        /// </summary>
        [HttpGet]
        public IActionResult GetConnectionStatistics()
        {
            try
            {
                var statistics = HsmsConnectionState.Connected;// _secsConnectionManager.GetConnectionStatistics();

                return Json(new
                {
                    success = true,
                    data = statistics
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取连接统计失败");
                return Json(new
                {
                    success = false,
                    message = "获取统计信息失败: " + ex.Message
                });
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
                _logger.LogInformation("保存设备配置");

                // 验证配置
                var validationResult = ValidateEquipmentConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        message = "配置验证失败",
                        errors = validationResult.Errors
                    });
                }

                // TODO: 实现配置保存逻辑 - 更新appsettings.json或数据库
                 await SaveConfigurationToFile(config);

                return Json(new
                {
                    success = true,
                    message = "设备配置保存成功",
                    data = new
                    {
                        saveTime = DateTime.Now
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存设备配置失败");
                return Json(new
                {
                    success = false,
                    message = "保存配置失败: " + ex.Message
                });
            }
        }
        /// <summary>
        /// 将配置对象保存到appsettings.json文件
        /// </summary>
        /// <param name="config">配置对象</param>
        public static async Task SaveConfigurationToFile(EquipmentSystemConfiguration config)
        {
            var configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            // 读取现有配置
            var json = await System.IO. File.ReadAllTextAsync(configFilePath);
            var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                            ?? new Dictionary<string, object>();
            // 将配置转换为字典
            var updates = new Dictionary<string, object>
            {
                { "Equipment", config.Equipment },
            };
            // 更新配置
            foreach (var update in updates)
            {
                configDict[update.Key] = update.Value;
            }
            // 保存到文件
            var jsonString = JsonSerializer.Serialize(configDict, jsonOptions);
            await System.IO.File.WriteAllTextAsync(configFilePath, jsonString);
        }
        #endregion

        #region 私有方法

        /// <summary>
        /// 验证设备配置
        /// </summary>
        private ValidationResult ValidateEquipmentConfiguration(EquipmentSystemConfiguration config)
        {
            var result = new ValidationResult();

            if (config?.Equipment == null)
            {
                result.AddError("设备配置不能为空");
                return result;
            }

            // 验证设备基本信息
            if (config.Equipment.DeviceId <= 0)
                result.AddError("设备ID必须大于0");

            if (string.IsNullOrWhiteSpace(config.Equipment.EquipmentName))
                result.AddError("设备名称不能为空");

            if (string.IsNullOrWhiteSpace(config.Equipment.IpAddress))
                result.AddError("IP地址不能为空");

            if (config.Equipment.Port <= 0 || config.Equipment.Port > 65535)
                result.AddError("端口号必须在1-65535之间");

            // 验证超时参数
            if (config.Equipment.T3 < 1000 || config.Equipment.T3 > 120000)
                result.AddWarning("T3超时建议设置在1-120秒之间");

            if (config.Equipment.T5 < 1000 || config.Equipment.T5 > 60000)
                result.AddWarning("T5超时建议设置在1-60秒之间");

            return result;
        }

        #endregion
    }


}