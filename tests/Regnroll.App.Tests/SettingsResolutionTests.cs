using Regnroll.App.Models;
using Regnroll.App.Options;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class SettingsResolutionTests
{
    private static readonly RegnrollOptions Options = new() { PublicBaseUrl = "https://x", RotateBeforeDays = 30, WarnBeforeDays = 7 };

    [Fact]
    public void Defaults_ApplyWithoutOverrides()
    {
        var entity = new AppRegEntity { RowKey = "cid", ObjectId = "oid" };
        var settings = MetadataStore.Resolve(entity, Options);

        Assert.Equal(30, settings.RotateBeforeDays);
        Assert.Equal(7, settings.WarnBeforeDays);
    }

    [Fact]
    public void Overrides_WinOverDefaults()
    {
        var entity = new AppRegEntity { RowKey = "cid", ObjectId = "oid", RotateBeforeDaysOverride = 60, WarnBeforeDaysOverride = 14 };
        var settings = MetadataStore.Resolve(entity, Options);

        Assert.Equal(60, settings.RotateBeforeDays);
        Assert.Equal(14, settings.WarnBeforeDays);
    }

    [Fact]
    public void ClearingOverride_RestoresDefault()
    {
        var entity = new AppRegEntity { RowKey = "cid", ObjectId = "oid", RotateBeforeDaysOverride = 60 };
        entity.RotateBeforeDaysOverride = null;

        Assert.Equal(30, MetadataStore.Resolve(entity, Options).RotateBeforeDays);
    }

    [Fact]
    public void Contacts_AreParsedFromSemicolonList()
    {
        var entity = new AppRegEntity { RowKey = "cid", ObjectId = "oid", ContactEmails = " a@x.com ; b@y.com;;" };

        Assert.Equal(["a@x.com", "b@y.com"], entity.GetContacts());
    }
}
