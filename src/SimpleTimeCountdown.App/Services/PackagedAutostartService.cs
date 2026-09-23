namespace TimeCountdown.Services;

/// <summary>
/// Autostart for the MSIX build. The package declares a StartupTask, which only Windows (Settings >
/// Apps > Startup) switches on or off; a packaged app's writes to the Run key land in its private
/// registry view and never start anything. So this implementation never touches the Run key: the
/// settings UI hides the check box for packaged builds and opens the Windows page instead
/// (<see cref="ViewModels.MainWindowViewModel.OpenStartupSettings"/>).
/// </summary>
public sealed class PackagedAutostartService : IAutostartService
{
    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled)
    {
        // Reported the way the settings UI expects a refused change, so a caller that shows the
        // check box anyway gets it reverted with an explanation rather than a false success.
        throw new UnauthorizedAccessException("A packaged app's startup task can only be changed in Windows Settings.");
    }
}
