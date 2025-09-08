using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Core.Constants
{
    /// <summary>
    /// 划裂片设备ECID定义
    /// </summary>
    public static class DicerECID
    {
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

        // SEMI标准要求的ECID (675)
        public const uint TimeFormat = 675;                // 时钟格式

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
}
