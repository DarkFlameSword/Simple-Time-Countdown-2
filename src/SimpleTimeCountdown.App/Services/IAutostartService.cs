namespace TimeCountdown.Services;

/// <summary>
/// Reads and changes whether the app starts when the user signs in.
/// Implementations report failures only as <see cref="UnauthorizedAccessException"/>,
/// <see cref="System.Security.SecurityException"/> or <see cref="System.IO.IOException"/>, which
/// the settings UI turns into a message and a reverted check box.
/// </summary>
public interface IAutostartService
{
    /// <summary>True when the app will actually start at the next sign-in. Never changes anything.</summary>
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
