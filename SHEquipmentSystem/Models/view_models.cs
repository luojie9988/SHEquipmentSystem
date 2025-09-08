using DiceEquipmentSystem.Core.Configuration;

namespace SHEquipmentSystem.ViewModels
{
    /// <summary>
    /// 配置总览视图模型
    /// </summary>
    public class ConfigOverviewViewModel
    {
        public bool PLCStatus { get; set; }
        public string EquipmentStatus { get; set; } = "未知";
        public DateTime LastUpdateTime { get; set; }
    }

    /// <summary>
    /// PLC配置视图模型
    /// </summary>
    public class PLCConfigViewModel
    {
        public PlcConfiguration PLCConfig { get; set; } = new();
        public bool IsConnected { get; set; }
        public string ConnectionStatus { get; set; } = "未连接";
        public List<string> ValidationErrors { get; set; } = new();
    }

    /// <summary>
    /// 设备配置视图模型
    /// </summary>
    public class EquipmentConfigViewModel
    {
        public EquipmentSystemConfiguration EquipmentConfig { get; set; } = new();
        public Dictionary<string, object> SystemStatus { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
    }

    /// <summary>
    /// PLC配置类
    /// </summary>
    //public class PLCConfiguration
    //{
    //    public string IpAddress { get; set; } = "192.168.3.100";
    //    public int Port { get; set; } = 5007;
    //    public int NetworkNumber { get; set; } = 0;
    //    public int StationNumber { get; set; } = 0;
    //    public int ConnectTimeout { get; set; } = 5000;
    //    public int ReceiveTimeout { get; set; } = 3000;
    //    public int PollingInterval { get; set; } = 2000;
    //    public int MaxRetryCount { get; set; } = 3;
    //    public bool EnableAutoReconnect { get; set; } = true;
    //    public int ReconnectInterval { get; set; } = 5000;
    //    public List<DataBlock> DataBlocks { get; set; } = new();
    //}

    /// <summary>
    /// 数据块配置
    /// </summary>
    public class DataBlock
    {
        public string Name { get; set; } = string.Empty;
        public string StartAddress { get; set; } = string.Empty;
        public int Length { get; set; } = 100;
        public int UpdateInterval { get; set; } = 1000;
        public bool Editing { get; set; } = false;
    }

    /// <summary>
    /// SVID映射配置
    /// </summary>
    public class SvidMapping
    {
        public uint Svid { get; set; }
        public string PlcAddress { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DataType { get; set; } = "Int32";
        public bool Editing { get; set; } = false;
    }

    /// <summary>
    /// 默认报告配置
    /// </summary>
    public class DefaultReport
    {
        public uint ReportId { get; set; }
        public List<uint> VariableIds { get; set; } = new();
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 默认事件链接配置
    /// </summary>
    public class DefaultEventLink
    {
        public uint EventId { get; set; }
        public List<uint> ReportIds { get; set; } = new();
        public string Description { get; set; } = string.Empty;
    }
}