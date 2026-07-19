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

    private enum CatalogPreviewFailure
    {
        None = 0,
        UnsupportedTemplate = 1,
        MissingCatalog = 2,
        MissingEntry = 3,
        TypeMismatch = 4,
        InvalidSprite = 5
    }

    private struct CatalogWarningState
    {
        public bool IsInitialized;
        public int PostItId;
        public int VisualId;
        public PostItType Type;
        public CatalogPreviewFailure Failure;
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
    [SerializeField] private PostItVisualCatalogSO visualCatalog;

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexturePropertyId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapScaleOffsetPropertyId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTextureScaleOffsetPropertyId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int VisualIdPropertyId = Shader.PropertyToID("_PostItVisualId");
    private static readonly int TypePropertyId = Shader.PropertyToID("_PostItType");

    private sealed class VisualSlot
    {
        public GameObject Instance;
        public Renderer[] Renderers;
        public MaterialPropertyBlock PropertyBlock;
        public bool SupportsCatalogPreview;
        public CatalogWarningState WarningState;
        public PostItPublicVisualData Data;
        public bool HasData;
    }

    private sealed class WorldVisualSlot
    {
        public GameObject Instance;
        public Renderer[] Renderers;
        public MaterialPropertyBlock PropertyBlock;
        public bool SupportsCatalogPreview;
        public CatalogWarningState WarningState;
        public PostItWorldDropData Data;
        public bool HasData;
    }

    private PlayerPostItInventory _boundInventory;
    private PostItRoundManager _boundRoundManager;
    private Transform[] _resolvedAnchors = Array.Empty<Transform>();
    private VisualSlot[] _visualSlots = Array.Empty<VisualSlot>();
    private readonly List<WorldVisualSlot> _worldVisualSlots = new List<WorldVisualSlot>();
    private Transform _worldVisualRoot;
    private bool _poolInitialized;
    private bool _hasWarnedMissingTemplate;
    private int _visibleCount;
    private int _worldVisibleCount;
    private int _lastOverflowCount = -1;
    private float _nextWorldManagerResolveTime;

    private const float WorldManagerResolveInterval = 0.5f;
    private const float PreviewAcquiredTintMultiplier = 0.25f;
    private const float EffectPreviewTypeTintStrength = 0.18f;

    public PlayerPostItInventory BoundInventory => _boundInventory;
    public int AnchorCount => _resolvedAnchors.Length;
    public int VisibleCount => _visibleCount;
    public int WorldVisibleCount => _worldVisibleCount;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializePool();
        BindInventory(targetInventory);
        TryBindWorldDropManager(true);
    }

    private void OnDisable()
    {
        UnbindWorldDropManager();
        DestroyWorldDropPool();
        UnbindInventory();
        HideAllVisuals();
    }

    private void Update()
    {
        if (!CanPresentWorldDrops())
        {
            if (_boundRoundManager != null || _worldVisualRoot != null)
            {
                UnbindWorldDropManager();
                DestroyWorldDropPool();
            }

            return;
        }

        if (_boundRoundManager == null)
        {
            HideAllWorldDropVisuals();
            if (Time.unscaledTime >= _nextWorldManagerResolveTime)
            {
                TryBindWorldDropManager(false);
            }
        }
    }

    public void ForceRefresh()
    {
        RefreshVisuals();
        RefreshWorldDropVisuals();
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

    private bool CanPresentWorldDrops()
    {
        return isActiveAndEnabled &&
               _boundInventory != null &&
               _boundInventory.IsSpawned &&
               _boundInventory.IsOwner;
    }

    private void TryBindWorldDropManager(bool forceResolve)
    {
        if (!CanPresentWorldDrops())
            return;

        if (_boundRoundManager != null)
        {
            RefreshWorldDropVisuals();
            return;
        }

        if (!forceResolve && Time.unscaledTime < _nextWorldManagerResolveTime)
            return;

        _nextWorldManagerResolveTime = Time.unscaledTime + WorldManagerResolveInterval;
        PostItRoundManager manager = FindFirstObjectByType<PostItRoundManager>();
        if (manager == null)
            return;

        _boundRoundManager = manager;
        _boundRoundManager.WorldDropsChanged += OnWorldDropsChanged;
        RefreshWorldDropVisuals();
        Log($"Bound world-drop manager. count={_boundRoundManager.WorldDropCount}");
    }

    private void UnbindWorldDropManager()
    {
        if (_boundRoundManager != null)
        {
            _boundRoundManager.WorldDropsChanged -= OnWorldDropsChanged;
        }

        _boundRoundManager = null;
        HideAllWorldDropVisuals();
    }

    private void OnWorldDropsChanged()
    {
        RefreshWorldDropVisuals();
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
                SupportsCatalogPreview = SupportsCatalogPreviewTextures(renderers),
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

    private void RefreshWorldDropVisuals()
    {
        HideAllWorldDropVisuals();
        if (!CanPresentWorldDrops() || _boundRoundManager == null)
            return;

        IReadOnlyList<PostItWorldDropData> items = _boundRoundManager.WorldDrops;
        if (!EnsureWorldDropPoolCapacity(items.Count))
            return;

        for (int i = 0; i < items.Count; i++)
        {
            PostItWorldDropData data = items[i];
            WorldVisualSlot slot = _worldVisualSlots[i];
            if (!data.IsValid || slot == null || slot.Instance == null)
                continue;

            slot.Data = data;
            slot.HasData = true;
            slot.Instance.transform.SetPositionAndRotation(data.Position, data.Rotation);
            ApplyVisualProperties(
                slot.Renderers,
                slot.PropertyBlock,
                data.PostItId,
                data.Type,
                data.VisualId,
                data.IsOriginalOwnerItem,
                slot.SupportsCatalogPreview,
                ref slot.WarningState);
            slot.Instance.SetActive(true);
            _worldVisibleCount++;
        }

        Log($"Refreshed world-drop visuals. visible={_worldVisibleCount}, public={items.Count}");
    }

    private bool EnsureWorldDropPoolCapacity(int requiredCount)
    {
        bool hasStalePool = _worldVisualSlots.Count > 0 && _worldVisualRoot == null;
        for (int i = 0; !hasStalePool && i < _worldVisualSlots.Count; i++)
        {
            WorldVisualSlot slot = _worldVisualSlots[i];
            hasStalePool = slot == null || slot.Instance == null;
        }

        if (hasStalePool)
        {
            DestroyWorldDropPool();
        }

        if (requiredCount <= _worldVisualSlots.Count)
            return true;

        if (visualTemplate == null)
        {
            WarnMissingTemplate();
            return false;
        }

        if (_worldVisualRoot == null)
        {
            GameObject rootObject = new GameObject("PostItWorldDrops_Local");
            _worldVisualRoot = rootObject.transform;
            _worldVisualRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _worldVisualRoot.localScale = Vector3.one;
        }

        while (_worldVisualSlots.Count < requiredCount)
        {
            int poolIndex = _worldVisualSlots.Count;
            GameObject instance = Instantiate(visualTemplate, _worldVisualRoot, false);
            instance.name = $"{visualTemplate.name}_World_{poolIndex}";

            if (HasForbiddenComponents(instance))
            {
                LogWarning($"Rejected world visual template with gameplay or network components. poolIndex={poolIndex}");
                Destroy(instance);
                return false;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                LogWarning($"World visual template has no Renderer. poolIndex={poolIndex}");
                Destroy(instance);
                return false;
            }

            instance.SetActive(false);
            _worldVisualSlots.Add(new WorldVisualSlot
            {
                Instance = instance,
                Renderers = renderers,
                PropertyBlock = new MaterialPropertyBlock(),
                SupportsCatalogPreview = SupportsCatalogPreviewTextures(renderers),
                Data = PostItWorldDropData.Invalid,
                HasData = false
            });
        }

        return true;
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

    private void HideAllWorldDropVisuals()
    {
        _worldVisibleCount = 0;
        for (int i = 0; i < _worldVisualSlots.Count; i++)
        {
            WorldVisualSlot slot = _worldVisualSlots[i];
            if (slot == null)
                continue;

            slot.Data = PostItWorldDropData.Invalid;
            slot.HasData = false;
            if (slot.Instance != null && slot.Instance.activeSelf)
            {
                slot.Instance.SetActive(false);
            }
        }
    }

    private void DestroyWorldDropPool()
    {
        _worldVisibleCount = 0;
        _worldVisualSlots.Clear();
        if (_worldVisualRoot != null)
        {
            Destroy(_worldVisualRoot.gameObject);
            _worldVisualRoot = null;
        }
    }

    private void ApplyVisualData(VisualSlot slot, PostItPublicVisualData data)
    {
        ApplyVisualProperties(
            slot.Renderers,
            slot.PropertyBlock,
            data.PostItId,
            data.Type,
            data.VisualId,
            data.IsOriginalOwnerItem,
            slot.SupportsCatalogPreview,
            ref slot.WarningState);
    }

    private void ApplyVisualProperties(
        Renderer[] renderers,
        MaterialPropertyBlock propertyBlock,
        int postItId,
        PostItType type,
        int visualId,
        bool isOriginalOwnerItem,
        bool supportsCatalogPreview,
        ref CatalogWarningState warningState)
    {
        propertyBlock.Clear();
        bool hasCatalogPreview = TryApplyCatalogPreview(
            propertyBlock,
            postItId,
            type,
            visualId,
            supportsCatalogPreview,
            ref warningState);

        Color typeColor = ResolveTypeColor(type);
        Color color = hasCatalogPreview
            ? ResolvePreviewColor(type, typeColor)
            : typeColor;
        if (!isOriginalOwnerItem)
        {
            float tintStrength = hasCatalogPreview
                ? acquiredTintStrength * PreviewAcquiredTintMultiplier
                : acquiredTintStrength;
            color = Color.Lerp(color, acquiredTint, tintStrength);
        }

        propertyBlock.SetColor(BaseColorPropertyId, color);
        propertyBlock.SetColor(ColorPropertyId, color);
        propertyBlock.SetFloat(VisualIdPropertyId, visualId);
        propertyBlock.SetFloat(TypePropertyId, (int)type);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null)
            {
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }

    private bool TryApplyCatalogPreview(
        MaterialPropertyBlock propertyBlock,
        int postItId,
        PostItType type,
        int visualId,
        bool supportsCatalogPreview,
        ref CatalogWarningState warningState)
    {
        CatalogPreviewFailure failure = CatalogPreviewFailure.None;
        Texture previewTexture = null;
        Vector4 scaleOffset = default;

        if (!supportsCatalogPreview)
        {
            failure = CatalogPreviewFailure.UnsupportedTemplate;
        }
        else if (visualCatalog == null)
        {
            failure = CatalogPreviewFailure.MissingCatalog;
        }
        else if (!visualCatalog.TryGetEntryByVisualId(
                     visualId,
                     out PostItVisualCatalogSO.Entry entry))
        {
            failure = CatalogPreviewFailure.MissingEntry;
        }
        else if (entry.Type != type)
        {
            failure = CatalogPreviewFailure.TypeMismatch;
        }
        else if (!TryGetSpriteTextureData(
                     entry.PreviewSprite,
                     out previewTexture,
                     out scaleOffset))
        {
            failure = CatalogPreviewFailure.InvalidSprite;
        }

        if (failure != CatalogPreviewFailure.None)
        {
            if (!warningState.IsInitialized ||
                warningState.PostItId != postItId ||
                warningState.VisualId != visualId ||
                warningState.Type != type ||
                warningState.Failure != failure)
            {
                warningState = new CatalogWarningState
                {
                    IsInitialized = true,
                    PostItId = postItId,
                    VisualId = visualId,
                    Type = type,
                    Failure = failure
                };
                LogWarning(
                    $"Catalog preview unavailable; using color fallback. " +
                    $"postItId={postItId}, visualId={visualId}, " +
                    $"type={type}, failure={failure}");
            }

            return false;
        }

        warningState = default;
        propertyBlock.SetTexture(BaseMapPropertyId, previewTexture);
        propertyBlock.SetTexture(MainTexturePropertyId, previewTexture);
        propertyBlock.SetVector(BaseMapScaleOffsetPropertyId, scaleOffset);
        propertyBlock.SetVector(MainTextureScaleOffsetPropertyId, scaleOffset);
        return true;
    }

    private static Color ResolvePreviewColor(PostItType type, Color typeColor)
    {
        if (type == PostItType.Drawing)
            return Color.white;

        return Color.Lerp(Color.white, typeColor, EffectPreviewTypeTintStrength);
    }

    private static bool TryGetSpriteTextureData(
        Sprite sprite,
        out Texture texture,
        out Vector4 scaleOffset)
    {
        texture = null;
        scaleOffset = new Vector4(1f, 1f, 0f, 0f);
        if (sprite == null || sprite.texture == null)
            return false;

        if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            return false;

        Rect textureRect;
        try
        {
            textureRect = sprite.textureRect;
        }
        catch (UnityException)
        {
            return false;
        }

        Texture2D spriteTexture = sprite.texture;
        if (spriteTexture.width <= 0 ||
            spriteTexture.height <= 0 ||
            textureRect.width <= 0f ||
            textureRect.height <= 0f)
        {
            return false;
        }

        texture = spriteTexture;
        scaleOffset = new Vector4(
            textureRect.width / spriteTexture.width,
            textureRect.height / spriteTexture.height,
            textureRect.x / spriteTexture.width,
            textureRect.y / spriteTexture.height);
        return true;
    }

    private static bool SupportsCatalogPreviewTextures(Renderer[] renderers)
    {
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null &&
                    (material.HasProperty(BaseMapPropertyId) ||
                     material.HasProperty(MainTexturePropertyId)))
                {
                    return true;
                }
            }
        }

        return false;
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
