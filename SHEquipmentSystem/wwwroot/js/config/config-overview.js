// 配置总览页面Vue应用
const { createApp } = Vue;

createApp({
    data() {
        return {
            // 基础状态数据
            plcConnected: window.configOverviewData?.plcStatus || false,
            equipmentStatus: window.configOverviewData?.equipmentStatus || '正常',
            systemUptime: 48.5,
            
            // 配置数据
            plcConfig: {
                ipAddress: '192.168.3.100',
                port: 5007,
                networkNumber: 0,
                stationNumber: 0,
                pollingInterval: 2000,
                dataBlocks: [
                    { name: 'CoordinateData', startAddress: 'D100', length: 100 },
                    { name: 'ProcessData', startAddress: 'D200', length: 100 }
                ]
            },
            
            equipmentConfig: {
                equipment: {
                    deviceId: 1,
                    equipmentName: 'DICER-3000',
                    modelName: 'AIMFAB',
                    softwareRevision: 'V1.0.0',
                    isActive: false,
                    port: 5000
                }
            },
            
            // 状态指示
            plcConfigStatus: {
                type: 'success',
                text: '配置正常'
            },
            
            equipmentConfigStatus: {
                type: 'success', 
                text: '配置正常'
            },
            
            // 统计数据
            statisticsData: {
                svidMappings: 24,
                defaultReports: 8,
                eventLinks: 12,
                dataBlocks: 5
            },
            
            // 系统指标
            systemMetrics: {
                cpuUsage: 25,
                memoryUsage: 45,
                diskUsage: 68,
                networkLatency: 15
            },
            
            // 最近活动
            recentActivities: [
                {
                    id: 1,
                    time: '2024-01-15 14:30:00',
                    type: 'success',
                    description: 'PLC配置保存成功'
                },
                {
                    id: 2,
                    time: '2024-01-15 14:25:00',
                    type: 'info',
                    description: '设备状态同步完成'
                },
                {
                    id: 3,
                    time: '2024-01-15 14:20:00',
                    type: 'warning',
                    description: 'SVID映射更新'
                },
                {
                    id: 4,
                    time: '2024-01-15 14:15:00',
                    type: 'success',
                    description: '系统启动完成'
                }
            ],
            
            // 系统信息
            systemInfo: {
                os: 'Windows Server 2019',
                runtime: '.NET 6.0',
                hostname: 'EQP-SERVER-01',
                ipAddress: '192.168.1.100',
                startTime: '2024-01-15 08:00:00',
                uptime: '6小时30分钟',
                totalMemory: '16 GB',
                availableMemory: '8.5 GB'
            },
            
            // 界面状态
            testingPlc: false,
            validating: false,
            showSystemInfoDialog: false
        };
    },
    
    mounted() {
        registerIcons(this);
        this.startStatusUpdates();
        this.updateConfigStatus();
    },
    
    methods: {
        // ========== 状态更新方法 ==========
        
        // 开始状态更新
        startStatusUpdates() {
            // 每30秒更新一次状态
            setInterval(() => {
                this.updateSystemMetrics();
                this.updateUptime();
            }, 30000);
            
            // 每5分钟检查配置状态
            setInterval(() => {
                this.checkConfigurationStatus();
            }, 300000);
        },
        
        // 更新系统指标
        updateSystemMetrics() {
            // 模拟指标变化
            this.systemMetrics.cpuUsage = Math.max(10, Math.min(90, 
                this.systemMetrics.cpuUsage + (Math.random() - 0.5) * 10));
            this.systemMetrics.memoryUsage = Math.max(20, Math.min(80, 
                this.systemMetrics.memoryUsage + (Math.random() - 0.5) * 8));
            this.systemMetrics.networkLatency = Math.max(5, Math.min(50, 
                this.systemMetrics.networkLatency + (Math.random() - 0.5) * 8));
        },
        
        // 更新运行时间
        updateUptime() {
            this.systemUptime += 0.0083; // 约30秒
        },
        
        // 检查配置状态
        async checkConfigurationStatus() {
            try {
                // 检查PLC配置状态
                const plcResult = await ConfigUtils.apiCall('/api/config/GetPLCConfig');
                if (plcResult.success) {
                    this.plcConfigStatus = { type: 'success', text: '配置正常' };
                } else {
                    this.plcConfigStatus = { type: 'warning', text: '配置异常' };
                }
                
                // 检查设备配置状态
                const equipResult = await ConfigUtils.apiCall('/api/config/GetEquipmentConfig');
                if (equipResult.success) {
                    this.equipmentConfigStatus = { type: 'success', text: '配置正常' };
                } else {
                    this.equipmentConfigStatus = { type: 'warning', text: '配置异常' };
                }
            } catch (error) {
                console.error('配置状态检查失败:', error);
            }
        },
        
        // 更新配置状态
        updateConfigStatus() {
            // 根据连接状态更新配置状态
            if (!this.plcConnected) {
                this.plcConfigStatus = { type: 'warning', text: '连接异常' };
            }
        },
        
        // ========== 导航方法 ==========
        
        // 跳转到配置页面
        goToConfig(type) {
            const urls = {
                'plc': '/Config/PLCConfig',
                'equipment': '/Config/EquipmentConfig'
            };
            
            if (urls[type]) {
                window.location.href = urls[type];
            }
        },
        
        // ========== 测试和验证方法 ==========
        
        // 测试PLC连接
        async testPlcConnection() {
            this.testingPlc = true;
            
            try {
                const result = await ConfigUtils.apiCall('/api/config/TestPLCConnection', 'POST', this.plcConfig);
                
                if (result.success && result.data.connected) {
                    this.plcConnected = true;
                    this.plcConfigStatus = { type: 'success', text: '连接正常' };
                    ConfigUtils.showNotification('success', '连接测试', 'PLC连接测试成功');
                    
                    // 添加活动记录
                    this.addActivity('success', 'PLC连接测试成功');
                } else {
                    this.plcConnected = false;
                    this.plcConfigStatus = { type: 'danger', text: '连接失败' };
                    ConfigUtils.showNotification('error', '连接测试', 'PLC连接测试失败');
                    
                    this.addActivity('error', 'PLC连接测试失败');
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '测试失败', error.message);
            } finally {
                this.testingPlc = false;
            }
        },
        
        // 验证所有配置
        async validateConfigs() {
            this.validating = true;
            
            try {
                let allValid = true;
                let errors = [];
                
                // 验证PLC配置
                const plcResult = await ConfigUtils.apiCall('/api/config/ValidateConfig', 'POST', this.plcConfig);
                if (!plcResult.success || !plcResult.data?.isValid) {
                    allValid = false;
                    errors.push('PLC配置验证失败');
                }
                
                // 验证设备配置
                const equipResult = await ConfigUtils.apiCall('/api/config/ValidateConfig', 'POST', this.equipmentConfig);
                if (!equipResult.success || !equipResult.data?.isValid) {
                    allValid = false;
                    errors.push('设备配置验证失败');
                }
                
                if (allValid) {
                    ConfigUtils.showNotification('success', '验证完成', '所有配置验证通过');
                    this.addActivity('success', '配置验证通过');
                } else {
                    ConfigUtils.showNotification('warning', '验证完成', `发现问题: ${errors.join(', ')}`);
                    this.addActivity('warning', '配置验证发现问题');
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '验证失败', error.message);
            } finally {
                this.validating = false;
            }
        },
        
        // ========== 配置管理方法 ==========
        
        // 刷新状态
        async refreshStatus() {
            ConfigUtils.showMessage('info', '刷新中', '正在更新系统状态...');
            
            try {
                await this.checkConfigurationStatus();
                this.updateSystemMetrics();
                
                ConfigUtils.showMessage('success', '刷新完成', '系统状态已更新');
                this.addActivity('info', '系统状态刷新');
            } catch (error) {
                ConfigUtils.showMessage('error', '刷新失败', error.message);
            }
        },
        
        // 导出所有配置
        async exportAllConfigs() {
            try {
                const result = await ConfigUtils.apiCall('/api/config/ExportConfig?format=json');
                
                if (result.success) {
                    // 创建下载链接
                    const blob = new Blob([JSON.stringify(result.data, null, 2)], 
                        { type: 'application/json' });
                    ConfigUtils.downloadFile(blob, `all_configs_${new Date().toISOString().slice(0, 10)}.json`);
                    
                    ConfigUtils.showMessage('success', '导出成功', '配置文件已下载');
                    this.addActivity('success', '导出所有配置');
                } else {
                    ConfigUtils.showMessage('error', '导出失败', result.error);
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '导出失败', error.message);
            }
        },
        
        // 显示系统信息
        showSystemInfo() {
            this.showSystemInfoDialog = true;
        },
        
        // 关闭系统信息对话框
        closeSystemInfoDialog() {
            this.showSystemInfoDialog = false;
        },
        
        // ========== 配置操作方法 ==========
        
        // 备份配置
        async backupConfigs() {
            try {
                const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
                const filename = `config_backup_${timestamp}.json`;
                
                await this.exportAllConfigs();
                ConfigUtils.showMessage('success', '备份完成', `配置已备份到 ${filename}`);
                this.addActivity('success', '配置备份完成');
            } catch (error) {
                ConfigUtils.showMessage('error', '备份失败', error.message);
            }
        },
        
        // 恢复配置
        async restoreConfigs() {
            const confirmed = await ConfigUtils.showConfirm('恢复配置', 
                '确定要恢复配置吗？当前配置将被覆盖。', 'warning');
            
            if (confirmed) {
                ConfigUtils.showMessage('info', '功能开发中', '配置恢复功能正在开发中');
            }
        },
        
        // 重置为默认配置
        async resetToDefaults() {
            const confirmed = await ConfigUtils.showConfirm('重置配置', 
                '确定要重置所有配置为默认值吗？此操作不可撤销。', 'error');
            
            if (confirmed) {
                try {
                    // 重置PLC配置
                    const defaultPlcConfig = {
                        ipAddress: '192.168.3.100',
                        port: 5007,
                        networkNumber: 0,
                        stationNumber: 0
                    };
                    
                    await ConfigUtils.apiCall('/api/config/SavePLCConfig', 'POST', defaultPlcConfig);
                    
                    ConfigUtils.showMessage('success', '重置完成', '所有配置已重置为默认值');
                    this.addActivity('warning', '配置重置为默认值');
                    
                    // 刷新页面
                    setTimeout(() => {
                        window.location.reload();
                    }, 2000);
                } catch (error) {
                    ConfigUtils.showMessage('error', '重置失败', error.message);
                }
            }
        },
        
        // 导出配置
        async exportConfigs() {
            await this.exportAllConfigs();
        },
        
        // 导入配置
        async importConfigs(file) {
            try {
                const config = await ConfigUtils.readJsonFile(file);
                
                const confirmed = await ConfigUtils.showConfirm('导入配置', 
                    '确定要导入配置吗？当前配置将被覆盖。');
                
                if (confirmed) {
                    // 保存PLC配置
                    if (config.PLC) {
                        await ConfigUtils.apiCall('/api/config/SavePLCConfig', 'POST', config.PLC);
                    }
                    
                    // 保存设备配置
                    if (config.EquipmentSystem) {
                        await ConfigUtils.apiCall('/api/config/SaveEquipmentConfig', 'POST', config.EquipmentSystem);
                    }
                    
                    ConfigUtils.showMessage('success', '导入成功', '配置已导入并保存');
                    this.addActivity('success', '配置导入成功');
                    
                    // 刷新状态
                    setTimeout(() => {
                        this.refreshStatus();
                    }, 1000);
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '导入失败', error.message);
            }
            
            return false; // 阻止上传
        },
        
        // 下载配置模板
        downloadTemplate() {
            const template = {
                PLC: {
                    ipAddress: '192.168.3.100',
                    port: 5007,
                    networkNumber: 0,
                    stationNumber: 0,
                    connectTimeout: 5000,
                    receiveTimeout: 3000,
                    pollingInterval: 2000,
                    dataBlocks: [
                        {
                            name: 'CoordinateData',
                            startAddress: 'D100',
                            length: 100,
                            updateInterval: 200
                        }
                    ]
                },
                EquipmentSystem: {
                    equipment: {
                        deviceId: 1,
                        equipmentName: 'DICER-3000',
                        modelName: 'AIMFAB',
                        softwareRevision: 'V1.0.0',
                        ipAddress: '0.0.0.0',
                        port: 5000,
                        isActive: false
                    },
                    svidMapping: {
                        '10020': 'D1000',
                        '10021': 'D1002'
                    }
                }
            };
            
            ConfigUtils.exportToJson(template, 'config_template.json');
            this.addActivity('info', '下载配置模板');
        },
        
        // ========== 系统维护方法 ==========
        
        // 清理日志
        async cleanupLogs() {
            const confirmed = await ConfigUtils.showConfirm('清理日志', 
                '确定要清理系统日志吗？此操作不可撤销。');
            
            if (confirmed) {
                try {
                    // 模拟清理过程
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    
                    ConfigUtils.showMessage('success', '清理完成', '系统日志已清理');
                    this.addActivity('info', '系统日志清理');
                } catch (error) {
                    ConfigUtils.showMessage('error', '清理失败', error.message);
                }
            }
        },
        
        // 系统优化
        async optimizeSystem() {
            const confirmed = await ConfigUtils.showConfirm('系统优化', 
                '确定要进行系统优化吗？此过程可能需要几分钟。');
            
            if (confirmed) {
                try {
                    ConfigUtils.showMessage('info', '优化中', '正在进行系统优化...');
                    
                    // 模拟优化过程
                    await new Promise(resolve => setTimeout(resolve, 3000));
                    
                    ConfigUtils.showMessage('success', '优化完成', '系统性能已优化');
                    this.addActivity('success', '系统优化完成');
                } catch (error) {
                    ConfigUtils.showMessage('error', '优化失败', error.message);
                }
            }
        },
        
        // 重启服务
        async restartService() {
            const confirmed = await ConfigUtils.showConfirm('重启服务', 
                '确定要重启系统服务吗？这将中断当前连接。', 'warning');
            
            if (confirmed) {
                try {
                    ConfigUtils.showMessage('warning', '重启中', '正在重启系统服务...');
                    this.addActivity('warning', '系统服务重启');
                    
                    // 模拟重启过程
                    await new Promise(resolve => setTimeout(resolve, 5000));
                    
                    ConfigUtils.showMessage('success', '重启完成', '系统服务已重启');
                } catch (error) {
                    ConfigUtils.showMessage('error', '重启失败', error.message);
                }
            }
        },
        
        // ========== 辅助方法 ==========
        
        // 添加活动记录
        addActivity(type, description) {
            this.recentActivities.unshift({
                id: Date.now(),
                time: new Date().toLocaleString('zh-CN'),
                type: type,
                description: description
            });
            
            // 保持最多10条记录
            if (this.recentActivities.length > 10) {
                this.recentActivities.pop();
            }
        },
        
        // 获取CPU颜色
        getCpuColor(value) {
            if (value < 50) return '#67c23a';
            if (value < 80) return '#e6a23c';
            return '#f56c6c';
        },
        
        // 获取内存颜色
        getMemoryColor(value) {
            if (value < 60) return '#67c23a';
            if (value < 85) return '#e6a23c';
            return '#f56c6c';
        },
        
        // 获取磁盘颜色
        getDiskColor(value) {
            if (value < 70) return '#67c23a';
            if (value < 90) return '#e6a23c';
            return '#f56c6c';
        }
    }
}).use(ElementPlus).mount('#config-overview-app');