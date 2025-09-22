// 文件路径: src/DiceEquipmentSystem/Extensions/ServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiceEquipmentSystem.Data;
using DiceEquipmentSystem.Data.Repositories;
using DiceEquipmentSystem.Data.Entities;
using SHEquipmentSystem.Services.Interfaces;
using SHEquipmentSystem.Services;

namespace DiceEquipmentSystem.Extensions
{
    /// <summary>
    /// 服务注册扩展类
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加ID映射数据库服务
        /// </summary>
        public static IServiceCollection AddIdMappingDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            // 确保数据目录存在
            var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            // 获取连接字符串
            var connectionString = configuration.GetConnectionString("IdMappingDatabase")
                ?? Path.Combine(dataDirectory, "idmapping.db");

            // 如果连接字符串不包含"Data Source="，则添加
            if (!connectionString.Contains("Data Source="))
            {
                connectionString = $"Data Source={connectionString}";
            }

            // 注册DbContext
            services.AddDbContext<IdMappingDbContext>(options =>
            {
                options.UseSqlite(connectionString, sqliteOptions =>
                {
                    sqliteOptions.MigrationsAssembly(typeof(IdMappingDbContext).Assembly.FullName);
                });

                // 开发环境启用详细日志
                if (configuration.GetValue<bool>("Logging:EnableSensitiveDataLogging", false))
                {
                    options.EnableSensitiveDataLogging();
                    options.EnableDetailedErrors();
                }

                // 启用查询跟踪
                options.LogTo(Console.WriteLine, LogLevel.Information);
            });

            return services;
        }

        /// <summary>
        /// 添加Repository服务
        /// </summary>
        public static IServiceCollection AddIdMappingRepositories(this IServiceCollection services)
        {
            // 注册泛型Repository
            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

            // 注册所有 Repository 接口和实现
            services.AddScoped<ISvidMappingRepository, SvidMappingRepository>();
            services.AddScoped<ICeidMappingRepository, CeidMappingRepository>();
            services.AddScoped<IRptidMappingRepository, RptidMappingRepository>();
            services.AddScoped<IAlidMappingRepository, AlidMappingRepository>();

            return services;
        }

        /// <summary>
        /// 添加ID映射业务服务
        /// </summary>
        public static IServiceCollection AddIdMappingServices(this IServiceCollection services)
        {
            services.AddScoped<IIdMappingService, IdMappingService>();
            return services;
        }

        /// <summary>
        /// 添加完整的ID映射功能
        /// </summary>
        public static IServiceCollection AddIdMappingFeature(this IServiceCollection services, IConfiguration configuration)
        {
            // 按正确的顺序注册服务
            services.AddIdMappingDatabase(configuration);
            services.AddIdMappingRepositories();
            services.AddIdMappingServices();

            return services;
        }

        /// <summary>
        /// 确保数据库已创建并初始化
        /// </summary>
        public static async Task<IServiceProvider> EnsureIdMappingDatabaseAsync(this IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var context = services.GetRequiredService<IdMappingDbContext>();
                var logger = services.GetRequiredService<ILogger<IdMappingDbContext>>();

                // 确保数据库创建
                var created = await context.Database.EnsureCreatedAsync();
                if (created)
                {
                    logger.LogInformation("ID映射数据库已创建");
                }
                else
                {
                    logger.LogInformation("ID映射数据库已存在");
                }

                // 初始化种子数据
                await SeedDataAsync(context, logger);

                return serviceProvider;
            }
            catch (Exception ex)
            {
                //var logger = services.GetRequiredService<ILogger<ServiceCollectionExtensions>>();
                //logger.LogError(ex, "初始化ID映射数据库失败");
                throw;
            }
        }

        /// <summary>
        /// 种子数据初始化
        /// </summary>
        private static async Task SeedDataAsync(IdMappingDbContext context, ILogger logger)
        {
            try
            {
                // 检查是否已有数据
                if (context.SvidMappings.Any())
                {
                    logger.LogDebug("数据库已存在数据，跳过种子数据初始化");
                    return;
                }

                logger.LogInformation("开始初始化种子数据");

                // 添加默认SVID映射
                var defaultSvidMappings = new[]
                {
                    new SvidMapping
                    {
                        SvidId = 10001,
                        SvidName = "ControlState",
                        PlcAddress = "D100",
                        DataType = PLC.Models.PlcDataType.Int16,
                        Description = "Equipment control state",
                        Units = null,
                        IsActive = true
                    },
                    new SvidMapping
                    {
                        SvidId = 10002,
                        SvidName = "ProcessState",
                        PlcAddress = "D101",
                        DataType = PLC.Models.PlcDataType.Int16,
                        Description = "Process state",
                        Units = null,
                        IsActive = true
                    },
                    new SvidMapping
                    {
                        SvidId = 10003,
                        SvidName = "AlarmCount",
                        PlcAddress = "D102",
                        DataType = PLC.Models.PlcDataType.Int16,
                        Description = "Active alarm count",
                        Units = "count",
                        IsActive = true
                    }
                };

                context.SvidMappings.AddRange(defaultSvidMappings);

                // 添加默认CEID映射
                var defaultCeidMappings = new[]
                {
                    new CeidMapping
                    {
                        CeidId = 11001,
                        EventName = "ProcessStateChange",
                        TriggerAddress = "D101",
                        TriggerType = Core.Enums.TriggerType.ValueChange,
                        Description = "Process state change event",
                        IsEnabled = true
                    },
                    new CeidMapping
                    {
                        CeidId = 11002,
                        EventName = "AlarmSet",
                        TriggerAddress = "M100",
                        TriggerType = Core.Enums.TriggerType.RisingEdge,
                        Description = "Alarm set event",
                        IsEnabled = true
                    }
                };

                context.CeidMappings.AddRange(defaultCeidMappings);

                await context.SaveChangesAsync();
                logger.LogInformation("种子数据初始化完成");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "种子数据初始化失败");
                throw;
            }
        }
    }
}