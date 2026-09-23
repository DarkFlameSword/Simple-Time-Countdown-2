namespace TimeCountdown.Models;

public sealed class AppSettings
{
    public bool AlwaysOnTop { get; set; } = true;

    public bool HideOnCloseToTray { get; set; } = true;

    public bool DesktopLayerEnabled { get; set; }

    public double PanelOpacity { get; set; } = 0.96;

    public bool ShowArchivedOnly { get; set; }

    public int DefaultReminderMinutesBefore { get; set; } = 24 * 60;

    public string DefaultTimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>Countdowns due within this many days are Perilous.</summary>
    public int TodayThresholdDays { get; set; } = CountdownThresholds.Default.PerilousDays;

    /// <summary>
    /// Countdowns due within this many days are Urgent; later ones are Standing. The name is
    /// kept from the three-threshold era so existing state files keep their value.
    /// </summary>
    public int SafeThresholdDays { get; set; } = CountdownThresholds.Default.UrgentDays;

    public string LanguageCode { get; set; } = "en";

    public double WindowLeft { get; set; } = double.NaN;

    public double WindowTop { get; set; } = double.NaN;

    public double WindowWidth { get; set; } = Services.WindowPlacement.DesignWidth;

    public double WindowHeight { get; set; } = Services.WindowPlacement.DefaultHeight;
}
