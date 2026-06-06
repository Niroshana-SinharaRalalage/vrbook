using System.Security.Cryptography;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Generates user-facing booking references like "VRB-7K2X9A".
/// Crockford base32 alphabet (no I/L/O/U) so they're unambiguous over phone.
/// </summary>
public static class BookingReference
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate(int length = 6)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 4);
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }
        return $"VRB-{new string(chars)}";
    }
}
