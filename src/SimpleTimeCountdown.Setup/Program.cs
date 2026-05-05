namespace TimeCountdown.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var silent = args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
        var noLaunch = args.Any(arg => string.Equals(arg, "--no-launch", StringComparison.OrdinalIgnoreCase));
        var uninstall = args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase));
        var removeData = args.Any(arg => string.Equals(arg, "--remove-data", StringComparison.OrdinalIgnoreCase));
        var installDirectory = ResolveInstallDirectory(args);

        try
        {
            if (silent)
            {
                RunSilent(uninstall, noLaunch, removeData, installDirectory);
                return;
            }

            using var form = new InstallerForm(uninstall);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"安装程序运行失败：{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                InstallerContext.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void RunSilent(bool uninstall, bool noLaunch, bool removeData, string? installDirectory)
    {
        if (uninstall)
        {
            InstallerEngine.Uninstall(
                new InstallOptions
                {
                    RemoveLocalData = removeData
                },
                progress: null);
            return;
        }

        InstallerEngine.Install(
            new InstallOptions
            {
                LaunchAfterInstall = !noLaunch,
                InstallDirectory = installDirectory
            },
            progress: null);
    }

    private static string? ResolveInstallDirectory(string[] args)
    {
        var argument = args.FirstOrDefault(static arg => arg.StartsWith("--install-dir=", StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return null;
        }

        var value = argument["--install-dir=".Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
