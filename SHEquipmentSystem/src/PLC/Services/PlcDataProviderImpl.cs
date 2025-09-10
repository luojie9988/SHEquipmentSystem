// 文件路径: src/SHEquipmentSystem/PLC/Services/PlcDataProviderImpl.cs
// 版本: v4.0.0
// 描述: PLC数据提供者实现 - 支持实际PLC连接和模拟模式

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HslCommunication;
using HslCommunication.Profinet.Melsec;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Models;
using DiceEquipmentSystem.PLC.Mapping;

namespace SHEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC数据提供者实现 - 支持实际PLC连接和模拟模式
    /// 根据配置自动切换实际PLC通信或模拟数据生成
    /// </summary>
    public class PlcDataProviderImpl : IPlcDataProvider, IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<PlcDataProviderImpl> _logger;
        private readonly IConfiguration _configuration;
        private readonly PlcConfiguration _plcConfig;
        private readonly PlcDataMapper _dataMapper;

        /// <summary>三菱PLC通信对象（实际模式）</summary>
        private MelsecMcNet? _plc;

        /// <summary>连接状态</summary>
        private volatile bool _isConnected;

        /// <summary>是否使用模拟模式</summary>
        private readonly bool _useSimulation;

        /// <summary>数据缓存</summary>
        private readonly ConcurrentDictionary<string, object> _dataCache;

        /// <summary>读写锁</summary>
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        /// <summary>数据采集定时器</summary>
        private Timer? _dataCollectionTimer;

        /// <summary>模拟数据生成定时器</summary>
        private Timer? _simulationTimer;

        /// <summary>重连定时器</summary>
        private Timer? _reconnectTimer;

        /// <summary>事件监控定时器</summary>
        private Timer? _eventMonitorTimer;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>连接统计信息</summary>
        private readonly PlcConnectionStatistics _statistics = new PlcConnectionStatistics();

        /// <summary>最后连接时间</summary>
        private DateTime? _lastConnectedTime;

        /// <summary>最后错误信息</summary>
        private string? _lastError;

        /// <summary>随机数生成器（模拟模式）</summary>
        private readonly Random _random = new Random();

        /// <summary>模拟数据状态</summary>
        private SimulationState _simulationState = new SimulationState();

        /// <summary>事件监控字典</summary>
        private Dictionary<uint, string>? _monitoredEvents;

        /// <summary>事件触发回调</summary>
        private Action<uint>? _eventCallback;

        #endregion

        #region 属性

        /// <summary>
        /// 连接状态
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 连接统计信息
        /// </summary>
        public PlcConnectionStatistics Statistics => _statistics;

        /// <summary>
        /// 最后连接时间
        /// </summary>
        public DateTime? LastConnectedTime => _lastConnectedTime;

        /// <summary>
        /// 最后错误信息
        /// </summary>
        public string? LastError => _lastError;

        /// <summary>
        /// 是否为模拟模式
        /// </summary>
        public bool IsSimulationMode => _useSimulation;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcDataProviderImpl(
            ILogger<PlcDataProviderImpl> logger,
            IConfiguration configuration,
            PlcDataMapper dataMapper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dataMapper = dataMapper ?? throw new ArgumentNullException(nameof(dataMapper));

            // 加载配置
            _plcConfig = LoadPlcConfiguration();
            _useSimulation = _configuration.GetValue("PLC:UseSimulation", true);
            _dataCache = new ConcurrentDictionary<string, object>();

            _logger.LogInformation("════════════════════════════════════════════");
            _logger.LogInformation("PLC数据提供者已初始化");
            _logger.LogInformation($"运行模式: {(_useSimulation ? "🎮 模拟模式" : "🔌 实际PLC连接")}");
            _logger.LogInformation($"IP地址: {_plcConfig.IpAddress}:{_plcConfig.Port}");
            _logger.LogInformation($"网络号: {_plcConfig.NetworkNumber}, 站号: {_plcConfig.StationNumber}");
            _logger.LogInformation($"采集周期: {_plcConfig.PollInterval}ms");
            _logger.LogInformation("════════════════════════════════════════════");

            if (_useSimulation)
            {
                InitializeSimulationData();
            }
            else
            {
                InitializePlcConnection();
            }
        }

        #endregion

        #region IHostedService实现

        /// <summary>
        /// 启动服务
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在启动PLC数据采集服务...");

            _cancellationTokenSource = new CancellationTokenSource();

            if (_useSimulation)
            {
                await StartSimulationModeAsync(cancellationToken);
            }
            else
            {
                await StartRealModeAsync(cancellationToken);
            }

            // 启动数据采集定时器
            _dataCollectionTimer = new Timer(
                async _ => await CollectDataAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(_plcConfig.PollInterval));

            _logger.LogInformation("✅ PLC数据采集服务已启动");
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止PLC数据采集服务...");

            _dataCollectionTimer?.Dispose();
            _simulationTimer?.Dispose();
            _reconnectTimer?.Dispose();
            _eventMonitorTimer?.Dispose();
            _cancellationTokenSource?.Cancel();

            await DisconnectAsync();

            _logger.LogInformation("PLC数据采集服务已停止");
        }

        #endregion

        #region IPlcDataProvider实现

        /// <summary>
        /// 连接到PLC
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_isConnected)
            {
                _logger.LogDebug("PLC已连接，跳过连接操作");
                return true;
            }

            if (_useSimulation)
            {
                return await ConnectSimulationAsync();
            }
            else
            {
                return await ConnectRealPlcAsync(cancellationToken);
            }
        }

        /// <summary>
        /// 断开PLC连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                if (!_useSimulation)
                {
                    _plc?.ConnectClose();
                }

                _isConnected = false;
                _logger.LogInformation($"{(_useSimulation ? "模拟" : "实际")}PLC连接已断开");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开PLC连接时发生异常");
            }
        }

        /// <summary>
        /// 读取SVID值
        /// </summary>
        public async Task<object?> ReadSvidAsync(uint svid, string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取SVID {svid}");
                return null;
            }

            try
            {
                object? result;

                if (_useSimulation)
                {
                    result = ReadSimulationValue(address);
                }
                else
                {
                    result = await ReadRealPlcValueAsync(address, cancellationToken);
                }

                if (result != null)
                {
                    _statistics.ReceiveCount++;
                    _logger.LogDebug($"读取SVID {svid} 成功: {address} = {result}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取SVID {svid} 失败: {address}");
                return null;
            }
        }

        /// <summary>
        /// 写入ECID值
        /// </summary>
        public async Task<bool> WriteEcidAsync(uint ecid, string address, object value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入ECID {ecid}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealPlcValueAsync(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入ECID {ecid} 成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入ECID {ecid} 失败: {address}");
                return false;
            }
        }

        /// <summary>
        /// 读取Int16值
        /// </summary>
        public async Task<short> ReadInt16Async(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToInt16(value ?? 0);
                }
                else
                {
                    return await ReadRealInt16Async(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取Int16异常: {address}");
                return 0;
            }
        }

        /// <summary>
        /// 读取Int32值
        /// </summary>
        public async Task<int> ReadInt32Async(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToInt32(value ?? 0);
                }
                else
                {
                    return await ReadRealInt32Async(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取Int32异常: {address}");
                return 0;
            }
        }

        /// <summary>
        /// 读取Float值
        /// </summary>
        public async Task<float> ReadFloatAsync(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0f;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToSingle(value ?? 0);
                }
                else
                {
                    return await ReadRealFloatAsync(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取Float异常: {address}");
                return 0f;
            }
        }

        /// <summary>
        /// 读取Boolean值
        /// </summary>
        public async Task<bool> ReadBoolAsync(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return false;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToBoolean(value ?? false);
                }
                else
                {
                    return await ReadRealBoolAsync(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取Bool异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 读取字符串值
        /// </summary>
        public async Task<string> ReadStringAsync(string address, int length = 32, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return string.Empty;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return value?.ToString() ?? string.Empty;
                }
                else
                {
                    return await ReadRealStringAsync(address, length, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取String异常: {address}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 读取Byte值
        /// </summary>
        public async Task<byte> ReadByteAsync(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToByte(value ?? 0);
                }
                else
                {
                    return await ReadRealByteAsync(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取Byte异常: {address}");
                return 0;
            }
        }

        /// <summary>
        /// 读取UInt16值
        /// </summary>
        public async Task<ushort> ReadUInt16Async(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToUInt16(value ?? 0);
                }
                else
                {
                    return await ReadRealUInt16Async(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取UInt16异常: {address}");
                return 0;
            }
        }

        /// <summary>
        /// 读取UInt32值
        /// </summary>
        public async Task<uint> ReadUInt32Async(string address, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址 {address}");
                return 0;
            }

            try
            {
                if (_useSimulation)
                {
                    var value = ReadSimulationValue(address);
                    return Convert.ToUInt32(value ?? 0);
                }
                else
                {
                    return await ReadRealUInt32Async(address, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"读取UInt32异常: {address}");
                return 0;
            }
        }

        /// <summary>
        /// 写入Int16值
        /// </summary>
        public async Task<bool> WriteInt16Async(string address, short value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealInt16Async(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入Int16成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入Int16异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入Int32值
        /// </summary>
        public async Task<bool> WriteInt32Async(string address, int value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealInt32Async(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入Int32成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入Int32异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入Float值
        /// </summary>
        public async Task<bool> WriteFloatAsync(string address, float value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealFloatAsync(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入Float成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入Float异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入Boolean值
        /// </summary>
        public async Task<bool> WriteBoolAsync(string address, bool value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealBoolAsync(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入Bool成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入Bool异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入Byte值
        /// </summary>
        public async Task<bool> WriteByteAsync(string address, byte value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealByteAsync(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入Byte成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入Byte异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入UInt16值
        /// </summary>
        public async Task<bool> WriteUInt16Async(string address, ushort value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealUInt16Async(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入UInt16成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入UInt16异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入UInt32值
        /// </summary>
        public async Task<bool> WriteUInt32Async(string address, uint value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealUInt32Async(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入UInt32成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入UInt32异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 写入字符串值
        /// </summary>
        public async Task<bool> WriteStringAsync(string address, string value, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址 {address}");
                return false;
            }

            try
            {
                bool success;

                if (_useSimulation)
                {
                    success = WriteSimulationValue(address, value);
                }
                else
                {
                    success = await WriteRealStringAsync(address, value, cancellationToken);
                }

                if (success)
                {
                    _statistics.SentCount++;
                    _logger.LogDebug($"写入String成功: {address} = {value}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"写入String异常: {address}");
                return false;
            }
        }

        /// <summary>
        /// 执行PLC命令（带参数）
        /// </summary>
        public async Task<PlcExecutionResult> ExecuteAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                return PlcExecutionResult.CreateFailure("PLC未连接，无法执行命令");
            }

            var startTime = DateTime.Now;

            try
            {
                _logger.LogInformation($"执行PLC命令: {command}，参数: {string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}");

                if (_useSimulation)
                {
                    return await ExecuteSimulationCommandAsync(command, parameters, cancellationToken);
                }
                else
                {
                    return await ExecuteRealCommandAsync(command, parameters, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _logger.LogError(ex, $"执行PLC命令异常: {command}");

                return PlcExecutionResult.CreateFailure(
                    $"执行命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        /// <summary>
        /// 执行PLC命令（无参数）
        /// </summary>
        public async Task<PlcExecutionResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(command, new Dictionary<string, object>(), cancellationToken);
        }

        /// <summary>
        /// 监控PLC事件
        /// </summary>
        public void MonitorEvents(Dictionary<uint, string> ceidAddress, Action<uint> onEventTriggered)
        {
            _monitoredEvents = ceidAddress;
            _eventCallback = onEventTriggered;

            if (_monitoredEvents?.Any() == true)
            {
                _logger.LogInformation($"开始监控 {_monitoredEvents.Count} 个PLC事件");

                // 启动事件监控定时器（每100ms检查一次）
                _eventMonitorTimer = new Timer(
                    async _ => await CheckEventsAsync(),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100));
            }
        }

        /// <summary>
        /// 停止事件监控
        /// </summary>
        public async Task StopEventMonitoringAsync()
        {
            _eventMonitorTimer?.Dispose();
            _eventMonitorTimer = null;
            _monitoredEvents = null;
            _eventCallback = null;

            _logger.LogInformation("PLC事件监控已停止");
            await Task.CompletedTask;
        }

        #endregion

        #region 模拟模式实现

        /// <summary>
        /// 启动模拟模式
        /// </summary>
        private async Task StartSimulationModeAsync(CancellationToken cancellationToken)
        {
            _isConnected = true;
            _lastConnectedTime = DateTime.Now;
            _statistics.ConnectionCount++;

            _logger.LogInformation("✅ 模拟PLC连接成功");
            _logger.LogInformation("📊 初始化设备数据:");
            _logger.LogInformation($"  - 坐标: X={_dataCache["D100"]}, Y={_dataCache["D102"]}, Z={_dataCache["D104"]}");
            _logger.LogInformation($"  - 配方: {_dataCache["D400"]}");
            _logger.LogInformation($"  - 批次: {_dataCache["D420"]}");

            // 启动模拟数据生成器
            StartSimulation();

            await Task.CompletedTask;
        }

        /// <summary>
        /// 连接模拟PLC
        /// </summary>
        private async Task<bool> ConnectSimulationAsync()
        {
            _isConnected = true;
            _lastConnectedTime = DateTime.Now;
            _statistics.ConnectionCount++;

            _logger.LogInformation("✅ 模拟PLC连接成功");
            return await Task.FromResult(true);
        }

        /// <summary>
        /// 初始化模拟数据
        /// </summary>
        private void InitializeSimulationData()
        {
            // 设备坐标数据
            _dataCache["D100"] = (short)1000;    // X坐标
            _dataCache["D102"] = (short)2000;    // Y坐标  
            _dataCache["D104"] = (short)500;     // Z坐标

            // 设备状态
            _dataCache["M100"] = true;           // 设备运行状态
            _dataCache["M101"] = false;          // 报警状态
            _dataCache["M102"] = true;           // 自动模式

            // 工艺参数
            _dataCache["D200"] = (short)1500;    // 切割速度
            _dataCache["D202"] = (short)800;     // 进给速度
            _dataCache["D204"] = (short)50;      // 切割深度

            // 生产数据
            _dataCache["D300"] = (int)156;       // 当日产量
            _dataCache["D302"] = (int)150;       // 良品数
            _dataCache["D304"] = (int)6;         // 不良品数

            // 配方和批次
            _dataCache["D400"] = "Recipe001";    // 当前配方
            _dataCache["D420"] = "Batch20250910"; // 当前批次

            _logger.LogDebug("模拟数据初始化完成");
        }

        /// <summary>
        /// 启动模拟数据生成
        /// </summary>
        private void StartSimulation()
        {
            _simulationTimer = new Timer(
                _ => UpdateSimulationData(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2)); // 每2秒更新一次模拟数据
        }

        /// <summary>
        /// 更新模拟数据
        /// </summary>
        private void UpdateSimulationData()
        {
            try
            {
                // 模拟坐标变化
                var currentX = (short)(_dataCache.GetValueOrDefault("D100", (short)1000));
                var currentY = (short)(_dataCache.GetValueOrDefault("D102", (short)2000));

                _dataCache["D100"] = (short)(currentX + _random.Next(-50, 51));
                _dataCache["D102"] = (short)(currentY + _random.Next(-30, 31));

                // 模拟生产计数增加
                var currentCount = (int)(_dataCache.GetValueOrDefault("D300", 156));
                if (_random.Next(1, 11) > 7) // 30%概率增加产量
                {
                    _dataCache["D300"] = currentCount + 1;

                    // 95%良品率
                    if (_random.Next(1, 101) <= 95)
                    {
                        var goodCount = (int)(_dataCache.GetValueOrDefault("D302", 150));
                        _dataCache["D302"] = goodCount + 1;
                    }
                    else
                    {
                        var badCount = (int)(_dataCache.GetValueOrDefault("D304", 6));
                        _dataCache["D304"] = badCount + 1;
                    }
                }

                // 模拟偶发报警
                if (_random.Next(1, 1001) == 1) // 0.1%概率触发报警
                {
                    _dataCache["M101"] = true;
                    _logger.LogWarning("模拟报警触发");
                }
                else if ((bool)(_dataCache.GetValueOrDefault("M101", false)) && _random.Next(1, 6) == 1)
                {
                    _dataCache["M101"] = false; // 20%概率清除报警
                    _logger.LogInformation("模拟报警清除");
                }

                _logger.LogTrace("模拟数据已更新");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新模拟数据异常");
            }
        }

        /// <summary>
        /// 读取模拟值
        /// </summary>
        private object? ReadSimulationValue(string address)
        {
            _dataCache.TryGetValue(address, out var value);
            return value;
        }

        /// <summary>
        /// 写入模拟值
        /// </summary>
        private bool WriteSimulationValue(string address, object value)
        {
            _dataCache[address] = value;
            _logger.LogDebug($"写入模拟值: {address} = {value}");
            return true;
        }

        /// <summary>
        /// 执行模拟命令
        /// </summary>
        private async Task<PlcExecutionResult> ExecuteSimulationCommandAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                await Task.Delay(100, cancellationToken); // 模拟执行时间

                switch (command.ToUpper())
                {
                    case "START_EQUIPMENT":
                        _dataCache["M100"] = true; // 设备运行状态
                        _logger.LogInformation("模拟设备启动成功");
                        break;

                    case "STOP_EQUIPMENT":
                        _dataCache["M100"] = false; // 设备停止状态
                        _logger.LogInformation("模拟设备停止成功");
                        break;

                    case "RESET_EQUIPMENT":
                        _dataCache["M101"] = false; // 清除报警
                        _dataCache["D300"] = 0; // 重置产量计数
                        _logger.LogInformation("模拟设备重置成功");
                        break;

                    case "CHANGE_RECIPE":
                        if (parameters.TryGetValue("recipeName", out var recipe))
                        {
                            _dataCache["D400"] = recipe.ToString();
                            _logger.LogInformation($"模拟配方切换成功: {recipe}");
                        }
                        break;

                    default:
                        return PlcExecutionResult.CreateFailure(
                            $"未知的模拟命令: {command}",
                            DateTime.Now - startTime);
                }

                return PlcExecutionResult.CreateSuccess(
                    new Dictionary<string, object> { { "command", command }, { "result", "success" } },
                    DateTime.Now - startTime);
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"执行模拟命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        #endregion

        #region 实际PLC模式实现

        /// <summary>
        /// 启动实际PLC模式
        /// </summary>
        private async Task StartRealModeAsync(CancellationToken cancellationToken)
        {
            // 尝试连接实际PLC
            await ConnectAsync(cancellationToken);

            // 启动重连定时器（如果启用自动重连）
            if (_plcConfig.EnableAutoReconnect)
            {
                _reconnectTimer = new Timer(
                    async _ => await CheckAndReconnectAsync(),
                    null,
                    TimeSpan.FromMilliseconds(_plcConfig.ReconnectInterval),
                    TimeSpan.FromMilliseconds(_plcConfig.ReconnectInterval));
            }
        }

        /// <summary>
        /// 连接实际PLC
        /// </summary>
        private async Task<bool> ConnectRealPlcAsync(CancellationToken cancellationToken)
        {
            if (_plc == null)
            {
                _logger.LogError("PLC连接对象未初始化");
                return false;
            }

            try
            {
                _logger.LogInformation($"正在连接PLC: {_plcConfig.IpAddress}:{_plcConfig.Port}");

                var connectResult = await Task.Run(() => _plc.ConnectServer(), cancellationToken);

                if (connectResult?.IsSuccess == true)
                {
                    _isConnected = true;
                    _lastConnectedTime = DateTime.Now;
                    _lastError = null;
                    _statistics.ConnectionCount++;

                    _logger.LogInformation($"✅ PLC连接成功: {_plcConfig.IpAddress}:{_plcConfig.Port}");

                    // 测试连接有效性
                    await TestRealConnectionAsync();

                    return true;
                }
                else
                {
                    _lastError = connectResult?.Message ?? "连接失败";
                    _statistics.ErrorCount++;

                    _logger.LogError($"❌ PLC连接失败: {_lastError}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _statistics.ErrorCount++;
                _logger.LogError(ex, "PLC连接异常");
                return false;
            }
        }

        /// <summary>
        /// 测试实际PLC连接
        /// </summary>
        private async Task TestRealConnectionAsync()
        {
            try
            {
                // 尝试读取一个测试地址来验证连接
                var testAddress = "M100"; // 测试地址
                await Task.Run(() => _plc?.ReadBool(testAddress));

                _logger.LogDebug("实际PLC连接测试成功");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "实际PLC连接测试失败，但连接可能仍然有效");
            }
        }

        /// <summary>
        /// 读取实际PLC值
        /// </summary>
        private async Task<object?> ReadRealPlcValueAsync(string address, CancellationToken cancellationToken)
        {
            // 根据地址类型判断读取方法
            if (address.StartsWith("M") || address.StartsWith("X") || address.StartsWith("Y"))
            {
                return await ReadRealBoolAsync(address, cancellationToken);
            }
            else if (address.StartsWith("D") || address.StartsWith("W"))
            {
                return await ReadRealInt16Async(address, cancellationToken);
            }
            else
            {
                _logger.LogWarning($"未识别的地址类型: {address}");
                return null;
            }
        }

        /// <summary>
        /// 写入实际PLC值
        /// </summary>
        private async Task<bool> WriteRealPlcValueAsync(string address, object value, CancellationToken cancellationToken)
        {
            try
            {
                // 根据地址类型和值类型判断写入方法
                if (address.StartsWith("M") || address.StartsWith("X") || address.StartsWith("Y"))
                {
                    if (value is bool boolValue)
                    {
                        return await WriteRealBoolAsync(address, boolValue, cancellationToken);
                    }
                }
                else if (address.StartsWith("D") || address.StartsWith("W"))
                {
                    if (value is short shortValue)
                    {
                        return await WriteRealInt16Async(address, shortValue, cancellationToken);
                    }
                    else if (value is int intValue)
                    {
                        return await WriteRealInt32Async(address, intValue, cancellationToken);
                    }
                    else if (value is float floatValue)
                    {
                        return await WriteRealFloatAsync(address, floatValue, cancellationToken);
                    }
                }

                _logger.LogWarning($"无法写入地址 {address}，值类型: {value?.GetType()}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"写入地址 {address} 异常");
                return false;
            }
        }

        // 实际PLC读取方法
        private async Task<short> ReadRealInt16Async(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadInt16(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取Int16失败: {address} - {result?.Message}");
                return 0;
            }
        }

        private async Task<int> ReadRealInt32Async(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadInt32(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取Int32失败: {address} - {result?.Message}");
                return 0;
            }
        }

        private async Task<float> ReadRealFloatAsync(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadFloat(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取Float失败: {address} - {result?.Message}");
                return 0f;
            }
        }

        private async Task<bool> ReadRealBoolAsync(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadBool(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取Bool失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<string> ReadRealStringAsync(string address, int length, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadString(address, (ushort)length), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content ?? string.Empty;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取String失败: {address} - {result?.Message}");
                return string.Empty;
            }
        }

        private async Task<byte> ReadRealByteAsync(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Read(address,1), cancellationToken);
            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value[0];
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取Byte失败: {address} - {result?.Message}");
                return 0;
            }
        }

        private async Task<ushort> ReadRealUInt16Async(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadUInt16(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取UInt16失败: {address} - {result?.Message}");
                return 0;
            }
        }

        private async Task<uint> ReadRealUInt32Async(string address, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.ReadUInt32(address), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.ReceiveCount++;
                var value = result.Content;
                _dataCache[address] = value;
                return value;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"读取UInt32失败: {address} - {result?.Message}");
                return 0;
            }
        }

        // 实际PLC写入方法
        private async Task<bool> WriteRealInt16Async(string address, short value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入Int16失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealInt32Async(string address, int value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入Int32失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealFloatAsync(string address, float value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入Float失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealBoolAsync(string address, bool value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入Bool失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealByteAsync(string address, byte value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入Byte失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealUInt16Async(string address, ushort value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入UInt16失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealUInt32Async(string address, uint value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入UInt32失败: {address} - {result?.Message}");
                return false;
            }
        }

        private async Task<bool> WriteRealStringAsync(string address, string value, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _plc?.Write(address, value), cancellationToken);

            if (result?.IsSuccess == true)
            {
                _statistics.SentCount++;
                _dataCache[address] = value;
                return true;
            }
            else
            {
                _statistics.ErrorCount++;
                _logger.LogWarning($"写入String失败: {address} - {result?.Message}");
                return false;
            }
        }

        /// <summary>
        /// 执行实际PLC命令
        /// </summary>
        private async Task<PlcExecutionResult> ExecuteRealCommandAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                // 这里可以根据具体的PLC命令实现相应的逻辑
                // 例如：启动设备、停止设备、切换模式等

                switch (command.ToUpper())
                {
                    case "START_EQUIPMENT":
                        return await ExecuteStartEquipmentCommand(parameters, cancellationToken);

                    case "STOP_EQUIPMENT":
                        return await ExecuteStopEquipmentCommand(parameters, cancellationToken);

                    case "RESET_EQUIPMENT":
                        return await ExecuteResetEquipmentCommand(parameters, cancellationToken);

                    case "CHANGE_RECIPE":
                        return await ExecuteChangeRecipeCommand(parameters, cancellationToken);

                    default:
                        return PlcExecutionResult.CreateFailure(
                            $"未知的PLC命令: {command}",
                            DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"执行实际PLC命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        #endregion

        #region 命令执行实现

        private async Task<PlcExecutionResult> ExecuteStartEquipmentCommand(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                // 写入启动信号到PLC
                var success = await WriteBoolAsync("M200", true, cancellationToken); // 启动信号地址

                if (success)
                {
                    return PlcExecutionResult.CreateSuccess(
                        new Dictionary<string, object> { { "started", true } },
                        DateTime.Now - startTime);
                }
                else
                {
                    return PlcExecutionResult.CreateFailure(
                        "发送启动信号失败",
                        DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"启动设备命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        private async Task<PlcExecutionResult> ExecuteStopEquipmentCommand(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                // 写入停止信号到PLC
                var success = await WriteBoolAsync("M201", true, cancellationToken); // 停止信号地址

                if (success)
                {
                    return PlcExecutionResult.CreateSuccess(
                        new Dictionary<string, object> { { "stopped", true } },
                        DateTime.Now - startTime);
                }
                else
                {
                    return PlcExecutionResult.CreateFailure(
                        "发送停止信号失败",
                        DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"停止设备命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        private async Task<PlcExecutionResult> ExecuteResetEquipmentCommand(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                // 写入重置信号到PLC
                var success = await WriteBoolAsync("M202", true, cancellationToken); // 重置信号地址

                if (success)
                {
                    return PlcExecutionResult.CreateSuccess(
                        new Dictionary<string, object> { { "reset", true } },
                        DateTime.Now - startTime);
                }
                else
                {
                    return PlcExecutionResult.CreateFailure(
                        "发送重置信号失败",
                        DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"重置设备命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        private async Task<PlcExecutionResult> ExecuteChangeRecipeCommand(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            try
            {
                if (!parameters.TryGetValue("recipeName", out var recipeObj))
                {
                    return PlcExecutionResult.CreateFailure(
                        "缺少配方名称参数",
                        DateTime.Now - startTime);
                }

                var recipeName = recipeObj.ToString();

                // 写入配方名称到PLC
                var success = await WriteStringAsync("D400", recipeName ?? "", cancellationToken);

                if (success)
                {
                    return PlcExecutionResult.CreateSuccess(
                        new Dictionary<string, object> { { "recipe", recipeName }, { "changed", true } },
                        DateTime.Now - startTime);
                }
                else
                {
                    return PlcExecutionResult.CreateFailure(
                        "写入配方名称失败",
                        DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                return PlcExecutionResult.CreateFailure(
                    $"切换配方命令异常: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        #endregion

        #region 事件监控实现

        /// <summary>
        /// 检查事件状态
        /// </summary>
        private async Task CheckEventsAsync()
        {
            if (_monitoredEvents == null || _eventCallback == null || !_isConnected)
            {
                return;
            }

            try
            {
                foreach (var kvp in _monitoredEvents)
                {
                    var ceid = kvp.Key;
                    var address = kvp.Value;

                    bool eventTriggered;

                    if (_useSimulation)
                    {
                        // 模拟事件触发（1%概率）
                        eventTriggered = _random.Next(1, 1001) == 1;
                    }
                    else
                    {
                        // 读取实际PLC地址状态
                        eventTriggered = await ReadBoolAsync(address);
                    }

                    if (eventTriggered)
                    {
                        _logger.LogInformation($"检测到事件触发: CEID={ceid}, 地址={address}");
                        _eventCallback(ceid);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查PLC事件异常");
            }
        }

        #endregion

        #region 通用私有方法

        /// <summary>
        /// 加载PLC配置
        /// </summary>
        private PlcConfiguration LoadPlcConfiguration()
        {
            var config = _configuration.GetSection("PLC").Get<PlcConfiguration>();

            if (config == null)
            {
                _logger.LogWarning("未找到PLC配置，使用默认配置");
                config = new PlcConfiguration();
            }

            return config;
        }

        /// <summary>
        /// 初始化PLC连接对象（实际模式）
        /// </summary>
        private void InitializePlcConnection()
        {
            try
            {
                _plc = new MelsecMcNet(_plcConfig.IpAddress, _plcConfig.Port)
                {
                    NetworkNumber = (byte)_plcConfig.NetworkNumber,
                    NetworkStationNumber = (byte)_plcConfig.StationNumber,
                    ConnectTimeOut = _plcConfig.ConnectionTimeout,
                    ReceiveTimeOut = _plcConfig.ReceiveTimeout
                };

                _logger.LogInformation("实际PLC连接对象初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化PLC连接对象失败");
                throw;
            }
        }

        /// <summary>
        /// 数据采集任务
        /// </summary>
        private async Task CollectDataAsync()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                // 这里可以定义需要定期采集的PLC地址列表
                // 实际实现时根据具体需求添加

                _logger.LogTrace($"{(_useSimulation ? "模拟" : "实际")}数据采集周期执行");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据采集异常");
            }
        }

        /// <summary>
        /// 检查连接并自动重连（仅实际模式）
        /// </summary>
        private async Task CheckAndReconnectAsync()
        {
            if (_isConnected || _useSimulation)
            {
                return;
            }

            try
            {
                _logger.LogInformation("检测到PLC连接断开，尝试重连...");
                await ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动重连失败");
            }
        }

        #endregion

        #region IDisposable实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _dataCollectionTimer?.Dispose();
            _simulationTimer?.Dispose();
            _reconnectTimer?.Dispose();
            _eventMonitorTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _lock?.Dispose();

            if (!_useSimulation)
            {
                _plc?.ConnectClose();
                _plc?.Dispose();
            }

            _logger.LogInformation("PLC数据提供者已释放资源");
        }

        #endregion
    }

    #region 辅助类定义

    /// <summary>
    /// PLC连接统计信息
    /// </summary>
    public class PlcConnectionStatistics
    {
        /// <summary>连接次数</summary>
        public int ConnectionCount { get; set; }

        /// <summary>发送消息计数</summary>
        public int SentCount { get; set; }

        /// <summary>接收消息计数</summary>
        public int ReceiveCount { get; set; }

        /// <summary>错误计数</summary>
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// 模拟状态数据
    /// </summary>
    public class SimulationState
    {
        public bool IsRunning { get; set; } = true;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public int CycleCount { get; set; } = 0;
    }


    #endregion
}