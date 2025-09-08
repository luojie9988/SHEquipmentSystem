// 文件路径: src/DiceEquipmentSystem/Secs/Helpers/SecsItemHelper.cs
// 版本: v1.0.0
// 描述: SECS消息Item处理帮助类

using System;
using Secs4Net;

namespace DiceEquipmentSystem.Secs.Helpers
{
    /// <summary>
    /// SECS消息Item处理帮助类
    /// 提供通用的Item解析和转换方法
    /// </summary>
    public static class SecsItemHelper
    {
        /// <summary>
        /// 解析CEID值
        /// </summary>
        /// <param name="item">消息项</param>
        /// <returns>CEID值，无效时返回0</returns>
        public static uint ParseCeid(Item? item)
        {
            if (item == null) return 0;

            return item.Format switch
            {
                SecsFormat.U1 => item.FirstValue<byte>(),
                SecsFormat.U2 => item.FirstValue<ushort>(),
                SecsFormat.U4 => item.FirstValue<uint>(),
                SecsFormat.I1 => (uint)Math.Max((sbyte)0, item.FirstValue<sbyte>()),
                SecsFormat.I2 => (uint)Math.Max((short)0, item.FirstValue<short>()),
                SecsFormat.I4 => (uint)Math.Max(0, item.FirstValue<int>()),
                _ => 0
            };
        }

        /// <summary>
        /// 解析无符号整数值（SVID、ECID、RPTID、VID等）
        /// </summary>
        public static uint ParseUInt(Item? item) => ParseCeid(item);

        /// <summary>
        /// 解析布尔值
        /// </summary>
        public static bool ParseBoolean(Item? item)
        {
            if (item == null) return false;

            return item.Format switch
            {
                SecsFormat.Boolean => item.FirstValue<bool>(),
                SecsFormat.U1 => item.FirstValue<byte>() != 0,
                SecsFormat.U2 => item.FirstValue<ushort>() != 0,
                SecsFormat.U4 => item.FirstValue<uint>() != 0,
                SecsFormat.I1 => item.FirstValue<sbyte>() != 0,
                SecsFormat.I2 => item.FirstValue<short>() != 0,
                SecsFormat.I4 => item.FirstValue<int>() != 0,
                _ => false
            };
        }

        /// <summary>
        /// 将对象值转换为Item
        /// </summary>
        /// <param name="value">要转换的值</param>
        /// <returns>SECS Item</returns>
        public static Item ConvertToItem(object? value)
        {
            if (value == null)
                return Item.A("");

            return value switch
            {
                string s => Item.A(s),
                bool b => Item.Boolean(b),
                byte b => Item.U1(b),
                ushort us => Item.U2(us),
                uint ui => Item.U4(ui),
                ulong ul => Item.U8(ul),
                sbyte sb => Item.I1(sb),
                short s => Item.I2(s),
                int i => Item.I4(i),
                long l => Item.I8(l),
                float f => Item.F4(f),
                double d => Item.F8(d),
                byte[] ba => Item.B(ba),
                DateTime dt => Item.A(dt.ToString("yyyyMMddHHmmss")),
                Item item => item,
                _ => Item.A(value.ToString() ?? "")
            };
        }

        /// <summary>
        /// 解析通用值
        /// </summary>
        public static object ParseValue(Item? item)
        {
            if (item == null)
                return "";

            return item.Format switch
            {
                SecsFormat.ASCII => item.GetString(),
                SecsFormat.Binary => item.GetMemory<byte>().ToArray(),
                SecsFormat.Boolean => item.FirstValue<bool>(),
                SecsFormat.U1 => item.FirstValue<byte>(),
                SecsFormat.U2 => item.FirstValue<ushort>(),
                SecsFormat.U4 => item.FirstValue<uint>(),
                SecsFormat.U8 => item.FirstValue<ulong>(),
                SecsFormat.I1 => item.FirstValue<sbyte>(),
                SecsFormat.I2 => item.FirstValue<short>(),
                SecsFormat.I4 => item.FirstValue<int>(),
                SecsFormat.I8 => item.FirstValue<long>(),
                SecsFormat.F4 => item.FirstValue<float>(),
                SecsFormat.F8 => item.FirstValue<double>(),
                _ => item.ToString()
            };
        }
    }
}
