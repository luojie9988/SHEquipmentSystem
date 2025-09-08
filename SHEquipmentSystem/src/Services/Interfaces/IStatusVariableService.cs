// 文件路径: src/DiceEquipmentSystem/Services/Interfaces/IStatusVariableService.cs
// 版本: v1.1.0
// 描述: 状态变量服务接口 - 添加SetSvidValueAsync方法

using System.Collections.Generic;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Services.Interfaces
{
    /// <summary>
    /// 状态变量(SVID)服务接口
    /// </summary>
    public interface IStatusVariableService
    {
        /// <summary>
        /// 获取所有可用的SVID列表
        /// </summary>
        Task<List<uint>> GetAllSvidListAsync();

        /// <summary>
        /// 获取指定SVID的值
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <returns>状态变量值</returns>
        Task<object> GetSvidValueAsync(uint svid);

        /// <summary>
        /// 批量获取SVID值
        /// </summary>
        /// <param name="svidList">SVID列表</param>
        /// <returns>SVID值字典</returns>
        Task<Dictionary<uint, object>> GetSvidValuesAsync(List<uint> svidList);

        /// <summary>
        /// 设置SVID值
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="value">值</param>
        /// <returns>是否成功</returns>
        Task<bool> SetSvidValueAsync(uint svid, object value);

        /// <summary>
        /// 注册SVID
        /// </summary>
        /// <param name="svid">状态变量ID</param>
        /// <param name="name">名称</param>
        /// <param name="defaultValue">默认值</param>
        void RegisterSvid(uint svid, string name, object defaultValue);

        /// <summary>
        /// 获取状态变量值（通用方法）
        /// </summary>
        /// <param name="vid">变量ID</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>变量值</returns>
        Task<object> GetStatusVariableAsync(uint vid, object? defaultValue = null);
    }
}
