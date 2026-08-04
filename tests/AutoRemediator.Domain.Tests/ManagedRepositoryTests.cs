using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class ManagedRepositoryTests
{
    [Fact]
    public void Slug_combines_organization_project_and_name()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");

        Assert.Equal("contoso/platform/web-api", repo.Slug);
    }

    [Fact]
    public void New_repository_is_enabled_by_default()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");

        Assert.True(repo.Enabled);
    }

    [Fact]
    public void Disable_then_enable_toggles_state()
    {
        var repo = new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "web-api");

        repo.Disable();
        Assert.False(repo.Enabled);

        repo.Enable();
        Assert.True(repo.Enabled);
    }

    [Fact]
    public void Constructor_rejects_blank_name()
    {
        Assert.Throws<ArgumentException>(() =>
            new ManagedRepository(Guid.NewGuid(), "contoso", "platform", "   "));
    }
}
