namespace MichiChatbot.Infrastructure.Calendar;

/// <summary>
/// Pure overlap logic, deliberately separated from <see cref="GoogleCalendarService"/>'s live API
/// call so it's unit-testable without network access or credentials (plan.md's phase-2 verification
/// calls for "FreeBusy unit tests: overlap, timezone" — this is that logic, isolated).
/// </summary>
public static class AvailabilityChecker
{
    /// <summary>
    /// True if the requested [start, end) window overlaps ANY busy period. Half-open intervals: a
    /// requested slot that ends exactly when a busy period starts (or starts exactly when one ends)
    /// does NOT overlap — back-to-back bookings are allowed.
    /// DateTimeOffset comparisons are offset-aware, so busy periods and the request can each carry
    /// their own UTC offset (e.g. one from Google in UTC, one computed in Asia/Manila) and still
    /// compare correctly — no manual normalization to a common zone needed.
    /// </summary>
    public static bool Overlaps(
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods)
    {
        foreach (var (busyStart, busyEnd) in busyPeriods)
        {
            if (requestedStart < busyEnd && busyStart < requestedEnd)
                return true;
        }
        return false;
    }
}
