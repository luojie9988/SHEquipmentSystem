// 文件路径: src/DiceEquipmentSystem/PLC/Interfaces/IPlcDataProvider.cs
// 版本: v1.1.0
// 描述: PLC数据提供者接口 - 添加ExecuteAsync方法

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.PLC.Interfaces
{
    /// <summary>
    /// PLC数据提供者接口
    /// 提供与PLC系统交互的标准接口
    /// </summary>
    public interface IPlcDataProvider
    {
        #region 连接管理

        /// <summary>
        /// 连接状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接到PLC
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接是否成功</returns>
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开PLC连接
        /// </summary>
        Task DisconnectAsync();

        #endregion

        #region 数据读写

        /// <summary>
        /// 读取SVID值
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="address">PLC地址</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>读取的值</returns>
        Task<object?> ReadSvidAsync(uint svid, string address, CancellationToken cancellationToken = default);

        /// <summary>
        /// 写入ECID值
        /// </summary>
        /// <param name="ecid">设备常量ID</param>
        /// <param name="address">PLC地址</param>
        /// <param name="value">要写入的值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入是否成功</returns>
        Task<bool> WriteEcidAsync(uint ecid, string address, object value, CancellationToken cancellationToken = default);

        #endregion

        #region 命令执行

        /// <summary>
        /// 执行PLC命令
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="parameters">命令参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>执行结果</returns>
        Task<PlcExecutionResult> ExecuteAsync(string command, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行PLC命令（无参数版本）
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>执行结果</returns>
        Task<PlcExecutionResult> ExecuteAsync(string command, CancellationToken cancellationToken = default);

        #endregion

        #region 事件监控

        /// <summary>
        /// 监控PLC事件
        /// </summary>
        /// <param name="ceidAddress">事件地址映射</param>
        /// <param name="onEventTriggered">事件触发回调</param>
        void MonitorEvents(Dictionary<uint, string> ceidAddress, Action<uint> onEventTriggered);

        /// <summary>
        /// 停止事件监控
        /// </summary>
        Task StopEventMonitoringAsync();

        #endregion
    }

    /// <summary>
    /// PLC执行结果
    /// </summary>
    public class PlcExecutionResult
    {
        /// <summary>
        /// 执行是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误消息（当Success为false时）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// 返回数据（可选）
        /// </summary>
        public Dictionary<string, object>? Data { get; set; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        /// <param name="data">返回数据</param>
        /// <param name="executionTime">执行时间</param>
        /// <returns>成功结果</returns>
        public static PlcExecutionResult CreateSuccess(Dictionary<string, object>? data = null, TimeSpan? executionTime = null)
        {
            return new PlcExecutionResult
            {
                Success = true,
                Data = data,
                ExecutionTime = executionTime ?? TimeSpan.Zero
            };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <param name="executionTime">执行时间</param>
        /// <returns>失败结果</returns>
        public static PlcExecutionResult CreateFailure(string errorMessage, TimeSpan? executionTime = null)
        {
            return new PlcExecutionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ExecutionTime = executionTime ?? TimeSpan.Zero
            };
        }

        public override string ToString()
        {
            return Success ?
                $"Success (Time: {ExecutionTime.TotalMilliseconds:F0}ms)" :
                $"Failed: {ErrorMessage} (Time: {ExecutionTime.TotalMilliseconds:F0}ms)";
        }
    }
}
