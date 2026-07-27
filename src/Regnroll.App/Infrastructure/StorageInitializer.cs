using Azure.Data.Tables;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Regnroll.App.Models;

namespace Regnroll.App.Infrastructure;

/// <summary>Creates the data tables at startup (idempotent).</summary>
public sealed class StorageInitializer(TableServiceClient tables, ILogger<StorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { TableNames.AppRegs, TableNames.Links, TableNames.Templates })
        {
            await tables.CreateTableIfNotExistsAsync(name, cancellationToken);
        }

        logger.LogInformation("Data tables verified.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
