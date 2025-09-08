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
            // ����Serilog��־
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
                Log.Information("����Ƭ�豸SECS/GEMϵͳ����");
                Log.Information("==========================================");
            }
            catch (Exception)
            {
                throw;
            }

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            // ����ϵͳ����
            builder.Services.Configure<EquipmentSystemConfiguration>(
                builder.Configuration.GetSection("EquipmentSystem"));
            // ����JSON���л�ѡ��
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
                options.SerializerOptions.WriteIndented = true;
            });

            // ״̬��
            builder.Services.AddSingleton<ProcessStateMachine>();

            // �豸����
            builder.Services.AddSingleton<IEquipmentStateService, EquipmentStateService>();
            builder.Services.AddSingleton<IStatusVariableService, StatusVariableService>();
            builder.Services.AddSingleton<IEventReportService, EventReportService>();
            builder.Services.AddSingleton<IAlarmService, AlarmServiceImpl>();

            // PLC�����ṩ��
            builder.Services.AddSingleton<PlcConnectionManager>();
            builder.Services.AddSingleton<PlcDataMapper>();
            builder.Services.AddSingleton<PlcDataProviderImpl>();
            builder.Services.AddSingleton<IPlcDataProvider>(provider => provider.GetService<PlcDataProviderImpl>()!);
            builder.Services.AddHostedService<PlcDataProviderImpl>();

            // SECS���ӹ�����
            builder.Services.AddSingleton<ISecsConnectionManager, SecsConnectionManager>();

            // ��Ϣ�ַ���
            builder.Services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            // ������ݲɼ�����
            builder.Services.AddSingleton<IDataCollectionService, DataCollectionService>();

            RegisterMessageHandlers(builder.Services);

            // Add services to the container.
            builder.Services.AddControllersWithViews();
            // ����ڴ滺��
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
            // ����CORS�������ã�
            app.UseCors("AllowLocalhost");
            //app.UseSession();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
            // �����豸ϵͳ
            await StartEquipmentSystem(app);
            app.Run();
        }
        /// <summary>
        /// ע��������Ϣ������
        /// </summary>
        private static void RegisterMessageHandlers(IServiceCollection services)
        {
            // Stream 1 - �豸״̬��ͨ��
            services.AddTransient<S1F1Handler>();
            services.AddTransient<S1F2Handler>();
            services.AddTransient<S1F3Handler>();
            services.AddTransient<S1F11Handler>();
            services.AddTransient<S1F13Handler>();
            services.AddTransient<S1F15Handler>();
            services.AddTransient<S1F17Handler>();

            // Stream 2 - �豸����
            services.AddTransient<S2F13Handler>();
            services.AddTransient<S2F15Handler>();
            services.AddTransient<S2F29Handler>();
            services.AddTransient<S2F23Handler>();
            //services.AddTransient<S2F31Handler>();
            services.AddTransient<S2F33Handler>();
            services.AddTransient<S2F35Handler>();
            services.AddTransient<S2F37Handler>();
            services.AddTransient<S2F41Handler>();

            // Stream 6 - �¼�����
            services.AddTransient<S6F11Handler>();
            services.AddTransient<S6F15Handler>();
            services.AddTransient<S6F19Handler>();

            // Stream 7 - �䷽����
            //services.AddTransient<S7F1Handler>();
            //services.AddTransient<S7F3Handler>();
            //services.AddTransient<S7F5Handler>();
            //services.AddTransient<S7F17Handler>();
            //services.AddTransient<S7F19Handler>();

            // Stream 10 - �ն˷���
            //services.AddTransient<S10F1Handler>();
            //services.AddTransient<S10F3Handler>();
        }
        /// <summary>
        /// �����豸ϵͳ
        /// </summary>
        private static async Task StartEquipmentSystem(IHost host)
        {
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var connectionManager = host.Services.GetRequiredService<ISecsConnectionManager>();
            var stateService = host.Services.GetRequiredService<IEquipmentStateService>();
            var svidService = host.Services.GetRequiredService<IStatusVariableService>();

            try
            {
                logger.LogInformation("��ʼ���豸״̬...");

                // ��ʼ��Ĭ��SVID
                InitializeDefaultSvids(svidService, logger);

                // ����SECS���ӣ�Passiveģʽ���ȴ�Host���ӣ�
                logger.LogInformation("����SECSͨ�ŷ���...");
                await connectionManager.StartAsync();

                logger.LogInformation("�豸ϵͳ������ɣ��ȴ���������...");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "�豸ϵͳ����ʧ��");
                throw;
            }
        }
        /// <summary>
        /// ��ʼ��Ĭ��״̬����
        /// </summary>
        private static void InitializeDefaultSvids(IStatusVariableService svidService, Microsoft.Extensions.Logging.ILogger logger)
        {
            logger.LogDebug("��ʼ��Ĭ��״̬����...");

            // ��ЩSVID����StatusVariableService���캯���г�ʼ��
            // ���������Ӷ���ĳ�ʼ���߼�
        }
    }
}