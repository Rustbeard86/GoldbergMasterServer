namespace GoldbergMasterServer.Configuration;

/// <summary>
///     Server-specific configuration settings
/// </summary>
public sealed class ServerConfig
{
    private const ulong DefaultMasterServerSteamId = 0x100001DEADBEEF;

    public int Port { get; set; } = 47584;
    public int PeerCleanupIntervalSeconds { get; set; } = 10;
    public int PeerTimeoutSeconds { get; set; } = 30;
    public ulong MasterServerSteamId { get; set; } = DefaultMasterServerSteamId;
}

/// <summary>
///     Individual log level configuration
/// </summary>
public sealed class LogCategoryConfig(LogLevel level = LogLevel.Information)
{
    public LogLevel Level { get; set; } = level;
}

/// <summary>
///     Log level configuration for different categories
/// </summary>
public sealed class LogLevelConfig
{
    public LogCategoryConfig Default { get; set; } = new();

    public LogCategoryConfig Microsoft { get; set; } = new(LogLevel.Warning);

    public LogCategoryConfig System { get; set; } = new(LogLevel.Warning);

    public LogCategoryConfig Network { get; set; } = new();

    public LogCategoryConfig PeerManagement { get; set; } = new();

    public LogCategoryConfig Authentication { get; set; } = new();
}

/// <summary>
///     Debug-specific logging configuration
/// </summary>
public sealed class DebugConfig
{
    public bool EnableDebug { get; set; } = false;

    public bool LogNetworkPackets { get; set; } = false;

    public bool LogPeerEvents { get; set; } = true;

    public bool LogAuthenticationAttempts { get; set; } = true;
}

/// <summary>
///     Logging-specific configuration settings
/// </summary>
public sealed class LoggingConfig
{
    public LogLevelConfig LogLevels { get; set; } = new();

    public DebugConfig Debug { get; set; } = new();

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool IncludeTimestamps { get; set; } = true;

    public bool IncludeSourceInfo { get; set; } = true;

    public bool IncludeEventId { get; set; } = true;

    public bool LogToConsole { get; set; } = true;

    public bool LogToFile { get; set; } = true;
}

/// <summary>
///     Root configuration class that holds all configuration sections
/// </summary>
public sealed class AppConfig
{
    public ServerConfig Server { get; set; } = new();

    public LoggingConfig Logging { get; set; } = new();
}