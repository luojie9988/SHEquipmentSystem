// 文件路径: Program.MultiDevice.cs
// 版本: v1.0.0
// 描述: 多设备模式的Program.cs示例

using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Managers;
using DiceEquipmentSystem.PLC.Mapping;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.Secs.Interfaces;
using DiceEquipmentSystem.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace SHEquipmentSystem
{
    public class MultiDeviceProgram
    {
        public static async Task Main(string[] args)
        {
            // 配置Serilog日志
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(
                    path: @"..\..\logs\MultiDevice\system-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    retainedFileCountLimit: 7)
                .WriteTo.File(
                    path: @"..\..\logs\MultiDevice\error-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            try
            {
                Log.Information("==========================================");
                Log.Information("多设备SECS/GEM系统启动");
                Log.Information("==========================================");

                var builder = WebApplication.CreateBuilder(args);
                
                // 添加Windows服务支持
                builder.Host.UseWindowsService();
                builder.Host.UseSerilog();

                // 配置多设备系统
                builder.Services.Configure<MultiEquipmentSystemConfiguration>(
                    builder.Configuration.GetSection("MultiEquipmentSystem"));

                // 注册多设备服务
                RegisterMultiDeviceServices(builder.Services);

                // 注册通用服务
                RegisterCommonServices(builder.Services);

                // 配置Kestrel服务器
                builder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenAnyIP(5001);
                });

                var app = builder.Build();

                // 配置HTTP请求管道
                if (!app.Environment.IsDevelopment())
                {
                    app.UseExceptionHandler("/Home/Error");
                    app.UseHsts();
                }

                app.UseStaticFiles();
                app.UseRouting();
                app.UseAuthorization();

                app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

                // 添加多设备管理路由
                app.MapControllerRoute(
                    name: "multidevice",
                    pattern: "{controller=Home}/{action=MultiDevice}");

                // 启动多设备系统
                await StartMultiDeviceSystem(app);
                
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用程序启动失败");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// 启动多设备系统
        /// </summary>
        private static async Task StartMultiDeviceSystem(IHost host)
        {
            var logger = host.Services.GetRequiredService<ILogger<MultiDeviceProgram>>();
            
            try
            {
                logger.LogInformation("初始化多设备系统...");

                // 获取设备实例服务
                var equipmentInstanceService = host.Services.GetService<IEquipmentInstanceService>();
                if (equipmentInstanceService != null)
                {
                    logger.LogInformation("多设备系统启动完成，等待设备连接...");
                }
                else
                {
                    logger.LogWarning("多设备服务不可用，系统将以降级模式运行");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "多设备系统启动失败");
                throw;
            }
        }

        /// <summary>
        /// 注册多设备服务
        /// </summary>
        private static void RegisterMultiDeviceServices(IServiceCollection services)
        {
            Log.Information("注册多设备服务...");

            // 注册多实例管理器
            services.AddSingleton<IEquipmentInstanceManager, EquipmentInstanceManager>();
            services.AddSingleton<IMultiSecsConnectionManager, MultiSecsConnectionManager>();
            services.AddSingleton<IMultiPlcDataProviderManager, MultiPlcDataProviderManager>();

            // 注册设备实例服务
            services.AddSingleton<IEquipmentInstanceService, EquipmentInstanceService>();

            // 注册多设备管理器作为后台服务

            //services.AddHostedService(provider =>
            //    provider.GetRequiredService<IMultiPlcDataProviderManager>() );
            //services.AddHostedService(provider =>
            //    provider.GetRequiredService<IEquipmentInstanceService>() );

            Log.Information("多设备服务注册完成");
        }

        /// <summary>
        /// 注册通用服务
        /// </summary>
        private static void RegisterCommonServices(IServiceCollection services)
        {
            Log.Information("注册通用服务...");

            // JSON序列化选项
            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
                options.SerializerOptions.WriteIndented = true;
            });

            // 控制器和视图
            services.AddControllersWithViews();

            // 内存缓存
            services.AddMemoryCache();

            // 注册数据映射器
            services.AddSingleton<PlcDataMapper>();

            // 注册消息分发器
            services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            Log.Information("通用服务注册完成");
        }
    }
}

/*
使用方法：

1. 将此文件重命名为 Program.cs 以替换原有文件，或者：

2. 在 appsettings.json 中添加：
{
  "UseMultiDevice": true,
  // ... 其他配置
}

3. 在原有 Program.cs 中的 Main 方法开始处添加：
var useMultiDevice = builder.Configuration.GetValue("UseMultiDevice", false);
if (useMultiDevice)
{
    await MultiDeviceProgram.Main(args);
    return;
}

4. 使用 appsettings.MultiDevice.json 配置文件来配置多设备系统
*/