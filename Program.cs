using GoldbergMasterServer.Configuration;
using GoldbergMasterServer.Services;

namespace GoldbergMasterServer;

internal class Program
{
    private static async Task Main(string[] args)
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
            logService.Info("Shutting down server...", "Program");
            e.Cancel = true; // Prevent immediate termination
        };

        // Start the server and run it indefinitely
        await server.StartListeningAsync();
    }
}