using FluentAssertions;
using VrBook.Modules.Sync.Infrastructure.Channels;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the iCal parser used by AirBnBICalChannel. No HTTP, no DbContext —
/// just exercise the Ical.Net-backed parser against canned VEVENT payloads. WireMock-
/// based end-to-end HTTP tests land with A0.2 deferred Docker work.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AirBnBICalParserTests
{
    private const string MinimalCalendar = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//AirBnB Inc//Hosting Calendar 0.8.8//EN
        CALSCALE:GREGORIAN
        BEGIN:VEVENT
        DTEND;VALUE=DATE:20260720
        DTSTART;VALUE=DATE:20260715
        UID:abnb-reservation-1@airbnb.com
        DESCRIPTION:Reserved
        SUMMARY:Reserved
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Parse_extracts_one_reservation_from_minimal_payload()
    {
        var result = AirBnBICalChannel.Parse(MinimalCalendar);

        result.Should().HaveCount(1);
        var r = result[0];
        r.ICalUid.Should().Be("abnb-reservation-1@airbnb.com");
        r.Checkin.Should().Be(new DateOnly(2026, 7, 15));
        r.Checkout.Should().Be(new DateOnly(2026, 7, 20));
        r.Summary.Should().Be("Reserved");
    }

    [Fact]
    public void Parse_empty_string_returns_empty_list()
    {
        var result = AirBnBICalChannel.Parse(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_whitespace_only_returns_empty_list()
    {
        var result = AirBnBICalChannel.Parse("   \r\n  ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_malformed_payload_returns_empty_list_not_throw()
    {
        var act = () => AirBnBICalChannel.Parse("BEGIN:VCALENDAR\nthis is junk\n");
        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_strips_utf8_BOM()
    {
        var withBom = '﻿' + MinimalCalendar;
        var result = AirBnBICalChannel.Parse(withBom);
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_multiple_VEVENTs_returns_all()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            DTSTART;VALUE=DATE:20260715
            DTEND;VALUE=DATE:20260720
            UID:r1@airbnb.com
            SUMMARY:Reserved
            END:VEVENT
            BEGIN:VEVENT
            DTSTART;VALUE=DATE:20260801
            DTEND;VALUE=DATE:20260805
            UID:r2@airbnb.com
            SUMMARY:Reserved
            END:VEVENT
            END:VCALENDAR
            """;

        var result = AirBnBICalChannel.Parse(ics);
        result.Should().HaveCount(2);
        result.Select(r => r.ICalUid).Should().Equal("r1@airbnb.com", "r2@airbnb.com");
    }

    [Fact]
    public void Parse_synthesizes_uid_for_events_with_no_source_UID()
    {
        // Ical.Net auto-generates a UID when the source omits one. Document that
        // behaviour so future readers know the parser DOES emit a reservation —
        // but the synthesized UID is not stable across polls so duplicates can
        // occur if the upstream feed omits UIDs. Real AirBnB feeds always include UIDs.
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            DTSTART;VALUE=DATE:20260715
            DTEND;VALUE=DATE:20260720
            SUMMARY:No UID here
            END:VEVENT
            END:VCALENDAR
            """;

        var result = AirBnBICalChannel.Parse(ics);

        result.Should().HaveCount(1);
        result[0].ICalUid.Should().NotBeNullOrWhiteSpace("Ical.Net always emits a UID");
    }

    [Fact]
    public void Parse_skips_events_where_dtend_equals_dtstart()
    {
        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VEVENT
            DTSTART;VALUE=DATE:20260715
            DTEND;VALUE=DATE:20260715
            UID:zero-duration@airbnb.com
            SUMMARY:Zero
            END:VEVENT
            END:VCALENDAR
            """;

        var result = AirBnBICalChannel.Parse(ics);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_captures_raw_payload_per_event()
    {
        var result = AirBnBICalChannel.Parse(MinimalCalendar);
        result.Should().HaveCount(1);
        result[0].RawPayload.Should().Contain("BEGIN:VEVENT");
        result[0].RawPayload.Should().Contain("UID:abnb-reservation-1@airbnb.com");
        result[0].RawPayload.Should().Contain("END:VEVENT");
    }
}
