using Azure;
using Azure.Data.Tables;
using Regnroll.App.Models;

namespace Regnroll.App.Services;

public interface ILinkStore
{
    Task<LinkEntity> CreateSecretLinkAsync(
        string linkId, string clientId, string ciphertext, string nonce,
        DateTimeOffset createdAt, DateTimeOffset expiresAt,
        Guid newCredentialKeyId, DateTimeOffset newCredentialExpiresAt, DateTimeOffset? oldCredentialExpiresAt,
        CancellationToken ct = default);

    Task<LinkEntity> CreateUploadLinkAsync(
        string linkId, string clientId,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, DateTimeOffset? oldCredentialExpiresAt,
        CancellationToken ct = default);

    /// <summary>Looks a link up by its raw id (hashes internally). No expiry filtering — callers decide.</summary>
    Task<LinkEntity?> FindByRawIdAsync(string linkId, CancellationToken ct = default);

    /// <summary>
    /// Atomically completes a Pending link: strips ciphertext material, sets the final status,
    /// and writes with an ETag precondition. Returns false when a concurrent request won the race
    /// or the row is gone — the caller must treat that as "gone".
    /// </summary>
    Task<bool> TryCompleteAsync(LinkEntity entity, string newStatus, DateTimeOffset completedAt, CancellationToken ct = default);

    /// <summary>Best-effort revert of a completion (used when a Graph write fails after consuming an upload link).</summary>
    Task TryRevertToPendingAsync(LinkEntity entity, CancellationToken ct = default);

    Task<IReadOnlyList<LinkEntity>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LinkEntity>> GetByClientAsync(string clientId, CancellationToken ct = default);
    Task MarkWarnedAsync(LinkEntity entity, DateTimeOffset warnedAt, CancellationToken ct = default);
    /// <summary>Deletes Pending links of the given type for the client (manual re-trigger supersedes).</summary>
    Task InvalidatePendingAsync(string clientId, string type, CancellationToken ct = default);
    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct = default);
    Task DeleteAsync(string rowKey, CancellationToken ct = default);
    /// <summary>Removes every link row of a client (used by unlink).</summary>
    Task DeleteByClientAsync(string clientId, CancellationToken ct = default);
}

public sealed class LinkStore(TableServiceClient tables) : ILinkStore
{
    private readonly TableClient _table = tables.GetTableClient(TableNames.Links);

    public async Task<LinkEntity> CreateSecretLinkAsync(
        string linkId, string clientId, string ciphertext, string nonce,
        DateTimeOffset createdAt, DateTimeOffset expiresAt,
        Guid newCredentialKeyId, DateTimeOffset newCredentialExpiresAt, DateTimeOffset? oldCredentialExpiresAt,
        CancellationToken ct = default)
    {
        var entity = new LinkEntity
        {
            RowKey = CryptoService.HashId(linkId),
            Type = CredentialTypes.Secret,
            ClientId = clientId,
            Ciphertext = ciphertext,
            Nonce = nonce,
            Status = LinkStatuses.Pending,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            NewCredentialKeyId = newCredentialKeyId.ToString(),
            NewCredentialExpiresAt = newCredentialExpiresAt,
            OldCredentialExpiresAt = oldCredentialExpiresAt,
        };
        await _table.AddEntityAsync(entity, ct);
        return entity;
    }

    public async Task<LinkEntity> CreateUploadLinkAsync(
        string linkId, string clientId,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, DateTimeOffset? oldCredentialExpiresAt,
        CancellationToken ct = default)
    {
        var entity = new LinkEntity
        {
            RowKey = CryptoService.HashId(linkId),
            Type = CredentialTypes.Certificate,
            ClientId = clientId,
            Status = LinkStatuses.Pending,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            OldCredentialExpiresAt = oldCredentialExpiresAt,
        };
        await _table.AddEntityAsync(entity, ct);
        return entity;
    }

    public async Task<LinkEntity?> FindByRawIdAsync(string linkId, CancellationToken ct = default)
    {
        try
        {
            return await _table.GetEntityAsync<LinkEntity>(LinkEntity.Partition, CryptoService.HashId(linkId), cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> TryCompleteAsync(LinkEntity entity, string newStatus, DateTimeOffset completedAt, CancellationToken ct = default)
    {
        entity.Ciphertext = null;
        entity.Nonce = null;
        entity.Status = newStatus;
        entity.CompletedAt = completedAt;
        try
        {
            await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
            return true;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 409 or 412)
        {
            return false;
        }
    }

    public async Task TryRevertToPendingAsync(LinkEntity entity, CancellationToken ct = default)
    {
        entity.Status = LinkStatuses.Pending;
        entity.CompletedAt = null;
        try
        {
            await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException)
        {
            // Best effort — the customer will be told to contact IT support if this ever fails.
        }
    }

    public async Task<IReadOnlyList<LinkEntity>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<LinkEntity>();
        await foreach (var entity in _table.QueryAsync<LinkEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {LinkEntity.Partition}"), cancellationToken: ct))
        {
            result.Add(entity);
        }

        return result;
    }

    public async Task<IReadOnlyList<LinkEntity>> GetByClientAsync(string clientId, CancellationToken ct = default)
    {
        var result = new List<LinkEntity>();
        await foreach (var entity in _table.QueryAsync<LinkEntity>(
            TableClient.CreateQueryFilter($"PartitionKey eq {LinkEntity.Partition} and ClientId eq {clientId}"), cancellationToken: ct))
        {
            result.Add(entity);
        }

        return result;
    }

    public Task MarkWarnedAsync(LinkEntity entity, DateTimeOffset warnedAt, CancellationToken ct = default)
    {
        entity.WarnedAt = warnedAt;
        return _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
    }

    public async Task InvalidatePendingAsync(string clientId, string type, CancellationToken ct = default)
    {
        foreach (var link in await GetByClientAsync(clientId, ct))
        {
            if (link.IsPending && link.Type == type)
            {
                await _table.DeleteEntityAsync(LinkEntity.Partition, link.RowKey, ETag.All, ct);
            }
        }
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var purged = 0;
        foreach (var link in await GetAllAsync(ct))
        {
            if (link.IsExpired(now))
            {
                await _table.DeleteEntityAsync(LinkEntity.Partition, link.RowKey, ETag.All, ct);
                purged++;
            }
        }

        return purged;
    }

    public async Task DeleteAsync(string rowKey, CancellationToken ct = default)
    {
        await _table.DeleteEntityAsync(LinkEntity.Partition, rowKey, cancellationToken: ct);
    }

    public async Task DeleteByClientAsync(string clientId, CancellationToken ct = default)
    {
        foreach (var link in await GetByClientAsync(clientId, ct))
        {
            await _table.DeleteEntityAsync(LinkEntity.Partition, link.RowKey, ETag.All, ct);
        }
    }
}
