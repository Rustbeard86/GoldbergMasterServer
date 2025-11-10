using GoldbergMasterServer.Configuration;
using GoldbergMasterServer.Services;

namespace GoldbergMasterServer;

internal class Program
{
    private static bool _isShuttingDown;
    private static readonly TaskCompletionSource<bool> ShutdownSignal = new();

    private static async Task Main(string[] _)
    {
        // Initialize configuration
        ConfigurationManager.Initialize();
        var config = ConfigurationManager.Instance.Current;

        // Initialize logging service
        var logService = new LogService(
            config.Logging.MinimumLevel,
            config.Logging.IncludeTimestamps,
            config.Logging.IncludeSourceInfo
        );

        logService.Info("Starting Goldberg Master Server...", "Program");
        logService.Debug($"Logging initialized with level: {config.Logging.MinimumLevel}", "Program");

        // Handle graceful shutdown signal
        Console.CancelKeyPress += (_, e) =>
        {
            if (_isShuttingDown)
            {
                logService.Warning("Force shutdown requested, terminating immediately...", "Program");
                e.Cancel = false; // Allow immediate termination
                return;
            }

            logService.Info("Shutdown requested, cleaning up...", "Program");
            _isShuttingDown = true;
            e.Cancel = true; // Prevent immediate termination, allow graceful shutdown
            ShutdownSignal.TrySetResult(true); // Signal shutdown
        };

        MasterServer? server = null;
        try
        {
            server = new MasterServer(config.Server.Port, logService);

            // Create a task that completes when either server stops or shutdown is signaled
            var serverTask = server.StartListeningAsync();
            var shutdownTask = ShutdownSignal.Task;

            // Wait for either the server to stop naturally or shutdown signal
            var completedTask = await Task.WhenAny(serverTask, shutdownTask);

            if (completedTask == shutdownTask)
            {
                // Shutdown was signaled, stop the server
                logService.Info("Shutdown signal received, stopping server...", "Program");
                server.Stop();

                // Wait for server to actually stop (with timeout)
                await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            // Dispose server in finally block to ensure cleanup even on exceptions
            server?.Dispose();
            logService.Info("Server stopped successfully.", "Program");
        }
    }
}