using System;
using System.Text.Json;
using MediaBrowser.Model.Federation.ActivityStreams;
using Xunit;

using AsActivity = MediaBrowser.Model.Federation.ActivityStreams.Activity;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public class ActivityStreamsJsonOptionsTests
{
    [Fact]
    public void ActivityRoundTrip_PreservesAtContextAndType()
    {
        var activity = new Create
        {
            Id = "https://example.test/activities/123",
            Actor = "https://example.test/Federation/Actor",
            Object = new MediaBrowser.Model.Federation.ActivityStreams.Object
            {
                Id = "https://example.test/Items/abc",
                Type = "Video",
                Name = "Example"
            }
        };

        var json = JsonSerializer.Serialize(activity, ActivityStreamsJsonOptions.Default);

        // Spot-check a handful of property names that the ActivityPub consumers rely on.
        Assert.Contains("\"@context\":", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"Create\"", json, StringComparison.Ordinal);
        Assert.Contains("\"actor\":", json, StringComparison.Ordinal);
        Assert.Contains("\"object\":", json, StringComparison.Ordinal);

        var roundTripped = JsonSerializer.Deserialize<AsActivity>(json, ActivityStreamsJsonOptions.Default);

        Assert.NotNull(roundTripped);
        Assert.Equal("Create", roundTripped!.Type);
        Assert.Equal(activity.Actor, roundTripped.Actor);
        Assert.Equal(activity.Id, roundTripped.Id);
        Assert.NotNull(roundTripped.Object);
        Assert.Equal("Video", roundTripped.Object!.Type);
        Assert.Equal("Example", roundTripped.Object.Name);
    }

    [Fact]
    public void NullProperties_AreOmittedFromOutput()
    {
        var activity = new Create
        {
            Id = "https://example.test/activities/1",
            Actor = "https://example.test/Federation/Actor"
            // Object deliberately left null
        };

        var json = JsonSerializer.Serialize(activity, ActivityStreamsJsonOptions.Default);

        Assert.DoesNotContain("\"object\":null", json, StringComparison.Ordinal);
    }
}
