using DiceEquipmentSystem.Data.Entities;

namespace DiceEquipmentSystem.Data.Repositories
{
    /// <summary>
    /// ALID映射仓储接口
    /// </summary>
    public interface IAlidMappingRepository
    {
        /// <summary>
        /// 获取所有ALID映射
        /// </summary>
        Task<IEnumerable<AlidMapping>> GetAllAsync();

        /// <summary>
        /// 根据ID获取ALID映射
        /// </summary>
        Task<AlidMapping?> GetByIdAsync(uint alidId);

        /// <summary>
        /// 创建ALID映射
        /// </summary>
        Task<AlidMapping> CreateAsync(AlidMapping entity);

        /// <summary>
        /// 更新ALID映射
        /// </summary>
        Task<AlidMapping> UpdateAsync(AlidMapping entity);

        /// <summary>
        /// 删除ALID映射
        /// </summary>
        Task<bool> DeleteAsync(uint alidId);

        /// <summary>
        /// 检查ALID是否存在
        /// </summary>
        Task<bool> ExistsAsync(uint alidId);

        /// <summary>
        /// 根据分类获取ALID映射
        /// </summary>
        Task<IEnumerable<AlidMapping>> GetByCategoryAsync(int category);

        /// <summary>
        /// 根据优先级获取ALID映射
        /// </summary>
        Task<IEnumerable<AlidMapping>> GetByPriorityAsync(int priority);

        /// <summary>
        /// 获取正在监控的ALID映射
        /// </summary>
        Task<IEnumerable<AlidMapping>> GetMonitoredAsync();

        /// <summary>
        /// 根据PLC地址获取ALID映射
        /// </summary>
        Task<AlidMapping?> GetByTriggerAddressAsync(string triggerAddress);

        /// <summary>
        /// 分页获取ALID映射
        /// </summary>
        Task<IEnumerable<AlidMapping>> GetPagedAsync(int pageNumber, int pageSize, string? searchTerm = null);

        /// <summary>
        /// 获取总数
        /// </summary>
        Task<int> GetCountAsync(string? searchTerm = null);

        /// <summary>
        /// 批量更新监控状态
        /// </summary>
        Task<int> UpdateMonitoringStatusAsync(List<uint> alidIds, bool isMonitored);
    }
}