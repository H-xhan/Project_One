using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Visual Scale")]
    [SerializeField, Min(0.0001f), Tooltip(
        "캐릭터 몸에 부착되는 포스트잇 시각 크기 배율입니다. " +
        "캐릭터 Visual 부모 스케일과 함께 최종 크기가 결정됩니다.")]
    private float bodyVisualScaleMultiplier = 1f;
    [SerializeField, Min(0.0001f), Tooltip(
        "책상 위 World Drop 포스트잇 시각 크기 배율입니다.")]
    private float worldVisualScaleMultiplier = 1f;

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexturePropertyId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapScaleOffsetPropertyId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTextureScaleOffsetPropertyId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int VisualIdPropertyId = Shader.PropertyToID("_PostItVisualId");
    private static readonly int TypePropertyId = Shader.PropertyToID("_PostItType");
    private static readonly int SurfacePropertyId = Shader.PropertyToID("_Surface");
    private static readonly int BlendPropertyId = Shader.PropertyToID("_Blend");
    private static readonly int AlphaClipPropertyId = Shader.PropertyToID("_AlphaClip");
    private static readonly int SrcBlendPropertyId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendPropertyId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWritePropertyId = Shader.PropertyToID("_ZWrite");
    private static readonly int CullPropertyId = Shader.PropertyToID("_Cull");

    private const float PaperFlyDuration = 0.3f;
    private const float PaperFlyMatchWindow = 0.5f;
    private const float PaperFlyArcHeight = 0.18f;
    private const float PaperFlyFadeStart = 0.55f;
    private const int PaperFlyPoolCapacity = 4;
    private const int PaperFlyMaxFrameSeparation = 1;

    private sealed class VisualSlot
    {
        public GameObject Instance;
        public Renderer[] Renderers;
        public MaterialPropertyBlock PropertyBlock;
        public bool SupportsCatalogPreview;
        public CatalogWarningState WarningState;
        public PostItPublicVisualData Data;
        public Vector3 SourceScale;
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
        public Vector3 SourceScale;
        public bool HasData;
    }

    private sealed class PaperFlyEndpoint
    {
        public PlayerPostItWorldPresenter Presenter;
        public PostItPublicVisualData Data;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public float ObservedAt;
        public int ObservedFrame;
    }

    private sealed class PaperFlySlot
    {
        public GameObject Instance;
        public Renderer[] Renderers;
        public MaterialPropertyBlock PropertyBlock;
        public bool SupportsCatalogPreview;
        public CatalogWarningState WarningState;
        public Color BaseColor;
        public PlayerPostItWorldPresenter DestinationPresenter;
        public int DestinationPostItId;
        public Vector3 SourcePosition;
        public Quaternion SourceRotation;
        public Vector3 SourceScale;
        public Vector3 DestinationPosition;
        public Quaternion DestinationRotation;
        public Vector3 DestinationScale;
        public float StartedAt;
        public bool Active;
    }

    private static readonly List<PaperFlyEndpoint> PendingPaperFlyRemovals =
        new List<PaperFlyEndpoint>();
    private static readonly List<PaperFlyEndpoint> PendingPaperFlyAdditions =
        new List<PaperFlyEndpoint>();
    private static int _lastPendingPaperFlyPruneFrame = -1;

    private PlayerPostItInventory _boundInventory;
    private PostItRoundManager _boundRoundManager;
    private Transform[] _resolvedAnchors = Array.Empty<Transform>();
    private VisualSlot[] _visualSlots = Array.Empty<VisualSlot>();
    private readonly List<WorldVisualSlot> _worldVisualSlots = new List<WorldVisualSlot>();
    private readonly List<PostItPublicVisualData> _publicVisualSnapshot =
        new List<PostItPublicVisualData>();
    private readonly List<PaperFlySlot> _paperFlySlots = new List<PaperFlySlot>();
    private readonly List<Material> _paperFlyRuntimeMaterials = new List<Material>();
    private Transform _worldVisualRoot;
    private Transform _paperFlyRoot;
    private GameStateManager _paperFlyGameStateManager;
    private bool _poolInitialized;
    private bool _publicVisualSnapshotWasSpawned;
    private bool _wasPaperFlyPlaying;
    private bool _hasWarnedMissingTemplate;
    private int _visibleCount;
    private int _worldVisibleCount;
    private int _lastOverflowCount = -1;
    private float _nextWorldManagerResolveTime;
    private float _nextPaperFlyGameStateResolveTime;

    private const float WorldManagerResolveInterval = 0.5f;
    private const float PreviewAcquiredTintMultiplier = 0.25f;
    private const float EffectPreviewTypeTintStrength = 0.18f;

    public PlayerPostItInventory BoundInventory => _boundInventory;
    public int AnchorCount => _resolvedAnchors.Length;
    public int VisibleCount => _visibleCount;
    public int WorldVisibleCount => _worldVisibleCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPaperFlyStatics()
    {
        PendingPaperFlyRemovals.Clear();
        PendingPaperFlyAdditions.Clear();
        _lastPendingPaperFlyPruneFrame = -1;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializePool();
        BindInventory(targetInventory);
        _wasPaperFlyPlaying = IsPaperFlyPlaying();
        TryBindWorldDropManager(true);
    }

    private void OnDisable()
    {
        RemovePendingPaperFlyEndpoints(this);
        StopAllPaperFlyAnimations();
        DestroyPaperFlyPool();
        UnbindWorldDropManager();
        DestroyWorldDropPool();
        UnbindInventory();
        HideAllVisuals();
        _wasPaperFlyPlaying = false;
    }

    private void Update()
    {
        UpdatePaperFlyLifecycle();

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
        RemovePendingPaperFlyEndpoints(this);
        RefreshVisuals(false);
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
            RefreshVisuals(false);
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
        RefreshVisuals(false);
        Log($"Bound inventory. publicCount={_boundInventory.PublicVisualCount}");
    }

    private void UnbindInventory()
    {
        RemovePendingPaperFlyEndpoints(this);
        _publicVisualSnapshot.Clear();
        _publicVisualSnapshotWasSpawned = false;

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
        bool refreshCompleted = false;
        try
        {
            RefreshVisuals(true);
            refreshCompleted = true;
        }
        catch (Exception exception)
        {
            LogWarning(
                $"Paper-fly observation failed; preserving public visual refresh. " +
                $"exception={exception.GetType().Name}");
        }
        finally
        {
            if (!refreshCompleted)
            {
                try
                {
                    RemovePendingPaperFlyEndpoints(this);
                    RefreshVisuals(false);
                }
                catch (Exception exception)
                {
                    LogWarning(
                        $"Public visual refresh failed after paper-fly fallback. " +
                        $"exception={exception.GetType().Name}");
                }
            }
        }
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
                SourceScale = instance.transform.localScale,
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

    private void RefreshVisuals(bool observeTransferDelta = false)
    {
        InitializePool();
        IReadOnlyList<PostItPublicVisualData> items =
            _boundInventory != null
                ? _boundInventory.PublicVisualItems
                : null;
        bool currentSnapshotWasSpawned =
            _boundInventory != null && _boundInventory.IsSpawned;
        bool canObserveTransfer =
            observeTransferDelta &&
            _publicVisualSnapshotWasSpawned &&
            currentSnapshotWasSpawned &&
            _wasPaperFlyPlaying &&
            IsPaperFlyPlaying();

        if (canObserveTransfer)
        {
            ObservePaperFlyRemovals(items);
        }

        HideAllVisuals();

        if (items == null || _visualSlots.Length == 0)
        {
            CapturePublicVisualSnapshot(items, currentSnapshotWasSpawned);
            return;
        }

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
            ApplyVisualScale(
                slot.Instance.transform,
                slot.SourceScale,
                bodyVisualScaleMultiplier);
            slot.Instance.SetActive(true);
            _visibleCount++;
        }

        if (canObserveTransfer)
        {
            ObservePaperFlyAdditions(items);
        }

        CapturePublicVisualSnapshot(items, currentSnapshotWasSpawned);
        ReportOverflowIfChanged(overflowCount, items.Count);
        Log($"Refreshed visuals. visible={_visibleCount}, public={items.Count}, anchors={_visualSlots.Length}");
    }

    private void ObservePaperFlyRemovals(IReadOnlyList<PostItPublicVisualData> currentItems)
    {
        for (int i = 0; i < _publicVisualSnapshot.Count; i++)
        {
            PostItPublicVisualData previousData = _publicVisualSnapshot[i];
            if (!previousData.IsValid || ContainsPostIt(currentItems, previousData.PostItId))
                continue;

            if (TryCaptureVisualPose(
                    previousData.PostItId,
                    out Vector3 position,
                    out Quaternion rotation,
                    out Vector3 scale))
            {
                QueuePaperFlyEndpoint(
                    true,
                    CreatePaperFlyEndpoint(previousData, position, rotation, scale));
            }
        }
    }

    private void ObservePaperFlyAdditions(IReadOnlyList<PostItPublicVisualData> currentItems)
    {
        for (int i = 0; i < currentItems.Count; i++)
        {
            PostItPublicVisualData currentData = currentItems[i];
            if (!currentData.IsValid ||
                ContainsPostIt(_publicVisualSnapshot, currentData.PostItId))
            {
                continue;
            }

            if (TryCaptureVisualPose(
                    currentData.PostItId,
                    out Vector3 position,
                    out Quaternion rotation,
                    out Vector3 scale))
            {
                QueuePaperFlyEndpoint(
                    false,
                    CreatePaperFlyEndpoint(currentData, position, rotation, scale));
            }
        }
    }

    private PaperFlyEndpoint CreatePaperFlyEndpoint(
        PostItPublicVisualData data,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        return new PaperFlyEndpoint
        {
            Presenter = this,
            Data = data,
            Position = position,
            Rotation = rotation,
            Scale = scale,
            ObservedAt = Time.unscaledTime,
            ObservedFrame = Time.frameCount
        };
    }

    private void CapturePublicVisualSnapshot(
        IReadOnlyList<PostItPublicVisualData> items,
        bool wasSpawned)
    {
        _publicVisualSnapshot.Clear();
        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                _publicVisualSnapshot.Add(items[i]);
            }
        }

        _publicVisualSnapshotWasSpawned = wasSpawned;
    }

    private static bool ContainsPostIt(
        IReadOnlyList<PostItPublicVisualData> items,
        int postItId)
    {
        if (items == null)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].PostItId == postItId)
                return true;
        }

        return false;
    }

    private bool TryCaptureVisualPose(
        int postItId,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        position = default;
        rotation = Quaternion.identity;
        scale = Vector3.one;

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

            Transform visualTransform = slot.Instance.transform;
            position = visualTransform.position;
            rotation = visualTransform.rotation;
            scale = visualTransform.lossyScale;
            return IsFiniteVector3(position) &&
                   IsFiniteQuaternion(rotation) &&
                   IsFiniteVector3(scale);
        }

        return false;
    }

    private static void QueuePaperFlyEndpoint(
        bool isRemoval,
        PaperFlyEndpoint endpoint)
    {
        if (!IsPaperFlyEndpointUsable(endpoint))
            return;

        PrunePendingPaperFlyEndpoints();
        List<PaperFlyEndpoint> ownEndpoints = isRemoval
            ? PendingPaperFlyRemovals
            : PendingPaperFlyAdditions;
        List<PaperFlyEndpoint> oppositeEndpoints = isRemoval
            ? PendingPaperFlyAdditions
            : PendingPaperFlyRemovals;
        int matchingIndex = FindMatchingPaperFlyEndpoint(oppositeEndpoints, endpoint);
        if (matchingIndex >= 0)
        {
            PaperFlyEndpoint opposite = oppositeEndpoints[matchingIndex];
            oppositeEndpoints.RemoveAt(matchingIndex);

            PaperFlyEndpoint removal = isRemoval ? endpoint : opposite;
            PaperFlyEndpoint addition = isRemoval ? opposite : endpoint;
            if (removal.Presenter == addition.Presenter)
                return;

            try
            {
                removal.Presenter.TryStartPaperFly(removal, addition);
            }
            catch (Exception exception)
            {
                if (removal.Presenter != null)
                {
                    removal.Presenter.LogWarning(
                        $"Paper-fly start failed without affecting transfer state. " +
                        $"postItId={removal.Data.PostItId}, " +
                        $"exception={exception.GetType().Name}");
                }
            }

            return;
        }

        for (int i = ownEndpoints.Count - 1; i >= 0; i--)
        {
            if (ownEndpoints[i].Data.PostItId == endpoint.Data.PostItId)
            {
                ownEndpoints.RemoveAt(i);
            }
        }

        ownEndpoints.Add(endpoint);
    }

    private static int FindMatchingPaperFlyEndpoint(
        List<PaperFlyEndpoint> candidates,
        PaperFlyEndpoint endpoint)
    {
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            PaperFlyEndpoint candidate = candidates[i];
            if (!IsPaperFlyEndpointUsable(candidate) ||
                candidate.Data.PostItId != endpoint.Data.PostItId ||
                candidate.Data.Type != endpoint.Data.Type ||
                candidate.Data.VisualId != endpoint.Data.VisualId ||
                Mathf.Abs(candidate.ObservedFrame - endpoint.ObservedFrame) >
                    PaperFlyMaxFrameSeparation ||
                Mathf.Abs(candidate.ObservedAt - endpoint.ObservedAt) > PaperFlyMatchWindow)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool IsPaperFlyEndpointUsable(PaperFlyEndpoint endpoint)
    {
        return endpoint != null &&
               endpoint.Presenter != null &&
               endpoint.Presenter.isActiveAndEnabled &&
               endpoint.Presenter._boundInventory != null &&
               endpoint.Presenter._boundInventory.IsSpawned &&
               endpoint.Presenter._wasPaperFlyPlaying &&
               endpoint.Presenter.IsPaperFlyPlaying() &&
               Time.unscaledTime - endpoint.ObservedAt <= PaperFlyMatchWindow;
    }

    private static void PrunePendingPaperFlyEndpoints()
    {
        if (_lastPendingPaperFlyPruneFrame == Time.frameCount)
            return;

        _lastPendingPaperFlyPruneFrame = Time.frameCount;
        PrunePendingPaperFlyEndpoints(PendingPaperFlyRemovals);
        PrunePendingPaperFlyEndpoints(PendingPaperFlyAdditions);
    }

    private static void PrunePendingPaperFlyEndpoints(List<PaperFlyEndpoint> endpoints)
    {
        for (int i = endpoints.Count - 1; i >= 0; i--)
        {
            if (!IsPaperFlyEndpointUsable(endpoints[i]))
            {
                endpoints.RemoveAt(i);
            }
        }
    }

    private static void RemovePendingPaperFlyEndpoints(
        PlayerPostItWorldPresenter presenter)
    {
        RemovePendingPaperFlyEndpoints(PendingPaperFlyRemovals, presenter);
        RemovePendingPaperFlyEndpoints(PendingPaperFlyAdditions, presenter);
    }

    private static void RemovePendingPaperFlyEndpoints(
        List<PaperFlyEndpoint> endpoints,
        PlayerPostItWorldPresenter presenter)
    {
        for (int i = endpoints.Count - 1; i >= 0; i--)
        {
            if (endpoints[i].Presenter == presenter)
            {
                endpoints.RemoveAt(i);
            }
        }
    }

    private static void ClearPendingPaperFlyEndpoints()
    {
        PendingPaperFlyRemovals.Clear();
        PendingPaperFlyAdditions.Clear();
    }

    private void UpdatePaperFlyLifecycle()
    {
        bool isPlaying = IsPaperFlyPlaying();
        if (isPlaying != _wasPaperFlyPlaying)
        {
            ClearPendingPaperFlyEndpoints();
            StopAllPaperFlyAnimations();
            _wasPaperFlyPlaying = isPlaying;
            CapturePublicVisualSnapshot(
                _boundInventory != null ? _boundInventory.PublicVisualItems : null,
                _boundInventory != null && _boundInventory.IsSpawned);
        }

        PrunePendingPaperFlyEndpoints();
        if (!isPlaying)
            return;

        UpdatePaperFlyAnimations();
    }

    private bool IsPaperFlyPlaying()
    {
        if (_paperFlyGameStateManager == null &&
            Time.unscaledTime >= _nextPaperFlyGameStateResolveTime)
        {
            _nextPaperFlyGameStateResolveTime =
                Time.unscaledTime + WorldManagerResolveInterval;
            _paperFlyGameStateManager = FindFirstObjectByType<GameStateManager>();
        }

        return _paperFlyGameStateManager != null &&
               _paperFlyGameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private void TryStartPaperFly(
        PaperFlyEndpoint removal,
        PaperFlyEndpoint addition)
    {
        if (removal == null ||
            addition == null ||
            removal.Presenter != this ||
            !IsPaperFlyEndpointUsable(removal) ||
            !IsPaperFlyEndpointUsable(addition) ||
            !IsFiniteVector3(removal.Position) ||
            !IsFiniteVector3(removal.Scale) ||
            !IsFiniteVector3(addition.Position) ||
            !IsFiniteVector3(addition.Scale))
        {
            return;
        }

        PaperFlySlot slot = AcquirePaperFlySlot();
        if (slot == null)
            return;

        try
        {
            ApplyVisualProperties(
                slot.Renderers,
                slot.PropertyBlock,
                addition.Data.PostItId,
                addition.Data.Type,
                addition.Data.VisualId,
                addition.Data.IsOriginalOwnerItem,
                slot.SupportsCatalogPreview,
                ref slot.WarningState);

            Color baseColor = slot.PropertyBlock.GetColor(BaseColorPropertyId);
            baseColor.a = 1f;
            slot.BaseColor = baseColor;
            slot.DestinationPresenter = addition.Presenter;
            slot.DestinationPostItId = addition.Data.PostItId;
            slot.SourcePosition = removal.Position;
            slot.SourceRotation = removal.Rotation;
            slot.SourceScale = removal.Scale;
            slot.DestinationPosition = addition.Position;
            slot.DestinationRotation = addition.Rotation;
            slot.DestinationScale = addition.Scale;
            slot.StartedAt = Time.unscaledTime;
            slot.Active = true;
            slot.Instance.transform.SetPositionAndRotation(
                slot.SourcePosition,
                slot.SourceRotation);
            slot.Instance.transform.localScale = slot.SourceScale;
            slot.Instance.SetActive(true);
        }
        catch (Exception exception)
        {
            ReleasePaperFlySlot(slot);
            LogWarning(
                $"Paper-fly setup failed without affecting transfer state. " +
                $"postItId={addition.Data.PostItId}, " +
                $"exception={exception.GetType().Name}");
        }
    }

    private PaperFlySlot AcquirePaperFlySlot()
    {
        for (int i = 0; i < _paperFlySlots.Count; i++)
        {
            PaperFlySlot slot = _paperFlySlots[i];
            if (slot != null && !slot.Active && slot.Instance != null)
                return slot;
        }

        if (_paperFlySlots.Count >= PaperFlyPoolCapacity)
            return null;

        return TryCreatePaperFlySlot();
    }

    private PaperFlySlot TryCreatePaperFlySlot()
    {
        if (visualTemplate == null)
        {
            WarnMissingTemplate();
            return null;
        }

        if (_paperFlyRoot == null)
        {
            GameObject rootObject = new GameObject("PostItPaperFly_Local");
            _paperFlyRoot = rootObject.transform;
            _paperFlyRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _paperFlyRoot.localScale = Vector3.one;
        }

        int poolIndex = _paperFlySlots.Count;
        int runtimeMaterialStartIndex = _paperFlyRuntimeMaterials.Count;
        GameObject instance = null;
        try
        {
            instance = Instantiate(visualTemplate, _paperFlyRoot, false);
            instance.name = $"{visualTemplate.name}_PaperFly_{poolIndex}";
            if (HasForbiddenComponents(instance))
            {
                LogWarning(
                    $"Rejected paper-fly template with gameplay or network components. " +
                    $"poolIndex={poolIndex}");
                Destroy(instance);
                return null;
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                LogWarning($"Paper-fly template has no Renderer. poolIndex={poolIndex}");
                Destroy(instance);
                return null;
            }

            ClonePaperFlyMaterials(renderers);
            instance.SetActive(false);
            PaperFlySlot slot = new PaperFlySlot
            {
                Instance = instance,
                Renderers = renderers,
                PropertyBlock = new MaterialPropertyBlock(),
                SupportsCatalogPreview = SupportsCatalogPreviewTextures(renderers)
            };
            _paperFlySlots.Add(slot);
            return slot;
        }
        catch (Exception exception)
        {
            DestroyPaperFlyRuntimeMaterialsFrom(runtimeMaterialStartIndex);
            if (instance != null)
            {
                Destroy(instance);
            }

            LogWarning(
                $"Paper-fly pool creation failed. poolIndex={poolIndex}, " +
                $"exception={exception.GetType().Name}");
            return null;
        }
    }

    private void ClonePaperFlyMaterials(Renderer[] renderers)
    {
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] runtimeMaterials = new Material[sourceMaterials.Length];
            for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                Material sourceMaterial = sourceMaterials[materialIndex];
                if (sourceMaterial == null)
                    continue;

                Material runtimeMaterial = new Material(sourceMaterial)
                {
                    name = $"{sourceMaterial.name}_PaperFly_Runtime"
                };
                ConfigurePaperFlyMaterial(runtimeMaterial);
                runtimeMaterials[materialIndex] = runtimeMaterial;
                _paperFlyRuntimeMaterials.Add(runtimeMaterial);
            }

            renderer.sharedMaterials = runtimeMaterials;
        }
    }

    private static void ConfigurePaperFlyMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        SetMaterialFloatIfPresent(material, SurfacePropertyId, 1f);
        SetMaterialFloatIfPresent(material, BlendPropertyId, 0f);
        SetMaterialFloatIfPresent(material, AlphaClipPropertyId, 0f);
        SetMaterialFloatIfPresent(material, SrcBlendPropertyId, (float)BlendMode.SrcAlpha);
        SetMaterialFloatIfPresent(
            material,
            DstBlendPropertyId,
            (float)BlendMode.OneMinusSrcAlpha);
        SetMaterialFloatIfPresent(material, ZWritePropertyId, 0f);
        SetMaterialFloatIfPresent(material, CullPropertyId, (float)CullMode.Off);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void SetMaterialFloatIfPresent(
        Material material,
        int propertyId,
        float value)
    {
        if (material.HasProperty(propertyId))
        {
            material.SetFloat(propertyId, value);
        }
    }

    private void UpdatePaperFlyAnimations()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < _paperFlySlots.Count; i++)
        {
            PaperFlySlot slot = _paperFlySlots[i];
            if (slot == null || !slot.Active)
                continue;

            try
            {
                if (slot.Instance == null)
                {
                    ReleasePaperFlySlot(slot);
                    continue;
                }

                if (slot.DestinationPresenter == null ||
                    !slot.DestinationPresenter.isActiveAndEnabled ||
                    slot.DestinationPresenter._boundInventory == null ||
                    !slot.DestinationPresenter._boundInventory.IsSpawned ||
                    !slot.DestinationPresenter._wasPaperFlyPlaying)
                {
                    ReleasePaperFlySlot(slot);
                    continue;
                }

                if (slot.DestinationPresenter.TryCaptureVisualPose(
                        slot.DestinationPostItId,
                        out Vector3 destinationPosition,
                        out Quaternion destinationRotation,
                        out Vector3 destinationScale))
                {
                    slot.DestinationPosition = destinationPosition;
                    slot.DestinationRotation = destinationRotation;
                    slot.DestinationScale = destinationScale;
                }

                float progress = Mathf.Clamp01(
                    (now - slot.StartedAt) / Mathf.Max(0.01f, PaperFlyDuration));
                if (progress >= 1f)
                {
                    ReleasePaperFlySlot(slot);
                    continue;
                }

                float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
                Vector3 position = Vector3.Lerp(
                    slot.SourcePosition,
                    slot.DestinationPosition,
                    easedProgress);
                position += Vector3.up *
                    (Mathf.Sin(progress * Mathf.PI) * PaperFlyArcHeight);
                Quaternion rotation = Quaternion.Slerp(
                    slot.SourceRotation,
                    slot.DestinationRotation,
                    easedProgress);
                float fadeProgress = Mathf.InverseLerp(
                    PaperFlyFadeStart,
                    1f,
                    progress);
                float scaleMultiplier = Mathf.Lerp(1f, 0.8f, fadeProgress);
                Vector3 scale = Vector3.Lerp(
                    slot.SourceScale,
                    slot.DestinationScale,
                    easedProgress) * scaleMultiplier;

                slot.Instance.transform.SetPositionAndRotation(position, rotation);
                slot.Instance.transform.localScale = scale;
                ApplyPaperFlyAlpha(slot, 1f - fadeProgress);
            }
            catch (Exception exception)
            {
                int failedPostItId = slot.DestinationPostItId;
                ReleasePaperFlySlot(slot);
                LogWarning(
                    $"Paper-fly update failed without affecting transfer state. " +
                    $"postItId={failedPostItId}, " +
                    $"exception={exception.GetType().Name}");
            }
        }
    }

    private static void ApplyPaperFlyAlpha(PaperFlySlot slot, float alpha)
    {
        Color color = slot.BaseColor;
        color.a = Mathf.Clamp01(alpha);
        slot.PropertyBlock.SetColor(BaseColorPropertyId, color);
        slot.PropertyBlock.SetColor(ColorPropertyId, color);
        for (int i = 0; i < slot.Renderers.Length; i++)
        {
            Renderer renderer = slot.Renderers[i];
            if (renderer != null)
            {
                renderer.SetPropertyBlock(slot.PropertyBlock);
            }
        }
    }

    private static void ReleasePaperFlySlot(PaperFlySlot slot)
    {
        if (slot == null)
            return;

        slot.Active = false;
        slot.DestinationPresenter = null;
        slot.DestinationPostItId = -1;
        if (slot.Instance != null && slot.Instance.activeSelf)
        {
            slot.Instance.SetActive(false);
        }
    }

    private void StopAllPaperFlyAnimations()
    {
        for (int i = 0; i < _paperFlySlots.Count; i++)
        {
            ReleasePaperFlySlot(_paperFlySlots[i]);
        }
    }

    private void DestroyPaperFlyPool()
    {
        StopAllPaperFlyAnimations();
        _paperFlySlots.Clear();
        if (_paperFlyRoot != null)
        {
            Destroy(_paperFlyRoot.gameObject);
            _paperFlyRoot = null;
        }

        DestroyPaperFlyRuntimeMaterialsFrom(0);
    }

    private void DestroyPaperFlyRuntimeMaterialsFrom(int startIndex)
    {
        for (int i = _paperFlyRuntimeMaterials.Count - 1; i >= startIndex; i--)
        {
            Material material = _paperFlyRuntimeMaterials[i];
            _paperFlyRuntimeMaterials.RemoveAt(i);
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    private static bool IsFiniteVector3(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private static void ApplyVisualScale(
        Transform target,
        Vector3 sourceScale,
        float multiplier)
    {
        if (target == null)
            return;

        target.localScale = sourceScale * SanitizeVisualScaleMultiplier(multiplier);
    }

    private static float SanitizeVisualScaleMultiplier(float multiplier)
    {
        return float.IsFinite(multiplier) && multiplier > 0f
            ? multiplier
            : 1f;
    }

    private static bool IsFiniteQuaternion(Quaternion value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z) &&
               float.IsFinite(value.w);
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
            ApplyVisualScale(
                slot.Instance.transform,
                slot.SourceScale,
                worldVisualScaleMultiplier);
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
                SourceScale = instance.transform.localScale,
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
        bodyVisualScaleMultiplier =
            SanitizeVisualScaleMultiplier(bodyVisualScaleMultiplier);
        worldVisualScaleMultiplier =
            SanitizeVisualScaleMultiplier(worldVisualScaleMultiplier);
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
