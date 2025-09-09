// 文件路径: src/Common/ProcessProgramManager.cs
// 版本: v1.0.0
// 描述: 工艺程序管理器 - 符合SEMI E30标准

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Secs4Net;
using static Secs4Net.Item;

namespace DiceSystem.Common
{
    /// <summary>
    /// 工艺程序管理器
    /// 管理设备的工艺程序（Process Program）
    /// </summary>
    public class ProcessProgramManager
    {
        private readonly ILogger<ProcessProgramManager> _logger;
        private readonly Dictionary<string, ProcessProgram> _programs;
        private readonly object _lock = new object();
        
        // 存储限制
        private const int MAX_PROGRAMS = 100;
        private const long MAX_TOTAL_SIZE = 100 * 1024 * 1024; // 100MB
        
        public ProcessProgramManager(ILogger<ProcessProgramManager> logger)
        {
            _logger = logger;
            _programs = new Dictionary<string, ProcessProgram>();
        }
        
        /// <summary>
        /// 工艺程序数据模型
        /// </summary>
        public class ProcessProgram
        {
            public string PPID { get; set; }           // 程序ID
            public string Body { get; set; }           // 程序内容
            public long Length { get; set; }           // 程序长度
            public DateTime CreateTime { get; set; }   // 创建时间
            public DateTime ModifyTime { get; set; }   // 修改时间
            public string Version { get; set; }        // 版本号
            public bool IsSelected { get; set; }       // 是否被选中
            public int UsageCount { get; set; }        // 使用次数
            
            // 划裂片设备特定参数
            public DicingParameters Parameters { get; set; }
        }
        
        /// <summary>
        /// 划裂片参数
        /// </summary>
        public class DicingParameters
        {
            // 切割参数
            public double CuttingSpeed { get; set; }      // 切割速度 (mm/s)
            public double SpindleSpeed { get; set; }      // 主轴转速 (rpm)
            public double CuttingDepth { get; set; }      // 切割深度 (mm)
            public double IndexSize { get; set; }         // 步进尺寸 (mm)
            
            // 刀具参数
            public string BladeType { get; set; }         // 刀片型号
            public double BladeThickness { get; set; }    // 刀片厚度 (mm)
            public int BladeLife { get; set; }           // 刀片寿命 (cuts)
            
            // 工作台参数
            public double ChuckVacuum { get; set; }       // 吸盘真空度 (kPa)
            public double CoolingFlow { get; set; }       // 冷却水流量 (L/min)
            
            // 对位参数
            public double AlignmentTolerance { get; set; } // 对位精度 (μm)
            public bool AutoAlignment { get; set; }        // 自动对位
            
            // 切割路径
            public List<CutLine> CutLines { get; set; }   // 切割线列表
        }
        
        /// <summary>
        /// 切割线定义
        /// </summary>
        public class CutLine
        {
            public int LineNumber { get; set; }       // 线号
            public double StartX { get; set; }        // 起点X
            public double StartY { get; set; }        // 起点Y
            public double EndX { get; set; }          // 终点X
            public double EndY { get; set; }          // 终点Y
            public double Angle { get; set; }         // 角度
            public int Channel { get; set; }          // 通道（1或2）
        }
        
        #region S7F1/F2 - Process Program Load Inquire
        
        /// <summary>
        /// 处理S7F1 - 查询是否可以下载程序
        /// </summary>
        public async Task<SecsMessage> HandleS7F1(SecsMessage request)
        {
            try
            {
                // 解析S7F1消息
                // L[2]
                //   A[n] <PPID>
                //   U4   <LENGTH>
                
                var items = request.SecsItem.Items;
                var ppid = items[0].GetString();
                var length = items[1].FirstValue<uint>();
                
                _logger.LogInformation($"[S7F1] 收到程序下载查询: PPID={ppid}, Length={length}");
                
                // 检查是否可以接收程序
                var ppgnt = CheckProgramLoadPermission(ppid, length);
                
                // 构建S7F2响应
                var response = new SecsMessage(7, 2, false)
                {
                    Name = "PP Load Grant",
                    SecsItem = Item.B((byte)ppgnt)
                };
                
                _logger.LogInformation($"[S7F2] 响应程序下载查询: PPGNT={ppgnt}");
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7F1] 处理失败");
                return new SecsMessage(7, 2, false)
                {
                    Name = "PP Load Grant",
                    SecsItem = Item.B(5)
                }; // Will not accept
            }
        }
        
        /// <summary>
        /// 检查程序下载权限
        /// </summary>
        private byte CheckProgramLoadPermission(string ppid, uint length)
        {
            lock (_lock)
            {
                // PPGNT值定义：
                // 0 = OK
                // 1 = Already have
                // 2 = No space
                // 3 = Invalid PPID
                // 4 = Busy
                // 5 = Will not accept
                // 6-127 = Reserved
                
                // 检查PPID格式
                if (string.IsNullOrEmpty(ppid) || ppid.Length > 120)
                {
                    return 3; // Invalid PPID
                }
                
                // 检查是否已存在
                if (_programs.ContainsKey(ppid))
                {
                    return 1; // Already have
                }
                
                // 检查存储空间
                if (_programs.Count >= MAX_PROGRAMS)
                {
                    return 2; // No space (too many programs)
                }
                
                var totalSize = _programs.Values.Sum(p => p.Length) + length;
                if (totalSize > MAX_TOTAL_SIZE)
                {
                    return 2; // No space (size limit)
                }
                
                return 0; // OK
            }
        }
        
        #endregion
        
        #region S7F3/F4 - Process Program Send
        
        /// <summary>
        /// 处理S7F3 - 接收工艺程序
        /// </summary>
        public async Task<SecsMessage> HandleS7F3(SecsMessage request)
        {
            try
            {
                // 解析S7F3消息
                // L[2]
                //   A[n] <PPID>
                //   A[n] <PPBODY>
                
                var items = request.SecsItem.Items;
                var ppid = items[0].GetString();
                var ppbody = items[1].GetString();
                
                _logger.LogInformation($"[S7F3] 收到工艺程序: PPID={ppid}, Size={ppbody.Length}");
                
                // 保存程序
                var ackc7 = await SaveProcessProgram(ppid, ppbody);
                
                // 构建S7F4响应
                var response = new SecsMessage(7, 4, false)
                {
                    Name = "PP Acknowledge",
                    SecsItem = Item.B((byte)ackc7)
                };
                
                _logger.LogInformation($"[S7F4] 程序接收确认: ACKC7={ackc7}");
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7F3] 处理失败");
                return new SecsMessage(7, 4, false)
                {
                    Name = "PP Acknowledge",
                    SecsItem = Item.B(1)
                }; // Error
            }
        }
        
        /// <summary>
        /// 保存工艺程序
        /// </summary>
        private async Task<byte> SaveProcessProgram(string ppid, string ppbody)
        {
            try
            {
                // ACKC7值定义：
                // 0 = Accepted
                // 1 = Permission not granted
                // 2 = Length error
                // 3 = Matrix overflow
                // 4 = PPID not found
                // 5 = Mode unsupported
                // 6 = Data error
                // 7-127 = Reserved
                
                // 解析程序内容
                var program = ParseProcessProgram(ppid, ppbody);
                if (program == null)
                {
                    return 6; // Data error
                }
                
                lock (_lock)
                {
                    // 保存或更新程序
                    if (_programs.ContainsKey(ppid))
                    {
                        program.ModifyTime = DateTime.Now;
                        program.Version = IncrementVersion(_programs[ppid].Version);
                        _programs[ppid] = program;
                        _logger.LogInformation($"更新工艺程序: {ppid} -> v{program.Version}");
                    }
                    else
                    {
                        program.CreateTime = DateTime.Now;
                        program.ModifyTime = DateTime.Now;
                        program.Version = "1.0";
                        _programs.Add(ppid, program);
                        _logger.LogInformation($"新增工艺程序: {ppid} v{program.Version}");
                    }
                }
                
                // 保存到文件系统
                await SaveProgramToFile(program);
                
                return 0; // Accepted
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"保存程序失败: {ppid}");
                return 1; // Permission not granted
            }
        }
        
        /// <summary>
        /// 解析工艺程序内容
        /// </summary>
        private ProcessProgram ParseProcessProgram(string ppid, string ppbody)
        {
            try
            {
                var program = new ProcessProgram
                {
                    PPID = ppid,
                    Body = ppbody,
                    Length = ppbody.Length,
                    Parameters = new DicingParameters
                    {
                        CutLines = new List<CutLine>()
                    }
                };
                
                // 解析程序内容（简化示例）
                var lines = ppbody.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("SPEED="))
                    {
                        program.Parameters.CuttingSpeed = double.Parse(trimmed.Substring(6));
                    }
                    else if (trimmed.StartsWith("SPINDLE="))
                    {
                        program.Parameters.SpindleSpeed = double.Parse(trimmed.Substring(8));
                    }
                    else if (trimmed.StartsWith("DEPTH="))
                    {
                        program.Parameters.CuttingDepth = double.Parse(trimmed.Substring(6));
                    }
                    else if (trimmed.StartsWith("BLADE="))
                    {
                        program.Parameters.BladeType = trimmed.Substring(6);
                    }
                    else if (trimmed.StartsWith("CUT="))
                    {
                        // 解析切割线
                        var cutData = trimmed.Substring(4).Split(',');
                        if (cutData.Length >= 5)
                        {
                            program.Parameters.CutLines.Add(new CutLine
                            {
                                LineNumber = program.Parameters.CutLines.Count + 1,
                                StartX = double.Parse(cutData[0]),
                                StartY = double.Parse(cutData[1]),
                                EndX = double.Parse(cutData[2]),
                                EndY = double.Parse(cutData[3]),
                                Channel = int.Parse(cutData[4])
                            });
                        }
                    }
                }
                
                return program;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析程序内容失败");
                return null;
            }
        }
        
        #endregion
        
        #region S7F5/F6 - Process Program Request
        
        /// <summary>
        /// 处理S7F5 - 程序上传请求
        /// </summary>
        public async Task<SecsMessage> HandleS7F5(SecsMessage request)
        {
            try
            {
                // 解析S7F5消息
                // A[n] <PPID>
                
                var ppid = request.SecsItem.GetString();
                
                _logger.LogInformation($"[S7F5] 收到程序上传请求: PPID={ppid}");
                
                ProcessProgram program = null;
                lock (_lock)
                {
                    _programs.TryGetValue(ppid, out program);
                }
                
                SecsMessage response;
                
                if (program != null)
                {
                    // 构建S7F6响应 - 包含程序
                    response = new SecsMessage(7, 6, false)
                    {
                        Name = "PP Data",
                        SecsItem = Item.L(
                            Item.A(program.PPID),
                            Item.A(program.Body)
                        )
                    };
                    _logger.LogInformation($"[S7F6] 发送程序: {ppid}, Size={program.Length}");
                }
                else
                {
                    // 程序不存在，返回空
                    response = new SecsMessage(7, 6, false)
                    {
                        Name = "PP Data",
                        SecsItem = Item.L(
                            Item.A(ppid),
                            Item.A("")  // 空程序体表示不存在
                        )
                    };
                    _logger.LogWarning($"[S7F6] 程序不存在: {ppid}");
                }
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7F5] 处理失败");
                return new SecsMessage(7, 6, false)
                {
                    Name = "PP Data",
                    SecsItem = Item.L()
                };
            }
        }
        
        #endregion
        
        #region S7F17/F18 - Delete Process Program
        
        /// <summary>
        /// 处理S7F17 - 删除工艺程序
        /// </summary>
        public async Task<SecsMessage> HandleS7F17(SecsMessage request)
        {
            try
            {
                // 解析S7F17消息
                // L[n]
                //   A[n] <PPID1>
                //   A[n] <PPID2>
                //   ...
                
                var ppidList = request.SecsItem.Items
                    .Select(item => item.GetString())
                    .ToList();
                
                _logger.LogInformation($"[S7F17] 收到删除程序请求: {string.Join(", ", ppidList)}");
                
                var ackc7 = await DeleteProcessPrograms(ppidList);
                
                // 构建S7F18响应
                var response = new SecsMessage(7, 18, false)
                {
                    Name = "Delete PP Acknowledge",
                    SecsItem = Item.B((byte)ackc7)
                };
                
                _logger.LogInformation($"[S7F18] 删除程序确认: ACKC7={ackc7}");
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7F17] 处理失败");
                return new SecsMessage(7, 18, false)
                {
                    Name = "Delete PP Acknowledge",
                    SecsItem = Item.B(1)
                };
            }
        }
        
        #endregion
        
        #region S7F19/F20 - Current Process Program Dir
        
        /// <summary>
        /// 处理S7F19 - 请求程序目录
        /// </summary>
        public async Task<SecsMessage> HandleS7F19(SecsMessage request)
        {
            try
            {
                _logger.LogInformation("[S7F19] 收到程序目录请求");
                
                List<string> ppidList;
                lock (_lock)
                {
                    ppidList = _programs.Keys.OrderBy(k => k).ToList();
                }
                
                // 构建S7F20响应
                var items = ppidList.Select(ppid => Item.A(ppid)).ToArray();
                var response = new SecsMessage(7, 20, false)
                {
                    Name = "Current PPID",
                    SecsItem = items.Length > 0 ? Item.L(items) : Item.L()
                };
                
                _logger.LogInformation($"[S7F20] 发送程序目录: {ppidList.Count}个程序");
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S7F19] 处理失败");
                return new SecsMessage(7, 20, false)
                {
                    Name = "Current PPID",
                    SecsItem = Item.L()
                };
            }
        }
        
        #endregion
        
        #region 辅助方法
        
        /// <summary>
        /// 删除工艺程序
        /// </summary>
        private async Task<byte> DeleteProcessPrograms(List<string> ppidList)
        {
            try
            {
                lock (_lock)
                {
                    foreach (var ppid in ppidList)
                    {
                        if (_programs.ContainsKey(ppid))
                        {
                            // 检查是否正在使用
                            if (_programs[ppid].IsSelected)
                            {
                                _logger.LogWarning($"程序 {ppid} 正在使用，无法删除");
                                return 1; // Permission not granted
                            }
                            
                            _programs.Remove(ppid);
                            _logger.LogInformation($"删除程序: {ppid}");
                            
                            // 删除文件
                            Task.Run(() => DeleteProgramFile(ppid));
                        }
                    }
                }
                
                return 0; // Accepted
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除程序失败");
                return 1;
            }
        }
        
        /// <summary>
        /// 保存程序到文件
        /// </summary>
        private async Task SaveProgramToFile(ProcessProgram program)
        {
            var filePath = $"ProcessPrograms/{program.PPID}.pp";
            // 实际文件保存逻辑...
            await Task.Delay(10); // 模拟异步操作
        }
        
        /// <summary>
        /// 删除程序文件
        /// </summary>
        private async Task DeleteProgramFile(string ppid)
        {
            var filePath = $"ProcessPrograms/{ppid}.pp";
            // 实际文件删除逻辑...
            await Task.Delay(10); // 模拟异步操作
        }
        
        /// <summary>
        /// 递增版本号
        /// </summary>
        private string IncrementVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return "1.0";
                
            var parts = version.Split('.');
            if (parts.Length == 2 && int.TryParse(parts[1], out int minor))
            {
                return $"{parts[0]}.{minor + 1}";
            }
            
            return "1.0";
        }
        
        #endregion
    }
}
