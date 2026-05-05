using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using Jellyfin.Server.Implementations.Federation.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Federation.ActivityStreams;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Tests.Federation;

public sealed class FederationLibraryIngesterTests : IDisposable
{
    private const string FollowedActorUrl = "https://peer.example/actor";

    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly IJellyfinDatabaseProvider _dbProvider;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;
    private readonly List<BaseItem> _library;
    private readonly FederationLibraryIngester _sut;
    private int _followedActorId;

    public FederationLibraryIngesterTests()
    {
        _libraryManagerMock = new Mock<ILibraryManager>();
        _library = new List<BaseItem>();

        _libraryManagerMock
            .Setup(m => m.CreateItem(It.IsAny<BaseItem>(), It.IsAny<BaseItem?>()))
            .Callback<BaseItem, BaseItem?>((item, _) => _library.Add(item));

        _libraryManagerMock
            .Setup(m => m.GetItemById(It.IsAny<Guid>()))
            .Returns<Guid>(id => _library.FirstOrDefault(i => i.Id.Equals(id)));

        _libraryManagerMock
            .Setup(m => m.QueryItems(It.IsAny<InternalItemsQuery>()))
            .Returns<InternalItemsQuery>(query =>
            {
                IEnumerable<BaseItem> matches = _library;
                if (!string.IsNullOrEmpty(query.ExternalId))
                {
                    matches = matches.Where(i => string.Equals(i.ExternalId, query.ExternalId, StringComparison.Ordinal));
                }

                return new QueryResult<BaseItem>(matches.ToList());
            });

        _libraryManagerMock
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns<InternalItemsQuery>(query =>
            {
                IEnumerable<BaseItem> matches = _library;
                if (query.IncludeItemTypes is { Length: > 0 })
                {
                    matches = matches.Where(i => query.IncludeItemTypes.Contains(i.GetBaseItemKind()));
                }

                if (query.HasAnyProviderId is { Count: > 0 })
                {
                    matches = matches.Where(item => query.HasAnyProviderId.Any(pair =>
                        item.ProviderIds.TryGetValue(pair.Key, out var value)
                        && string.Equals(value, pair.Value, StringComparison.Ordinal)));
                }

                if (query.Limit.HasValue)
                {
                    matches = matches.Take(query.Limit.Value);
                }

                return matches.ToList();
            });

        _libraryManagerMock
            .Setup(m => m.UpdateItemAsync(It.IsAny<BaseItem>(), It.IsAny<BaseItem>(), It.IsAny<ItemUpdateType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _libraryManagerMock
            .Setup(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()))
            .Callback<BaseItem, DeleteOptions>((item, _) => _library.Remove(item));

        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite($"Data Source=federation-ingester-test-{Guid.NewGuid()}.db")
            .Options;

        _dbProvider = new Mock<IJellyfinDatabaseProvider>().Object;

        using (var initDb = CreateDbContext())
        {
            initDb.Database.EnsureCreated();

            var actor = new FederationActor(FollowedActorUrl, FollowedActorUrl + "/inbox", FollowedActorUrl + "/outbox", "pubkey");
            initDb.FederationActors.Add(actor);
            initDb.SaveChanges();
            initDb.FederationFollowings.Add(new FederationFollowing(actor.Id));
            initDb.SaveChanges();
            _followedActorId = actor.Id;
        }

        var factoryMock = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDbContext());
        _dbFactory = factoryMock.Object;

        _sut = new FederationLibraryIngester(
            _libraryManagerMock.Object,
            _dbFactory,
            NullLogger<FederationLibraryIngester>.Instance);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task Ingest_MissingActor_Drops()
    {
        var activity = new Create { Actor = null, Object = BuildVideo(Guid.NewGuid(), "X", "Movie") };
        await _sut.IngestAsync(activity);

        await using var db = CreateDbContext();
        Assert.Empty(_library);
        Assert.Equal(0, await db.FederationIngestedItems.CountAsync());
    }

    [Fact]
    public async Task Ingest_NonFollowedActor_Drops()
    {
        var activity = new Create { Actor = "https://stranger.example/actor", Object = BuildVideo(Guid.NewGuid(), "X", "Movie") };
        await _sut.IngestAsync(activity);

        await using var db = CreateDbContext();
        Assert.Empty(_library);
        Assert.Equal(0, await db.FederationIngestedItems.CountAsync());
    }

    [Fact]
    public async Task Ingest_Create_Movie_InsertsMovieAndFolders()
    {
        var sourceId = Guid.NewGuid();
        var activity = new Create { Actor = FollowedActorUrl, Object = BuildVideo(sourceId, "The Matrix", "Movie") };

        await _sut.IngestAsync(activity);

        var movie = _library.OfType<Movie>().FirstOrDefault();
        Assert.NotNull(movie);
        Assert.Equal("The Matrix", movie!.Name);
        Assert.True(movie.IsVirtualItem);
        Assert.Null(movie.Path);

        Assert.Contains(_library, item => item is Folder && item.Name == "Federated");
        Assert.Contains(_library, item => item is Folder && item.Name == "peer.example");

        await using var db = CreateDbContext();
        var row = await db.FederationIngestedItems.FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(sourceId, row!.SourceId);
        Assert.Equal(movie.Id, row.BaseItemId);
        Assert.Equal(_followedActorId, row.ActorId);
    }

    [Fact]
    public async Task Ingest_Create_NonMovie_Drops()
    {
        var activity = new Create { Actor = FollowedActorUrl, Object = BuildVideo(Guid.NewGuid(), "Pilot", "Episode") };

        await _sut.IngestAsync(activity);

        Assert.Empty(_library);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationIngestedItems.CountAsync());
    }

    [Fact]
    public async Task Ingest_Create_Idempotent_UpdatesExisting()
    {
        var sourceId = Guid.NewGuid();

        await _sut.IngestAsync(new Create { Actor = FollowedActorUrl, Object = BuildVideo(sourceId, "Original Name", "Movie") });

        var initialMovieCount = _library.OfType<Movie>().Count();

        await _sut.IngestAsync(new Create { Actor = FollowedActorUrl, Object = BuildVideo(sourceId, "New Name", "Movie", overview: "updated overview") });

        Assert.Equal(initialMovieCount, _library.OfType<Movie>().Count());

        var movie = _library.OfType<Movie>().Single();
        Assert.Equal("New Name", movie.Name);
        Assert.Equal("updated overview", movie.Overview);

        _libraryManagerMock.Verify(
            m => m.UpdateItemAsync(It.Is<BaseItem>(i => i.Id.Equals(movie.Id)), It.IsAny<BaseItem>(), ItemUpdateType.MetadataImport, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Ingest_Update_UpdatesExisting()
    {
        var sourceId = Guid.NewGuid();
        await _sut.IngestAsync(new Create { Actor = FollowedActorUrl, Object = BuildVideo(sourceId, "Original", "Movie") });

        var updatePayload = BuildVideo(sourceId, "Edited", "Movie", overview: "new overview");
        var update = new ActivityStreams.Activity { Type = "Update", Actor = FollowedActorUrl, Object = updatePayload };

        await _sut.IngestAsync(update);

        var movie = _library.OfType<Movie>().Single();
        Assert.Equal("Edited", movie.Name);
        Assert.Equal("new overview", movie.Overview);
    }

    [Fact]
    public async Task Ingest_Delete_RemovesItemAndRow()
    {
        var sourceId = Guid.NewGuid();
        await _sut.IngestAsync(new Create { Actor = FollowedActorUrl, Object = BuildVideo(sourceId, "Condemned", "Movie") });

        var tombstone = new ActivityStreams.Object
        {
            Id = $"{FollowedActorUrl}/Items/{sourceId}",
            Type = "Tombstone"
        };
        await _sut.IngestAsync(new Delete { Actor = FollowedActorUrl, Object = tombstone });

        Assert.Empty(_library.OfType<Movie>());

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationIngestedItems.CountAsync());
    }

    [Fact]
    public async Task Ingest_Delete_DedupedRow_KeepsSharedBaseItem()
    {
        const string PeerBUrl = "https://peer-b.example/actor";
        AddFollowedActor(PeerBUrl);

        var actorASourceId = Guid.NewGuid();
        var actorBSourceId = Guid.NewGuid();
        var sharedProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" };

        await _sut.IngestAsync(new Create
        {
            Actor = FollowedActorUrl,
            Object = BuildVideo(actorASourceId, "The Matrix", "Movie", providerIds: sharedProviderIds)
        });
        await _sut.IngestAsync(new Create
        {
            Actor = PeerBUrl,
            Object = BuildVideo(actorBSourceId, "The Matrix", "Movie", providerIds: sharedProviderIds, actorUrl: PeerBUrl)
        });

        Assert.Single(_library.OfType<Movie>());

        // Peer B deletes its reference — shared Movie must stay because actor A still references it.
        await _sut.IngestAsync(new Delete
        {
            Actor = PeerBUrl,
            Object = new ActivityStreams.Object
            {
                Id = $"{PeerBUrl}/Items/{actorBSourceId}",
                Type = "Tombstone"
            }
        });

        Assert.Single(_library.OfType<Movie>());

        await using var db = CreateDbContext();
        var rows = await db.FederationIngestedItems.ToListAsync();
        Assert.Single(rows);
        Assert.Equal(_followedActorId, rows[0].ActorId);
    }

    [Fact]
    public async Task Ingest_Delete_PreservesLocalBaseItem()
    {
        var local = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "The Matrix",
        };
        local.ProviderIds["Tmdb"] = "603";
        _library.Add(local);

        var sourceId = Guid.NewGuid();
        await _sut.IngestAsync(new Create
        {
            Actor = FollowedActorUrl,
            Object = BuildVideo(sourceId, "Matrix Remote", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" })
        });

        await _sut.IngestAsync(new Delete
        {
            Actor = FollowedActorUrl,
            Object = new ActivityStreams.Object
            {
                Id = $"{FollowedActorUrl}/Items/{sourceId}",
                Type = "Tombstone"
            }
        });

        // Local movie untouched; federation row removed.
        Assert.Contains(_library.OfType<Movie>(), m => m.Id.Equals(local.Id));
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.FederationIngestedItems.CountAsync());
    }

    [Fact]
    public async Task Ingest_Delete_UnknownItem_IsNoOp()
    {
        var tombstone = new ActivityStreams.Object
        {
            Id = $"{FollowedActorUrl}/Items/{Guid.NewGuid()}",
            Type = "Tombstone"
        };
        await _sut.IngestAsync(new Delete { Actor = FollowedActorUrl, Object = tombstone });

        _libraryManagerMock.Verify(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()), Times.Never);
    }

    [Fact]
    public async Task Ingest_Create_CrossActorSameProviderId_DedupesAgainstExistingBaseItem()
    {
        const string PeerBUrl = "https://peer-b.example/actor";
        var peerBActorId = AddFollowedActor(PeerBUrl);

        var actorASourceId = Guid.NewGuid();
        var actorBSourceId = Guid.NewGuid();
        var sharedProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" };

        await _sut.IngestAsync(new Create
        {
            Actor = FollowedActorUrl,
            Object = BuildVideo(actorASourceId, "The Matrix", "Movie", providerIds: sharedProviderIds)
        });

        await _sut.IngestAsync(new Create
        {
            Actor = PeerBUrl,
            Object = BuildVideo(actorBSourceId, "The Matrix", "Movie", providerIds: sharedProviderIds, actorUrl: PeerBUrl)
        });

        var movies = _library.OfType<Movie>().ToList();
        Assert.Single(movies);

        await using var db = CreateDbContext();
        var rows = await db.FederationIngestedItems.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(movies[0].Id, r.BaseItemId));
        Assert.Contains(rows, r => r.ActorId == _followedActorId && r.SourceId.Equals(actorASourceId));
        Assert.Contains(rows, r => r.ActorId == peerBActorId && r.SourceId.Equals(actorBSourceId));
    }

    [Fact]
    public async Task Ingest_Create_MatchesLocalItem_PointsAtLocalWithoutCreatingNew()
    {
        var local = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "The Matrix",
        };
        local.ProviderIds["Tmdb"] = "603";
        _library.Add(local);

        var sourceId = Guid.NewGuid();
        await _sut.IngestAsync(new Create
        {
            Actor = FollowedActorUrl,
            Object = BuildVideo(sourceId, "The Matrix (Federated)", "Movie", providerIds: new Dictionary<string, string> { ["Tmdb"] = "603" })
        });

        Assert.Single(_library.OfType<Movie>());
        var movie = _library.OfType<Movie>().Single();
        Assert.Equal(local.Id, movie.Id);
        Assert.Equal("The Matrix", movie.Name); // local metadata preserved

        await using var db = CreateDbContext();
        var row = await db.FederationIngestedItems.SingleAsync();
        Assert.Equal(local.Id, row.BaseItemId);
        Assert.Equal(_followedActorId, row.ActorId);
        Assert.Equal(sourceId, row.SourceId);
    }

    [Fact]
    public async Task Ingest_Update_ForDedupedRow_DoesNotOverwriteSharedMetadata()
    {
        const string PeerBUrl = "https://peer-b.example/actor";
        AddFollowedActor(PeerBUrl);

        var actorASourceId = Guid.NewGuid();
        var actorBSourceId = Guid.NewGuid();
        var sharedProviderIds = new Dictionary<string, string> { ["Tmdb"] = "603" };

        await _sut.IngestAsync(new Create
        {
            Actor = FollowedActorUrl,
            Object = BuildVideo(actorASourceId, "Matrix A Version", "Movie", providerIds: sharedProviderIds)
        });

        // Peer B dedupes against the Movie created by actor A.
        await _sut.IngestAsync(new Create
        {
            Actor = PeerBUrl,
            Object = BuildVideo(actorBSourceId, "Matrix B Version", "Movie", providerIds: sharedProviderIds, actorUrl: PeerBUrl)
        });

        var sharedMovie = _library.OfType<Movie>().Single();
        Assert.Equal("Matrix A Version", sharedMovie.Name);

        // Peer B sends an Update — should NOT overwrite A's canonical metadata.
        await _sut.IngestAsync(new ActivityStreams.Activity
        {
            Type = "Update",
            Actor = PeerBUrl,
            Object = BuildVideo(actorBSourceId, "Matrix B Updated", "Movie", overview: "B's edit", providerIds: sharedProviderIds, actorUrl: PeerBUrl)
        });

        Assert.Equal("Matrix A Version", sharedMovie.Name);
        Assert.Null(sharedMovie.Overview);
    }

    [Fact]
    public async Task Ingest_Create_ProviderIdsPreserved()
    {
        var sourceId = Guid.NewGuid();
        var video = BuildVideo(sourceId, "Tagged", "Movie");
        video.Tag = video.Tag!.Append(new ActivityStreams.Object { Type = "PropertyValue", Name = "Imdb", Content = "tt1234567" }).ToArray();

        await _sut.IngestAsync(new Create { Actor = FollowedActorUrl, Object = video });

        var movie = _library.OfType<Movie>().Single();
        Assert.Equal("tt1234567", movie.ProviderIds["Imdb"]);
    }

    private JellyfinDbContext CreateDbContext()
    {
        return new JellyfinDbContext(
            _dbOptions,
            NullLogger<JellyfinDbContext>.Instance,
            _dbProvider,
            new Jellyfin.Database.Implementations.Locking.NoLockBehavior(NullLogger<Jellyfin.Database.Implementations.Locking.NoLockBehavior>.Instance));
    }

    private static ActivityStreams.Object BuildVideo(
        Guid sourceId,
        string name,
        string mediaType,
        string? overview = null,
        IDictionary<string, string>? providerIds = null,
        string? actorUrl = null)
    {
        var ownerUrl = actorUrl ?? FollowedActorUrl;
        var tags = new List<ActivityStreams.Object>
        {
            new ActivityStreams.Object { Type = "PropertyValue", Name = "MediaType", Content = mediaType }
        };
        if (providerIds is not null)
        {
            foreach (var (key, value) in providerIds)
            {
                tags.Add(new ActivityStreams.Object { Type = "PropertyValue", Name = key, Content = value });
            }
        }

        return new ActivityStreams.Object
        {
            Id = $"{ownerUrl}/Items/{sourceId}",
            Type = "Video",
            Name = name,
            Content = overview,
            Tag = tags.ToArray()
        };
    }

    private int AddFollowedActor(string actorUrl)
    {
        using var db = CreateDbContext();
        var actor = new FederationActor(actorUrl, actorUrl + "/inbox", actorUrl + "/outbox", "pubkey");
        db.FederationActors.Add(actor);
        db.SaveChanges();
        db.FederationFollowings.Add(new FederationFollowing(actor.Id));
        db.SaveChanges();
        return actor.Id;
    }
}
