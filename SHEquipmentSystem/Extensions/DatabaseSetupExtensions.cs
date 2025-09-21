// 文件路径: src/DiceEquipmentSystem/Extensions/DatabaseSetupExtensions.cs
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiceEquipmentSystem.Extensions
{
    public static class DatabaseSetupExtensions
    {
        /// <summary>
        /// 确保数据文件夹存在
        /// </summary>
        public static IServiceCollection EnsureDataDirectory(this IServiceCollection services)
        {
            try
            {
                var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
                if (!Directory.Exists(dataDirectory))
                {
                    Directory.CreateDirectory(dataDirectory);
                    Console.WriteLine($"创建数据目录: {dataDirectory}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"创建数据目录失败: {ex.Message}");
            }

            return services;
        }
    }
}