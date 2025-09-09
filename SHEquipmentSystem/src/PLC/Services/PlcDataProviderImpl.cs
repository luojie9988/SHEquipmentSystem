// 文件路径: src/DiceEquipmentSystem/PLC/Services/PlcDataProviderImpl.cs
// 版本: v3.2.0
// 描述: PLC数据提供者 - 模拟模式实现
// 更新: 2025-09-02 - 支持完整的生产流程模拟，不需要实际PLC硬件

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Mapping;
using DiceEquipmentSystem.PLC.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC数据提供者实现类 - 模拟模式
    /// 模拟与三菱PLC的实时通信，用于测试和演示
    /// </summary>
    /// <remarks>
    /// 功能特点：
    /// 1. 完全模拟PLC数据，不需要实际硬件
    /// 2. 支持完整的生产流程模拟
    /// 3. 自动生成变化的工艺数据
    /// 4. 模拟事件触发和报警
    /// 5. 支持远程命令响应
    /// </remarks>
    public class PlcDataProviderImpl : IPlcDataProvider, IHostedService, IDisposable
    {
        #region 私有字段

        private readonly ILogger<PlcDataProviderImpl> _logger;
        private readonly IConfiguration _configuration;
        private readonly PlcConnectionManager _connectionManager;
        private readonly PlcDataMapper _dataMapper;

        /// <summary>数据缓存</summary>
        private readonly ConcurrentDictionary<string, object> _dataCache;

        /// <summary>数据采集定时器</summary>
        private Timer? _dataCollectionTimer;

        /// <summary>模拟数据生成定时器</summary>
        private Timer? _simulationTimer;

        /// <summary>连接状态（模拟）</summary>
        private volatile bool _isConnected;

        /// <summary>是否使用模拟模式</summary>
        private bool _useSimulation;

        /// <summary>采集周期(毫秒)</summary>
        private readonly int _pollingInterval;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>随机数生成器</summary>
        private readonly Random _random = new Random();

        /// <summary>模拟数据状态</summary>
        private SimulationState _simulationState = new SimulationState();

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcDataProviderImpl(
            ILogger<PlcDataProviderImpl> logger,
            IConfiguration configuration,
            PlcConnectionManager connectionManager,
            PlcDataMapper dataMapper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _dataMapper = dataMapper ?? throw new ArgumentNullException(nameof(dataMapper));

            _dataCache = new ConcurrentDictionary<string, object>();
            _pollingInterval = _configuration.GetValue("PLC:PollingInterval", 200);
            _useSimulation = _configuration.GetValue("PLC:UseSimulation", true); // 默认使用模拟模式

            InitializeSimulationData();

            _logger.LogInformation("════════════════════════════════════════════");
            _logger.LogInformation("PLC数据提供者初始化");
            _logger.LogInformation($"模式: {(_useSimulation ? "🎮 模拟模式" : "🔌 实际PLC")}");
            _logger.LogInformation($"采集周期: {_pollingInterval}ms");
            _logger.LogInformation("════════════════════════════════════════════");
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
                // 模拟模式：直接设置为连接状态
                _isConnected = true;
                _logger.LogInformation("✅ 模拟PLC连接成功");
                _logger.LogInformation("📊 初始化设备数据:");
                _logger.LogInformation($"  - 坐标: X={_dataCache["D100"]}, Y={_dataCache["D102"]}, Z={_dataCache["D104"]}");
                _logger.LogInformation($"  - 配方: {_dataCache["D400"]}");
                _logger.LogInformation($"  - 批次: {_dataCache["D420"]}");
                
                // 启动模拟数据生成器
                StartSimulation();
            }
            else
            {
                // 实际PLC连接（暂未实现）
                _logger.LogWarning("⚠️ 实际PLC连接模式暂未实现，自动切换到模拟模式");
                _useSimulation = true;
                _isConnected = true;
                StartSimulation();
            }

            // 启动数据采集定时器
            _dataCollectionTimer = new Timer(
                async _ => await CollectDataAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(_pollingInterval));

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
            _cancellationTokenSource?.Cancel();

            _isConnected = false;

            _logger.LogInformation("PLC数据采集服务已停止");

            await Task.CompletedTask;
        }

        #endregion

        #region IPlcDataProvider实现

        /// <summary>
        /// 获取设备数据
        /// </summary>
        public DiceDeviceData? GetDeviceData(int deviceId)
        {
            try
            {
                if (!_isConnected)
                {
                    _logger.LogWarning($"PLC未连接，无法获取设备{deviceId}数据");
                    return null;
                }

                var deviceData = new DiceDeviceData
                {
                    DeviceId = deviceId,
                    // 坐标数据
                    CurrentX = GetCachedValue<float>("D100"),
                    CurrentY = GetCachedValue<float>("D102"),
                    CurrentZ = GetCachedValue<float>("D104"),
                    CurrentTheta = GetCachedValue<float>("D106"),

                    // 工艺数据
                    ProcessSpeed = GetCachedValue<float>("D200"),
                    ProcessPressure = GetCachedValue<float>("D202"),
                    ProcessTemperature = GetCachedValue<float>("D204"),

                    // 划刀信息
                    KnifeType = GetCachedValue<int>("D300"),
                    ScribeKnifeUsageCount = GetCachedValue<int>("D302"),
                    BreakKnifeUsageCount = GetCachedValue<int>("D304"),

                    // 材料信息
                    CurrentRecipeId = GetCachedValue<string>("D400", 20),
                    CurrentLotId = GetCachedValue<string>("D420", 20),
                    CurrentWaferId = GetCachedValue<string>("D440", 20),
                    CurrentSlotNumber = GetCachedValue<int>("D460"),

                    // 生产统计
                    TotalProcessedCount = GetCachedValue<int>("D500"),
                    GoodCount = GetCachedValue<int>("D502"),
                    NgCount = GetCachedValue<int>("D504"),

                    // 时间戳
                    LastUpdateTime = DateTime.Now
                };

                return deviceData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取设备{deviceId}数据失败");
                return null;
            }
        }

        /// <summary>
        /// 读取单个PLC地址（模拟）
        /// </summary>
        public T? ReadPlcValue<T>(string address) where T : struct
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址{address}");
                return null;
            }

            try
            {
                // 从缓存中读取模拟数据
                if (_dataCache.TryGetValue(address, out var value))
                {
                    if (value is T typedValue)
                    {
                        return typedValue;
                    }
                    
                    // 尝试转换类型
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch
                    {
                        _logger.LogWarning($"无法将地址{address}的值转换为类型{typeof(T)}");
                    }
                }

                // 返回默认值
                return default(T);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"读取PLC地址{address}异常");
                return null;
            }
        }

        /// <summary>
        /// 写入单个PLC地址（模拟）
        /// </summary>
        public bool WritePlcValue<T>(string address, T value) where T : struct
        {
            if (!_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址{address}");
                return false;
            }

            try
            {
                _dataCache[address] = value!;
                _logger.LogDebug($"写入PLC地址 {address} = {value}");
                
                // 触发相关事件处理
                HandleWriteEvent(address, value);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"写入PLC地址{address}异常");
                return false;
            }
        }

        /// <summary>
        /// 批量读取PLC数据
        /// </summary>
        public Dictionary<string, object> ReadBatch(List<PlcTag> tags)
        {
            var results = new Dictionary<string, object>();

            if (!_isConnected)
            {
                _logger.LogWarning("PLC未连接，无法批量读取");
                return results;
            }

            foreach (var tag in tags)
            {
                try
                {
                    if (_dataCache.TryGetValue(tag.Address, out var value))
                    {
                        results[tag.Name] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"读取标签{tag.Name}失败");
                }
            }

            return results;
        }

        /// <summary>
        /// 批量写入PLC数据
        /// </summary>
        public bool WriteBatch(Dictionary<string, object> values)
        {
            if (!_isConnected)
            {
                _logger.LogWarning("PLC未连接，无法批量写入");
                return false;
            }

            bool allSuccess = true;

            foreach (var kvp in values)
            {
                try
                {
                    var tag = _dataMapper.GetTagByName(kvp.Key);
                    if (tag == null)
                    {
                        _logger.LogWarning($"未找到标签{kvp.Key}的映射");
                        allSuccess = false;
                        continue;
                    }

                    _dataCache[tag.Address] = kvp.Value;
                    HandleWriteEvent(tag.Address, kvp.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"写入标签{kvp.Key}失败");
                    allSuccess = false;
                }
            }

            return allSuccess;
        }

        /// <summary>
        /// 获取PLC连接状态
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 异步连接PLC（模拟）
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_isConnected)
                {
                    _logger.LogDebug("PLC已经连接");
                    return true;
                }

                // 模拟连接延迟
                await Task.Delay(500, cancellationToken);
                
                _isConnected = true;
                _logger.LogInformation("✅ 模拟PLC连接成功");
                
                // 启动模拟
                StartSimulation();
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接PLC失败");
                return false;
            }
        }

        /// <summary>
        /// 异步断开PLC连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _isConnected = false;
                _simulationTimer?.Dispose();
                _logger.LogInformation("已断开PLC连接");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开PLC连接失败");
            }
        }

        /// <summary>
        /// 读取SVID（状态变量）
        /// </summary>
        public async Task<object?> ReadSvidAsync(uint svid, string? address = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            try
            {
                var plcAddress = address ?? GetSvidAddress(svid);
                if (string.IsNullOrEmpty(plcAddress))
                {
                    _logger.LogWarning($"未找到SVID {svid} 的映射地址");
                    return null;
                }

                if (_dataCache.TryGetValue(plcAddress, out var value))
                {
                    return value;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"读取SVID {svid} 失败");
                return null;
            }
        }

        /// <summary>
        /// 写入ECID（设备常量）
        /// </summary>
        public async Task<bool> WriteEcidAsync(uint ecid, string? address, object value, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            try
            {
                var plcAddress = address ?? GetEcidAddress(ecid);
                if (string.IsNullOrEmpty(plcAddress))
                {
                    _logger.LogWarning($"未找到ECID {ecid} 的映射地址");
                    return false;
                }

                _dataCache[plcAddress] = value;
                _logger.LogInformation($"设置ECID {ecid} (地址:{plcAddress}) = {value}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"写入ECID {ecid} 失败");
                return false;
            }
        }

        // 异步读取方法（模拟实现）
        public async Task<short> ReadInt16Async(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<short>(address) ?? 0;
        }

        public async Task<int> ReadInt32Async(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<int>(address) ?? 0;
        }

        public async Task<float> ReadFloatAsync(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<float>(address) ?? 0f;
        }

        public async Task<string> ReadStringAsync(string address, int length = 32, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_dataCache.TryGetValue(address, out var value) && value is string str)
            {
                return str;
            }
            return string.Empty;
        }

        public async Task<byte> ReadByteAsync(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return (byte)(ReadPlcValue<byte>(address) ?? 0);
        }

        public async Task<ushort> ReadUInt16Async(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<ushort>(address) ?? 0;
        }

        public async Task<uint> ReadUInt32Async(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<uint>(address) ?? 0;
        }

        public async Task<bool> ReadBoolAsync(string address, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return ReadPlcValue<bool>(address) ?? false;
        }

        /// <summary>
        /// 执行PLC命令
        /// </summary>
        public async Task<PlcExecutionResult> ExecuteAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.Now;
            await Task.Yield();

            try
            {
                _logger.LogInformation($"═══ 执行PLC命令: {command} ═══");
                bool success = false;
                var resultData = new Dictionary<string, object>();

                // 模拟命令执行
                switch (command.ToUpper())
                {
                    case "START":
                        _simulationState.IsProcessing = true;
                        _simulationState.ProcessState = "Processing";
                        _dataCache["M300"] = true;
                        success = true;
                        resultData["Status"] = "Processing";
                        _logger.LogInformation("▶️ 设备开始处理");
                        break;

                    case "STOP":
                        _simulationState.IsProcessing = false;
                        _simulationState.ProcessState = "Stopped";
                        _dataCache["M301"] = true;
                        success = true;
                        resultData["Status"] = "Stopped";
                        _logger.LogInformation("⏹️ 设备停止");
                        break;

                    case "PAUSE":
                        _simulationState.IsPaused = true;
                        _simulationState.ProcessState = "Paused";
                        _dataCache["M302"] = true;
                        success = true;
                        resultData["Status"] = "Paused";
                        _logger.LogInformation("⏸️ 设备暂停");
                        break;

                    case "RESUME":
                        _simulationState.IsPaused = false;
                        _simulationState.ProcessState = "Processing";
                        _dataCache["M303"] = true;
                        success = true;
                        resultData["Status"] = "Processing";
                        _logger.LogInformation("▶️ 设备恢复");
                        break;

                    case "RESET":
                        ResetSimulation();
                        _dataCache["M304"] = true;
                        success = true;
                        resultData["Status"] = "Ready";
                        _logger.LogInformation("🔄 设备复位");
                        break;

                    case "PP-SELECT":
                        if (parameters.TryGetValue("RecipeId", out var recipeId))
                        {
                            _dataCache["D600"] = recipeId.ToString() ?? "";
                            _dataCache["D400"] = recipeId.ToString() ?? "";  // 同时更新当前配方
                            _simulationState.CurrentRecipeId = recipeId.ToString() ?? "";
                            success = true;
                            resultData["RecipeId"] = recipeId.ToString() ?? "";
                            _logger.LogInformation($"📋 选择配方: {recipeId}");
                        }
                        break;

                    case "SCANSLOTMAPPING":
                        _dataCache["M305"] = true;
                        _simulationState.SlotMappingComplete = true;
                        success = true;
                        resultData["Action"] = "SlotMappingStarted";
                        _logger.LogInformation("🔍 开始槽位映射");
                        
                        // 模拟槽位映射结果
                        Task.Run(async () =>
                        {
                            await Task.Delay(2000); // 模拟扫描时间
                            TriggerEvent(11011); // SlotMapEnd事件
                            _logger.LogInformation("✅ 槽位映射完成");
                        });
                        break;

                    case "CASSETTESTART":
                        _dataCache["M306"] = true;
                        _simulationState.CassetteStarted = true;
                        success = true;
                        resultData["Action"] = "CassetteStarted";
                        _logger.LogInformation("📦 Cassette开始处理");
                        break;

                    case "FRAMESTART":
                        _dataCache["M307"] = true;
                        _simulationState.FrameStarted = true;
                        _simulationState.CurrentFrameNumber++;
                        success = true;
                        resultData["Action"] = "FrameStarted";
                        resultData["FrameNumber"] = _simulationState.CurrentFrameNumber;
                        _logger.LogInformation($"🔲 Frame {_simulationState.CurrentFrameNumber} 开始处理");
                        break;

                    default:
                        return PlcExecutionResult.CreateFailure($"Unknown command: {command}",
                            DateTime.Now - startTime);
                }

                return success
                    ? PlcExecutionResult.CreateSuccess(resultData, DateTime.Now - startTime)
                    : PlcExecutionResult.CreateFailure($"Failed to execute command: {command}",
                        DateTime.Now - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行PLC命令 {command} 异常");
                return PlcExecutionResult.CreateFailure($"Exception: {ex.Message}",
                    DateTime.Now - startTime);
            }
        }

        public async Task<PlcExecutionResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(command, new Dictionary<string, object>(), cancellationToken);
        }

        /// <summary>
        /// 监控PLC事件（模拟）
        /// </summary>
        public void MonitorEvents(Dictionary<uint, string> ceidAddress, Action<uint> onEventTriggered)
        {
            if (ceidAddress == null || onEventTriggered == null)
            {
                _logger.LogWarning("事件映射或处理器为空，无法启动监控");
                return;
            }

            _simulationState.EventMapping = ceidAddress;
            _simulationState.EventCallback = onEventTriggered;
            
            _logger.LogInformation($"📡 开始监控 {ceidAddress.Count} 个PLC事件（模拟模式）");
        }

        /// <summary>
        /// 停止事件监控
        /// </summary>
        public async Task StopEventMonitoringAsync()
        {
            _simulationState.EventMapping = null;
            _simulationState.EventCallback = null;
            _logger.LogInformation("事件监控已停止");
            await Task.CompletedTask;
        }

        #endregion

        #region 模拟数据生成

        /// <summary>
        /// 初始化模拟数据
        /// </summary>
        private void InitializeSimulationData()
        {
            // 初始化坐标数据
            _dataCache["D100"] = 100.0f;  // X坐标
            _dataCache["D102"] = 200.0f;  // Y坐标
            _dataCache["D104"] = 50.0f;   // Z坐标
            _dataCache["D106"] = 0.0f;    // θ角度

            // 初始化工艺数据
            _dataCache["D200"] = 80.0f;   // 速度
            _dataCache["D202"] = 2.5f;    // 压力
            _dataCache["D204"] = 25.0f;   // 温度

            // 初始化刀具信息
            _dataCache["D300"] = 1;       // 刀具类型
            _dataCache["D302"] = 1234;    // 划刀使用次数
            _dataCache["D304"] = 567;     // 裂刀使用次数

            // 初始化材料信息
            _dataCache["D400"] = "RECIPE001";    // 配方ID
            _dataCache["D420"] = "LOT20250902";  // 批次ID
            _dataCache["D440"] = "WAFER001";     // Wafer ID
            _dataCache["D460"] = 1;              // 槽位号

            // 初始化生产统计
            _dataCache["D500"] = 100;     // 总处理数
            _dataCache["D502"] = 95;      // 良品数
            _dataCache["D504"] = 5;       // 不良品数

            // 初始化系统状态
            _dataCache["M200"] = true;    // 系统就绪
            _dataCache["M201"] = false;   // 处理中
            _dataCache["M202"] = false;   // 报警激活
            _dataCache["M205"] = true;    // 自动模式

            // 初始化标准SVID对应的地址
            _dataCache["D280"] = "";      // EventsEnabled
            _dataCache["D490"] = "";      // AlarmsEnabled
            _dataCache["D491"] = "";      // AlarmsSet
            _dataCache["D672"] = DateTime.Now.ToString("yyyyMMddHHmmss"); // Clock
            _dataCache["D720"] = 2;       // ControlMode (2=OnlineRemote)
            _dataCache["D721"] = 5;       // ControlState (5=OnlineRemote)

            _logger.LogDebug("模拟数据初始化完成");
        }

        /// <summary>
        /// 启动模拟
        /// </summary>
        private void StartSimulation()
        {
            if (!_useSimulation) return;

            _simulationTimer = new Timer(
                _ => UpdateSimulationData(),
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)); // 每秒更新一次模拟数据

            _logger.LogInformation("🎮 模拟数据生成器已启动");
        }

        /// <summary>
        /// 更新模拟数据
        /// </summary>
        private void UpdateSimulationData()
        {
            try
            {
                // 更新时钟
                _dataCache["D672"] = DateTime.Now.ToString("yyyyMMddHHmmss");

                // 如果正在处理，更新坐标位置
                if (_simulationState.IsProcessing && !_simulationState.IsPaused)
                {
                    // 模拟X轴移动（正弦波动）
                    var time = DateTime.Now.Second + (DateTime.Now.Millisecond / 1000.0);
                    var currentX = 250.0f + (float)(Math.Sin(time * 0.1) * 100);
                    _dataCache["D100"] = currentX;

                    // 模拟Y轴移动（余弦波动）
                    var currentY = 250.0f + (float)(Math.Cos(time * 0.1) * 100);
                    _dataCache["D102"] = currentY;

                    // 模拟Z轴轻微波动
                    var currentZ = 50.0f + ((float)(_random.NextDouble() - 0.5) * 2);
                    _dataCache["D104"] = currentZ;

                    // 模拟温度波动
                    var temp = GetCachedValue<float>("D204");
                    temp += (float)((_random.NextDouble() - 0.5) * 0.2);
                    _dataCache["D204"] = Math.Max(20, Math.Min(30, temp));

                    // 模拟压力波动
                    var pressure = GetCachedValue<float>("D202");
                    pressure += (float)((_random.NextDouble() - 0.5) * 0.1);
                    _dataCache["D202"] = Math.Max(2.0f, Math.Min(3.0f, pressure));

                    // 每10秒增加处理计数
                    if (DateTime.Now.Second % 10 == 0)
                    {
                        var total = GetCachedValue<int>("D500");
                        total++;
                        _dataCache["D500"] = total;

                        // 95%良率
                        if (_random.Next(100) < 95)
                        {
                            var good = GetCachedValue<int>("D502");
                            _dataCache["D502"] = good + 1;
                        }
                        else
                        {
                            var ng = GetCachedValue<int>("D504");
                            _dataCache["D504"] = ng + 1;
                        }

                        // 增加刀具使用次数
                        var scribeCount = GetCachedValue<int>("D302");
                        _dataCache["D302"] = scribeCount + 1;

                        _logger.LogDebug($"生产计数更新: 总数={total+1}");
                    }

                    // 模拟Frame处理完成事件（每15秒）
                    if (_simulationState.FrameStarted && DateTime.Now.Second % 15 == 0)
                    {
                        TriggerEvent(11013); // Frame End事件
                        _simulationState.FrameStarted = false;
                        
                        // 更新Wafer ID
                        var waferId = GetCachedValue<string>("D440", 20);
                        var waferNum = int.Parse(waferId.Replace("WAFER", "")) + 1;
                        _dataCache["D440"] = $"WAFER{waferNum:D3}";
                        _dataCache["D460"] = GetCachedValue<int>("D460") + 1; // 更新槽位号
                        
                        _logger.LogInformation($"✅ Frame {_simulationState.CurrentFrameNumber} 处理完成");
                    }
                }

                // 更新系统状态
                _dataCache["M201"] = _simulationState.IsProcessing;

                // 模拟随机报警（0.5%概率）
                if (_random.Next(200) < 1 && !_simulationState.HasAlarm)
                {
                    _simulationState.HasAlarm = true;
                    _dataCache["M202"] = true;
                    var alarmId = 12000 + _random.Next(10); // 随机报警ID
                    TriggerEvent((uint)alarmId);
                    _logger.LogWarning($"⚠️ 模拟报警触发 ALID={alarmId}");
                }
                else if (_simulationState.HasAlarm && _random.Next(100) < 5)
                {
                    // 5%概率清除报警
                    _simulationState.HasAlarm = false;
                    _dataCache["M202"] = false;
                    _logger.LogInformation("✅ 模拟报警清除");
                }

                // 定期触发一些生产事件
                if (_simulationState.IsProcessing && !_simulationState.IsPaused)
                {
                    if (DateTime.Now.Second == 30)
                    {
                        TriggerEvent(11006); // PictureSearch 图像搜索
                    }
                    else if (DateTime.Now.Second == 45)
                    {
                        TriggerEvent(11007); // ParaPosition 图像对位
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新模拟数据异常");
            }
        }

        /// <summary>
        /// 触发事件
        /// </summary>
        private void TriggerEvent(uint ceid)
        {
            if (_simulationState.EventCallback != null)
            {
                Task.Run(() =>
                {
                    try
                    {
                        _simulationState.EventCallback(ceid);
                        _logger.LogInformation($"🎯 触发事件 CEID={ceid}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"处理事件 CEID={ceid} 异常");
                    }
                });
            }
        }

        /// <summary>
        /// 处理写入事件
        /// </summary>
        private void HandleWriteEvent(string address, object value)
        {
            // 根据写入的地址触发相应的事件
            switch (address)
            {
                case "M300": // START
                    if ((bool)value) TriggerEvent(11004); // ProcessStart
                    break;
                case "M301": // STOP
                    if ((bool)value) TriggerEvent(11005); // ProcessEnd
                    break;
                case "M305": // ScanSlotMapping
                    if ((bool)value)
                    {
                        // 槽位映射会在命令执行中延迟触发
                    }
                    break;
                case "M306": // CassetteStart
                    if ((bool)value) TriggerEvent(11014); // CST.ST
                    break;
                case "M307": // FrameStart
                    if ((bool)value) TriggerEvent(11012); // FrameStart
                    break;
                case "D400": // Recipe切换
                case "D600": // Recipe选择
                    TriggerEvent(11003); // PPSelected
                    break;
            }
        }

        /// <summary>
        /// 重置模拟状态
        /// </summary>
        private void ResetSimulation()
        {
            _simulationState = new SimulationState();
            InitializeSimulationData();
            _logger.LogInformation("🔄 模拟状态已重置");
        }

        #endregion

        #region 私有方法

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
                var tags = _dataMapper.GetAllActiveTags();
                var batchData = ReadBatch(tags);

                // 触发数据变更事件
                if (batchData.Count > 0)
                {
                    OnDataUpdated(batchData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据采集异常");
            }
        }

        /// <summary>
        /// 从缓存获取值
        /// </summary>
        private T GetCachedValue<T>(string address, int length = 0)
        {
            if (_dataCache.TryGetValue(address, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    // 转换失败，返回默认值
                }
            }

            return default!;
        }

        /// <summary>
        /// 触发数据更新事件
        /// </summary>
        private void OnDataUpdated(Dictionary<string, object> data)
        {
            DataUpdated?.Invoke(this, new PlcDataEventArgs { Data = data });
        }

        #endregion

        #region 事件

        /// <summary>
        /// 数据更新事件
        /// </summary>
        public event EventHandler<PlcDataEventArgs>? DataUpdated;

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取SVID对应的PLC地址
        /// </summary>
        private string GetSvidAddress(uint svid)
        {
            return svid switch
            {
                // 使用Common中的标准定义
                280 => "D280",   // EventsEnabled
                490 => "D490",   // AlarmsEnabled
                491 => "D491",   // AlarmsSet
                672 => "D672",   // Clock
                720 => "D720",   // ControlMode
                721 => "D721",   // ControlState
                
                // 自定义SVID (10001-10016)
                10001 => "D10001", // PortID
                10002 => "D10002", // CassetteID
                10003 => "D420",   // LotID
                10004 => "D400",   // PPID
                10005 => "D10005", // CassetteSlotMap
                10006 => "D500",   // ProcessedCount
                10007 => "D300",   // KnifeModel
                10008 => "D302",   // UseNO (ScribeKnifeUsageCount)
                10009 => "D10009", // UseMAXNO
                10010 => "D10010", // ProgressBar
                10011 => "D10011", // BARNO
                10012 => "D10012", // CurrentBAR
                10013 => "D10013", // RFID
                10014 => "D10014", // QRContent
                10015 => "D10015", // GetFrameLY
                10016 => "D10016", // PutFrameLY

                // 坐标状态变量
                1001 => "D100",  // CurrentX
                1002 => "D102",  // CurrentY
                1003 => "D104",  // CurrentZ
                1004 => "D106",  // CurrentTheta

                // 工艺状态变量
                2001 => "D200",  // ProcessSpeed
                2002 => "D202",  // ProcessPressure
                2003 => "D204",  // ProcessTemperature

                _ => ""
            };
        }

        /// <summary>
        /// 获取ECID对应的PLC地址
        /// </summary>
        private string GetEcidAddress(uint ecid)
        {
            return ecid switch
            {
                // 使用Common中的标准定义
                250 => "D250",   // EstablishCommunicationsTimeout
                310 => "D310",   // AnnotateEventReport
                311 => "D311",   // ConfigEvents
                675 => "D675",   // TimeFormat

                _ => ""
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _dataCollectionTimer?.Dispose();
            _simulationTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// 模拟状态类
    /// </summary>
    internal class SimulationState
    {
        public bool IsProcessing { get; set; }
        public bool IsPaused { get; set; }
        public bool HasAlarm { get; set; }
        public string ProcessState { get; set; } = "Ready";
        public string CurrentRecipeId { get; set; } = "RECIPE001";
        public bool SlotMappingComplete { get; set; }
        public bool CassetteStarted { get; set; }
        public bool FrameStarted { get; set; }
        public int CurrentFrameNumber { get; set; } = 0;
        public Dictionary<uint, string>? EventMapping { get; set; }
        public Action<uint>? EventCallback { get; set; }
    }

    /// <summary>
    /// PLC数据事件参数
    /// </summary>
    public class PlcDataEventArgs : EventArgs
    {
        public Dictionary<string, object> Data { get; set; } = new();
    }
}
