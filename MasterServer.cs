using GoldbergMasterServer.Services;

namespace GoldbergMasterServer;

/// <summary>
///     The main UDP Master Server logic.
/// </summary>
public sealed class MasterServer : IDisposable
{
    private readonly Timer _cleanupTimer;
    private readonly LobbyManager _lobbyManager;
    private readonly MessageHandler _messageHandler;
    private readonly NetworkService _networkService;
    private readonly PeerManager _peerManager;
    private bool _disposed;

    public MasterServer(int port)
    {
        _networkService = new NetworkService(port);
        _peerManager = new PeerManager(TimeSpan.FromSeconds(30));
        _lobbyManager = new LobbyManager(TimeSpan.FromMinutes(5)); // 5 minute timeout for deleted lobbies
        _messageHandler = new MessageHandler(_peerManager, _networkService, _lobbyManager);

        // Start a timer to clean up disconnected peers every 10 seconds
        _cleanupTimer = new Timer(_ => _peerManager.CleanupStaleMembers(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MasterServer()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _cleanupTimer.Dispose();
            _networkService.Dispose();
            _peerManager.Shutdown();
            _lobbyManager.Shutdown();
        }

        _disposed = true;
    }

    /// <summary>
    ///     Starts the main UDP listening loop.
    /// </summary>
    public async Task StartListeningAsync()
    {
        Console.WriteLine("[INFO] Master server listening...");
        try
        {
            while (!_disposed)
            {
                var (buffer, endpoint) = await _networkService.ReceiveAsync();
                // Process the packet without blocking the listener loop
                _ = Task.Run(() => _messageHandler.HandleMessageAsync(buffer, endpoint));
            }
        }
        catch (ObjectDisposedException)
        {
            // Server is shutting down, this is normal
        }
    }
}