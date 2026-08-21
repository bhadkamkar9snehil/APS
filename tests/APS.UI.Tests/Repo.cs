namespace APS.UI.Tests;

internal static class Repo
{
    private static readonly string Root = FindRoot();

    public static string File(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "APS.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the APS repository root.");
    }
}
