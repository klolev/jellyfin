using System;

namespace MediaBrowser.Model.Federation.Media;

/// <summary>
/// The result of issuing a streaming token.
/// </summary>
/// <param name="Token">The raw token string. Not persisted in plaintext.</param>
/// <param name="ExpiresAt">The UTC expiry stored in the database.</param>
public record struct IssuedToken(string Token, DateTime ExpiresAt);
