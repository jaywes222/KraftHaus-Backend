using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ticketing.Domain.Tickets;

namespace Ticketing.Application.Tickets;

/// <summary>QR signing keys (<c>Ticketing:Qr</c>). Keys are secrets: environment / user-secrets outside Development.</summary>
public sealed class TicketQrOptions
{
    public const string SectionName = "Ticketing:Qr";

    /// <summary>Development / Testing only. Refused in any other environment.</summary>
    public const string DevelopmentKey = "a3JhZnRoYXVzLWRldmVsb3BtZW50LXFyLWtleS0wMDAwMDAwMDAwMDAw";

    /// <summary>Key that signs new codes. Old keys stay in <see cref="Keys" /> so issued codes keep scanning during a rotation.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Key id (1-8 letters/digits) → base64 secret of at least 32 bytes.</summary>
    public Dictionary<string, string> Keys { get; init; } = new(StringComparer.Ordinal);

    /// <summary>A code stops scanning this many hours after its date starts (covers all-night events).</summary>
    public int ValidityAfterStartHours { get; init; } = 48;

    /// <summary>
    ///     True (the product rule): the QR and code show from midnight in Nairobi on the ticket's date, and transfers
    ///     close then. False: the QR shows as soon as the ticket is issued and transfers close when the date starts.
    /// </summary>
    public bool LiveOnEventDay { get; init; } = true;
}

public sealed class TicketQrOptionsValidator(IHostEnvironment environment) : IValidateOptions<TicketQrOptions>
{
    public ValidateOptionsResult Validate(string? name, TicketQrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!options.Keys.TryGetValue(options.ActiveKeyId, out _))
        {
            failures.Add("Ticketing:Qr:ActiveKeyId must name one of Ticketing:Qr:Keys.");
        }

        foreach (var (id, encoded) in options.Keys)
        {
            if (id.Length is 0 or > 8 || !id.All(char.IsAsciiLetterOrDigit))
            {
                failures.Add($"Ticketing:Qr:Keys: key id '{id}' must be 1-8 letters or digits.");
            }

            if (!TryDecode(encoded, out var bytes) || bytes.Length < 32)
            {
                failures.Add($"Ticketing:Qr:Keys:{id} must be base64 of at least 32 random bytes.");
            }

            if (encoded == TicketQrOptions.DevelopmentKey && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                failures.Add($"Ticketing:Qr:Keys:{id} is the public development key; set a real secret.");
            }
        }

        if (options.ValidityAfterStartHours is < 1 or > 24 * 30)
        {
            failures.Add("Ticketing:Qr:ValidityAfterStartHours must be between 1 and 720.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>What a scanned QR says, before the database decides.</summary>
public sealed record QrContent(Guid TicketId, Guid EventDateId, string Code);

/// <summary>
///     Ticket QR codes (Spec 08 §8.3): <c>KH1.&lt;key&gt;.&lt;payload&gt;.&lt;mac&gt;</c>, payload = ticket id, date id,
///     expiry and the ticket's current code, MAC = HMAC-SHA256 (first 16 bytes). The signature rejects forgeries
///     without a database read; the code ties the QR to the current holder, so a transfer (new code) kills screenshots
///     of the old one.
/// </summary>
public sealed class TicketQrCodec(IOptions<TicketQrOptions> options)
{
    private const string Prefix = "KH1";
    private const int MacLength = 16;
    private const int PayloadLength = 16 + 16 + 8 + Ticket.CodeLength;

    /// <summary>When the holder first sees the QR and code.</summary>
    public DateTimeOffset LiveAt(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return options.Value.LiveOnEventDay ? ticket.GoesLiveAt : ticket.IssuedAt;
    }

    /// <summary>The last moment (exclusive) a ticket can be transferred.</summary>
    public DateTimeOffset TransfersCloseAt(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return options.Value.LiveOnEventDay ? ticket.GoesLiveAt : ticket.DateStartsAt;
    }

    public string Issue(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var settings = options.Value;
        var expires = ticket.DateStartsAt.AddHours(settings.ValidityAfterStartHours).ToUnixTimeSeconds();

        Span<byte> payload = stackalloc byte[PayloadLength];
        ticket.Id.Value.TryWriteBytes(payload[..16], bigEndian: true, out _);
        ticket.EventDateId.TryWriteBytes(payload[16..32], bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(payload[32..40], expires);
        Encoding.ASCII.GetBytes(ticket.Code, payload[40..]);

        var mac = Mac(settings.ActiveKeyId, payload);

        return $"{Prefix}.{settings.ActiveKeyId}.{Base64Url(payload)}.{Base64Url(mac)}";
    }

    /// <summary><c>null</c> for anything malformed, forged, signed with an unknown key or expired.</summary>
    public QrContent? Read(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            return null;
        }

        var parts = value.Trim().Split('.');
        if (parts.Length != 4 || parts[0] != Prefix || !options.Value.Keys.ContainsKey(parts[1]))
        {
            return null;
        }

        var payload = FromBase64Url(parts[2]);
        var mac = FromBase64Url(parts[3]);

        if (payload is not { Length: PayloadLength } || mac is not { Length: MacLength } ||
            !CryptographicOperations.FixedTimeEquals(Mac(parts[1], payload), mac))
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(32, 8)));
        if (now > expires)
        {
            return null;
        }

        return new QrContent(
            new Guid(payload.AsSpan(0, 16), bigEndian: true),
            new Guid(payload.AsSpan(16, 16), bigEndian: true),
            Encoding.ASCII.GetString(payload, 40, Ticket.CodeLength));
    }

    private byte[] Mac(string keyId, ReadOnlySpan<byte> payload)
    {
        var key = Convert.FromBase64String(options.Value.Keys[keyId]);
        return HMACSHA256.HashData(key, payload)[..MacLength];
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };

        return TicketQrOptionsValidator.TryDecode(padded, out var bytes) ? bytes : null;
    }
}
