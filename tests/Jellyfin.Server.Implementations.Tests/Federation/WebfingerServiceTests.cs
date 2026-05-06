using System.Threading.Tasks;
using Jellyfin.Server.Implementations.Federation.Webfinger;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public class WebfingerServiceTests
{
    private readonly WebfingerService _sut;

    public WebfingerServiceTests()
    {
        var configMock = new Mock<IConfigurationManager>();
        configMock
            .Setup(m => m.GetConfiguration(FederationConfigurationStore.StoreKey))
            .Returns(new FederationConfiguration
            {
                Enabled = true,
                Hostname = "my-instance.com",
                ActorName = "jellyfin"
            });
        _sut = new WebfingerService(configMock.Object);
    }

    [Fact]
    public async Task QueryAsync_MatchingResource_ReturnsResponse()
    {
        var result = await _sut.QueryAsync("acct:jellyfin@my-instance.com");

        Assert.NotNull(result);
        Assert.Equal("acct:jellyfin@my-instance.com", result.Subject);
        Assert.Single(result.Links);
        Assert.Equal("self", result.Links[0].Relation);
        Assert.Equal("application/activity+json", result.Links[0].Type);
        Assert.Equal("https://my-instance.com/Federation/Actor", result.Links[0].Href);
    }

    [Theory]
    [InlineData("acct:other@my-instance.com")]
    [InlineData("acct:jellyfin@other-instance.com")]
    [InlineData("https://my-instance.com/Federation/Actor")]
    [InlineData("")]
    public async Task QueryAsync_NonMatchingResource_ReturnsNull(string resource)
    {
        var result = await _sut.QueryAsync(resource);

        Assert.Null(result);
    }
}
