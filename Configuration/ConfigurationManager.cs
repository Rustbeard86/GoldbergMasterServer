using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoldbergMasterServer.Configuration;

/// <summary>
///     Manages application configuration loading and access
/// </summary>
public sealed class ConfigurationManager
{
    private static ConfigurationManager? _instance;
    private static readonly object Lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip // Enable JSONC support
    };

    private readonly string _configPath;

    private ConfigurationManager(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (!File.Exists(_configPath))
        {
            Current = CreateDefaultConfig();
            SaveConfiguration();
        }
        else
        {
            try
            {
                var jsonString = File.ReadAllText(_configPath);
                Current = JsonSerializer.Deserialize<AppConfig>(jsonString, JsonOptions) ?? CreateDefaultConfig();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing configuration file: {ex.Message}");
                Current = CreateDefaultConfig();
                SaveConfiguration();
            }
        }
    }

    public static ConfigurationManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (Lock)
            {
                _instance ??= new ConfigurationManager();
            }

            return _instance;
        }
    }

    /// <summary>
    ///     Gets the current configuration
    /// </summary>
    public AppConfig Current { get; }

    private static AppConfig CreateDefaultConfig()
    {
        var logLevel = new LogLevelConfig
        {
            Default = "Information",
            Microsoft = "Warning",
            System = "Warning"
        };

        var debug = new DebugConfig
        {
            EnableDebug = false
        };

        var logging = new LoggingConfig
        {
            LogLevel = logLevel,
            Debug = debug
        };

        var server = new ServerConfig
        {
            Port = 47584,
            PeerCleanupIntervalSeconds = 10,
            PeerTimeoutSeconds = 30,
            MasterServerSteamId = 0x100001DEADBEEF
        };

        return new AppConfig
        {
            Server = server,
            Logging = logging
        };
    }

    /// <summary>
    ///     Initializes the configuration manager with an optional config file path
    /// </summary>
    /// <param name="configPath">Path to the configuration file. If null, uses default path.</param>
    public static void Initialize(string? configPath = null)
    {
        if (_instance != null)
            throw new InvalidOperationException("Configuration manager is already initialized.");

        lock (Lock)
        {
            _instance = new ConfigurationManager(configPath);
        }
    }

    /// <summary>
    ///     Saves the current configuration to the specified file
    /// </summary>
    private void SaveConfiguration()
    {
        var jsonString = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_configPath, jsonString);
    }
}