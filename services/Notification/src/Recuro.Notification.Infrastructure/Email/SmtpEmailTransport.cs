using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Infrastructure.Email;

/// <summary>Bound from <c>Smtp</c>. Locally this points at Mailpit; in production at any SMTP relay the tenant chooses.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary><c>None</c>, <c>StartTls</c>, <c>SslOnConnect</c> or <c>Auto</c>.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Where the portal lives, so a call-to-action becomes an absolute link.</summary>
    public Uri PortalBaseUrl { get; set; } = new("http://localhost:5173/");
}

/// <summary>
/// Sends plain-text mail over SMTP with MailKit (MIT). Plain text only: template output is text, so
/// nothing an event carries can become HTML in someone's inbox.
/// </summary>
internal sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task<string> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mime = Build(message, settings.PortalBaseUrl);

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, ct);
        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
        return mime.MessageId ?? string.Empty;
    }

    internal static MimeMessage Build(EmailMessage message, Uri portalBaseUrl)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(message.FromName, message.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress ?? throw new InvalidOperationException("The email has no recipient address.")));
        mime.Subject = message.Subject;
        mime.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(new MailboxAddress(string.Empty, message.FromAddress).Domain);
        mime.Headers.Add("X-Recuro-Template", $"{message.TemplateKey}@{message.TemplateVersion}");

        var lines = new List<string>(message.Paragraphs);
        if (message.Cta is not null && message.Link is not null)
        {
            lines.Add($"{message.Cta}: {new Uri(portalBaseUrl, message.Link.TrimStart('/'))}");
        }

        lines.Add("—");
        lines.Add(message.Signature);
        mime.Body = new TextPart("plain") { Text = string.Join("\n\n", lines) };
        return mime;
    }
}
