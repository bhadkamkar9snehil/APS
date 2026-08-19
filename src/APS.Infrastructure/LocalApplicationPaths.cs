namespace APS.Infrastructure;

/// <summary>
/// Canonical local filesystem layout for the desktop application. Keeping these paths in one
/// object prevents the host and infrastructure services from independently reconstructing
/// slightly different locations and filename conventions.
/// </summary>
public sealed class LocalApplicationPaths
{
    public const string ProductDirectoryName = "APS";
    public const string DataDirectoryName = "Data";
    public const string LogFilePrefix = "aps-";

    public LocalApplicationPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DataDirectory = Path.GetFullPath(dataDirectory);
        LogDirectory = Path.Combine(DataDirectory, "logs");
        LogFilePattern = Path.Combine(LogDirectory, $"{LogFilePrefix}.log");
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }

    /// <summary>
    /// Serilog rolling-file pattern. With daily rolling it produces aps-YYYYMMDD.log.
    /// </summary>
    public string LogFilePattern { get; }

    public string GetLogPath(DateTime localDate) =>
        Path.Combine(LogDirectory, $"{LogFilePrefix}{localDate:yyyyMMdd}.log");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public static LocalApplicationPaths ForCurrentUser()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName,
            DataDirectoryName);
        return new LocalApplicationPaths(root);
    }
}
