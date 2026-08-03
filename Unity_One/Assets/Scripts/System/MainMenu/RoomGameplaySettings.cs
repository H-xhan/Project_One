using System;

public enum PostItLiarPromptSourceMode
{
    PresetDatabase = 0,
    CitizenAuthor = 1
}

public sealed class RoomGameplayCoreSettings
{
    public string GameModeId { get; }
    public string MapId { get; }
    public int MaxPlayers { get; }

    internal RoomGameplayCoreSettings(string gameModeId, string mapId, int maxPlayers)
    {
        GameModeId = gameModeId;
        MapId = mapId;
        MaxPlayers = maxPlayers;
    }
}

public sealed class PostItLiarRoomSettings
{
    public PostItLiarPromptSourceMode PromptSourceMode { get; }

    internal PostItLiarRoomSettings(PostItLiarPromptSourceMode promptSourceMode)
    {
        PromptSourceMode = promptSourceMode;
    }
}

public sealed class RoomGameplaySettingsSnapshot : IEquatable<RoomGameplaySettingsSnapshot>
{
    public int SchemaVersion { get; }
    public RoomGameplayCoreSettings Core { get; }
    public PostItLiarRoomSettings PostItLiar { get; }

    internal RoomGameplaySettingsSnapshot(
        int schemaVersion,
        RoomGameplayCoreSettings core,
        PostItLiarRoomSettings postItLiar)
    {
        SchemaVersion = schemaVersion;
        Core = core ?? throw new ArgumentNullException(nameof(core));
        PostItLiar = postItLiar ?? throw new ArgumentNullException(nameof(postItLiar));
    }

    public bool Equals(RoomGameplaySettingsSnapshot other)
    {
        return other != null &&
               SchemaVersion == other.SchemaVersion &&
               string.Equals(Core.GameModeId, other.Core.GameModeId, StringComparison.Ordinal) &&
               string.Equals(Core.MapId, other.Core.MapId, StringComparison.Ordinal) &&
               Core.MaxPlayers == other.Core.MaxPlayers &&
               PostItLiar.PromptSourceMode == other.PostItLiar.PromptSourceMode;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as RoomGameplaySettingsSnapshot);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = SchemaVersion;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Core.GameModeId);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Core.MapId);
            hash = (hash * 397) ^ Core.MaxPlayers;
            hash = (hash * 397) ^ (int)PostItLiar.PromptSourceMode;
            return hash;
        }
    }
}

public sealed class RoomGameplayCoreSettingsDraft
{
    public string GameModeId { get; set; }
    public string MapId { get; set; }
    public int MaxPlayers { get; set; }

    internal RoomGameplayCoreSettingsDraft()
    {
        ResetToDefaults();
    }

    internal void ResetToDefaults()
    {
        GameModeId = RoomGameplaySettingsValidator.CurrentGameModeId;
        MapId = RoomGameplaySettingsValidator.CurrentMapId;
        MaxPlayers = RoomGameplaySettingsValidator.CurrentMaxPlayers;
    }
}

public sealed class PostItLiarRoomSettingsDraft
{
    public PostItLiarPromptSourceMode PromptSourceMode { get; set; }

    internal PostItLiarRoomSettingsDraft()
    {
        ResetToDefaults();
    }

    internal void ResetToDefaults()
    {
        PromptSourceMode = PostItLiarPromptSourceMode.PresetDatabase;
    }
}

public sealed class RoomGameplaySettingsDraft
{
    public RoomGameplayCoreSettingsDraft Core { get; }
    public PostItLiarRoomSettingsDraft PostItLiar { get; }

    public RoomGameplaySettingsDraft()
    {
        Core = new RoomGameplayCoreSettingsDraft();
        PostItLiar = new PostItLiarRoomSettingsDraft();
    }

    public RoomGameplaySettingsSnapshot Freeze()
    {
        return RoomGameplaySettingsValidator.CreateSnapshot(
            Core.GameModeId,
            Core.MapId,
            Core.MaxPlayers,
            PostItLiar.PromptSourceMode);
    }

    public void ResetToDefaults()
    {
        Core.ResetToDefaults();
        PostItLiar.ResetToDefaults();
    }
}

public static class RoomGameplaySettingsValidator
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentGameModeId = "postit_liar_brawl";
    public const string CurrentMapId = "desk";
    public const int CurrentMaxPlayers = 4;

    public static RoomGameplaySettingsSnapshot CreateDefaultSnapshot()
    {
        return CreateSnapshot(
            CurrentGameModeId,
            CurrentMapId,
            CurrentMaxPlayers,
            PostItLiarPromptSourceMode.PresetDatabase);
    }

    public static RoomGameplaySettingsSnapshot CreateSnapshot(
        string gameModeId,
        string mapId,
        int maxPlayers,
        PostItLiarPromptSourceMode promptSourceMode)
    {
        return new RoomGameplaySettingsSnapshot(
            CurrentSchemaVersion,
            new RoomGameplayCoreSettings(
                NormalizeGameModeId(gameModeId),
                NormalizeMapId(mapId),
                NormalizeMaxPlayers(maxPlayers)),
            new PostItLiarRoomSettings(NormalizePromptSourceMode(promptSourceMode)));
    }

    public static string NormalizeGameModeId(string value)
    {
        _ = value;
        return CurrentGameModeId;
    }

    public static string NormalizeMapId(string value)
    {
        _ = value;
        return CurrentMapId;
    }

    public static int NormalizeMaxPlayers(int value)
    {
        return value == CurrentMaxPlayers ? value : CurrentMaxPlayers;
    }

    public static PostItLiarPromptSourceMode NormalizePromptSourceMode(
        PostItLiarPromptSourceMode value)
    {
        return value == PostItLiarPromptSourceMode.CitizenAuthor
            ? PostItLiarPromptSourceMode.CitizenAuthor
            : PostItLiarPromptSourceMode.PresetDatabase;
    }
}
