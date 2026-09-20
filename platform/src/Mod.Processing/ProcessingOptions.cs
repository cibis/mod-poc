namespace Mod.Processing;

internal record ProcessingOptions(
    int WindowGraceSeconds,
    int ProcessingDelayMs,
    int MappingCacheSeconds);

internal record RetentionOptions(
    int DedupeRetentionDays,
    int RawRetentionDays);

internal sealed class LeaderState
{
    private volatile bool _isLeader;
    public bool IsLeader => _isLeader;
    internal void Set(bool value) => _isLeader = value;
}
