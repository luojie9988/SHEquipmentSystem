# 多设备SECS/GEM系统使用指南

## 🚀 概述

本解决方案将原有的单设备SECS/GEM系统改造为支持多个独立设备实例并行运行的架构。每个设备实例包含独立的SECS连接和PLC连接，可以独立启动、停止和管理。

## 🏗️ 架构特点

### 核心组件
- **IEquipmentInstanceManager**: 设备实例管理器，负责设备实例的生命周期管理
- **IMultiSecsConnectionManager**: 多SECS连接管理器，管理多个SECS连接实例
- **IMultiPlcDataProviderManager**: 多PLC数据提供者管理器，管理多个PLC连接实例
- **IEquipmentInstanceService**: 设备实例服务，整合SECS和PLC为完整的设备实例

### 设计优势
1. **独立性**: 每个设备实例完全独立，不会相互影响
2. **可扩展性**: 可以动态添加或移除设备实例
3. **向后兼容**: 保持原有单设备模式的兼容性
4. **统一管理**: 提供统一的Web界面管理所有设备
5. **并行处理**: 支持多设备并行操作

## 📁 文件结构

### 新增核心文件
```
src/
├── Core/
│   ├── Configuration/
│   │   └── MultiEquipmentConfiguration.cs     # 多设备配置模型
│   └── Managers/
│       └── EquipmentInstanceManager.cs        # 设备实例管理器
├── Secs/
│   └── Communication/
│       └── MultiSecsConnectionManager.cs      # 多SECS连接管理器
├── PLC/
│   └── Services/
│       └── MultiPlcDataProviderManager.cs     # 多PLC管理器
├── Services/
│   └── EquipmentInstanceService.cs            # 设备实例服务
└── Controllers/
    └── MultiDeviceController.cs               # 多设备控制器

配置文件/
├── appsettings.MultiDevice.json               # 多设备配置示例
├── Program.MultiDevice.cs                     # 多设备启动程序示例
└── Views/Home/MultiDevice.cshtml              # 多设备管理页面
```

## 🔧 配置说明

### 1. 多设备配置文件 (appsettings.MultiDevice.json)

```json
{
  "MultiEquipmentSystem": {
    "System": {
      "SystemName": "SH Multi-Equipment System",
      "MaxConcurrentDevices": 10,
      "HealthCheckInterval": 30
    },
    "EquipmentInstances": [
      {
        "DeviceId": "DICER-001",
        "DeviceName": "划裂片设备-1",
        "Enabled": true,
        "Priority": 1,
        "SecsConfiguration": {
          "DeviceId": 1,
          "Port": 5001,
          "IpAddress": "127.0.0.1"
        },
        "PlcConfiguration": {
          "IpAddress": "192.168.3.101",
          "Port": 5007,
          "UseSimulation": true
        }
      }
    ]
  }
}
```

### 2. 启用多设备模式

在 `appsettings.json` 中添加：
```json
{
  "UseMultiDevice": true
}
```

## 🚀 启动和使用

### 1. 系统启动
```bash
# 使用多设备配置启动
dotnet run --environment=Production --configuration=appsettings.MultiDevice.json
```

### 2. 访问管理界面
- 主页: `http://localhost:5001`
- 多设备管理: `http://localhost:5001/Home/MultiDevice`
- API文档: `http://localhost:5001/api/MultiDevice/overview`

### 3. 核心API端点

#### 系统概览
```http
GET /api/MultiDevice/overview
```

#### 设备列表
```http
GET /api/MultiDevice/devices
```

#### 设备操作
```http
POST /api/MultiDevice/devices/{deviceId}/start    # 启动设备
POST /api/MultiDevice/devices/{deviceId}/stop     # 停止设备  
POST /api/MultiDevice/devices/{deviceId}/restart  # 重启设备
```

#### 批量操作
```http
POST /api/MultiDevice/devices/batch-operation
{
  "DeviceIds": ["DICER-001", "DICER-002"],
  "Action": "start",
  "Parallel": true
}
```

#### 连接状态
```http
GET /api/MultiDevice/connections/secs             # SECS连接状态
GET /api/MultiDevice/connections/plc              # PLC连接状态
```

## 🔍 功能验证

### 1. 基本功能测试

#### 启动系统
1. 使用 `Program.MultiDevice.cs` 启动应用
2. 检查日志输出确认多设备服务注册成功
3. 访问 `/Home/MultiDevice` 页面

#### 设备管理测试
1. 查看系统概览，确认设备总数正确
2. 查看设备列表，确认所有配置的设备显示
3. 测试单个设备启动/停止
4. 测试批量设备操作
5. 查看设备详情和统计信息

#### 连接状态测试
1. 检查SECS连接状态
2. 检查PLC连接状态（模拟模式）
3. 验证连接断开重连机制

### 2. 并发性能测试

#### 多设备并行启动
```csharp
// 同时启动多个设备
var tasks = deviceIds.Select(deviceId => 
    httpClient.PostAsync($"/api/MultiDevice/devices/{deviceId}/start", null));
await Task.WhenAll(tasks);
```

#### 负载测试
```bash
# 使用工具测试API并发性能
curl -X POST http://localhost:5001/api/MultiDevice/devices/batch-operation \
  -H "Content-Type: application/json" \
  -d '{"DeviceIds":["DICER-001","DICER-002"],"Action":"start","Parallel":true}'
```

### 3. 故障恢复测试

#### 单设备故障
1. 故意断开某个设备的PLC连接
2. 验证其他设备不受影响
3. 检查错误报告和自动重连

#### 系统重启测试
1. 重启应用程序
2. 验证所有设备自动恢复连接
3. 检查状态数据完整性

## 📊 监控和诊断

### 1. 日志分析
- 系统日志: `logs/MultiDevice/system-*.log`
- 错误日志: `logs/MultiDevice/error-*.log`
- 设备专用日志: `logs/Device-{DeviceId}/`

### 2. 性能指标
- 设备启动时间
- SECS/PLC连接建立时间
- 消息处理延迟
- 内存和CPU使用率

### 3. 健康检查
```http
GET /api/MultiDevice/devices/{deviceId}
```
返回设备健康状态，包括：
- 连接状态
- 运行时长
- 错误计数
- 统计信息

## ⚠️ 注意事项

### 1. 端口配置
- 确保每个SECS设备配置不同的端口
- PLC IP地址不能重复
- 防火墙设置正确

### 2. 资源限制
- 监控内存使用，避免过多设备实例
- 配置合理的线程池大小
- 设置适当的超时时间

### 3. 并发控制
- 避免同时对同一设备进行多个操作
- 使用信号量控制并发访问
- 合理设置批量操作的并行度

## 🔄 从单设备迁移

### 1. 配置迁移
```bash
# 将现有配置转换为多设备格式
# 原配置 -> 多设备配置的第一个实例
```

### 2. 代码兼容性
- 原有单设备API继续可用
- 新增多设备API不影响现有代码
- 渐进式迁移策略

### 3. 数据迁移
- 设备状态数据结构兼容
- 历史记录数据保持
- 配置参数平滑过渡

## 🎯 最佳实践

### 1. 设备命名规范
- 使用有意义的设备ID: `DICER-001`, `DICER-002`
- 设备名称包含位置信息: `"生产线A-划裂片设备"`
- 优先级设置合理: 主要设备优先级高

### 2. 监控策略
- 设置合理的健康检查间隔
- 启用按设备分离的日志
- 配置告警阈值

### 3. 部署建议
- 使用容器化部署支持弹性伸缩
- 配置负载均衡和高可用
- 实施蓝绿部署策略

---

## 📞 技术支持

如有问题，请检查：
1. 日志文件中的错误信息
2. 配置文件格式正确性
3. 网络连接和端口可用性
4. 系统资源使用情况

此多设备SECS/GEM系统为生产环境设计，支持工业4.0智能制造需求。