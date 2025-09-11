using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.StateMachine;
using DiceEquipmentSystem.PLC.Interfaces;
using DiceEquipmentSystem.PLC.Mapping;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.Secs.Handlers;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services;
using DiceEquipmentSystem.Services.Interfaces;
using Serilog;
using Serilog.Events;
using SHEquipmentSystem.PLC.Services;

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
                // 控制台输出 - 简洁格式
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                // 主日志文件 - 信息级别
                .WriteTo.File(
                    path: @"..\..\logs\Equipment\equipment-info-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    retainedFileCountLimit: 7)  // 保留7天
                                                // SECS通信日志 - 专门记录SECS消息
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("SourceContext") &&
                        e.Properties["SourceContext"].ToString().Contains("Secs"))
                    .WriteTo.File(
                        path: @"..\..\logs\Equipment\equipment-secs-.log",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "[{Timestamp:HH:mm:ss.fff}] {Message:lj}{NewLine}",
                        retainedFileCountLimit: 3))
                // 错误日志 - 单独文件
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
                Log.Information("划裂片设备SECS/GEM系统启动");
                Log.Information("==========================================");
            }
            catch (Exception)
            {
                throw;
            }

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            // 配置系统设置
            builder.Services.Configure<EquipmentSystemConfiguration>(
                builder.Configuration.GetSection("EquipmentSystem"));
            // 配置JSON序列化选项
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
                options.SerializerOptions.WriteIndented = true;
            });
            // 注册核心服务
            RegisterCoreServices(builder.Services);

            // 注册SECS服务
            RegisterSecsServices(builder.Services);

            // 注册消息处理器
            RegisterMessageHandlers(builder.Services);

            // 注册后台服务
            builder.Services.AddHostedService<EquipmentBackgroundService>();
            // Add services to the container.
            builder.Services.AddControllersWithViews();
            // 添加内存缓存
            builder.Services.AddMemoryCache();


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }
            app.UseStaticFiles();

            app.UseRouting();
            // 启用CORS（如配置）
            app.UseCors("AllowLocalhost");
            //app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            // 启动设备系统
            await StartEquipmentSystem(app);
            app.Run();
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
                logger.LogInformation("初始化设备状态...");

                // 初始化默认SVID
                InitializeDefaultSvids(svidService, logger);

                // 启动SECS连接（Passive模式，等待Host连接）
                logger.LogInformation("启动SECS通信服务...");
                await connectionManager.StartAsync();

                logger.LogInformation("设备系统启动完成，等待主机连接...");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设备系统启动失败");
                throw;
            }
        }
        /// <summary>
        /// 初始化默认状态变量
        /// </summary>
        private static void InitializeDefaultSvids(IStatusVariableService svidService, Microsoft.Extensions.Logging.ILogger logger)
        {
            logger.LogDebug("初始化默认状态变量...");

            // 这些SVID已在StatusVariableService构造函数中初始化
            // 这里可以添加额外的初始化逻辑
        }
        /// <summary>
        /// 注册核心服务
        /// </summary>
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // 注册设备数据模型（单例，全局共享）
            services.AddSingleton<DiceEquipmentSystem.Core.Models.DiceDataModel>();

            // 状态机
            services.AddSingleton<ProcessStateMachine>();

            // 设备服务
            services.AddSingleton<IEquipmentStateService, EquipmentStateService>();
            services.AddSingleton<IStatusVariableService, StatusVariableService>();
            services.AddSingleton<IEventReportService, EventReportService>();
            services.AddSingleton<IAlarmService, AlarmServiceImpl>();

            // 注册生产服务（Phase 1新增）
            services.AddSingleton<IProductionService, ProductionService>();

            // 注册SECS/GEM初始化管理器
            services.AddSingleton<DiceEquipmentSystem.Secs.Initialization.ISecsGemInitializationManager,
                                 DiceEquipmentSystem.Secs.Initialization.SecsGemInitializationManager>();

            //services.AddSingleton<IRecipeService, RecipeService>();
            //services.AddSingleton<ITerminalService, TerminalService>();
        }

        /// <summary>
        /// 注册SECS通信服务
        /// </summary>
        private static void RegisterSecsServices(IServiceCollection services)
        {
            // PLC数据提供器
            services.AddSingleton<PlcConnectionManager>();
            services.AddSingleton<PlcDataMapper>();
            //services.AddSingleton<PlcDataProviderImpl>();
            //services.AddSingleton<IPlcDataProvider>(provider => provider.GetService<PlcDataProviderImpl>()!);
            //services.AddHostedService<PlcDataProviderImpl>();
            // 方案1：直接注册PlcDataProviderImpl为单例
            services.AddSingleton<PlcDataProviderImpl>();

            // 通过工厂方法注册接口，确保返回同一个实例
            services.AddSingleton<IPlcDataProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());

            // 通过工厂方法注册HostedService，确保返回同一个实例
            services.AddSingleton<IHostedService>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());
            // SECS连接管理器
            services.AddSingleton<ISecsConnectionManager, SecsConnectionManager>();

            // 消息分发器
            services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            // 添加数据采集服务
            services.AddSingleton<IDataCollectionService, DataCollectionService>();

        }

        /// <summary>
        /// 注册所有消息处理器
        /// </summary>
        private static void RegisterMessageHandlers(IServiceCollection services)
        {
            // Stream 1 - 设备状态和通信
            services.AddTransient<S1F1Handler>();
            services.AddTransient<S1F2Handler>();
            services.AddTransient<S1F3Handler>();
            services.AddTransient<S1F11Handler>();
            // S1F13Handler使用单例以保持通信建立状态
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

            // Stream 6 - 事件报告
            services.AddTransient<S6F11Handler>();
            services.AddTransient<S6F15Handler>();
            services.AddTransient<S6F19Handler>();

            // Stream 7 - 配方管理
            //services.AddTransient<S7F1Handler>();
            //services.AddTransient<S7F3Handler>();
            //services.AddTransient<S7F5Handler>();
            //services.AddTransient<S7F17Handler>();
            //services.AddTransient<S7F19Handler>();

            // Stream 10 - 终端服务
            //services.AddTransient<S10F1Handler>();
            //services.AddTransient<S10F3Handler>();
        }
    } 
}