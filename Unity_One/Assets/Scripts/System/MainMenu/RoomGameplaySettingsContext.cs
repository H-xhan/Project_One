using System;

public enum RoomGameplaySettingsSource
{
    None = 0,
    HostCreated = 1,
    Joined = 2,
    LocalFallback = 3
}

public sealed class RoomGameplaySettingsContext
{
    public string BoundLobbyId { get; private set; } = string.Empty;
    public RoomGameplaySettingsSnapshot CanonicalSnapshot { get; private set; }
    public RoomGameplaySettingsSource Source { get; private set; }
    public string LastResetReason { get; private set; } = string.Empty;
    public bool HasSnapshot => CanonicalSnapshot != null &&
                               !string.IsNullOrEmpty(BoundLobbyId);

    public void Bind(
        string lobbyIdOrSessionToken,
        RoomGameplaySettingsSnapshot snapshot,
        RoomGameplaySettingsSource source)
    {
        if (string.IsNullOrWhiteSpace(lobbyIdOrSessionToken))
            throw new ArgumentException("A lobby ID or session token is required.", nameof(lobbyIdOrSessionToken));

        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        if (source == RoomGameplaySettingsSource.None)
            throw new ArgumentOutOfRangeException(nameof(source));

        BoundLobbyId = lobbyIdOrSessionToken.Trim();
        CanonicalSnapshot = RoomGameplaySettingsValidator.CreateSnapshot(
            snapshot.Core.GameModeId,
            snapshot.Core.MapId,
            snapshot.Core.MaxPlayers,
            snapshot.PostItLiar.PromptSourceMode);
        Source = source;
        LastResetReason = string.Empty;
    }

    public void BindLocalFallback(string sessionToken)
    {
        Bind(
            sessionToken,
            RoomGameplaySettingsValidator.CreateDefaultSnapshot(),
            RoomGameplaySettingsSource.LocalFallback);
    }

    public bool TryGetSnapshotForLobby(
        string lobbyIdOrSessionToken,
        out RoomGameplaySettingsSnapshot snapshot)
    {
        bool matches = HasSnapshot &&
                       !string.IsNullOrWhiteSpace(lobbyIdOrSessionToken) &&
                       string.Equals(
                           BoundLobbyId,
                           lobbyIdOrSessionToken.Trim(),
                           StringComparison.Ordinal);

        snapshot = matches ? CanonicalSnapshot : null;
        return matches;
    }

    public void Reset(string reason)
    {
        BoundLobbyId = string.Empty;
        CanonicalSnapshot = null;
        Source = RoomGameplaySettingsSource.None;
        LastResetReason = reason ?? string.Empty;
    }
}
