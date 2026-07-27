using Azure.Data.Tables;
using Regnroll.App.Models;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

/// <summary>
/// Integration tests for the real Table Storage LinkStore against Azurite.
/// Skipped automatically when no Azurite/emulator listens on 127.0.0.1:10002.
/// </summary>
public sealed class AzuriteFixture : IDisposable
{
    public TableServiceClient? Client { get; }

    public AzuriteFixture()
    {
        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            if (!probe.ConnectAsync("127.0.0.1", 10002).Wait(1500))
            {
                return;
            }

            var client = new TableServiceClient("UseDevelopmentStorage=true");
            client.CreateTableIfNotExists(TableNames.Links);
            Client = client;
        }
        catch
        {
            Client = null;
        }
    }

    public void Dispose()
    {
    }
}

public class LinkStoreAzuriteTests(AzuriteFixture fixture) : IClassFixture<AzuriteFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private LinkStore Store()
    {
        Skip.If(fixture.Client is null, "Azurite is not running on 127.0.0.1:10002");
        return new LinkStore(fixture.Client!);
    }

    private static string NewId() => new CryptoService().GenerateLinkId();

    [SkippableFact]
    public async Task Create_Find_RoundTrips_WithHashedRowKey()
    {
        var store = Store();
        var id = NewId();

        await store.CreateSecretLinkAsync(id, $"c-{id}", "cipher", "nonce", Now, Now.AddDays(14), Guid.NewGuid(), Now.AddDays(180), null);
        var found = await store.FindByRawIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(CryptoService.HashId(id), found!.RowKey);
        Assert.DoesNotContain(id, found.RowKey);
        Assert.Equal(LinkStatuses.Pending, found.Status);
    }

    [SkippableFact]
    public async Task TryComplete_StripsCiphertext_AndIsAtomic()
    {
        var store = Store();
        var id = NewId();
        await store.CreateSecretLinkAsync(id, $"c-{id}", "cipher", "nonce", Now, Now.AddDays(14), Guid.NewGuid(), Now.AddDays(180), null);

        // Two independent reads → two claim attempts with the same ETag: exactly one wins.
        var first = await store.FindByRawIdAsync(id);
        var second = await store.FindByRawIdAsync(id);
        var win1 = await store.TryCompleteAsync(first!, LinkStatuses.Claimed, Now);
        var win2 = await store.TryCompleteAsync(second!, LinkStatuses.Claimed, Now);

        Assert.True(win1 ^ win2);
        var after = await store.FindByRawIdAsync(id);
        Assert.Equal(LinkStatuses.Claimed, after!.Status);
        Assert.Null(after.Ciphertext);
        Assert.Null(after.Nonce);
    }

    [SkippableFact]
    public async Task InvalidatePending_RemovesOnlyPendingOfThatType()
    {
        var store = Store();
        var client = $"c-{NewId()}";
        var secretId = NewId();
        var uploadId = NewId();
        await store.CreateSecretLinkAsync(secretId, client, "x", "n", Now, Now.AddDays(14), Guid.NewGuid(), Now.AddDays(180), null);
        await store.CreateUploadLinkAsync(uploadId, client, Now, Now.AddDays(14), null);

        await store.InvalidatePendingAsync(client, CredentialTypes.Secret);

        Assert.Null(await store.FindByRawIdAsync(secretId));
        Assert.NotNull(await store.FindByRawIdAsync(uploadId));
    }

    [SkippableFact]
    public async Task PurgeExpired_RemovesOnlyExpiredRows()
    {
        var store = Store();
        var expiredId = NewId();
        var liveId = NewId();
        var client = $"c-{NewId()}";
        await store.CreateUploadLinkAsync(expiredId, client, Now.AddDays(-30), Now.AddDays(-1), null);
        await store.CreateUploadLinkAsync(liveId, client, Now, Now.AddDays(14), null);

        await store.PurgeExpiredAsync(Now);

        Assert.Null(await store.FindByRawIdAsync(expiredId));
        Assert.NotNull(await store.FindByRawIdAsync(liveId));
    }

    [SkippableFact]
    public async Task DeleteByClient_RemovesAllRowsOfThatClientOnly()
    {
        var store = Store();
        var client = $"c-{NewId()}";
        var otherClient = $"c-{NewId()}";
        var mine = NewId();
        var other = NewId();
        await store.CreateUploadLinkAsync(mine, client, Now, Now.AddDays(14), null);
        await store.CreateUploadLinkAsync(other, otherClient, Now, Now.AddDays(14), null);

        await store.DeleteByClientAsync(client);

        Assert.Null(await store.FindByRawIdAsync(mine));
        Assert.NotNull(await store.FindByRawIdAsync(other));
    }

    [SkippableFact]
    public async Task MarkWarned_PersistsWarnedAt()
    {
        var store = Store();
        var id = NewId();
        await store.CreateUploadLinkAsync(id, $"c-{id}", Now, Now.AddDays(14), Now.AddDays(5));
        var entity = await store.FindByRawIdAsync(id);

        await store.MarkWarnedAsync(entity!, Now);

        Assert.NotNull((await store.FindByRawIdAsync(id))!.WarnedAt);
    }
}
