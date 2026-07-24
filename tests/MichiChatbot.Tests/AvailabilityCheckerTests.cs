using MichiChatbot.Infrastructure.Calendar;

namespace MichiChatbot.Tests;

/// <summary>
/// Pure overlap-logic tests for phase 2's check_availability tool (plan.md's own verification
/// bullet: "FreeBusy unit tests: overlap, timezone"). No network, no DB, no Google credentials —
/// exactly why AvailabilityChecker was split out of GoogleCalendarService in the first place.
/// </summary>
public sealed class AvailabilityCheckerTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    private static DateTimeOffset ManilaTime(int hour, int minute = 0) =>
        new(new DateTime(2026, 8, 1, hour, minute, 0), Manila.GetUtcOffset(new DateTime(2026, 8, 1)));

    [Fact]
    public void Requested_slot_fully_inside_a_busy_period_overlaps()
    {
        var busy = new[] { (ManilaTime(13), ManilaTime(17)) };
        Assert.True(AvailabilityChecker.Overlaps(ManilaTime(14), ManilaTime(15), busy));
    }

    [Fact]
    public void Requested_slot_fully_outside_all_busy_periods_does_not_overlap()
    {
        var busy = new[] { (ManilaTime(13), ManilaTime(17)) };
        Assert.False(AvailabilityChecker.Overlaps(ManilaTime(9), ManilaTime(11), busy));
    }

    [Fact]
    public void Partial_overlap_at_the_start_is_detected()
    {
        var busy = new[] { (ManilaTime(13), ManilaTime(17)) };
        // Requested 12:00-14:00 overlaps the last hour of the 13:00-17:00 busy period.
        Assert.True(AvailabilityChecker.Overlaps(ManilaTime(12), ManilaTime(14), busy));
    }

    [Fact]
    public void Adjacent_back_to_back_slots_do_not_overlap()
    {
        var busy = new[] { (ManilaTime(13), ManilaTime(17)) };
        // Requested slot starts exactly when the busy period ends — half-open interval, allowed.
        Assert.False(AvailabilityChecker.Overlaps(ManilaTime(17), ManilaTime(19), busy));
        Assert.False(AvailabilityChecker.Overlaps(ManilaTime(10), ManilaTime(13), busy));
    }

    [Fact]
    public void No_busy_periods_means_available()
    {
        Assert.False(AvailabilityChecker.Overlaps(ManilaTime(10), ManilaTime(12), []));
    }

    [Fact]
    public void Comparison_is_offset_aware_not_wall_clock()
    {
        // The same instant expressed in two different offsets (Manila +08:00 vs UTC +00:00) must
        // still be recognized as overlapping — DateTimeOffset compares absolute instants, so no
        // manual normalization to a shared timezone is needed before calling Overlaps.
        var busyStartUtc = ManilaTime(13).ToUniversalTime(); // same instant, offset +00:00
        var busyEndUtc = ManilaTime(17).ToUniversalTime();
        var busy = new[] { (busyStartUtc, busyEndUtc) };

        Assert.True(AvailabilityChecker.Overlaps(ManilaTime(14), ManilaTime(15), busy));
    }
}
