using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Federation.ActivityStreams;
using MediaBrowser.Model.Federation.Library;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public class FederationLibraryItemActivityBuilderTests
{
    private const string ActorUrl = "https://my-instance.com/Federation/Actor";

    private static FederationLibraryItem CreateSampleItem() => new()
    {
        SourceId = Guid.Parse("01020304-0506-0708-090a-0b0c0d0e0f10"),
        Name = "Test Movie",
        Overview = "A great film.",
        Year = 2024,
        RunTimeTicks = TimeSpan.FromMinutes(120).Ticks,
        MediaType = "Movie",
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tmdb"] = "12345",
            ["Imdb"] = "tt1234567"
        }
    };

    [Fact]
    public void BuildActivity_ReturnsCreate_WithCorrectStructure()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();

        Assert.Equal("Create", activity.Type);
        Assert.Equal(ActorUrl, activity.Actor);
        Assert.Contains(item.SourceId.ToString(), activity.Id, StringComparison.Ordinal);
        Assert.NotNull(activity.Object);
        Assert.Equal("Video", activity.Object.Type);
        Assert.Equal("Test Movie", activity.Object.Name);
        Assert.Equal("A great film.", activity.Object.Content);
        Assert.Equal($"{ActorUrl}/Items/{item.SourceId}", activity.Object.Id);
    }

    [Fact]
    public void BuildUpdateActivity_ReturnsUpdate_WithCorrectType()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildUpdateActivity();

        Assert.Equal("Update", activity.Type);
        Assert.Equal(ActorUrl, activity.Actor);
        Assert.NotNull(activity.Object);
        Assert.Equal("Video", activity.Object.Type);
    }

    [Fact]
    public void BuildDeleteActivity_ReturnsTombstone()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildDeleteActivity();

        Assert.Equal("Delete", activity.Type);
        Assert.Equal(ActorUrl, activity.Actor);
        Assert.NotNull(activity.Object);
        Assert.Equal("Tombstone", activity.Object.Type);
        Assert.Equal($"{ActorUrl}/Items/{item.SourceId}", activity.Object.Id);
    }

    [Fact]
    public void BuildActivity_IncludesProviderIdsAsTags()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();
        var tags = activity.Object!.Tag!;

        var tmdbTag = tags.FirstOrDefault(t => t.Name == "Tmdb");
        Assert.NotNull(tmdbTag);
        Assert.Equal("PropertyValue", tmdbTag.Type);
        Assert.Equal("12345", tmdbTag.Content);

        var imdbTag = tags.FirstOrDefault(t => t.Name == "Imdb");
        Assert.NotNull(imdbTag);
        Assert.Equal("tt1234567", imdbTag.Content);
    }

    [Fact]
    public void BuildActivity_IncludesMediaTypeTag()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();
        var mediaTypeTag = activity.Object!.Tag!.FirstOrDefault(t => t.Name == "MediaType");

        Assert.NotNull(mediaTypeTag);
        Assert.Equal("Movie", mediaTypeTag.Content);
    }

    [Fact]
    public void BuildActivity_IncludesYearTag()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();
        var yearTag = activity.Object!.Tag!.FirstOrDefault(t => t.Name == "Year");

        Assert.NotNull(yearTag);
        Assert.Equal("2024", yearTag.Content);
    }

    [Fact]
    public void BuildActivity_NullYear_OmitsYearTag()
    {
        var item = CreateSampleItem();
        item.Year = null;
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();
        var yearTag = activity.Object!.Tag!.FirstOrDefault(t => t.Name == "Year");

        Assert.Null(yearTag);
    }

    [Fact]
    public void BuildActivity_IncludesDuration()
    {
        var item = CreateSampleItem();
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();

        Assert.Equal("PT2H0M0S", activity.Object!.Duration);
    }

    [Fact]
    public void BuildActivity_NullRuntime_OmitsDuration()
    {
        var item = CreateSampleItem();
        item.RunTimeTicks = null;
        var builder = new FederationLibraryItemActivityBuilder(item, ActorUrl);

        var activity = builder.BuildActivity();

        Assert.Null(activity.Object!.Duration);
    }
}
