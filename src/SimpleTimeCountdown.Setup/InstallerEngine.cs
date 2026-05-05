using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace TimeCountdown.Setup;

internal static class InstallerEngine
{
    public static void Install(InstallOptions options, IProgress<InstallerProgress>? progress)
    {
        InstallerContext.SetInstallRoot(ResolveInstallDirectory(options.InstallDirectory));
        StopRunningApp();

        var tempDir = Path.Combine(Path.GetTempPath(), $"SimpleTimeCountdownSetup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Report(progress, 4, "准备安装", "正在检查安装包内容。");
            ExtractPayloadZip(tempDir);

            Report(progress, 12, "准备文件", "正在创建应用目录。");
            PrepareInstallRoot();

            Report(progress, 20, "复制应用文件", "正在写入程序文件。");
            CopyDirectory(tempDir, InstallerContext.InstallRoot, progress);

            Report(progress, 82, "完成安装配置", "正在注册快捷方式和卸载信息。");
            WriteInstallMarker();
            InstallBootstrapperCopy();
            CreateShortcuts();
            RegisterUninstall();

            if (options.LaunchAfterInstall)
            {
                Report(progress, 94, "启动应用", $"正在打开 {InstallerContext.ProductName}。");
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallerContext.AppExecutablePath,
                    UseShellExecute = true,
                    WorkingDirectory = InstallerContext.InstallRoot
                });
            }

            Report(progress, 100, "安装完成", $"{InstallerContext.ProductName} 已可以开始使用。");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    public static void Uninstall(InstallOptions options, IProgress<InstallerProgress>? progress)
    {
        Report(progress, 8, "准备卸载", "正在关闭运行中的应用。");
        StopRunningApp();

        Report(progress, 28, "移除快捷方式", "正在清理桌面、开始菜单和卸载信息。");
        RemoveShortcutsAndRegistry();

        if (Directory.Exists(InstallerContext.InstallRoot))
        {
            Report(progress, 58, "删除软件文件", "正在移除已安装的程序文件。");
            EnsureInstallRootCanBeRemoved();
            LaunchDeferredCleanup();
        }

        if (options.RemoveLocalData)
        {
            Report(progress, 82, "删除本地数据", "正在删除倒计时和设置数据。");
            TryDeleteDirectory(InstallerContext.LocalDataDirectory);
        }

        Report(progress, 100, "卸载完成", $"{InstallerContext.ProductName} 已移除。");
    }

    private static void ExtractPayloadZip(string destinationDirectory)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("TimeCountdown-portable.zip", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException("Installer payload is missing.");
        }

        var zipPath = Path.Combine(destinationDirectory, "TimeCountdown-portable.zip");
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException("Unable to open installer payload.");
        using (var file = File.Create(zipPath))
        {
            stream.CopyTo(file);
        }

        ZipFile.ExtractToDirectory(zipPath, destinationDirectory, overwriteFiles: true);
        File.Delete(zipPath);
    }

    private static void PrepareInstallRoot()
    {
        Directory.CreateDirectory(InstallerContext.InstallRoot);

        var hasMarker = File.Exists(InstallerContext.InstallMarkerPath);
        var hasAppExe = File.Exists(InstallerContext.AppExecutablePath);
        var isEmpty = !Directory.EnumerateFileSystemEntries(InstallerContext.InstallRoot).Any();

        if (!hasMarker && !hasAppExe && !isEmpty)
        {
            throw new InvalidOperationException(
                $"Install directory is not empty and is not a previous {InstallerContext.ProductName} installation: {InstallerContext.InstallRoot}.{Environment.NewLine}" +
                "Choose an empty folder or a folder previously used by this installer.");
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(InstallerContext.InstallRoot))
        {
            TryDeletePath(path);
        }
    }

    private static void WriteInstallMarker()
    {
        File.WriteAllText(
            InstallerContext.InstallMarkerPath,
            $"{InstallerContext.ProductName} {InstallerContext.ProductDisplayVersion}");
    }

    private static void InstallBootstrapperCopy()
    {
        Directory.CreateDirectory(InstallerContext.InstallerDirectory);

        var currentExe = Environment.ProcessPath ??
                         throw new InvalidOperationException("Unable to locate the setup executable.");
        File.Copy(currentExe, InstallerContext.InstallerExecutablePath, overwrite: true);
    }

    private static void CreateShortcuts()
    {
        RemoveShortcutArtifacts();
        Directory.CreateDirectory(InstallerContext.StartMenuDirectory);

        CreateShortcut(
            InstallerContext.DesktopShortcutPath,
            InstallerContext.AppExecutablePath,
            InstallerContext.InstallRoot,
            InstallerContext.AppShortcutIconPath,
            null);

        CreateShortcut(
            Path.Combine(InstallerContext.StartMenuDirectory, $"{InstallerContext.ProductName}.lnk"),
            InstallerContext.AppExecutablePath,
            InstallerContext.InstallRoot,
            InstallerContext.AppShortcutIconPath,
            null);

        CreateShortcut(
            Path.Combine(InstallerContext.StartMenuDirectory, $"Uninstall {InstallerContext.ProductName}.lnk"),
            InstallerContext.InstallerExecutablePath,
            InstallerContext.InstallerDirectory,
            InstallerContext.InstallerShortcutIconPath,
            "--uninstall");
    }

    private static void RegisterUninstall()
    {
        using var key = Registry.CurrentUser.CreateSubKey(InstallerContext.UninstallRegistryPath);

        key?.SetValue("DisplayName", InstallerContext.ProductName);
        key?.SetValue("Publisher", InstallerContext.ProductName);
        key?.SetValue("DisplayVersion", InstallerContext.ProductVersion);
        key?.SetValue("InstallLocation", InstallerContext.InstallRoot);
        key?.SetValue("DisplayIcon", InstallerContext.AppExecutablePath);
        key?.SetValue("UninstallString", $"\"{InstallerContext.InstallerExecutablePath}\" --uninstall");
        key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void RemoveShortcutsAndRegistry()
    {
        RemoveShortcutArtifacts();
        RemoveAutostartEntry();
        Registry.CurrentUser.DeleteSubKeyTree(InstallerContext.UninstallRegistryPath, throwOnMissingSubKey: false);
    }

    private static void RemoveAutostartEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InstallerContext.AutostartRegistryPath, writable: true);
        key?.DeleteValue(InstallerContext.AutostartRegistryValueName, throwOnMissingValue: false);
    }

    private static void RemoveShortcutArtifacts()
    {
        foreach (var shortcutPath in InstallerContext.AllDesktopShortcutPaths)
        {
            TryDeleteFile(shortcutPath);
        }

        TryDeleteStartMenuShortcut(InstallerContext.StartMenuDirectory, $"{InstallerContext.ProductName}.lnk");
        TryDeleteStartMenuShortcut(InstallerContext.StartMenuDirectory, $"Uninstall {InstallerContext.ProductName}.lnk");
        TryDeleteEmptyDirectory(InstallerContext.StartMenuDirectory);

        TryDeleteStartMenuShortcut(InstallerContext.LegacyStartMenuDirectory, "Time Countdown.lnk");
        TryDeleteStartMenuShortcut(InstallerContext.LegacyStartMenuDirectory, $"Uninstall {InstallerContext.ProductName}.lnk");
        TryDeleteStartMenuShortcut(InstallerContext.LegacyStartMenuDirectory, "Uninstall Time Countdown.lnk");
        TryDeleteEmptyDirectory(InstallerContext.LegacyStartMenuDirectory);
    }

    private static void LaunchDeferredCleanup()
    {
        RegisterDeferredCleanupRunOnce();

        var installRoot = InstallerContext.InstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var markerPath = InstallerContext.InstallMarkerPath;
        var appPath = InstallerContext.AppExecutablePath;
        var installerPath = InstallerContext.InstallerExecutablePath;
        var runOncePath = $@"HKCU\{InstallerContext.CleanupRunOnceRegistryPath}";
        var runOnceValueName = InstallerContext.CleanupRunOnceRegistryValueName;
        var arguments =
            $"/d /c timeout /t 2 /nobreak >nul & " +
            $"for /l %i in (1,1,30) do (" +
            $"if not exist \"{markerPath}\" exit /b 0 & " +
            $"if not exist \"{appPath}\" exit /b 0 & " +
            $"if not exist \"{installerPath}\" exit /b 0 & " +
            $"rmdir /s /q \"{installRoot}\" >nul 2>nul & " +
            $"if not exist \"{installRoot}\" (" +
            $"reg delete \"{runOncePath}\" /v \"{runOnceValueName}\" /f >nul 2>nul & exit /b 0" +
            $") & timeout /t 1 /nobreak >nul" +
            $")";

        var cleanupProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (cleanupProcess is null)
        {
            throw new InvalidOperationException("Unable to schedule installer cleanup.");
        }
    }

    private static void RegisterDeferredCleanupRunOnce()
    {
        var installRoot = InstallerContext.InstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        using var key = Registry.CurrentUser.CreateSubKey(InstallerContext.CleanupRunOnceRegistryPath, writable: true);
        key?.SetValue(
            InstallerContext.CleanupRunOnceRegistryValueName,
            $"cmd.exe /d /c if exist \"{InstallerContext.InstallMarkerPath}\" if exist \"{InstallerContext.AppExecutablePath}\" if exist \"{InstallerContext.InstallerExecutablePath}\" rmdir /s /q \"{installRoot}\"",
            RegistryValueKind.String);
    }

    private static void EnsureInstallRootCanBeRemoved()
    {
        if (InstallerContext.IsInstallRootSafeForRemoval(InstallerContext.InstallRoot))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to remove install directory because it could not be verified as a {InstallerContext.ProductName} installation: {InstallerContext.InstallRoot}");
    }

    private static void StopRunningApp()
    {
        var targetPath = Path.GetFullPath(InstallerContext.AppExecutablePath);

        foreach (var process in Process.GetProcessesByName("TimeCountdown"))
        {
            try
            {
                string? processPath = null;
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch
                {
                }

                if (processPath is null ||
                    !string.Equals(Path.GetFullPath(processPath), targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }

    private static void CopyDirectory(string source, string destination, IProgress<InstallerProgress>? progress)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);

            var percent = 20 + (int)Math.Round(((index + 1d) / files.Length) * 58);
            Report(progress, percent, "复制应用文件", $"正在安装 {relative}");
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string? arguments)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
                        throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = iconPath;
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            shortcut.Arguments = arguments;
        }

        shortcut.Save();
    }

    private static void Report(IProgress<InstallerProgress>? progress, int percent, string title, string detail)
    {
        progress?.Report(new InstallerProgress(percent, title, detail));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, recursive: false);
        }
    }

    private static void TryDeleteStartMenuShortcut(string directory, string fileName)
    {
        TryDeleteFile(Path.Combine(directory, fileName));
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            TryDeleteDirectory(path);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ResolveInstallDirectory(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return InstallerContext.InstallRoot;
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(installDirectory.Trim()));
    }
}
