using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class TemplateServiceTests
{
    private readonly InMemoryTemplateOverrideStore _store = new();
    private readonly TemplateService _service;

    public TemplateServiceTests() => _service = new TemplateService(_store);

    [Fact]
    public async Task AllFourDefaultTemplates_Exist()
    {
        var all = await _service.ListAsync();

        Assert.Equal(4, all.Count);
        Assert.All(all, t => Assert.False(t.IsOverridden));
        Assert.Contains(all, t => t.Key == TemplateKeys.NewSecret);
        Assert.Contains(all, t => t.Key == TemplateKeys.NewCertificate);
        Assert.Contains(all, t => t.Key == TemplateKeys.Warning);
        Assert.Contains(all, t => t.Key == TemplateKeys.Expired);
    }

    [Fact]
    public void Render_SubstitutesAllDocumentedVariables()
    {
        var variables = new Dictionary<string, string>
        {
            [TemplateVariables.Url] = "https://x/s/abc#key",
            [TemplateVariables.CredentialType] = "secret",
            [TemplateVariables.ExpiryDate] = "2026-12-31",
            [TemplateVariables.ClientId] = "cid-1",
            [TemplateVariables.ClientName] = "My App",
            [TemplateVariables.TokenEndpoint] = "https://login.microsoftonline.com/t/oauth2/v2.0/token",
        };
        var text = "{regnroll_url}|{credential_type}|{expiry_date}|{client_id}|{client_name}|{token_endpoint}";

        Assert.Equal(
            "https://x/s/abc#key|secret|2026-12-31|cid-1|My App|https://login.microsoftonline.com/t/oauth2/v2.0/token",
            _service.Render(text, variables));
    }

    [Fact]
    public void Render_LeavesUnknownPlaceholdersVerbatim()
    {
        var rendered = _service.Render("Hello {client_name}, {not_a_variable} stays.", new Dictionary<string, string>
        {
            [TemplateVariables.ClientName] = "App",
        });

        Assert.Equal("Hello App, {not_a_variable} stays.", rendered);
    }

    [Fact]
    public async Task Override_IsAppliedAndResettable()
    {
        await _service.SaveOverrideAsync(TemplateKeys.Warning, "Custom subject {client_name}", "<p>custom {client_id}</p>");

        var overridden = await _service.GetAsync(TemplateKeys.Warning);
        Assert.True(overridden.IsOverridden);
        Assert.Equal("Custom subject {client_name}", overridden.Subject);

        await _service.ResetAsync(TemplateKeys.Warning);
        var reset = await _service.GetAsync(TemplateKeys.Warning);
        Assert.False(reset.IsOverridden);
        Assert.NotEqual("Custom subject {client_name}", reset.Subject);
    }

    [Fact]
    public async Task UnknownKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetAsync("nope"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveOverrideAsync("nope", "s", "b"));
    }

    [Fact]
    public void DefaultBodies_ContainTheirCoreVariables()
    {
        Assert.True(DefaultTemplates.TryGet(TemplateKeys.NewSecret, out var newSecret));
        Assert.Contains("{regnroll_url}", newSecret.HtmlBody);
        Assert.Contains("{client_id}", newSecret.HtmlBody);

        Assert.True(DefaultTemplates.TryGet(TemplateKeys.Warning, out var warning));
        Assert.Contains("{expiry_date}", warning.HtmlBody);
        Assert.DoesNotContain("{regnroll_url}", warning.HtmlBody);
    }
}
