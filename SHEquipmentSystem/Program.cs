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

namespace SHEquipmentSystem
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // 配置Serilog日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/dice-equipment-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
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

            // 状态机
            builder.Services.AddSingleton<ProcessStateMachine>();

            // 设备服务
            builder.Services.AddSingleton<IEquipmentStateService, EquipmentStateService>();
            builder.Services.AddSingleton<IStatusVariableService, StatusVariableService>();
            builder.Services.AddSingleton<IEventReportService, EventReportService>();
            builder.Services.AddSingleton<IAlarmService, AlarmServiceImpl>();

            // PLC数据提供器
            builder.Services.AddSingleton<PlcConnectionManager>();
            builder.Services.AddSingleton<PlcDataMapper>();
            builder.Services.AddSingleton<PlcDataProviderImpl>();
            builder.Services.AddSingleton<IPlcDataProvider>(provider => provider.GetService<PlcDataProviderImpl>()!);
            builder.Services.AddHostedService<PlcDataProviderImpl>();

            // SECS连接管理器
            builder.Services.AddSingleton<ISecsConnectionManager, SecsConnectionManager>();

            // 消息分发器
            builder.Services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            // 添加数据采集服务
            builder.Services.AddSingleton<IDataCollectionService, DataCollectionService>();

            RegisterMessageHandlers(builder.Services);

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
        /// 注册所有消息处理器
        /// </summary>
        private static void RegisterMessageHandlers(IServiceCollection services)
        {
            // Stream 1 - 设备状态和通信
            services.AddTransient<S1F1Handler>();
            services.AddTransient<S1F2Handler>();
            services.AddTransient<S1F3Handler>();
            services.AddTransient<S1F11Handler>();
            services.AddTransient<S1F13Handler>();
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
    }
}