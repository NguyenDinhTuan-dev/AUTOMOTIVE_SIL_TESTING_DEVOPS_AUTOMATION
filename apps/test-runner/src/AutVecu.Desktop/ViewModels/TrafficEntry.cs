namespace AutVecu.Desktop.ViewModels;

public class TrafficEntry
{
    public DateTime Time { get; set; } = DateTime.Now;

    public string Direction { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Meaning { get; set; } = string.Empty;
}
