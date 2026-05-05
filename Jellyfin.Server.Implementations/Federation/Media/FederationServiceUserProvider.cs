using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Federation.Media;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Server.Implementations.Federation.Media;

/// <inheritdoc/>
public sealed class FederationServiceUserProvider : IFederationServiceUserProvider, IDisposable
{
    private const string ServiceUsername = "federation-service";

    private readonly IUserManager _userManager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _cachedUserId;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationServiceUserProvider"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    public FederationServiceUserProvider(IUserManager userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc/>
    public async Task<Guid> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        if (_cachedUserId is Guid cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedUserId is Guid raced)
            {
                return raced;
            }

            var existing = _userManager.GetUserByName(ServiceUsername);
            if (existing is not null)
            {
                _cachedUserId = existing.Id;
                return existing.Id;
            }

            var user = await _userManager.CreateUserAsync(ServiceUsername).ConfigureAwait(false);

            // Reassign to InvalidAuthProvider so no login flow can authenticate as this user,
            // and hide it from the management UI.
            user.AuthenticationProviderId = typeof(InvalidAuthProvider).FullName!;
            user.SetPermission(PermissionKind.IsHidden, true);
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            _cachedUserId = user.Id;
            return user.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _gate.Dispose();
    }
}
