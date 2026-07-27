using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Regnroll.App.Models;

namespace Regnroll.App.Services;

public record EmailTemplate(string Key, string Subject, string HtmlBody, bool IsOverridden);

public interface ITemplateOverrideStore
{
    Task<TemplateEntity?> GetAsync(string key, CancellationToken ct = default);
    Task UpsertAsync(TemplateEntity entity, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

public sealed class TableTemplateOverrideStore(TableServiceClient tables) : ITemplateOverrideStore
{
    private readonly TableClient _table = tables.GetTableClient(TableNames.Templates);

    public async Task<TemplateEntity?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _table.GetEntityAsync<TemplateEntity>(TemplateEntity.Partition, key, cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync(TemplateEntity entity, CancellationToken ct = default) =>
        _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _table.DeleteEntityAsync(TemplateEntity.Partition, key, cancellationToken: ct);
    }
}

public interface ITemplateService
{
    Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);
    Task SaveOverrideAsync(string key, string subject, string htmlBody, CancellationToken ct = default);
    Task ResetAsync(string key, CancellationToken ct = default);
    string Render(string text, IReadOnlyDictionary<string, string> variables);
}

/// <summary>
/// Embedded defaults + table-stored overrides, applied without redeploy.
/// Rendering is plain {variable} substitution; unknown placeholders are left verbatim.
/// </summary>
public sealed partial class TemplateService(ITemplateOverrideStore store) : ITemplateService
{
    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex PlaceholderRegex();

    public async Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default)
    {
        if (!DefaultTemplates.TryGet(key, out var fallback))
        {
            throw new ArgumentException($"Unknown template key '{key}'.", nameof(key));
        }

        var overrideEntity = await store.GetAsync(key, ct);
        return overrideEntity is null
            ? new EmailTemplate(key, fallback.Subject, fallback.HtmlBody, IsOverridden: false)
            : new EmailTemplate(key, overrideEntity.Subject, overrideEntity.HtmlBody, IsOverridden: true);
    }

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<EmailTemplate>();
        foreach (var key in TemplateKeys.All)
        {
            result.Add(await GetAsync(key, ct));
        }

        return result;
    }

    public Task SaveOverrideAsync(string key, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!DefaultTemplates.TryGet(key, out _))
        {
            throw new ArgumentException($"Unknown template key '{key}'.", nameof(key));
        }

        return store.UpsertAsync(new TemplateEntity { RowKey = key, Subject = subject, HtmlBody = htmlBody }, ct);
    }

    public Task ResetAsync(string key, CancellationToken ct = default) => store.DeleteAsync(key, ct);

    public string Render(string text, IReadOnlyDictionary<string, string> variables) =>
        PlaceholderRegex().Replace(text, m =>
            variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
}
