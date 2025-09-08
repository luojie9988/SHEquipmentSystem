using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC连接管理器
    /// 负责管理与PLC的连接配置和状态
    /// </summary>
    public class PlcConnectionManager
    {
        #region 私有字段

        private readonly ILogger<PlcConnectionManager> _logger;
        private readonly IConfiguration _configuration;
        private PlcConfiguration _plcConfig;
        private readonly object _configLock = new();

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcConnectionManager(
            ILogger<PlcConnectionManager> logger,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            LoadConfiguration();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取PLC配置
        /// </summary>
        public PlcConfiguration GetPlcConfiguration()
        {
            lock (_configLock)
            {
                return _plcConfig;
            }
        }

        /// <summary>
        /// 更新PLC配置
        /// </summary>
        public void UpdateConfiguration(PlcConfiguration newConfig)
        {
            lock (_configLock)
            {
                _plcConfig = newConfig;
                _logger.LogInformation($"PLC配置已更新: {newConfig.IpAddress}:{newConfig.Port}");
            }
        }

        /// <summary>
        /// 验证连接参数
        /// </summary>
        public bool ValidateConfiguration(PlcConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.IpAddress))
            {
                _logger.LogWarning("PLC IP地址不能为空");
                return false;
            }

            if (config.Port <= 0 || config.Port > 65535)
            {
                _logger.LogWarning($"PLC端口号无效: {config.Port}");
                return false;
            }

            if (config.ConnectTimeout < 1000)
            {
                _logger.LogWarning($"连接超时时间太短: {config.ConnectTimeout}ms");
                return false;
            }

            return true;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfiguration()
        {
            _plcConfig = new PlcConfiguration
            {
                IpAddress = _configuration["PLC:IpAddress"] ?? "192.168.1.10",
                Port = _configuration.GetValue("PLC:Port", 6000),
                NetworkNumber = _configuration.GetValue<byte>("PLC:NetworkNumber", 0),
                StationNumber = _configuration.GetValue<byte>("PLC:StationNumber", 0),
                ConnectTimeout = _configuration.GetValue("PLC:ConnectTimeout", 5000),
                ReceiveTimeout = _configuration.GetValue("PLC:ReceiveTimeout", 3000),
                MaxRetryCount = _configuration.GetValue("PLC:MaxRetryCount", 3)
            };

            _logger.LogInformation($"PLC配置已加载: {_plcConfig.IpAddress}:{_plcConfig.Port}");
        }

        #endregion
    }

    /// <summary>
    /// PLC配置信息
    /// </summary>
    public class PlcConfiguration
    {
        /// <summary>IP地址</summary>
        public string IpAddress { get; set; } = "192.168.1.10";

        /// <summary>端口号</summary>
        public int Port { get; set; } = 6000;

        /// <summary>网络号</summary>
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>站号</summary>
        public byte StationNumber { get; set; } = 0;

        /// <summary>连接超时(毫秒)</summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>接收超时(毫秒)</summary>
        public int ReceiveTimeout { get; set; } = 3000;

        /// <summary>最大重试次数</summary>
        public int MaxRetryCount { get; set; } = 3;
    }
}
