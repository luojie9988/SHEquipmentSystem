using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 设备初始化管理器接口
    /// 管理SEMI E30标准的初始化流程
    /// </summary>
    public interface IInitializationManager
    {
        /// <summary>
        /// 获取当前初始化状态
        /// </summary>
        InitializationManager.InitializationState CurrentState { get; }
        
        /// <summary>
        /// 获取初始化是否完成
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// 初始化进度事件
        /// </summary>
        event EventHandler<InitializationProgressEventArgs>? InitializationProgress;
        
        /// <summary>
        /// 初始化完成事件
        /// </summary>
        event EventHandler<InitializationCompleteEventArgs>? InitializationComplete;
        
        /// <summary>
        /// 执行完整的初始化流程
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>初始化是否成功</returns>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 重置初始化状态
        /// </summary>
        Task ResetAsync();
        
        /// <summary>
        /// 获取初始化进度百分比
        /// </summary>
        /// <returns>进度百分比（0-100）</returns>
        int GetInitializationProgress();
        
        /// <summary>
        /// 获取初始化状态信息
        /// </summary>
        /// <returns>状态信息</returns>
        InitializationStatus GetStatus();
    }
    

    

}
