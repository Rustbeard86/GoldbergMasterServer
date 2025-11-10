using System.Net.Sockets;
using GoldbergMasterServer.Services;

namespace GoldbergMasterServer;

/// <summary>
///     The main UDP Master Server logic.
/// </summary>
public sealed class MasterServer : IDisposable
{
    private readonly Timer _cleanupTimer;
    private readonly GameserverManager _gameserverManager;
    private readonly LobbyManager _lobbyManager;
    private readonly LogService _logService;
    private readonly MessageHandler _messageHandler;
    private readonly NetworkService _networkService;
    private readonly P2PRelayManager _p2PRelayManager;
    private readonly PeerManager _peerManager;
    private bool _disposed;
    private volatile bool _isRunning = true;

    public MasterServer(int port, LogService logService)
    {
        _logService = logService;
        _networkService = new NetworkService(port, logService);
        _peerManager = new PeerManager(TimeSpan.FromSeconds(30), logService);
        _lobbyManager = new LobbyManager(TimeSpan.FromMinutes(5), logService);
        _gameserverManager = new GameserverManager(TimeSpan.FromMinutes(10), logService);
        _p2PRelayManager = new P2PRelayManager(TimeSpan.FromMinutes(5), logService);
        _messageHandler = new MessageHandler(_peerManager, _networkService, _lobbyManager, _gameserverManager,
            _p2PRelayManager, logService);

        _logService.Info($"Server initialized on UDP port {port}", "MasterServer");

        // Start a timer to clean up disconnected peers, stale servers, and idle connections every 10 seconds
        _cleanupTimer = new Timer(_ =>
        {
            _peerManager.CleanupStaleMembers();
            _gameserverManager.CleanupStaleServers();
            _p2PRelayManager.CleanupStaleConnections();
        }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
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
            _logService.Info("Disposing server resources...", "MasterServer");
            _isRunning = false;
            _cleanupTimer.Dispose();
            _networkService.Dispose(); // Safe to call multiple times due to NetworkService's _disposed check
            _peerManager.Shutdown();
            _lobbyManager.Shutdown();
            _gameserverManager.Shutdown();
            _p2PRelayManager.Shutdown();
        }

        _disposed = true;
    }

    /// <summary>
    ///     Signals the server to stop listening
    /// </summary>
    public void Stop()
    {
        _logService.Info("Stop signal received, shutting down network listener...", "MasterServer");
        _isRunning = false;

        // Close the network service to interrupt the ReceiveAsync() call
        _networkService.Dispose();
    }

    /// <summary>
    ///     Starts the main UDP listening loop.
    /// </summary>
    public async Task StartListeningAsync()
    {
        _logService.Info("Master server listening for connections...", "MasterServer");
        try
        {
            while (_isRunning && !_disposed)
            {
                var (buffer, endpoint) = await _networkService.ReceiveAsync();

                // Log incoming connection at Info level for visibility
                _logService.Info($"Received {buffer.Length} bytes from {endpoint}", "MasterServer");

                // Process the packet without blocking the listener loop
                _ = Task.Run(() => _messageHandler.HandleMessageAsync(buffer, endpoint))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logService.Error($"Error processing message: {t.Exception?.GetBaseException().Message}",
                                "MasterServer");
                    });
            }
        }
        catch (ObjectDisposedException)
        {
            // Server is shutting down, this is normal
            _logService.Info("Server shutting down (socket disposed)...", "MasterServer");
        }
        catch (SocketException ex) when (ex.ErrorCode == 995 || ex.ErrorCode == 10004)
        {
            // WSA operation aborted (995) or interrupted (10004) - normal during shutdown
            _logService.Info("Server shutting down (socket operation cancelled)...", "MasterServer");
        }
        catch (Exception ex)
        {
            _logService.Critical($"Unexpected error in server loop: {ex.Message}", "MasterServer");
            throw;
        }
        finally
        {
            _logService.Info("Server loop exited", "MasterServer");
        }
    }
}