// 文件路径: src/DiceEquipmentSystem/Services/StatusVariableService.cs
// 版本: v1.0.0
// 描述: 状态变量(SVID)服务 - 管理设备的状态变量

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Enums;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Services
{
    /// <summary>
    /// 状态变量服务实现
    /// 根据SEMI E5标准管理状态变量(Status Variable)
    /// </summary>
    public class StatusVariableService : IStatusVariableService
    {
        #region 字段

        private readonly ILogger<StatusVariableService> _logger;
        private readonly IEquipmentStateService _stateService;

        /// <summary>
        /// SVID定义字典
        /// </summary>
        private readonly ConcurrentDictionary<uint, SvidDefinition> _svidDefinitions;

        /// <summary>
        /// SVID值缓存
        /// </summary>
        private readonly ConcurrentDictionary<uint, object> _svidValues;

        /// <summary>
        /// SVID更新回调
        /// </summary>
        private readonly ConcurrentDictionary<uint, List<Func<uint, object, Task>>> _updateCallbacks;

        /// <summary>
        /// 值更新锁
        /// </summary>
        private readonly ReaderWriterLockSlim _valueLock = new();

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public StatusVariableService(
            ILogger<StatusVariableService> logger,
            IEquipmentStateService stateService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));

            _svidDefinitions = new ConcurrentDictionary<uint, SvidDefinition>();
            _svidValues = new ConcurrentDictionary<uint, object>();
            _updateCallbacks = new ConcurrentDictionary<uint, List<Func<uint, object, Task>>>();

            // 初始化标准SVID
            InitializeStandardSvids();

            _logger.LogInformation("状态变量服务已初始化，共定义 {Count} 个SVID", _svidDefinitions.Count);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取所有可用的SVID列表
        /// </summary>
        public async Task<List<uint>> GetAllSvidListAsync()
        {
            return await Task.Run(() => _svidDefinitions.Keys.OrderBy(k => k).ToList());
        }

        /// <summary>
        /// 获取指定SVID的值
        /// </summary>
        public async Task<object> GetSvidValueAsync(uint svid)
        {
            // 检查是否为动态SVID（需要实时获取）
            if (IsDynamicSvid(svid))
            {
                return await GetDynamicSvidValueAsync(svid);
            }

            // 从缓存获取
            _valueLock.EnterReadLock();
            try
            {
                if (_svidValues.TryGetValue(svid, out var value))
                {
                    return value;
                }

                // 如果没有缓存值，返回默认值
                if (_svidDefinitions.TryGetValue(svid, out var definition))
                {
                    return definition.DefaultValue;
                }

                _logger.LogWarning($"未定义的SVID: {svid}");
                return 0;
            }
            finally
            {
                _valueLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取状态变量值（通用方法）
        /// 为了接口兼容性提供的方法，内部调用GetSvidValueAsync
        /// </summary>
        /// <param name="vid">变量ID</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>变量值</returns>
        public async Task<object> GetStatusVariableAsync(uint vid, object? defaultValue = null)
        {
            try
            {
                // 调用已有的GetSvidValueAsync方法
                return await GetSvidValueAsync(vid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"获取状态变量 {vid} 失败，返回默认值");
                return defaultValue ?? 0;
            }
        }

        /// <summary>
        /// 批量获取SVID值
        /// </summary>
        public async Task<Dictionary<uint, object>> GetSvidValuesAsync(List<uint> svidList)
        {
            var result = new Dictionary<uint, object>();

            foreach (var svid in svidList)
            {
                try
                {
                    result[svid] = await GetSvidValueAsync(svid);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"获取SVID {svid} 失败");
                    result[svid] = 0;  // 错误时返回默认值
                }
            }

            return result;
        }

        /// <summary>
        /// 设置SVID值
        /// </summary>
        public async Task<bool> SetSvidValueAsync(uint svid, object value)
        {
            if (!_svidDefinitions.ContainsKey(svid))
            {
                _logger.LogWarning($"尝试设置未定义的SVID: {svid}");
                return false;
            }

            return await Task.Run(() =>
            {
                _valueLock.EnterWriteLock();
                try
                {
                    var oldValue = _svidValues.GetValueOrDefault(svid);
                    _svidValues[svid] = value;

                    _logger.LogDebug($"SVID {svid} 值更新: {oldValue} -> {value}");

                    // 触发更新回调
                    if (_updateCallbacks.TryGetValue(svid, out var callbacks))
                    {
                        foreach (var callback in callbacks)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await callback(svid, value);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"SVID {svid} 更新回调执行失败");
                                }
                            });
                        }
                    }

                    return true;
                }
                finally
                {
                    _valueLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 注册SVID
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="name">名称</param>
        /// <param name="defaultValue">默认值</param>
        public void RegisterSvid(uint svid, string name, object defaultValue)
        {
            RegisterSvid(svid, name, defaultValue, SvidType.Status);
        }

        /// <summary>
        /// 注册SVID（带类型）
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="name">名称</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="type">SVID类型</param>
        public void RegisterSvid(uint svid, string name, object defaultValue, SvidType type)
        {
            var definition = new SvidDefinition
            {
                Svid = svid,
                Name = name,
                DefaultValue = defaultValue,
                Type = type,
                DataType = defaultValue?.GetType() ?? typeof(object)
            };

            if (_svidDefinitions.TryAdd(svid, definition))
            {
                _svidValues[svid] = defaultValue ?? new object(); // 确保不为null
                _logger.LogInformation($"注册SVID: {svid} - {name} (默认值: {defaultValue})");
            }
            else
            {
                _logger.LogWarning($"SVID {svid} 已存在，跳过注册");
            }
        }

        /// <summary>
        /// 注册SVID更新回调
        /// </summary>
        public void RegisterUpdateCallback(uint svid, Func<uint, object, Task> callback)
        {
            _updateCallbacks.AddOrUpdate(svid,
                new List<Func<uint, object, Task>> { callback },
                (key, list) =>
                {
                    list.Add(callback);
                    return list;
                });

            _logger.LogDebug($"为SVID {svid} 注册更新回调");
        }

        /// <summary>
        /// 获取SVID定义信息
        /// </summary>
        public SvidDefinition? GetSvidDefinition(uint svid)
        {
            return _svidDefinitions.GetValueOrDefault(svid);
        }

        /// <summary>
        /// 获取所有SVID定义
        /// </summary>
        public IEnumerable<SvidDefinition> GetAllSvidDefinitions()
        {
            return _svidDefinitions.Values.OrderBy(d => d.Svid);
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化标准SVID
        /// </summary>
        private void InitializeStandardSvids()
        {
            // SEMI E5/E30标准SVID

            // 控制状态相关
            RegisterSvid(720, "ControlMode", (byte)ControlMode.Offline, SvidType.Status);
            RegisterSvid(721, "ControlState", (byte)ControlState.EquipmentOffline, SvidType.Status);
            RegisterSvid(722, "ProcessState", "IDLE", SvidType.Status);
            RegisterSvid(723, "EquipmentState", "STANDBY", SvidType.Status);
            RegisterSvid(724, "DataSyncState", (byte)1, SvidType.Status);


            // 添加主机需要的SVID (3001-3013)
            RegisterSvid(3001, "AlarmEnabled", true, SvidType.Configuration);
            RegisterSvid(3002, "AlarmSetCount", (uint)0, SvidType.Status);
            RegisterSvid(3003, "AlarmClearCount", (uint)0, SvidType.Status);
            RegisterSvid(3004, "LastAlarmID", (uint)0, SvidType.Status);
            RegisterSvid(3010, "EventsEnabled", true, SvidType.Configuration);
            RegisterSvid(3011, "EventsLinked", (uint)0, SvidType.Status);
            RegisterSvid(3012, "EventsSent", (uint)0, SvidType.Status);
            RegisterSvid(3013, "LastEventID", (uint)0, SvidType.Status);

            // 时钟
            RegisterSvid(14, "Clock", DateTime.Now.ToString("yyyyMMddHHmmss"), SvidType.Status);

            // 位置数据（划裂片设备特有）
            RegisterSvid(10010, "XPosition", 0.0f, SvidType.Data);
            RegisterSvid(10011, "YPosition", 0.0f, SvidType.Data);
            RegisterSvid(10012, "ZPosition", 0.0f, SvidType.Data);
            RegisterSvid(10013, "ThetaAngle", 0.0f, SvidType.Data);
            RegisterSvid(10014, "SpindleSpeed", (uint)0, SvidType.Data);
            RegisterSvid(10015, "FeedRate", 0.0f, SvidType.Data);

            // 刀具信息
            RegisterSvid(10007, "BladeModel", "BLADE-001", SvidType.Equipment);
            RegisterSvid(10008, "BladeUsageCount", (uint)0, SvidType.Equipment);
            RegisterSvid(10009, "BladeMaxUsage", (uint)10000, SvidType.Equipment);

            // 材料信息
            RegisterSvid(1101, "LotID", "", SvidType.Data);
            RegisterSvid(1204, "WaferID", "", SvidType.Data);
            RegisterSvid(18, "PPID", "", SvidType.Data);  // Process Program ID

            // 生产统计
            RegisterSvid(10020, "ProcessedWaferCount", (uint)0, SvidType.Status);
            RegisterSvid(10021, "CurrentStepNumber", (uint)0, SvidType.Status);
            RegisterSvid(10022, "RemainingTime", (uint)0, SvidType.Status);
            RegisterSvid(10023, "CompletedDieCount", (uint)0, SvidType.Status);

            // 报警统计
            RegisterSvid(10030, "ActiveAlarmCount", (uint)0, SvidType.Status);
            RegisterSvid(10031, "TotalAlarmCount", (uint)0, SvidType.Status);

            _logger.LogInformation($"初始化了 {_svidDefinitions.Count} 个标准SVID");
        }

        /// <summary>
        /// 判断是否为动态SVID
        /// </summary>
        private bool IsDynamicSvid(uint svid)
        {
            // 这些SVID需要实时从状态服务获取
            return svid switch
            {
                720 or 721 or 722 or 723 => true,  // 状态相关
                14 => true,  // 时钟
                _ => false
            };
        }

        /// <summary>
        /// 获取动态SVID值
        /// </summary>
        private async Task<object> GetDynamicSvidValueAsync(uint svid)
        {
            switch (svid)
            {
                case 720:  // ControlMode
                    return (byte)await _stateService.GetControlModeAsync();

                case 721:  // ControlState
                    return (byte)await _stateService.GetControlStateAsync();

                case 722:  // ProcessState
                    var processState = await _stateService.GetProcessStateAsync();
                    return ProcessStateHelper.ToSecsString(processState);

                case 723:  // EquipmentState
                    var equipmentState = await _stateService.GetEquipmentStateAsync();
                    return equipmentState.ToString().ToUpper();

                case 14:  // Clock
                    return DateTime.Now.ToString("yyyyMMddHHmmss");

                default:
                    return 0;
            }
        }

        #endregion
    }

    #region 辅助类

    /// <summary>
    /// SVID定义
    /// </summary>
    public class SvidDefinition
    {
        /// <summary>
        /// SVID编号
        /// </summary>
        public uint Svid { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; } = 0;

        /// <summary>
        /// SVID类型
        /// </summary>
        public SvidType Type { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType { get; set; } = typeof(object);

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// 最小值（如果适用）
        /// </summary>
        public object? MinValue { get; set; }

        /// <summary>
        /// 最大值（如果适用）
        /// </summary>
        public object? MaxValue { get; set; }
    }

    /// <summary>
    /// SVID类型
    /// </summary>
    public enum SvidType
    {
        /// <summary>
        /// 状态变量
        /// </summary>
        Status,

        /// <summary>
        /// 数据变量
        /// </summary>
        Data,

        /// <summary>
        /// 设备常量
        /// </summary>
        Equipment,

        /// <summary>
        /// 配置变量
        /// </summary>
        Configuration
    }

    #endregion
}
