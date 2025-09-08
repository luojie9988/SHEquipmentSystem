// PLC配置页面Vue应用
const { createApp } = Vue;

createApp({
    data() {
        return {
            // 配置数据
            plcConfig: window.plcConfigData || this.getDefaultPLCConfig(),
            
            // 界面状态
            activeTab: 'basic',
            saving: false,
            testing: false,
            isConnected: window.connectionStatus || false,
            lastTestTime: null,
            
            // 连接状态
            connectionStatus: {
                title: '连接状态',
                type: 'info',
                description: '未连接'
            },
            
            // 数据块搜索和分页
            datablockSearchText: '',
            datablockCurrentPage: 1,
            datablockPageSize: 10,
            
            // 表单验证规则
            basicRules: {
                ipAddress: ConfigUtils.validationRules.ipAddress,
                port: ConfigUtils.validationRules.port,
                networkNumber: [
                    { type: 'number', min: 0, max: 255, message: '网络号范围0-255', trigger: 'blur' }
                ],
                stationNumber: [
                    { type: 'number', min: 0, max: 255, message: '站号范围0-255', trigger: 'blur' }
                ]
            },
            
            timeoutRules: {
                connectTimeout: [
                    { type: 'number', min: 1000, max: 30000, message: '连接超时范围1000-30000ms', trigger: 'blur' }
                ],
                receiveTimeout: [
                    { type: 'number', min: 1000, max: 30000, message: '接收超时范围1000-30000ms', trigger: 'blur' }
                ],
                pollingInterval: [
                    { type: 'number', min: 100, max: 10000, message: '轮询间隔范围100-10000ms', trigger: 'blur' }
                ]
            }
        };
    },
    
    computed: {
        // 过滤后的数据块列表
        filteredDataBlocks() {
            if (!this.datablockSearchText) {
                return this.plcConfig.dataBlocks;
            }
            return this.plcConfig.dataBlocks.filter(db =>
                db.name.toLowerCase().includes(this.datablockSearchText.toLowerCase()) ||
                db.startAddress.toLowerCase().includes(this.datablockSearchText.toLowerCase())
            );
        },
        
        // 配置预览文本
        configPreviewText() {
            return JSON.stringify(this.plcConfig, null, 2);
        }
    },
    
    mounted() {
        registerIcons(this);
        this.updateConnectionStatus();
        this.initializeAdvancedConfig();
        
        // 监听配置变化
        this.$watch('plcConfig', () => {
            window.hasUnsavedChanges = true;
        }, { deep: true });
    },
    
    methods: {
        // 获取默认PLC配置
        getDefaultPLCConfig() {
            return {
                ipAddress: '192.168.3.100',
                port: 5007,
                networkNumber: 0,
                stationNumber: 0,
                connectTimeout: 5000,
                receiveTimeout: 3000,
                pollingInterval: 2000,
                maxRetryCount: 3,
                enableAutoReconnect: true,
                reconnectInterval: 5000,
                dataBlocks: [],
                // 高级配置
                enableHeartbeat: true,
                enableDataCache: false,
                cacheSize: 256,
                logLevel: 'Information',
                enableCommLog: false,
                enablePerformanceStats: true,
                statsPeriod: 5,
                errorHandling: 'retry',
                maxErrorCount: 10
            };
        },
        
        // 初始化高级配置
        initializeAdvancedConfig() {
            const defaults = this.getDefaultPLCConfig();
            
            // 确保高级配置项存在
            Object.keys(defaults).forEach(key => {
                if (!(key in this.plcConfig)) {
                    this.plcConfig[key] = defaults[key];
                }
            });
        },
        
        // 保存PLC配置
        async savePLCConfig() {
            try {
                // 验证当前标签页的表单
                await this.validateCurrentTab();
                
                this.saving = true;
                
                const result = await ConfigUtils.apiCall('/api/config/SavePLCConfig', 'POST', this.plcConfig);
                
                if (result.success) {
                    ConfigUtils.showMessage('success', '成功', 'PLC配置保存成功');
                    window.hasUnsavedChanges = false;
                } else {
                    ConfigUtils.showMessage('error', '错误', result.error);
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '验证失败', '请检查输入的配置参数');
            } finally {
                this.saving = false;
            }
        },
        
        // 验证当前标签页
        async validateCurrentTab() {
            switch (this.activeTab) {
                case 'basic':
                    await this.$refs.basicForm?.validate();
                    break;
                case 'timeout':
                    await this.$refs.timeoutForm?.validate();
                    break;
                case 'advanced':
                    await this.$refs.advancedForm?.validate();
                    break;
            }
        },
        
        // 测试PLC连接
        async testConnection() {
            this.testing = true;
            
            try {
                const result = await ConfigUtils.apiCall('/api/config/TestPLCConnection', 'POST', this.plcConfig);
                
                this.lastTestTime = new Date();
                
                if (result.success && result.data.connected) {
                    this.isConnected = true;
                    this.updateConnectionStatus();
                    ConfigUtils.showNotification('success', '连接成功', 'PLC连接测试成功');
                } else {
                    this.isConnected = false;
                    this.updateConnectionStatus();
                    ConfigUtils.showNotification('error', '连接失败', 
                        result.data?.error || '无法连接到PLC，请检查网络和配置');
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '测试失败', error);
            } finally {
                this.testing = false;
            }
        },
        
        // 重置配置
        async resetConfig() {
            const confirmed = await ConfigUtils.showConfirm('重置配置', 
                '确定要重置所有PLC配置吗？此操作不可撤销。');
            if (confirmed) {
                this.plcConfig = this.getDefaultPLCConfig();
                ConfigUtils.showMessage('info', '配置重置', 'PLC配置已重置为默认值');
                window.hasUnsavedChanges = true;
            }
        },
        
        // 更新连接状态显示
        updateConnectionStatus() {
            if (this.isConnected) {
                this.connectionStatus = {
                    title: 'PLC连接正常',
                    type: 'success',
                    description: `已连接到 ${this.plcConfig.ipAddress}:${this.plcConfig.port}`
                };
            } else {
                this.connectionStatus = {
                    title: 'PLC未连接',
                    type: 'warning',
                    description: '请检查PLC配置并测试连接'
                };
            }
        },
        
        // 格式化时间
        formatTime(date) {
            return ConfigUtils.formatTime(date);
        },
        
        // ========== 数据块管理方法 ==========
        
        // 添加数据块
        addDataBlock() {
            const newDataBlock = {
                name: `DataBlock${this.plcConfig.dataBlocks.length + 1}`,
                startAddress: 'D100',
                length: 100,
                updateInterval: 1000,
                editing: true
            };
            this.plcConfig.dataBlocks.push(newDataBlock);
        },
        
        // 编辑数据块
        editDataBlock(index) {
            this.plcConfig.dataBlocks[index].editing = true;
        },
        
        // 保存数据块
        saveDataBlock(index) {
            const dataBlock = this.plcConfig.dataBlocks[index];
            
            // 验证数据块配置
            if (!dataBlock.name || !dataBlock.startAddress) {
                ConfigUtils.showMessage('error', '验证失败', '数据块名称和起始地址不能为空');
                return;
            }
            
            if (dataBlock.length <= 0 || dataBlock.length > 1000) {
                ConfigUtils.showMessage('error', '验证失败', '数据块长度范围1-1000');
                return;
            }
            
            if (dataBlock.updateInterval < 100 || dataBlock.updateInterval > 10000) {
                ConfigUtils.showMessage('error', '验证失败', '更新间隔范围100-10000ms');
                return;
            }
            
            dataBlock.editing = false;
            ConfigUtils.showMessage('success', '保存成功', '数据块配置已保存');
        },
        
        // 取消编辑
        cancelEdit(index) {
            const dataBlock = this.plcConfig.dataBlocks[index];
            if (dataBlock.name === `DataBlock${this.plcConfig.dataBlocks.length}` && !dataBlock.name.trim()) {
                // 如果是新添加且未保存的数据块，直接删除
                this.plcConfig.dataBlocks.splice(index, 1);
            } else {
                dataBlock.editing = false;
            }
        },
        
        // 删除数据块
        async deleteDataBlock(index) {
            this.plcConfig.dataBlocks.splice(index, 1);
            ConfigUtils.showMessage('info', '删除成功', '数据块已删除');
        },
        
        // 测试数据块
        async testDataBlock(dataBlock) {
            ConfigUtils.showNotification('info', '测试数据块', 
                `正在测试数据块: ${dataBlock.name} (${dataBlock.startAddress})`);
            
            // 模拟测试逻辑
            await new Promise(resolve => setTimeout(resolve, 1000));
            
            ConfigUtils.showNotification('success', '测试完成', 
                `数据块 ${dataBlock.name} 测试成功`);
        },
        
        // 获取数据块状态
        getDataBlockStatus(dataBlock) {
            if (dataBlock.editing) return '编辑中';
            if (!this.isConnected) return '未连接';
            return Math.random() > 0.3 ? '正常' : '异常';
        },
        
        // 获取数据块状态类型
        getDataBlockStatusType(dataBlock) {
            if (dataBlock.editing) return 'warning';
            if (!this.isConnected) return 'info';
            return Math.random() > 0.3 ? 'success' : 'danger';
        },
        
        // 导入数据块
        async importDataBlocks() {
            ConfigUtils.showMessage('info', '功能开发中', '数据块导入功能正在开发中');
        },
        
        // 导出数据块
        exportDataBlocks() {
            const data = this.plcConfig.dataBlocks.map(db => ({
                name: db.name,
                startAddress: db.startAddress,
                length: db.length,
                updateInterval: db.updateInterval
            }));
            
            ConfigUtils.exportToJson(data, `plc_datablocks_${new Date().toISOString().slice(0, 10)}.json`);
        },
        
        // ========== 高级配置方法 ==========
        
        // 导出PLC配置
        exportPLCConfig() {
            const config = ConfigUtils.deepClone(this.plcConfig);
            // 移除编辑状态
            config.dataBlocks = config.dataBlocks.map(db => {
                const { editing, ...rest } = db;
                return rest;
            });
            
            ConfigUtils.exportToJson(config, `plc_config_${new Date().toISOString().slice(0, 10)}.json`);
        },
        
        // 导入PLC配置
        async importPLCConfig(file) {
            try {
                const config = await ConfigUtils.readJsonFile(file);
                
                // 验证配置格式
                if (!config.ipAddress || !config.port) {
                    throw new Error('配置文件格式错误');
                }
                
                const confirmed = await ConfigUtils.showConfirm('导入配置', 
                    '确定要导入配置吗？当前配置将被覆盖。');
                
                if (confirmed) {
                    this.plcConfig = { ...this.getDefaultPLCConfig(), ...config };
                    this.initializeAdvancedConfig();
                    ConfigUtils.showMessage('success', '导入成功', 'PLC配置已导入');
                    window.hasUnsavedChanges = true;
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '导入失败', error.message);
            }
            
            return false; // 阻止上传
        },
        
        // 恢复默认配置
        async resetToDefaults() {
            const confirmed = await ConfigUtils.showConfirm('恢复默认', 
                '确定要恢复所有默认配置吗？当前配置将丢失。', 'warning');
            
            if (confirmed) {
                this.plcConfig = this.getDefaultPLCConfig();
                ConfigUtils.showMessage('success', '恢复完成', '已恢复为默认配置');
                window.hasUnsavedChanges = true;
            }
        }
    }
}).use(ElementPlus).mount('#plc-config-app');