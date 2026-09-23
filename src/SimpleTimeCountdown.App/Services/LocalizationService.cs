using System.ComponentModel;
using System.Globalization;

namespace TimeCountdown.Services;

/// <summary>
/// In-process string tables for the two UI languages, exposed to XAML through the indexer
/// (<c>{Binding [Key], Source={StaticResource Loc}}</c>) and to code through <see cref="Format"/>.
///
/// Only the UI culture follows the chosen language. The thread's formatting culture stays the
/// user's regional setting, so typed dates in the editor keep parsing the way Windows is set up
/// (a UK user's 03/04 stays 3 April); text the app composes itself is formatted with
/// <see cref="Culture"/> so it reads in the UI language.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string English = "en";
    public const string Chinese = "zh-CN";

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _translations;
    private string _currentLanguageCode = English;
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    private LocalizationService()
    {
        _translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [English] = BuildEnglish(),
            [Chinese] = BuildChinese()
        };
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguageCode => _currentLanguageCode;

    /// <summary>Culture matching the UI language, for text the app formats itself.</summary>
    public CultureInfo Culture => _culture;

    public bool IsChinese => _currentLanguageCode == Chinese;

    public string this[string key]
    {
        get
        {
            if (_translations[_currentLanguageCode].TryGetValue(key, out var value))
            {
                return value;
            }

            return _translations[English].TryGetValue(key, out var fallback) ? fallback : key;
        }
    }

    /// <summary>Formats the localized pattern for <paramref name="key"/> in the UI culture.</summary>
    public string Format(string key, params object?[] args)
    {
        return string.Format(_culture, this[key], args);
    }

    /// <summary>
    /// A span as its two most significant units ("3 days 1 hour", "1 分钟 5 秒"), with singular
    /// and plural forms taken from the tables.
    /// </summary>
    public string FormatDuration(TimeSpan span)
    {
        span = span.Duration();
        if (span.TotalDays >= 1)
        {
            return Format("Time.Pair", Unit("Day", (int)span.TotalDays), Unit("Hour", span.Hours));
        }

        return span.TotalHours >= 1
            ? Format("Time.Pair", Unit("Hour", (int)span.TotalHours), Unit("Minute", span.Minutes))
            : Format("Time.Pair", Unit("Minute", (int)span.TotalMinutes), Unit("Second", span.Seconds));

        string Unit(string unit, int count) => count == 1 ? this[$"Time.{unit}.One"] : Format($"Time.{unit}.Other", count);
    }

    /// <summary>Every key defined for <paramref name="languageCode"/>; used by the key-parity tests.</summary>
    public IReadOnlyCollection<string> GetKeys(string languageCode)
    {
        return _translations.TryGetValue(NormalizeLanguageCode(languageCode), out var map)
            ? map.Keys.ToList()
            : [];
    }

    /// <summary>The raw table value, without falling back to English; used by the tests.</summary>
    public string? GetRaw(string languageCode, string key)
    {
        return _translations.TryGetValue(NormalizeLanguageCode(languageCode), out var map) && map.TryGetValue(key, out var value)
            ? value
            : null;
    }

    public void SetLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        ApplyUiCulture(normalized);
        if (_currentLanguageCode == normalized)
        {
            return;
        }

        _currentLanguageCode = normalized;
        _culture = CultureInfo.GetCultureInfo(normalized == Chinese ? "zh-CN" : "en-US");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageCode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public static string NormalizeLanguageCode(string? languageCode)
    {
        return languageCode is not null &&
               (languageCode.Equals(Chinese, StringComparison.OrdinalIgnoreCase) ||
                languageCode.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                languageCode.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
            ? Chinese
            : English;
    }

    private static void ApplyUiCulture(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode == Chinese ? "zh-CN" : "en-US");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static IReadOnlyDictionary<string, string> BuildEnglish()
    {
        return new Dictionary<string, string>
        {
            // ===== App & windows =====
            ["App.Name"] = "Simple Time Countdown",
            ["Window.Main.Title"] = "Simple Time Countdown",
            ["Window.Editor.NewTitle"] = "New countdown",
            ["Window.Editor.EditTitle"] = "Edit countdown",
            ["Window.Settings.Title"] = "Settings",
            ["Language.English"] = "English",
            ["Language.Chinese"] = "中文",

            // ===== Main panel =====
            ["Main.Subtitle"] = "A REGISTER OF MATTERS PENDING & DUE",
            ["Main.Clock"] = "{0} · {1}",
            ["Format.Clock"] = "ddd d MMM, HH:mm:ss",
            ["Main.Search.Label"] = "Search countdowns",
            ["Main.Search.Tooltip"] = "Search (Ctrl+F)",
            ["Main.Search.Placeholder"] = "Search titles and notes",
            ["Main.Search.Clear"] = "Clear search (Esc)",
            ["Main.Button.New"] = "New countdown (Ctrl+N)",
            ["Main.Button.Settings"] = "Settings (Ctrl+,)",
            ["Main.Button.ShowArchive"] = "Show archive (Ctrl+E)",
            ["Main.Button.HideArchive"] = "Back to active countdowns (Ctrl+E)",
            ["Main.Button.Minimize"] = "Minimize",
            ["Main.Button.CloseToTray"] = "Close to tray",
            ["Main.Button.Exit"] = "Exit",
            ["Main.ArchiveBanner"] = "ARCHIVE",
            ["Main.List.Name"] = "Countdowns",
            ["Main.Empty.Active"] = "No countdowns yet. Choose + to add one.",
            ["Main.Empty.Archive"] = "Nothing has been archived yet.",
            ["Main.Empty.Search"] = "No countdowns match “{0}”.",
            ["Main.Summary"] = "{0} shown · {1} total",
            ["Main.SaveError"] = "Changes could not be saved. Retrying…",
            ["Main.LimitReached"] = "The panel holds up to {0:N0} countdowns. Archive or delete some to add more.",

            // ===== Countdown cards =====
            ["Card.Pinned"] = "PINNED",
            ["Status.Overdue"] = "OVERDUE",
            ["Status.Perilous"] = "PERILOUS",
            ["Status.Urgent"] = "URGENT",
            ["Status.Standing"] = "STANDING",
            ["Status.Archived"] = "ARCHIVED",
            ["Card.Unit.Days"] = "DAYS",
            ["Card.Unit.Hours"] = "HRS",
            ["Card.Unit.Minutes"] = "MIN",
            ["Card.Unit.Seconds"] = "SEC",
            ["Card.Due"] = "DUE {0}",
            ["Card.ArchivedAt"] = "ARCHIVED {0}",
            ["Format.CardDate"] = "d MMM yyyy · HH:mm",
            ["Card.Elapsed"] = "ELAPSED",
            ["Card.Action.Archive"] = "Archive",
            ["Card.Action.Restore"] = "Restore",
            ["Card.Action.Edit"] = "Edit",
            ["Card.Action.Delete"] = "Delete",
            ["Card.A11y.Active"] = "{0}. {1}. {2} left. Due {3}.",
            ["Card.A11y.Overdue"] = "{0}. Overdue by {2}. Was due {3}.",
            ["Card.A11y.Archived"] = "{0}. Archived. Was due {3}.",
            ["Card.A11y.Progress"] = "{0}% of the time elapsed",
            ["Card.A11y.Pinned"] = "Pinned",
            ["Time.Pair"] = "{0} {1}",
            ["Time.Day.One"] = "1 day",
            ["Time.Day.Other"] = "{0} days",
            ["Time.Hour.One"] = "1 hour",
            ["Time.Hour.Other"] = "{0} hours",
            ["Time.Minute.One"] = "1 minute",
            ["Time.Minute.Other"] = "{0} minutes",
            ["Time.Second.One"] = "1 second",
            ["Time.Second.Other"] = "{0} seconds",
            ["Stamp.Top"] = "ARCHIVED",
            ["Stamp.Bottom"] = "221B · THE CASEBOOK",
            ["Format.StampMonth"] = "MMM",
            ["Cigar.BurntOut"] = "finis",

            // ===== Notifications & messages =====
            ["Notification.DueIn"] = "Due in {1}: {0}",
            ["Notification.Reached"] = "Deadline reached: {0}",
            ["Notification.Summary"] = "{0} countdowns need attention",
            ["Message.DeleteTitle"] = "Delete countdown",
            ["Message.DeletePrompt"] = "Delete “{0}”? This cannot be undone.",
            ["Message.DesktopLayerUnavailableTitle"] = "Desktop layer unavailable",
            ["Message.DesktopLayerUnavailableBody"] = "Windows did not allow pinning the panel to the desktop layer, so it is back to a normal floating window.",
            ["Message.UninstallerMissing"] = "The uninstaller was not found in this installation. Uninstall the app from Settings > Apps instead.",
            ["Message.UninstallDescription"] = "Uninstall Simple Time Countdown",
            ["Error.Title"] = "Simple Time Countdown",
            ["Error.Unexpected"] = "Something went wrong, but the app can keep running and your countdowns are saved.\n\nIf this keeps happening, the log at\n{0}\nwill help diagnose it.",
            ["Error.Fatal"] = "Simple Time Countdown hit an error it cannot recover from and will close. Your countdowns have been saved.\n\nDetails were written to:\n{0}",
            ["Error.StateRecovered"] = "Your saved countdowns could not be read, so the last good copy was restored. The unreadable file was kept at:\n{0}",
            ["Error.StateReset"] = "Your saved countdowns could not be read and no backup was available, so the app started empty. The unreadable file was kept at:\n{0}",
            ["Error.AutostartFailed"] = "Windows did not allow changing the startup setting.",

            // ===== Tray =====
            ["Tray.ShowPanel"] = "Show panel",
            ["Tray.AddCountdown"] = "Add countdown",
            ["Tray.Settings"] = "Settings",
            ["Tray.AlwaysOnTop"] = "Always on top",
            ["Tray.Uninstall"] = "Uninstall",
            ["Tray.Exit"] = "Exit",

            // ===== Settings =====
            ["Settings.Heading"] = "HOUSEKEEPING",
            ["Settings.Subtitle"] = "Choose how the countdown panel looks and behaves on your desktop.",
            ["Settings.Section.Display"] = "I · Display",
            ["Settings.Section.Behavior"] = "II · Behaviour & defaults",
            ["Settings.Section.Thresholds"] = "III · Status thresholds",
            ["Settings.Section.Placement"] = "IV · Window position",
            ["Settings.AlwaysOnTop"] = "Keep the panel above other windows",
            ["Settings.DesktopLayer"] = "Keep the panel behind other windows",
            ["Settings.DesktopLayerHint"] = "The panel stays behind other windows. If Windows does not allow it, the panel returns to a normal floating window.",
            ["Settings.PanelOpacity"] = "Panel opacity",
            ["Settings.PanelOpacityHighContrast"] = "Opacity is fixed at 100% while a Windows contrast theme is on.",
            ["Settings.Language"] = "Language",
            ["Settings.Startup"] = "Launch at startup",
            ["Settings.StartupPackagedHint"] = "For this installation, Windows manages whether the app starts when you sign in.",
            ["Settings.StartupOpenSettings"] = "Open Windows startup settings",
            ["Settings.HideToTray"] = "Keep running in the tray when the panel is closed",
            ["Settings.DefaultReminder"] = "Default reminder",
            ["Settings.DefaultTimeZone"] = "Default time zone",
            ["Settings.ThresholdHint"] = "Countdowns due within the first limit are Perilous and within the second are Urgent; later ones are Standing. Passed deadlines are Overdue.",
            ["Settings.Threshold.Perilous"] = "Perilous when due within",
            ["Settings.Threshold.Urgent"] = "Urgent when due within",
            ["Settings.Days"] = "{0} days",
            ["Settings.Days.One"] = "1 day",
            ["Settings.PlacementHint"] = "The panel remembers its size and position.",
            ["Settings.ResetPlacement"] = "Reset panel position",
            ["Settings.Done"] = "DONE",
            ["Settings.Section.About"] = "V · About",
            ["Settings.About.Version"] = "Version",
            ["Settings.About.License"] = "Free software under the MIT License.",
            ["Settings.About.Notices"] = "Licence and third-party notices",
            ["Settings.About.ReportProblem"] = "Report a problem",
            ["Settings.About.OpenLogs"] = "Open the log folder",

            // ===== Editor =====
            ["Editor.HeadingNew"] = "COMMIT A NEW ENTRY",
            ["Editor.HeadingEdit"] = "AMEND THIS ENTRY",
            ["Editor.Instructions"] = "Name the matter, set its moment of reckoning, and add any pertinent detail.",
            ["Editor.Label.Title"] = "TITLE",
            ["Editor.Label.Note"] = "NOTE",
            ["Editor.Label.Date"] = "DUE DATE",
            ["Editor.Label.Hour"] = "HOUR",
            ["Editor.Label.Minute"] = "MINUTE",
            ["Editor.Label.TimeZone"] = "TIME ZONE",
            ["Editor.Label.Reminder"] = "REMINDER",
            ["Editor.Label.Pin"] = "Pin this countdown to the top of the list",
            ["Editor.Counter"] = "{0} / {1}",
            ["Editor.Save.New"] = "SAVE ENTRY",
            ["Editor.Save.Edit"] = "SAVE CHANGES",
            ["Editor.Cancel"] = "CANCEL",
            ["Validation.MissingTitle"] = "Enter a title.",
            ["Validation.MissingDate"] = "Choose a valid due date.",
            ["Validation.MissingTime"] = "Choose an hour and a minute.",
            ["Validation.MissingTimeZone"] = "Choose a time zone.",
            ["Validation.MissingReminder"] = "Choose a reminder.",
            ["Validation.InvalidTime"] = "That time is skipped by a daylight-saving change in this time zone. Choose another time.",
            ["Validation.DateOutOfRange"] = "Choose a date between {0} and {1}.",

            // ===== Main window (added) =====
            ["Main.Button.MinimizeToTray"] = "Minimize to tray",
            ["Card.Action.Pin"] = "Pin to top",
            ["Card.Action.Unpin"] = "Unpin",

            // ===== Editor dialog (added) =====
            ["Editor.Required"] = "required",
            ["Editor.AmbiguousTime.First"] = "This time occurs twice here when the clocks go back; the first occurrence ({0}) will be used.",
            ["Editor.AmbiguousTime.Kept"] = "This time occurs twice here when the clocks go back; the occurrence already saved ({0}) is kept.",
            ["Editor.CounterSpoken"] = "{0} of {1} characters used.",

            // ===== Infra (added) =====
            ["Error.StateTrimmed"] = "Some saved countdowns were longer than the app supports, so they were shortened or left out. The original file was kept at:\n{0}",
            ["Message.UninstallerFailed"] = "The uninstaller could not be started. Uninstall the app from Settings > Apps instead.",
            ["Error.UnexpectedUnsaved"] = "Something went wrong. The app can keep running, but your latest changes could not be saved.\n\nIf this keeps happening, the log at\n{0}\nwill help diagnose it.",
            ["Error.FatalUnsaved"] = "Simple Time Countdown hit an error it cannot recover from and will close. Your latest changes could not be saved; countdowns saved before them are not affected.\n\nDetails were written to:\n{0}",
            ["Error.StateRecoveredPending"] = "Your saved countdowns could not be read, so the last good copy was restored. The unreadable file could not be moved aside yet (another program may be using it). It has been left unchanged, and it will be moved to the following location before the app saves anything:\n{0}",
            ["Error.StateResetPending"] = "Your saved countdowns could not be read and no backup was available, so the app started empty. The unreadable file could not be moved aside yet (another program may be using it). It has been left unchanged, and it will be moved to the following location before the app saves anything:\n{0}",
            ["Error.StateTrimmedPending"] = "Some saved countdowns were longer than the app supports, so they were shortened or left out. The original file could not be copied yet (another program may be using it). It has been left unchanged, and it will be moved to the following location before the app saves anything:\n{0}",

            // ===== Option lists =====
            ["Reminder.None"] = "No reminder",
            ["Reminder.15m"] = "15 minutes before",
            ["Reminder.1h"] = "1 hour before",
            ["Reminder.1d"] = "1 day before",
            ["Reminder.3d"] = "3 days before"
        };
    }

    private static IReadOnlyDictionary<string, string> BuildChinese()
    {
        return new Dictionary<string, string>
        {
            // ===== App & windows =====
            ["App.Name"] = "Simple Time Countdown",
            ["Window.Main.Title"] = "Simple Time Countdown",
            ["Window.Editor.NewTitle"] = "新建倒计时",
            ["Window.Editor.EditTitle"] = "编辑倒计时",
            ["Window.Settings.Title"] = "设置",
            ["Language.English"] = "English",
            ["Language.Chinese"] = "中文",

            // ===== Main panel =====
            ["Main.Subtitle"] = "待办与到期事项名录",
            ["Main.Clock"] = "{0} · {1}",
            ["Format.Clock"] = "M月d日 dddd HH:mm:ss",
            ["Main.Search.Label"] = "搜索倒计时",
            ["Main.Search.Tooltip"] = "搜索 (Ctrl+F)",
            ["Main.Search.Placeholder"] = "搜索标题或备注",
            ["Main.Search.Clear"] = "清除搜索 (Esc)",
            ["Main.Button.New"] = "新建倒计时 (Ctrl+N)",
            ["Main.Button.Settings"] = "设置 (Ctrl+,)",
            ["Main.Button.ShowArchive"] = "查看归档 (Ctrl+E)",
            ["Main.Button.HideArchive"] = "返回进行中的倒计时 (Ctrl+E)",
            ["Main.Button.Minimize"] = "最小化",
            ["Main.Button.CloseToTray"] = "关闭到托盘",
            ["Main.Button.Exit"] = "退出",
            ["Main.ArchiveBanner"] = "归档",
            ["Main.List.Name"] = "倒计时列表",
            ["Main.Empty.Active"] = "还没有倒计时。点击 + 新建一个。",
            ["Main.Empty.Archive"] = "归档中还没有内容。",
            ["Main.Empty.Search"] = "没有与“{0}”匹配的倒计时。",
            ["Main.Summary"] = "显示 {0} 项 · 共 {1} 项",
            ["Main.SaveError"] = "更改未能保存，正在重试……",
            ["Main.LimitReached"] = "面板最多容纳 {0:N0} 个倒计时。请先归档或删除一些再添加。",

            // ===== Countdown cards =====
            ["Card.Pinned"] = "已置顶",
            ["Status.Overdue"] = "已过期",
            ["Status.Perilous"] = "紧急",
            ["Status.Urgent"] = "即将到期",
            ["Status.Standing"] = "正常",
            ["Status.Archived"] = "已归档",
            ["Card.Unit.Days"] = "天",
            ["Card.Unit.Hours"] = "时",
            ["Card.Unit.Minutes"] = "分",
            ["Card.Unit.Seconds"] = "秒",
            ["Card.Due"] = "截止 {0}",
            ["Card.ArchivedAt"] = "归档于 {0}",
            ["Format.CardDate"] = "yyyy年M月d日 HH:mm",
            ["Card.Elapsed"] = "已过去",
            ["Card.Action.Archive"] = "归档",
            ["Card.Action.Restore"] = "恢复",
            ["Card.Action.Edit"] = "编辑",
            ["Card.Action.Delete"] = "删除",
            ["Card.A11y.Active"] = "{0}。{1}。剩余 {2}。截止 {3}。",
            ["Card.A11y.Overdue"] = "{0}。已过期 {2}。截止于 {3}。",
            ["Card.A11y.Archived"] = "{0}。已归档。截止于 {3}。",
            ["Card.A11y.Progress"] = "时间已过去 {0}%",
            ["Card.A11y.Pinned"] = "已置顶",
            ["Time.Pair"] = "{0} {1}",
            ["Time.Day.One"] = "1 天",
            ["Time.Day.Other"] = "{0} 天",
            ["Time.Hour.One"] = "1 小时",
            ["Time.Hour.Other"] = "{0} 小时",
            ["Time.Minute.One"] = "1 分钟",
            ["Time.Minute.Other"] = "{0} 分钟",
            ["Time.Second.One"] = "1 秒",
            ["Time.Second.Other"] = "{0} 秒",
            ["Stamp.Top"] = "已归档",
            ["Stamp.Bottom"] = "221B · 案录所",
            ["Format.StampMonth"] = "M月",
            ["Cigar.BurntOut"] = "终",

            // ===== Notifications & messages =====
            ["Notification.DueIn"] = "{1}后到期：{0}",
            ["Notification.Reached"] = "已到截止时间：{0}",
            ["Notification.Summary"] = "{0} 个倒计时需要留意",
            ["Message.DeleteTitle"] = "删除倒计时",
            ["Message.DeletePrompt"] = "确定删除“{0}”吗？此操作无法撤销。",
            ["Message.DesktopLayerUnavailableTitle"] = "桌面层模式不可用",
            ["Message.DesktopLayerUnavailableBody"] = "Windows 未允许将面板固定到桌面层，面板已恢复为普通悬浮窗口。",
            ["Message.UninstallerMissing"] = "当前安装目录中未找到卸载程序。请改为通过“设置 > 应用”卸载。",
            ["Message.UninstallDescription"] = "卸载 Simple Time Countdown",
            ["Error.Title"] = "Simple Time Countdown",
            ["Error.Unexpected"] = "发生了错误，但应用可以继续运行，你的倒计时已保存。\n\n如果问题反复出现，以下日志有助于诊断：\n{0}",
            ["Error.Fatal"] = "Simple Time Countdown 遇到无法恢复的错误，即将关闭。你的倒计时已保存。\n\n详细信息已写入：\n{0}",
            ["Error.StateRecovered"] = "无法读取已保存的倒计时，已恢复最近一次完好的副本。无法读取的文件保留在：\n{0}",
            ["Error.StateReset"] = "无法读取已保存的倒计时，且没有可用的备份，应用已以空白状态启动。无法读取的文件保留在：\n{0}",
            ["Error.AutostartFailed"] = "Windows 不允许更改开机启动设置。",

            // ===== Tray =====
            ["Tray.ShowPanel"] = "显示面板",
            ["Tray.AddCountdown"] = "新建倒计时",
            ["Tray.Settings"] = "设置",
            ["Tray.AlwaysOnTop"] = "始终置顶",
            ["Tray.Uninstall"] = "卸载",
            ["Tray.Exit"] = "退出",

            // ===== Settings =====
            ["Settings.Heading"] = "案务整顿",
            ["Settings.Subtitle"] = "设置倒计时面板在桌面上的外观与行为。",
            ["Settings.Section.Display"] = "其一 · 显示",
            ["Settings.Section.Behavior"] = "其二 · 行为与默认值",
            ["Settings.Section.Thresholds"] = "其三 · 状态阈值",
            ["Settings.Section.Placement"] = "其四 · 窗口位置",
            ["Settings.AlwaysOnTop"] = "让面板始终位于其他窗口之上",
            ["Settings.DesktopLayer"] = "让面板停留在其他窗口之后",
            ["Settings.DesktopLayerHint"] = "面板会停留在其他窗口之后。如果 Windows 不允许，面板会恢复为普通悬浮窗口。",
            ["Settings.PanelOpacity"] = "面板不透明度",
            ["Settings.PanelOpacityHighContrast"] = "启用 Windows 对比度主题时，不透明度固定为 100%。",
            ["Settings.Language"] = "语言",
            ["Settings.Startup"] = "开机时启动",
            ["Settings.StartupPackagedHint"] = "此安装方式下，是否在登录时启动由 Windows 管理。",
            ["Settings.StartupOpenSettings"] = "打开 Windows 启动设置",
            ["Settings.HideToTray"] = "关闭面板后继续在托盘中运行",
            ["Settings.DefaultReminder"] = "默认提醒",
            ["Settings.DefaultTimeZone"] = "默认时区",
            ["Settings.ThresholdHint"] = "在第一个期限内到期的倒计时为“紧急”，在第二个期限内到期的为“即将到期”，更晚的为“正常”；已过截止时间的为“已过期”。",
            ["Settings.Threshold.Perilous"] = "“紧急”：距截止不足",
            ["Settings.Threshold.Urgent"] = "“即将到期”：距截止不足",
            ["Settings.Days"] = "{0} 天",
            ["Settings.Days.One"] = "1 天",
            ["Settings.PlacementHint"] = "面板会自动记住大小和位置。",
            ["Settings.ResetPlacement"] = "重置面板位置",
            ["Settings.Done"] = "完成",
            ["Settings.Section.About"] = "其五 · 关于",
            ["Settings.About.Version"] = "版本",
            ["Settings.About.License"] = "基于 MIT 许可证的自由软件。",
            ["Settings.About.Notices"] = "许可证与第三方声明",
            ["Settings.About.ReportProblem"] = "反馈问题",
            ["Settings.About.OpenLogs"] = "打开日志文件夹",

            // ===== Editor =====
            ["Editor.HeadingNew"] = "录入新案",
            ["Editor.HeadingEdit"] = "修订案录",
            ["Editor.Instructions"] = "写下事项，定下截止时刻，并补充相关细节。",
            ["Editor.Label.Title"] = "标题",
            ["Editor.Label.Note"] = "备注",
            ["Editor.Label.Date"] = "截止日期",
            ["Editor.Label.Hour"] = "时",
            ["Editor.Label.Minute"] = "分",
            ["Editor.Label.TimeZone"] = "时区",
            ["Editor.Label.Reminder"] = "提醒",
            ["Editor.Label.Pin"] = "将此倒计时置顶",
            ["Editor.Counter"] = "{0} / {1}",
            ["Editor.Save.New"] = "保存",
            ["Editor.Save.Edit"] = "保存修改",
            ["Editor.Cancel"] = "取消",
            ["Validation.MissingTitle"] = "请输入标题。",
            ["Validation.MissingDate"] = "请选择有效的截止日期。",
            ["Validation.MissingTime"] = "请选择时和分。",
            ["Validation.MissingTimeZone"] = "请选择时区。",
            ["Validation.MissingReminder"] = "请选择提醒方式。",
            ["Validation.InvalidTime"] = "该时区在此时刻因夏令时切换而不存在，请选择其他时间。",
            ["Validation.DateOutOfRange"] = "请选择 {0} 至 {1} 之间的日期。",

            // ===== Main window (added) =====
            ["Main.Button.MinimizeToTray"] = "最小化到托盘",
            ["Card.Action.Pin"] = "置顶",
            ["Card.Action.Unpin"] = "取消置顶",

            // ===== Editor dialog (added) =====
            ["Editor.Required"] = "必填",
            ["Editor.AmbiguousTime.First"] = "时钟回拨时，该时区会经过此时刻两次；将采用第一次（{0}）。",
            ["Editor.AmbiguousTime.Kept"] = "时钟回拨时，该时区会经过此时刻两次；将保留已保存的那一次（{0}）。",
            ["Editor.CounterSpoken"] = "已输入 {0} 个字符，上限 {1} 个。",

            // ===== Infra (added) =====
            ["Error.StateTrimmed"] = "部分已保存的倒计时超出了应用支持的长度或数量，已被截短或略去。原始文件保留在：\n{0}",
            ["Message.UninstallerFailed"] = "无法启动卸载程序。请改为通过“设置 > 应用”卸载。",
            ["Error.UnexpectedUnsaved"] = "发生了错误。应用可以继续运行，但最近的更改未能保存。\n\n如果问题反复出现，以下日志有助于诊断：\n{0}",
            ["Error.FatalUnsaved"] = "Simple Time Countdown 遇到无法恢复的错误，即将关闭。最近的更改未能保存；此前已保存的倒计时不受影响。\n\n详细信息已写入：\n{0}",
            ["Error.StateRecoveredPending"] = "无法读取已保存的倒计时，已恢复最近一次完好的副本。无法读取的文件暂时无法移走（可能有其他程序正在使用它）。该文件未被改动，应用在保存任何内容之前会先把它移到：\n{0}",
            ["Error.StateResetPending"] = "无法读取已保存的倒计时，且没有可用的备份，应用已以空白状态启动。无法读取的文件暂时无法移走（可能有其他程序正在使用它）。该文件未被改动，应用在保存任何内容之前会先把它移到：\n{0}",
            ["Error.StateTrimmedPending"] = "部分已保存的倒计时超出了应用支持的长度或数量，已被截短或略去。原始文件暂时无法复制（可能有其他程序正在使用它）。该文件未被改动，应用在保存任何内容之前会先把它移到：\n{0}",

            // ===== Option lists =====
            ["Reminder.None"] = "不提醒",
            ["Reminder.15m"] = "提前 15 分钟",
            ["Reminder.1h"] = "提前 1 小时",
            ["Reminder.1d"] = "提前 1 天",
            ["Reminder.3d"] = "提前 3 天"
        };
    }
}
