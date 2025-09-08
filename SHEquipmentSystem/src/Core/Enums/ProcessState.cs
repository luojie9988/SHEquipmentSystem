// 文件路径: src/DiceEquipmentSystem/Core/Enums/EquipmentEnums.cs
// 版本: v1.1.0
// 描述: 设备端枚举定义 - 基于SEMI E5/E30/E37标准

namespace DiceEquipmentSystem.Core.Enums
{
    /// <summary>
    /// 处理状态枚举 - 对应SVID 722 (ProcessState)
    /// 根据SEMI E30/E58标准定义，与Host端保持一致
    /// </summary>
    /// <remarks>
    /// 虽然定义了完整的状态集，但划裂片设备实际使用的状态子集为：
    /// - Init: 设备初始化
    /// - Idle: 等待晶圆
    /// - Setup: 加载程序/对准
    /// - Ready: 准备切割
    /// - Executing: 切割中
    /// - Pause/Paused: 暂停（可选）
    /// - Complete/Completed: 完成
    /// - Abort/Aborting/Aborted: 异常中止
    /// </remarks>
    public enum ProcessState
    {
        /// <summary>
        /// 0 = INIT (初始化)
        /// 设备正在初始化，包括系统自检、原点复位等
        /// </summary>
        Init = 0,

        /// <summary>
        /// 1 = IDLE (空闲)
        /// 设备已准备好但未处理材料，等待晶圆载入
        /// </summary>
        Idle = 1,

        /// <summary>
        /// 2 = SETUP (设置)
        /// 设备正在为处理进行设置
        /// - 加载切割程序
        /// - 晶圆对准
        /// - 刀具检查
        /// </summary>
        Setup = 2,

        /// <summary>
        /// 3 = READY (就绪)
        /// 设备已准备好开始处理
        /// - 程序已加载
        /// - 晶圆已对准
        /// - 等待START命令
        /// </summary>
        Ready = 3,

        /// <summary>
        /// 4 = EXECUTING (执行中)
        /// 设备正在处理材料
        /// - 正在进行切割
        /// - 实时位置更新
        /// - 刀具旋转中
        /// </summary>
        Executing = 4,

        /// <summary>
        /// 5 = PAUSE (暂停)
        /// 处理暂停中（过渡状态）
        /// </summary>
        Pause = 5,

        /// <summary>
        /// 6 = PAUSED (已暂停)
        /// 设备处于暂停状态
        /// - 刀具停止但未抬起
        /// - 保持当前位置
        /// - 可恢复处理
        /// </summary>
        Paused = 6,

        /// <summary>
        /// 7 = RESUME (恢复)
        /// 正在从暂停状态恢复（过渡状态）
        /// </summary>
        Resume = 7,

        /// <summary>
        /// 8 = HOLD (保持)
        /// 处理被保持（较少使用）
        /// </summary>
        Hold = 8,

        /// <summary>
        /// 9 = HELD (已保持)
        /// 设备处于保持状态（较少使用）
        /// </summary>
        Held = 9,

        /// <summary>
        /// 10 = ABORT (中止)
        /// 正在中止处理（过渡状态）
        /// </summary>
        Abort = 10,

        /// <summary>
        /// 11 = ABORTING (中止中)
        /// 处理正在被中止
        /// - 刀具抬起
        /// - 主轴停止
        /// - 返回安全位置
        /// </summary>
        Aborting = 11,

        /// <summary>
        /// 12 = ABORTED (已中止)
        /// 处理已被中止
        /// - 需要操作员介入
        /// - 可能需要重新设置
        /// </summary>
        Aborted = 12,

        /// <summary>
        /// 13 = STOP (停止)
        /// 正在停止处理（较少使用）
        /// </summary>
        Stop = 13,

        /// <summary>
        /// 14 = STOPPING (停止中)
        /// 处理正在停止（较少使用）
        /// </summary>
        Stopping = 14,

        /// <summary>
        /// 15 = STOPPED (已停止)
        /// 处理已停止（较少使用）
        /// </summary>
        Stopped = 15,

        /// <summary>
        /// 16 = COMPLETE (完成)
        /// 正在完成处理（过渡状态）
        /// </summary>
        Complete = 16,

        /// <summary>
        /// 17 = COMPLETING (完成中)
        /// 处理正在完成
        /// - 刀具返回原点
        /// - 数据保存
        /// </summary>
        Completing = 17,

        /// <summary>
        /// 18 = COMPLETED (已完成)
        /// 处理已完成
        /// - 晶圆切割完成
        /// - 等待卸载
        /// </summary>
        Completed = 18,

        /// <summary>
        /// 19 = NOT_APPLICABLE (不适用)
        /// 状态不适用于当前设备
        /// </summary>
        NotApplicable = 19
    }

    /// <summary>
    /// 处理状态辅助类
    /// </summary>
    public static class ProcessStateHelper
    {
        /// <summary>
        /// 划裂片设备实际使用的状态集
        /// </summary>
        private static readonly HashSet<ProcessState> ActuallyUsedStates = new()
        {
            ProcessState.Init,
            ProcessState.Idle,
            ProcessState.Setup,
            ProcessState.Ready,
            ProcessState.Executing,
            ProcessState.Pause,
            ProcessState.Paused,
            ProcessState.Resume,
            ProcessState.Aborting,
            ProcessState.Aborted,
            ProcessState.Completing,
            ProcessState.Completed
        };

        /// <summary>
        /// 判断是否为划裂片设备实际使用的状态
        /// </summary>
        public static bool IsActuallyUsed(ProcessState state)
        {
            return ActuallyUsedStates.Contains(state);
        }

        /// <summary>
        /// 判断是否为稳定状态（非过渡状态）
        /// </summary>
        public static bool IsStableState(ProcessState state)
        {
            return state switch
            {
                ProcessState.Init => true,
                ProcessState.Idle => true,
                ProcessState.Setup => true,
                ProcessState.Ready => true,
                ProcessState.Executing => true,
                ProcessState.Paused => true,
                ProcessState.Held => true,
                ProcessState.Aborted => true,
                ProcessState.Stopped => true,
                ProcessState.Completed => true,
                _ => false
            };
        }

        /// <summary>
        /// 判断是否为过渡状态
        /// </summary>
        public static bool IsTransientState(ProcessState state)
        {
            return !IsStableState(state);
        }

        /// <summary>
        /// 判断是否可以开始处理
        /// </summary>
        public static bool CanStartProcess(ProcessState state)
        {
            return state == ProcessState.Idle ||
                   state == ProcessState.Ready ||
                   state == ProcessState.Completed ||
                   state == ProcessState.Aborted;
        }

        /// <summary>
        /// 判断是否正在处理中
        /// </summary>
        public static bool IsProcessing(ProcessState state)
        {
            return state == ProcessState.Executing ||
                   state == ProcessState.Pause ||
                   state == ProcessState.Paused ||
                   state == ProcessState.Resume;
        }

        /// <summary>
        /// 获取状态的中文描述
        /// </summary>
        public static string GetChineseDescription(ProcessState state)
        {
            return state switch
            {
                ProcessState.Init => "初始化",
                ProcessState.Idle => "空闲",
                ProcessState.Setup => "设置中",
                ProcessState.Ready => "就绪",
                ProcessState.Executing => "执行中",
                ProcessState.Pause => "暂停",
                ProcessState.Paused => "已暂停",
                ProcessState.Resume => "恢复",
                ProcessState.Aborting => "中止中",
                ProcessState.Aborted => "已中止",
                ProcessState.Completing => "完成中",
                ProcessState.Completed => "已完成",
                _ => state.ToString()
            };
        }

        /// <summary>
        /// 转换为SECS格式字符串
        /// </summary>
        public static string ToSecsString(ProcessState state)
        {
            // SEMI标准定义的字符串格式
            return state.ToString().ToUpper();
        }
    }
}
