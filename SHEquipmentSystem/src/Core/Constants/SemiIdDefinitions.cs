// 文件路径: src/DiceEquipmentSystem/Core/Constants/SemiIdDefinitions.cs
// 版本: v3.3.0
// 描述: 设备端SEMI ID定义 - 引用Common统一定义

using Common.SemiStandard;

namespace DiceEquipmentSystem.Core.Constants
{
    /// <summary>
    /// 设备端SEMI ID定义引用
    /// 所有定义从Common.SemiStandard统一获取，确保设备端和主机端一致性
    /// </summary>
    public static class SemiIdDefinitions
    {
        #region SVID定义引用
        
        /// <summary>
        /// 状态变量ID定义
        /// </summary>
        public static class Svid
        {
            // SEMI标准SVID (280-721)
            public const uint EventsEnabled = SemiStandardDefinitions.Svid.EventsEnabled;
            public const uint AlarmsEnabled = SemiStandardDefinitions.Svid.AlarmsEnabled;
            public const uint AlarmsSet = SemiStandardDefinitions.Svid.AlarmsSet;
            public const uint HostClock = SemiStandardDefinitions.Svid.HostClock;
            public const uint Clock = SemiStandardDefinitions.Svid.Clock;
            public const uint ControlMode = SemiStandardDefinitions.Svid.ControlMode;
            public const uint ControlState = SemiStandardDefinitions.Svid.ControlState;

            // 设备特定SVID (10001-10016)
            public const uint PortID = SemiStandardDefinitions.Svid.PortID;
            public const uint CassetteID = SemiStandardDefinitions.Svid.CassetteID;
            public const uint LotID = SemiStandardDefinitions.Svid.LotID;
            public const uint PPID = SemiStandardDefinitions.Svid.PPID;
            public const uint CassetteSlotMap = SemiStandardDefinitions.Svid.CassetteSlotMap;
            public const uint ProcessedCount = SemiStandardDefinitions.Svid.ProcessedCount;
            public const uint KnifeModel = SemiStandardDefinitions.Svid.KnifeModel;
            public const uint UseNO = SemiStandardDefinitions.Svid.UseNO;
            public const uint UseMAXNO = SemiStandardDefinitions.Svid.UseMAXNO;
            public const uint ProgressBar = SemiStandardDefinitions.Svid.ProgressBar;
            public const uint BARNO = SemiStandardDefinitions.Svid.BARNO;
            public const uint CurrentBAR = SemiStandardDefinitions.Svid.CurrentBAR;
            public const uint RFID = SemiStandardDefinitions.Svid.RFID;
            public const uint QRContent = SemiStandardDefinitions.Svid.QRContent;
            public const uint GetFrameLY = SemiStandardDefinitions.Svid.GetFrameLY;
            public const uint PutFrameLY = SemiStandardDefinitions.Svid.PutFrameLY;

            /// <summary>
            /// 获取所有定义的SVID列表
            /// </summary>
            public static readonly uint[] AllSvids = SemiStandardDefinitions.Svid.AllSvids;
        }

        #endregion

        #region CEID定义引用
        
        /// <summary>
        /// 采集事件ID定义
        /// </summary>
        public static class Ceid
        {
            // 控制状态事件 (200-240)
            public const uint ControlStateOFFLINE = SemiStandardDefinitions.Ceid.ControlStateOFFLINE;
            public const uint ControlStateLOCAL = SemiStandardDefinitions.Ceid.ControlStateLOCAL;
            public const uint ControlStateREMOTE = SemiStandardDefinitions.Ceid.ControlStateREMOTE;
            public const uint OperatorEquipmentConstantChange = SemiStandardDefinitions.Ceid.OperatorEquipmentConstantChange;
            public const uint MessageRecognition = SemiStandardDefinitions.Ceid.MessageRecognition;

            // 物料处理事件 (11000-11005)
            public const uint MaterialArrival = SemiStandardDefinitions.Ceid.MaterialArrival;
            public const uint MaterialRemoved = SemiStandardDefinitions.Ceid.MaterialRemoved;
            public const uint MapComplete = SemiStandardDefinitions.Ceid.MapComplete;
            public const uint PPSelected = SemiStandardDefinitions.Ceid.PPSelected;
            public const uint ProcessStart = SemiStandardDefinitions.Ceid.ProcessStart;
            public const uint ProcessEnd = SemiStandardDefinitions.Ceid.ProcessEnd;

            // 视觉和对位事件 (11006-11010)
            public const uint PictureSearch = SemiStandardDefinitions.Ceid.PictureSearch;
            public const uint ParaPosition = SemiStandardDefinitions.Ceid.ParaPosition;
            public const uint Centring = SemiStandardDefinitions.Ceid.Centring;
            public const uint PlaneZeroing = SemiStandardDefinitions.Ceid.PlaneZeroing;
            public const uint FNSQR = SemiStandardDefinitions.Ceid.FNSQR;

            // 槽位和Frame事件 (11011-11018)
            public const uint SlotMapEnd = SemiStandardDefinitions.Ceid.SlotMapEnd;
            public const uint FrameStart = SemiStandardDefinitions.Ceid.FrameStart;
            public const uint FrameEnd = SemiStandardDefinitions.Ceid.FrameEnd;
            public const uint CassetteStart = SemiStandardDefinitions.Ceid.CassetteStart;
            public const uint KnifeUnload = SemiStandardDefinitions.Ceid.KnifeUnload;
            public const uint KnifeInstall = SemiStandardDefinitions.Ceid.KnifeInstall;
            public const uint FrameStartAlt = SemiStandardDefinitions.Ceid.FrameStartAlt;
        }

        #endregion

        #region ALID定义引用
        
        /// <summary>
        /// 报警ID定义
        /// </summary>
        public static class Alid
        {
            // 使用SemiStandardDefinitions.Alid的所有定义
            // 紧急和安全报警
            public const uint EMERGENCY = SemiStandardDefinitions.Alid.EMERGENCY;
            public const uint DOOR_COVER_INTERLOCK = SemiStandardDefinitions.Alid.DOOR_COVER_INTERLOCK;
            public const uint INTERLOCK = SemiStandardDefinitions.Alid.INTERLOCK;
            
            // 其他报警定义通过SemiStandardDefinitions.Alid访问
            
            /// <summary>
            /// 获取报警优先级
            /// </summary>
            public static AlarmPriority GetAlarmPriority(uint alid)
            {
                var priority = SemiStandardDefinitions.GetAlarmPriority(alid);
                return (AlarmPriority)(int)priority;
            }
            
            /// <summary>
            /// 获取报警类别
            /// </summary>
            public static string GetAlarmCategory(uint alid)
            {
                // 紧急报警
                if (alid == EMERGENCY || alid == DOOR_COVER_INTERLOCK || alid == INTERLOCK)
                    return "Safety";

                // 驱动和限位报警
                if ((alid >= 12003 && alid <= 12029) || (alid >= 12071 && alid <= 12075))
                    return "Motion";

                // 系统报警
                if ((alid >= 12007 && alid <= 12036) || (alid >= 12084 && alid <= 12096))
                    return "System";

                // 视觉和检查报警
                if (alid >= 12038 && alid <= 12055)
                    return "Vision";

                return "Unknown";
            }
        }

        #endregion

        #region ECID定义引用
        
        /// <summary>
        /// 设备常量ID定义
        /// </summary>
        public static class Ecid
        {
            // SEMI标准要求的ECID
            public const uint EstablishCommunicationsTimeout = SemiStandardDefinitions.Ecid.EstablishCommunicationsTimeout;
            public const uint AnnotateEventReport = SemiStandardDefinitions.Ecid.AnnotateEventReport;
            public const uint ConfigEvents = SemiStandardDefinitions.Ecid.ConfigEvents;
            public const uint TimeFormat = SemiStandardDefinitions.Ecid.TimeFormat;

            // 设备配置常量 (1-99)
            public const uint DeviceId = SemiStandardDefinitions.Ecid.DeviceId;
            public const uint ModelName = SemiStandardDefinitions.Ecid.ModelName;
            public const uint Manufacturer = SemiStandardDefinitions.Ecid.Manufacturer;
            public const uint SerialNumber = SemiStandardDefinitions.Ecid.SerialNumber;
            public const uint SoftwareVersion = SemiStandardDefinitions.Ecid.SoftwareVersion;
            public const uint HardwareVersion = SemiStandardDefinitions.Ecid.HardwareVersion;
            public const uint MaxWaferSize = SemiStandardDefinitions.Ecid.MaxWaferSize;
            public const uint MinWaferSize = SemiStandardDefinitions.Ecid.MinWaferSize;

            // 通信参数 (100-199)
            public const uint T3Timeout = SemiStandardDefinitions.Ecid.T3Timeout;
            public const uint T5Timeout = SemiStandardDefinitions.Ecid.T5Timeout;
            public const uint T6Timeout = SemiStandardDefinitions.Ecid.T6Timeout;
            public const uint T7Timeout = SemiStandardDefinitions.Ecid.T7Timeout;
            public const uint T8Timeout = SemiStandardDefinitions.Ecid.T8Timeout;
            public const uint LinkTestInterval = SemiStandardDefinitions.Ecid.LinkTestInterval;
            public const uint RetryLimit = SemiStandardDefinitions.Ecid.RetryLimit;
            public const uint MaxSpoolSize = SemiStandardDefinitions.Ecid.MaxSpoolSize;

            // 工艺参数限值 (200-299)
            public const uint MaxCuttingSpeed = SemiStandardDefinitions.Ecid.MaxCuttingSpeed;
            public const uint MinCuttingSpeed = SemiStandardDefinitions.Ecid.MinCuttingSpeed;
            public const uint MaxCuttingPressure = SemiStandardDefinitions.Ecid.MaxCuttingPressure;
            public const uint MinCuttingPressure = SemiStandardDefinitions.Ecid.MinCuttingPressure;
            public const uint MaxSpindleSpeed = SemiStandardDefinitions.Ecid.MaxSpindleSpeed;
            public const uint MinSpindleSpeed = SemiStandardDefinitions.Ecid.MinSpindleSpeed;
            public const uint MaxCoolingFlow = SemiStandardDefinitions.Ecid.MaxCoolingFlow;
            public const uint MinCoolingFlow = SemiStandardDefinitions.Ecid.MinCoolingFlow;
            public const uint MaxVacuumPressure = SemiStandardDefinitions.Ecid.MaxVacuumPressure;
            public const uint MinVacuumPressure = SemiStandardDefinitions.Ecid.MinVacuumPressure;
            public const uint MaxTemperature = SemiStandardDefinitions.Ecid.MaxTemperature;
            public const uint MinTemperature = SemiStandardDefinitions.Ecid.MinTemperature;

            // 维护参数 (300-399)
            public const uint MaintenanceInterval = SemiStandardDefinitions.Ecid.MaintenanceInterval;
            public const uint KnifeLifeLimit = SemiStandardDefinitions.Ecid.KnifeLifeLimit;
            public const uint KnifeWarningThreshold = SemiStandardDefinitions.Ecid.KnifeWarningThreshold;
            public const uint CleaningInterval = SemiStandardDefinitions.Ecid.CleaningInterval;
            public const uint CalibrationInterval = SemiStandardDefinitions.Ecid.CalibrationInterval;
            public const uint FilterLifeLimit = SemiStandardDefinitions.Ecid.FilterLifeLimit;
            public const uint LubricationInterval = SemiStandardDefinitions.Ecid.LubricationInterval;

            // 划裂片专用参数 (1000-1999)
            public const uint DefaultCuttingSpeed = SemiStandardDefinitions.Ecid.DefaultCuttingSpeed;
            public const uint DefaultCuttingPressure = SemiStandardDefinitions.Ecid.DefaultCuttingPressure;
            public const uint DefaultSpindleSpeed = SemiStandardDefinitions.Ecid.DefaultSpindleSpeed;
            public const uint KerfWidth = SemiStandardDefinitions.Ecid.KerfWidth;
            public const uint ChuckVacuumLevel = SemiStandardDefinitions.Ecid.ChuckVacuumLevel;
            public const uint AlignmentTolerance = SemiStandardDefinitions.Ecid.AlignmentTolerance;
            public const uint EdgeExclusion = SemiStandardDefinitions.Ecid.EdgeExclusion;
            public const uint DicingMode = SemiStandardDefinitions.Ecid.DicingMode;
            public const uint CoolingMode = SemiStandardDefinitions.Ecid.CoolingMode;
            public const uint CleaningMode = SemiStandardDefinitions.Ecid.CleaningMode;
        }

        #endregion

        #region 辅助方法
        
        /// <summary>
        /// 获取报警优先级
        /// </summary>
        public static SemiStandardDefinitions.AlarmPriority GetAlarmPriority(uint alid)
        {
            return SemiStandardDefinitions.GetAlarmPriority(alid);
        }

        /// <summary>
        /// 获取事件类别
        /// </summary>
        public static SemiStandardDefinitions.EventCategory GetEventCategory(uint ceid)
        {
            return SemiStandardDefinitions.GetEventCategory(ceid);
        }

        #endregion
        
        #region 扩展定义
        
        /// <summary>
        /// 报警优先级枚举
        /// </summary>
        public enum AlarmPriority
        {
            Info = 0,
            Warning = 1,
            Minor = 2,
            Major = 3,
            Critical = 4
        }
        
        /// <summary>
        /// 验证器类
        /// </summary>
        public static class Validator
        {
            /// <summary>
            /// 验证ALID是否有效
            /// </summary>
            public static bool IsValidAlid(uint alid)
            {
                // 紧急和安全报警 (12001-12002)
                if (alid >= 12001 && alid <= 12002) return true;
                
                // 驱动报警 (12003-12029)
                if (alid >= 12003 && alid <= 12029) return true;
                
                // 视觉报警 (12038-12055)
                if (alid >= 12038 && alid <= 12055) return true;
                
                // 限位报警 (12071-12075)
                if (alid >= 12071 && alid <= 12075) return true;
                
                // 系统报警 (12084-12096)
                if (alid >= 12084 && alid <= 12096) return true;
                
                return false;
            }
            
            /// <summary>
            /// 验证CEID是否有效
            /// </summary>
            public static bool IsValidCeid(uint ceid)
            {
                // 控制状态事件
                if (ceid >= 200 && ceid <= 240) return true;
                
                // 物料处理事件
                if (ceid >= 11000 && ceid <= 11018) return true;
                
                return false;
            }
        }
        
        #endregion
    }
}
