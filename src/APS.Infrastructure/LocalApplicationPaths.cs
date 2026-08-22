namespace APS.Infrastructure;

/// <summary>
/// Canonical local filesystem layout for the desktop application. Keeping these paths in one
/// object prevents the host and infrastructure services from independently reconstructing
/// slightly different locations and filename conventions.
/// </summary>
public sealed class LocalApplicationPaths
{
    /// <summary>
    /// Deliberately NOT "APS" - Velopack installs the app itself at %LocalAppData%\APS and wipes
    /// that entire tree on every update (rename-for-rollback then clean). A data directory nested
    /// inside it gets deleted along with the old app version on every single update. This name
    /// must never collide with the Velopack packId used in build/release.ps1.
    /// </summary>
    public const string ProductDirectoryName = "APS-Data";
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
