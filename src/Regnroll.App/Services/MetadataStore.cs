using Azure;
using Azure.Data.Tables;
using Regnroll.App.Models;
using Regnroll.App.Options;

namespace Regnroll.App.Services;

public record EffectiveSettings(int RotateBeforeDays, int WarnBeforeDays);

public interface IMetadataStore
{
    Task<IReadOnlyList<AppRegEntity>> GetAllAsync(CancellationToken ct = default);
    Task<AppRegEntity?> GetAsync(string clientId, CancellationToken ct = default);
    Task UpsertAsync(AppRegEntity entity, CancellationToken ct = default);
    Task DeleteAsync(string clientId, CancellationToken ct = default);
}

public sealed class MetadataStore(TableServiceClient tables) : IMetadataStore
{
    private readonly TableClient _table = tables.GetTableClient(TableNames.AppRegs);

    public static EffectiveSettings Resolve(AppRegEntity entity, RegnrollOptions options) => new(
        entity.RotateBeforeDaysOverride ?? options.RotateBeforeDays,
        entity.WarnBeforeDaysOverride ?? options.WarnBeforeDays);

    public async Task<IReadOnlyList<AppRegEntity>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<AppRegEntity>();
        await foreach (var entity in _table.QueryAsync<AppRegEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {AppRegEntity.Partition}"), cancellationToken: ct))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<AppRegEntity?> GetAsync(string clientId, CancellationToken ct = default)
    {
        try
        {
            return await _table.GetEntityAsync<AppRegEntity>(AppRegEntity.Partition, clientId, cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync(AppRegEntity entity, CancellationToken ct = default) =>
        _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

    public async Task DeleteAsync(string clientId, CancellationToken ct = default)
    {
        await _table.DeleteEntityAsync(AppRegEntity.Partition, clientId, cancellationToken: ct);
    }
}
