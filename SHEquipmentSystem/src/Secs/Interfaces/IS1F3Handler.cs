using DiceEquipmentSystem.Secs.Handlers;
using Secs4Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Secs.Interfaces
{
    /// <summary>
    /// S1F3处理器接口
    /// </summary>
    public interface IS1F3Handler
    {
        /// <summary>
        /// 获取单个SVID值
        /// </summary>
        Task<Item> GetSvidValueAsync(uint svid, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量获取SVID值
        /// </summary>
        Task<Dictionary<uint, Item>> GetSvidValuesAsync(IEnumerable<uint> svids, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有SVID值
        /// </summary>
        Task<Dictionary<uint, Item>> GetAllSvidValuesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取SVID定义
        /// </summary>
        SvidDefinitionInfo? GetSvidDefinition(uint svid);
    }
}
