using DiceEquipmentSystem.Core.Models;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Mapping;
using DiceEquipmentSystem.PLC.Models;
using HslCommunication;
using HslCommunication.Profinet.Melsec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC数据提供者实现类
    /// 基于HslCommunication的MC协议实现与三菱PLC的实时通信
    /// </summary>
    public class PlcDataProviderImpl : IPlcDataProvider, IHostedService, IDisposable
    {
        #region 私有字段
        /// <summary>
        /// PLC读写锁
        /// </summary>
        private readonly ReaderWriterLockSlim _plcLock = new();
        private readonly ILogger<PlcDataProviderImpl> _logger;
        private readonly IConfiguration _configuration;
        private readonly PlcConnectionManager _connectionManager;
        private readonly PlcDataMapper _dataMapper;

        /// <summary>MC协议客户端</summary>
        private MelsecMcNet? _mcClient;

        /// <summary>数据缓存</summary>
        private readonly ConcurrentDictionary<string, object> _dataCache;

        /// <summary>数据采集定时器</summary>
        private Timer? _dataCollectionTimer;

        /// <summary>连接状态</summary>
        private volatile bool _isConnected;

        /// <summary>采集周期(毫秒)</summary>
        private readonly int _pollingInterval;

        /// <summary>重连间隔(毫秒)</summary>
        private readonly int _reconnectInterval = 5000;

        /// <summary>取消令牌源</summary>
        private CancellationTokenSource? _cancellationTokenSource;

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

            _logger.LogInformation($"PLC数据提供者已初始化，采集周期: {_pollingInterval}ms");
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

            // 初始化PLC连接
            await InitializePlcConnectionAsync();
            // 启动重连任务
            _ = Task.Run(async () => await ReconnectAsync());
            // 启动数据采集定时器
            _dataCollectionTimer = new Timer(
                async _ => await CollectDataAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(_pollingInterval));

            _logger.LogInformation("PLC数据采集服务已启动");
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止PLC数据采集服务...");

            _dataCollectionTimer?.Dispose();
            _cancellationTokenSource?.Cancel();

            DisconnectFromPlc();

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
        /// 读取单个PLC地址
        /// </summary>
        public T? ReadPlcValue<T>(string address) where T : struct
        {
            if (_mcClient == null || !_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法读取地址{address}");
                return null;
            }
            _plcLock.EnterReadLock();
            try
            {
                OperateResult<T> result = typeof(T) switch
                {
                    Type t when t == typeof(bool) => _mcClient.ReadBool(address) as OperateResult<T>,
                    Type t when t == typeof(short) => _mcClient.ReadInt16(address) as OperateResult<T>,
                    Type t when t == typeof(int) => _mcClient.ReadInt32(address) as OperateResult<T>,
                    Type t when t == typeof(float) => _mcClient.ReadFloat(address) as OperateResult<T>,
                    Type t when t == typeof(double) => _mcClient.ReadDouble(address) as OperateResult<T>,
                    _ => throw new NotSupportedException($"不支持的数据类型: {typeof(T)}")
                };

                if (result?.IsSuccess == true)
                {
                    return result.Content;
                }
                _isConnected = false;//读取失败说明连接已断开
                _logger.LogWarning($"读取PLC地址{address}失败: {result?.Message}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"读取PLC地址{address}异常");
                return null;
            }finally
            {
                _plcLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 写入单个PLC地址
        /// </summary>
        public bool WritePlcValue<T>(string address, T value) where T : struct
        {
            if (_mcClient == null || !_isConnected)
            {
                _logger.LogWarning($"PLC未连接，无法写入地址{address}");
                return false;
            }
            if (_plcLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(100)))
            {
                try
                {
                    OperateResult result = value switch
                    {
                        bool boolValue => _mcClient.Write(address, boolValue),
                        short shortValue => _mcClient.Write(address, shortValue),
                        int intValue => _mcClient.Write(address, intValue),
                        float floatValue => _mcClient.Write(address, floatValue),
                        double doubleValue => _mcClient.Write(address, doubleValue),
                        _ => throw new NotSupportedException($"不支持的数据类型: {typeof(T)}")
                    };

                    if (result?.IsSuccess == true)
                    {
                        _logger.LogDebug($"成功写入PLC地址{address}，值: {value}");
                        return true;
                    }

                    _logger.LogWarning($"写入PLC地址{address}失败: {result?.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"写入PLC地址{address}异常");
                    return false;
                }
                finally
                {
                    _plcLock.ExitWriteLock();
                }
            }
            return false;
            
        }

        /// <summary>
        /// 批量读取PLC数据   
        /// </summary>
        public Dictionary<string, object> ReadBatch(List<PlcTag> tags)
        {
            var results = new Dictionary<string, object>();

            if (_mcClient == null || !_isConnected)
            {
                _logger.LogWarning("PLC未连接，无法批量读取");
                return results;
            }

            foreach (var tag in tags)
            {
                try
                {
                    object? value = tag.DataType switch
                    {
                        PlcDataType.Bool => ReadPlcValue<bool>(tag.Address),
                        PlcDataType.Int16 => ReadPlcValue<short>(tag.Address),
                        PlcDataType.Int32 => ReadPlcValue<int>(tag.Address),
                        PlcDataType.Float => ReadPlcValue<float>(tag.Address),
                        PlcDataType.String => ReadString(tag.Address, tag.Length),
                        _ => null
                    };

                    if (value != null)
                    {
                        results[tag.Name] = value;
                        _dataCache[tag.Address] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"读取标签{tag.Name}失败");
                }
            }

            return results;
        }

        public async Task ReadAllAddr()
        {
           // _plcLock.EnterReadLock();
            try
            {
                var listF = await _mcClient.ReadBoolAsync("F0", 120);
                var listM = await _mcClient.ReadBoolAsync("M0", 768);
                var listD = await _mcClient.ReadInt16Async("D0", 18432);
                var listL = await _mcClient.ReadBoolAsync("L0", 128);
                var listX = await _mcClient.ReadBoolAsync("X0", 0x2ff);
                var listSM = await _mcClient.ReadBoolAsync("SM0", 4080);
            }
            catch (Exception ex)
            {

            }
            finally
            {
               // _plcLock.ExitReadLock();
            }
            
        }
        /// <summary>
        /// 批量写入PLC数据
        /// </summary>
        public bool WriteBatch(Dictionary<string, object> values)
        {
            if (_mcClient == null || !_isConnected)
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

                    bool success = kvp.Value switch
                    {
                        bool boolValue => WritePlcValue(tag.Address, boolValue),
                        short shortValue => WritePlcValue(tag.Address, shortValue),
                        int intValue => WritePlcValue(tag.Address, intValue),
                        float floatValue => WritePlcValue(tag.Address, floatValue),
                        string stringValue => WriteString(tag.Address, stringValue, tag.Length),
                        _ => false
                    };

                    if (!success)
                    {
                        allSuccess = false;
                    }
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
        /// 异步连接PLC
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

                await InitializePlcConnectionAsync();
                return _isConnected;
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
                DisconnectFromPlc();
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
        /// <param name="svid">状态变量ID</param>
        /// <param name="address">PLC地址（可选）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>状态变量值</returns>
        public async Task<object?> ReadSvidAsync(uint svid, string? address = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield(); // 确保异步

            try
            {
                // 根据SVID映射到PLC地址
                var plcAddress = address ?? GetSvidAddress(svid);
                if (string.IsNullOrEmpty(plcAddress))
                {
                    _logger.LogWarning($"未找到SVID {svid} 的映射地址");
                    return null;
                }

                // 根据SVID类型读取不同的数据
                return svid switch
                {
                    >= 1000 and < 2000 => ReadPlcValue<bool>(plcAddress),     // 布尔型状态
                    >= 2000 and < 3000 => ReadPlcValue<float>(plcAddress),    // 浮点型参数
                    >= 3000 and < 4000 => ReadPlcValue<int>(plcAddress),      // 整型计数
                    >= 4000 and < 5000 => ReadString(plcAddress, 20),         // 字符串ID
                    _ => ReadPlcValue<int>(plcAddress)                        // 默认整型
                };
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
        /// <param name="ecid">设备常量ID</param>
        /// <param name="address">PLC地址（可选）</param>
        /// <param name="value">要写入的值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入是否成功</returns>
        public async Task<bool> WriteEcidAsync(uint ecid, string? address, object value, CancellationToken cancellationToken = default)
        {
            await Task.Yield(); // 确保异步

            try
            {
                // 根据ECID映射到PLC地址
                var plcAddress = address ?? GetEcidAddress(ecid);
                if (string.IsNullOrEmpty(plcAddress))
                {
                    _logger.LogWarning($"未找到ECID {ecid} 的映射地址");
                    return false;
                }

                // 根据值类型进行写入
                return value switch
                {
                    bool boolValue => WritePlcValue(plcAddress, boolValue),
                    short shortValue => WritePlcValue(plcAddress, shortValue),
                    int intValue => WritePlcValue(plcAddress, intValue),
                    float floatValue => WritePlcValue(plcAddress, floatValue),
                    double doubleValue => WritePlcValue(plcAddress, doubleValue),
                    string stringValue => WriteString(plcAddress, stringValue, 20),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"写入ECID {ecid} 失败");
                return false;
            }
        }

        /// <summary>
        /// 执行PLC命令（带参数）
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="parameters">命令参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>执行结果</returns>
        public async Task<PlcExecutionResult> ExecuteAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.Now;
            await Task.Yield(); // 确保异步

            try
            {
                _logger.LogInformation($"执行PLC命令: {command}");
                bool success = false;
                var resultData = new Dictionary<string, object>();

                // 根据命令类型执行不同的操作
                switch (command.ToUpper())
                {
                    case "START":
                        success = WritePlcValue("M300", true);
                        if (success) resultData["Status"] = "Processing";
                        break;

                    case "STOP":
                        success = WritePlcValue("M301", true);
                        if (success) resultData["Status"] = "Stopped";
                        break;

                    case "PAUSE":
                        success = WritePlcValue("M302", true);
                        if (success) resultData["Status"] = "Paused";
                        break;

                    case "RESUME":
                        success = WritePlcValue("M303", true);
                        if (success) resultData["Status"] = "Processing";
                        break;

                    case "RESET":
                        success = WritePlcValue("M304", true);
                        if (success) resultData["Status"] = "Ready";
                        break;

                    case "PP-SELECT":
                        if (parameters.TryGetValue("RecipeId", out var recipeId))
                        {
                            success = WriteString("D600", recipeId.ToString() ?? "", 20);
                            if (success) resultData["RecipeId"] = recipeId.ToString() ?? "";
                        }
                        else
                        {
                            return PlcExecutionResult.CreateFailure("RecipeId parameter missing",
                                DateTime.Now - startTime);
                        }
                        break;

                    case "SCANSLOTMAPPING":
                        success = WritePlcValue("M305", true);
                        if (success) resultData["Action"] = "SlotMappingStarted";
                        break;

                    case "CASSETTESTART":
                        success = WritePlcValue("M306", true);
                        if (success) resultData["Action"] = "CassetteStarted";
                        break;

                    case "FRAMESTART":
                        success = WritePlcValue("M307", true);
                        if (success) resultData["Action"] = "FrameStarted";
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

        /// <summary>
        /// 执行PLC命令（无参数）
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>执行结果</returns>
        public async Task<PlcExecutionResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(command, new Dictionary<string, object>(), cancellationToken);
        }

        /// <summary>
        /// 监控PLC事件
        /// </summary>
        /// <param name="ceidAddress">CEID与PLC地址映射字典</param>
        /// <param name="onEventTriggered">事件触发时的回调函数</param>
        public void MonitorEvents(Dictionary<uint, string> ceidAddress, Action<uint> onEventTriggered)
        {
            if (ceidAddress == null || onEventTriggered == null)
            {
                _logger.LogWarning("事件映射或处理器为空，无法启动监控");
                return;
            }

            try
            {
                // 创建事件监控任务
                _ = Task.Run(async () =>
                {
                    var previousValues = new Dictionary<uint, bool>();

                    // 初始化所有监控点的状态
                    foreach (var kvp in ceidAddress)
                    {
                        previousValues[kvp.Key] = false;
                    }

                    _logger.LogInformation($"开始监控 {ceidAddress.Count} 个PLC事件");

                    while (!(_cancellationTokenSource?.IsCancellationRequested ?? true))
                    {
                        try
                        {
                            foreach (var mapping in ceidAddress)
                            {
                                var ceid = mapping.Key;
                                var plcAddress = mapping.Value;

                                // 读取当前PLC地址的值
                                var currentValue = ReadPlcValue<bool>(plcAddress) ?? false;

                                // 检测上升沿（从false变为true）
                                if (previousValues.ContainsKey(ceid))
                                {
                                    if (!previousValues[ceid] && currentValue)
                                    {
                                        _logger.LogInformation($"检测到事件触发 - CEID: {ceid}, PLC地址: {plcAddress}");

                                        // 异步调用回调函数，避免阻塞监控循环
                                        _ = Task.Run(() =>
                                        {
                                            try
                                            {
                                                onEventTriggered(ceid);
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, $"处理事件 CEID: {ceid} 的回调函数时发生异常");
                                            }
                                        });
                                    }
                                }

                                // 更新前一个值
                                previousValues[ceid] = currentValue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "监控PLC事件时发生异常");
                        }

                        // 监控周期
                        await Task.Delay(100, _cancellationTokenSource?.Token ?? CancellationToken.None);
                    }

                    _logger.LogInformation("PLC事件监控已停止");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动事件监控失败");
            }
        }

        /// <summary>
        /// 停止事件监控
        /// </summary>
        public async Task StopEventMonitoringAsync()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                await Task.Delay(200); // 等待监控任务停止
                _logger.LogInformation("事件监控已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止事件监控失败");
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化PLC连接
        /// </summary>
        private async Task InitializePlcConnectionAsync()
        {
            try
            {
                var plcConfig = _connectionManager.GetPlcConfiguration();

                _mcClient = new MelsecMcNet
                {
                    IpAddress = plcConfig.IpAddress,
                    Port = plcConfig.Port,
                    ConnectTimeOut = plcConfig.ConnectTimeout,
                    ReceiveTimeOut = plcConfig.ReceiveTimeout
                };

                // 设置网络编号和站号
                _mcClient.NetworkNumber = plcConfig.NetworkNumber;
                _mcClient.NetworkStationNumber = plcConfig.StationNumber;

                var result = await _mcClient.ConnectServerAsync();

                if (result.IsSuccess)
                {
                    _isConnected = true;
                    _logger.LogInformation($"成功连接到PLC: {plcConfig.IpAddress}:{plcConfig.Port}");
                }
                else
                {
                    _isConnected = false;
                    _logger.LogError($"连接PLC失败: {result.Message}");

                    
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化PLC连接异常");
                _isConnected = false;
            }
        }

        /// <summary>
        /// 断开PLC连接
        /// </summary>
        private void DisconnectFromPlc()
        {
            try
            {
                _mcClient?.ConnectClose();
                _isConnected = false;
                _logger.LogInformation("已断开PLC连接");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开PLC连接异常");
            }
        }

        /// <summary>
        /// 自动重连
        /// </summary>
        private async Task ReconnectAsync()
        {
            while (!_isConnected && !(_cancellationTokenSource?.IsCancellationRequested ?? true))
            {
                _logger.LogInformation("尝试重新连接PLC...");

                await Task.Delay(_reconnectInterval);
                await InitializePlcConnectionAsync();
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
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                var tags = _dataMapper.GetAllActiveTags();
                var batchData = ReadBatch(tags);
                 //await ReadAllAddr();
                stopwatch.Stop();

                // 触发数据变更事件
                //if (batchData.Count > 0)
                //{
                //    OnDataUpdated(batchData);
                //}
                _logger.LogDebug($"数据采集数量{batchData.Count} 耗时{stopwatch.ElapsedMilliseconds}");
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
            }

            return default!;
        }

        /// <summary>
        /// 读取字符串
        /// </summary>
        private string? ReadString(string address, int length)
        {
            if (_mcClient == null || !_isConnected)
            {
                return null;
            }

            var result = _mcClient.ReadString(address, (ushort)length);
            return result.IsSuccess ? result.Content : null;
        }

        /// <summary>
        /// 写入字符串
        /// </summary>
        private bool WriteString(string address, string value, int maxLength)
        {
            if (_mcClient == null || !_isConnected)
            {
                return false;
            }

            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            var result = _mcClient.Write(address, value);
            return result.IsSuccess;
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
                // 坐标状态变量
                1001 => "D100",  // CurrentX
                1002 => "D102",  // CurrentY
                1003 => "D104",  // CurrentZ
                1004 => "D106",  // CurrentTheta

                // 工艺状态变量
                2001 => "D200",  // ProcessSpeed
                2002 => "D202",  // ProcessPressure
                2003 => "D204",  // ProcessTemperature
                2004 => "D206",  // SpindleSpeed

                // 刀具状态变量
                3001 => "D302",  // ScribeKnifeUsageCount
                3002 => "D304",  // BreakKnifeUsageCount

                // 材料状态变量
                4001 => "D400",  // CurrentRecipeId
                4002 => "D420",  // CurrentLotId
                4003 => "D440",  // CurrentWaferId
                4004 => "D460",  // CurrentSlotNumber

                // 生产统计变量
                5001 => "D500",  // TotalProcessedCount
                5002 => "D502",  // GoodCount
                5003 => "D504",  // NgCount
                5004 => "D506",  // YieldRate

                // 系统状态变量
                6001 => "M200",  // SystemReady
                6002 => "M201",  // Processing
                6003 => "M202",  // AlarmActive
                6004 => "M205",  // AutoMode

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
                // 工艺参数常量
                1001 => "D1000",  // SpeedLimit
                1002 => "D1002",  // PressureLimit
                1003 => "D1004",  // TemperatureLimit

                // 刀具参数常量
                2001 => "D1100",  // ScribeKnifeLifeLimit
                2002 => "D1102",  // BreakKnifeLifeLimit

                // 系统配置常量
                3001 => "D1200",  // CycleTimeLimit
                3002 => "D1202",  // YieldTarget

                _ => ""
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _dataCollectionTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            DisconnectFromPlc();
            _mcClient?.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// PLC数据事件参数
    /// </summary>
    public class PlcDataEventArgs : EventArgs
    {
        public Dictionary<string, object> Data { get; set; } = new();
    }
}
