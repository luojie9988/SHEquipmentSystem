using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Managers;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.Data.Repositories;
using DiceEquipmentSystem.Extensions;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Mapping;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.Secs.Handlers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services;
using DiceEquipmentSystem.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SHEquipmentSystem.PLC.Services;
using SHEquipmentSystem.Services;
using SHEquipmentSystem.Services.Interfaces;

namespace SHEquipmentSystem
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // 配置Serilog日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()  // 默认信息级别
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                // 控制台输出 - 文本格式
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                // 日志文件 - 信息级别
                .WriteTo.File(
                    path: @"..\..\logs\Equipment\equipment-info-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    retainedFileCountLimit: 7)  // 保留7天
                                                // SECS通信日志 - 记录SECS信息
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("SourceContext") &&
                        e.Properties["SourceContext"].ToString().Contains("Secs"))
                    .WriteTo.File(
                        path: @"..\..\logs\Equipment\equipment-secs-.log",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "[{Timestamp:HH:mm:ss.fff}] {Message:lj}{NewLine}",
                        retainedFileCountLimit: 3))
                // 错误日志 - 文件
                .WriteTo.File(
                    path: @"..\..\logs\Equipment\equipment-error-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    retainedFileCountLimit: 30)  // 错误日志保留30天
                .CreateLogger();
            try
            {
                Log.Information("==========================================");
                Log.Information("晶圆设备SECS/GEM系统启动");
                Log.Information("==========================================");
            }
            catch (Exception)
            {
                throw;
            }

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            // 检查是否启用多设备模式
            var useMultiDevice = builder.Configuration.GetValue("UseMultiDevice", false);

            if (useMultiDevice)
            {
                // 多设备模式配置
                builder.Services.Configure<MultiEquipmentSystemConfiguration>(
                    builder.Configuration.GetSection("MultiEquipmentSystem"));
                
                Log.Information("启用多设备模式");
                RegisterMultiDeviceServices(builder.Services);
            }
            else
            {
                // 单设备模式配置（向后兼容）
                builder.Services.Configure<EquipmentSystemConfiguration>(
                    builder.Configuration.GetSection("EquipmentSystem"));
                
                Log.Information("启用单设备模式（向后兼容）");
                RegisterSingleDeviceServices(builder.Services);
            }
            // 确保数据目录存在

            builder.Services.EnsureDataDirectory();
            //// 添加ID映射功能（包含数据库、Repository和服务）
            builder.Services.AddIdMappingFeature(builder.Configuration);
            // 配置JSON序列化选项
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
                options.SerializerOptions.WriteIndented = true;
            });// 配置后台使用 Windows 服务
            builder.Host.UseWindowsService(options =>
            {
                // 配置服务名称，以便在Windows服务管理器中显示
                options.ServiceName = "My WebAPI Service";
            });
            // 注册核心服务
            RegisterCoreServices(builder.Services);

            // 注册SECS服务
            RegisterSecsServices(builder.Services);

            // 注册消息处理器
            RegisterMessageHandlers(builder.Services);

            builder.Services.AddScoped<ISvidMappingRepository, SvidMappingRepository>();
            builder.Services.AddScoped<IIdMappingService, IdMappingService>();

            // 注册后台服务
            builder.Services.AddHostedService<EquipmentBackgroundService>();
            // Add services to the container.
            builder.Services.AddControllersWithViews();
            // 配置内存缓存
            builder.Services.AddMemoryCache();
          
            // 配置Kestrel服务器以使用指定端口
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(5001); // 替换为5000或其他端口号，例如5001
            });
            var app = builder.Build();
            // 验证关键服务是否注册成功
            //using (var scope = app.Services.CreateScope())
            //{
            //    try
            //    {
            //        var dbContext = scope.ServiceProvider.GetRequiredService<IdMappingDbContext>();
            //        var svidRepo = scope.ServiceProvider.GetRequiredService<ISvidMappingRepository>();
            //        var idMappingService = scope.ServiceProvider.GetRequiredService<IIdMappingService>();

            //        app.Logger.LogInformation("所有关键服务注册成功");
            //    }
            //    catch (Exception ex)
            //    {
            //        app.Logger.LogError(ex, "服务注册验证失败");
            //        throw;
            //    }
            //}
            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            app.UseStaticFiles();

            app.UseRouting();
            // 配置CORS策略
            app.UseCors("AllowLocalhost");
            //app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            // 启动设备系统
            await StartEquipmentSystem(app);
            // 确保数据库初始化
           // await app.Services.EnsureIdMappingDatabaseAsync();
            await app.RunAsync();
        }

        /// <summary>
        /// 启动设备系统
        /// </summary>
        private static async Task StartEquipmentSystem(IHost host)
        {
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var connectionManager = host.Services.GetRequiredService<ISecsConnectionManager>();
            var stateService = host.Services.GetRequiredService<IEquipmentStateService>();
            var svidService = host.Services.GetRequiredService<IStatusVariableService>();

            try
            {
                logger.LogInformation("启动设备状态...");
                await stateService.CompleteProcessInitializationAsync();
                // 启动默认SVID
                InitializeDefaultSvids(svidService, logger);

                // 启动SECS通信，Passive模式，等待Host连接
                logger.LogInformation("启动SECS通信...");
                await connectionManager.StartAsync();

                logger.LogInformation("设备系统启动完成，等待连接...");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设备系统启动失败");
                throw;
            }
        }
        /// <summary>
        /// 启动默认状态变量
        /// </summary>
        private static void InitializeDefaultSvids(IStatusVariableService svidService, Microsoft.Extensions.Logging.ILogger logger)
        {
            logger.LogDebug("启动默认状态变量...");

            // 通过StatusVariableService构造函数启动一些SVID
            // 根据需要添加更多的初始值
        }
        /// <summary>
        /// 注册核心服务
        /// </summary>
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // 注册设备数据模型，全局唯一实例
            services.AddSingleton<DiceEquipmentSystem.Core.Models.DiceDataModel>();

            // 状态机
            services.AddSingleton<ProcessStateMachine>();

            // 设备状态
            services.AddSingleton<IEquipmentStateService, EquipmentStateService>();
            services.AddSingleton<IStatusVariableService, StatusVariableService>();
            services.AddSingleton<IEventReportService, EventReportService>();
            services.AddSingleton<IAlarmService, AlarmServiceImpl>();
            //services.AddSingleton<EqpMultiStateManager>();

            // 注册生产服务（Phase 1）
            services.AddSingleton<IProductionService, ProductionService>();

            // 注册SECS/GEM初始化服务
            services.AddSingleton<DiceEquipmentSystem.Secs.Initialization.ISecsGemInitializationManager,
                                 DiceEquipmentSystem.Secs.Initialization.SecsGemInitializationManager>();

            //services.AddSingleton<IRecipeService, RecipeService>();
            //services.AddSingleton<ITerminalService, TerminalService>();
        }

        /// <summary>
        /// 注册SECS通信
        /// </summary>
        private static void RegisterSecsServices(IServiceCollection services)
        {
            // PLC数据提供服务
            services.AddSingleton<PlcConnectionManager>();
            services.AddSingleton<PlcDataMapper>();
            //services.AddSingleton<PlcDataProviderImpl>();
            //services.AddSingleton<IPlcDataProvider>(provider => provider.GetService<PlcDataProviderImpl>()!);
            //services.AddHostedService<PlcDataProviderImpl>();
            //直接注册PlcDataProviderImpl为服务
            services.AddSingleton<PlcDataProviderImpl>();

            // 通过服务提供者注册接口，确保同一实例
            services.AddSingleton<IPlcDataProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());
            // 通过服务提供者注册HostedService，确保同一实例
            services.AddSingleton<IHostedService>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());
            // SECS通信管理器
            services.AddSingleton<ISecsConnectionManager, SecsConnectionManager>();

            // 消息分发器
            services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            // 数据采集服务
            services.AddSingleton<IDataCollectionService, DataCollectionService>();

        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        private static void RegisterMessageHandlers(IServiceCollection services)
        {
            // Stream 1 - 设备状态通信
            services.AddTransient<S1F1Handler>();
            services.AddTransient<S1F2Handler>();
            services.AddTransient<S1F3Handler>();
            services.AddTransient<S1F11Handler>();
            // S1F13Handler使用单例模式处理状态
            services.AddSingleton<S1F13Handler>();
            services.AddSingleton<IS1F13Handler>(provider => provider.GetService<S1F13Handler>()!);
            services.AddTransient<S1F15Handler>();
            services.AddTransient<S1F17Handler>();

            // Stream 2 - 设备控制
            services.AddTransient<S2F13Handler>();
            services.AddTransient<S2F15Handler>();
            services.AddTransient<S2F29Handler>();
            services.AddTransient<S2F23Handler>();
            //services.AddTransient<S2F31Handler>();
            services.AddTransient<S2F33Handler>();
            services.AddTransient<S2F35Handler>();
            services.AddTransient<S2F37Handler>();
            services.AddTransient<S2F41Handler>();

            // Stream 6 - 日志通信
            services.AddTransient<S6F11Handler>();
            services.AddTransient<S6F15Handler>();
            services.AddTransient<S6F19Handler>();

            // Stream 7 - 诊断通信
            //services.AddTransient<S7F1Handler>();
            //services.AddTransient<S7F3Handler>();
            //services.AddTransient<S7F5Handler>();
            //services.AddTransient<S7F17Handler>();
            //services.AddTransient<S7F19Handler>();

            // Stream 10 - 远程命令
            //services.AddTransient<S10F1Handler>();
            //services.AddTransient<S10F3Handler>();
        }

        /// <summary>
        /// 注册单设备服务
        /// </summary>
        private static void RegisterSingleDeviceServices(IServiceCollection services)
        {
            // 注册核心服务
            RegisterCoreServices(services);

            // 注册SECS服务
            RegisterSecsServices(services);

            // 注册消息处理器
            RegisterMessageHandlers(services);

            // 注册后台服务
            services.AddHostedService<EquipmentBackgroundService>();
        }

        /// <summary>
        /// 注册多设备服务
        /// </summary>
        private static void RegisterMultiDeviceServices(IServiceCollection services)
        {
            // 注册核心服务
            RegisterCoreServices(services);

            // 注册SECS服务
            RegisterSecsServices(services);

            // 注册消息处理器
            RegisterMessageHandlers(services);

            // 注册后台服务
            services.AddHostedService<EquipmentBackgroundService>();
        }
    } 
}