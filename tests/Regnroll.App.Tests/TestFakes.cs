using System.Security.Cryptography.X509Certificates;
using Regnroll.App.Models;
using Regnroll.App.Services;

namespace Regnroll.App.Tests;

public sealed class FixedTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public sealed class InMemoryTemplateOverrideStore : ITemplateOverrideStore
{
    private readonly Dictionary<string, TemplateEntity> _items = [];

    public Task<TemplateEntity?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_items.TryGetValue(key, out var e) ? e : null);

    public Task UpsertAsync(TemplateEntity entity, CancellationToken ct = default)
    {
        _items[entity.RowKey] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        _items.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryMetadataStore : IMetadataStore
{
    public readonly Dictionary<string, AppRegEntity> Items = [];

    public Task<IReadOnlyList<AppRegEntity>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppRegEntity>>(Items.Values.ToList());

    public Task<AppRegEntity?> GetAsync(string clientId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(clientId, out var e) ? e : null);

    public Task UpsertAsync(AppRegEntity entity, CancellationToken ct = default)
    {
        Items[entity.RowKey] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string clientId, CancellationToken ct = default)
    {
        Items.Remove(clientId);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory ILinkStore with real ETag-race semantics (clone on read, compare-and-swap on complete).</summary>
public sealed class InMemoryLinkStore : ILinkStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (LinkEntity Entity, string ETag)> _rows = [];

    private static LinkEntity Clone(LinkEntity e) => new()
    {
        RowKey = e.RowKey, Type = e.Type, ClientId = e.ClientId, Ciphertext = e.Ciphertext, Nonce = e.Nonce,
        Status = e.Status, CreatedAt = e.CreatedAt, ExpiresAt = e.ExpiresAt, CompletedAt = e.CompletedAt,
        WarnedAt = e.WarnedAt, NewCredentialKeyId = e.NewCredentialKeyId, NewCredentialExpiresAt = e.NewCredentialExpiresAt,
        OldCredentialExpiresAt = e.OldCredentialExpiresAt, ETag = e.ETag,
    };

    private LinkEntity Add(LinkEntity entity)
    {
        lock (_lock)
        {
            var etag = Guid.NewGuid().ToString();
            entity.ETag = new Azure.ETag(etag);
            _rows[entity.RowKey] = (Clone(entity), etag);
            return entity;
        }
    }

    public Task<LinkEntity> CreateSecretLinkAsync(string linkId, string clientId, string ciphertext, string nonce, DateTimeOffset createdAt, DateTimeOffset expiresAt, Guid newCredentialKeyId, DateTimeOffset newCredentialExpiresAt, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default) =>
        Task.FromResult(Add(new LinkEntity
        {
            RowKey = CryptoService.HashId(linkId), Type = CredentialTypes.Secret, ClientId = clientId,
            Ciphertext = ciphertext, Nonce = nonce, CreatedAt = createdAt, ExpiresAt = expiresAt,
            NewCredentialKeyId = newCredentialKeyId.ToString(), NewCredentialExpiresAt = newCredentialExpiresAt,
            OldCredentialExpiresAt = oldCredentialExpiresAt,
        }));

    public Task<LinkEntity> CreateUploadLinkAsync(string linkId, string clientId, DateTimeOffset createdAt, DateTimeOffset expiresAt, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default) =>
        Task.FromResult(Add(new LinkEntity
        {
            RowKey = CryptoService.HashId(linkId), Type = CredentialTypes.Certificate, ClientId = clientId,
            CreatedAt = createdAt, ExpiresAt = expiresAt, OldCredentialExpiresAt = oldCredentialExpiresAt,
        }));

    public Task<LinkEntity?> FindByRawIdAsync(string linkId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(CryptoService.HashId(linkId), out var row))
            {
                var clone = Clone(row.Entity);
                clone.ETag = new Azure.ETag(row.ETag);
                return Task.FromResult<LinkEntity?>(clone);
            }

            return Task.FromResult<LinkEntity?>(null);
        }
    }

    public Task<bool> TryCompleteAsync(LinkEntity entity, string newStatus, DateTimeOffset completedAt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(entity.RowKey, out var row) || row.ETag != entity.ETag.ToString())
            {
                return Task.FromResult(false);
            }

            entity.Ciphertext = null;
            entity.Nonce = null;
            entity.Status = newStatus;
            entity.CompletedAt = completedAt;
            _rows[entity.RowKey] = (Clone(entity), Guid.NewGuid().ToString());
            return Task.FromResult(true);
        }
    }

    public Task TryRevertToPendingAsync(LinkEntity entity, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(entity.RowKey, out var row))
            {
                row.Entity.Status = LinkStatuses.Pending;
                row.Entity.CompletedAt = null;
                _rows[entity.RowKey] = (row.Entity, Guid.NewGuid().ToString());
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<LinkEntity>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<LinkEntity>>(_rows.Values.Select(r =>
            {
                var clone = Clone(r.Entity);
                clone.ETag = new Azure.ETag(r.ETag);
                return clone;
            }).ToList());
        }
    }

    public async Task<IReadOnlyList<LinkEntity>> GetByClientAsync(string clientId, CancellationToken ct = default) =>
        (await GetAllAsync(ct)).Where(l => l.ClientId == clientId).ToList();

    public Task MarkWarnedAsync(LinkEntity entity, DateTimeOffset warnedAt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(entity.RowKey, out var row))
            {
                row.Entity.WarnedAt = warnedAt;
                _rows[entity.RowKey] = (row.Entity, Guid.NewGuid().ToString());
            }

            return Task.CompletedTask;
        }
    }

    public Task InvalidatePendingAsync(string clientId, string type, CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var key in _rows.Where(r => r.Value.Entity.ClientId == clientId && r.Value.Entity.Type == type && r.Value.Entity.IsPending).Select(r => r.Key).ToList())
            {
                _rows.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var expired = _rows.Where(r => r.Value.Entity.IsExpired(now)).Select(r => r.Key).ToList();
            foreach (var key in expired)
            {
                _rows.Remove(key);
            }

            return Task.FromResult(expired.Count);
        }
    }

    public Task DeleteAsync(string rowKey, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _rows.Remove(rowKey);
            return Task.CompletedTask;
        }
    }

    public Task DeleteByClientAsync(string clientId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var key in _rows.Where(r => r.Value.Entity.ClientId == clientId).Select(r => r.Key).ToList())
            {
                _rows.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    public readonly List<(IReadOnlyList<string> To, string Subject, string HtmlBody)> Sent = [];
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(IReadOnlyList<string> to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }
}

public sealed class FakeGraphAppService : IGraphAppService
{
    public readonly Dictionary<string, AppRegistration> Apps = [];
    public readonly List<Guid> RemovedSecrets = [];
    public readonly List<string> AddedCertificates = [];
    public List<CredentialInfo> ExpiredCertificatesToRemove = [];
    public Exception? ThrowOnAddCertificate { get; set; }
    public string NextSecretText { get; set; } = "generated-secret-value";

    public Task<IReadOnlyList<AppRegistration>> ListManageableAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppRegistration>>(Apps.Values.ToList());

    public Task<AppRegistration> GetByClientIdAsync(string clientId, CancellationToken ct = default) =>
        Apps.TryGetValue(clientId, out var app)
            ? Task.FromResult(app)
            : throw new RegnrollGraphException("not found", 404);

    public Task<CreatedSecret> AddSecretAsync(string objectId, string displayName, DateTimeOffset endDateTime, CancellationToken ct = default) =>
        Task.FromResult(new CreatedSecret(Guid.NewGuid(), NextSecretText, endDateTime));

    public Task RemoveSecretAsync(string objectId, Guid keyId, CancellationToken ct = default)
    {
        RemovedSecrets.Add(keyId);
        return Task.CompletedTask;
    }

    public Task<string> AddCertificateAsync(string objectId, X509Certificate2 certificate, string displayName, CancellationToken ct = default)
    {
        if (ThrowOnAddCertificate is not null)
        {
            throw ThrowOnAddCertificate;
        }

        AddedCertificates.Add(certificate.Thumbprint);
        return Task.FromResult(certificate.Thumbprint);
    }

    public Task<IReadOnlyList<CredentialInfo>> RemoveExpiredCertificatesAsync(string objectId, DateTimeOffset now, CancellationToken ct = default)
    {
        var removed = ExpiredCertificatesToRemove;
        ExpiredCertificatesToRemove = [];
        return Task.FromResult<IReadOnlyList<CredentialInfo>>(removed);
    }
}
