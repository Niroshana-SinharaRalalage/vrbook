namespace VrBook.Contracts.Common;

/// <summary>
/// Cursor-paginated response envelope. Use for high-cardinality lists
/// (search, history, threads). For admin tables use <see cref="OffsetPagedResult{T}"/>.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    int? Total = null);

/// <summary>Offset-paged response envelope. See proposal §6.1.</summary>
public sealed record OffsetPagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int Size,
    int Total);
