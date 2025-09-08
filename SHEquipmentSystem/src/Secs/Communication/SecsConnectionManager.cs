// 文件路径: src/DiceEquipmentSystem/Secs/Communication/SecsConnectionManager.cs
// 版本: v1.0.0
// 描述: 设备端SECS连接管理器实现

using System;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Secs.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Communication
{
    /// <summary>
    /// 设备端SECS连接管理器
    /// 管理设备作为Equipment与Host的HSMS连接
    /// </summary>
    public class SecsConnectionManager : ISecsConnectionManager, IDisposable
    {
        #region 私有字段

        private readonly ILogger<SecsConnectionManager> _logger;
        private readonly EquipmentSystemConfiguration _config;
        private readonly ISecsMessageDispatcher? _messageDispatcher;
        private HsmsConnection? _hsmsConnection;
        private SecsGem? _secsGem;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _messageProcessingTask;
        private HsmsConnectionState _connectionState = HsmsConnectionState.Retry;
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public SecsConnectionManager(
            ILogger<SecsConnectionManager> logger,
            IOptions<EquipmentSystemConfiguration> options,
            ISecsMessageDispatcher? messageDispatcher = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _messageDispatcher = messageDispatcher;
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _connectionState == HsmsConnectionState.Connected ||
                                   _connectionState == HsmsConnectionState.Selected;

        /// <summary>
        /// 是否已选中
        /// </summary>
        public bool IsSelected => _connectionState == HsmsConnectionState.Selected;

        /// <summary>
        /// 连接状态
        /// </summary>
        public HsmsConnectionState HsmsConnectionState => _connectionState;

        #endregion

        #region 公共事件

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// 主动消息接收事件
        /// </summary>
        public event EventHandler<PrimaryMessageReceivedEventArgs>? PrimaryMessageReceived;

        /// <summary>
        /// 通信错误事件
        /// </summary>
        public event EventHandler<CommunicationErrorEventArgs>? CommunicationError;

        #endregion

        #region 连接管理

        /// <summary>
        /// 启动连接
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation($"启动SECS连接 - 模式: {(_config.Equipment.IsActive ? "Active" : "Passive")}");

                // 创建HSMS组件
                CreateHsmsComponents();

                // 订阅事件
                SubscribeToHsmsEvents();

                // 启动连接
                _cancellationTokenSource = new CancellationTokenSource();
                _hsmsConnection!.Start(_cancellationTokenSource.Token);

                // 如果是Active模式，等待连接建立
                if (_config.Equipment.IsActive)
                {
                    await WaitForConnection(cancellationToken);
                }

                // 启动消息处理
                StartMessageProcessing();

                _logger.LogInformation("SECS连接已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动SECS连接失败");
                throw;
            }
        }

        /// <summary>
        /// 停止连接
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("停止SECS连接");

                _cancellationTokenSource?.Cancel();

                if (_messageProcessingTask != null)
                {
                    await _messageProcessingTask;
                }

                if (_hsmsConnection != null)
                {
                    await _hsmsConnection.DisposeAsync();
                }

                _secsGem?.Dispose();

                UpdateConnectionState(HsmsConnectionState.Retry);

                _logger.LogInformation("SECS连接已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止SECS连接失败");
            }
        }

        #endregion

        #region 消息发送

        /// <summary>
        /// 发送消息并等待响应
        /// </summary>
        public async Task<SecsMessage?> SendMessageAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            await _sendSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (!IsSelected)
                {
                    throw new InvalidOperationException($"设备未就绪，当前状态: {_connectionState}");
                }

                _logger.LogDebug($"发送消息: S{message.S}F{message.F}");

                var reply = await _secsGem!.SendAsync(message, cancellationToken);

                if (reply != null)
                {
                    _logger.LogDebug($"收到响应: S{reply.S}F{reply.F}");
                }

                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送消息失败: S{message.S}F{message.F}");
                OnCommunicationError(ex.Message, ex);
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// 发送消息但不等待响应
        /// </summary>
        public async Task SendWithoutReplyAsync(SecsMessage message)
        {
            try
            {
                if (!IsSelected)
                {
                    throw new InvalidOperationException($"设备未就绪，当前状态: {_connectionState}");
                }

                var messageToSend = new SecsMessage(message.S, message.F, false)
                {
                    Name = message.Name,
                    SecsItem = message.SecsItem
                };

                await _secsGem!.SendAsync(messageToSend);

                _logger.LogDebug($"发送无响应消息成功: S{message.S}F{message.F}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送无响应消息失败: S{message.S}F{message.F}");
                OnCommunicationError(ex.Message, ex);
                throw;
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 创建HSMS组件
        /// </summary>
        private void CreateHsmsComponents()
        {
            var options = Options.Create(new SecsGemOptions
            {
                DeviceId = (ushort)_config.Equipment.DeviceId,
                IsActive = _config.Equipment.IsActive,
                IpAddress = _config.Equipment.IpAddress,
                Port = _config.Equipment.Port,
                SocketReceiveBufferSize = 65536,
                T3 = _config.Equipment.T3,
                T5 = _config.Equipment.T5,
                T6 = _config.Equipment.T6,
                T7 = _config.Equipment.T7,
                T8 = _config.Equipment.T8
            });

            var loggerAdapter = new SecsGemLoggerAdapter(_logger);

            _hsmsConnection = new HsmsConnection(options, loggerAdapter);
            _secsGem = new SecsGem(options, _hsmsConnection, loggerAdapter);
        }

        /// <summary>
        /// 订阅HSMS事件
        /// </summary>
        private void SubscribeToHsmsEvents()
        {
            _hsmsConnection!.ConnectionChanged += (sender, state) =>
            {
                UpdateConnectionState((HsmsConnectionState)state);
            };
        }

        /// <summary>
        /// 等待连接建立
        /// </summary>
        private async Task WaitForConnection(CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(_config.Equipment.T5);
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime) < timeout)
            {
                if (IsSelected)
                {
                    _logger.LogInformation("连接已建立");
                    return;
                }

                await Task.Delay(500, cancellationToken);
            }

            throw new TimeoutException("等待连接超时");
        }

        /// <summary>
        /// 启动消息处理
        /// </summary>
        private void StartMessageProcessing()
        {
            _messageProcessingTask = Task.Run(async () =>
            {
                _logger.LogInformation("消息处理循环已启动");

                try
                {
                    await foreach (var wrapper in _secsGem!.GetPrimaryMessageAsync(_cancellationTokenSource!.Token))
                    {
                        await HandlePrimaryMessage(wrapper);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("消息处理循环已停止");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "消息处理循环异常");
                }
            });
        }

        /// <summary>
        /// 处理主动消息
        /// </summary>
        private async Task HandlePrimaryMessage(PrimaryMessageWrapper wrapper)
        {
            try
            {
                var message = wrapper.PrimaryMessage;
                _logger.LogDebug($"收到主动消息: S{message.S}F{message.F}");

                // 触发事件
                OnPrimaryMessageReceived(message, wrapper);

                // 使用消息分发器处理
                if (_messageDispatcher != null && message.ReplyExpected)
                {
                    var reply = await _messageDispatcher.DispatchAsync(message);
                    if (reply != null)
                    {
                        await wrapper.TryReplyAsync(reply);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理主动消息失败");
            }
        }

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionState(HsmsConnectionState newState)
        {
            var oldState = _connectionState;
            _connectionState = newState;

            if (oldState != newState)
            {
                _logger.LogInformation($"连接状态变化: {oldState} -> {newState}");
                OnConnectionStateChanged(oldState, newState);
            }
        }

        #endregion

        #region 事件触发

        private void OnConnectionStateChanged(HsmsConnectionState oldState, HsmsConnectionState newState)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Timestamp = DateTime.Now
            });
        }

        private void OnPrimaryMessageReceived(SecsMessage message, PrimaryMessageWrapper wrapper)
        {
            PrimaryMessageReceived?.Invoke(this, new PrimaryMessageReceivedEventArgs
            {
                Message = message,
                MessageWrapper = wrapper,
                Timestamp = DateTime.Now
            });
        }

        private void OnCommunicationError(string errorMessage, Exception? exception)
        {
            CommunicationError?.Invoke(this, new CommunicationErrorEventArgs
            {
                ErrorMessage = errorMessage,
                Exception = exception,
                ErrorType = exception switch
                {
                    TimeoutException => CommunicationErrorType.Timeout,
                    _ => CommunicationErrorType.Unknown
                },
                ErrorTime = DateTime.Now
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            StopAsync().Wait(5000);
            _sendSemaphore?.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// SECS/GEM日志适配器
        /// </summary>
        private class SecsGemLoggerAdapter : ISecsGemLogger
        {
            private readonly ILogger _logger;

            public SecsGemLoggerAdapter(ILogger logger)
            {
                _logger = logger;
            }

            public void MessageIn(SecsMessage msg, int id)
            {
                _logger.LogInformation($"[IN] [ID:{id}] S{msg.S}F{msg.F} {msg.Name}");
            }

            public void MessageOut(SecsMessage msg, int id)
            {
                _logger.LogInformation($"[OUT] [ID:{id}] S{msg.S}F{msg.F} {msg.Name}");
            }

            public void Debug(string msg) => _logger.LogDebug(msg);
            public void Info(string msg) => _logger.LogInformation(msg);
            public void Warning(string msg) => _logger.LogWarning(msg);
            public void Error(string msg, SecsMessage? message, Exception? ex)
            {
                if (ex != null)
                    _logger.LogError(ex, msg);
                else
                    _logger.LogError(msg);
            }
        }

        #endregion
    }
}
