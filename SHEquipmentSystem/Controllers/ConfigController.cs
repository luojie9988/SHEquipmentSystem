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
        // 在现有ConfigController中添加以下PLC配置相关的方法
        // 保持现有功能不变，仅添加新功能

        #region PLC配置管理API

        /// <summary>
        /// 获取PLC配置
        /// </summary>
        [HttpGet]
        public IActionResult GetPLCConfig()
        {
            try
            {
                // 从配置文件读取PLC配置
                var plcConfig = _configuration.GetSection("PLC").Get<PlcConfiguration>();

                // 如果没有配置，返回默认配置
                if (plcConfig == null)
                {
                    plcConfig = new PlcConfiguration
                    {
                        IpAddress = "192.168.1.10",
                        Port = 6000,
                        NetworkNumber = 0,
                        StationNumber = 0,
                        ConnectTimeout = 5000,
                        ReceiveTimeout = 3000,
                        PollInterval = 1000,
                        MaxRetryCount = 3,
                        ReconnectInterval = 5000,
                        EnableAutoReconnect = true
                    };
                }

                _logger.LogInformation("获取PLC配置成功");

                return Json(new
                {
                    success = true,
                    data = plcConfig,
                    message = "PLC配置获取成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC配置失败");
                return Json(new
                {
                    success = false,
                    message = "获取PLC配置失败: " + ex.Message
                });
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
                _logger.LogInformation("保存PLC配置");

                // 验证配置
                var validationResult = ValidatePlcConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        message = "配置验证失败",
                        errors = validationResult.Errors
                    });
                }

                // 保存配置到appsettings.json
                await SavePlcConfigurationToFile(config);

                _logger.LogInformation("PLC配置保存成功");

                return Json(new
                {
                    success = true,
                    message = "PLC配置保存成功",
                    data = new
                    {
                        saveTime = DateTime.Now,
                        config = config
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存PLC配置失败");
                return Json(new
                {
                    success = false,
                    message = "保存PLC配置失败: " + ex.Message
                });
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
                _logger.LogInformation($"测试PLC连接: {config.IpAddress}:{config.Port}");

                // 验证配置
                var validationResult = ValidatePlcConfiguration(config);
                if (!validationResult.IsValid)
                {
                    return Json(new
                    {
                        success = false,
                        message = "配置验证失败: " + string.Join(", ", validationResult.Errors)
                    });
                }

                var startTime = DateTime.Now;

                // 使用HslCommunication测试PLC连接
                using var plc = new HslCommunication.Profinet.Melsec.MelsecMcNet(config.IpAddress, config.Port);
                plc.NetworkNumber = (byte)config.NetworkNumber;
                plc.NetworkStationNumber = (byte)config.StationNumber;
                plc.ConnectTimeOut = config.ConnectTimeout;
                plc.ReceiveTimeOut = config.ReceiveTimeout;

                var connectResult = await Task.Run(() => plc.ConnectServer());
                var responseTime = (DateTime.Now - startTime).TotalMilliseconds;

                if (connectResult.IsSuccess)
                {
                    // 连接成功，尝试读取一个测试地址
                    var readResult = await Task.Run(() => plc.ReadBool("M100"));

                    plc.ConnectClose();

                    _logger.LogInformation($"PLC连接测试成功，响应时间: {responseTime}ms");

                    return Json(new
                    {
                        success = true,
                        message = "PLC连接测试成功",
                        data = new
                        {
                            responseTime = Math.Round(responseTime, 2),
                            testTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            details = new
                            {
                                ipAddress = config.IpAddress,
                                port = config.Port,
                                networkNumber = config.NetworkNumber,
                                stationNumber = config.StationNumber,
                                readTestSuccess = readResult.IsSuccess,
                                readTestMessage = readResult.IsSuccess ? "数据读取测试成功" : readResult.Message
                            }
                        }
                    });
                }
                else
                {
                    _logger.LogWarning($"PLC连接测试失败: {connectResult.Message}");

                    return Json(new
                    {
                        success = false,
                        message = $"PLC连接失败: {connectResult.Message}",
                        data = new
                        {
                            responseTime = Math.Round(responseTime, 2),
                            testTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            errorCode = connectResult.ErrorCode
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC连接测试异常");
                return Json(new
                {
                    success = false,
                    message = $"连接测试异常: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 获取PLC连接状态
        /// </summary>
        [HttpGet]
        public IActionResult GetPLCStatus()
        {
            try
            {
                // 这里应该从实际的PLC连接管理器获取状态
                // 目前返回模拟数据，实际使用时需要集成真实的PLC管理器

                var status = new
                {
                    success = true,
                    data = new
                    {
                        isConnected = false, // 实际应从PLC管理器获取
                        connectedTime = (DateTime?)null,
                        sentCount = 0,
                        receivedCount = 0,
                        errorCount = 0,
                        lastError = (string?)null,
                        connectionQuality = 0.0,
                        responseTime = 0.0
                    }
                };

                return Json(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PLC状态失败");
                return Json(new
                {
                    success = false,
                    message = "获取PLC状态失败: " + ex.Message
                });
            }
        }

        #endregion

        #region PLC配置私有方法

        /// <summary>
        /// 验证PLC配置
        /// </summary>
        private ValidationResult ValidatePlcConfiguration(PlcConfiguration config)
        {
            var result = new ValidationResult();

            if (config == null)
            {
                result.AddError("PLC配置不能为空");
                return result;
            }

            // 验证IP地址
            if (string.IsNullOrWhiteSpace(config.IpAddress))
            {
                result.AddError("IP地址不能为空");
            }
            else if (!System.Net.IPAddress.TryParse(config.IpAddress, out _))
            {
                result.AddError("IP地址格式无效");
            }

            // 验证端口
            if (config.Port <= 0 || config.Port > 65535)
                result.AddError("端口号必须在1-65535之间");

            // 验证网络号
            if (config.NetworkNumber < 0 || config.NetworkNumber > 255)
                result.AddError("网络号必须在0-255之间");

            // 验证站号
            if (config.StationNumber < 0 || config.StationNumber > 31)
                result.AddError("站号必须在0-31之间");

            // 验证超时参数
            if (config.ConnectTimeout < 1000 || config.ConnectTimeout > 30000)
                result.AddWarning("连接超时建议设置在1000-30000毫秒之间");

            if (config.ReceiveTimeout < 1000 || config.ReceiveTimeout > 20000)
                result.AddWarning("接收超时建议设置在1000-20000毫秒之间");

            if (config.PollInterval < 100 || config.PollInterval > 10000)
                result.AddWarning("轮询间隔建议设置在100-10000毫秒之间");

            // 验证重试参数
            if (config.MaxRetryCount < 1 || config.MaxRetryCount > 10)
                result.AddWarning("最大重试次数建议设置在1-10之间");

            if (config.ReconnectInterval < 1000 || config.ReconnectInterval > 60000)
                result.AddWarning("重连间隔建议设置在1000-60000毫秒之间");

            return result;
        }

        /// <summary>
        /// 将PLC配置保存到appsettings.json文件
        /// </summary>
        /// <param name="config">PLC配置对象</param>
        private async Task SavePlcConfigurationToFile(PlcConfiguration config)
        {
            var configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // 读取现有配置
            var json = await System.IO.File.ReadAllTextAsync(configFilePath);
            var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                            ?? new Dictionary<string, object>();

            // 更新PLC配置
            configDict["PLC"] = config;

            // 保存到文件
            var jsonString = JsonSerializer.Serialize(configDict, jsonOptions);
            await System.IO.File.WriteAllTextAsync(configFilePath, jsonString);

            _logger.LogInformation("PLC配置已保存到appsettings.json");
        }

        #endregion

        // 添加PlcConfiguration类的定义（如果在其他地方没有定义的话）
        #region PLC配置模型

        /// <summary>
        /// PLC配置模型
        /// </summary>
        public class PlcConfiguration
        {
            /// <summary>
            /// PLC IP地址
            /// </summary>
            public string IpAddress { get; set; } = "192.168.1.10";

            /// <summary>
            /// PLC端口
            /// </summary>
            public int Port { get; set; } = 6000;

            /// <summary>
            /// 网络号
            /// </summary>
            public int NetworkNumber { get; set; } = 0;

            /// <summary>
            /// 站号
            /// </summary>
            public int StationNumber { get; set; } = 0;

            /// <summary>
            /// 连接超时时间（毫秒）
            /// </summary>
            public int ConnectTimeout { get; set; } = 5000;

            /// <summary>
            /// 接收超时时间（毫秒）
            /// </summary>
            public int ReceiveTimeout { get; set; } = 3000;

            /// <summary>
            /// 轮询间隔（毫秒）
            /// </summary>
            public int PollInterval { get; set; } = 1000;

            /// <summary>
            /// 最大重试次数
            /// </summary>
            public int MaxRetryCount { get; set; } = 3;

            /// <summary>
            /// 重连间隔（毫秒）
            /// </summary>
            public int ReconnectInterval { get; set; } = 5000;

            /// <summary>
            /// 是否启用自动重连
            /// </summary>
            public bool EnableAutoReconnect { get; set; } = true;
        }

        #endregion
    }


}