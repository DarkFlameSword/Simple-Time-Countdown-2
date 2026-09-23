using System.IO;

namespace TimeCountdown.Tests;

/// <summary>Locates the repository so tests can scan sources (XAML and C#) for resource keys.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string AppSource => Path.Combine(Root, "src", "SimpleTimeCountdown.App");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SimpleTimeCountdown.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("SimpleTimeCountdown.sln not found above the test output folder.");
    }
}
