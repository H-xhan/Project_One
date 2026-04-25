using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class ItemPickupNetwork : NetworkBehaviour
{
    private const float AppliedPosePositionEpsilon = 0.0005f;
    private const float AppliedPoseRotationEpsilon = 0.1f;
    private const float AppliedPoseScaleEpsilon = 0.0005f;

    [Tooltip("아이템 데이터 (SO 파일 연결 필수)")]
    [SerializeField] public ItemDataSO itemData;

    [Tooltip("월드에서 보이는 모델 루트(비우면 이 오브젝트 아래의 Renderer를 모두 제어)")]
    [SerializeField] private GameObject worldVisualRoot;

    [Tooltip("잡고 있는 동안 월드 비주얼을 숨길지")]
    [SerializeField] private bool hideWorldVisualWhenHeld = true;

    [Header("Debug")]
    [Tooltip("디버그 로그 출력 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private readonly NetworkVariable<bool> _worldVisualVisible =
        new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<bool> _heldState =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private NetworkTransform _networkTransform;
    private Vector3 _defaultLocalScale = Vector3.one;
    private bool _hasDefaultLocalScale;
    private Renderer[] _cachedRenderers;
    private bool[] _cachedRendererEnabledStates;
    private Collider[] _cachedColliders;
    private bool[] _cachedColliderEnabledStates;
    private bool _hasCachedHeldDisabledState;
    private Rigidbody _cachedRigidbody;
    private bool _cachedIsKinematic;
    private bool _cachedUseGravity;
    private bool _cachedDetectCollisions;
    private bool _hasCachedRigidbodyState;
    private bool _dropRestoreInProgress;
    private Vector3 _lastAppliedWorldPosition;
    private Quaternion _lastAppliedWorldRotation = Quaternion.identity;
    private Vector3 _lastAppliedWorldScale = Vector3.one;
    private bool _hasLastAppliedPose;

    public int ItemId => itemData != null ? itemData.itemId : 0;

    private void Awake()
    {
        CacheDefaultScale();
        _networkTransform = GetComponent<NetworkTransform>();
    }

    public WeaponItemDataSO GetWeaponData()
    {
        return itemData as WeaponItemDataSO;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _worldVisualVisible.OnValueChanged += OnWorldVisualChanged;
        _heldState.OnValueChanged += OnHeldStateChanged;
        ApplyWorldVisual(_worldVisualVisible.Value);
        ApplyHeldStateLocal(_heldState.Value);

        if (IsServer)
            ApplyHeldPhysicsAuthoritatively(_heldState.Value);
    }

    public override void OnNetworkDespawn()
    {
        _worldVisualVisible.OnValueChanged -= OnWorldVisualChanged;
        _heldState.OnValueChanged -= OnHeldStateChanged;
        _hasLastAppliedPose = false;
        base.OnNetworkDespawn();
    }

    private void OnWorldVisualChanged(bool previousValue, bool newValue)
    {
        if (_heldState.Value)
            return;

        ApplyWorldVisual(newValue);
    }

    private void OnHeldStateChanged(bool previousValue, bool newValue)
    {
        ApplyHeldStateLocal(newValue);
    }

    public void SetWorldVisualVisibleServer(bool visible)
    {
        if (!IsServer) return;
        if (!hideWorldVisualWhenHeld) return;
        _worldVisualVisible.Value = visible;
    }

    public bool IsWorldVisualVisible()
    {
        return _worldVisualVisible.Value;
    }

    internal void SetHeldStateServer(bool held)
    {
        if (!IsServer)
            return;

        if (_heldState.Value == held)
        {
            ApplyHeldPhysicsAuthoritatively(held);
            ApplyHeldStateLocal(held);
            return;
        }

        _heldState.Value = held;
        ApplyHeldPhysicsAuthoritatively(held);
    }

    private static string FormatDropDebugRigidbodyState(Rigidbody rb)
    {
        if (rb == null)
            return "rb=null";

        return $"rbKinematic={rb.isKinematic} rbUseGravity={rb.useGravity} rbLinearVelocity={rb.linearVelocity} rbAngularVelocity={rb.angularVelocity}";
    }

    internal bool TryRestoreDroppedStateServer(
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 worldScale,
        Vector3 carrierVelocity,
        Vector3 dropImpulse)
    {
        if (!IsServer || _dropRestoreInProgress || !_heldState.Value)
            return false;

        _dropRestoreInProgress = true;
        try
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            Log(
                $"[ItemPickup][DropDebug] restoreBegin item={name} restoreTargetPos={worldPosition} restoreTargetRot={worldRotation.eulerAngles} " +
                $"currentPosBeforeRestore={transform.position} currentRotBeforeRestore={transform.rotation.eulerAngles} {FormatDropDebugRigidbodyState(rb)}");

            ApplyPose(worldPosition, worldRotation, worldScale, true);
            Log(
                $"[ItemPickup][DropDebug] afterApplyPose item={name} posAfterApplyPose={transform.position} rotAfterApplyPose={transform.rotation.eulerAngles}");

            _heldState.Value = false;
            ApplyHeldPhysicsAuthoritatively(false);
            Log(
                $"[ItemPickup][DropDebug] afterPhysicsRestore item={name} posAfterPhysicsRestore={transform.position} rotAfterPhysicsRestore={transform.rotation.eulerAngles} {FormatDropDebugRigidbodyState(rb)}");

            if (rb != null)
            {
                if (rb.isKinematic)
                {
                    LogWarning($"[ItemPickupNetwork] Drop restore skipped velocity/impulse because Rigidbody remained kinematic on {name}.");
                    Log(
                        $"[ItemPickup][DropDebug] afterImpulse item={name} carrierVelocity={carrierVelocity} dropImpulse={dropImpulse} " +
                        $"posAfterImpulse={transform.position} rotAfterImpulse={transform.rotation.eulerAngles} skippedImpulse=true reason=rbKinematic {FormatDropDebugRigidbodyState(rb)}");
                }
                else
                {
                    rb.linearVelocity = carrierVelocity;
                    rb.angularVelocity = Vector3.zero;
                    rb.AddForce(dropImpulse, ForceMode.Impulse);
                    Log(
                        $"[ItemPickup][DropDebug] afterImpulse item={name} carrierVelocity={carrierVelocity} dropImpulse={dropImpulse} " +
                        $"posAfterImpulse={transform.position} rotAfterImpulse={transform.rotation.eulerAngles} {FormatDropDebugRigidbodyState(rb)}");
                }
            }
            else
            {
                Log(
                    $"[ItemPickup][DropDebug] afterImpulse item={name} carrierVelocity={carrierVelocity} dropImpulse={dropImpulse} " +
                    $"posAfterImpulse={transform.position} rotAfterImpulse={transform.rotation.eulerAngles} skippedImpulse=true reason=noRigidbody {FormatDropDebugRigidbodyState(rb)}");
            }

            return true;
        }
        finally
        {
            _dropRestoreInProgress = false;
        }
    }

    public Vector3 GetDefaultLocalScale()
    {
        CacheDefaultScale();
        return _defaultLocalScale;
    }

    public void ApplyPose(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, bool syncNetworkTransform)
    {
        if (!syncNetworkTransform && _hasLastAppliedPose)
        {
            float positionDeltaSqr = (_lastAppliedWorldPosition - worldPosition).sqrMagnitude;
            float rotationDelta = Quaternion.Angle(_lastAppliedWorldRotation, worldRotation);
            float scaleDeltaSqr = (_lastAppliedWorldScale - worldScale).sqrMagnitude;

            if (positionDeltaSqr <= AppliedPosePositionEpsilon * AppliedPosePositionEpsilon &&
                rotationDelta <= AppliedPoseRotationEpsilon &&
                scaleDeltaSqr <= AppliedPoseScaleEpsilon * AppliedPoseScaleEpsilon)
            {
                return;
            }
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);
        transform.localScale = worldScale;

        if (syncNetworkTransform && IsServer && _networkTransform != null)
            _networkTransform.Teleport(worldPosition, worldRotation, worldScale);

        _lastAppliedWorldPosition = worldPosition;
        _lastAppliedWorldRotation = worldRotation;
        _lastAppliedWorldScale = worldScale;
        _hasLastAppliedPose = true;
    }

    private void ApplyWorldVisual(bool visible)
    {
        GameObject root = worldVisualRoot != null ? worldVisualRoot : gameObject;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = visible;
        }
    }

    private void ApplyHeldStateLocal(bool held)
    {
        if (held)
        {
            CacheHeldDisabledState();
            SetCachedRenderersEnabled(false);
            SetCachedCollidersEnabled(false);
            return;
        }

        RestoreCachedRenderers();
        RestoreCachedColliders();
    }

    private void ApplyHeldPhysicsAuthoritatively(bool held)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
            return;

        if (held)
        {
            if (!_hasCachedRigidbodyState)
            {
                _cachedRigidbody = rb;
                _cachedIsKinematic = rb.isKinematic;
                _cachedUseGravity = rb.useGravity;
                _cachedDetectCollisions = rb.detectCollisions;
                _hasCachedRigidbodyState = true;
            }

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.Sleep();
            return;
        }

        if (!_hasCachedRigidbodyState || _cachedRigidbody == null)
            return;

        rb.useGravity = _cachedUseGravity;
        rb.isKinematic = _cachedIsKinematic;
        rb.detectCollisions = _cachedDetectCollisions;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
        _hasCachedRigidbodyState = false;
        _cachedRigidbody = null;
    }

    private void CacheHeldDisabledState()
    {
        if (_hasCachedHeldDisabledState)
            return;

        _cachedRenderers = GetComponentsInChildren<Renderer>(true);
        _cachedRendererEnabledStates = new bool[_cachedRenderers.Length];
        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer renderer = _cachedRenderers[i];
            _cachedRendererEnabledStates[i] = renderer != null && renderer.enabled;
        }

        _cachedColliders = GetComponentsInChildren<Collider>(true);
        _cachedColliderEnabledStates = new bool[_cachedColliders.Length];
        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            Collider collider = _cachedColliders[i];
            _cachedColliderEnabledStates[i] = collider != null && collider.enabled;
        }

        _hasCachedHeldDisabledState = true;
    }

    private void RestoreCachedRenderers()
    {
        if (_cachedRenderers == null || _cachedRendererEnabledStates == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            if (_cachedRenderers[i] != null)
                _cachedRenderers[i].enabled = _cachedRendererEnabledStates[i];
        }
    }

    private void RestoreCachedColliders()
    {
        if (_cachedColliders == null || _cachedColliderEnabledStates == null)
            return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            if (_cachedColliders[i] != null)
                _cachedColliders[i].enabled = _cachedColliderEnabledStates[i];
        }

        _hasCachedHeldDisabledState = false;
    }

    private void SetCachedRenderersEnabled(bool enabled)
    {
        if (_cachedRenderers == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            if (_cachedRenderers[i] != null)
                _cachedRenderers[i].enabled = enabled;
        }
    }

    private void SetCachedCollidersEnabled(bool enabled)
    {
        if (_cachedColliders == null)
            return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            if (_cachedColliders[i] != null)
                _cachedColliders[i].enabled = enabled;
        }
    }

    private void CacheDefaultScale()
    {
        if (_hasDefaultLocalScale)
            return;

        _defaultLocalScale = transform.localScale;
        if (_defaultLocalScale == Vector3.zero)
            _defaultLocalScale = Vector3.one;

        _hasDefaultLocalScale = true;
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
