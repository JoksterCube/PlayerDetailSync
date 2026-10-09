namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class PendingWrite
{
    internal string Path { get; }
    internal PlayerSnapshot Snapshot { get; }

    internal PendingWrite(string path, PlayerSnapshot snapshot)
    {
        Path = path;
        Snapshot = snapshot;
    }
}