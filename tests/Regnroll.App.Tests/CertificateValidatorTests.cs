using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class CertificateValidatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static X509Certificate2 MakeCert(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=regnroll-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    [Fact]
    public void ValidDerBase64_IsAccepted()
    {
        using var cert = MakeCert(Now.AddDays(-1), Now.AddYears(1));
        var result = CertificateValidator.Validate(Convert.ToBase64String(cert.Export(X509ContentType.Cert)), Now);

        Assert.True(result.IsValid);
        Assert.Equal(cert.Thumbprint, result.Certificate!.Thumbprint);
    }

    [Fact]
    public void ValidPem_IsAccepted()
    {
        using var cert = MakeCert(Now.AddDays(-1), Now.AddYears(1));
        var result = CertificateValidator.Validate(cert.ExportCertificatePem(), Now);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Garbage_IsRejectedAsInvalid()
    {
        Assert.Equal(CertValidationError.Invalid, CertificateValidator.Validate("this is not a certificate", Now).Error);
        Assert.Equal(CertValidationError.Invalid, CertificateValidator.Validate(Convert.ToBase64String("hello world"u8.ToArray()), Now).Error);
    }

    [Fact]
    public void ExpiredCertificate_IsRejected()
    {
        using var cert = MakeCert(Now.AddYears(-2), Now.AddDays(-1));
        var result = CertificateValidator.Validate(cert.ExportCertificatePem(), Now);

        Assert.Equal(CertValidationError.Expired, result.Error);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public void NotYetValidCertificate_IsRejected()
    {
        using var cert = MakeCert(Now.AddDays(2), Now.AddYears(1));
        var result = CertificateValidator.Validate(cert.ExportCertificatePem(), Now);

        Assert.Equal(CertValidationError.NotYetValid, result.Error);
    }

    [Fact]
    public void PemPrivateKey_IsRejectedAsPrivateKeyMaterial()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();

        Assert.Equal(CertValidationError.PrivateKeyMaterial, CertificateValidator.Validate(pem, Now).Error);
    }

    [Fact]
    public void PemBundleContainingPrivateKey_IsRejected()
    {
        using var cert = MakeCert(Now.AddDays(-1), Now.AddYears(1));
        using var rsa = RSA.Create(2048);
        var bundle = cert.ExportCertificatePem() + "\n" + rsa.ExportRSAPrivateKeyPem();

        Assert.Equal(CertValidationError.PrivateKeyMaterial, CertificateValidator.Validate(bundle, Now).Error);
    }

    [Fact]
    public void PfxUpload_IsRejectedAsPrivateKeyMaterial()
    {
        using var cert = MakeCert(Now.AddDays(-1), Now.AddYears(1));
        var pfx = Convert.ToBase64String(cert.Export(X509ContentType.Pkcs12));

        Assert.Equal(CertValidationError.PrivateKeyMaterial, CertificateValidator.Validate(pfx, Now).Error);
    }
}
