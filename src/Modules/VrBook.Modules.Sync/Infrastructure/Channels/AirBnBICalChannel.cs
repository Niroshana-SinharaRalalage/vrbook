using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.Serialization;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Sync.Infrastructure.Channels;

/// <summary>
/// AirBnB iCal channel adapter. Implements <see cref="IExternalChannel"/> for
/// <see cref="ChannelKind.AirBnb"/>: pulls the host calendar over HTTPS with
/// ETag / Last-Modified for cache friendliness, parses VEVENTs via Ical.Net,
/// and renders the outbound subscription URL.
///
/// AirBnB blocks every reserved date with a single VEVENT whose DTSTART = check-in
/// and DTEND = check-out. The SUMMARY is typically "Reserved" or
/// "Reserved (Not available)". UID is stable across polls.
/// </summary>
public sealed class AirBnBICalChannel(
    IHttpClientFactory httpClientFactory,
    ILogger<AirBnBICalChannel> logger) : IExternalChannel
{
    public const string HttpClientName = "AirBnBICal";

    public ChannelKind Kind => ChannelKind.AirBnb;

    public async Task<IReadOnlyList<ExternalReservationDto>> PullAsync(
        ChannelFeedConfig config,
        CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var req = new HttpRequestMessage(HttpMethod.Get, config.InboundUrl);
        if (!string.IsNullOrEmpty(config.ETag))
        {
            req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(config.ETag, isWeak: true));
        }
        if (config.LastModified.HasValue)
        {
            req.Headers.IfModifiedSince = config.LastModified.Value;
        }

        using var resp = await client.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotModified)
        {
            logger.LogInformation("Feed {FeedId} returned 304 Not Modified.", config.ChannelFeedId);
            return Array.Empty<ExternalReservationDto>();
        }
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        return Parse(body);
    }

    public Task<string> RenderOutboundFeedAsync(
        Guid propertyId,
        IReadOnlyList<OutboundReservation> reservations,
        CancellationToken ct = default)
    {
        var cal = new Calendar
        {
            ProductId = "-//VrBook//EN",
            Version = "2.0",
        };
        cal.Properties.Add(new Ical.Net.CalendarProperty("X-WR-CALNAME", $"VrBook bookings for {propertyId}"));

        foreach (var r in reservations)
        {
            cal.Events.Add(new CalendarEvent
            {
                Uid = r.Uid,
                Summary = r.Summary,
                Start = new Ical.Net.DataTypes.CalDateTime(r.Checkin.ToDateTime(TimeOnly.MinValue), tzId: "UTC") { HasTime = false },
                End = new Ical.Net.DataTypes.CalDateTime(r.Checkout.ToDateTime(TimeOnly.MinValue), tzId: "UTC") { HasTime = false },
                LastModified = new Ical.Net.DataTypes.CalDateTime(r.LastModified.UtcDateTime, tzId: "UTC"),
                Properties = { new Ical.Net.CalendarProperty("STATUS", r.IsTentative ? "TENTATIVE" : "CONFIRMED") },
            });
        }

        var serializer = new CalendarSerializer();
        var ics = serializer.SerializeToString(cal);
        return Task.FromResult(ics);
    }

    /// <summary>
    /// Internal pure parser exposed for unit tests — no DI, no HTTP.
    /// </summary>
    internal static IReadOnlyList<ExternalReservationDto> Parse(string icsBody)
    {
        if (string.IsNullOrWhiteSpace(icsBody))
        {
            return Array.Empty<ExternalReservationDto>();
        }
        // Strip BOM if present — some hosts return iCal with UTF-8 BOM.
        if (icsBody.Length > 0 && icsBody[0] == '﻿')
        {
            icsBody = icsBody[1..];
        }

        Calendar cal;
        try
        {
            cal = Calendar.Load(icsBody);
        }
        catch (Exception)
        {
            // Malformed feed — treat as empty rather than throw so a transient
            // upstream glitch doesn't tank the entire sync run.
            return Array.Empty<ExternalReservationDto>();
        }

        var results = new List<ExternalReservationDto>(cal.Events.Count);
        foreach (var ev in cal.Events)
        {
            if (ev.Start is null || ev.End is null || string.IsNullOrWhiteSpace(ev.Uid))
            {
                continue;
            }
            // AirBnB sends date-only for blocked ranges. Some hosts send datetime.
            // Convert UTC datetime to local DateOnly conservatively.
            var checkin = DateOnly.FromDateTime(ev.Start.AsUtc);
            var checkout = DateOnly.FromDateTime(ev.End.AsUtc);
            if (checkout <= checkin)
            {
                continue; // skip zero-duration garbage
            }
            results.Add(new ExternalReservationDto(
                ICalUid: ev.Uid,
                Checkin: checkin,
                Checkout: checkout,
                Summary: ev.Summary,
                RawPayload: ExtractRawVEvent(icsBody, ev.Uid)));
        }
        return results;
    }

    private static string ExtractRawVEvent(string fullIcs, string uid)
    {
        // Best-effort: find the BEGIN:VEVENT…END:VEVENT block whose UID matches.
        // Used purely for forensic / audit storage in external_reservations.raw_payload.
        var idx = 0;
        while (idx < fullIcs.Length)
        {
            var begin = fullIcs.IndexOf("BEGIN:VEVENT", idx, StringComparison.OrdinalIgnoreCase);
            if (begin < 0)
            {
                break;
            }
            var end = fullIcs.IndexOf("END:VEVENT", begin, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                break;
            }
            end += "END:VEVENT".Length;
            var block = fullIcs[begin..end];
            if (block.Contains($"UID:{uid}", StringComparison.OrdinalIgnoreCase))
            {
                return block;
            }
            idx = end;
        }
        return $"UID:{uid}";
    }
}
