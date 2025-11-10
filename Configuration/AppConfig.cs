namespace GoldbergMasterServer.Configuration;

/// <summary>
///     Server-specific configuration settings
/// </summary>
public sealed class ServerConfig
{
    public required int Port { get; set; }
    public required int PeerCleanupIntervalSeconds { get; set; }
    public required int PeerTimeoutSeconds { get; set; }
    public required ulong MasterServerSteamId { get; set; }
}

/// <summary>
///     Log level configuration for different categories
/// </summary>
public sealed class LogLevelConfig
{
    public required string Default { get; set; }
    public required string Microsoft { get; set; }
    public required string System { get; set; }
}

/// <summary>
///     Debug-specific logging configuration
/// </summary>
public sealed class DebugConfig
{
    public required bool EnableDebug { get; set; }
}

/// <summary>
///     Logging-specific configuration settings
/// </summary>
public sealed class LoggingConfig
{
    public required LogLevelConfig LogLevel { get; init; }
    public required DebugConfig Debug { get; init; }
    public string MinimumLevel { get; set; } = "Info";
    public bool IncludeTimestamps { get; set; } = true;
    public bool IncludeSourceInfo { get; set; } = true;
}

/// <summary>
///     Root configuration class that holds all configuration sections
/// </summary>
public sealed class AppConfig
{
    public required ServerConfig Server { get; init; }
    public required LoggingConfig Logging { get; init; }
}