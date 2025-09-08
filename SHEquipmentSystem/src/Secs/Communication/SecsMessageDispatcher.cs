// 文件路径: src/DiceEquipmentSystem/Secs/Communication/SecsMessageDispatcher.cs
// 版本: v1.0.0
// 描述: SECS消息分发器实现

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Secs.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Communication
{
    /// <summary>
    /// SECS消息分发器实现
    /// 管理和路由SECS消息到相应的处理器
    /// </summary>
    public class SecsMessageDispatcher : ISecsMessageDispatcher
    {
        #region 私有字段

        private readonly ILogger<SecsMessageDispatcher> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, ISecsMessageHandler> _handlers;
        private readonly ConcurrentDictionary<string, Type> _handlerTypes;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="serviceProvider">服务提供者</param>
        public SecsMessageDispatcher(
            ILogger<SecsMessageDispatcher> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _handlers = new ConcurrentDictionary<string, ISecsMessageHandler>();
            _handlerTypes = new ConcurrentDictionary<string, Type>();

            // 自动注册所有Handler
            RegisterAllHandlers();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 分发消息到相应的处理器
        /// </summary>
        public async Task<SecsMessage?> DispatchAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            var key = GetHandlerKey(message.S, message.F);

            _logger.LogDebug($"分发消息 S{message.S}F{message.F}");

            // 尝试获取处理器
            var handler = GetOrCreateHandler(message.S, message.F);

            if (handler == null)
            {
                _logger.LogWarning($"未找到S{message.S}F{message.F}的处理器");
                return GenerateDefaultResponse(message);
            }

            try
            {
                _logger.LogDebug($"调用处理器处理S{message.S}F{message.F}");
                var response = await handler.HandleAsync(message, cancellationToken);

                if (response != null)
                {
                    _logger.LogDebug($"处理器返回响应 S{response.S}F{response.F}");
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理S{message.S}F{message.F}时发生错误");
                return GenerateErrorResponse(message);
            }
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        public void RegisterHandler(byte stream, byte function, ISecsMessageHandler handler)
        {
            var key = GetHandlerKey(stream, function);
            _handlers[key] = handler;
            _logger.LogDebug($"注册处理器 S{stream}F{function}");
        }

        /// <summary>
        /// 移除消息处理器
        /// </summary>
        public void UnregisterHandler(byte stream, byte function)
        {
            var key = GetHandlerKey(stream, function);
            if (_handlers.TryRemove(key, out _))
            {
                _logger.LogDebug($"移除处理器 S{stream}F{function}");
            }
        }

        /// <summary>
        /// 检查是否有处理器
        /// </summary>
        public bool HasHandler(byte stream, byte function)
        {
            var key = GetHandlerKey(stream, function);
            return _handlers.ContainsKey(key) || _handlerTypes.ContainsKey(key);
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 自动注册所有Handler
        /// </summary>
        private void RegisterAllHandlers()
        {
            // 注册所有标准Handler类型
            var handlerMappings = new Dictionary<string, Type>
            {
                // Stream 1
                { GetHandlerKey(1, 1), typeof(Handlers.S1F1Handler) },
                { GetHandlerKey(1, 3), typeof(Handlers.S1F3Handler) },
                { GetHandlerKey(1, 11), typeof(Handlers.S1F11Handler) },
                { GetHandlerKey(1, 13), typeof(Handlers.S1F13Handler) },
                { GetHandlerKey(1, 17), typeof(Handlers.S1F17Handler) },
                
                // Stream 2
                { GetHandlerKey(2, 13), typeof(Handlers.S2F13Handler) },
                { GetHandlerKey(2, 15), typeof(Handlers.S2F15Handler) },
                { GetHandlerKey(2, 23), typeof(Handlers.S2F23Handler) },
                { GetHandlerKey(2, 29), typeof(Handlers.S2F29Handler) },
                //{ GetHandlerKey(2, 31), typeof(Handlers.S2F31Handler) },
                { GetHandlerKey(2, 33), typeof(Handlers.S2F33Handler) },
                { GetHandlerKey(2, 35), typeof(Handlers.S2F35Handler) },
                { GetHandlerKey(2, 37), typeof(Handlers.S2F37Handler) },
                { GetHandlerKey(2, 41), typeof(Handlers.S2F41Handler) },
                
                // Stream 6
                { GetHandlerKey(6, 11), typeof(Handlers.S6F11Handler) },
                { GetHandlerKey(6, 15), typeof(Handlers.S6F15Handler) },
                { GetHandlerKey(6, 19), typeof(Handlers.S6F19Handler) },
                
                // Stream 7
                //{ GetHandlerKey(7, 1), typeof(Handlers.S7F1Handler) },
                //{ GetHandlerKey(7, 3), typeof(Handlers.S7F3Handler) },
                //{ GetHandlerKey(7, 5), typeof(Handlers.S7F5Handler) },
                //{ GetHandlerKey(7, 17), typeof(Handlers.S7F17Handler) },
                //{ GetHandlerKey(7, 19), typeof(Handlers.S7F19Handler) },
                
                //// Stream 10
                //{ GetHandlerKey(10, 1), typeof(Handlers.S10F1Handler) },
                //{ GetHandlerKey(10, 3), typeof(Handlers.S10F3Handler) }
            };

            foreach (var mapping in handlerMappings)
            {
                _handlerTypes[mapping.Key] = mapping.Value;
                _logger.LogDebug($"注册Handler类型: {mapping.Key} -> {mapping.Value.Name}");
            }
        }

        /// <summary>
        /// 获取或创建处理器
        /// </summary>
        private ISecsMessageHandler? GetOrCreateHandler(byte stream, byte function)
        {
            var key = GetHandlerKey(stream, function);

            // 先检查已实例化的处理器
            if (_handlers.TryGetValue(key, out var handler))
            {
                return handler;
            }

            // 尝试从服务容器创建处理器
            if (_handlerTypes.TryGetValue(key, out var handlerType))
            {
                try
                {
                    handler = _serviceProvider.GetService(handlerType) as ISecsMessageHandler;
                    if (handler != null)
                    {
                        _handlers[key] = handler;
                        _logger.LogDebug($"创建处理器实例: S{stream}F{function}");
                        return handler;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"创建处理器S{stream}F{function}失败");
                }
            }

            return null;
        }

        /// <summary>
        /// 获取处理器键值
        /// </summary>
        private string GetHandlerKey(byte stream, byte function)
        {
            return $"S{stream}F{function}";
        }

        /// <summary>
        /// 生成默认响应
        /// </summary>
        private SecsMessage? GenerateDefaultResponse(SecsMessage message)
        {
            // 对于需要响应的消息，生成默认响应
            if (message.F % 2 == 1 && message.ReplyExpected)
            {
                var responseFunction = (byte)(message.F + 1);

                return message.S switch
                {
                    1 when message.F == 1 => new SecsMessage(1, 2, false)
                    {
                        Name = "OnLineData",
                        SecsItem = Item.L()
                    },
                    1 when message.F == 13 => new SecsMessage(1, 14, false)
                    {
                        Name = "EstablishCommunicationsRequestAcknowledge",
                        SecsItem = Item.L(
                            Item.B(0),  // COMMACK = 0 (接受)
                            Item.L()    // 空列表
                        )
                    },
                    2 when message.F == 31 => new SecsMessage(2, 32, false)
                    {
                        Name = "DateTimeSetAcknowledge",
                        SecsItem = Item.B(0)  // TIACK = 0 (OK)
                    },
                    2 when message.F == 41 => new SecsMessage(2, 42, false)
                    {
                        Name = "HostCommandAcknowledge",
                        SecsItem = Item.L(
                            Item.B(0),  // HCACK = 0 (OK)
                            Item.L()    // PARAMS
                        )
                    },
                    _ => new SecsMessage(message.S, responseFunction, false)
                    {
                        Name = "DefaultResponse",
                        SecsItem = Item.B(0)  // 默认确认
                    }
                };
            }

            return null;
        }

        /// <summary>
        /// 生成错误响应
        /// </summary>
        private SecsMessage? GenerateErrorResponse(SecsMessage message)
        {
            if (message.F % 2 == 1 && message.ReplyExpected)
            {
                var responseFunction = (byte)(message.F + 1);

                // 根据不同的Stream生成错误响应
                return message.S switch
                {
                    1 => new SecsMessage(1, responseFunction, false)
                    {
                        Name = "ErrorResponse",
                        SecsItem = Item.B(1)  // 错误代码
                    },
                    2 => new SecsMessage(2, responseFunction, false)
                    {
                        Name = "ErrorResponse",
                        SecsItem = Item.B(1)  // 错误代码
                    },
                    _ => new SecsMessage(message.S, responseFunction, false)
                    {
                        Name = "ErrorResponse",
                        SecsItem = Item.L()  // 空列表表示错误
                    }
                };
            }

            return null;
        }

        #endregion
    }
}
