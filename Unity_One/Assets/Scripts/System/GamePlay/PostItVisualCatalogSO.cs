using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "PostItVisualCatalog",
    menuName = "Project One/Post-it Visual Catalog")]
public sealed class PostItVisualCatalogSO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public int VisualId;
        public PostItTopicId TopicId;
        public PostItType Type;
        public Sprite PreviewSprite;
        public string DisplayName;
        public bool Enabled;
    }

    public const bool AllowsMultipleDrawingVisualsPerTopic = true;

    [SerializeField]
    private Entry[] entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public bool TryGetEntryByVisualId(int visualId, out Entry entry)
    {
        entry = default;
        if (visualId <= 0 || entries == null)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry candidate = entries[i];
            if (candidate.VisualId != visualId)
            {
                continue;
            }

            if (found || !IsRuntimeUsable(candidate))
            {
                entry = default;
                return false;
            }

            entry = candidate;
            found = true;
        }

        return found;
    }

    public bool TryGetDrawingEntry(PostItTopicId topicId, out Entry entry)
    {
        entry = default;
        if (!IsSupportedDrawingTopic(topicId) || entries == null)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry candidate = entries[i];
            if (candidate.Type != PostItType.Drawing ||
                candidate.TopicId != topicId ||
                !IsRuntimeUsable(candidate))
            {
                continue;
            }

            if (!found || candidate.VisualId < entry.VisualId)
            {
                entry = candidate;
                found = true;
            }
        }

        return found;
    }

    public bool TryGetFirstEntryByTopic(PostItTopicId topicId, out Entry entry)
    {
        entry = default;
        if (entries == null)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry candidate = entries[i];
            if (candidate.TopicId != topicId || !IsRuntimeUsable(candidate))
            {
                continue;
            }

            if (!found || candidate.VisualId < entry.VisualId)
            {
                entry = candidate;
                found = true;
            }
        }

        return found;
    }

    public int GetEnabledDrawingTopics(List<PostItTopicId> destination)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.Clear();
        if (entries == null)
        {
            return 0;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            Entry candidate = entries[i];
            if (candidate.Type != PostItType.Drawing ||
                !IsRuntimeUsable(candidate) ||
                destination.Contains(candidate.TopicId))
            {
                continue;
            }

            int insertIndex = destination.Count;
            while (insertIndex > 0 &&
                   (int)destination[insertIndex - 1] > (int)candidate.TopicId)
            {
                insertIndex--;
            }

            destination.Insert(insertIndex, candidate.TopicId);
        }

        return destination.Count;
    }

    public bool ValidateCatalog(out string validationError)
    {
        validationError = null;
        if (entries == null || entries.Length == 0)
        {
            validationError = "Catalog must contain at least one entry.";
            return false;
        }

        int enabledCount = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry.VisualId <= 0)
            {
                validationError = $"Entry {i} has an invalid VisualId.";
                return false;
            }

            if (entry.Type == PostItType.None)
            {
                validationError = $"Entry {i} has no Post-it type.";
                return false;
            }

            if (entry.Type == PostItType.Drawing && !IsSupportedDrawingTopic(entry.TopicId))
            {
                validationError = $"Drawing entry {i} has an unsupported TopicId.";
                return false;
            }

            if (entry.PreviewSprite == null)
            {
                validationError = $"Entry {i} has no preview Sprite.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                validationError = $"Entry {i} has no display name.";
                return false;
            }

            for (int otherIndex = i + 1; otherIndex < entries.Length; otherIndex++)
            {
                if (entry.VisualId == entries[otherIndex].VisualId)
                {
                    validationError = $"VisualId {entry.VisualId} is duplicated.";
                    return false;
                }
            }

            if (entry.Enabled)
            {
                enabledCount++;
            }
        }

        if (enabledCount == 0)
        {
            validationError = "Catalog must contain at least one enabled entry.";
            return false;
        }

        return true;
    }

    public static bool IsSupportedDrawingTopic(PostItTopicId topicId)
    {
        return topicId == PostItTopicId.Animal ||
               topicId == PostItTopicId.Food ||
               topicId == PostItTopicId.Object ||
               topicId == PostItTopicId.Emotion;
    }

    private static bool IsRuntimeUsable(Entry entry)
    {
        if (!entry.Enabled ||
            entry.VisualId <= 0 ||
            entry.Type == PostItType.None ||
            entry.PreviewSprite == null)
        {
            return false;
        }

        return entry.Type != PostItType.Drawing || IsSupportedDrawingTopic(entry.TopicId);
    }
}
