using System.Net.Mail;

namespace Recuro.Admin.Application.Abstractions;

/// <summary>The one email check for accounts (workspace owners and the people they add), like <c>isValidEmail</c> in the client.</summary>
internal static class EmailAddresses
{
    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && MailAddress.TryCreate(email.Trim(), out var address)
        && address.Address == email.Trim()
        && address.Host.Contains('.', StringComparison.Ordinal);
}
