// 文件路径: src/DiceEquipmentSystem/Secs/Handlers/S2F41Handler.cs
// 版本: v1.1.0
// 描述: S2F41远程命令处理器 - 修复版本

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Handlers
{
    /// <summary>
    /// S2F41 (Host Command Send) 远程命令处理器
    /// 处理主机发送的远程控制命令，符合SEMI E30 Remote Control标准
    /// </summary>
    public class S2F41Handler : SecsMessageHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// 划裂片设备远程命令常量定义
        /// </summary>
        public static class DicerRemoteCommands
        {
            /// <summary>启动槽位映射检测</summary>
            public const string SCAN_SLOT_MAPPING = "ScanSlotMapping";

            /// <summary>启动Cassette处理</summary>
            public const string CASSETTE_START = "CassetteStart";

            /// <summary>选择工艺配方</summary>
            public const string PP_SELECT = "PP-SELECT";

            /// <summary>启动Frame环处理</summary>
            public const string FRAME_START = "FrameStart";

            /// <summary>开始工艺处理</summary>
            public const string START = "START";

            /// <summary>停止工艺处理</summary>
            public const string STOP = "STOP";

            /// <summary>暂停工艺处理</summary>
            public const string PAUSE = "PAUSE";

            /// <summary>恢复工艺处理</summary>
            public const string RESUME = "RESUME";

            /// <summary>中止工艺处理</summary>
            public const string ABORT = "ABORT";
        }

        /// <summary>
        /// Host命令确认代码 (HCACK) 定义
        /// </summary>
        public enum HostCommandAck : byte
        {
            /// <summary>命令已接受并将执行</summary>
            Accepted = 0,

            /// <summary>命令无效或不存在</summary>
            InvalidCommand = 1,

            /// <summary>当前状态下无法执行</summary>
            CannotPerform = 2,

            /// <summary>至少一个参数无效</summary>
            InvalidParameter = 3,

            /// <summary>命令将被执行</summary>
            WillExecute = 4,

            /// <summary>设备拒绝</summary>
            Rejected = 5
        }

        #endregion

        #region 私有字段

        /// <summary>设备状态管理服务</summary>
        private readonly IEquipmentStateService _stateService;

        /// <summary>事件报告服务</summary>
        private readonly IEventReportService _eventReportService;

        /// <summary>PLC数据提供者</summary>
        private readonly IPlcDataProvider? _plcProvider;

        /// <summary>设备配置</summary>
        private readonly EquipmentSystemConfiguration _config;

        /// <summary>命令执行锁</summary>
        private readonly SemaphoreSlim _commandExecutionLock;

        /// <summary>命令超时时间(毫秒)</summary>
        private const int COMMAND_TIMEOUT_MS = 60000;

        #endregion

        #region 属性

        /// <summary>消息流号</summary>
        public override byte Stream => 2;

        /// <summary>消息功能号</summary>
        public override byte Function => 41;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="stateService">设备状态服务</param>
        /// <param name="eventReportService">事件报告服务</param>
        /// <param name="options">设备配置选项</param>
        /// <param name="plcProvider">PLC数据提供者(可选)</param>
        public S2F41Handler(
            ILogger<S2F41Handler> logger,
            IEquipmentStateService stateService,
            IEventReportService eventReportService,
            IOptions<EquipmentSystemConfiguration> options,
            IPlcDataProvider? plcProvider = null) : base(logger)
        {
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));
            _eventReportService = eventReportService ?? throw new ArgumentNullException(nameof(eventReportService));
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _plcProvider = plcProvider;
            _commandExecutionLock = new SemaphoreSlim(1, 1);
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理S2F41远程命令消息
        /// </summary>
        /// <param name="message">接收到的S2F41消息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>S2F42命令确认响应消息</returns>
        public override async Task<SecsMessage?> HandleAsync(SecsMessage message, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            LogHandlingStart(message);

            var startTime = DateTime.Now;

            await _commandExecutionLock.WaitAsync(cancellationToken);
            try
            {
                // 解析命令
                var parseResult = ParseS2F41Message(message);
                if (!parseResult.IsValid)
                {
                    Logger.LogWarning("S2F41消息解析失败: {Reason}", parseResult.ErrorReason);
                    return CreateS2F42Response(HostCommandAck.InvalidCommand, parseResult.ErrorParameters);
                }

                var command = parseResult.Command!;
                var parameters = parseResult.Parameters!;

                Logger.LogInformation("解析命令成功: {Command}, 参数数量: {ParamCount}", command, parameters.Count);

                // 验证权限
                var authResult = await ValidateRemoteControlAuthority();
                if (!authResult.IsAuthorized)
                {
                    Logger.LogWarning("远程控制权限验证失败: {Reason}", authResult.RejectReason);
                    return CreateS2F42Response(HostCommandAck.CannotPerform, null);
                }

                // 验证命令
                var validationResult = await ValidateCommandAndParameters(command, parameters);
                if (!validationResult.IsValid)
                {
                    Logger.LogWarning("命令验证失败: {Command}, 原因: {Reason}", command, validationResult.ErrorReason);
                    return CreateS2F42Response(validationResult.HcackCode, validationResult.ErrorParameters);
                }

                // 执行命令
                var executionResult = await ExecuteRemoteCommandAsync(command, parameters, cancellationToken);

                // 触发事件
                await TriggerCommandExecutionEventAsync(command, parameters, executionResult);

                Logger.LogInformation("远程命令执行完成: {Command}, 结果: {Result}",
                    command, executionResult.HcackCode);

                return CreateS2F42Response(executionResult.HcackCode, executionResult.ErrorParameters);
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("远程命令处理被取消");
                return CreateS2F42Response(HostCommandAck.CannotPerform, null);
            }
            catch (Exception ex)
            {
                LogHandlingError(message, ex);
                return CreateS2F42Response(HostCommandAck.Rejected, null);
            }
            finally
            {
                _commandExecutionLock.Release();
                LogHandlingComplete(message, null, DateTime.Now - startTime);
            }
        }

        #endregion

        #region 消息解析

        /// <summary>
        /// 解析S2F41消息内容
        /// </summary>
        /// <param name="message">S2F41消息</param>
        /// <returns>解析结果</returns>
        private CommandParseResult ParseS2F41Message(SecsMessage message)
        {
            try
            {
                if (message.SecsItem == null)
                {
                    return CommandParseResult.CreateError("消息体为空", ("Message", "Empty body"));
                }

                if (message.SecsItem.Count != 2)
                {
                    return CommandParseResult.CreateError($"消息体格式错误，期望2个元素，实际{message.SecsItem.Count}个",
                        ("Format", "Invalid structure"));
                }

                var items = message.SecsItem.Items;

                // 解析命令名称
                if (items[0].Format != SecsFormat.ASCII)
                {
                    return CommandParseResult.CreateError("命令名称必须是ASCII格式", ("RCMD", "Invalid format"));
                }

                var command = items[0].GetString().Trim();
                if (string.IsNullOrEmpty(command))
                {
                    return CommandParseResult.CreateError("命令名称不能为空", ("RCMD", "Empty command"));
                }

                // 解析参数列表
                var parameters = new Dictionary<string, object>();
                var paramErrors = new List<(string, string)>();

                if (items[1].Count > 0)
                {
                    foreach (var paramItem in items[1].Items)
                    {
                        var paramResult = ParseSingleParameter(paramItem);
                        if (paramResult.IsValid)
                        {
                            parameters[paramResult.Name!] = paramResult.Value!;
                        }
                        else
                        {
                            paramErrors.Add((paramResult.Name ?? "Unknown", paramResult.ErrorReason ?? "Parse error"));
                        }
                    }
                }

                if (paramErrors.Count > 0)
                {
                    return CommandParseResult.CreateError($"参数解析失败，{paramErrors.Count}个参数有错误",
                        paramErrors.ToArray());
                }

                return CommandParseResult.CreateSuccess(command, parameters);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解析S2F41消息异常");
                return CommandParseResult.CreateError($"解析异常: {ex.Message}", ("Exception", ex.Message));
            }
        }

        /// <summary>
        /// 解析单个命令参数
        /// </summary>
        /// <param name="paramItem">参数项</param>
        /// <returns>参数解析结果</returns>
        private ParameterParseResult ParseSingleParameter(Item paramItem)
        {
            try
            {
                if (paramItem.Count != 2)
                {
                    return new ParameterParseResult
                    {
                        IsValid = false,
                        ErrorReason = "参数格式错误，必须包含名称和值"
                    };
                }

                var nameItem = paramItem.Items[0];
                var valueItem = paramItem.Items[1];

                if (nameItem.Format != SecsFormat.ASCII)
                {
                    return new ParameterParseResult
                    {
                        IsValid = false,
                        ErrorReason = "参数名称必须是ASCII格式"
                    };
                }

                var name = nameItem.GetString().Trim();
                if (string.IsNullOrEmpty(name))
                {
                    return new ParameterParseResult
                    {
                        IsValid = false,
                        ErrorReason = "参数名称不能为空"
                    };
                }

                var value = ParseParameterValue(valueItem);

                return new ParameterParseResult
                {
                    IsValid = true,
                    Name = name,
                    Value = value
                };
            }
            catch (Exception ex)
            {
                return new ParameterParseResult
                {
                    IsValid = false,
                    ErrorReason = $"参数解析异常: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 解析SECS格式的参数值
        /// </summary>
        /// <param name="valueItem">值项</param>
        /// <returns>解析后的参数值对象</returns>
        private object ParseParameterValue(Item valueItem)
        {
            return valueItem.Format switch
            {
                SecsFormat.ASCII => valueItem.GetString(),
                SecsFormat.U1 => valueItem.FirstValue<byte>(),
                SecsFormat.U2 => valueItem.FirstValue<ushort>(),
                SecsFormat.U4 => valueItem.FirstValue<uint>(),
                SecsFormat.U8 => valueItem.FirstValue<ulong>(),
                SecsFormat.I1 => valueItem.FirstValue<sbyte>(),
                SecsFormat.I2 => valueItem.FirstValue<short>(),
                SecsFormat.I4 => valueItem.FirstValue<int>(),
                SecsFormat.I8 => valueItem.FirstValue<long>(),
                SecsFormat.F4 => valueItem.FirstValue<float>(),
                SecsFormat.F8 => valueItem.FirstValue<double>(),
                SecsFormat.Boolean => valueItem.FirstValue<bool>(),
                SecsFormat.Binary => valueItem.GetMemory<byte>().ToArray(),
                _ => valueItem.ToString()
            };
        }

        #endregion

        #region 权限验证

        /// <summary>
        /// 验证远程控制权限
        /// </summary>
        /// <returns>权限验证结果</returns>
        private async Task<RemoteControlAuthResult> ValidateRemoteControlAuthority()
        {
            try
            {
                var controlState = await _stateService.GetControlStateAsync();
                var statusInfo = await _stateService.GetStatusInfoAsync();

                // 检查控制状态
                if (controlState != ControlState.OnlineRemote)
                {
                    return new RemoteControlAuthResult
                    {
                        IsAuthorized = false,
                        RejectReason = $"不在远程控制模式，当前状态: {controlState}"
                    };
                }

                // 检查设备状态
                if (statusInfo.EquipmentState == EquipmentState.Error)
                {
                    return new RemoteControlAuthResult
                    {
                        IsAuthorized = false,
                        RejectReason = "设备处于错误状态，不接受远程控制"
                    };
                }

                // 检查通信状态
                if (!statusInfo.IsCommunicationEstablished)
                {
                    return new RemoteControlAuthResult
                    {
                        IsAuthorized = false,
                        RejectReason = "通信未正确建立"
                    };
                }

                return new RemoteControlAuthResult { IsAuthorized = true };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证远程控制权限异常");
                return new RemoteControlAuthResult
                {
                    IsAuthorized = false,
                    RejectReason = "权限验证异常"
                };
            }
        }

        #endregion

        #region 命令验证

        /// <summary>
        /// 验证命令和参数的有效性
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="parameters">命令参数</param>
        /// <returns>验证结果</returns>
        private async Task<CommandValidationResult> ValidateCommandAndParameters(
            string command, Dictionary<string, object> parameters)
        {
            // 验证命令支持性
            if (!IsSupportedCommand(command))
            {
                return CommandValidationResult.CreateInvalidCommand($"不支持的命令: {command}");
            }

            // 验证状态条件
            var statusInfo = await _stateService.GetStatusInfoAsync();
            var stateValidation = ValidateCommandForCurrentState(command, statusInfo);
            if (!stateValidation.IsValid)
            {
                return CommandValidationResult.CreateCannotPerform(stateValidation.ErrorReason!);
            }

            // 验证特定参数
            var paramValidation = await ValidateCommandSpecificParameters(command, parameters);
            if (!paramValidation.IsValid)
            {
                return CommandValidationResult.CreateInvalidParameter(paramValidation.ErrorReason!,
                    paramValidation.ErrorParameters);
            }

            return CommandValidationResult.CreateValid();
        }

        /// <summary>
        /// 检查命令是否被支持
        /// </summary>
        private bool IsSupportedCommand(string command)
        {
            return command.ToUpper() switch
            {
                DicerRemoteCommands.SCAN_SLOT_MAPPING => true,
                DicerRemoteCommands.CASSETTE_START => true,
                DicerRemoteCommands.PP_SELECT => true,
                DicerRemoteCommands.FRAME_START => true,
                DicerRemoteCommands.START => true,
                DicerRemoteCommands.STOP => true,
                DicerRemoteCommands.PAUSE => true,
                DicerRemoteCommands.RESUME => true,
                DicerRemoteCommands.ABORT => true,
                _ => false
            };
        }

        /// <summary>
        /// 验证当前状态是否支持指定命令
        /// </summary>
        private StateValidationResult ValidateCommandForCurrentState(string command, EquipmentStatusInfo statusInfo)
        {
            return command.ToUpper() switch
            {
                DicerRemoteCommands.START => ValidateStartCommand(statusInfo),
                DicerRemoteCommands.STOP => ValidateStopCommand(statusInfo),
                DicerRemoteCommands.PP_SELECT => ValidatePPSelectCommand(statusInfo),
                _ => new StateValidationResult { IsValid = true } // 其他命令默认允许
            };
        }

        /// <summary>
        /// 验证START命令状态条件
        /// </summary>
        private StateValidationResult ValidateStartCommand(EquipmentStatusInfo statusInfo)
        {
            if (statusInfo.IsProcessing)
            {
                return new StateValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"设备正在处理中，当前状态: {statusInfo.ProcessState}"
                };
            }

            if (statusInfo.ProcessState != ProcessState.Ready && statusInfo.ProcessState != ProcessState.Idle)
            {
                return new StateValidationResult
                {
                    IsValid = false,
                    ErrorReason = $"处理状态不允许启动，当前状态: {statusInfo.ProcessState}"
                };
            }

            return new StateValidationResult { IsValid = true };
        }

        /// <summary>
        /// 验证STOP命令状态条件
        /// </summary>
        private StateValidationResult ValidateStopCommand(EquipmentStatusInfo statusInfo)
        {
            if (!statusInfo.IsProcessing && statusInfo.ProcessState == ProcessState.Idle)
            {
                return new StateValidationResult
                {
                    IsValid = false,
                    ErrorReason = "没有正在运行的处理可以停止"
                };
            }

            return new StateValidationResult { IsValid = true };
        }

        /// <summary>
        /// 验证PP-SELECT命令状态条件
        /// </summary>
        private StateValidationResult ValidatePPSelectCommand(EquipmentStatusInfo statusInfo)
        {
            if (statusInfo.IsProcessing)
            {
                return new StateValidationResult
                {
                    IsValid = false,
                    ErrorReason = "处理中不能切换配方"
                };
            }

            return new StateValidationResult { IsValid = true };
        }

        /// <summary>
        /// 验证命令特定参数
        /// </summary>
        private async Task<ParameterValidationResult> ValidateCommandSpecificParameters(
            string command, Dictionary<string, object> parameters)
        {
            return command.ToUpper() switch
            {
                DicerRemoteCommands.PP_SELECT => await ValidatePPSelectParameters(parameters),
                _ => new ParameterValidationResult { IsValid = true }
            };
        }

        /// <summary>
        /// 验证PP-SELECT命令参数
        /// </summary>
        private async Task<ParameterValidationResult> ValidatePPSelectParameters(Dictionary<string, object> parameters)
        {
            if (!parameters.ContainsKey("RecipeName") && !parameters.ContainsKey("PPID"))
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = "PP-SELECT命令必须包含RecipeName或PPID参数",
                    ErrorParameters = new[] { ("RecipeName", "Missing parameter") }
                };
            }

            var recipeName = parameters.GetValueOrDefault("RecipeName")?.ToString() ??
                           parameters.GetValueOrDefault("PPID")?.ToString();

            if (string.IsNullOrWhiteSpace(recipeName))
            {
                return new ParameterValidationResult
                {
                    IsValid = false,
                    ErrorReason = "配方名称不能为空",
                    ErrorParameters = new[] { ("RecipeName", "Empty value") }
                };
            }

            await Task.CompletedTask;
            return new ParameterValidationResult { IsValid = true };
        }

        #endregion

        #region 命令执行

        /// <summary>
        /// 执行远程命令
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="parameters">命令参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>命令执行结果</returns>
        private async Task<CommandExecutionResult> ExecuteRemoteCommandAsync(
            string command, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(COMMAND_TIMEOUT_MS);

                return command.ToUpper() switch
                {
                    DicerRemoteCommands.SCAN_SLOT_MAPPING => await ExecuteScanSlotMappingAsync(parameters, cts.Token),
                    DicerRemoteCommands.CASSETTE_START => await ExecuteCassetteStartAsync(parameters, cts.Token),
                    DicerRemoteCommands.PP_SELECT => await ExecutePPSelectAsync(parameters, cts.Token),
                    DicerRemoteCommands.FRAME_START => await ExecuteFrameStartAsync(parameters, cts.Token),
                    DicerRemoteCommands.START => await ExecuteStartCommandAsync(parameters, cts.Token),
                    DicerRemoteCommands.STOP => await ExecuteStopCommandAsync(parameters, cts.Token),
                    DicerRemoteCommands.PAUSE => await ExecutePauseCommandAsync(parameters, cts.Token),
                    DicerRemoteCommands.RESUME => await ExecuteResumeCommandAsync(parameters, cts.Token),
                    DicerRemoteCommands.ABORT => await ExecuteAbortCommandAsync(parameters, cts.Token),
                    _ => CommandExecutionResult.CreateFailure(HostCommandAck.InvalidCommand, $"未实现的命令: {command}")
                };
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("远程命令执行超时: {Command}", command);
                return CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform, "命令执行超时");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行远程命令异常: {Command}", command);
                return CommandExecutionResult.CreateFailure(HostCommandAck.Rejected, $"命令执行异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行START命令
        /// </summary>
        private async Task<CommandExecutionResult> ExecuteStartCommandAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("开始执行START命令...");

                // 启动处理状态
                var startSuccess = await _stateService.StartProcessAsync();
                if (!startSuccess)
                {
                    return CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform, "状态管理器拒绝启动处理");
                }

                // 执行PLC操作
                if (_plcProvider != null)
                {
                    var plcResult = await _plcProvider.ExecuteAsync("START_PROCESS", parameters, cancellationToken);
                    if (!plcResult.Success)
                    {
                        await _stateService.StopProcessAsync(); // 回滚状态
                        return CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform,
                            $"PLC启动失败: {plcResult.ErrorMessage}");
                    }
                }

                Logger.LogInformation("START命令执行成功");
                return CommandExecutionResult.CreateSuccess();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行START命令失败");
                return CommandExecutionResult.CreateFailure(HostCommandAck.Rejected, ex.Message);
            }
        }

        /// <summary>
        /// 执行STOP命令
        /// </summary>
        private async Task<CommandExecutionResult> ExecuteStopCommandAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("开始执行STOP命令...");

                var stopSuccess = await _stateService.StopProcessAsync();
                if (!stopSuccess)
                {
                    return CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform, "无法停止处理");
                }

                if (_plcProvider != null)
                {
                    var plcResult = await _plcProvider.ExecuteAsync("STOP_PROCESS", parameters, cancellationToken);
                    if (!plcResult.Success)
                    {
                        Logger.LogWarning("PLC停止操作失败: {Error}", plcResult.ErrorMessage);
                    }
                }

                Logger.LogInformation("STOP命令执行成功");
                return CommandExecutionResult.CreateSuccess();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行STOP命令失败");
                return CommandExecutionResult.CreateFailure(HostCommandAck.Rejected, ex.Message);
            }
        }

        /// <summary>
        /// 执行配方选择命令
        /// </summary>
        private async Task<CommandExecutionResult> ExecutePPSelectAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            try
            {
                var recipeName = parameters.GetValueOrDefault("RecipeName")?.ToString() ??
                               parameters.GetValueOrDefault("PPID")?.ToString();

                Logger.LogInformation("开始执行配方选择: {RecipeName}", recipeName);

                if (_plcProvider != null)
                {
                    var plcParams = new Dictionary<string, object> { ["RecipeName"] = recipeName! };
                    var plcResult = await _plcProvider.ExecuteAsync("SELECT_RECIPE", plcParams, cancellationToken);

                    if (!plcResult.Success)
                    {
                        return CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform,
                            $"PLC配方加载失败: {plcResult.ErrorMessage}");
                    }
                }

                await Task.Delay(1000, cancellationToken); // 模拟配方加载时间

                Logger.LogInformation("配方选择完成: {RecipeName}", recipeName);
                return CommandExecutionResult.CreateSuccess();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行配方选择失败");
                return CommandExecutionResult.CreateFailure(HostCommandAck.Rejected, ex.Message);
            }
        }

        /// <summary>
        /// 执行其他命令的占位实现
        /// </summary>
        private async Task<CommandExecutionResult> ExecuteScanSlotMappingAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            Logger.LogInformation("执行SCAN_SLOT_MAPPING命令");
            await Task.Delay(2000, cancellationToken);
            return CommandExecutionResult.CreateSuccess();
        }

        private async Task<CommandExecutionResult> ExecuteCassetteStartAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            Logger.LogInformation("执行CASSETTE_START命令");
            await Task.Delay(1000, cancellationToken);
            return CommandExecutionResult.CreateSuccess();
        }

        private async Task<CommandExecutionResult> ExecuteFrameStartAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            Logger.LogInformation("执行FRAME_START命令");
            await Task.Delay(500, cancellationToken);
            return CommandExecutionResult.CreateSuccess();
        }

        private async Task<CommandExecutionResult> ExecutePauseCommandAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var pauseSuccess = await _stateService.PauseProcessAsync();
            return pauseSuccess ?
                CommandExecutionResult.CreateSuccess() :
                CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform, "无法暂停处理");
        }

        private async Task<CommandExecutionResult> ExecuteResumeCommandAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var resumeSuccess = await _stateService.ResumeProcessAsync();
            return resumeSuccess ?
                CommandExecutionResult.CreateSuccess() :
                CommandExecutionResult.CreateFailure(HostCommandAck.CannotPerform, "无法恢复处理");
        }

        private async Task<CommandExecutionResult> ExecuteAbortCommandAsync(
            Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            Logger.LogInformation("执行ABORT命令");
            await _stateService.StopProcessAsync(); // 使用停止代替中止
            return CommandExecutionResult.CreateSuccess();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 触发命令执行相关事件
        /// </summary>
        private async Task TriggerCommandExecutionEventAsync(
            string command, Dictionary<string, object> parameters, CommandExecutionResult result)
        {
            if (result.HcackCode != HostCommandAck.Accepted)
            {
                return;
            }

            try
            {
                uint ceid = command.ToUpper() switch
                {
                    DicerRemoteCommands.PP_SELECT => 11003,         // PPSelected
                    DicerRemoteCommands.START => 11004,             // ProcessStart
                    DicerRemoteCommands.STOP => 11005,              // ProcessEnd
                    DicerRemoteCommands.CASSETTE_START => 11014,    // CassetteStart
                    DicerRemoteCommands.FRAME_START => 11012,       // FrameStart
                    DicerRemoteCommands.SCAN_SLOT_MAPPING => 11002, // MapComplete
                    _ => 0
                };

                if (ceid > 0)
                {
                    Logger.LogInformation("触发命令执行事件: {Command} -> CEID {CEID}", command, ceid);

                    var eventParameters = new List<object>
                    {
                        DateTime.Now,
                        command,
                        "Remote Command"
                    };

                    if (parameters.Count > 0)
                    {
                        eventParameters.Add(parameters);
                    }

                    await _eventReportService.ReportEventAsync(ceid, eventParameters.ToArray());
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "触发命令执行事件失败: {Command}", command);
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 创建S2F42命令确认响应
        /// </summary>
        private SecsMessage CreateS2F42Response(HostCommandAck hcack, IEnumerable<(string name, string reason)>? errorParameters)
        {
            var ackCode = (byte)hcack;
            Logger.LogDebug("创建S2F42响应: HCACK={AckCode} ({Description})", ackCode, GetHcackDescription(hcack));

            Item parameterList;
            if (hcack == HostCommandAck.InvalidParameter && errorParameters != null)
            {
                var paramItems = errorParameters.Select(param =>
                    Item.L(
                        Item.A(param.name),
                        Item.A(param.reason)
                    )
                ).ToArray();

                parameterList = Item.L(paramItems);
            }
            else
            {
                parameterList = Item.L(); // 空列表
            }

            return new SecsMessage(2, 42, false)
            {
                Name = "Host Command Acknowledge",
                SecsItem = Item.L(
                    Item.B(ackCode),
                    parameterList
                )
            };
        }

        /// <summary>
        /// 获取HCACK代码描述
        /// </summary>
        private string GetHcackDescription(HostCommandAck hcack)
        {
            return hcack switch
            {
                HostCommandAck.Accepted => "命令已接受",
                HostCommandAck.InvalidCommand => "命令无效",
                HostCommandAck.CannotPerform => "无法执行",
                HostCommandAck.InvalidParameter => "参数无效",
                HostCommandAck.WillExecute => "将执行",
                HostCommandAck.Rejected => "命令拒绝",
                _ => $"未知代码: {(byte)hcack}"
            };
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 释放处理器特定资源
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected override void DisposeCore(bool disposing)
        {
            if (disposing)
            {
                _commandExecutionLock?.Dispose();
            }
        }

        #endregion

        #region 辅助类

        /// <summary>
        /// 命令解析结果
        /// </summary>
        private class CommandParseResult
        {
            public bool IsValid { get; private set; }
            public string? Command { get; private set; }
            public Dictionary<string, object>? Parameters { get; private set; }
            public string? ErrorReason { get; private set; }
            public (string name, string reason)[]? ErrorParameters { get; private set; }

            public static CommandParseResult CreateSuccess(string command, Dictionary<string, object> parameters)
            {
                return new CommandParseResult
                {
                    IsValid = true,
                    Command = command,
                    Parameters = parameters
                };
            }

            public static CommandParseResult CreateError(string reason, params (string name, string reason)[] errorParams)
            {
                return new CommandParseResult
                {
                    IsValid = false,
                    ErrorReason = reason,
                    ErrorParameters = errorParams
                };
            }
        }

        /// <summary>
        /// 参数解析结果
        /// </summary>
        private class ParameterParseResult
        {
            public bool IsValid { get; set; }
            public string? Name { get; set; }
            public object? Value { get; set; }
            public string? ErrorReason { get; set; }
        }

        /// <summary>
        /// 远程控制权限验证结果
        /// </summary>
        private class RemoteControlAuthResult
        {
            public bool IsAuthorized { get; set; }
            public string? RejectReason { get; set; }
        }

        /// <summary>
        /// 命令验证结果
        /// </summary>
        private class CommandValidationResult
        {
            public bool IsValid { get; private set; }
            public HostCommandAck HcackCode { get; private set; }
            public string? ErrorReason { get; private set; }
            public (string name, string reason)[]? ErrorParameters { get; private set; }

            public static CommandValidationResult CreateValid()
            {
                return new CommandValidationResult { IsValid = true };
            }

            public static CommandValidationResult CreateInvalidCommand(string reason)
            {
                return new CommandValidationResult
                {
                    IsValid = false,
                    HcackCode = HostCommandAck.InvalidCommand,
                    ErrorReason = reason
                };
            }

            public static CommandValidationResult CreateCannotPerform(string reason)
            {
                return new CommandValidationResult
                {
                    IsValid = false,
                    HcackCode = HostCommandAck.CannotPerform,
                    ErrorReason = reason
                };
            }

            public static CommandValidationResult CreateInvalidParameter(string reason, (string name, string reason)[]? errorParams)
            {
                return new CommandValidationResult
                {
                    IsValid = false,
                    HcackCode = HostCommandAck.InvalidParameter,
                    ErrorReason = reason,
                    ErrorParameters = errorParams
                };
            }
        }

        /// <summary>
        /// 状态验证结果
        /// </summary>
        private class StateValidationResult
        {
            public bool IsValid { get; set; }
            public string? ErrorReason { get; set; }
        }

        /// <summary>
        /// 参数验证结果
        /// </summary>
        private class ParameterValidationResult
        {
            public bool IsValid { get; set; }
            public string? ErrorReason { get; set; }
            public (string name, string reason)[]? ErrorParameters { get; set; }
        }

        /// <summary>
        /// 命令执行结果
        /// </summary>
        private class CommandExecutionResult
        {
            public HostCommandAck HcackCode { get; private set; }
            public string? ErrorReason { get; private set; }
            public (string name, string reason)[]? ErrorParameters { get; private set; }

            public static CommandExecutionResult CreateSuccess()
            {
                return new CommandExecutionResult { HcackCode = HostCommandAck.Accepted };
            }

            public static CommandExecutionResult CreateFailure(HostCommandAck hcack, string? reason)
            {
                return new CommandExecutionResult
                {
                    HcackCode = hcack,
                    ErrorReason = reason
                };
            }
        }

        #endregion
    }
}
