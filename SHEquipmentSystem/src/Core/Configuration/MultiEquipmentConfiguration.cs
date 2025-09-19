// 文件路径: src/Core/Configuration/MultiEquipmentConfiguration.cs
// 版本: v1.0.0
// 描述: 多设备实例配置模型

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DiceEquipmentSystem.Core.Configuration
{
    /// <summary>
    /// 多设备系统配置
    /// </summary>
    public class MultiEquipmentSystemConfiguration
    {
        /// <summary>
        /// 系统级配置
        /// </summary>
        public SystemConfiguration System { get; set; } = new();

        /// <summary>
        /// 设备实例列表
        /// </summary>
        public List<EquipmentInstanceConfiguration> EquipmentInstances { get; set; } = new();

        /// <summary>
        /// 全局性能配置
        /// </summary>
        public GlobalPerformanceConfiguration GlobalPerformance { get; set; } = new();

        /// <summary>
        /// 全局日志配置
        /// </summary>
        public GlobalLogConfiguration GlobalLogging { get; set; } = new();

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (EquipmentInstances == null || EquipmentInstances.Count == 0)
            {
                errors.Add("至少需要配置一个设备实例");
                return false;
            }

            // 检查设备ID唯一性
            var deviceIds = new HashSet<string>();
            var secsDeviceIds = new HashSet<ushort>();
            var plcEndpoints = new HashSet<string>();

            foreach (var instance in EquipmentInstances)
            {
                // 验证设备ID唯一性
                if (!deviceIds.Add(instance.DeviceId))
                {
                    errors.Add($"设备ID重复: {instance.DeviceId}");
                }

                // 验证SECS设备ID唯一性
                if (instance.SecsConfiguration.Enabled)
                {
                    if (!secsDeviceIds.Add(instance.SecsConfiguration.DeviceId))
                    {
                        errors.Add($"SECS设备ID重复: {instance.SecsConfiguration.DeviceId}");
                    }
                }

                // 验证PLC连接唯一性
                if (instance.PlcConfiguration.Enabled)
                {
                    var endpoint = $"{instance.PlcConfiguration.IpAddress}:{instance.PlcConfiguration.Port}";
                    if (!plcEndpoints.Add(endpoint))
                    {
                        errors.Add($"PLC连接端点重复: {endpoint}");
                    }
                }

                // 验证实例配置
                if (!instance.Validate(out var instanceErrors))
                {
                    errors.AddRange(instanceErrors.Select(e => $"设备{instance.DeviceId}: {e}"));
                }
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// 设备实例配置
    /// </summary>
    public class EquipmentInstanceConfiguration
    {
        /// <summary>
        /// 设备唯一标识
        /// </summary>
        [Required]
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// 设备名称
        /// </summary>
        [Required]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 设备型号
        /// </summary>
        public string ModelName { get; set; } = "DICER-3000";

        /// <summary>
        /// 设备描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用此设备实例
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 设备优先级（用于启动顺序等）
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// SECS/GEM配置
        /// </summary>
        public SecsInstanceConfiguration SecsConfiguration { get; set; } = new();

        /// <summary>
        /// PLC配置
        /// </summary>
        public PlcInstanceConfiguration PlcConfiguration { get; set; } = new();

        /// <summary>
        /// 设备特定配置
        /// </summary>
        public EquipmentConfiguration Equipment { get; set; } = new();

        /// <summary>
        /// SVID映射配置
        /// </summary>
        public Dictionary<uint, string> SvidMapping { get; set; } = new();

        /// <summary>
        /// 事件配置
        /// </summary>
        public EventConfiguration EventConfiguration { get; set; } = new();

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(DeviceId))
            {
                errors.Add("DeviceId不能为空");
            }

            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                errors.Add("DeviceName不能为空");
            }

            if (SecsConfiguration.Enabled && !SecsConfiguration.Validate(out var secsErrors))
            {
                errors.AddRange(secsErrors.Select(e => $"SECS配置: {e}"));
            }

            if (PlcConfiguration.Enabled && !PlcConfiguration.Validate(out var plcErrors))
            {
                errors.AddRange(plcErrors.Select(e => $"PLC配置: {e}"));
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// SECS实例配置
    /// </summary>
    public class SecsInstanceConfiguration
    {
        /// <summary>
        /// 是否启用SECS通信
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// SECS设备ID
        /// </summary>
        public ushort DeviceId { get; set; } = 1;

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; } = 5000;

        /// <summary>
        /// 是否为Active模式
        /// </summary>
        public bool IsActive { get; set; } = false;

        /// <summary>
        /// 超时配置
        /// </summary>
        public SecsTimeoutConfiguration Timeouts { get; set; } = new();

        /// <summary>
        /// 自动重连配置
        /// </summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔（毫秒）
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (DeviceId <= 0 || DeviceId > 32767)
            {
                errors.Add("DeviceId必须在1-32767范围内");
            }

            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                errors.Add("IpAddress不能为空");
            }

            if (Port <= 0 || Port > 65535)
            {
                errors.Add("Port必须在1-65535范围内");
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// PLC实例配置
    /// </summary>
    public class PlcInstanceConfiguration
    {
        /// <summary>
        /// 是否启用PLC通信
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// PLC类型
        /// </summary>
        public string PlcType { get; set; } = "Mitsubishi";

        /// <summary>
        /// IP地址
        /// </summary>
        public string IpAddress { get; set; } = "192.168.1.10";

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; } = 5007;

        /// <summary>
        /// 网络号
        /// </summary>
        public int NetworkNumber { get; set; } = 0;

        /// <summary>
        /// 站号
        /// </summary>
        public int StationNumber { get; set; } = 0;

        /// <summary>
        /// 是否使用模拟模式
        /// </summary>
        public bool UseSimulation { get; set; } = false;

        /// <summary>
        /// 连接配置
        /// </summary>
        public PlcConnectionConfiguration Connection { get; set; } = new();

        /// <summary>
        /// 数据块配置
        /// </summary>
        public List<PlcDataBlockConfiguration> DataBlocks { get; set; } = new();

        /// <summary>
        /// 验证配置
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                errors.Add("IpAddress不能为空");
            }

            if (Port <= 0 || Port > 65535)
            {
                errors.Add("Port必须在1-65535范围内");
            }

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// SECS超时配置
    /// </summary>
    public class SecsTimeoutConfiguration
    {
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
    }

    /// <summary>
    /// PLC连接配置
    /// </summary>
    public class PlcConnectionConfiguration
    {
        /// <summary>
        /// 连接超时（毫秒）
        /// </summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>
        /// 接收超时（毫秒）
        /// </summary>
        public int ReceiveTimeout { get; set; } = 3000;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollingInterval { get; set; } = 1000;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>
        /// 自动重连
        /// </summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔（毫秒）
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;
    }

    /// <summary>
    /// PLC数据块配置
    /// </summary>
    public class PlcDataBlockConfiguration
    {
        /// <summary>
        /// 数据块名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 起始地址
        /// </summary>
        public string StartAddress { get; set; } = string.Empty;

        /// <summary>
        /// 长度
        /// </summary>
        public int Length { get; set; } = 100;

        /// <summary>
        /// 更新间隔（毫秒）
        /// </summary>
        public int UpdateInterval { get; set; } = 1000;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// 事件配置
    /// </summary>
    public class EventConfiguration
    {
        /// <summary>
        /// 默认事件报告配置
        /// </summary>
        public List<DefaultReportConfiguration> DefaultReports { get; set; } = new();

        /// <summary>
        /// 默认事件链接配置
        /// </summary>
        public List<DefaultEventLinkConfiguration> DefaultEventLinks { get; set; } = new();
    }

    /// <summary>
    /// 默认报告配置
    /// </summary>
    public class DefaultReportConfiguration
    {
        /// <summary>
        /// 报告ID
        /// </summary>
        public int ReportId { get; set; }

        /// <summary>
        /// 变量ID列表
        /// </summary>
        public List<uint> VariableIds { get; set; } = new();

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 默认事件链接配置
    /// </summary>
    public class DefaultEventLinkConfiguration
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        public uint EventId { get; set; }

        /// <summary>
        /// 报告ID列表
        /// </summary>
        public List<int> ReportIds { get; set; } = new();

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 系统配置
    /// </summary>
    public class SystemConfiguration
    {
        /// <summary>
        /// 系统名称
        /// </summary>
        public string SystemName { get; set; } = "SH Multi-Equipment System";

        /// <summary>
        /// 系统版本
        /// </summary>
        public string SystemVersion { get; set; } = "1.0.0";

        /// <summary>
        /// 最大并发设备数
        /// </summary>
        public int MaxConcurrentDevices { get; set; } = 10;

        /// <summary>
        /// 是否启用设备自动发现
        /// </summary>
        public bool EnableAutoDiscovery { get; set; } = false;

        /// <summary>
        /// 健康检查间隔（秒）
        /// </summary>
        public int HealthCheckInterval { get; set; } = 30;
    }

    /// <summary>
    /// 全局性能配置
    /// </summary>
    public class GlobalPerformanceConfiguration
    {
        /// <summary>
        /// 全局消息超时时间（毫秒）
        /// </summary>
        public int GlobalMessageTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 全局最大线程数
        /// </summary>
        public int MaxThreadCount { get; set; } = 100;

        /// <summary>
        /// 内存使用限制（MB）
        /// </summary>
        public int MemoryLimitMB { get; set; } = 1024;

        /// <summary>
        /// 数据缓存大小
        /// </summary>
        public int CacheSize { get; set; } = 10000;
    }

    /// <summary>
    /// 全局日志配置
    /// </summary>
    public class GlobalLogConfiguration
    {
        /// <summary>
        /// 根日志路径
        /// </summary>
        public string RootLogPath { get; set; } = @"..\..\logs";

        /// <summary>
        /// 日志级别
        /// </summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// 是否启用按设备分离日志
        /// </summary>
        public bool EnablePerDeviceLogging { get; set; } = true;

        /// <summary>
        /// 日志保留天数
        /// </summary>
        public int RetainDays { get; set; } = 30;

        /// <summary>
        /// 是否启用结构化日志
        /// </summary>
        public bool EnableStructuredLogging { get; set; } = true;
    }
}