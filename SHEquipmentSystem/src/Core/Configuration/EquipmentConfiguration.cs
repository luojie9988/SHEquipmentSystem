// 文件路径: src/DiceEquipmentSystem/Core/Configuration/EquipmentConfiguration.cs
// 版本: v1.0.0
// 描述: 设备配置模型

using System;
using System.Collections.Generic;

namespace DiceEquipmentSystem.Core.Configuration
{
    /// <summary>
    /// 设备系统配置
    /// </summary>
    public class EquipmentSystemConfiguration
    {
        /// <summary>
        /// 设备配置
        /// </summary>
        public EquipmentConfiguration Equipment { get; set; } = new();

        /// <summary>
        /// PLC配置
        /// </summary>
        public PlcConfiguration? Plc { get; set; }

        /// <summary>
        /// SVID映射配置
        /// </summary>
        public Dictionary<uint, string> SvidMapping { get; set; } = new();

        /// <summary>
        /// CEID映射配置
        /// </summary>
        public Dictionary<uint, string> CeidMapping { get; set; } = new();

        /// <summary>
        /// ALID映射配置
        /// </summary>
        public Dictionary<uint, string> AlidMapping { get; set; } = new();

        /// <summary>
        /// 日志配置
        /// </summary>
        public LogConfiguration Logging { get; set; } = new();

        /// <summary>
        /// 功能配置
        /// </summary>
        public FeatureConfiguration? Features { get; set; }

        /// <summary>
        /// 性能配置
        /// </summary>
        public PerformanceConfiguration Performance { get; set; } = new();

        /// <summary>
        /// 数据采集配置
        /// </summary>
        public DataCollectionConfiguration DataCollection { get; set; } = new();

        /// <summary>
        /// 事件报告配置
        /// </summary>
        public EventReportConfiguration EventReport { get; set; } = new();
    }

    /// <summary>
    /// 设备配置
    /// </summary>
    public class EquipmentConfiguration
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public ushort DeviceId { get; set; } = 1;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string EquipmentName { get; set; } = "Dicer01";

        /// <summary>
        /// 型号名称
        /// </summary>
        public string ModelName { get; set; } = "AIMFAB";

        /// <summary>
        /// 软件版本
        /// </summary>
        public string SoftwareRevision { get; set; } = "V01R01";

        /// <summary>
        /// Ip地址
        /// </summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>
        /// 监听端口（Passive模式）
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// 是否为Active模式
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// T3超时（毫秒）- Reply Timeout
        /// </summary>
        public int T3 { get; set; } = 45000;

        /// <summary>
        /// T5超时（毫秒）- Connect Separation Timeout
        /// </summary>
        public int T5 { get; set; } = 10000;

        /// <summary>
        /// T6超时（毫秒）- Control Timeout
        /// </summary>
        public int T6 { get; set; } = 5000;

        /// <summary>
        /// T7超时（毫秒）- Not Selected Timeout
        /// </summary>
        public int T7 { get; set; } = 10000;

        /// <summary>
        /// T8超时（毫秒）- Network Intercharacter Timeout
        /// </summary>
        public int T8 { get; set; } = 5000;

        /// <summary>
        /// LinkTest间隔（毫秒）
        /// </summary>
        public int LinkTestInterval { get; set; } = 60000;

        /// <summary>
        /// 是否自动回复在线
        /// </summary>
        public bool AutoOnline { get; set; } = true;

        /// <summary>
        /// 默认控制状态
        /// </summary>
        public string DefaultControlState { get; set; } = "OnlineRemote";

        /// <summary>
        /// 建立通信超时时间（秒）- ECID 250
        /// 默认值: 5秒, 范围: 1-120秒
        /// </summary>
        public int EstablishCommunicationsTimeout { get; set; } = 5;

        /// <summary>
        /// 允许的主机列表（用于验证主机连接）
        /// 空列表表示允许所有主机
        /// </summary>
        public List<string> AllowedHosts { get; set; } = new();

        /// <summary>
        /// 验证主机信息
        /// </summary>
        /// <param name="hostInfo">主机信息</param>
        /// <returns>是否验证通过</returns>
        public bool ValidateHostInfo(string hostInfo)
        {
            // 如果允许列表为空，接受所有主机
            if (AllowedHosts == null || AllowedHosts.Count == 0)
                return true;

            // 检查主机是否在允许列表中
            return AllowedHosts.Contains(hostInfo, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// PLC配置
    /// </summary>
    public class PlcConfiguration
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public ushort DeviceId { get; set; } = 1;
        /// <summary>
        /// PLC IP地址
        /// </summary>
        public string IpAddress { get; set; } = "192.168.1.10";

        /// <summary>
        /// PLC端口
        /// </summary>
        public int Port { get; set; } = 5001;

        /// <summary>
        /// 站号
        /// </summary>
        public int StationNumber { get; set; } = 0;

        /// <summary>
        /// 网络号
        /// </summary>
        public int NetworkNumber { get; set; } = 0;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollInterval { get; set; } = 100;

        /// <summary>
        /// 连接超时（毫秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;

        /// <summary>
        /// 是否启用PLC通信
        /// </summary>
        public bool Enabled { get; set; } = false;
    }

    /// <summary>
    /// 日志配置
    /// </summary>
    public class LogConfiguration
    {
        /// <summary>
        /// 日志路径
        /// </summary>
        public string LogPath { get; set; } = "Logs";

        /// <summary>
        /// 日志级别
        /// </summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// 是否启用控制台日志
        /// </summary>
        public bool EnableConsole { get; set; } = true;

        /// <summary>
        /// 是否启用文件日志
        /// </summary>
        public bool EnableFile { get; set; } = true;

        /// <summary>
        /// 日志文件滚动大小(MB)
        /// </summary>
        public int RollingFileSizeMB { get; set; } = 100;

        /// <summary>
        /// 日志保留天数
        /// </summary>
        public int RetainDays { get; set; } = 30;
    }

    /// <summary>
    /// 性能配置
    /// </summary>
    public class PerformanceConfiguration
    {
        /// <summary>
        /// 最大事件报告数量
        /// </summary>
        public int MaxEventReports { get; set; } = 100;

        /// <summary>
        /// 最大数据采集任务数量
        /// </summary>
        public int MaxDataTraces { get; set; } = 10;

        /// <summary>
        /// 消息处理超时时间(毫秒)
        /// </summary>
        public int MessageTimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// 功能配置
    /// </summary>
    public class FeatureConfiguration
    {
        /// <summary>
        /// 是否自动上线
        /// </summary>
        public bool AutoOnline { get; set; }

        /// <summary>
        /// 是否启用事件报告
        /// </summary>
        public bool EnableEventReport { get; set; } = true;

        /// <summary>
        /// 是否启用报警管理
        /// </summary>
        public bool EnableAlarmManagement { get; set; } = true;

        /// <summary>
        /// 是否启用数据采集
        /// </summary>
        public bool EnableDataCollection { get; set; } = true;
    }

    /// <summary>
    /// 数据采集配置
    /// </summary>
    public class DataCollectionConfiguration
    {
        /// <summary>
        /// 默认采样周期(毫秒)
        /// </summary>
        public int DefaultSamplingPeriodMs { get; set; } = 1000;

        /// <summary>
        /// 最小采样周期(毫秒)
        /// </summary>
        public int MinSamplingPeriodMs { get; set; } = 100;

        /// <summary>
        /// 最大采样周期(毫秒)
        /// </summary>
        public int MaxSamplingPeriodMs { get; set; } = 3600000; // 1小时

        /// <summary>
        /// 最大采样数量
        /// </summary>
        public uint MaxTotalSamples { get; set; } = 100000;

        /// <summary>
        /// 最大报告组大小
        /// </summary>
        public uint MaxReportGroupSize { get; set; } = 1000;

        /// <summary>
        /// 每个跟踪任务的最大SVID数量
        /// </summary>
        public int MaxSvidsPerTrace { get; set; } = 50;

        /// <summary>
        /// 数据缓存目录
        /// </summary>
        public string CacheDirectory { get; set; } = "TraceData";
    }

    /// <summary>
    /// 事件报告配置
    /// </summary>
    public class EventReportConfiguration
    {
        /// <summary>
        /// 最大报告定义数量
        /// </summary>
        public int MaxReportDefinitions { get; set; } = 100;

        /// <summary>
        /// 每个报告的最大VID数量
        /// </summary>
        public int MaxVidsPerReport { get; set; } = 20;

        /// <summary>
        /// 事件报告发送超时时间(毫秒)
        /// </summary>
        public int EventReportTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 是否启用事件报告缓存
        /// </summary>
        public bool EnableEventCache { get; set; } = true;

        /// <summary>
        /// 事件缓存大小
        /// </summary>
        public int EventCacheSize { get; set; } = 1000;
    }
}
