using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrBook.Api.IntegrationTests.Contract.Settings;

/// <summary>
/// JSON options for deserializing API responses in the settings round-trips. The API
/// serializes enums as STRINGS (<c>JsonStringEnumConverter</c>, see Program.cs), so a
/// response DTO with an enum field (ChannelFeedDto.Channel, PropertyDto.Type,
/// PropertyCancellationSettingsDto.Model) can't be read with the default Web options
/// (which expect enum numbers) — it throws <c>JsonException ... Path: $.channel</c>.
/// Use these options for every <c>ReadFromJsonAsync</c>/<c>GetFromJsonAsync</c> of an
/// enum-bearing DTO.
/// </summary>
internal static class SettingsTestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
