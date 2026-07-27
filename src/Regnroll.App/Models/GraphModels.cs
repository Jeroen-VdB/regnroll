namespace Regnroll.App.Models;

/// <summary>A password or key credential on an app registration, as read from Graph.</summary>
public record CredentialInfo(Guid KeyId, string? DisplayName, DateTimeOffset? StartDateTime, DateTimeOffset? EndDateTime)
{
    public bool IsExpired(DateTimeOffset now) => EndDateTime is { } end && end <= now;
}

/// <summary>An app registration the managed identity can manage.</summary>
public record AppRegistration(
    string ObjectId,
    string ClientId,
    string DisplayName,
    IReadOnlyList<CredentialInfo> Secrets,
    IReadOnlyList<CredentialInfo> Certificates)
{
    public IReadOnlyList<CredentialInfo> CredentialsOf(string type) =>
        type == CredentialTypes.Secret ? Secrets : Certificates;

    /// <summary>The latest-expiring credential of the given type; rotation decisions key off this.</summary>
    public CredentialInfo? LatestExpiring(string type) =>
        CredentialsOf(type)
            .Where(c => c.EndDateTime is not null)
            .OrderByDescending(c => c.EndDateTime)
            .FirstOrDefault();
}

/// <summary>Result of creating a client secret; SecretText is only ever held in memory.</summary>
public record CreatedSecret(Guid KeyId, string SecretText, DateTimeOffset ExpiresAt);
