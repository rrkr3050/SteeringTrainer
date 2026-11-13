namespace SteeringTrainer.Core.Models
{
    public class DeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ApiType { get; set; } = string.Empty;
        public int AxisCount { get; set; }
        public bool IsSteeringDevice { get; set; }
    }
}
