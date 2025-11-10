using GoldbergMasterServer.Configuration;
using GoldbergMasterServer.Services;

namespace GoldbergMasterServer;

internal class Program
{
    private static bool _isShuttingDown;
    
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

        using var server = new MasterServer(config.Server.Port, logService);

        // Handle graceful shutdown
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
            server.Stop(); // Signal the server to stop
        };

        // Start the server and run it indefinitely
        await server.StartListeningAsync();
        
        logService.Info("Server stopped successfully.", "Program");
    }
}