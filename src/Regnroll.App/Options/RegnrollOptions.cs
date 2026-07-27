using System.ComponentModel.DataAnnotations;

namespace Regnroll.App.Options;

/// <summary>
/// Product configuration, bound from the "Regnroll" section (Regnroll__* app settings).
/// The lifecycle timer schedule is intentionally a flat setting (REGNROLL_TIMER_SCHEDULE)
/// because trigger attributes resolve %names% outside the options system.
/// </summary>
public class RegnrollOptions : IValidatableObject
{
    public const string SectionName = "Regnroll";

    public const string GraphModeOwnedBy = "OwnedBy";
    public const string GraphModeAll = "All";

    /// <summary>Base URL used when generating customer-facing links, e.g. https://regnroll-x.azurewebsites.net</summary>
    [Required]
    public string PublicBaseUrl { get; set; } = null!;

    /// <summary>Create a replacement credential this many days before the old one expires.</summary>
    [Range(1, 365)]
    public int RotateBeforeDays { get; set; } = 30;

    /// <summary>Send a reminder for unactioned links this many days before the old credential expires.</summary>
    [Range(1, 365)]
    public int WarnBeforeDays { get; set; } = 7;

    /// <summary>Validity of client secrets created by Regnroll.</summary>
    [Range(1, 730)]
    public int SecretValidityDays { get; set; } = 180;

    /// <summary>Maximum lifetime of a delivery/upload link (capped at the old credential's expiry when rotating).</summary>
    [Range(1, 365)]
    public int LinkTtlDays { get; set; } = 14;

    /// <summary>"OwnedBy" (default, least privilege: only app registrations owned by the managed identity) or "All" (tenant-wide, requires Application.ReadWrite.All).</summary>
    [RegularExpression("OwnedBy|All", ErrorMessage = "Regnroll__GraphMode must be 'OwnedBy' or 'All'.")]
    public string GraphMode { get; set; } = GraphModeOwnedBy;

    /// <summary>Object id of the managed identity's service principal; required for OwnedBy discovery.</summary>
    public string? ManagedIdentityPrincipalId { get; set; }

    /// <summary>Entra tenant id; used to render the {token_endpoint} template variable.</summary>
    public string? TenantId { get; set; }

    /// <summary>Table endpoint of the dedicated data storage account, e.g. https://account.table.core.windows.net (uses the managed identity).</summary>
    public string? DataTablesEndpoint { get; set; }

    /// <summary>Connection string alternative for local development (e.g. Azurite).</summary>
    public string? DataTablesConnectionString { get; set; }

    /// <summary>Azure Communication Services endpoint, e.g. https://acs-name.communication.azure.com (uses the managed identity).</summary>
    public string? AcsEndpoint { get; set; }

    /// <summary>Connection string fallback for ACS if identity-based email send is not available.</summary>
    public string? AcsConnectionString { get; set; }

    /// <summary>Sender address; defaults to the ACS managed domain DoNotReply address configured at deployment.</summary>
    public string? SenderAddress { get; set; }

    public bool UseTenantWideMode => string.Equals(GraphMode, GraphModeAll, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(DataTablesEndpoint) && string.IsNullOrWhiteSpace(DataTablesConnectionString))
        {
            yield return new ValidationResult(
                "Either Regnroll__DataTablesEndpoint or Regnroll__DataTablesConnectionString must be configured.",
                [nameof(DataTablesEndpoint)]);
        }
    }
}
