using AutVecu.Cores.Models;

namespace AutVecu.Services.Interfaces;

public interface IVecuBridgeService
{
    bool IsConnected { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    Task<UdsResponse> SendAsync(UdsRequest request, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
