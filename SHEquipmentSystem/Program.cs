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
            // ����Serilog��־
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()  // Ĭ����Ϣ����
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                // ����̨��� - ����ʽ
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                // ����־�ļ� - ��Ϣ����
                .WriteTo.File(
                    path: @"..\..\logs\Equipment\equipment-info-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    retainedFileCountLimit: 7)  // ����7��
                                                // SECSͨ����־ - ר�ż�¼SECS��Ϣ
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("SourceContext") &&
                        e.Properties["SourceContext"].ToString().Contains("Secs"))
                    .WriteTo.File(
                        path: @"..\..\logs\Equipment\equipment-secs-.log",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "[{Timestamp:HH:mm:ss.fff}] {Message:lj}{NewLine}",
                        retainedFileCountLimit: 3))
                // ������־ - �����ļ�
                .WriteTo.File(
                    path: @"..\..\logs\Equipment\equipment-error-.log",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    retainedFileCountLimit: 30)  // ������־����30��
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
            // ע����ķ���
            RegisterCoreServices(builder.Services);

            // ע��SECS����
            RegisterSecsServices(builder.Services);

            // ע����Ϣ������
            RegisterMessageHandlers(builder.Services);

            // ע���̨����
            builder.Services.AddHostedService<EquipmentBackgroundService>();
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
        /// <summary>
        /// ע����ķ���
        /// </summary>
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // ע���豸����ģ�ͣ�������ȫ�ֹ���
            services.AddSingleton<DiceEquipmentSystem.Core.Models.DiceDataModel>();

            // ״̬��
            services.AddSingleton<ProcessStateMachine>();

            // �豸����
            services.AddSingleton<IEquipmentStateService, EquipmentStateService>();
            services.AddSingleton<IStatusVariableService, StatusVariableService>();
            services.AddSingleton<IEventReportService, EventReportService>();
            services.AddSingleton<IAlarmService, AlarmServiceImpl>();

            // ע����������Phase 1������
            services.AddSingleton<IProductionService, ProductionService>();

            // ע��SECS/GEM��ʼ��������
            services.AddSingleton<DiceEquipmentSystem.Secs.Initialization.ISecsGemInitializationManager,
                                 DiceEquipmentSystem.Secs.Initialization.SecsGemInitializationManager>();

            //services.AddSingleton<IRecipeService, RecipeService>();
            //services.AddSingleton<ITerminalService, TerminalService>();
        }

        /// <summary>
        /// ע��SECSͨ�ŷ���
        /// </summary>
        private static void RegisterSecsServices(IServiceCollection services)
        {
            // PLC�����ṩ��
            services.AddSingleton<PlcConnectionManager>();
            services.AddSingleton<PlcDataMapper>();
            //services.AddSingleton<PlcDataProviderImpl>();
            //services.AddSingleton<IPlcDataProvider>(provider => provider.GetService<PlcDataProviderImpl>()!);
            //services.AddHostedService<PlcDataProviderImpl>();
            // ����1��ֱ��ע��PlcDataProviderImplΪ����
            services.AddSingleton<PlcDataProviderImpl>();

            // ͨ����������ע��ӿڣ�ȷ������ͬһ��ʵ��
            services.AddSingleton<IPlcDataProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());

            // ͨ����������ע��HostedService��ȷ������ͬһ��ʵ��
            services.AddSingleton<IHostedService>(serviceProvider =>
                serviceProvider.GetRequiredService<PlcDataProviderImpl>());
            // SECS���ӹ�����
            services.AddSingleton<ISecsConnectionManager, SecsConnectionManager>();

            // ��Ϣ�ַ���
            services.AddSingleton<ISecsMessageDispatcher, SecsMessageDispatcher>();

            // ������ݲɼ�����
            services.AddSingleton<IDataCollectionService, DataCollectionService>();

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
            // S1F13Handlerʹ�õ����Ա���ͨ�Ž���״̬
            services.AddSingleton<S1F13Handler>();
            services.AddSingleton<IS1F13Handler>(provider => provider.GetService<S1F13Handler>()!);
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
    } 
}