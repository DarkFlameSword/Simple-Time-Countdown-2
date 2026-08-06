using System.ComponentModel;
using System.Globalization;

namespace TimeCountdown.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _translations;
    private string _currentLanguageCode = "en";

    private LocalizationService()
    {
        _translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = BuildEnglish(),
            ["zh-CN"] = BuildChinese()
        };
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguageCode => _currentLanguageCode;

    public string this[string key]
    {
        get
        {
            var normalized = NormalizeLanguageCode(_currentLanguageCode);
            if (_translations.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_translations["en"].TryGetValue(key, out var fallback))
            {
                return fallback;
            }

            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, this[key], args);
    }

    public void SetLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        if (string.Equals(_currentLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
        {
            ApplyCulture(normalized);
            return;
        }

        _currentLanguageCode = normalized;
        ApplyCulture(normalized);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageCode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        return string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en";
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = new CultureInfo(languageCode == "zh-CN" ? "zh-CN" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static IReadOnlyDictionary<string, string> BuildEnglish()
    {
        return new Dictionary<string, string>
        {
            ["App.Name"] = "Simple Time Countdown",
            ["Window.Main.Title"] = "Simple Time Countdown",
            ["Main.AppTitle"] = "Simple Time Countdown",
            ["Main.Subtitle"] = "Floating desktop deadlines for Windows 11",
            ["Main.Button.New"] = "New",
            ["Main.Button.Settings"] = "Settings",
            ["Main.Button.Hide"] = "Hide",
            ["Main.Button.Exit"] = "Exit",
            ["Main.Search.Tooltip"] = "Search by title, subtitle, or tags",
            ["Main.Empty"] = "No countdowns match the current search or filter.",
            ["Main.Card.Pinned"] = "PINNED",
            ["Common.Edit"] = "Edit",
            ["Common.Delete"] = "Delete",
            ["Common.Close"] = "Close",
            ["Common.Cancel"] = "Cancel",
            ["Common.Save"] = "Save",
            ["Summary.VisibleTotal"] = "{0} visible | {1} total",
            ["Footer.LocalTime"] = "Local time",
            ["Status.Due"] = "DUE",
            ["Status.Today"] = "TODAY",
            ["Status.Soon"] = "SOON",
            ["Status.Safe"] = "SAFE",
            ["Countdown.FallbackSubtitle"] = "Countdown item",
            ["Countdown.LeftSuffix"] = "{0} left",
            ["Countdown.TargetPassed"] = "The target time has passed",
            ["Countdown.Deadline"] = "Deadline | {0:yyyy-MM-dd HH:mm}",
            ["Time.Days"] = "{0} days",
            ["Time.Hours"] = "{0} hours",
            ["Time.Minutes"] = "{0} minutes",
            ["Time.Seconds"] = "{0} seconds",
            ["Notification.DueIn"] = "{0} is due in {1}.",
            ["Notification.Reached"] = "{0} has reached its deadline.",
            ["Message.DeleteTitle"] = "Remove countdown",
            ["Message.DeletePrompt"] = "Delete \"{0}\"?",
            ["Message.DesktopLayerUnavailableTitle"] = "Desktop layer unavailable",
            ["Message.DesktopLayerUnavailableBody"] = "Desktop layer mode could not be applied, so the panel was restored to the normal floating window mode.",
            ["Tray.ShowPanel"] = "Show panel",
            ["Tray.AddCountdown"] = "Add countdown",
            ["Tray.Settings"] = "Settings",
            ["Tray.AlwaysOnTop"] = "Always on top",
            ["Tray.Uninstall"] = "Uninstall",
            ["Tray.Exit"] = "Exit",
            ["Window.Settings.Title"] = "Settings",
            ["Settings.Title"] = "Settings",
            ["Settings.Subtitle"] = "Manage how the floating countdown panel behaves on your desktop.",
            ["Settings.Display"] = "Display",
            ["Settings.AlwaysOnTop"] = "Always on top",
            ["Settings.DesktopLayer"] = "Desktop layer",
            ["Settings.DesktopLayerHint"] = "Keeps the panel down in the desktop layer area. If the system cannot apply it, the app will automatically fall back to the normal floating mode.",
            ["Settings.PanelOpacity"] = "Panel opacity",
            ["Settings.BehaviorDefaults"] = "Behavior And Defaults",
            ["Settings.Language"] = "Language",
            ["Settings.Startup"] = "Launch at startup",
            ["Settings.HideToTray"] = "Hide to tray when the panel is closed",
            ["Settings.StatusThresholds"] = "Status thresholds (days from now)",
            ["Settings.StatusThresholdHint"] = "The app normalizes values automatically to keep Overdue < Urgent < Soon < Safe.",
            ["Settings.Threshold.Overdue"] = "Overdue if earlier than",
            ["Settings.Threshold.Today"] = "Urgent if earlier than",
            ["Settings.Threshold.Soon"] = "Soon if earlier than",
            ["Settings.Threshold.Safe"] = "Safe if later than",
            ["Settings.DayUnit"] = "days",
            ["Settings.DefaultReminder"] = "Default reminder",
            ["Settings.DefaultTimeZone"] = "Default time zone",
            ["Settings.PanelPlacement"] = "Panel placement",
            ["Settings.PlacementHint"] = "Window size and location are remembered automatically.",
            ["Settings.ResetPlacement"] = "Reset panel position",
            ["Window.Editor.Title"] = "Countdown details",
            ["Window.Editor.NewTitle"] = "New Countdown",
            ["Window.Editor.EditTitle"] = "Edit Countdown",
            ["Editor.Title"] = "Countdown details",
            ["Editor.Field.Title"] = "Title",
            ["Editor.Field.Subtitle"] = "Subtitle",
            ["Editor.Field.Date"] = "Date",
            ["Editor.Field.Hour"] = "Hour",
            ["Editor.Field.Minute"] = "Minute",
            ["Editor.Field.TimeZone"] = "Time zone",
            ["Editor.Field.Tags"] = "Tags (comma separated)",
            ["Editor.Field.Reminder"] = "Reminder",
            ["Editor.Field.Pin"] = "Pin this item",
            ["Reminder.None"] = "No reminder",
            ["Reminder.15m"] = "15 minutes before",
            ["Reminder.1h"] = "1 hour before",
            ["Reminder.1d"] = "1 day before",
            ["Reminder.3d"] = "3 days before",
            ["Language.English"] = "English",
            ["Language.Chinese"] = "中文",
            ["Validation.MissingTitle.Title"] = "Missing title",
            ["Validation.MissingTitle.Body"] = "Please enter a title.",
            ["Validation.MissingDate.Title"] = "Missing date",
            ["Validation.MissingDate.Body"] = "Please choose a target date.",
            ["Validation.MissingTime.Title"] = "Missing time",
            ["Validation.MissingTime.Body"] = "Please choose a valid time.",
            ["Validation.MissingTimeZone.Title"] = "Missing time zone",
            ["Validation.MissingTimeZone.Body"] = "Please choose a time zone.",
            ["Validation.MissingReminder.Title"] = "Missing reminder",
            ["Validation.MissingReminder.Body"] = "Please choose a reminder option.",
            ["Validation.InvalidTime.Title"] = "Invalid time",
            ["Validation.InvalidTime.Body"] = "The selected time does not exist in the chosen time zone because of a daylight saving transition. Please choose a different time.",

            // ===== Casebook UI (English) =====
            ["Common.Dismiss"] = "DISMISS",
            ["Common.Leave"] = "LEAVE",
            ["Common.Inscribe"] = "INSCRIBE",
            ["Common.Amend"] = "AMEND",
            ["Common.CommitSeal"] = "COMMIT & SEAL",
            ["Common.AmendSeal"] = "AMEND & SEAL",
            ["Editor.HeadingNew"] = "COMMIT A NEW ENTRY",
            ["Editor.HeadingEdit"] = "AMEND THIS ENTRY",
            ["Editor.Instructions"] = "— Inscribe the matter, the moment of reckoning, and any pertinent detail —",
            ["Editor.Label.Subject"] = "SUBJECT",
            ["Editor.Label.Note"] = "NOTE",
            ["Editor.Label.Marginalia"] = "MARGINALIA · separated by commas",
            ["Editor.Label.DueOn"] = "DUE ON",
            ["Editor.Label.Hour"] = "HR",
            ["Editor.Label.Minute"] = "MIN",
            ["Editor.Label.Disposition"] = "DISPOSITION OF HOURS",
            ["Editor.Label.Alert"] = "ALERT",
            ["Editor.Label.Pin"] = "Pin this entry to the top of the register",
            ["Settings.Heading"] = "HOUSEKEEPING",
            ["Settings.SectionDisplay"] = "I · Of the Casebook's Bearing",
            ["Settings.SectionBehavior"] = "II · Of Defaults & Ledger Conduct",
            ["Settings.SectionThresholds"] = "III · Of the Hour Thresholds",
            ["Settings.SectionPlacement"] = "IV · Of the Window's Lodging",
            ["Stamp.Top"] = "ARCHIVED",
            ["Stamp.Bottom"] = "221B · THE CASEBOOK",
            ["Status.Group.Overdue"] = "OVERDUE",
            ["Status.Group.Perilous"] = "PERILOUS",
            ["Status.Group.Urgent"] = "URGENT",
            ["Status.Group.Standing"] = "STANDING",
            ["Status.Group.Archived"] = "ARCHIVED",
            ["Casebook.Sign"] = "— S.H.",
            ["Casebook.Subtitle"] = "A REGISTER OF MATTERS PENDING & DUE",
            ["Casebook.Chrome"] = "— 221B · The Casebook —"
        };
    }

    private static IReadOnlyDictionary<string, string> BuildChinese()
    {
        return new Dictionary<string, string>
        {
            ["App.Name"] = "Simple Time Countdown",
            ["Window.Main.Title"] = "Simple Time Countdown",
            ["Main.AppTitle"] = "Simple Time Countdown",
            ["Main.Subtitle"] = "适用于 Windows 11 的桌面悬浮截止日期面板",
            ["Main.Button.New"] = "新建",
            ["Main.Button.Settings"] = "设置",
            ["Main.Button.Hide"] = "隐藏",
            ["Main.Button.Exit"] = "退出",
            ["Main.Search.Tooltip"] = "按标题、副标题或标签搜索",
            ["Main.Empty"] = "当前搜索或筛选条件下没有匹配的倒计时。",
            ["Main.Card.Pinned"] = "已置顶",
            ["Common.Edit"] = "编辑",
            ["Common.Delete"] = "删除",
            ["Common.Close"] = "关闭",
            ["Common.Cancel"] = "取消",
            ["Common.Save"] = "保存",
            ["Summary.VisibleTotal"] = "显示 {0} 项 | 共 {1} 项",
            ["Footer.LocalTime"] = "本地时间",
            ["Status.Due"] = "已到期",
            ["Status.Today"] = "今天",
            ["Status.Soon"] = "即将到期",
            ["Status.Safe"] = "正常",
            ["Countdown.FallbackSubtitle"] = "倒计时项目",
            ["Countdown.LeftSuffix"] = "剩余 {0}",
            ["Countdown.TargetPassed"] = "目标时间已过去",
            ["Countdown.Deadline"] = "截止时间 | {0:yyyy-MM-dd HH:mm}",
            ["Time.Days"] = "{0} 天",
            ["Time.Hours"] = "{0} 小时",
            ["Time.Minutes"] = "{0} 分钟",
            ["Time.Seconds"] = "{0} 秒",
            ["Notification.DueIn"] = "{0} 将在 {1} 后到期。",
            ["Notification.Reached"] = "{0} 已到达截止时间。",
            ["Message.DeleteTitle"] = "删除倒计时",
            ["Message.DeletePrompt"] = "确认删除“{0}”？",
            ["Message.DesktopLayerUnavailableTitle"] = "桌面层模式不可用",
            ["Message.DesktopLayerUnavailableBody"] = "当前系统无法应用桌面层模式，面板已自动恢复为普通悬浮窗口。",
            ["Tray.ShowPanel"] = "显示面板",
            ["Tray.AddCountdown"] = "新增倒计时",
            ["Tray.Settings"] = "设置",
            ["Tray.AlwaysOnTop"] = "始终置顶",
            ["Tray.Uninstall"] = "卸载",
            ["Tray.Exit"] = "退出",
            ["Window.Settings.Title"] = "设置",
            ["Settings.Title"] = "设置",
            ["Settings.Subtitle"] = "管理倒计时面板在桌面上的显示与交互方式。",
            ["Settings.Display"] = "显示",
            ["Settings.AlwaysOnTop"] = "始终置顶",
            ["Settings.DesktopLayer"] = "桌面层模式",
            ["Settings.DesktopLayerHint"] = "将面板压到桌面层区域显示。如果系统当前无法应用，会自动回退到普通悬浮模式。",
            ["Settings.PanelOpacity"] = "面板透明度",
            ["Settings.BehaviorDefaults"] = "行为与默认值",
            ["Settings.Language"] = "语言",
            ["Settings.Startup"] = "开机启动",
            ["Settings.HideToTray"] = "关闭面板时隐藏到托盘",
            ["Settings.StatusThresholds"] = "状态阈值（与当前时间的天数）",
            ["Settings.StatusThresholdHint"] = "应用会自动归一化阈值，保持 已到期 < 火烧眉毛 < 即将到期 < 正常。",
            ["Settings.Threshold.Overdue"] = "已到期阈值（小于）",
            ["Settings.Threshold.Today"] = "火烧眉毛阈值（小于）",
            ["Settings.Threshold.Soon"] = "即将到期阈值（小于）",
            ["Settings.Threshold.Safe"] = "正常阈值（大于等于）",
            ["Settings.DayUnit"] = "天",
            ["Settings.DefaultReminder"] = "默认提醒时间",
            ["Settings.DefaultTimeZone"] = "默认时区",
            ["Settings.PanelPlacement"] = "面板位置",
            ["Settings.PlacementHint"] = "窗口大小和位置会自动记忆。",
            ["Settings.ResetPlacement"] = "重置面板位置",
            ["Window.Editor.Title"] = "倒计时详情",
            ["Window.Editor.NewTitle"] = "新建倒计时",
            ["Window.Editor.EditTitle"] = "编辑倒计时",
            ["Editor.Title"] = "倒计时详情",
            ["Editor.Field.Title"] = "标题",
            ["Editor.Field.Subtitle"] = "副标题",
            ["Editor.Field.Date"] = "日期",
            ["Editor.Field.Hour"] = "小时",
            ["Editor.Field.Minute"] = "分钟",
            ["Editor.Field.TimeZone"] = "时区",
            ["Editor.Field.Tags"] = "标签（逗号分隔）",
            ["Editor.Field.Reminder"] = "提醒",
            ["Editor.Field.Pin"] = "置顶此项目",
            ["Reminder.None"] = "不提醒",
            ["Reminder.15m"] = "提前 15 分钟",
            ["Reminder.1h"] = "提前 1 小时",
            ["Reminder.1d"] = "提前 1 天",
            ["Reminder.3d"] = "提前 3 天",
            ["Language.English"] = "English",
            ["Language.Chinese"] = "中文",
            ["Validation.MissingTitle.Title"] = "缺少标题",
            ["Validation.MissingTitle.Body"] = "请输入标题。",
            ["Validation.MissingDate.Title"] = "缺少日期",
            ["Validation.MissingDate.Body"] = "请选择目标日期。",
            ["Validation.MissingTime.Title"] = "缺少时间",
            ["Validation.MissingTime.Body"] = "请选择有效时间。",
            ["Validation.MissingTimeZone.Title"] = "缺少时区",
            ["Validation.MissingTimeZone.Body"] = "请选择时区。",
            ["Validation.MissingReminder.Title"] = "缺少提醒",
            ["Validation.MissingReminder.Body"] = "请选择提醒选项。",
            ["Validation.InvalidTime.Title"] = "时间无效",
            ["Validation.InvalidTime.Body"] = "所选时间因夏令时切换在该时区内不存在，请选择其他时间。",

            // ===== 案录风 UI（中文） =====
            ["Common.Dismiss"] = "搁置",
            ["Common.Leave"] = "离开",
            ["Common.Inscribe"] = "录入",
            ["Common.Amend"] = "修订",
            ["Common.CommitSeal"] = "封存录入",
            ["Common.AmendSeal"] = "修订封存",
            ["Editor.HeadingNew"] = "录入新案",
            ["Editor.HeadingEdit"] = "修订案录",
            ["Editor.Instructions"] = "— Inscribe the matter, the moment of reckoning, and any pertinent detail —",
            ["Editor.Label.Subject"] = "事项",
            ["Editor.Label.Note"] = "备注",
            ["Editor.Label.Marginalia"] = "标签 · 以逗号分隔",
            ["Editor.Label.DueOn"] = "截止之日",
            ["Editor.Label.Hour"] = "时",
            ["Editor.Label.Minute"] = "分",
            ["Editor.Label.Disposition"] = "时区",
            ["Editor.Label.Alert"] = "提醒",
            ["Editor.Label.Pin"] = "将此案录置顶",
            ["Settings.Heading"] = "案务整顿",
            ["Settings.SectionDisplay"] = "其一 · 案录之外观",
            ["Settings.SectionBehavior"] = "其二 · 行为与默认",
            ["Settings.SectionThresholds"] = "其三 · 时辰之阈",
            ["Settings.SectionPlacement"] = "其四 · 窗扉之位",
            ["Stamp.Top"] = "已归档",
            ["Stamp.Bottom"] = "221B · 案录所",
            ["Status.Group.Overdue"] = "已过期",
            ["Status.Group.Perilous"] = "紧急",
            ["Status.Group.Urgent"] = "即将到期",
            ["Status.Group.Standing"] = "正常",
            ["Status.Group.Archived"] = "已归档",
            ["Casebook.Sign"] = "— 福尔摩斯",
            ["Casebook.Subtitle"] = "A REGISTER OF MATTERS PENDING & DUE",
            ["Casebook.Chrome"] = "— 221B · 案录 —"
        };
    }
}
