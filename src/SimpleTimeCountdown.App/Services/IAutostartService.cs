namespace TimeCountdown.Services;

public interface IAutostartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

