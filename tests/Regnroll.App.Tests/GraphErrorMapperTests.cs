using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class GraphErrorMapperTests
{
    [Fact]
    public void Forbidden_InOwnedByMode_ExplainsOwnershipAndPermission()
    {
        var message = GraphErrorMapper.Map("create client secret", 403, "Authorization_RequestDenied", "denied", tenantWideMode: false);

        Assert.Contains("Application.ReadWrite.OwnedBy", message);
        Assert.Contains("owner", message);
        Assert.Contains("grant-graph-permissions.ps1", message);
        Assert.Contains(GraphErrorMapper.DocsUrl, message);
    }

    [Fact]
    public void Forbidden_InTenantWideMode_NamesTheAllPermission()
    {
        var message = GraphErrorMapper.Map("list all applications", 403, "Authorization_RequestDenied", "denied", tenantWideMode: true);

        Assert.Contains("Application.ReadWrite.All", message);
        Assert.DoesNotContain("must be an owner", message);
    }

    [Fact]
    public void NotFound_MentionsOwnershipVisibility()
    {
        var message = GraphErrorMapper.Map("read app registration x", 404, "Request_ResourceNotFound", "nf", tenantWideMode: false);

        Assert.Contains("not be owned", message);
    }

    [Fact]
    public void Unauthorized_PointsAtIdentity()
    {
        var message = GraphErrorMapper.Map("read key credentials", 401, null, null, tenantWideMode: false);

        Assert.Contains("managed identity", message);
    }

    [Fact]
    public void OtherErrors_PassThroughCodeAndMessage()
    {
        var message = GraphErrorMapper.Map("update key credentials", 400, "BadRequest", "key too large", tenantWideMode: false);

        Assert.Contains("BadRequest", message);
        Assert.Contains("key too large", message);
    }
}
