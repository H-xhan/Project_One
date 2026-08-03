using System;
using System.Collections.Generic;
using System.Globalization;

public static class RoomGameplaySettingsCodec
{
    public const string SchemaVersionKey = "room_settings_version";
    public const string GameModeIdKey = "game_mode_id";
    public const string MapIdKey = "map_id";
    public const string MaxPlayersKey = "max_players";
    public const string PostItPromptModeKey = "postit_prompt_mode";

    public const string PresetPromptModeId = "preset";
    public const string CitizenAuthorPromptModeId = "citizen_author";

    public static IReadOnlyDictionary<string, string> Serialize(
        RoomGameplaySettingsSnapshot snapshot)
    {
        RoomGameplaySettingsSnapshot normalized = snapshot ??
            RoomGameplaySettingsValidator.CreateDefaultSnapshot();

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaVersionKey] = normalized.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            [GameModeIdKey] = normalized.Core.GameModeId,
            [MapIdKey] = normalized.Core.MapId,
            [MaxPlayersKey] = normalized.Core.MaxPlayers.ToString(CultureInfo.InvariantCulture),
            [PostItPromptModeKey] = ToPromptModeId(normalized.PostItLiar.PromptSourceMode)
        };
    }

    public static RoomGameplaySettingsSnapshot Deserialize(
        IReadOnlyDictionary<string, string> metadata)
    {
        RoomGameplaySettingsSnapshot defaults =
            RoomGameplaySettingsValidator.CreateDefaultSnapshot();

        if (metadata == null || metadata.Count == 0)
            return defaults;

        string gameModeId = ReadValue(metadata, GameModeIdKey, defaults.Core.GameModeId);
        string mapId = ReadValue(metadata, MapIdKey, defaults.Core.MapId);
        int maxPlayers = ParseMaxPlayers(
            ReadValue(metadata, MaxPlayersKey, string.Empty),
            defaults.Core.MaxPlayers);
        PostItLiarPromptSourceMode promptSourceMode = ParsePromptMode(
            ReadValue(metadata, PostItPromptModeKey, string.Empty));

        // The current client safely consumes known fields from higher schema versions
        // and ignores unknown additions. Canonical snapshots always use our schema.
        _ = ParseSchemaVersion(
            ReadValue(metadata, SchemaVersionKey, string.Empty),
            defaults.SchemaVersion);

        return RoomGameplaySettingsValidator.CreateSnapshot(
            gameModeId,
            mapId,
            maxPlayers,
            promptSourceMode);
    }

    public static string ToPromptModeId(PostItLiarPromptSourceMode mode)
    {
        return mode == PostItLiarPromptSourceMode.CitizenAuthor
            ? CitizenAuthorPromptModeId
            : PresetPromptModeId;
    }

    public static PostItLiarPromptSourceMode ParsePromptMode(string value)
    {
        return string.Equals(value, CitizenAuthorPromptModeId, StringComparison.Ordinal)
            ? PostItLiarPromptSourceMode.CitizenAuthor
            : PostItLiarPromptSourceMode.PresetDatabase;
    }

    private static string ReadValue(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback)
    {
        if (!metadata.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim();
    }

    private static int ParseSchemaVersion(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
               parsed > 0
            ? parsed
            : fallback;
    }

    private static int ParseMaxPlayers(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }
}
