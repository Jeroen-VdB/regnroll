using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Regnroll.App.Services;

public enum CertValidationError
{
    None,
    Invalid,
    PrivateKeyMaterial,
    Expired,
    NotYetValid,
}

public record CertValidationResult(CertValidationError Error, X509Certificate2? Certificate)
{
    public bool IsValid => Error == CertValidationError.None && Certificate is not null;

    public string ErrorMessage => Error switch
    {
        CertValidationError.Invalid => "The uploaded content is not a parseable X.509 certificate. Upload the public part as PEM (-----BEGIN CERTIFICATE-----) or DER/CER (raw or base64).",
        CertValidationError.PrivateKeyMaterial => "The upload contains private key material (PFX/PKCS#12 or a PEM private key). Only the public certificate part must be submitted — never share your private key.",
        CertValidationError.Expired => "The uploaded certificate has already expired (NotAfter is in the past).",
        CertValidationError.NotYetValid => "The uploaded certificate is not yet valid (NotBefore is in the future).",
        _ => "",
    };
}

/// <summary>
/// Validates customer certificate uploads before anything touches Graph.
/// Accepts PEM text, raw DER bytes, or base64 of DER. Rejects private key material outright.
/// </summary>
public static class CertificateValidator
{
    private static readonly TimeSpan NotBeforeSkew = TimeSpan.FromMinutes(5);

    public static CertValidationResult Validate(string content, DateTimeOffset now)
    {
        var trimmed = content.Trim();
        X509Certificate2? cert;

        if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            if (trimmed.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                return new(CertValidationError.PrivateKeyMaterial, null);
            }

            try
            {
                cert = X509Certificate2.CreateFromPem(trimmed);
            }
            catch (CryptographicException)
            {
                return new(CertValidationError.Invalid, null);
            }
        }
        else
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(trimmed);
            }
            catch (FormatException)
            {
                return new(CertValidationError.Invalid, null);
            }

            try
            {
                cert = X509CertificateLoader.LoadCertificate(bytes);
            }
            catch (CryptographicException)
            {
                // Not a plain certificate — if it parses as PKCS#12 it carries (or is meant to carry) a private key.
                try
                {
                    using var pfx = X509CertificateLoader.LoadPkcs12(bytes, null);
                    return new(CertValidationError.PrivateKeyMaterial, null);
                }
                catch (CryptographicException)
                {
                    // A password-protected PFX also lands here; the magic bytes distinguish it from garbage.
                    return IsProbablyPkcs12(bytes)
                        ? new(CertValidationError.PrivateKeyMaterial, null)
                        : new(CertValidationError.Invalid, null);
                }
            }
        }

        if (cert.NotAfter.ToUniversalTime() <= now.UtcDateTime)
        {
            return new(CertValidationError.Expired, cert);
        }

        if (cert.NotBefore.ToUniversalTime() > now.UtcDateTime + NotBeforeSkew)
        {
            return new(CertValidationError.NotYetValid, cert);
        }

        return new(CertValidationError.None, cert);
    }

    /// <summary>PKCS#12 is an ASN.1 SEQUENCE whose first element is the integer version 3.</summary>
    private static bool IsProbablyPkcs12(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x30 && bytes.AsSpan(0, Math.Min(bytes.Length, 16)).IndexOf<byte>(0x02) >= 0;
}
