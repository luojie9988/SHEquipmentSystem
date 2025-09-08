using DiceEquipmentSystem.Core.Configuration;
using DiceEquipmentSystem.PLC.Services;

namespace SHEquipmentSystem.Models
{
    // ViewModels/ConfigViewModels.cs
    public class PLCConfigViewModel
    {
        public DiceEquipmentSystem.Core.Configuration.PlcConfiguration PLCConfig { get; set; }
        public bool IsConnected { get; set; }
        public string ConnectionStatus { get; set; }
        public List<string> ValidationErrors { get; set; }
    }

    public class EquipmentConfigViewModel
    {
        public EquipmentSystemConfiguration EquipmentConfig { get; set; }
        public Dictionary<string, object> SystemStatus { get; set; }
        public List<string> ValidationErrors { get; set; }
    }
}
