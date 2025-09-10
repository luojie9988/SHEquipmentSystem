using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HslCommunication.Profinet.Melsec;
using HslCommunication;

namespace DiceEquipmentSystem.PLC.Services
{
    /// <summary>
    /// PLC连接实例
    /// 封装单个PLC的连接管理和数据通信
    /// </summary>
    public class PlcConnectionInstance : IDisposable
    {
        #region 私有字段

        private readonly ILogger _logger;
        private readonly string _plcId;
        private readonly PlcConfiguration _configuration;
        private MelsecMcNet _mcClient;
        private volatile bool _isConnected;
        private readonly object _connectionLock = new();
        private volatile bool _disposed = false;

        // 统计信息
        private int _connectionCount;
        private int _errorCount;
        private DateTime? _lastConnectTime;
        private DateTime? _lastDisconnectTime;
        private string _lastErrorMessage;

        #endregion

        #region 事件定义

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<PlcConnectionStatusEventArgs> ConnectionStatusChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public PlcConnectionInstance(string plcId, PlcConfiguration configuration, ILogger logger)
        {
            _plcId = plcId ?? throw new ArgumentNullException(nameof(plcId));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializePlcClient();
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// PLC ID
        /// </summary>
        public string PlcId => _plcId;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 配置信息
        /// </summary>
        public PlcConfiguration Configuration => _configuration;

        /// <summary>
        /// 连接次数
        /// </summary>
        public int ConnectionCount => _connectionCount;

        /// <summary>
        /// 错误次数
        /// </summary>
        public int ErrorCount => _errorCount;

        /// <summary>
        /// 最后连接时间
        /// </summary>
        public DateTime? LastConnectTime => _lastConnectTime;

        /// <summary>
        /// 最后断开时间
        /// </summary>
        public DateTime? LastDisconnectTime => _lastDisconnectTime;

        /// <summary>
        /// 最后错误消息
        /// </summary>
        public string LastErrorMessage => _lastErrorMessage;

        #endregion

        #region 公共方法

        /// <summary>
        /// 异步连接PLC
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            lock (_connectionLock)
            {
                if (_isConnected)
                {
                    _logger.LogDebug($"PLC {_plcId} 已经连接");
                    return true;
                }
            }

            try
            {
                _logger.LogInformation($"正在连接PLC {_plcId}: {_configuration.IpAddress}:{_configuration.Port}");

                var connectResult = await _mcClient.ConnectServerAsync();

                if (connectResult.IsSuccess)
                {
                    lock (_connectionLock)
                    {
                        _isConnected = true;
                        _lastConnectTime = DateTime.Now;
                        _connectionCount++;
                    }

                    _logger.LogInformation($"成功连接PLC {_plcId}");

                    // 触发连接状态变化事件
                    OnConnectionStatusChanged(true, "连接成功");

                    return true;
                }
                else
                {
                    var errorMsg = $"连接失败: {connectResult.Message}";
                    UpdateErrorInfo(errorMsg);
                    _logger.LogError($"连接PLC {_plcId} 失败: {connectResult.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"连接异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"连接PLC {_plcId} 异常");
                return false;
            }
        }

        /// <summary>
        /// 异步断开PLC连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            lock (_connectionLock)
            {
                if (!_isConnected)
                {
                    _logger.LogDebug($"PLC {_plcId} 已经断开");
                    return;
                }
            }

            try
            {
                _logger.LogInformation($"正在断开PLC {_plcId} 连接");

                var disconnectResult = await _mcClient.ConnectCloseAsync();

                lock (_connectionLock)
                {
                    _isConnected = false;
                    _lastDisconnectTime = DateTime.Now;
                }

                _logger.LogInformation($"PLC {_plcId} 连接已断开");

                // 触发连接状态变化事件
                OnConnectionStatusChanged(false, "连接断开");
            }
            catch (Exception ex)
            {
                var errorMsg = $"断开连接异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"断开PLC {_plcId} 连接异常");
            }
        }

        /// <summary>
        /// 测试连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            try
            {
                if (!_isConnected)
                {
                    return false;
                }

                // 尝试读取一个测试地址来验证连接
                var testResult = await ReadAsync("M0", 1);
                return testResult.IsSuccess;
            }
            catch (Exception ex)
            {
                var errorMsg = $"连接测试异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"PLC {_plcId} 连接测试失败");
                return false;
            }
        }

        /// <summary>
        /// 异步读取数据
        /// </summary>
        public async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult<byte[]>($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.ReadAsync(address, length);
            }
            catch (Exception ex)
            {
                var errorMsg = $"读取数据异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"从PLC {_plcId} 读取数据失败, 地址: {address}");
                return new OperateResult<byte[]>(errorMsg);
            }
        }

        /// <summary>
        /// 异步写入数据
        /// </summary>
        public async Task<OperateResult> WriteAsync(string address, byte[] data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.WriteAsync(address, data);
            }
            catch (Exception ex)
            {
                var errorMsg = $"写入数据异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"向PLC {_plcId} 写入数据失败, 地址: {address}");
                return new OperateResult(errorMsg);
            }
        }

        /// <summary>
        /// 读取布尔值
        /// </summary>
        public async Task<OperateResult<bool>> ReadBoolAsync(string address)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult<bool>($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.ReadBoolAsync(address);
            }
            catch (Exception ex)
            {
                var errorMsg = $"读取布尔值异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"从PLC {_plcId} 读取布尔值失败, 地址: {address}");
                return new OperateResult<bool>(errorMsg);
            }
        }

        /// <summary>
        /// 写入布尔值
        /// </summary>
        public async Task<OperateResult> WriteBoolAsync(string address, bool value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.WriteAsync(address, value);
            }
            catch (Exception ex)
            {
                var errorMsg = $"写入布尔值异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"向PLC {_plcId} 写入布尔值失败, 地址: {address}");
                return new OperateResult(errorMsg);
            }
        }

        /// <summary>
        /// 读取16位整数
        /// </summary>
        public async Task<OperateResult<short>> ReadInt16Async(string address)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult<short>($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.ReadInt16Async(address);
            }
            catch (Exception ex)
            {
                var errorMsg = $"读取Int16异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"从PLC {_plcId} 读取Int16失败, 地址: {address}");
                return new OperateResult<short>(errorMsg);
            }
        }

        /// <summary>
        /// 写入16位整数
        /// </summary>
        public async Task<OperateResult> WriteInt16Async(string address, short value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.WriteAsync(address, value);
            }
            catch (Exception ex)
            {
                var errorMsg = $"写入Int16异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"向PLC {_plcId} 写入Int16失败, 地址: {address}");
                return new OperateResult(errorMsg);
            }
        }

        /// <summary>
        /// 读取32位整数
        /// </summary>
        public async Task<OperateResult<int>> ReadInt32Async(string address)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult<int>($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.ReadInt32Async(address);
            }
            catch (Exception ex)
            {
                var errorMsg = $"读取Int32异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"从PLC {_plcId} 读取Int32失败, 地址: {address}");
                return new OperateResult<int>(errorMsg);
            }
        }

        /// <summary>
        /// 写入32位整数
        /// </summary>
        public async Task<OperateResult> WriteInt32Async(string address, int value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.WriteAsync(address, value);
            }
            catch (Exception ex)
            {
                var errorMsg = $"写入Int32异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"向PLC {_plcId} 写入Int32失败, 地址: {address}");
                return new OperateResult(errorMsg);
            }
        }

        /// <summary>
        /// 读取浮点数
        /// </summary>
        public async Task<OperateResult<float>> ReadFloatAsync(string address)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult<float>($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.ReadFloatAsync(address);
            }
            catch (Exception ex)
            {
                var errorMsg = $"读取Float异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"从PLC {_plcId} 读取Float失败, 地址: {address}");
                return new OperateResult<float>(errorMsg);
            }
        }

        /// <summary>
        /// 写入浮点数
        /// </summary>
        public async Task<OperateResult> WriteFloatAsync(string address, float value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PlcConnectionInstance));

            if (!_isConnected)
            {
                return new OperateResult($"PLC {_plcId} 未连接");
            }

            try
            {
                return await _mcClient.WriteAsync(address, value);
            }
            catch (Exception ex)
            {
                var errorMsg = $"写入Float异常: {ex.Message}";
                UpdateErrorInfo(errorMsg);
                _logger.LogError(ex, $"向PLC {_plcId} 写入Float失败, 地址: {address}");
                return new OperateResult(errorMsg);
            }
        }

        /// <summary>
        /// 批量读取多个地址
        /// </summary>
        public async Task<Dictionary<string, object>> BatchReadAsync(Dictionary<string, Type> addresses)
        {
            var results = new Dictionary<string, object>();

            if (_disposed || !_isConnected)
            {
                return results;
            }

            foreach (var kvp in addresses)
            {
                try
                {
                    var address = kvp.Key;
                    var dataType = kvp.Value;

                    if (dataType == typeof(bool))
                    {
                        var result = await ReadBoolAsync(address);
                        if (result.IsSuccess)
                            results[address] = result.Content;
                    }
                    else if (dataType == typeof(short))
                    {
                        var result = await ReadInt16Async(address);
                        if (result.IsSuccess)
                            results[address] = result.Content;
                    }
                    else if (dataType == typeof(int))
                    {
                        var result = await ReadInt32Async(address);
                        if (result.IsSuccess)
                            results[address] = result.Content;
                    }
                    else if (dataType == typeof(float))
                    {
                        var result = await ReadFloatAsync(address);
                        if (result.IsSuccess)
                            results[address] = result.Content;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"批量读取地址 {kvp.Key} 失败");
                }
            }

            return results;
        }

        /// <summary>
        /// 批量写入多个地址
        /// </summary>
        public async Task<Dictionary<string, bool>> BatchWriteAsync(Dictionary<string, object> values)
        {
            var results = new Dictionary<string, bool>();

            if (_disposed || !_isConnected)
            {
                return results;
            }

            foreach (var kvp in values)
            {
                try
                {
                    var address = kvp.Key;
                    var value = kvp.Value;
                    bool success = false;

                    switch (value)
                    {
                        case bool boolValue:
                            var boolResult = await WriteBoolAsync(address, boolValue);
                            success = boolResult.IsSuccess;
                            break;
                        case short shortValue:
                            var shortResult = await WriteInt16Async(address, shortValue);
                            success = shortResult.IsSuccess;
                            break;
                        case int intValue:
                            var intResult = await WriteInt32Async(address, intValue);
                            success = intResult.IsSuccess;
                            break;
                        case float floatValue:
                            var floatResult = await WriteFloatAsync(address, floatValue);
                            success = floatResult.IsSuccess;
                            break;
                    }

                    results[address] = success;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"批量写入地址 {kvp.Key} 失败");
                    results[kvp.Key] = false;
                }
            }

            return results;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化PLC客户端
        /// </summary>
        private void InitializePlcClient()
        {
            _mcClient = new MelsecMcNet
            {
                IpAddress = _configuration.IpAddress,
                Port = _configuration.Port,
                ConnectTimeOut = _configuration.ConnectTimeout,
                ReceiveTimeOut = _configuration.ReceiveTimeout
            };

            // 设置网络编号和站号
            _mcClient.NetworkNumber = _configuration.NetworkNumber;
            _mcClient.NetworkStationNumber = _configuration.StationNumber;

            _logger.LogDebug($"PLC客户端已初始化: {_plcId} - {_configuration.IpAddress}:{_configuration.Port}");
        }

        /// <summary>
        /// 更新错误信息
        /// </summary>
        private void UpdateErrorInfo(string errorMessage)
        {
            _errorCount++;
            _lastErrorMessage = errorMessage;
        }

        /// <summary>
        /// 触发连接状态变化事件
        /// </summary>
        private void OnConnectionStatusChanged(bool isConnected, string message)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(this, new PlcConnectionStatusEventArgs(_plcId, isConnected, message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"触发PLC连接状态变化事件失败: {_plcId}");
            }
        }

        #endregion

        #region IDisposable 实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (_isConnected)
                    {
                        _mcClient?.ConnectClose();
                        _isConnected = false;
                    }

                    _mcClient?.Dispose();
                    _disposed = true;

                    _logger.LogDebug($"PLC连接实例已释放: {_plcId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"释放PLC连接实例异常: {_plcId}");
                }
            }
        }

        #endregion
    }
}