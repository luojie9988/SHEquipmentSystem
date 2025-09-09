// 文件路径: src/Common/SvidMappingFix.cs
// 版本: v1.0.0
// 描述: SVID映射修复实现

using System;
using System.Collections.Generic;
using System.Linq;
using Secs4Net;

namespace DiceSystem.Common.Svid
{
    /// <summary>
    /// SVID映射修复类
    /// 解决Host和Equipment之间的SVID映射不一致问题
    /// </summary>
    public static class SvidMappingFix
    {
        #region 标准SVID定义（确保两端一致）
        
        /// <summary>
        /// 标准SVID定义表
        /// 所有SVID必须在Host和Equipment端保持一致
        /// </summary>
        public static readonly Dictionary<uint, SvidInfo> StandardSvidDefinitions = new Dictionary<uint, SvidInfo>
        {
            // SEMI标准SVID (280-721)
            [280] = new SvidInfo { Id = 280, Name = "EventsEnabled", Format = SecsFormat.List, DataType = "L", Description = "启用的事件列表" },
            [490] = new SvidInfo { Id = 490, Name = "AlarmsEnabled", Format = SecsFormat.List, DataType = "L", Description = "启用的报警列表" },
            [491] = new SvidInfo { Id = 491, Name = "AlarmsSet", Format = SecsFormat.List, DataType = "L", Description = "当前激活的报警" },
            [672] = new SvidInfo { Id = 672, Name = "Clock", Format = SecsFormat.ASCII, DataType = "A16", Description = "当前时钟" },
            [720] = new SvidInfo { Id = 720, Name = "ControlMode", Format = SecsFormat.U1, DataType = "U1", Description = "控制模式" },
            [721] = new SvidInfo { Id = 721, Name = "ControlState", Format = SecsFormat.U1, DataType = "U1", Description = "控制状态" },
            
            // 设备特定SVID (10001-10016)
            [10001] = new SvidInfo { Id = 10001, Name = "PortID", Format = SecsFormat.ASCII, DataType = "A", Description = "端口ID" },
            [10002] = new SvidInfo { Id = 10002, Name = "CassetteID", Format = SecsFormat.ASCII, DataType = "A", Description = "Cassette ID" },
            [10003] = new SvidInfo { Id = 10003, Name = "LotID", Format = SecsFormat.ASCII, DataType = "A", Description = "批次ID" },
            [10004] = new SvidInfo { Id = 10004, Name = "PPID", Format = SecsFormat.ASCII, DataType = "A", Description = "工艺程序ID" },
            [10005] = new SvidInfo { Id = 10005, Name = "CassetteSlotMap", Format = SecsFormat.ASCII, DataType = "A", Description = "槽位映射" },
            [10006] = new SvidInfo { Id = 10006, Name = "ProcessedCount", Format = SecsFormat.I2, DataType = "I2", Description = "已处理数量" },
            [10007] = new SvidInfo { Id = 10007, Name = "KnifeModel", Format = SecsFormat.ASCII, DataType = "A", Description = "刀具型号" },
            [10008] = new SvidInfo { Id = 10008, Name = "UseNumber", Format = SecsFormat.I4, DataType = "I4", Description = "使用次数" },
            [10009] = new SvidInfo { Id = 10009, Name = "UseMaxNumber", Format = SecsFormat.I4, DataType = "I4", Description = "最大使用次数" },
            [10010] = new SvidInfo { Id = 10010, Name = "ProgressBar", Format = SecsFormat.I2, DataType = "I2", Description = "进度条" },
            [10011] = new SvidInfo { Id = 10011, Name = "BarNumber", Format = SecsFormat.I2, DataType = "I2", Description = "BAR总数" },
            [10012] = new SvidInfo { Id = 10012, Name = "CurrentBar", Format = SecsFormat.I2, DataType = "I2", Description = "当前BAR" },
            [10013] = new SvidInfo { Id = 10013, Name = "RFID", Format = SecsFormat.ASCII, DataType = "A", Description = "RFID内容" },
            [10014] = new SvidInfo { Id = 10014, Name = "QRContent", Format = SecsFormat.ASCII, DataType = "A", Description = "扫码内容" },
            [10015] = new SvidInfo { Id = 10015, Name = "GetFrameLY", Format = SecsFormat.I2, DataType = "I2", Description = "取环层" },
            [10016] = new SvidInfo { Id = 10016, Name = "PutFrameLY", Format = SecsFormat.I2, DataType = "I2", Description = "放环层" }
        };
        
        #endregion
        
        #region SVID值转换方法
        
        /// <summary>
        /// 将值转换为正确的SECS Item格式
        /// </summary>
        public static Item ConvertToSecsItem(uint svid, object value)
        {
            if (!StandardSvidDefinitions.TryGetValue(svid, out var svidInfo))
            {
                // 未定义的SVID返回空列表
                return Item.L();
            }
            
            // 处理null值
            if (value == null)
            {
                return CreateDefaultItem(svidInfo.Format);
            }
            
            // 根据格式转换
            try
            {
                switch (svidInfo.Format)
                {
                    case SecsFormat.List:
                        if (value is IEnumerable<object> list)
                        {
                            return Item.L(list.Select(v => ConvertToSecsItem(svid, v)).ToArray());
                        }
                        return Item.L();
                        
                    case SecsFormat.ASCII:
                        return Item.A(value.ToString() ?? "");
                        
                    case SecsFormat.U1:
                        return Item.U1(Convert.ToByte(value));
                        
                    case SecsFormat.U2:
                        return Item.U2(Convert.ToUInt16(value));
                        
                    case SecsFormat.U4:
                        return Item.U4(Convert.ToUInt32(value));
                        
                    case SecsFormat.I1:
                        return Item.I1(Convert.ToSByte(value));
                        
                    case SecsFormat.I2:
                        return Item.I2(Convert.ToInt16(value));
                        
                    case SecsFormat.I4:
                        return Item.I4(Convert.ToInt32(value));
                        
                    case SecsFormat.F4:
                        return Item.F4(Convert.ToSingle(value));
                        
                    case SecsFormat.F8:
                        return Item.F8(Convert.ToDouble(value));
                        
                    case SecsFormat.Boolean:
                        return Item.Boolean(Convert.ToBoolean(value));
                        
                    default:
                        return Item.A(value.ToString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SVID {svid} 值转换失败: {ex.Message}");
                return CreateDefaultItem(svidInfo.Format);
            }
        }
        
        /// <summary>
        /// 从SECS Item解析值
        /// </summary>
        public static object ParseFromSecsItem(uint svid, Item item)
        {
            if (item == null || !StandardSvidDefinitions.TryGetValue(svid, out var svidInfo))
            {
                return null;
            }
            
            try
            {
                switch (item.Format)
                {
                    case SecsFormat.List:
                        if (item.Count == 0) return null;
                        if (item.Count == 1) return ParseFromSecsItem(svid, item.Items[0]);
                        return item.Items.Select(i => ParseFromSecsItem(svid, i)).ToList();
                        
                    case SecsFormat.ASCII:
                        return item.GetString();
                        
                    case SecsFormat.Boolean:
                        return item.FirstValue<bool>();
                        
                    case SecsFormat.Binary:
                        return item.GetMemory<byte>().ToArray();
                        
                    case SecsFormat.U1:
                        return item.FirstValue<byte>();
                        
                    case SecsFormat.U2:
                        return item.FirstValue<ushort>();
                        
                    case SecsFormat.U4:
                        return item.FirstValue<uint>();
                        
                    case SecsFormat.U8:
                        return item.FirstValue<ulong>();
                        
                    case SecsFormat.I1:
                        return item.FirstValue<sbyte>();
                        
                    case SecsFormat.I2:
                        return item.FirstValue<short>();
                        
                    case SecsFormat.I4:
                        return item.FirstValue<int>();
                        
                    case SecsFormat.I8:
                        return item.FirstValue<long>();
                        
                    case SecsFormat.F4:
                        return item.FirstValue<float>();
                        
                    case SecsFormat.F8:
                        return item.FirstValue<double>();
                        
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SVID {svid} 值解析失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 创建默认Item
        /// </summary>
        private static Item CreateDefaultItem(SecsFormat format)
        {
            switch (format)
            {
                case SecsFormat.List:
                    return Item.L();
                case SecsFormat.ASCII:
                    return Item.A("");
                case SecsFormat.U1:
                    return Item.U1(0);
                case SecsFormat.U2:
                    return Item.U2(0);
                case SecsFormat.U4:
                    return Item.U4(0);
                case SecsFormat.I1:
                    return Item.I1(0);
                case SecsFormat.I2:
                    return Item.I2(0);
                case SecsFormat.I4:
                    return Item.I4(0);
                case SecsFormat.F4:
                    return Item.F4(0);
                case SecsFormat.F8:
                    return Item.F8(0);
                case SecsFormat.Boolean:
                    return Item.Boolean(false);
                default:
                    return Item.L();
            }
        }
        
        #endregion
        
        #region S1F3/S1F4消息处理修复
        
        /// <summary>
        /// 构建S1F3请求消息
        /// </summary>
        public static SecsMessage BuildS1F3Request(IEnumerable<uint> svidList)
        {
            var svids = svidList?.ToList() ?? new List<uint>();
            
            if (svids.Count == 0)
            {
                // 空列表表示请求所有SVID
                return new SecsMessage(1, 3, true)
            {
                Name = "S1F3",
                SecsItem = Item.L()
            };
            }
            
            // 构建SVID列表
            var items = svids.Select(svid => Item.U4(svid)).ToArray();
            return new SecsMessage(1, 3, true)
            {
                Name = "S1F3",
                SecsItem = Item.L(items)
            };
        }
        
        /// <summary>
        /// 构建S1F4响应消息
        /// </summary>
        public static SecsMessage BuildS1F4Response(IEnumerable<uint> requestedSvids, Dictionary<uint, object> svidValues)
        {
            var svids = requestedSvids?.ToList() ?? StandardSvidDefinitions.Keys.ToList();
            var items = new List<Item>();
            
            foreach (var svid in svids)
            {
                if (svidValues.TryGetValue(svid, out var value))
                {
                    items.Add(ConvertToSecsItem(svid, value));
                }
                else
                {
                    // SVID未定义或无值，返回空列表
                    items.Add(Item.L());
                }
            }
            
            return new SecsMessage(1, 4, false)
            {
                Name = "S1F4",
                SecsItem = Item.L(items.ToArray())
            };
        }
        
        /// <summary>
        /// 解析S1F4响应
        /// </summary>
        public static Dictionary<uint, object> ParseS1F4Response(SecsMessage s1f4, IList<uint> requestedSvids)
        {
            var result = new Dictionary<uint, object>();
            
            if (s1f4?.SecsItem == null || requestedSvids == null)
            {
                return result;
            }
            
            var items = s1f4.SecsItem.Items;
            if (items == null) return result;
            
            // 解析每个值（顺序对应）
            for (int i = 0; i < Math.Min(items.Length, requestedSvids.Count); i++)
            {
                var svid = requestedSvids[i];
                var value = ParseFromSecsItem(svid, items[i]);
                if (value != null)
                {
                    result[svid] = value;
                }
            }
            
            return result;
        }
        
        #endregion
        
        #region 验证方法
        
        /// <summary>
        /// 验证SVID是否有效
        /// </summary>
        public static bool IsValidSvid(uint svid)
        {
            return StandardSvidDefinitions.ContainsKey(svid);
        }
        
        /// <summary>
        /// 验证SVID值格式
        /// </summary>
        public static bool ValidateFormat(uint svid, object value)
        {
            if (!StandardSvidDefinitions.TryGetValue(svid, out var svidInfo))
            {
                return false;
            }
            
            if (value == null)
            {
                return true; // null值是允许的
            }
            
            try
            {
                // 尝试转换，如果成功则格式正确
                var item = ConvertToSecsItem(svid, value);
                return item != null;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 获取所有定义的SVID
        /// </summary>
        public static IEnumerable<uint> GetAllSvids()
        {
            return StandardSvidDefinitions.Keys;
        }
        
        /// <summary>
        /// 获取SVID信息
        /// </summary>
        public static SvidInfo GetSvidInfo(uint svid)
        {
            return StandardSvidDefinitions.TryGetValue(svid, out var info) ? info : null;
        }
        
        #endregion
    }
    
    /// <summary>
    /// SVID信息
    /// </summary>
    public class SvidInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public SecsFormat Format { get; set; }
        public string DataType { get; set; }
        public string Description { get; set; }
        public object DefaultValue { get; set; }
    }
}
