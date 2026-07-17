using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerPostItWorldPresenter : MonoBehaviour
{
    [Serializable]
    private struct RuntimeAnchorDefinition
    {
        public string Name;
        public string BonePath;
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public Vector3 LocalScale;
    }

    [SerializeField] private PlayerPostItInventory targetInventory;
    [SerializeField] private GameObject visualTemplate;
    [SerializeField] private Transform[] anchors = Array.Empty<Transform>();
    [SerializeField] private Transform animatedModelRoot;
    [SerializeField] private RuntimeAnchorDefinition[] runtimeAnchors =
        Array.Empty<RuntimeAnchorDefinition>();
    [SerializeField] private Color drawingColor = new Color(1f, 0.78f, 0.18f, 1f);
    [SerializeField] private Color messageColor = new Color(0.34f, 0.72f, 1f, 1f);
    [SerializeField] private Color bonusColor = new Color(0.38f, 0.82f, 0.42f, 1f);
    [SerializeField] private Color penaltyColor = new Color(1f, 0.32f, 0.28f, 1f);
    [SerializeField] private Color acquiredTint = new Color(1f, 0.46f, 0.12f, 1f);
    [SerializeField, Range(0f, 1f)] private float acquiredTintStrength = 0.35f;
    [SerializeField] private bool debugLogs = false;

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int VisualIdPropertyId = Shader.PropertyToID("_PostItVisualId");
    private static readonly int TypePropertyId = Shader.PropertyToID("_PostItType");

    private sealed class VisualSlot
    {
        public GameObject Instance;
        public Renderer[] Renderers;
        public MaterialPropertyBlock PropertyBlock;
        public PostItPublicVisualData Data;
        public bool HasData;
    }

    private PlayerPostItInventory _boundInventory;
    private Transform[] _resolvedAnchors = Array.Empty<Transform>();
    private VisualSlot[] _visualSlots = Array.Empty<VisualSlot>();
    private bool _poolInitialized;
    private bool _hasWarnedMissingTemplate;
    private int _visibleCount;
    private int _lastOverflowCount = -1;

    public PlayerPostItInventory BoundInventory => _boundInventory;
    public int AnchorCount => _resolvedAnchors.Length;
    public int VisibleCount => _visibleCount;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializePool();
        BindInventory(targetInventory);
    }

    private void OnDisable()
    {
        UnbindInventory();
        HideAllVisuals();
    }

    public void ForceRefresh()
    {
        RefreshVisuals();
    }

    public bool TryGetClosestVisiblePostIt(
        Ray ray,
        float maxRayDistance,
        float maxDistanceFromRay,
        out PostItPublicVisualData data)
    {
        data = PostItPublicVisualData.Invalid;

        float directionSqrMagnitude = ray.direction.sqrMagnitude;
        if (directionSqrMagnitude <= Mathf.Epsilon ||
            maxRayDistance < 0f ||
            maxDistanceFromRay < 0f)
        {
            return false;
        }

        Vector3 direction = ray.direction / Mathf.Sqrt(directionSqrMagnitude);
        float maxDistanceFromRaySqr = maxDistanceFromRay * maxDistanceFromRay;
        float bestDistanceFromRaySqr = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < _visualSlots.Length; i++)
        {
            VisualSlot slot = _visualSlots[i];
            if (slot == null ||
                !slot.HasData ||
                slot.Instance == null ||
                !slot.Instance.activeInHierarchy)
            {
                continue;
            }

            Vector3 toVisual = slot.Instance.transform.position - ray.origin;
            float distanceAlongRay = Vector3.Dot(toVisual, direction);
            if (distanceAlongRay < 0f || distanceAlongRay > maxRayDistance)
            {
                continue;
            }

            Vector3 closestPoint = ray.origin + direction * distanceAlongRay;
            float distanceFromRaySqr =
                (slot.Instance.transform.position - closestPoint).sqrMagnitude;
            if (distanceFromRaySqr > maxDistanceFromRaySqr ||
                distanceFromRaySqr >= bestDistanceFromRaySqr)
            {
                continue;
            }

            bestDistanceFromRaySqr = distanceFromRaySqr;
            data = slot.Data;
            found = true;
        }

        return found;
    }

    public bool TryGetVisiblePostItWorldPosition(
        int postItId,
        out Vector3 worldPosition)
    {
        worldPosition = default;

        for (int i = 0; i < _visualSlots.Length; i++)
        {
            VisualSlot slot = _visualSlots[i];
            if (slot == null ||
                !slot.HasData ||
                slot.Data.PostItId != postItId ||
                slot.Instance == null ||
                !slot.Instance.activeInHierarchy)
            {
                continue;
            }

            worldPosition = slot.Instance.transform.position;
            return true;
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (targetInventory != null)
        {
            return;
        }

        targetInventory = GetComponent<PlayerPostItInventory>();
        if (targetInventory == null)
        {
            targetInventory = GetComponentInParent<PlayerPostItInventory>();
        }

        if (targetInventory == null)
        {
            targetInventory = GetComponentInChildren<PlayerPostItInventory>(true);
        }

        if (animatedModelRoot == null)
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animatedModelRoot = animator.transform;
            }
        }
    }

    private void BindInventory(PlayerPostItInventory inventory)
    {
        if (_boundInventory == inventory)
        {
            RefreshVisuals();
            return;
        }

        UnbindInventory();
        if (inventory == null)
        {
            HideAllVisuals();
            return;
        }

        _boundInventory = inventory;
        _boundInventory.PublicVisualsChanged += OnPublicVisualsChanged;
        RefreshVisuals();
        Log($"Bound inventory. publicCount={_boundInventory.PublicVisualCount}");
    }

    private void UnbindInventory()
    {
        if (_boundInventory == null)
        {
            return;
        }

        _boundInventory.PublicVisualsChanged -= OnPublicVisualsChanged;
        _boundInventory = null;
        Log("Unbound inventory.");
    }

    private void OnPublicVisualsChanged()
    {
        RefreshVisuals();
    }

    private void InitializePool()
    {
        if (_poolInitialized)
        {
            return;
        }

        _poolInitialized = true;
        ResolveAnchors();
        int anchorCount = AnchorCount;
        _visualSlots = new VisualSlot[anchorCount];

        if (visualTemplate == null)
        {
            WarnMissingTemplate();
            return;
        }

        for (int i = 0; i < anchorCount; i++)
        {
            Transform anchor = _resolvedAnchors[i];
            if (anchor == null)
            {
                LogWarning($"Anchor is missing. anchorIndex={i}");
                continue;
            }

            GameObject instance = Instantiate(visualTemplate, anchor, false);
            instance.name = $"{visualTemplate.name}_Slot_{i}";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            if (HasForbiddenComponents(instance))
            {
                LogWarning($"Rejected visual template with gameplay or network components. anchorIndex={i}");
                Destroy(instance);
                continue;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                LogWarning($"Visual template has no Renderer. anchorIndex={i}");
                Destroy(instance);
                continue;
            }

            instance.SetActive(false);
            _visualSlots[i] = new VisualSlot
            {
                Instance = instance,
                Renderers = renderers,
                PropertyBlock = new MaterialPropertyBlock(),
                Data = PostItPublicVisualData.Invalid,
                HasData = false
            };
        }
    }

    private void ResolveAnchors()
    {
        if (anchors != null && anchors.Length > 0)
        {
            _resolvedAnchors = anchors;
            return;
        }

        if (animatedModelRoot == null || runtimeAnchors == null || runtimeAnchors.Length == 0)
        {
            _resolvedAnchors = Array.Empty<Transform>();
            return;
        }

        _resolvedAnchors = new Transform[runtimeAnchors.Length];
        for (int i = 0; i < runtimeAnchors.Length; i++)
        {
            RuntimeAnchorDefinition definition = runtimeAnchors[i];
            if (string.IsNullOrWhiteSpace(definition.BonePath))
            {
                LogWarning($"Runtime anchor bone path is missing. anchorIndex={i}");
                continue;
            }

            Transform bone = animatedModelRoot.Find(definition.BonePath);
            if (bone == null)
            {
                LogWarning(
                    $"Runtime anchor bone was not found. " +
                    $"anchorIndex={i}, bonePath={definition.BonePath}");
                continue;
            }

            string anchorName = string.IsNullOrWhiteSpace(definition.Name)
                ? $"PostItAnchor_{i}"
                : definition.Name;
            GameObject anchorObject = new GameObject(anchorName);
            Transform anchor = anchorObject.transform;
            anchor.SetParent(bone, false);
            anchor.localPosition = definition.LocalPosition;
            anchor.localRotation = Quaternion.Euler(definition.LocalEulerAngles);
            anchor.localScale = definition.LocalScale == Vector3.zero
                ? Vector3.one
                : definition.LocalScale;
            _resolvedAnchors[i] = anchor;
        }
    }

    private static bool HasForbiddenComponents(GameObject instance)
    {
        return instance.GetComponentInChildren<NetworkObject>(true) != null ||
               instance.GetComponentInChildren<NetworkBehaviour>(true) != null ||
               instance.GetComponentInChildren<Rigidbody>(true) != null ||
               instance.GetComponentInChildren<Collider>(true) != null ||
               instance.GetComponentInChildren<PlayerPostItWorldPresenter>(true) != null;
    }

    private void RefreshVisuals()
    {
        InitializePool();
        HideAllVisuals();

        if (_boundInventory == null || _visualSlots.Length == 0)
        {
            return;
        }

        IReadOnlyList<PostItPublicVisualData> items = _boundInventory.PublicVisualItems;
        int overflowCount = 0;

        for (int i = 0; i < items.Count; i++)
        {
            PostItPublicVisualData data = items[i];
            int slotIndex = data.SlotIndex;
            if (!data.IsValid ||
                slotIndex < 0 ||
                slotIndex >= _visualSlots.Length ||
                _visualSlots[slotIndex] == null ||
                _visualSlots[slotIndex].HasData)
            {
                overflowCount++;
                continue;
            }

            VisualSlot slot = _visualSlots[slotIndex];
            slot.Data = data;
            slot.HasData = true;
            ApplyVisualData(slot, data);
            slot.Instance.SetActive(true);
            _visibleCount++;
        }

        ReportOverflowIfChanged(overflowCount, items.Count);
        Log($"Refreshed visuals. visible={_visibleCount}, public={items.Count}, anchors={_visualSlots.Length}");
    }

    private void HideAllVisuals()
    {
        _visibleCount = 0;

        for (int i = 0; i < _visualSlots.Length; i++)
        {
            VisualSlot slot = _visualSlots[i];
            if (slot == null)
            {
                continue;
            }

            slot.Data = PostItPublicVisualData.Invalid;
            slot.HasData = false;
            if (slot.Instance != null && slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(false);
            }
        }
    }

    private void ApplyVisualData(VisualSlot slot, PostItPublicVisualData data)
    {
        Color color = ResolveTypeColor(data.Type);
        if (!data.IsOriginalOwnerItem)
        {
            color = Color.Lerp(color, acquiredTint, acquiredTintStrength);
        }

        MaterialPropertyBlock propertyBlock = slot.PropertyBlock;
        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorPropertyId, color);
        propertyBlock.SetColor(ColorPropertyId, color);
        propertyBlock.SetFloat(VisualIdPropertyId, data.VisualId);
        propertyBlock.SetFloat(TypePropertyId, (int)data.Type);

        for (int i = 0; i < slot.Renderers.Length; i++)
        {
            Renderer renderer = slot.Renderers[i];
            if (renderer != null)
            {
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }

    private Color ResolveTypeColor(PostItType type)
    {
        switch (type)
        {
            case PostItType.Message:
                return messageColor;
            case PostItType.Bonus:
                return bonusColor;
            case PostItType.Penalty:
                return penaltyColor;
            default:
                return drawingColor;
        }
    }

    private void ReportOverflowIfChanged(int overflowCount, int publicCount)
    {
        if (_lastOverflowCount == overflowCount)
        {
            return;
        }

        _lastOverflowCount = overflowCount;
        if (overflowCount > 0)
        {
            LogWarning(
                $"Public visuals exceed valid unique anchors. " +
                $"overflow={overflowCount}, public={publicCount}, anchors={_visualSlots.Length}");
        }
    }

    private void WarnMissingTemplate()
    {
        if (_hasWarnedMissingTemplate)
        {
            return;
        }

        _hasWarnedMissingTemplate = true;
        LogWarning("Visual template is missing.");
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[PostItWorld] {message}", this);
        }
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"[PostItWorld] {message}", this);
    }

    private void OnValidate()
    {
        acquiredTintStrength = Mathf.Clamp01(acquiredTintStrength);
        if (anchors == null)
        {
            anchors = Array.Empty<Transform>();
        }

        if (runtimeAnchors == null)
        {
            runtimeAnchors = Array.Empty<RuntimeAnchorDefinition>();
        }
    }
}
