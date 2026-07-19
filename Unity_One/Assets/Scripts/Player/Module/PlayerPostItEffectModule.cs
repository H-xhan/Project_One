using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class PlayerPostItEffectModule : MonoBehaviour
{
    private const float GameStateResolveRetryInterval = 0.5f;

    [Header("References")]
    [SerializeField] private PlayerPostItInventory inventory;
    [SerializeField] private PlayerHub playerHub;
    [SerializeField] private PlayerInteractModule interactModule;
    [SerializeField] private PlayerStatusModule statusModule;
    [SerializeField] private HamsterMotorShellItemAdapter itemAdapter;
    [SerializeField] private HamsterRagdollGrabber ragdollGrabber;
    [SerializeField] private Camera ownerCamera;

    [Header("Input")]
    [SerializeField] private Key guardKey = Key.E;
    [SerializeField] private Key heavyKey = Key.Q;
    [SerializeField] private bool ignoreWhenUiFocused = true;

    [Header("Heavy Targeting")]
    [SerializeField, Min(0f)] private float heavyTargetDistance = 2f;
    [SerializeField, Min(0f)] private float heavyTargetRayRadius = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private NetworkObject _ownerNetworkObject;
    private GameStateManager _gameStateManager;
    private float _nextGameStateResolveTime;
    private bool _hasSearchedInteractModule;
    private int _pendingGuardFrame = -1;
    private int _pendingHeavyFrame = -1;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnEnable()
    {
        _hasSearchedInteractModule = false;
        CacheReferences();
        ClearPendingInput();
    }

    private void OnDisable()
    {
        ClearPendingInput();
    }

    private void Update()
    {
        ClearPendingInput();
        if (!CanReadOwnerEffectInput())
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool guardPressed = ReadKeyDown(keyboard, guardKey);
        bool heavyPressed = ReadKeyDown(keyboard, heavyKey);
        if (!guardPressed && !heavyPressed)
        {
            return;
        }

        if (guardPressed && heavyPressed)
        {
            return;
        }

        // Snapshot blockers before the legacy E/Q readers mutate held state later in Update.
        if (HasHeldOrCharacterGrabState())
        {
            return;
        }

        int frame = Time.frameCount;
        if (guardPressed)
        {
            _pendingGuardFrame = frame;
        }
        else if (heavyPressed)
        {
            _pendingHeavyFrame = frame;
        }
    }

    private void LateUpdate()
    {
        int frame = Time.frameCount;
        bool requestGuard = _pendingGuardFrame == frame;
        bool requestHeavy = _pendingHeavyFrame == frame;
        ClearPendingInput();

        if ((!requestGuard && !requestHeavy) ||
            !CanReadOwnerEffectInput() ||
            HasHeldOrCharacterGrabState())
        {
            return;
        }

        if (requestGuard)
        {
            TryRequestGuard();
        }
        else if (requestHeavy)
        {
            TryRequestHeavy();
        }
    }

    private bool CanReadOwnerEffectInput()
    {
        CacheMissingReferences();
        if (_ownerNetworkObject == null ||
            !_ownerNetworkObject.IsSpawned ||
            !_ownerNetworkObject.IsOwner ||
            inventory == null ||
            !inventory.IsSpawned ||
            !inventory.IsOwner ||
            inventory.GetComponentInParent<NetworkObject>() != _ownerNetworkObject ||
            statusModule == null ||
            statusModule.IsEliminated ||
            !statusModule.CanInteract ||
            !IsPlayingState())
        {
            return false;
        }

        NetworkManager networkManager = inventory.NetworkManager;
        if (networkManager == null || !networkManager.IsListening)
        {
            return false;
        }

        return !ignoreWhenUiFocused || !IsUiInputBlocked();
    }

    private void TryRequestGuard()
    {
        if (inventory == null ||
            !inventory.TryGetFirstGuardCard(out PostItRuntimeData guardCard) ||
            !guardCard.IsValid)
        {
            return;
        }

        inventory.RequestActivateGuard(guardCard.PostItId);
        Log($"Guard request sent. postItId={guardCard.PostItId}");
    }

    private void TryRequestHeavy()
    {
        if (inventory == null ||
            !inventory.TryGetFirstHeavyCard(out PostItRuntimeData heavyCard) ||
            !heavyCard.IsValid)
        {
            return;
        }

        Camera camera = ResolveOwnerCamera();
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return;
        }

        Ray aimRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!TryFindHeavyTarget(
                aimRay,
                out NetworkObjectReference targetReference,
                out ulong targetOwnerClientId))
        {
            return;
        }

        inventory.RequestApplyHeavy(
            heavyCard.PostItId,
            targetReference,
            aimRay.direction.normalized);
        Log(
            $"Heavy request sent. postItId={heavyCard.PostItId}, " +
            $"targetOwnerClientId={targetOwnerClientId}");
    }

    private bool TryFindHeavyTarget(
        Ray aimRay,
        out NetworkObjectReference targetReference,
        out ulong targetOwnerClientId)
    {
        targetReference = default;
        targetOwnerClientId = ulong.MaxValue;

        float selectionDistance = Mathf.Max(0f, heavyTargetDistance);
        if (selectionDistance <= 0f ||
            !IsFiniteVector3(aimRay.origin) ||
            !IsFiniteVector3(aimRay.direction) ||
            aimRay.direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float rayRadius = Mathf.Max(0f, heavyTargetRayRadius);
        RaycastHit[] hits = rayRadius > 0f
            ? Physics.SphereCastAll(
                aimRay,
                rayRadius,
                selectionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide)
            : Physics.RaycastAll(
                aimRay,
                selectionDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            NetworkObject hitNetworkObject =
                hitCollider.GetComponentInParent<NetworkObject>();
            if (hitNetworkObject == _ownerNetworkObject)
            {
                continue;
            }

            if (hitNetworkObject != null)
            {
                PlayerPostItInventory targetInventory =
                    hitNetworkObject.GetComponentInChildren<PlayerPostItInventory>(true);
                PlayerStatusModule targetStatus =
                    hitNetworkObject.GetComponentInChildren<PlayerStatusModule>(true);
                if (hitNetworkObject.IsSpawned &&
                    hitNetworkObject.OwnerClientId != _ownerNetworkObject.OwnerClientId &&
                    targetInventory != null &&
                    targetStatus != null &&
                    !targetStatus.IsEliminated &&
                    targetInventory.GetComponentInParent<NetworkObject>() == hitNetworkObject)
                {
                    targetReference = hitNetworkObject;
                    targetOwnerClientId = hitNetworkObject.OwnerClientId;
                    return true;
                }

                if (hitCollider.isTrigger)
                {
                    continue;
                }

                return false;
            }

            if (!hitCollider.isTrigger)
            {
                return false;
            }
        }

        return false;
    }

    private bool HasHeldOrCharacterGrabState()
    {
        if (interactModule != null &&
            (interactModule.HasHeldItem() || interactModule.IsCharacterGrabBusy))
        {
            return true;
        }

        if (itemAdapter != null && itemAdapter.HasHeldItem)
        {
            return true;
        }

        return ragdollGrabber != null &&
               (ragdollGrabber.IsHolding ||
                ragdollGrabber.HasPendingGrab ||
                ragdollGrabber.HasPendingThrow);
    }

    private bool IsPlayingState()
    {
        if (_gameStateManager == null &&
            Time.unscaledTime >= _nextGameStateResolveTime)
        {
            _gameStateManager = FindFirstObjectByType<GameStateManager>();
            _nextGameStateResolveTime =
                Time.unscaledTime + GameStateResolveRetryInterval;
        }

        return _gameStateManager != null &&
               _gameStateManager.GetState() == GameStateManager.GameState.Playing;
    }

    private Camera ResolveOwnerCamera()
    {
        if (ownerCamera == null && playerHub != null)
        {
            ownerCamera = playerHub.PlayerCamera;
        }

        if (ownerCamera == null && _ownerNetworkObject != null)
        {
            ownerCamera =
                _ownerNetworkObject.GetComponentInChildren<Camera>(true);
        }

        return ownerCamera;
    }

    private void CacheReferences()
    {
        if (_ownerNetworkObject == null)
        {
            _ownerNetworkObject = GetComponent<NetworkObject>();
        }

        if (_ownerNetworkObject == null)
        {
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();
        }

        CacheMissingReferences();
        if (_gameStateManager == null)
        {
            _gameStateManager = FindFirstObjectByType<GameStateManager>();
            _nextGameStateResolveTime =
                Time.unscaledTime + GameStateResolveRetryInterval;
        }
    }

    private void CacheMissingReferences()
    {
        Transform root = _ownerNetworkObject != null
            ? _ownerNetworkObject.transform
            : transform.root;
        if (root == null)
        {
            return;
        }

        if (inventory == null)
        {
            inventory = root.GetComponentInChildren<PlayerPostItInventory>(true);
        }

        if (playerHub == null)
        {
            playerHub = root.GetComponentInChildren<PlayerHub>(true);
        }

        if (interactModule == null && !_hasSearchedInteractModule)
        {
            interactModule = root.GetComponentInChildren<PlayerInteractModule>(true);
            _hasSearchedInteractModule = true;
        }

        if (statusModule == null)
        {
            statusModule = root.GetComponentInChildren<PlayerStatusModule>(true);
        }

        if (itemAdapter == null)
        {
            itemAdapter =
                root.GetComponentInChildren<HamsterMotorShellItemAdapter>(true);
        }

        if (ragdollGrabber == null)
        {
            ragdollGrabber =
                root.GetComponentInChildren<HamsterRagdollGrabber>(true);
        }

        ResolveOwnerCamera();
    }

    private void ClearPendingInput()
    {
        _pendingGuardFrame = -1;
        _pendingHeavyFrame = -1;
    }

    private static bool ReadKeyDown(Keyboard keyboard, Key key)
    {
        return keyboard != null &&
               key != Key.None &&
               keyboard[key].wasPressedThisFrame;
    }

    private static bool IsUiInputBlocked()
    {
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null &&
               (eventSystem.currentSelectedGameObject != null ||
                eventSystem.IsPointerOverGameObject());
    }

    private static bool IsFiniteVector3(Vector3 value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z);
    }

    private void Log(string message)
    {
        if (debugLogs)
        {
            Debug.Log($"[{nameof(PlayerPostItEffectModule)}] {message}", this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        heavyTargetDistance = Mathf.Max(0f, heavyTargetDistance);
        heavyTargetRayRadius = Mathf.Max(0f, heavyTargetRayRadius);
    }
#endif
}
