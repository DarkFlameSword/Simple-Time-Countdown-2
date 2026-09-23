using System.Globalization;

namespace TimeCountdown.Setup;

/// <summary>
/// English and Simplified Chinese text for Setup. The language follows the Windows display
/// language (zh-* picks Chinese, anything else English) unless <c>--lang</c> chooses one.
/// Every message the engine reports or throws, and everything the wizard shows ("Ui.*"), comes
/// from here, so a Chinese error is never wrapped around English exception text. Every key has
/// both an English and a Chinese entry. Strings may contain <c>{product}</c>, which is
/// replaced with the product name, and composite-format items such as <c>{0}</c>.
/// </summary>
internal static class InstallerText
{
    public const string EnglishCode = "en";
    public const string ChineseCode = "zh-CN";

    private const string ProductToken = "{product}";

    private static bool _isChinese = IsChineseCulture(CultureInfo.CurrentUICulture);

    public static bool IsChinese => _isChinese;

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_isChinese ? ChineseCode : "en-US");

    /// <summary>Picks the language from a <c>--lang</c> value; null or empty keeps the Windows choice.</summary>
    public static void SetLanguage(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            _isChinese = languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsSupportedLanguage(string languageCode) =>
        languageCode.Equals(EnglishCode, StringComparison.OrdinalIgnoreCase) ||
        languageCode.StartsWith("en-", StringComparison.OrdinalIgnoreCase) ||
        languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static string Get(string key)
    {
        var table = _isChinese ? Chinese : English;
        if (!table.TryGetValue(key, out var value) && !English.TryGetValue(key, out value))
        {
            // A missing key is a programming error; showing the key keeps the message traceable.
            value = key;
        }

        return value.Replace(ProductToken, ProductConstants.ProductName, StringComparison.Ordinal);
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    /// <summary>Formats a byte count as KB/MB/GB with the UI culture's number format; "—" when unknown.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        const double kib = 1024.0;
        const double mib = kib * 1024.0;
        const double gib = mib * 1024.0;
        return bytes switch
        {
            < (long)mib => string.Format(Culture, "{0:0.#} KB", bytes / kib),
            < (long)gib => string.Format(Culture, "{0:0.#} MB", bytes / mib),
            _ => string.Format(Culture, "{0:0.##} GB", bytes / gib)
        };
    }

    private static bool IsChineseCulture(CultureInfo culture) =>
        culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    internal static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // ===== Engine: progress =====
        ["Progress.Checking"] = "Checking the installation",
        ["Progress.Checking.Detail"] = "Verifying the setup package and the destination folder.",
        ["Progress.ClosingApp"] = "Closing {product}",
        ["Progress.ClosingApp.Detail"] = "Asking the running app to save its work and exit.",
        ["Progress.Unpacking"] = "Unpacking program files",
        ["Progress.Unpacking.Detail"] = "Unpacking {0}",
        ["Progress.Replacing"] = "Putting the new files in place",
        ["Progress.Replacing.Detail"] = "Updating the files in {0}.",
        ["Progress.Registering"] = "Creating shortcuts",
        ["Progress.Registering.Detail"] = "Adding {product} to the Start menu and to Settings > Apps.",
        ["Progress.RemovingOldCopy"] = "Removing the previous copy",
        ["Progress.RemovingOldCopy.Detail"] = "{product} now lives in a new folder; removing the old files in {0}.",
        ["Progress.Launching"] = "Starting {product}",
        ["Progress.Launching.Detail"] = "Opening the countdown panel.",
        ["Progress.Installed"] = "Installation complete",
        ["Progress.Installed.Detail"] = "{product} is ready to use.",
        ["Progress.RollingBack"] = "Undoing the changes",
        ["Progress.RollingBack.Detail"] = "Restoring the files that were there before.",
        ["Progress.Uninstall.Preparing"] = "Preparing to uninstall",
        ["Progress.Uninstall.Preparing.Detail"] = "Checking the installation in {0}.",
        ["Progress.Uninstall.Removing"] = "Removing program files",
        ["Progress.Uninstall.Removing.Detail"] = "Removing {0}",
        ["Progress.Uninstall.Shortcuts"] = "Removing shortcuts",
        ["Progress.Uninstall.Shortcuts.Detail"] = "Removing {product} from the Start menu, the desktop and Settings > Apps.",
        ["Progress.Uninstall.Data"] = "Removing your data",
        ["Progress.Uninstall.Data.Detail"] = "Deleting your countdowns, settings and logs.",
        ["Progress.Uninstall.Done"] = "Uninstall complete",
        ["Progress.Uninstall.Done.Detail"] = "{product} has been removed.",

        // ===== Engine: errors =====
        ["Error.Title"] = "{product} Setup",
        ["Error.PayloadMissing"] = "This setup file doesn't contain the program files. Download the complete installer and run it again.",
        ["Error.PayloadCorrupt"] = "The setup file is damaged ({0}). Download the installer again.",
        ["Error.InvalidDirectory"] = "\"{0}\" isn't a valid folder. Enter a full path, such as {1}.",
        ["Error.ProtectedDirectory"] = "{0} is a system or personal folder. Choose a folder of its own, such as {1}.",
        ["Error.UnsupportedDrive"] = "{0} is on a network, removable or non-NTFS drive, where the program files can't be protected. Choose a folder on a local NTFS drive.",
        ["Error.DirectoryNotEmpty"] = "{0} already contains other files. Choose an empty folder or a new one, such as {1}.",
        ["Error.DirectoryNotWritable"] = "Setup can't write to {0}. Choose a folder you're allowed to change.",
        ["Error.SecureFolderFailed"] = "Setup couldn't restrict access to {0}, so other people on this PC could change the program files. Choose a folder in your user profile.",
        ["Error.InsufficientSpace"] = "There isn't enough free space on {0}: {1} is needed and {2} is free.",
        ["Error.AppRunning"] = "{product} is running. It has to be closed before Setup can continue.",
        ["Error.AppCouldNotBeClosed"] = "{product} couldn't be closed. Exit it from its tray icon, then try again.",
        ["Error.FilesInUse"] = "Some program files are in use: {0}. Close the program that's using them, then try again.",
        ["Error.NotInstalled"] = "{product} isn't installed for this account.",
        ["Error.UnverifiedInstall"] = "{0} couldn't be verified as a {product} installation, so nothing was removed.",
        ["Error.Unexpected"] = "Setup couldn't finish: {0}",
        ["Error.AnotherSetupRunning"] = "Another {product} setup is already running. Finish it first.",
        ["Error.UninstallerCopyFailed"] = "Setup couldn't prepare the uninstaller in {0}. Make sure there's some free space on that drive, then try again.",
        ["Error.InvalidArguments"] = "Unrecognised option: {0}",

        // ===== Engine: warnings shown on the completion page =====
        ["Warning.ShortcutsFailed"] = "The Start menu and desktop shortcuts couldn't be created. You can start {product} from {0}.",
        ["Warning.LaunchFailed"] = "{product} was installed but couldn't be started. Start it from the Start menu.",
        ["Warning.OldCopyKept"] = "The previous copy in {0} couldn't be removed completely. You can delete that folder yourself.",
        ["Warning.UnknownFilesKept"] = "{0} contains files that Setup didn't install, so the folder was kept.",
        ["Warning.RemovalPending"] = "A few files in {0} were still in use. They'll be removed when Setup closes, or at your next sign-in.",
        ["Warning.DataNotRemoved"] = "Some of your data in {0} couldn't be removed. You can delete that folder yourself.",
        ["Warning.RegistrationOnly"] = "The program folder {0} no longer exists, so only the shortcuts and the Settings > Apps entry were removed.",

        // ===== Command line =====
        ["Usage"] =
            "Usage: \"{0}\" [options]\n\n" +
            "  --silent                  Install or uninstall without showing any window.\n" +
            "  --install-dir=<folder>    Install into <folder> (default: {1}).\n" +
            "  --launch                  Start {product} after a silent install.\n" +
            "  --no-desktop-shortcut     Don't create a desktop shortcut.\n" +
            "  --uninstall               Uninstall {product}.\n" +
            "  --remove-data             With --uninstall, also delete your countdowns, settings and logs.\n" +
            "  --log=<file>              Write the setup log to <file>.\n" +
            "  --lang=en|zh-CN           Choose the language of Setup.\n\n" +
            "Exit codes: 0 success, 87 invalid option or folder, 112 not enough disk space, 1602 cancelled, " +
            "1603 failed, 1605 not installed, 1618 another setup is running, 1620 damaged setup file.",

        // ===== Wizard: window, sidebar and buttons =====
        // "&" marks the access key. Keep one per page unique: L, B, D, S and I/U on the install page.
        ["Ui.WindowTitle.Install"] = "{product} Setup",
        ["Ui.WindowTitle.Uninstall"] = "Uninstall {product}",
        ["Ui.Sidebar.Version"] = "Version {0}",
        ["Ui.Sidebar.Logo"] = "{product} logo",
        ["Ui.Step.Options"] = "Choose options",
        ["Ui.Step.Install"] = "Install",
        ["Ui.Step.Uninstall"] = "Uninstall",
        ["Ui.Step.Finish"] = "Finish",
        ["Ui.Step.Done"] = "{0}, done",
        ["Ui.Step.Current"] = "{0}, current step",
        ["Ui.Step.Pending"] = "{0}, not started yet",
        ["Ui.Button.Install"] = "&Install",
        ["Ui.Button.Update"] = "&Update",
        ["Ui.Button.Uninstall"] = "&Uninstall",
        ["Ui.Button.Cancel"] = "Cancel",
        ["Ui.Button.Finish"] = "&Finish",
        ["Ui.Button.Close"] = "&Close",
        ["Ui.Button.Browse"] = "&Browse…",

        // ===== Wizard: install options =====
        ["Ui.Install.Heading"] = "Install {product}",
        ["Ui.Install.Subtitle"] = "Choose where to install it, then select Install.",
        ["Ui.Update.Heading"] = "Update {product}",
        ["Ui.Update.Subtitle"] = "Your countdowns and settings are kept.",
        ["Ui.Install.Intro"] = "{product} shows your deadlines as countdown cards on the desktop. Setup installs it for your Windows account only, so it doesn't need administrator rights.",
        ["Ui.Card.Version"] = "Version",
        ["Ui.Card.InstalledVersion"] = "Installed version",
        ["Ui.Card.Space"] = "Space required",
        ["Ui.Card.Location"] = "Location",
        ["Ui.Card.UnknownVersion"] = "Unknown",
        ["Ui.Location.Label"] = "Install &location",
        ["Ui.Location.AccessibleName"] = "Install location",
        ["Ui.Location.BrowseTitle"] = "Choose a folder for {product}",
        ["Ui.Location.SubfolderAdded"] = "Setup added a “{product}” folder inside the folder you chose, so the program files stay together.",
        ["Ui.Location.Empty"] = "Enter the folder to install into, such as {0}.",
        ["Ui.Location.Upgrade"] = "Version {0} is installed here and will be updated to {1}.",
        ["Ui.Location.Reinstall"] = "This version is already installed here. Setup will replace its files.",
        ["Ui.Location.Downgrade"] = "A newer version ({0}) is installed here. Installing replaces it with version {1}.",
        ["Ui.Location.Move"] = "{product} is installed in {0} at the moment. Setup will move it here and remove the old copy.",
        ["Ui.Option.DesktopShortcut"] = "Create a &desktop shortcut",
        ["Ui.Option.Launch"] = "&Start {product} when Setup finishes",
        ["Ui.Elevated"] = "Setup is running as administrator, which it doesn't need. It will install {product} for {0} only. If that isn't your account, close Setup and run it again without administrator rights.",

        // ===== Wizard: uninstall options =====
        ["Ui.Uninstall.Heading"] = "Uninstall {product}",
        ["Ui.Uninstall.Subtitle"] = "Check what will be removed, then select Uninstall.",
        ["Ui.Uninstall.Intro"] = "Setup removes the program, its shortcuts and its entry in Settings > Apps. Files you added to the program folder are kept.",
        ["Ui.Option.RemoveData"] = "Also &delete my countdowns, settings and logs",
        ["Ui.Option.RemoveData.Hint"] = "Your countdowns and settings are saved in {0}, the logs in {1}. Leave this unchecked to keep them for a later reinstall.",

        // ===== Wizard: progress =====
        ["Ui.Progress.Install.Heading"] = "Installing {product}",
        ["Ui.Progress.Update.Heading"] = "Updating {product}",
        ["Ui.Progress.Uninstall.Heading"] = "Uninstalling {product}",
        ["Ui.Progress.Subtitle"] = "This takes a few seconds. Please keep this window open.",
        ["Ui.Progress.Starting"] = "Getting ready…",
        ["Ui.Progress.Name"] = "Progress",
        ["Ui.Progress.Cancelling"] = "Cancelling. Setup is putting back what it changed…",

        // ===== Wizard: result =====
        ["Ui.Done.Install.Heading"] = "Setup is complete",
        ["Ui.Done.Update.Heading"] = "Update complete",
        ["Ui.Done.Uninstall.Heading"] = "Uninstall complete",
        ["Ui.Done.Subtitle"] = "Select Finish to close Setup.",
        ["Ui.Done.Installed"] = "{product} is installed",
        ["Ui.Done.Updated"] = "{product} is up to date",
        ["Ui.Done.Location"] = "Version {0} is in {1}.",
        ["Ui.Done.Launched"] = "It's starting now, and its panel appears on your desktop in a moment.",
        ["Ui.Done.StartMenu"] = "You can start it from the Start menu.",
        ["Ui.Done.Restart"] = "Setup closed {product} while it worked. Start it again from the Start menu.",
        ["Ui.Done.Removed"] = "{product} has been removed",
        ["Ui.Done.RemovalPending"] = "{product} is almost removed",
        ["Ui.Done.Uninstalled"] = "{product} has been uninstalled",
        ["Ui.Done.DataDeleted"] = "Your countdowns, settings and logs were deleted.",
        ["Ui.Done.DataKept"] = "Your countdowns and settings are still in {0}, ready if you install {product} again.",
        ["Ui.Done.Notes"] = "Please note:",
        ["Ui.Cancelled.Heading"] = "Setup was cancelled",
        ["Ui.Cancelled.Subtitle"] = "Select Close to exit Setup.",
        ["Ui.Cancelled.Title"] = "Nothing was changed",
        ["Ui.Cancelled.Body"] = "Setup put back everything it had changed. You can run it again at any time.",
        ["Ui.Badge.Success"] = "Succeeded",
        ["Ui.Badge.Attention"] = "Finished with notes",
        ["Ui.Badge.Stopped"] = "Stopped",

        // ===== Wizard: questions and messages =====
        ["Ui.Dialog.Back"] = "Go back",
        ["Ui.Dialog.OK"] = "OK",
        ["Ui.CloseApp.Heading"] = "Close {product}?",
        ["Ui.CloseApp.Text"] = "{product} is running. Setup will ask it to save your countdowns and close, and will end it if it hasn't closed after a few seconds.",
        ["Ui.CloseApp.Confirm"] = "Close it and continue",
        ["Ui.Downgrade.Heading"] = "Replace a newer version?",
        ["Ui.Downgrade.Text"] = "Version {0} is installed, which is newer than this setup ({1}). Your countdowns and settings are kept, but anything added after version {1} will be gone.",
        ["Ui.Downgrade.Confirm"] = "Install version {0}",
        ["Ui.RemoveData.Heading"] = "Delete your countdowns, settings and logs?",
        ["Ui.RemoveData.Text"] = "Everything in {0} and {1} will be deleted. This can't be undone.",
        ["Ui.RemoveData.Confirm"] = "Uninstall and delete",
        ["Ui.StopInstall.Heading"] = "Stop the installation?",
        ["Ui.StopInstall.Text"] = "Setup will undo what it has done so far and leave everything as it was.",
        ["Ui.StopInstall.Confirm"] = "Stop installing",
        ["Ui.StopInstall.Continue"] = "Keep installing",
        ["Ui.StopInstall.TooLate.Heading"] = "It's too late to stop",
        ["Ui.StopInstall.TooLate.Text"] = "Setup has already put the new files in place and is finishing the installation. It'll be done in a moment.",
        ["Ui.Busy.Heading"] = "Setup is still working",
        ["Ui.Busy.Text"] = "Closing Setup now could leave {product} incomplete. You can close the window as soon as it has finished.",
        ["Ui.Failed.Install.Heading"] = "Setup couldn't finish",
        ["Ui.Failed.Uninstall.Heading"] = "Uninstall couldn't finish",
        ["Ui.Failed.Log"] = "Details are in the setup log: {0}"
    };

    internal static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        // ===== Engine: progress =====
        ["Progress.Checking"] = "正在检查安装环境",
        ["Progress.Checking.Detail"] = "正在校验安装包和目标文件夹。",
        ["Progress.ClosingApp"] = "正在关闭 {product}",
        ["Progress.ClosingApp.Detail"] = "正在请求运行中的应用保存并退出。",
        ["Progress.Unpacking"] = "正在解压程序文件",
        ["Progress.Unpacking.Detail"] = "正在解压 {0}",
        ["Progress.Replacing"] = "正在放置新文件",
        ["Progress.Replacing.Detail"] = "正在更新 {0} 中的文件。",
        ["Progress.Registering"] = "正在创建快捷方式",
        ["Progress.Registering.Detail"] = "正在将 {product} 添加到开始菜单和“设置 > 应用”。",
        ["Progress.RemovingOldCopy"] = "正在移除旧副本",
        ["Progress.RemovingOldCopy.Detail"] = "{product} 已安装到新文件夹，正在移除 {0} 中的旧文件。",
        ["Progress.Launching"] = "正在启动 {product}",
        ["Progress.Launching.Detail"] = "正在打开倒计时面板。",
        ["Progress.Installed"] = "安装完成",
        ["Progress.Installed.Detail"] = "{product} 已可以使用。",
        ["Progress.RollingBack"] = "正在撤销更改",
        ["Progress.RollingBack.Detail"] = "正在恢复原有文件。",
        ["Progress.Uninstall.Preparing"] = "正在准备卸载",
        ["Progress.Uninstall.Preparing.Detail"] = "正在检查 {0} 中的安装。",
        ["Progress.Uninstall.Removing"] = "正在删除程序文件",
        ["Progress.Uninstall.Removing.Detail"] = "正在删除 {0}",
        ["Progress.Uninstall.Shortcuts"] = "正在删除快捷方式",
        ["Progress.Uninstall.Shortcuts.Detail"] = "正在从开始菜单、桌面和“设置 > 应用”中移除 {product}。",
        ["Progress.Uninstall.Data"] = "正在删除本地数据",
        ["Progress.Uninstall.Data.Detail"] = "正在删除倒计时、设置和日志。",
        ["Progress.Uninstall.Done"] = "卸载完成",
        ["Progress.Uninstall.Done.Detail"] = "{product} 已移除。",

        // ===== Engine: errors =====
        ["Error.Title"] = "{product} 安装程序",
        ["Error.PayloadMissing"] = "此安装文件不包含程序文件。请下载完整的安装程序后重新运行。",
        ["Error.PayloadCorrupt"] = "安装文件已损坏（{0}）。请重新下载安装程序。",
        ["Error.InvalidDirectory"] = "“{0}”不是有效的文件夹。请输入完整路径，例如 {1}。",
        ["Error.ProtectedDirectory"] = "{0} 是系统文件夹或个人文件夹。请选择单独的文件夹，例如 {1}。",
        ["Error.UnsupportedDrive"] = "{0} 位于网络驱动器、可移动驱动器或非 NTFS 驱动器上，无法保护其中的程序文件。请选择本地 NTFS 驱动器上的文件夹。",
        ["Error.DirectoryNotEmpty"] = "{0} 中已有其他文件。请选择空文件夹或新文件夹，例如 {1}。",
        ["Error.DirectoryNotWritable"] = "安装程序无法写入 {0}。请选择您有权修改的文件夹。",
        ["Error.SecureFolderFailed"] = "安装程序无法限制对 {0} 的访问，这台电脑上的其他用户可能会修改程序文件。请选择位于您的用户文件夹中的位置。",
        ["Error.InsufficientSpace"] = "{0} 上的可用空间不足：需要 {1}，当前可用 {2}。",
        ["Error.AppRunning"] = "{product} 正在运行，需要先将其关闭才能继续。",
        ["Error.AppCouldNotBeClosed"] = "无法关闭 {product}。请通过托盘图标退出后重试。",
        ["Error.FilesInUse"] = "部分程序文件正在使用中：{0}。请关闭正在使用它们的程序后重试。",
        ["Error.NotInstalled"] = "当前账户未安装 {product}。",
        ["Error.UnverifiedInstall"] = "无法确认 {0} 是 {product} 的安装位置，因此未删除任何内容。",
        ["Error.Unexpected"] = "安装程序未能完成：{0}",
        ["Error.AnotherSetupRunning"] = "另一个 {product} 安装程序正在运行。请先完成它。",
        ["Error.UninstallerCopyFailed"] = "安装程序无法在 {0} 中准备卸载程序。请确保该驱动器有可用空间，然后重试。",
        ["Error.InvalidArguments"] = "无法识别的选项：{0}",

        // ===== Engine: warnings shown on the completion page =====
        ["Warning.ShortcutsFailed"] = "无法创建开始菜单和桌面快捷方式。您可以从 {0} 启动 {product}。",
        ["Warning.LaunchFailed"] = "{product} 已安装，但未能启动。请从开始菜单启动。",
        ["Warning.OldCopyKept"] = "无法完全移除 {0} 中的旧副本。您可以手动删除该文件夹。",
        ["Warning.UnknownFilesKept"] = "{0} 中有并非由安装程序安装的文件，因此保留了该文件夹。",
        ["Warning.RemovalPending"] = "{0} 中有少量文件仍在使用，将在安装程序关闭后或下次登录时删除。",
        ["Warning.DataNotRemoved"] = "无法删除 {0} 中的部分数据。您可以手动删除该文件夹。",
        ["Warning.RegistrationOnly"] = "程序文件夹 {0} 已不存在，因此仅移除了快捷方式和“设置 > 应用”中的条目。",

        // ===== Command line =====
        ["Usage"] =
            "用法：\"{0}\" [选项]\n\n" +
            "  --silent                  安装或卸载时不显示任何窗口。\n" +
            "  --install-dir=<文件夹>    安装到 <文件夹>（默认：{1}）。\n" +
            "  --launch                  静默安装完成后启动 {product}。\n" +
            "  --no-desktop-shortcut     不创建桌面快捷方式。\n" +
            "  --uninstall               卸载 {product}。\n" +
            "  --remove-data             与 --uninstall 一起使用时，同时删除倒计时、设置和日志。\n" +
            "  --log=<文件>              将安装日志写入 <文件>。\n" +
            "  --lang=en|zh-CN           选择安装程序的语言。\n\n" +
            "退出代码：0 成功，87 选项或文件夹无效，112 磁盘空间不足，1602 已取消，" +
            "1603 失败，1605 未安装，1618 另一个安装程序正在运行，1620 安装文件已损坏。",

        // ===== Wizard: window, sidebar and buttons =====
        ["Ui.WindowTitle.Install"] = "{product} 安装程序",
        ["Ui.WindowTitle.Uninstall"] = "卸载 {product}",
        ["Ui.Sidebar.Version"] = "版本 {0}",
        ["Ui.Sidebar.Logo"] = "{product} 标志",
        ["Ui.Step.Options"] = "选择选项",
        ["Ui.Step.Install"] = "安装",
        ["Ui.Step.Uninstall"] = "卸载",
        ["Ui.Step.Finish"] = "完成",
        ["Ui.Step.Done"] = "{0}，已完成",
        ["Ui.Step.Current"] = "{0}，当前步骤",
        ["Ui.Step.Pending"] = "{0}，尚未开始",
        ["Ui.Button.Install"] = "安装(&I)",
        ["Ui.Button.Update"] = "更新(&U)",
        ["Ui.Button.Uninstall"] = "卸载(&U)",
        ["Ui.Button.Cancel"] = "取消",
        ["Ui.Button.Finish"] = "完成(&F)",
        ["Ui.Button.Close"] = "关闭(&C)",
        ["Ui.Button.Browse"] = "浏览(&B)…",

        // ===== Wizard: install options =====
        ["Ui.Install.Heading"] = "安装 {product}",
        ["Ui.Install.Subtitle"] = "选择安装位置，然后单击“安装”。",
        ["Ui.Update.Heading"] = "更新 {product}",
        ["Ui.Update.Subtitle"] = "您的倒计时和设置会保留。",
        ["Ui.Install.Intro"] = "{product} 以倒计时卡片的形式在桌面上显示您的截止日期。安装程序只为您的 Windows 账户安装，因此不需要管理员权限。",
        ["Ui.Card.Version"] = "版本",
        ["Ui.Card.InstalledVersion"] = "已安装版本",
        ["Ui.Card.Space"] = "所需空间",
        ["Ui.Card.Location"] = "位置",
        ["Ui.Card.UnknownVersion"] = "未知",
        ["Ui.Location.Label"] = "安装位置(&L)",
        ["Ui.Location.AccessibleName"] = "安装位置",
        ["Ui.Location.BrowseTitle"] = "选择 {product} 的安装文件夹",
        ["Ui.Location.SubfolderAdded"] = "安装程序在您选择的文件夹中添加了“{product}”文件夹，使程序文件集中存放。",
        ["Ui.Location.Empty"] = "请输入安装文件夹，例如 {0}。",
        ["Ui.Location.Upgrade"] = "此处已安装版本 {0}，将更新为 {1}。",
        ["Ui.Location.Reinstall"] = "此处已安装相同版本，安装程序将替换其文件。",
        ["Ui.Location.Downgrade"] = "此处已安装较新的版本（{0}）。继续安装会将其替换为版本 {1}。",
        ["Ui.Location.Move"] = "{product} 目前安装在 {0}。安装程序会将其移到此处，并删除旧副本。",
        ["Ui.Option.DesktopShortcut"] = "创建桌面快捷方式(&D)",
        ["Ui.Option.Launch"] = "安装完成后启动 {product}(&S)",
        ["Ui.Elevated"] = "安装程序正以管理员身份运行，但它并不需要管理员权限。{product} 将只为 {0} 安装。如果这不是您的账户，请关闭安装程序，然后以普通方式重新运行。",

        // ===== Wizard: uninstall options =====
        ["Ui.Uninstall.Heading"] = "卸载 {product}",
        ["Ui.Uninstall.Subtitle"] = "确认要删除的内容，然后单击“卸载”。",
        ["Ui.Uninstall.Intro"] = "安装程序将删除程序本身、快捷方式以及“设置 > 应用”中的条目。您自行放入程序文件夹的文件会保留。",
        ["Ui.Option.RemoveData"] = "同时删除我的倒计时、设置和日志(&D)",
        ["Ui.Option.RemoveData.Hint"] = "倒计时和设置保存在 {0}，日志保存在 {1}。如不勾选，日后重新安装时仍可继续使用。",

        // ===== Wizard: progress =====
        ["Ui.Progress.Install.Heading"] = "正在安装 {product}",
        ["Ui.Progress.Update.Heading"] = "正在更新 {product}",
        ["Ui.Progress.Uninstall.Heading"] = "正在卸载 {product}",
        ["Ui.Progress.Subtitle"] = "这只需要几秒钟，请不要关闭此窗口。",
        ["Ui.Progress.Starting"] = "正在准备……",
        ["Ui.Progress.Name"] = "进度",
        ["Ui.Progress.Cancelling"] = "正在取消，安装程序正在还原已做的更改……",

        // ===== Wizard: result =====
        ["Ui.Done.Install.Heading"] = "安装完成",
        ["Ui.Done.Update.Heading"] = "更新完成",
        ["Ui.Done.Uninstall.Heading"] = "卸载完成",
        ["Ui.Done.Subtitle"] = "单击“完成”关闭安装程序。",
        ["Ui.Done.Installed"] = "{product} 已安装",
        ["Ui.Done.Updated"] = "{product} 已更新到最新版本",
        ["Ui.Done.Location"] = "版本 {0} 位于 {1}。",
        ["Ui.Done.Launched"] = "它正在启动，面板稍后会出现在桌面上。",
        ["Ui.Done.StartMenu"] = "您可以从“开始”菜单启动它。",
        ["Ui.Done.Restart"] = "安装期间 {product} 已被关闭，请从“开始”菜单重新启动它。",
        ["Ui.Done.Removed"] = "{product} 已删除",
        ["Ui.Done.RemovalPending"] = "{product} 即将删除完毕",
        ["Ui.Done.Uninstalled"] = "{product} 已卸载",
        ["Ui.Done.DataDeleted"] = "您的倒计时、设置和日志已删除。",
        ["Ui.Done.DataKept"] = "您的倒计时和设置仍保存在 {0}，重新安装 {product} 后可继续使用。",
        ["Ui.Done.Notes"] = "请注意：",
        ["Ui.Cancelled.Heading"] = "安装已取消",
        ["Ui.Cancelled.Subtitle"] = "单击“关闭”退出安装程序。",
        ["Ui.Cancelled.Title"] = "未做任何更改",
        ["Ui.Cancelled.Body"] = "安装程序已还原所做的全部更改。您可以随时重新运行安装程序。",
        ["Ui.Badge.Success"] = "成功",
        ["Ui.Badge.Attention"] = "已完成，但有需要注意的事项",
        ["Ui.Badge.Stopped"] = "已停止",

        // ===== Wizard: questions and messages =====
        ["Ui.Dialog.Back"] = "返回",
        ["Ui.Dialog.OK"] = "确定",
        ["Ui.CloseApp.Heading"] = "关闭 {product}？",
        ["Ui.CloseApp.Text"] = "{product} 正在运行。安装程序会请它保存倒计时并退出；如果几秒钟后仍未退出，将强制结束。",
        ["Ui.CloseApp.Confirm"] = "关闭并继续",
        ["Ui.Downgrade.Heading"] = "替换为较旧的版本？",
        ["Ui.Downgrade.Text"] = "已安装的版本 {0} 比此安装程序（{1}）更新。您的倒计时和设置会保留，但版本 {1} 之后新增的内容将不再可用。",
        ["Ui.Downgrade.Confirm"] = "安装版本 {0}",
        ["Ui.RemoveData.Heading"] = "删除您的倒计时、设置和日志？",
        ["Ui.RemoveData.Text"] = "{0} 和 {1} 中的所有内容都将被删除，且无法恢复。",
        ["Ui.RemoveData.Confirm"] = "卸载并删除",
        ["Ui.StopInstall.Heading"] = "停止安装？",
        ["Ui.StopInstall.Text"] = "安装程序会撤销目前所做的操作，使一切保持原样。",
        ["Ui.StopInstall.Confirm"] = "停止安装",
        ["Ui.StopInstall.Continue"] = "继续安装",
        ["Ui.StopInstall.TooLate.Heading"] = "已无法停止",
        ["Ui.StopInstall.TooLate.Text"] = "安装程序已放置好新文件，正在完成安装，马上就好。",
        ["Ui.Busy.Heading"] = "安装程序仍在运行",
        ["Ui.Busy.Text"] = "现在关闭可能导致 {product} 处于不完整的状态。完成后即可关闭此窗口。",
        ["Ui.Failed.Install.Heading"] = "安装程序未能完成",
        ["Ui.Failed.Uninstall.Heading"] = "卸载未能完成",
        ["Ui.Failed.Log"] = "详细信息请查看安装日志：{0}"
    };
}
