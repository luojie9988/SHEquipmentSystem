// 通用配置功能
window.ConfigUtils = {
    // 表单验证规则
    validationRules: {
        ipAddress: [
            { required: true, message: 'IP地址不能为空', trigger: 'blur' },
            { 
                pattern: /^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$/, 
                message: 'IP地址格式不正确', 
                trigger: 'blur' 
            }
        ],
        port: [
            { required: true, message: '端口不能为空', trigger: 'blur' },
            { type: 'number', min: 1, max: 65535, message: '端口范围1-65535', trigger: 'blur' }
        ],
        deviceId: [
            { required: true, message: '设备ID不能为空', trigger: 'blur' },
            { type: 'number', min: 1, max: 9999, message: '设备ID范围1-9999', trigger: 'blur' }
        ],
        equipmentName: [
            { required: true, message: '设备名称不能为空', trigger: 'blur' },
            { min: 1, max: 20, message: '设备名称长度1-20个字符', trigger: 'blur' }
        ],
        modelName: [
            { required: true, message: '型号名称不能为空', trigger: 'blur' },
            { min: 1, max: 20, message: '型号名称长度1-20个字符', trigger: 'blur' }
        ],
        softwareRevision: [
            { required: true, message: '软件版本不能为空', trigger: 'blur' },
            { pattern: /^v?\d+\.\d+\.\d+$/i, message: '版本号格式如: V1.0.0', trigger: 'blur' }
        ]
    },

    // API调用封装
    async apiCall(url, method = 'GET', data = null) {
        try {
            const config = {
                method: method,
                url: url,
                headers: {
                    'Content-Type': 'application/json',
                    'X-Requested-With': 'XMLHttpRequest',
                    'RequestVerificationToken': this.getAntiForgeryToken()
                }
            };
            
            if (data) {
                config.data = JSON.stringify(data);
            }
            
            const response = await axios(config);
            return { success: true, data: response.data };
        } catch (error) {
            console.error('API调用失败:', error);
            return { 
                success: false, 
                error: error.response?.data?.message || error.message,
                details: error.response?.data
            };
        }
    },

    // 获取防伪令牌
    getAntiForgeryToken() {
        const token = document.querySelector('input[name="__RequestVerificationToken"]');
        return token ? token.value : '';
    },

    // 通知消息
    showMessage(type, title, message) {
        ElementPlus.ElMessage({
            type: type,
            title: title,
            message: message,
            duration: type === 'error' ? 5000 : 3000,
            showClose: true
        });
    },

    // 通知框
    showNotification(type, title, message, position = 'top-right') {
        ElementPlus.ElNotification({
            type: type,
            title: title,
            message: message,
            position: position,
            duration: type === 'error' ? 8000 : 4000
        });
    },

    // 确认对话框
    async showConfirm(title, message, type = 'warning') {
        try {
            await ElementPlus.ElMessageBox.confirm(message, title, {
                confirmButtonText: '确定',
                cancelButtonText: '取消',
                type: type,
                center: true
            });
            return true;
        } catch {
            return false;
        }
    },

    // 输入对话框
    async showPrompt(title, message, inputValue = '') {
        try {
            const result = await ElementPlus.ElMessageBox.prompt(message, title, {
                confirmButtonText: '确定',
                cancelButtonText: '取消',
                inputValue: inputValue
            });
            return result.value;
        } catch {
            return null;
        }
    },

    // 格式化时间
    formatTime(date) {
        if (!date) return '';
        const d = new Date(date);
        return d.toLocaleString('zh-CN', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    },

    // 格式化文件大小
    formatFileSize(bytes) {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    },

    // 深拷贝对象
    deepClone(obj) {
        if (obj === null || typeof obj !== 'object') return obj;
        if (obj instanceof Date) return new Date(obj.getTime());
        if (obj instanceof Array) return obj.map(item => this.deepClone(item));
        if (typeof obj === 'object') {
            const clonedObj = {};
            for (let key in obj) {
                if (obj.hasOwnProperty(key)) {
                    clonedObj[key] = this.deepClone(obj[key]);
                }
            }
            return clonedObj;
        }
    },

    // 防抖函数
    debounce(func, wait) {
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    },

    // 节流函数
    throttle(func, limit) {
        let inThrottle;
        return function(...args) {
            if (!inThrottle) {
                func.apply(this, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        };
    },

    // 验证IP地址
    isValidIP(ip) {
        const ipRegex = /^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$/;
        return ipRegex.test(ip);
    },

    // 导出配置为JSON
    exportToJson(data, filename) {
        const jsonStr = JSON.stringify(data, null, 2);
        const blob = new Blob([jsonStr], { type: 'application/json' });
        this.downloadFile(blob, filename);
    },

    // 导出配置为CSV
    exportToCsv(data, filename, headers) {
        let csvContent = '';
        
        // 添加标题行
        if (headers) {
            csvContent += headers.join(',') + '\n';
        }
        
        // 添加数据行
        data.forEach(row => {
            const values = Object.values(row).map(value => 
                typeof value === 'string' ? `"${value}"` : value
            );
            csvContent += values.join(',') + '\n';
        });
        
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        this.downloadFile(blob, filename);
    },

    // 下载文件
    downloadFile(blob, filename) {
        const link = document.createElement('a');
        if (link.download !== undefined) {
            const url = URL.createObjectURL(blob);
            link.setAttribute('href', url);
            link.setAttribute('download', filename);
            link.style.visibility = 'hidden';
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            URL.revokeObjectURL(url);
        }
    },

    // 从文件读取JSON
    async readJsonFile(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = (e) => {
                try {
                    const json = JSON.parse(e.target.result);
                    resolve(json);
                } catch (error) {
                    reject(new Error('JSON文件格式错误'));
                }
            };
            reader.onerror = () => reject(new Error('文件读取失败'));
            reader.readAsText(file);
        });
    },

    // 本地存储管理
    storage: {
        set(key, value) {
            try {
                localStorage.setItem(key, JSON.stringify(value));
                return true;
            } catch (error) {
                console.error('存储失败:', error);
                return false;
            }
        },

        get(key, defaultValue = null) {
            try {
                const item = localStorage.getItem(key);
                return item ? JSON.parse(item) : defaultValue;
            } catch (error) {
                console.error('读取存储失败:', error);
                return defaultValue;
            }
        },

        remove(key) {
            try {
                localStorage.removeItem(key);
                return true;
            } catch (error) {
                console.error('删除存储失败:', error);
                return false;
            }
        },

        clear() {
            try {
                localStorage.clear();
                return true;
            } catch (error) {
                console.error('清空存储失败:', error);
                return false;
            }
        }
    },

    // 配置变更检测
    configChangeDetector: {
        originalConfig: null,
        
        init(config) {
            this.originalConfig = ConfigUtils.deepClone(config);
        },
        
        hasChanges(currentConfig) {
            return JSON.stringify(this.originalConfig) !== JSON.stringify(currentConfig);
        },
        
        getChanges(currentConfig) {
            const changes = {};
            this._compareObjects(this.originalConfig, currentConfig, '', changes);
            return changes;
        },
        
        _compareObjects(original, current, path, changes) {
            for (let key in current) {
                const currentPath = path ? `${path}.${key}` : key;
                
                if (typeof current[key] === 'object' && current[key] !== null) {
                    if (typeof original[key] === 'object' && original[key] !== null) {
                        this._compareObjects(original[key], current[key], currentPath, changes);
                    } else {
                        changes[currentPath] = { old: original[key], new: current[key] };
                    }
                } else if (original[key] !== current[key]) {
                    changes[currentPath] = { old: original[key], new: current[key] };
                }
            }
        }
    }
};

// 页面离开确认
window.addEventListener('beforeunload', function(event) {
    if (window.hasUnsavedChanges) {
        event.preventDefault();
        event.returnValue = '您有未保存的配置更改，确定要离开吗？';
    }
});

// 初始化全局变量
window.hasUnsavedChanges = false;