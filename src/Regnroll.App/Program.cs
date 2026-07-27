using System.Text.Json;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Regnroll.App.Infrastructure;
using Regnroll.App.Options;
using Regnroll.App.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddOptions<RegnrollOptions>()
    .Bind(builder.Configuration.GetSection(RegnrollOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<RegnrollOptions>>().Value;
    return !string.IsNullOrWhiteSpace(options.DataTablesConnectionString)
        ? new TableServiceClient(options.DataTablesConnectionString)
        : new TableServiceClient(new Uri(options.DataTablesEndpoint!), sp.GetRequiredService<TokenCredential>());
});

builder.Services.AddSingleton(sp =>
    new GraphServiceClient(sp.GetRequiredService<TokenCredential>(), ["https://graph.microsoft.com/.default"]));

builder.Services.AddSingleton<CryptoService>();
builder.Services.AddSingleton<IMetadataStore, MetadataStore>();
builder.Services.AddSingleton<ILinkStore, LinkStore>();
builder.Services.AddSingleton<ITemplateOverrideStore, TableTemplateOverrideStore>();
builder.Services.AddSingleton<ITemplateService, TemplateService>();
builder.Services.AddSingleton<IEmailSender, AcsEmailSender>();
builder.Services.AddSingleton<IGraphAppService, GraphAppService>();
builder.Services.AddSingleton<IDeliveryService, DeliveryService>();
builder.Services.AddSingleton<ILifecycleService, LifecycleService>();
builder.Services.AddHostedService<StorageInitializer>();

builder.Build().Run();
