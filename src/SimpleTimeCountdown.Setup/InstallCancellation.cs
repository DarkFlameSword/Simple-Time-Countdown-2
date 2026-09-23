namespace TimeCountdown.Setup;

/// <summary>
/// Cancels an install for as long as the engine can still undo it. Once the new files are
/// committed there is no way back, so from then on <see cref="TryCancel"/> refuses and the wizard
/// never promises a rollback that will not happen. A plain <see cref="CancellationToken"/> cannot
/// say this: the engine could check it a moment before the user cancels and then finish anyway.
/// Here a lock makes the engine and the wizard agree on which came first.
/// </summary>
internal sealed class InstallCancellation : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private bool _pastPointOfNoReturn;

    public CancellationToken Token => _source.Token;

    public bool IsCancellationRequested => _source.IsCancellationRequested;

    /// <summary>True once the engine has started to commit the install; it can no longer be cancelled.</summary>
    public bool IsPastPointOfNoReturn
    {
        get
        {
            lock (_gate)
            {
                return _pastPointOfNoReturn;
            }
        }
    }

    /// <summary>Cancels the install; returns false when it is already past its point of no return.</summary>
    public bool TryCancel()
    {
        lock (_gate)
        {
            if (_pastPointOfNoReturn)
            {
                return false;
            }

            _source.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Called by the engine right before it commits: throws <see cref="OperationCanceledException"/>
    /// if the install was cancelled first, otherwise makes every later <see cref="TryCancel"/> fail.
    /// </summary>
    public void EnterPointOfNoReturn()
    {
        lock (_gate)
        {
            _source.Token.ThrowIfCancellationRequested();
            _pastPointOfNoReturn = true;
        }
    }

    public void Dispose() => _source.Dispose();
}
