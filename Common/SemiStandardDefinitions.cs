// 文件路径: src/Common/SemiStandardDefinitions.cs
// 版本: v3.0.0
// 描述: SEMI标准ID定义 - 统一管理SVID、CEID、ALID、ECID等标准ID

namespace Common.SemiStandard
{
    /// <summary>
    /// SEMI标准ID定义
    /// 基于SEMI E5和E30标准
    /// </summary>
    public static class SemiStandardDefinitions
    {
        #region 状态变量ID (SVID) - Status Variable ID

        /// <summary>
        /// SEMI E30标准定义的状态变量
        /// </summary>
        #region 状态变量ID (SVID) - Status Variable ID

        /// <summary>
        /// SEMI E30标准定义的状态变量
        /// </summary>
        public static class Svid
        {
            // ========== SEMI标准SVID (1-999) ==========
            
            /// <summary>已启用事件列表</summary>
            public const uint EventsEnabled = 280;
            
            /// <summary>已启用报警列表</summary>
            public const uint AlarmsEnabled = 490;
            
            /// <summary>当前激活的报警列表</summary>
            public const uint AlarmsSet = 491;
            
            /// <summary>主机设置的时间</summary>
            public const uint HostClock = 670;
            
            /// <summary>当前时钟</summary>
            public const uint Clock = 672;
            
            /// <summary>控制模式 (0=OFF-LINE, 1=ON-LINE LOCAL, 2=ON-LINE REMOTE)</summary>
            public const uint ControlMode = 720;
            
            /// <summary>控制状态 (1-5)</summary>
            public const uint ControlState = 721;

            // ========== 设备特定SVID (10000+) ==========
            
            /// <summary>端口ID</summary>
            public const uint PortID = 10001;
            
            /// <summary>Cassette ID</summary>
            public const uint CassetteID = 10002;
            
            /// <summary>批次ID</summary>
            public const uint LotID = 10003;
            
            /// <summary>配方ID</summary>
            public const uint PPID = 10004;
            
            /// <summary>Cassette槽位映射</summary>
            public const uint CassetteSlotMap = 10005;
            
            /// <summary>已处理数量</summary>
            public const uint ProcessedCount = 10006;
            
            /// <summary>划刀/裂刀型号</summary>
            public const uint KnifeModel = 10007;
            
            /// <summary>划刀/裂刀使用次数</summary>
            public const uint UseNO = 10008;
            
            /// <summary>划刀/裂刀使用最大次数限制</summary>
            public const uint UseMAXNO = 10009;
            
            /// <summary>当前bar进度</summary>
            public const uint ProgressBar = 10010;
            
            /// <summary>当前Frame下的BAR条总数</summary>
            public const uint BARNO = 10011;
            
            /// <summary>当前动作中的BAR数</summary>
            public const uint CurrentBAR = 10012;
            
            /// <summary>RFID内容</summary>
            public const uint RFID = 10013;
            
            /// <summary>扫码内容</summary>
            public const uint QRContent = 10014;
            
            /// <summary>取环所在层</summary>
            public const uint GetFrameLY = 10015;
            
            /// <summary>放环所在层</summary>
            public const uint PutFrameLY = 10016;

            /// <summary>
            /// 获取所有定义的SVID列表
            /// </summary>
            public static readonly uint[] AllSvids = new uint[]
            {
                EventsEnabled, AlarmsEnabled, AlarmsSet, HostClock, Clock, ControlMode, ControlState,
                PortID, CassetteID, LotID, PPID, CassetteSlotMap, ProcessedCount,
                KnifeModel, UseNO, UseMAXNO, ProgressBar, BARNO, CurrentBAR,
                RFID, QRContent, GetFrameLY, PutFrameLY
            };
        }

        #endregion

        #endregion

        #region 采集事件ID (CEID) - Collection Event ID

        /// <summary>
        /// 采集事件ID定义
        /// </summary>
        public static class Ceid
        {
            // ========== 控制状态事件 (200-299) ==========
            
            /// <summary>离线状态通知</summary>
            public const uint ControlStateOFFLINE = 200;
            
            /// <summary>本地在线状态通知</summary>
            public const uint ControlStateLOCAL = 201;
            
            /// <summary>远程在线状态通知</summary>
            public const uint ControlStateREMOTE = 202;
            
            /// <summary>设备常量改变</summary>
            public const uint OperatorEquipmentConstantChange = 220;
            
            /// <summary>Terminal消息确认通知</summary>
            public const uint MessageRecognition = 240;

            // ========== 物料处理事件 (11000-11099) ==========
            
            /// <summary>物料到达</summary>
            public const uint MaterialArrival = 11000;
            
            /// <summary>物料移除</summary>
            public const uint MaterialRemoved = 11001;
            
            /// <summary>映射完成</summary>
            public const uint MapComplete = 11002;
            
            /// <summary>配方选择</summary>
            public const uint PPSelected = 11003;
            
            /// <summary>处理开始</summary>
            public const uint ProcessStart = 11004;
            
            /// <summary>处理结束</summary>
            public const uint ProcessEnd = 11005;

            // ========== 视觉和对位事件 (11006-11010) ==========
            
            /// <summary>图像搜索</summary>
            public const uint PictureSearch = 11006;
            
            /// <summary>图像对位</summary>
            public const uint ParaPosition = 11007;
            
            /// <summary>图像对中</summary>
            public const uint Centring = 11008;
            
            /// <summary>晶面归零</summary>
            public const uint PlaneZeroing = 11009;
            
            /// <summary>扫码结束</summary>
            public const uint FNSQR = 11010;

            // ========== 槽位和Frame事件 (11011-11018) ==========
            
            /// <summary>槽位检测完成</summary>
            public const uint SlotMapEnd = 11011;
            
            /// <summary>环启动</summary>
            public const uint FrameStart = 11012;
            
            /// <summary>环结束</summary>
            public const uint FrameEnd = 11013;
            
            /// <summary>Cassette已启动</summary>
            public const uint CassetteStart = 11014;
            
            /// <summary>刀卸下</summary>
            public const uint KnifeUnload = 11015;
            
            /// <summary>刀具安装</summary>
            public const uint KnifeInstall = 11017;
            
            /// <summary>环启动（备用）</summary>
            public const uint FrameStartAlt = 11018;
        }

        #endregion

        #region 报警ID (ALID) - Alarm ID

        /// <summary>
        /// 报警ID定义
        /// </summary>
        public static class Alid
        {
            // ========== 紧急和安全报警 (12000-12099) ==========
            
            /// <summary>设备急停</summary>
            public const uint EMERGENCY = 12000;
            
            /// <summary>门开</summary>
            public const uint DOOR_COVER_INTERLOCK = 12037;
            
            /// <summary>互锁报警</summary>
            public const uint INTERLOCK = 12053;

            // ========== 轴驱动报警 (12003-12029) ==========
            
            /// <summary>Y轴电机故障</summary>
            public const uint Y_AXIS_MOTOR_ERROR = 12003;
            
            /// <summary>Z轴电机故障</summary>
            public const uint Z_AXIS_MOTOR_ERROR = 12004;
            
            /// <summary>X轴电机故障</summary>
            public const uint X_AXIS_MOTOR_ERROR = 12005;
            
            /// <summary>θ轴电机故障</summary>
            public const uint T_AXIS_MOTOR_ERROR = 12006;
            
            /// <summary>Y轴驱动故障</summary>
            public const uint Y_AXIS_SERVO_ERROR = 12012;
            
            /// <summary>X轴驱动故障</summary>
            public const uint X_AXIS_SERVO_ERROR = 12013;
            
            /// <summary>Y轴驱动警告</summary>
            public const uint Y_AXIS_SERVO_WARNING = 12014;
            
            /// <summary>X轴驱动警告</summary>
            public const uint X_AXIS_SERVO_WARNING = 12015;
            
            /// <summary>Z轴电机驱动报警</summary>
            public const uint Z_AXIS_DRIVER_ALARM = 12016;
            
            /// <summary>荷重轴电机报警</summary>
            public const uint FORCE_AXIS_MOTOR_ALARM = 12017;
            
            /// <summary>θ轴驱动故障</summary>
            public const uint T_AXIS_SERVO_ERROR = 12029;

            // ========== 轴限位报警 (12019-12028) ==========
            
            /// <summary>Y轴负限位</summary>
            public const uint Y_AXIS_CCW_LIMIT = 12019;
            
            /// <summary>Y轴正限位</summary>
            public const uint Y_AXIS_CW_LIMIT = 12020;
            
            /// <summary>Z轴负限位</summary>
            public const uint Z_AXIS_CCW_LIMIT = 12021;
            
            /// <summary>Z轴正限位</summary>
            public const uint Z_AXIS_CW_LIMIT = 12022;
            
            /// <summary>X轴负限位</summary>
            public const uint X_AXIS_CCW_LIMIT = 12023;
            
            /// <summary>X轴正限位</summary>
            public const uint X_AXIS_CW_LIMIT = 12024;
            
            /// <summary>θ轴负限位</summary>
            public const uint T_AXIS_CCW_LIMIT = 12025;
            
            /// <summary>θ轴正限位</summary>
            public const uint T_AXIS_CW_LIMIT = 12026;
            
            /// <summary>WT轴负限位</summary>
            public const uint FORCE_AXIS_CCW_LIMIT = 12027;
            
            /// <summary>WT轴正限位</summary>
            public const uint FORCE_AXIS_CW_LIMIT = 12028;

            // ========== 系统报警 (12007-12036) ==========
            
            /// <summary>真空气压低</summary>
            public const uint VAC_LEVEL_LOWER = 12007;
            
            /// <summary>PLC电池电量低</summary>
            public const uint BATTERY_LOW = 12018;
            
            /// <summary>气压低故障</summary>
            public const uint AIR_PRESSURE_ERROR = 12030;
            
            /// <summary>离子风机未打开</summary>
            public const uint ION_BLOWER_CLOSE = 12031;
            
            /// <summary>夹爪未做避让</summary>
            public const uint CLAW_NO_DODGING = 12032;
            
            /// <summary>摆臂负压报警</summary>
            public const uint VAC_2_ERROR = 12033;

            // ========== 加工报警 (12001-12011, 12034-12036) ==========
            
            /// <summary>执行完成</summary>
            public const uint AUTO_ACTION_END = 12001;
            
            /// <summary>划刀连锁(切刀位置低)</summary>
            public const uint CUT_INTERLOCK = 12002;
            
            /// <summary>1列执行完成</summary>
            public const uint LINE_CUT_END = 12008;
            
            /// <summary>荷重序号未选择</summary>
            public const uint FORCE_NUMBER_NOT_SELECT = 12009;
            
            /// <summary>原点回归未完成</summary>
            public const uint NO_REF = 12010;
            
            /// <summary>未设定第1/2面的划线位置</summary>
            public const uint NO_SET_POSITION = 12011;
            
            /// <summary>环未更换报警</summary>
            public const uint RING_UNEXCHANGE_ERROR = 12034;
            
            /// <summary>荷重4点学习未注册</summary>
            public const uint WT_NOT_REGISTERED = 12035;
            
            /// <summary>自动启动(连锁无效)</summary>
            public const uint AUTO_INTERLOCK_DISABLE = 12036;

            // ========== 视觉报警 (12038-12051) ==========
            
            /// <summary>对中报错</summary>
            public const uint CENTERING_ERROR = 12038;
            
            /// <summary>视觉通信错误</summary>
            public const uint VISION_COM_ERROR = 12039;
            
            /// <summary>θ补正值超出</summary>
            public const uint T_CORRECTION_VALUE_OVER = 12040;
            
            /// <summary>自动定位完成(暂停模式)</summary>
            public const uint AUTO_END_PAUSE_MODE = 12041;
            
            /// <summary>θ轴对位结束(分片模式)</summary>
            public const uint T_ALIGNMENT_END = 12042;
            
            /// <summary>晶片搜索错误</summary>
            public const uint WAFER_SEARCH_ERROR = 12043;
            
            /// <summary>右芯片边缘搜索错误</summary>
            public const uint RIGHT_CE_SEARCH_ERROR = 12044;
            
            /// <summary>左芯片边缘搜索错误</summary>
            public const uint LEFT_CE_SEARCH_ERROR = 12045;
            
            /// <summary>BAR条搜索错误</summary>
            public const uint BAR_SEARCH_ERROR = 12046;
            
            /// <summary>图像搜索错误</summary>
            public const uint PATTERN_SEARCH_ERROR = 12047;
            
            /// <summary>边缘搜索错误</summary>
            public const uint EDGE_SEARCH_ERROR = 12048;
            
            /// <summary>对边搜索错误</summary>
            public const uint PAIR_EDGE_SEARCH_ERROR = 12049;
            
            /// <summary>下芯片边缘搜索错误</summary>
            public const uint LOWER_CE_SEARCH_ERROR = 12050;
            
            /// <summary>上芯片边缘搜索错误</summary>
            public const uint UPPER_CE_SEARCH_ERROR = 12051;

            // ========== 检查报警 (12052-12055) ==========
            
            /// <summary>无环报错</summary>
            public const uint RING_CHECK_ERROR = 12052;
            
            /// <summary>1面裂片完成</summary>
            public const uint SIDE1_CUT_END = 12054;
            
            /// <summary>下料数量与上料数量不一致</summary>
            public const uint NO_DETECTION_ERROR = 12055;

            // ========== 裂片机专用报警 (12071-12096) ==========
            
            /// <summary>角轴电机正限位</summary>
            public const uint G_AXIS_CW_LIMIT = 12071;
            
            /// <summary>AZ1轴驱动故障</summary>
            public const uint AZ1_AXIS_SERVO_ERROR = 12072;
            
            /// <summary>AZ2轴驱动故障</summary>
            public const uint AZ2_AXIS_SERVO_ERROR = 12073;
            
            /// <summary>AY轴驱动故障</summary>
            public const uint AY_AXIS_SERVO_ERROR = 12074;
            
            /// <summary>AX轴驱动故障</summary>
            public const uint AX_AXIS_SERVO_ERROR = 12075;

            // 叠放报警
            
            /// <summary>第一层叠放</summary>
            public const uint FIRST_RING_STACK = 12084;
            
            /// <summary>第二层叠放</summary>
            public const uint SECOND_RING_STACK = 12085;
            
            /// <summary>第三层叠放</summary>
            public const uint THIRD_RING_STACK = 12086;
            
            /// <summary>第四层叠放</summary>
            public const uint FORTH_RING_STACK = 12087;
            
            /// <summary>第五层叠放</summary>
            public const uint FIFTH_RING_STACK = 12088;
            
            /// <summary>第六层叠放</summary>
            public const uint SIXTH_RING_STACK = 12089;
            
            /// <summary>第七层叠放</summary>
            public const uint SEVENTH_RING_STACK = 12090;

            // 错环报警
            
            /// <summary>1、2层错环</summary>
            public const uint LAYERS_1_2_STAGGERED = 12091;
            
            /// <summary>2、3层错环</summary>
            public const uint LAYERS_2_3_STAGGERED = 12092;
            
            /// <summary>3、4层错环</summary>
            public const uint LAYERS_3_4_STAGGERED = 12093;
            
            /// <summary>4、5层错环</summary>
            public const uint LAYERS_4_5_STAGGERED = 12094;
            
            /// <summary>5、6层错环</summary>
            public const uint LAYERS_5_6_STAGGERED = 12095;
            
            /// <summary>6、7层错环</summary>
            public const uint LAYERS_6_7_STAGGERED = 12096;
        }

        #endregion

        #region 设备常量ID (ECID) - Equipment Constant ID

        /// <summary>
        /// 设备常量ID定义
        /// </summary>
        public static class Ecid
        {
            // SEMI标准要求的ECID
            /// <summary>初始化通讯延迟定时器(秒)</summary>
            public const uint EstablishCommunicationsTimeout = 250;
            
            /// <summary>增强型事件报告标志 (0=S6F11/S6F9, 1=S6F13/S6F3)</summary>
            public const uint AnnotateEventReport = 310;
            
            /// <summary>事件配置标志 (0=GEM报告事件, 1=非GEM报告事件)</summary>
            public const uint ConfigEvents = 311;
            
            /// <summary>时间格式 (0=12字节格式, 1=16字节格式)</summary>
            public const uint TimeFormat = 675;

            // 设备配置常量 (1-99)
            public const uint DeviceId = 1;                     // 设备ID
            public const uint ModelName = 2;                    // 设备型号
            public const uint Manufacturer = 3;                 // 制造商
            public const uint SerialNumber = 4;                 // 序列号
            public const uint SoftwareVersion = 5;              // 软件版本
            public const uint HardwareVersion = 6;              // 硬件版本
            public const uint MaxWaferSize = 10;                // 最大晶圆尺寸
            public const uint MinWaferSize = 11;                // 最小晶圆尺寸

            // 通信参数 (100-199)
            public const uint T3Timeout = 100;                  // T3超时（Reply）
            public const uint T5Timeout = 101;                  // T5超时（Connect）
            public const uint T6Timeout = 102;                  // T6超时（Control）
            public const uint T7Timeout = 103;                  // T7超时（Not Selected）
            public const uint T8Timeout = 104;                  // T8超时（Network）
            public const uint LinkTestInterval = 110;           // LinkTest间隔
            public const uint RetryLimit = 111;                 // 重试限制
            public const uint MaxSpoolSize = 112;               // 最大缓存大小

            // 工艺参数限值 (200-299)
            public const uint MaxCuttingSpeed = 200;            // 最大切割速度
            public const uint MinCuttingSpeed = 201;            // 最小切割速度
            public const uint MaxCuttingPressure = 202;         // 最大切割压力
            public const uint MinCuttingPressure = 203;         // 最小切割压力
            public const uint MaxSpindleSpeed = 204;            // 最大主轴转速
            public const uint MinSpindleSpeed = 205;            // 最小主轴转速
            public const uint MaxCoolingFlow = 206;             // 最大冷却流量
            public const uint MinCoolingFlow = 207;             // 最小冷却流量
            public const uint MaxVacuumPressure = 208;          // 最大真空压力
            public const uint MinVacuumPressure = 209;          // 最小真空压力
            public const uint MaxTemperature = 210;             // 最大温度
            public const uint MinTemperature = 211;             // 最小温度

            // 维护参数 (300-399)
            public const uint MaintenanceInterval = 300;        // 保养间隔
            public const uint KnifeLifeLimit = 301;            // 刀具寿命限制
            public const uint KnifeWarningThreshold = 302;     // 刀具预警阈值
            public const uint CleaningInterval = 303;          // 清洁间隔
            public const uint CalibrationInterval = 304;       // 校准间隔
            public const uint FilterLifeLimit = 305;           // 过滤器寿命
            public const uint LubricationInterval = 306;       // 润滑间隔

            // 划裂片专用参数 (1000-1999)
            public const uint DefaultCuttingSpeed = 1000;      // 默认切割速度
            public const uint DefaultCuttingPressure = 1001;   // 默认切割压力
            public const uint DefaultSpindleSpeed = 1002;      // 默认主轴转速
            public const uint KerfWidth = 1003;                // 切割槽宽度
            public const uint ChuckVacuumLevel = 1004;         // 吸盘真空度
            public const uint AlignmentTolerance = 1005;       // 对准容差
            public const uint EdgeExclusion = 1006;            // 边缘排除区
            public const uint DicingMode = 1007;               // 切割模式
            public const uint CoolingMode = 1008;              // 冷却模式
            public const uint CleaningMode = 1009;             // 清洁模式
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取报警优先级
        /// </summary>
        public static AlarmPriority GetAlarmPriority(uint alid)
        {
            // 紧急报警
            if (alid == Alid.EMERGENCY || alid == Alid.DOOR_COVER_INTERLOCK || alid == Alid.INTERLOCK)
                return AlarmPriority.Critical;

            // 驱动和限位报警
            if ((alid >= 12003 && alid <= 12029) || (alid >= 12071 && alid <= 12075))
                return AlarmPriority.Major;

            // 系统报警
            if ((alid >= 12007 && alid <= 12036) || (alid >= 12084 && alid <= 12096))
                return AlarmPriority.Minor;

            // 视觉和检查报警
            if (alid >= 12038 && alid <= 12055)
                return AlarmPriority.Warning;

            return AlarmPriority.Info;
        }

        /// <summary>
        /// 获取事件类别
        /// </summary>
        public static EventCategory GetEventCategory(uint ceid)
        {
            if (ceid >= 200 && ceid <= 299)
                return EventCategory.ControlState;
            
            if (ceid >= 11000 && ceid <= 11005)
                return EventCategory.MaterialHandling;
            
            if (ceid >= 11006 && ceid <= 11010)
                return EventCategory.VisionAlignment;
            
            if (ceid >= 11011 && ceid <= 11018)
                return EventCategory.SlotFrame;

            return EventCategory.Unknown;
        }

        #endregion

        #region 枚举定义

        /// <summary>
        /// 报警优先级
        /// </summary>
        public enum AlarmPriority
        {
            /// <summary>信息</summary>
            Info = 0,
            /// <summary>警告</summary>
            Warning = 1,
            /// <summary>次要</summary>
            Minor = 2,
            /// <summary>主要</summary>
            Major = 3,
            /// <summary>严重</summary>
            Critical = 4
        }

        /// <summary>
        /// 事件类别
        /// </summary>
        public enum EventCategory
        {
            /// <summary>未知</summary>
            Unknown,
            /// <summary>控制状态</summary>
            ControlState,
            /// <summary>物料处理</summary>
            MaterialHandling,
            /// <summary>视觉对位</summary>
            VisionAlignment,
            /// <summary>槽位和Frame</summary>
            SlotFrame
        }

        #endregion
    }
}
