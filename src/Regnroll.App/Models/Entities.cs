using Azure;
using Azure.Data.Tables;

namespace Regnroll.App.Models;

public static class TableNames
{
    public const string AppRegs = "appregs";
    public const string Links = "links";
    public const string Templates = "templates";
}

public static class CredentialTypes
{
    public const string Secret = "secret";
    public const string Certificate = "certificate";
}

public static class LinkStatuses
{
    public const string Pending = "Pending";
    public const string Claimed = "Claimed";
    public const string Uploaded = "Uploaded";
}

/// <summary>A linked app registration. RowKey = client id (appId).</summary>
public class AppRegEntity : ITableEntity
{
    public const string Partition = "appreg";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = null!;
    public string ObjectId { get; set; } = null!;
    public string DisplayName { get; set; } = "";
    /// <summary>Semicolon-separated customer contact email addresses.</summary>
    public string ContactEmails { get; set; } = "";
    public int? RotateBeforeDaysOverride { get; set; }
    public int? WarnBeforeDaysOverride { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ClientId => RowKey;

    public string[] GetContacts() =>
        ContactEmails.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// A delivery (secret) or upload (certificate) link.
/// RowKey = lowercase hex SHA-256 of the raw link id, so a storage dump cannot reconstruct URLs.
/// The AES-GCM key is never stored anywhere; it only exists in the emailed URL fragment.
/// </summary>
public class LinkEntity : ITableEntity
{
    public const string Partition = "link";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = null!;
    /// <summary>CredentialTypes.Secret or CredentialTypes.Certificate.</summary>
    public string Type { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    /// <summary>Base64 of AES-256-GCM ciphertext with the 16-byte tag appended. Secret links only; removed on claim.</summary>
    public string? Ciphertext { get; set; }
    public string? Nonce { get; set; }
    public string Status { get; set; } = LinkStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>When the link was claimed (secret) or uploaded to (certificate).</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? WarnedAt { get; set; }
    /// <summary>Graph keyId of the newly created credential (secret links).</summary>
    public string? NewCredentialKeyId { get; set; }
    public DateTimeOffset? NewCredentialExpiresAt { get; set; }
    /// <summary>Expiry of the credential this link is rotating, if any; drives warn-before.</summary>
    public DateTimeOffset? OldCredentialExpiresAt { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
    public bool IsPending => Status == LinkStatuses.Pending;
}

/// <summary>An email template override. RowKey = template key. Absence means the embedded default applies.</summary>
public class TemplateEntity : ITableEntity
{
    public const string Partition = "template";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = null!;
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
