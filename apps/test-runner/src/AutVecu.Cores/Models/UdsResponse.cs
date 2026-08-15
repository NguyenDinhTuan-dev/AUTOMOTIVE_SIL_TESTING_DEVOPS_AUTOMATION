namespace AutVecu.Cores.Models;

public class UdsResponse
{
    public bool IsPositiveResponse { get; set; }

    public byte ServiceId { get; set; }

    public byte[] Payload { get; set; } = [];

    public string RawHex { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;
}
