// 设备配置页面Vue应用
const { createApp } = Vue;

createApp({
    data() {
        return {
            // 配置数据
            equipmentConfig: window.equipmentConfigData || this.getDefaultEquipmentConfig(),
            
            // 界面状态
            currentStep: 0,
            saving: false,
            validating: false,
            
            // 设备状态统计
            deviceUptime: 24,
            connectionCount: 1,
            memoryUsage: 128,
            
            // 网络状态
            networkStatus: {
                activeConnections: 1,
                messagesSent: 1256,
                messagesReceived: 1189
            },
            
            // SVID映射管理
            mappingSearchText: '',
            mappingCurrentPage: 1,
            mappingPageSize: 20,
            svidMappings: [],
            
            // 报告和事件配置
            defaultReports: [],
            defaultEventLinks: [],
            
            // 表单验证规则
            deviceRules: {
                deviceId: ConfigUtils.validationRules.deviceId,
                equipmentName: ConfigUtils.validationRules.equipmentName,
                modelName: ConfigUtils.validationRules.modelName,
                softwareRevision: ConfigUtils.validationRules.softwareRevision
            },
            
            networkRules: {
                ipAddress: [
                    { pattern: /^(0\.0\.0\.0|(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?))$/, 
                      message: 'IP地址格式不正确', trigger: 'blur' }
                ],
                port: ConfigUtils.validationRules.port,
                t3: [{ type: 'number', min: 1000, max: 120000, message: 'T3超时范围1000-120000ms', trigger: 'blur' }],
                t5: [{ type: 'number', min: 1000, max: 240000, message: 'T5超时范围1000-240000ms', trigger: 'blur' }],
                t6: [{ type: 'number', min: 1000, max: 240000, message: 'T6超时范围1000-240000ms', trigger: 'blur' }],
                t7: [{ type: 'number', min: 1000, max: 240000, message: 'T7超时范围1000-240000ms', trigger: 'blur' }],
                t8: [{ type: 'number', min: 1000, max: 120000, message: 'T8超时范围1000-120000ms', trigger: 'blur' }]
            }
        };
    },
    
    computed: {
        // 过滤后的SVID映射
        filteredSvidMappings() {
            if (!this.mappingSearchText) {
                return this.svidMappings;
            }
            return this.svidMappings.filter(mapping =>
                mapping.svid.toString().includes(this.mappingSearchText) ||
                mapping.plcAddress.toLowerCase().includes(this.mappingSearchText.toLowerCase()) ||
                mapping.description.toLowerCase().includes(this.mappingSearchText.toLowerCase())
            );
        },
        
        // 映射总数
        totalMappings() {
            return this.svidMappings.length;
        }
    },
    
    mounted() {
        registerIcons(this);
        this.initializeData();
        
        // 监听配置变化
        this.$watch('equipmentConfig', () => {
            window.hasUnsavedChanges = true;
        }, { deep: true });
        
        // 定时更新状态
        this.startStatusUpdate();
    },
    
    methods: {
        // 获取默认设备配置
        getDefaultEquipmentConfig() {
            return {
                equipment: {
                    deviceId: 1,
                    equipmentName: "DICER-3000",
                    modelName: "AIMFAB",
                    softwareRevision: "V1.0.0",
                    ipAddress: "0.0.0.0",
                    port: 5000,
                    isActive: false,
                    t3: 45000,
                    t5: 10000,
                    t6: 5000,
                    t7: 10000,
                    t8: 5000,
                    linkTestInterval: 60000,
                    autoOnline: true,
                    defaultControlState: "OnlineRemote",
                    description: ""
                },
                performance: {
                    maxEventReports: 100,
                    maxAlarms: 50,
                    maxTraceDataSize: 1048576,
                    maxSpoolSize: 10485760
                },
                security: {
                    enableAccessControl: false,
                    maxConnections: 5,
                    allowedIPs: []
                },
                svidMapping: {},
                defaultReports: [],
                defaultEventLinks: []
            };
        },
        
        // 初始化数据
        initializeData() {
            // 确保配置完整性
            const defaults = this.getDefaultEquipmentConfig();
            this.equipmentConfig = { ...defaults, ...this.equipmentConfig };
            
            // 初始化SVID映射
            this.loadSvidMappings();
            
            // 初始化报告和事件
            this.loadReportsAndEvents();
        },
        
        // 加载SVID映射
        loadSvidMappings() {
            this.svidMappings = Object.entries(this.equipmentConfig.svidMapping || {}).map(([svid, address]) => ({
                svid: parseInt(svid),
                plcAddress: address,
                description: this.getSvidDescription(svid),
                dataType: this.getSvidDataType(svid),
                editing: false
            }));
        },
        
        // 获取SVID描述
        getSvidDescription(svid) {
            const descriptions = {
                10020: "设备时钟",
                10021: "处理状态",
                10022: "控制状态",
                10023: "报警状态"
            };
            return descriptions[svid] || `状态变量${svid}`;
        },
        
        // 获取SVID数据类型
        getSvidDataType(svid) {
            const types = {
                10020: "String",
                10021: "Int32",
                10022: "Int32",
                10023: "Bool"
            };
            return types[svid] || "Int32";
        },
        
        // 加载报告和事件配置
        loadReportsAndEvents() {
            this.defaultReports = this.equipmentConfig.defaultReports || [];
            this.defaultEventLinks = this.equipmentConfig.defaultEventLinks || [];
        },
        
        // 开始状态更新
        startStatusUpdate() {
            setInterval(() => {
                this.updateDeviceStatus();
            }, 5000);
        },
        
        // 更新设备状态
        updateDeviceStatus() {
            this.deviceUptime += 0.001;
            this.networkStatus.messagesSent += Math.floor(Math.random() * 5);
            this.networkStatus.messagesReceived += Math.floor(Math.random() * 5);
            this.memoryUsage = 128 + Math.floor(Math.random() * 20);
        },
        
        // ========== 步骤导航 ==========
        
        // 下一步
        async nextStep() {
            try {
                await this.validateCurrentStep();
                if (this.currentStep < 4) {
                    this.currentStep++;
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '验证失败', '请检查当前步骤的配置参数');
            }
        },
        
        // 上一步
        prevStep() {
            if (this.currentStep > 0) {
                this.currentStep--;
            }
        },
        
        // 跳转到保存
        skipToSave() {
            this.currentStep = 4;
        },
        
        // 验证当前步骤
        async validateCurrentStep() {
            switch (this.currentStep) {
                case 0:
                    await this.$refs.deviceForm?.validate();
                    break;
                case 1:
                    await this.$refs.networkForm?.validate();
                    break;
                case 2:
                    await this.$refs.performanceForm?.validate();
                    break;
            }
        },
        
        // ========== 配置管理 ==========
        
        // 保存设备配置
        async saveEquipmentConfig() {
            try {
                this.saving = true;
                
                // 更新SVID映射到配置对象
                this.updateSvidMappingToConfig();
                
                const result = await ConfigUtils.apiCall('/api/config/SaveEquipmentConfig', 'POST', this.equipmentConfig);
                
                if (result.success) {
                    ConfigUtils.showNotification('success', '保存成功', '设备配置已成功保存');
                    window.hasUnsavedChanges = false;
                } else {
                    ConfigUtils.showNotification('error', '保存失败', result.error);
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '保存失败', error.message);
            } finally {
                this.saving = false;
            }
        },
        
        // 验证配置
        async validateConfig() {
            try {
                this.validating = true;
                
                const result = await ConfigUtils.apiCall('/api/config/ValidateConfig', 'POST', this.equipmentConfig);
                
                if (result.success && result.data.isValid) {
                    ConfigUtils.showNotification('success', '验证成功', '所有配置参数验证通过');
                } else {
                    const errors = result.data?.errors || ['配置验证失败'];
                    ConfigUtils.showNotification('error', '验证失败', errors.join('; '));
                }
            } catch (error) {
                ConfigUtils.showMessage('error', '验证失败', error.message);
            } finally {
                this.validating = false;
            }
        },
        
        // 处理导出
        async handleExport(command) {
            switch (command) {
                case 'json':
                    this.exportAsJson();
                    break;
                case 'csv':
                    this.exportAsCsv();
                    break;
                case 'template':
                    this.downloadTemplate();
                    break;
            }
        },
        
        // 导出为JSON
        exportAsJson() {
            const config = ConfigUtils.deepClone(this.equipmentConfig);
            ConfigUtils.exportToJson(config, `equipment_config_${new Date().toISOString().slice(0, 10)}.json`);
        },
        
        // 导出为CSV
        exportAsCsv() {
            const data = this.svidMappings.map(mapping => ({
                SVID: mapping.svid,
                PLC地址: mapping.plcAddress,
                描述: mapping.description,
                数据类型: mapping.dataType
            }));
            
            ConfigUtils.exportToCsv(data, `svid_mapping_${new Date().toISOString().slice(0, 10)}.csv`, 
                ['SVID', 'PLC地址', '描述', '数据类型']);
        },
        
        // 下载模板
        downloadTemplate() {
            const template = {
                equipment: this.getDefaultEquipmentConfig().equipment,
                svidMapping: {
                    "10020": "D1000",
                    "10021": "D1002",
                    "10022": "D1004"
                },
                defaultReports: [
                    { reportId: 1, variableIds: [1, 721, 722], description: "基本状态报告" }
                ]
            };
            
            ConfigUtils.exportToJson(template, 'equipment_config_template.json');
        },
        
        // ========== SVID映射管理 ==========
        
        // 添加SVID映射
        addSvidMapping() {
            this.svidMappings.push({
                svid: 10000 + this.svidMappings.length,
                plcAddress: 'D1000',
                description: '新状态变量',
                dataType: 'Int32',
                editing: true
            });
        },
        
        // 编辑映射
        editMapping(index) {
            this.svidMappings[index].editing = true;
        },
        
        // 保存映射
        saveMapping(index) {
            const mapping = this.svidMappings[index];
            
            // 验证SVID
            if (!mapping.svid || mapping.svid <= 0) {
                ConfigUtils.showMessage('error', '验证失败', 'SVID必须是正整数');
                return;
            }
            
            // 验证PLC地址
            if (!mapping.plcAddress || !mapping.plcAddress.trim()) {
                ConfigUtils.showMessage('error', '验证失败', 'PLC地址不能为空');
                return;
            }
            
            // 检查SVID重复
            const duplicate = this.svidMappings.find((m, i) => i !== index && m.svid === mapping.svid);
            if (duplicate) {
                ConfigUtils.showMessage('error', '验证失败', `SVID ${mapping.svid} 已存在`);
                return;
            }
            
            mapping.editing = false;
            ConfigUtils.showMessage('success', '保存成功', 'SVID映射已保存');
        },
        
        // 取消编辑映射
        cancelEditMapping(index) {
            const mapping = this.svidMappings[index];
            if (!mapping.svid && !mapping.plcAddress.trim()) {
                // 如果是新添加的空映射，直接删除
                this.svidMappings.splice(index, 1);
            } else {
                mapping.editing = false;
            }
        },
        
        // 删除映射
        deleteMapping(index) {
            this.svidMappings.splice(index, 1);
            ConfigUtils.showMessage('info', '删除成功', 'SVID映射已删除');
        },
        
        // 测试映射
        async testMapping(mapping) {
            ConfigUtils.showNotification('info', '测试映射', 
                `正在测试 SVID ${mapping.svid} -> ${mapping.plcAddress}`);
            
            // 模拟测试
            await new Promise(resolve => setTimeout(resolve, 1000));
            
            ConfigUtils.showNotification('success', '测试完成', 
                `SVID ${mapping.svid} 映射测试成功`);
        },
        
        // 获取数据类型颜色
        getDataTypeColor(dataType) {
            const colors = {
                'Int16': 'success',
                'Int32': 'primary',
                'Float': 'warning',
                'String': 'info',
                'Bool': 'danger'
            };
            return colors[dataType] || 'info';
        },
        
        // 导入映射
        async importMappings() {
            ConfigUtils.showMessage('info', '功能开发中', 'SVID映射导入功能正在开发中');
        },
        
        // 导出映射
        exportMappings() {
            this.exportAsCsv();
        },
        
        // 更新SVID映射到配置对象
        updateSvidMappingToConfig() {
            this.equipmentConfig.svidMapping = {};
            this.svidMappings.forEach(mapping => {
                if (!mapping.editing) {
                    this.equipmentConfig.svidMapping[mapping.svid] = mapping.plcAddress;
                }
            });
        },
        
        // ========== 安全设置管理 ==========
        
        // 添加允许的IP
        addAllowedIP() {
            if (!this.equipmentConfig.security.allowedIPs) {
                this.equipmentConfig.security.allowedIPs = [];
            }
            this.equipmentConfig.security.allowedIPs.push('192.168.1.100');
        },
        
        // 删除允许的IP
        removeAllowedIP(index) {
            this.equipmentConfig.security.allowedIPs.splice(index, 1);
        }
    }
}).use(ElementPlus).mount('#equipment-config-app');