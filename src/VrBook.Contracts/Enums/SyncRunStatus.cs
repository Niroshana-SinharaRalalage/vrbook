namespace VrBook.Contracts.Enums;

public enum SyncRunStatus
{
    Success = 0,
    Partial = 1,
    Failed = 2,
}

/// <summary>Outcomes when an owner resolves a sync conflict from the admin UI.</summary>
public enum SyncConflictResolution
{
    Pending = 0,
    OwnerKeptDirect = 1,
    OwnerCancelledDirect = 2,
    AutoCancelled = 3,
    ManualOverride = 4,
}
