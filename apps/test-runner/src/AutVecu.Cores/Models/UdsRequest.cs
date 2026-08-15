using AutVecu.Cores.Enums;

namespace AutVecu.Cores.Models;

public class UdsRequest
{
    public UdsServiceId ServiceId { get; set; }

    public byte[] Payload { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
