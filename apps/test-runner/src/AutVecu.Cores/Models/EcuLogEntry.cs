using AutVecu.Cores.Enums;

namespace AutVecu.Cores.Models;

public class EcuLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Source { get; set; } = string.Empty;

    public EcuLogLevel Level { get; set; } = EcuLogLevel.Information;

    public string Message { get; set; } = string.Empty;
}
