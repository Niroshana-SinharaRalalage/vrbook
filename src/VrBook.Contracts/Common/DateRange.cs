namespace VrBook.Contracts.Common;

/// <summary>
/// A half-open date range [Start, End) suitable for stays — End is the check-out date,
/// which is NOT a night spent. Use <see cref="Nights"/> for the count of overnight stays.
/// </summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public int Nights => End.DayNumber - Start.DayNumber;

    public bool Overlaps(DateRange other) => Start < other.End && other.Start < End;

    public bool Contains(DateOnly date) => date >= Start && date < End;

    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = Start; d < End; d = d.AddDays(1))
        {
            yield return d;
        }
    }

    public override string ToString() => $"{Start:yyyy-MM-dd} → {End:yyyy-MM-dd} ({Nights}n)";
}
