// 文件路径: src/DiceEquipmentSystem/Core/Models/DiceDataModel.cs
// 版本: v2.0.0
// 描述: 划裂片设备完整数据模型 - 基于SEMI E30/E5标准和多维状态模型

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiceEquipmentSystem.Core.Constants;
using Common.SemiStandard;
using DiceEquipmentSystem.Core.Enums;
using Secs4Net;

namespace DiceEquipmentSystem.Core.Models
{
    /// <summary>
    /// 划裂片设备完整数据模型
    /// 实现所有SVID对应的实时数据属性，支持与PLC数据映射和SECS消息转换
    /// </summary>
    public class DiceDataModel : INotifyPropertyChanged
    {
        #region 私有字段

        private readonly object _lockObject = new object();
        private readonly ConcurrentDictionary<uint, object> _svidCache = new();
        private DateTime _lastUpdateTime = DateTime.Now;

        #endregion

        #region 设备标识信息

        /// <summary>
        /// 设备ID
        /// </summary>
        public ushort DeviceId { get; set; } = 1;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string EquipmentName { get; set; } = "Dicer01";

        /// <summary>
        /// 型号名称 (MDLN)
        /// </summary>
        public string ModelName { get; set; } = "AIMFAB";

        /// <summary>
        /// 软件版本 (SOFTREV)
        /// </summary>
        public string SoftwareRevision { get; set; } = "V01R01";

        #endregion

        #region SEMI标准状态变量 (SVID 280-721)

        private List<uint> _eventsEnabled = new();
        /// <summary>
        /// SVID 280 - 启用的事件列表
        /// </summary>
        public List<uint> EventsEnabled
        {
            get => _eventsEnabled;
            set => SetProperty(ref _eventsEnabled, value, SemiIdDefinitions.Svid.EventsEnabled);
        }

        private List<uint> _alarmsEnabled = new();
        /// <summary>
        /// SVID 490 - 启用的报警列表
        /// </summary>
        public List<uint> AlarmsEnabled
        {
            get => _alarmsEnabled;
            set => SetProperty(ref _alarmsEnabled, value, SemiIdDefinitions.Svid.AlarmsEnabled);
        }

        private List<uint> _alarmsSet = new();
        /// <summary>
        /// SVID 491 - 当前激活的报警列表
        /// </summary>
        public List<uint> AlarmsSet
        {
            get => _alarmsSet;
            set => SetProperty(ref _alarmsSet, value, SemiIdDefinitions.Svid.AlarmsSet);
        }

        private string _clock = DateTime.Now.ToString("yyyyMMddHHmmssff");
        /// <summary>
        /// SVID 672 - 当前时钟（16位格式）
        /// </summary>
        public string Clock
        {
            get => _clock;
            set => SetProperty(ref _clock, value, SemiIdDefinitions.Svid.Clock);
        }

        private ControlMode _controlMode = ControlMode.Offline;
        /// <summary>
        /// SVID 720 - 控制模式
        /// 0=离线, 1=本地在线, 2=远程在线
        /// </summary>
        public ControlMode ControlMode
        {
            get => _controlMode;
            set => SetProperty(ref _controlMode, value, SemiIdDefinitions.Svid.ControlMode);
        }

        private ControlState _controlState = ControlState.EquipmentOffline;
        /// <summary>
        /// SVID 721 - 控制状态
        /// 1=设备离线, 2=尝试在线, 3=主机离线, 4=本地在线, 5=远程在线
        /// </summary>
        public ControlState ControlState
        {
            get => _controlState;
            set => SetProperty(ref _controlState, value, SemiIdDefinitions.Svid.ControlState);
        }

        #endregion

        #region 设备特定状态变量 (SVID 10001-10016)

        private string _portId = "LP1";
        /// <summary>
        /// SVID 10001 - 端口ID
        /// </summary>
        public string PortID
        {
            get => _portId;
            set => SetProperty(ref _portId, value, SemiIdDefinitions.Svid.PortID);
        }

        private string _cassetteId = "";
        /// <summary>
        /// SVID 10002 - Cassette ID
        /// </summary>
        public string CassetteID
        {
            get => _cassetteId;
            set => SetProperty(ref _cassetteId, value, SemiIdDefinitions.Svid.CassetteID);
        }

        private string _lotId = "";
        /// <summary>
        /// SVID 10003 - 批次ID
        /// </summary>
        public string LotID
        {
            get => _lotId;
            set => SetProperty(ref _lotId, value, SemiIdDefinitions.Svid.LotID);
        }

        private string _ppid = "";
        /// <summary>
        /// SVID 10004 - 工艺程序ID
        /// </summary>
        public string PPID
        {
            get => _ppid;
            set => SetProperty(ref _ppid, value, SemiIdDefinitions.Svid.PPID);
        }

        private string _cassetteSlotMap = "";
        /// <summary>
        /// SVID 10005 - Cassette槽位映射
        /// 格式: "1111111111111111111111111" (25个槽位，1=有片，0=无片)
        /// </summary>
        public string CassetteSlotMap
        {
            get => _cassetteSlotMap;
            set => SetProperty(ref _cassetteSlotMap, value, SemiIdDefinitions.Svid.CassetteSlotMap);
        }

        private short _processedCount = 0;
        /// <summary>
        /// SVID 10006 - 已处理数量
        /// </summary>
        public short ProcessedCount
        {
            get => _processedCount;
            set => SetProperty(ref _processedCount, value, SemiIdDefinitions.Svid.ProcessedCount);
        }

        private string _knifeModel = "";
        /// <summary>
        /// SVID 10007 - 划刀/裂刀型号
        /// </summary>
        public string KnifeModel
        {
            get => _knifeModel;
            set => SetProperty(ref _knifeModel, value, SemiIdDefinitions.Svid.KnifeModel);
        }

        private int _useNumber = 0;
        /// <summary>
        /// SVID 10008 - 划刀/裂刀使用次数
        /// </summary>
        public int UseNO
        {
            get => _useNumber;
            set => SetProperty(ref _useNumber, value, SemiIdDefinitions.Svid.UseNO);
        }

        private int _useMaxNumber = 10000;
        /// <summary>
        /// SVID 10009 - 划刀/裂刀最大使用次数限制
        /// </summary>
        public int UseMAXNO
        {
            get => _useMaxNumber;
            set => SetProperty(ref _useMaxNumber, value, SemiIdDefinitions.Svid.UseMAXNO);
        }

        private short _progressBar = 0;
        /// <summary>
        /// SVID 10010 - 当前bar进度
        /// </summary>
        public short ProgressBar
        {
            get => _progressBar;
            set => SetProperty(ref _progressBar, value, SemiIdDefinitions.Svid.ProgressBar);
        }

        private short _barNumber = 0;
        /// <summary>
        /// SVID 10011 - 当前Frame下的BAR条总数
        /// </summary>
        public short BARNO
        {
            get => _barNumber;
            set => SetProperty(ref _barNumber, value, SemiIdDefinitions.Svid.BARNO);
        }

        private short _currentBar = 0;
        /// <summary>
        /// SVID 10012 - 当前动作中的BAR数
        /// </summary>
        public short CurrentBar
        {
            get => _currentBar;
            set => SetProperty(ref _currentBar, value, SemiIdDefinitions.Svid.CurrentBAR);
        }

        private string _rfid = "";
        /// <summary>
        /// SVID 10013 - RFID内容
        /// </summary>
        public string RFID
        {
            get => _rfid;
            set => SetProperty(ref _rfid, value, SemiIdDefinitions.Svid.RFID);
        }

        private string _qrContent = "";
        /// <summary>
        /// SVID 10014 - 扫码内容
        /// </summary>
        public string QRContent
        {
            get => _qrContent;
            set => SetProperty(ref _qrContent, value, SemiIdDefinitions.Svid.QRContent);
        }

        private short _getFrameLY = 0;
        /// <summary>
        /// SVID 10015 - 取环所在层
        /// </summary>
        public short GetFrameLY
        {
            get => _getFrameLY;
            set => SetProperty(ref _getFrameLY, value, SemiIdDefinitions.Svid.GetFrameLY);
        }

        private short _putFrameLY = 0;
        /// <summary>
        /// SVID 10016 - 放环所在层
        /// </summary>
        public short PutFrameLY
        {
            get => _putFrameLY;
            set => SetProperty(ref _putFrameLY, value, SemiIdDefinitions.Svid.PutFrameLY);
        }

        #endregion

        #region 工艺坐标数据

        /// <summary>
        /// 当前X坐标（mm）
        /// </summary>
        public float CurrentX { get; set; }

        /// <summary>
        /// 当前Y坐标（mm）
        /// </summary>
        public float CurrentY { get; set; }

        /// <summary>
        /// 当前Z坐标（mm）
        /// </summary>
        public float CurrentZ { get; set; }

        /// <summary>
        /// 当前θ角度（deg）
        /// </summary>
        public float CurrentTheta { get; set; }

        /// <summary>
        /// 目标X坐标（mm）
        /// </summary>
        public float TargetX { get; set; }

        /// <summary>
        /// 目标Y坐标（mm）
        /// </summary>
        public float TargetY { get; set; }

        /// <summary>
        /// 目标Z坐标（mm）
        /// </summary>
        public float TargetZ { get; set; }

        /// <summary>
        /// 目标θ角度（deg）
        /// </summary>
        public float TargetTheta { get; set; }

        #endregion

        #region 工艺参数数据

        /// <summary>
        /// 处理速度（mm/s）
        /// </summary>
        public float ProcessSpeed { get; set; } = 100.0f;

        /// <summary>
        /// 处理压力（kPa）
        /// </summary>
        public float ProcessPressure { get; set; } = 50.0f;

        /// <summary>
        /// 处理温度（℃）
        /// </summary>
        public float ProcessTemperature { get; set; } = 25.0f;

        /// <summary>
        /// 主轴转速（rpm）
        /// </summary>
        public float SpindleSpeed { get; set; } = 30000.0f;

        /// <summary>
        /// 切割深度（mm）
        /// </summary>
        public float CutDepth { get; set; } = 0.1f;

        /// <summary>
        /// 进给速度（mm/min）
        /// </summary>
        public float FeedRate { get; set; } = 1000.0f;

        #endregion

        #region 多维状态模型

        /// <summary>
        /// HSMS连接状态
        /// </summary>
        public HsmsConnectionState ConnectionState { get; set; } = HsmsConnectionState.NotConnected;

        /// <summary>
        /// 处理状态
        /// </summary>
        public ProcessState ProcessState { get; set; } = ProcessState.Init;

        /// <summary>
        /// 设备状态（基于SEMI E10）
        /// </summary>
        public EquipmentState EquipmentState { get; set; } = EquipmentState.Unknown;

        /// <summary>
        /// 材料状态
        /// </summary>
        public MaterialState MaterialState { get; set; } = MaterialState.NoMaterial;

        /// <summary>
        /// 报警状态
        /// </summary>
        public AlarmState AlarmState { get; set; } = AlarmState.NoAlarm;

        #endregion

        #region SVID数据转换方法

        /// <summary>
        /// 根据SVID获取对应的数据值
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <returns>SECS格式的数据项</returns>
        public Item GetSvidValue(uint svid)
        {
            lock (_lockObject)
            {
                switch (svid)
                {
                    // SEMI标准SVID
                    case SemiIdDefinitions.Svid.EventsEnabled:
                        return Item.L(EventsEnabled.Select(id => Item.U4(id)).ToArray());

                    case SemiIdDefinitions.Svid.AlarmsEnabled:
                        return Item.L(AlarmsEnabled.Select(id => Item.U4(id)).ToArray());

                    case SemiIdDefinitions.Svid.AlarmsSet:
                        return Item.L(AlarmsSet.Select(id => Item.U4(id)).ToArray());

                    case SemiIdDefinitions.Svid.Clock:
                        UpdateClock();
                        return Item.A(Clock);

                    case SemiIdDefinitions.Svid.ControlMode:
                        return Item.U1((byte)ControlMode);

                    case SemiIdDefinitions.Svid.ControlState:
                        return Item.U1((byte)ControlState);

                    // 设备特定SVID
                    case SemiIdDefinitions.Svid.PortID:
                        return Item.A(PortID);

                    case SemiIdDefinitions.Svid.CassetteID:
                        return Item.A(CassetteID);

                    case SemiIdDefinitions.Svid.LotID:
                        return Item.A(LotID);

                    case SemiIdDefinitions.Svid.PPID:
                        return Item.A(PPID);

                    case SemiIdDefinitions.Svid.CassetteSlotMap:
                        return Item.A(CassetteSlotMap);

                    case SemiIdDefinitions.Svid.ProcessedCount:
                        return Item.I2(ProcessedCount);

                    case SemiIdDefinitions.Svid.KnifeModel:
                        return Item.A(KnifeModel);

                    case SemiIdDefinitions.Svid.UseNO:
                        return Item.I4(UseNO);

                    case SemiIdDefinitions.Svid.UseMAXNO:
                        return Item.I4(UseMAXNO);

                    case SemiIdDefinitions.Svid.ProgressBar:
                        return Item.I2(ProgressBar);

                    case SemiIdDefinitions.Svid.BARNO:
                        return Item.I2(BARNO);

                    case SemiIdDefinitions.Svid.CurrentBAR:
                        return Item.I2(CurrentBar);

                    case SemiIdDefinitions.Svid.RFID:
                        return Item.A(RFID);

                    case SemiIdDefinitions.Svid.QRContent:
                        return Item.A(QRContent);

                    case SemiIdDefinitions.Svid.GetFrameLY:
                        return Item.I2(GetFrameLY);

                    case SemiIdDefinitions.Svid.PutFrameLY:
                        return Item.I2(PutFrameLY);

                    default:
                        // 返回空列表表示未定义的SVID
                        return Item.L();
                }
            }
        }

        /// <summary>
        /// 批量获取SVID值
        /// </summary>
        /// <param name="svidList">SVID列表</param>
        /// <returns>SVID值列表</returns>
        public List<Item> GetSvidValues(IEnumerable<uint> svidList)
        {
            var result = new List<Item>();
            foreach (var svid in svidList)
            {
                result.Add(GetSvidValue(svid));
            }
            return result;
        }

        /// <summary>
        /// 设置SVID值（用于从PLC或其他源更新）
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="value">新值</param>
        public void SetSvidValue(uint svid, object value)
        {
            lock (_lockObject)
            {
                switch (svid)
                {
                    case SemiIdDefinitions.Svid.ControlMode:
                        if (value is byte b1)
                            ControlMode = (ControlMode)b1;
                        break;

                    case SemiIdDefinitions.Svid.ControlState:
                        if (value is byte b2)
                            ControlState = (ControlState)b2;
                        break;

                    case SemiIdDefinitions.Svid.PortID:
                        PortID = value?.ToString() ?? "";
                        break;

                    case SemiIdDefinitions.Svid.CassetteID:
                        CassetteID = value?.ToString() ?? "";
                        break;

                    case SemiIdDefinitions.Svid.LotID:
                        LotID = value?.ToString() ?? "";
                        break;

                    case SemiIdDefinitions.Svid.PPID:
                        PPID = value?.ToString() ?? "";
                        break;

                    case SemiIdDefinitions.Svid.ProcessedCount:
                        if (value is short s1)
                            ProcessedCount = s1;
                        break;

                    case SemiIdDefinitions.Svid.UseNO:
                        if (value is int i1)
                            UseNO = i1;
                        break;

                        // ... 其他SVID设置逻辑
                }

                _svidCache[svid] = value;
                _lastUpdateTime = DateTime.Now;
            }
        }

        #endregion

        #region 材料处理方法

        /// <summary>
        /// 更新槽位映射
        /// </summary>
        /// <param name="slotStates">槽位状态数组（true=有片）</param>
        public void UpdateSlotMap(bool[] slotStates)
        {
            var map = string.Join("", slotStates.Select(s => s ? "1" : "0"));
            CassetteSlotMap = map;
        }

        /// <summary>
        /// 获取槽位状态
        /// </summary>
        /// <returns>槽位状态数组</returns>
        public bool[] GetSlotStates()
        {
            if (string.IsNullOrEmpty(CassetteSlotMap))
                return new bool[25];

            return CassetteSlotMap.Select(c => c == '1').ToArray();
        }

        /// <summary>
        /// 获取有效槽位数量
        /// </summary>
        public int GetValidSlotCount()
        {
            return GetSlotStates().Count(s => s);
        }

        #endregion

        #region 刀具管理方法

        /// <summary>
        /// 检查刀具寿命
        /// </summary>
        /// <returns>剩余使用次数百分比</returns>
        public float GetKnifeLifePercentage()
        {
            if (UseMAXNO <= 0) return 0;
            var remaining = UseMAXNO - UseNO;
            return (float)remaining / UseMAXNO * 100;
        }

        /// <summary>
        /// 刀具是否需要更换
        /// </summary>
        public bool IsKnifeChangeRequired()
        {
            return UseNO >= UseMAXNO;
        }

        /// <summary>
        /// 重置刀具计数
        /// </summary>
        public void ResetKnifeCount()
        {
            UseNO = 0;
        }

        #endregion

        #region 工艺进度方法

        /// <summary>
        /// 获取工艺进度百分比
        /// </summary>
        public float GetProcessProgress()
        {
            if (BARNO <= 0) return 0;
            return (float)CurrentBar / BARNO * 100;
        }

        /// <summary>
        /// 更新工艺进度
        /// </summary>
        public void UpdateProgress(short current, short total)
        {
            CurrentBar = current;
            BARNO = total;
            ProgressBar = (short)(GetProcessProgress());
        }

        #endregion

        #region 事件报告数据收集

        /// <summary>
        /// 收集事件报告数据
        /// </summary>
        /// <param name="ceid">事件ID</param>
        /// <returns>事件相关的变量数据</returns>
        public Dictionary<uint, Item> CollectEventData(uint ceid)
        {
            var data = new Dictionary<uint, Item>();

            // 根据不同事件收集相关数据
            switch (ceid)
            {
                case SemiIdDefinitions.Ceid.ProcessStart:
                    data[SemiIdDefinitions.Svid.PPID] = Item.A(PPID);
                    data[SemiIdDefinitions.Svid.LotID] = Item.A(LotID);
                    data[SemiIdDefinitions.Svid.CassetteID] = Item.A(CassetteID);
                    data[SemiIdDefinitions.Svid.Clock] = Item.A(Clock);
                    break;

                case SemiIdDefinitions.Ceid.ProcessEnd:
                    data[SemiIdDefinitions.Svid.ProcessedCount] = Item.I2(ProcessedCount);
                    data[SemiIdDefinitions.Svid.Clock] = Item.A(Clock);
                    break;

                case SemiIdDefinitions.Ceid.KnifeInstall:
                    data[SemiIdDefinitions.Svid.KnifeModel] = Item.A(KnifeModel);
                    data[SemiIdDefinitions.Svid.UseNO] = Item.I4(UseNO);
                    data[SemiIdDefinitions.Svid.UseMAXNO] = Item.I4(UseMAXNO);
                    break;

                case SemiIdDefinitions.Ceid.ControlStateOFFLINE:
                case SemiIdDefinitions.Ceid.ControlStateLOCAL:
                case SemiIdDefinitions.Ceid.ControlStateREMOTE:
                    data[SemiIdDefinitions.Svid.ControlState] = Item.U1((byte)ControlState);
                    data[SemiIdDefinitions.Svid.ControlMode] = Item.U1((byte)ControlMode);
                    data[SemiIdDefinitions.Svid.Clock] = Item.A(Clock);
                    break;

                default:
                    // 默认包含时钟
                    data[SemiIdDefinitions.Svid.Clock] = Item.A(Clock);
                    break;
            }

            return data;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 更新时钟
        /// </summary>
        private void UpdateClock()
        {
            Clock = DateTime.Now.ToString("yyyyMMddHHmmssff");
        }

        /// <summary>
        /// 获取最后更新时间
        /// </summary>
        public DateTime GetLastUpdateTime()
        {
            return _lastUpdateTime;
        }

        /// <summary>
        /// 克隆当前数据模型
        /// </summary>
        public DiceDataModel Clone()
        {
            lock (_lockObject)
            {
                var clone = new DiceDataModel
                {
                    // 设备标识
                    DeviceId = this.DeviceId,
                    EquipmentName = this.EquipmentName,
                    ModelName = this.ModelName,
                    SoftwareRevision = this.SoftwareRevision,

                    // SEMI标准状态
                    EventsEnabled = new List<uint>(this.EventsEnabled),
                    AlarmsEnabled = new List<uint>(this.AlarmsEnabled),
                    AlarmsSet = new List<uint>(this.AlarmsSet),
                    Clock = this.Clock,
                    ControlMode = this.ControlMode,
                    ControlState = this.ControlState,

                    // 设备特定状态
                    PortID = this.PortID,
                    CassetteID = this.CassetteID,
                    LotID = this.LotID,
                    PPID = this.PPID,
                    CassetteSlotMap = this.CassetteSlotMap,
                    ProcessedCount = this.ProcessedCount,
                    KnifeModel = this.KnifeModel,
                    UseNO = this.UseNO,
                    UseMAXNO = this.UseMAXNO,
                    ProgressBar = this.ProgressBar,
                    BARNO = this.BARNO,
                    CurrentBar = this.CurrentBar,
                    RFID = this.RFID,
                    QRContent = this.QRContent,
                    GetFrameLY = this.GetFrameLY,
                    PutFrameLY = this.PutFrameLY,

                    // 工艺坐标
                    CurrentX = this.CurrentX,
                    CurrentY = this.CurrentY,
                    CurrentZ = this.CurrentZ,
                    CurrentTheta = this.CurrentTheta,
                    TargetX = this.TargetX,
                    TargetY = this.TargetY,
                    TargetZ = this.TargetZ,
                    TargetTheta = this.TargetTheta,

                    // 工艺参数
                    ProcessSpeed = this.ProcessSpeed,
                    ProcessPressure = this.ProcessPressure,
                    ProcessTemperature = this.ProcessTemperature,
                    SpindleSpeed = this.SpindleSpeed,
                    CutDepth = this.CutDepth,
                    FeedRate = this.FeedRate,

                    // 多维状态
                    ConnectionState = this.ConnectionState,
                    ProcessState = this.ProcessState,
                    EquipmentState = this.EquipmentState,
                    MaterialState = this.MaterialState,
                    AlarmState = this.AlarmState
                };

                return clone;
            }
        }

        /// <summary>
        /// 验证数据完整性
        /// </summary>
        public bool ValidateData()
        {
            // 验证必要字段
            if (string.IsNullOrEmpty(EquipmentName)) return false;
            if (string.IsNullOrEmpty(ModelName)) return false;
            if (string.IsNullOrEmpty(SoftwareRevision)) return false;

            // 验证数值范围
            if (UseNO < 0 || UseNO > UseMAXNO) return false;
            if (ProcessedCount < 0) return false;
            if (CurrentBar < 0 || CurrentBar > BARNO) return false;

            return true;
        }

        #endregion

        #region INotifyPropertyChanged实现

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 设置属性值并触发变更通知
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, uint? svid = null, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);

            // 更新SVID缓存
            if (svid.HasValue)
            {
                _svidCache[svid.Value] = value;
            }

            _lastUpdateTime = DateTime.Now;
            return true;
        }

        /// <summary>
        /// 触发属性变更事件
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region 枚举定义

    /// <summary>
    /// 材料状态
    /// </summary>
    public enum MaterialState
    {
        /// <summary>无材料</summary>
        NoMaterial = 0,
        /// <summary>等待处理</summary>
        WaitingForProcess = 1,
        /// <summary>处理中</summary>
        InProcess = 2,
        /// <summary>处理完成</summary>
        ProcessComplete = 3,
        /// <summary>需要移除</summary>
        NeedRemoval = 4
    }

    /// <summary>
    /// 报警状态
    /// </summary>
    public enum AlarmState
    {
        /// <summary>无报警</summary>
        NoAlarm = 0,
        /// <summary>警告</summary>
        Warning = 1,
        /// <summary>轻微报警</summary>
        Minor = 2,
        /// <summary>严重报警</summary>
        Major = 3,
        /// <summary>紧急报警</summary>
        Critical = 4
    }

    #endregion
}
