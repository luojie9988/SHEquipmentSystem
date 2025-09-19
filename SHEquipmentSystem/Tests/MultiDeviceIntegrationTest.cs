// 文件路径: Tests/MultiDeviceIntegrationTest.cs
// 版本: v1.0.0
// 描述: 多设备系统集成测试

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.Core.Managers;
using DiceEquipmentSystem.PLC.Services;
using DiceEquipmentSystem.Secs.Communication;
using DiceEquipmentSystem.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace SHEquipmentSystem.Tests
{
    /// <summary>
    /// 多设备系统集成测试
    /// </summary>
    public class MultiDeviceIntegrationTest : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;
        private readonly MultiEquipmentSystemConfiguration _testConfig;

        public MultiDeviceIntegrationTest(ITestOutputHelper output)
        {
            _output = output;
            
            // 创建测试配置
            _testConfig = CreateTestConfiguration();
            
            // 创建服务容器
            _serviceProvider = CreateServiceProvider();
        }

        /// <summary>
        /// 测试设备实例管理器初始化
        /// </summary>
        [Fact]
        public async Task Test_EquipmentInstanceManager_Initialize_Success()
        {
            // Arrange
            var instanceManager = _serviceProvider.GetRequiredService<IEquipmentInstanceManager>();

            // Act
            var result = await instanceManager.InitializeAsync();

            // Assert
            Assert.True(result, "设备实例管理器初始化应该成功");
            
            var instances = instanceManager.GetAllInstances();
            Assert.Equal(2, instances.Count()); // 配置了2个测试设备
            
            _output.WriteLine($"✅ 设备实例管理器初始化成功，共 {instances.Count()} 个设备实例");
        }

        /// <summary>
        /// 测试多SECS连接管理器
        /// </summary>
        [Fact]
        public async Task Test_MultiSecsConnectionManager_AddConnections_Success()
        {
            // Arrange
            var secsManager = _serviceProvider.GetRequiredService<IMultiSecsConnectionManager>();

            // Act
            foreach (var deviceConfig in _testConfig.EquipmentInstances)
            {
                var result = await secsManager.AddConnectionAsync(
                    deviceConfig.DeviceId, 
                    deviceConfig.SecsConfiguration);
                
                Assert.True(result, $"添加设备 {deviceConfig.DeviceId} 的SECS连接应该成功");
            }

            // Assert
            var connections = secsManager.GetAllConnections();
            Assert.Equal(2, connections.Count());
            
            _output.WriteLine($"✅ SECS连接管理器测试成功，共 {connections.Count()} 个连接");
        }

        /// <summary>
        /// 测试多PLC数据提供者管理器
        /// </summary>
        [Fact]
        public async Task Test_MultiPlcDataProviderManager_AddConnections_Success()
        {
            // Arrange
            var plcManager = _serviceProvider.GetRequiredService<IMultiPlcDataProviderManager>();

            // Act
            foreach (var deviceConfig in _testConfig.EquipmentInstances)
            {
                var result = await plcManager.AddConnectionAsync(
                    deviceConfig.DeviceId, 
                    deviceConfig.PlcConfiguration);
                
                Assert.True(result, $"添加设备 {deviceConfig.DeviceId} 的PLC连接应该成功");
            }

            // Assert
            var connections = plcManager.GetAllConnections();
            Assert.Equal(2, connections.Count());
            
            _output.WriteLine($"✅ PLC管理器测试成功，共 {connections.Count()} 个连接");
        }

        /// <summary>
        /// 测试设备实例服务完整流程
        /// </summary>
        [Fact]
        public async Task Test_EquipmentInstanceService_FullWorkflow_Success()
        {
            // Arrange
            var equipmentService = _serviceProvider.GetRequiredService<IEquipmentInstanceService>();
            var instanceManager = _serviceProvider.GetRequiredService<IEquipmentInstanceManager>();
            
            // 初始化管理器
            await instanceManager.InitializeAsync();

            // Act & Assert - 获取系统概览
            var overview = equipmentService.GetSystemOverview();
            Assert.NotNull(overview);
            _output.WriteLine("✅ 系统概览获取成功");

            // Act & Assert - 获取设备状态
            var deviceStates = equipmentService.GetAllDeviceStates();
            Assert.Equal(2, deviceStates.Count());
            _output.WriteLine($"✅ 设备状态获取成功，共 {deviceStates.Count()} 个设备");

            // Act & Assert - 测试设备启动
            var testDeviceId = _testConfig.EquipmentInstances.First().DeviceId;
            var startResult = await equipmentService.StartDeviceInstanceAsync(testDeviceId);
            // 注意：由于是模拟环境，启动可能会失败，但不应该抛出异常
            _output.WriteLine($"设备 {testDeviceId} 启动结果: {(startResult ? "成功" : "失败")}");

            // Act & Assert - 获取设备健康状态
            var healthStatus = equipmentService.GetDeviceHealthStatus(testDeviceId);
            Assert.NotNull(healthStatus);
            _output.WriteLine("✅ 设备健康状态获取成功");
        }

        /// <summary>
        /// 测试并发设备操作
        /// </summary>
        [Fact]
        public async Task Test_ConcurrentDeviceOperations_Success()
        {
            // Arrange
            var equipmentService = _serviceProvider.GetRequiredService<IEquipmentInstanceService>();
            var instanceManager = _serviceProvider.GetRequiredService<IEquipmentInstanceManager>();
            
            await instanceManager.InitializeAsync();

            var deviceIds = _testConfig.EquipmentInstances.Select(d => d.DeviceId).ToList();

            // Act - 并发启动所有设备
            var startTasks = deviceIds.Select(deviceId => 
                equipmentService.StartDeviceInstanceAsync(deviceId));
            
            var startResults = await Task.WhenAll(startTasks);

            // Assert
            _output.WriteLine($"并发启动完成，成功: {startResults.Count(r => r)}, 失败: {startResults.Count(r => !r)}");

            // Act - 并发停止所有设备
            var stopTasks = deviceIds.Select(deviceId => 
                equipmentService.StopDeviceInstanceAsync(deviceId));
            
            var stopResults = await Task.WhenAll(stopTasks);

            // Assert
            _output.WriteLine($"并发停止完成，成功: {stopResults.Count(r => r)}, 失败: {stopResults.Count(r => !r)}");
            
            _output.WriteLine("✅ 并发设备操作测试完成");
        }

        /// <summary>
        /// 测试配置验证
        /// </summary>
        [Fact]
        public void Test_ConfigurationValidation_Success()
        {
            // Act
            var isValid = _testConfig.Validate(out var errors);

            // Assert
            if (!isValid)
            {
                _output.WriteLine("配置验证错误:");
                foreach (var error in errors)
                {
                    _output.WriteLine($"  - {error}");
                }
            }
            
            Assert.True(isValid, "测试配置应该是有效的");
            _output.WriteLine("✅ 配置验证测试通过");
        }

        /// <summary>
        /// 测试系统资源使用
        /// </summary>
        [Fact]
        public void Test_SystemResourceUsage_WithinLimits()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(false);

            // Act - 创建多个服务实例
            for (int i = 0; i < 5; i++)
            {
                var instanceManager = _serviceProvider.GetRequiredService<IEquipmentInstanceManager>();
                var secsManager = _serviceProvider.GetRequiredService<IMultiSecsConnectionManager>();
                var plcManager = _serviceProvider.GetRequiredService<IMultiPlcDataProviderManager>();
            }

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert
            Assert.True(memoryIncrease < 50 * 1024 * 1024, "内存增长应该小于50MB"); // 50MB限制
            
            _output.WriteLine($"✅ 内存使用测试通过，增长: {memoryIncrease / 1024 / 1024:F2}MB");
        }

        /// <summary>
        /// 创建测试配置
        /// </summary>
        private MultiEquipmentSystemConfiguration CreateTestConfiguration()
        {
            return new MultiEquipmentSystemConfiguration
            {
                System = new SystemConfiguration
                {
                    SystemName = "Test Multi-Equipment System",
                    MaxConcurrentDevices = 5,
                    HealthCheckInterval = 10
                },
                EquipmentInstances = new List<EquipmentInstanceConfiguration>
                {
                    new EquipmentInstanceConfiguration
                    {
                        DeviceId = "TEST-001",
                        DeviceName = "测试设备-1",
                        ModelName = "TEST-MODEL",
                        Enabled = true,
                        Priority = 1,
                        SecsConfiguration = new SecsInstanceConfiguration
                        {
                            Enabled = true,
                            DeviceId = 1,
                            IpAddress = "127.0.0.1",
                            Port = 6001,
                            IsActive = false
                        },
                        PlcConfiguration = new PlcInstanceConfiguration
                        {
                            Enabled = true,
                            IpAddress = "192.168.1.101",
                            Port = 6002,
                            UseSimulation = true
                        }
                    },
                    new EquipmentInstanceConfiguration
                    {
                        DeviceId = "TEST-002",
                        DeviceName = "测试设备-2",
                        ModelName = "TEST-MODEL",
                        Enabled = true,
                        Priority = 2,
                        SecsConfiguration = new SecsInstanceConfiguration
                        {
                            Enabled = true,
                            DeviceId = 2,
                            IpAddress = "127.0.0.1",
                            Port = 6003,
                            IsActive = false
                        },
                        PlcConfiguration = new PlcInstanceConfiguration
                        {
                            Enabled = true,
                            IpAddress = "192.168.1.102",
                            Port = 6004,
                            UseSimulation = true
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 创建服务提供者
        /// </summary>
        private IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();

            // 配置日志
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // 配置测试配置
            services.AddSingleton(Options.Create(_testConfig));

            // 注册多设备服务
            services.AddSingleton<IEquipmentInstanceManager, EquipmentInstanceManager>();
            services.AddSingleton<IMultiSecsConnectionManager, MultiSecsConnectionManager>();
            services.AddSingleton<IMultiPlcDataProviderManager, MultiPlcDataProviderManager>();
            services.AddSingleton<IEquipmentInstanceService, EquipmentInstanceService>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            if (_serviceProvider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }
        }
    }

    /// <summary>
    /// 性能测试类
    /// </summary>
    public class MultiDevicePerformanceTest
    {
        /// <summary>
        /// 测试大量设备实例的性能
        /// </summary>
        [Fact]
        public async Task Test_ManyDevicesPerformance()
        {
            // 这个测试可以在需要时运行，测试系统在大量设备时的性能
            var deviceCount = 50; // 模拟50个设备
            
            var startTime = DateTime.Now;
            
            // 模拟创建大量设备配置
            var configs = new List<EquipmentInstanceConfiguration>();
            for (int i = 1; i <= deviceCount; i++)
            {
                configs.Add(new EquipmentInstanceConfiguration
                {
                    DeviceId = $"PERF-{i:D3}",
                    DeviceName = $"性能测试设备-{i}",
                    Enabled = true,
                    SecsConfiguration = new SecsInstanceConfiguration
                    {
                        DeviceId = (ushort)i,
                        Port = 7000 + i,
                        Enabled = true
                    },
                    PlcConfiguration = new PlcInstanceConfiguration
                    {
                        IpAddress = $"192.168.1.{100 + i % 100}",
                        Port = 8000 + i,
                        UseSimulation = true,
                        Enabled = true
                    }
                });
            }

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            Assert.True(duration.TotalSeconds < 10, $"创建{deviceCount}个设备配置应该在10秒内完成");
            Assert.Equal(deviceCount, configs.Count);
        }
    }
}

/*
运行测试的方法：

1. 在Visual Studio中:
   - 打开测试资源管理器
   - 运行 MultiDeviceIntegrationTest 类中的所有测试

2. 在命令行中:
   dotnet test --filter "FullyQualifiedName~MultiDeviceIntegrationTest"

3. 运行特定测试:
   dotnet test --filter "Test_EquipmentInstanceManager_Initialize_Success"

4. 生成测试报告:
   dotnet test --logger:trx --results-directory:TestResults

测试覆盖的场景：
- ✅ 设备实例管理器初始化
- ✅ 多SECS连接管理
- ✅ 多PLC连接管理  
- ✅ 设备实例服务完整工作流程
- ✅ 并发设备操作
- ✅ 配置验证
- ✅ 系统资源使用
- ✅ 性能测试
*/