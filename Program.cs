using GoldbergMasterServer.Configuration;

namespace GoldbergMasterServer;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Goldberg Master Server...");

        // Initialize configuration
        ConfigurationManager.Initialize();
        var config = ConfigurationManager.Instance.Current;

        using var server = new MasterServer(config.Server.Port);

        // Handle graceful shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
        };

        // Start the server and run it indefinitely
        await server.StartListeningAsync();
    }
}