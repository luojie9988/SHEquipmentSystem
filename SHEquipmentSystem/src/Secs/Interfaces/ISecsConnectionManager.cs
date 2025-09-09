// 文件路径: src/DiceEquipmentSystem/Secs/Interfaces/ISecsConnectionManager.cs
// 版本: v1.0.0
// 描述: SECS连接管理器接口定义

using DiceEquipmentSystem.Core.Enums;
using Secs4Net;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// SECS连接管理器接口
    /// 管理设备端的HSMS连接和SECS消息通信
    /// </summary>
    public interface ISecsConnectionManager
    {
        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// 主动消息接收事件（从主机接收）
        /// </summary>
        event EventHandler<PrimaryMessageReceivedEventArgs>? PrimaryMessageReceived;

        /// <summary>
        /// 通信错误事件
        /// </summary>
        event EventHandler<CommunicationErrorEventArgs>? CommunicationError;

        /// <summary>
        /// 启动连接（作为Active或Passive模式）
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止连接
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送SECS消息并等待响应
        /// </summary>
        Task<SecsMessage?> SendMessageAsync(SecsMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送SECS消息但不等待响应
        /// </summary>
        Task SendWithoutReplyAsync(SecsMessage message);

        /// <summary>
        /// 检查是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 检查是否已选中(Selected)
        /// </summary>
        bool IsSelected { get; }

        /// <summary>
        /// 获取连接状态
        /// </summary>
        HsmsConnectionState HsmsConnectionState { get; }

        /// <summary>
        /// 获取SECS配置
        /// </summary>
        object GetConfiguration();
    }

    /// <summary>
    /// 连接状态变更事件参数
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>新状态</summary>
        public HsmsConnectionState NewState { get; set; }

        /// <summary>旧状态</summary>
        public HsmsConnectionState OldState { get; set; }

        /// <summary>变更原因</summary>
        public string Reason { get; set; } = "";

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 主动消息接收事件参数
    /// </summary>
    public class PrimaryMessageReceivedEventArgs : EventArgs
    {
        /// <summary>接收到的消息</summary>
        public SecsMessage Message { get; set; } = null!;

        /// <summary>消息包装器（用于回复）</summary>
        public PrimaryMessageWrapper? MessageWrapper { get; set; }

        /// <summary>时间戳</summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 通信错误事件参数
    /// </summary>
    public class CommunicationErrorEventArgs : EventArgs
    {
        /// <summary>错误消息</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>异常对象</summary>
        public Exception? Exception { get; set; }

        /// <summary>错误类型</summary>
        public CommunicationErrorType ErrorType { get; set; }

        /// <summary>错误时间</summary>
        public DateTime ErrorTime { get; set; }
    }

    /// <summary>
    /// 通信错误类型
    /// </summary>
    public enum CommunicationErrorType
    {
        /// <summary>未知错误</summary>
        Unknown,

        /// <summary>连接丢失</summary>
        ConnectionLost,

        /// <summary>超时</summary>
        Timeout,

        /// <summary>协议错误</summary>
        ProtocolError,

        /// <summary>取消</summary>
        Cancelled
    }
}
