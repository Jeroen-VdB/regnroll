using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Options;
using Regnroll.App.Options;

namespace Regnroll.App.Services;

public interface IEmailSender
{
    Task SendAsync(IReadOnlyList<string> to, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>
/// Azure Communication Services email. Prefers the managed identity (AcsEndpoint + credential);
/// AcsConnectionString is the documented fallback for environments where identity-based send is unavailable.
/// </summary>
public sealed class AcsEmailSender(IOptions<RegnrollOptions> options, TokenCredential credential) : IEmailSender
{
    private readonly Lazy<EmailClient> _client = new(() =>
    {
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.AcsConnectionString))
        {
            return new EmailClient(o.AcsConnectionString);
        }

        if (!string.IsNullOrWhiteSpace(o.AcsEndpoint))
        {
            return new EmailClient(new Uri(o.AcsEndpoint), credential);
        }

        throw new InvalidOperationException(
            "Email is not configured: set Regnroll__AcsEndpoint (managed identity) or Regnroll__AcsConnectionString.");
    });

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (to.Count == 0)
        {
            throw new InvalidOperationException("No recipient addresses configured for this app registration.");
        }

        var sender = options.Value.SenderAddress;
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new InvalidOperationException("Regnroll__SenderAddress is not configured.");
        }

        var message = new EmailMessage(
            sender,
            new EmailRecipients(to.Select(a => new EmailAddress(a)).ToList()),
            new EmailContent(subject) { Html = htmlBody });

        await _client.Value.SendAsync(WaitUntil.Completed, message, ct);
    }
}
