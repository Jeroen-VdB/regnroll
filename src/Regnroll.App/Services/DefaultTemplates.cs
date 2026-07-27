namespace Regnroll.App.Services;

public static class TemplateKeys
{
    public const string NewSecret = "new-secret";
    public const string NewCertificate = "new-certificate";
    public const string Warning = "warning";
    public const string Expired = "expired";

    public static readonly string[] All = [NewSecret, NewCertificate, Warning, Expired];
}

public static class TemplateVariables
{
    public const string Url = "regnroll_url";
    public const string CredentialType = "credential_type";
    public const string ExpiryDate = "expiry_date";
    public const string ClientId = "client_id";
    public const string ClientName = "client_name";
    public const string TokenEndpoint = "token_endpoint";
}

/// <summary>Embedded default email templates; the templates table can override any of them per key.</summary>
public static class DefaultTemplates
{
    private const string ButtonStyle = "display:inline-block;padding:12px 24px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600";
    private const string Footer = """<p style="color:#6b7280;font-size:12px">— Regnroll automated credential rotation</p>""";

    public static bool TryGet(string key, out (string Subject, string HtmlBody) template)
    {
        switch (key)
        {
            case TemplateKeys.NewSecret:
                template = (
                    "Action required: retrieve the new client secret for {client_name}",
                    $$"""
                    <p>Hello,</p>
                    <p>A new client secret has been created for the application <strong>{client_name}</strong>
                    (client id <code>{client_id}</code>). The current secret expires on <strong>{expiry_date}</strong>,
                    so please switch to the new one before then.</p>
                    <p><a href="{regnroll_url}" style="{{ButtonStyle}}">Retrieve the new client secret</a></p>
                    <p>This link works <strong>once</strong> and expires automatically. Opening the link does nothing by
                    itself — the secret is only revealed after you press the reveal button on the page, and it can also be
                    retrieved from a script (the page shows how).</p>
                    <p>Your application keeps authenticating against the token endpoint:<br><code>{token_endpoint}</code></p>
                    <p>If the button does not work, copy this address into your browser:<br>{regnroll_url}</p>
                    {{Footer}}
                    """);
                return true;

            case TemplateKeys.NewCertificate:
                template = (
                    "Action required: upload a new certificate for {client_name}",
                    $$"""
                    <p>Hello,</p>
                    <p>The certificate of the application <strong>{client_name}</strong> (client id <code>{client_id}</code>)
                    expires on <strong>{expiry_date}</strong>. Please generate a new key pair and upload the
                    <strong>public part only</strong> (.cer or .pem — never the private key) using the secure link below.</p>
                    <p><a href="{regnroll_url}" style="{{ButtonStyle}}">Upload the new certificate</a></p>
                    <p>Your current certificate is <strong>not</strong> removed by the upload, so you can switch over at your
                    own pace before the expiry date.</p>
                    <p>Your application keeps authenticating against the token endpoint:<br><code>{token_endpoint}</code></p>
                    <p>If the button does not work, copy this address into your browser:<br>{regnroll_url}</p>
                    {{Footer}}
                    """);
                return true;

            case TemplateKeys.Warning:
                template = (
                    "Reminder: {credential_type} rotation for {client_name} is still pending",
                    $$"""
                    <p>Hello,</p>
                    <p>An earlier email with a secure Regnroll link was sent to this address to rotate the
                    <strong>{credential_type}</strong> of <strong>{client_name}</strong> (client id <code>{client_id}</code>).
                    That link has not been used yet, and the current {credential_type} expires on
                    <strong>{expiry_date}</strong>.</p>
                    <p>Please locate that earlier email and complete the rotation before the expiry date.
                    If you cannot find it, contact your IT support to have a new link issued.</p>
                    {{Footer}}
                    """);
                return true;

            case TemplateKeys.Expired:
                template = (
                    "The {credential_type} for {client_name} has expired and was removed",
                    $$"""
                    <p>Hello,</p>
                    <p>The <strong>{credential_type}</strong> of <strong>{client_name}</strong>
                    (client id <code>{client_id}</code>) expired on <strong>{expiry_date}</strong> and has been removed
                    automatically. A replacement should have been put in place through an earlier Regnroll notification.</p>
                    <p>If your integration is failing, contact your IT support to have a new {credential_type} issued.</p>
                    {{Footer}}
                    """);
                return true;

            default:
                template = default;
                return false;
        }
    }
}
