namespace TimeCountdown.Setup;

/// <summary>
/// Moves files and remembers every move and every folder it had to create, so a failure part-way
/// through an install or uninstall can put the installation back exactly as it was. All moves
/// stay on one volume (work folders live inside the installation, and the one move out of it,
/// the installed uninstaller's, is made only when %TEMP% is on the same drive), so each is an
/// atomic rename that also works on an executable that is running.
/// </summary>
internal sealed class FileMoveJournal(InstallerLog log)
{
    private readonly Stack<(string From, string To)> _moves = new();
    private readonly Stack<string> _createdDirectories = new();

    public void Move(string from, string to)
    {
        EnsureDirectory(Path.GetDirectoryName(to)!);
        FileOperations.MoveFile(from, to);
        _moves.Push((from, to));
    }

    /// <summary>Undoes every move in reverse order. Returns false if something could not be put back.</summary>
    public bool RollBack()
    {
        var complete = true;
        while (_moves.TryPop(out var move))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.From)!);
                FileOperations.MoveFile(move.To, move.From);
            }
            catch (Exception ex) when (FileOperations.IsFileSystemException(ex))
            {
                complete = false;
                log.Error($"Could not move {move.To} back to {move.From}.", ex);
            }
        }

        while (_createdDirectories.TryPop(out var directory))
        {
            FileOperations.TryDeleteEmptyDirectory(directory, log);
        }

        return complete;
    }

    /// <summary>Forgets the journal once the operation has passed its point of no return.</summary>
    public void Commit()
    {
        _moves.Clear();
        _createdDirectories.Clear();
    }

    private void EnsureDirectory(string directory)
    {
        var missing = new Stack<string>();
        for (var current = directory; current is not null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
        {
            missing.Push(current);
        }

        while (missing.TryPop(out var path))
        {
            Directory.CreateDirectory(path);
            _createdDirectories.Push(path);
        }
    }
}
