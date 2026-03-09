using Unity.Netcode;
using UnityEngine;

public class PlayerInteractModule : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("오너가 사용하는 카메라")]
    [SerializeField] private Camera ownerCamera;

    [Tooltip("픽업 레이캐스트 거리")]
    [SerializeField] private float pickupDistance = 6f;

    [Tooltip("픽업 가능한 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask;

    [Header("Hand")]
    [Tooltip("오른손 소켓(WeaponPoint). 비어있으면 휴머노이드 RightHand를 자동 탐색")]
    [SerializeField] private Transform rightHandBone;

    [Tooltip("아이템을 손에 붙일 때 기본 로컬 위치 오프셋")]
    [SerializeField] private Vector3 defaultHeldLocalPosition;

    [Tooltip("아이템을 손에 붙일 때 기본 로컬 회전 오프셋(오일러)")]
    [SerializeField] private Vector3 defaultHeldLocalEulerAngles;

    [Tooltip("픽업 중 캐릭터 컨트롤러 충돌을 끄고 싶으면 체크")]
    [SerializeField] private bool disableCharacterControllerWhilePickingUp = true;

    [Header("Pickup Timing")]
    [Tooltip("애니 이벤트가 누락되었을 때 자동으로 붙이는 대기 시간(초)")]
    [SerializeField] private float pickupPendingTime = 1.5f;

    [Header("Drop/Throw")]
    [Tooltip("드랍/던지기 전방 힘")]
    [SerializeField] private float throwForwardForce = 5f;

    [Tooltip("드랍/던지기 위쪽 힘")]
    [SerializeField] private float throwUpForce = 2f;

    [Header("Drop From Hand")]
    [Tooltip("드랍 시 손 위치에서 플레이어 전방으로 살짝 밀어 겹침을 줄이는 거리")]
    [SerializeField] private float dropHandForwardOffset = 0.15f;

    [Tooltip("드랍 시 손 위치에서 위로 살짝 올려 겹침을 줄이는 거리")]
    [SerializeField] private float dropHandUpOffset = 0.05f;

    private readonly NetworkVariable<NetworkObjectReference> _heldItem =
        new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool _ownerMode;
    private bool _pendingAttach;
    private bool _attached;
    private float _pendingStartTime;

    private NetworkObject _heldCache;
    private ItemPickupNetwork _heldPickup;
    private WeaponItemDataSO _heldWeaponData;

    private int _cachedWeaponAnimId;
    private Vector3 _cachedLocalPos;
    private Vector3 _cachedLocalEuler;
    private bool _hasCachedMeta;

    private CharacterController _cc;
    private Animator _anim;

    public void SetOwnerMode(bool active)
    {
        _ownerMode = active;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _cc = GetComponentInParent<CharacterController>();
        _anim = GetComponentInParent<Animator>();

        AutoFindRefs();
        _heldItem.OnValueChanged += OnHeldItemChanged;

        ResolveHeldCache();
        CacheHeldMeta();
    }

    public override void OnNetworkDespawn()
    {
        _heldItem.OnValueChanged -= OnHeldItemChanged;
        base.OnNetworkDespawn();
    }

    private void AutoFindRefs()
    {
        if (ownerCamera == null)
            ownerCamera = GetComponentInParent<Camera>();

        if (rightHandBone != null) return;

        if (_anim != null && _anim.isHuman)
            rightHandBone = _anim.GetBoneTransform(HumanBodyBones.RightHand);
    }

    private void OnHeldItemChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        _heldCache = null;
        _heldPickup = null;
        _heldWeaponData = null;

        _hasCachedMeta = false;
        _cachedWeaponAnimId = 0;
        _cachedLocalPos = defaultHeldLocalPosition;
        _cachedLocalEuler = defaultHeldLocalEulerAngles;

        _pendingAttach = false;
        _attached = false;
        _pendingStartTime = Time.time;

        ResolveHeldCache();
        CacheHeldMeta();

        if (IsServer && _heldCache != null)
            _pendingAttach = true;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (_pendingAttach && !_attached)
        {
            if (Time.time - _pendingStartTime >= pickupPendingTime)
                ForceAttach();
        }
    }

    private void LateUpdate()
    {
        if (!IsServer) return;
        if (!_attached) return;

        if (!ResolveHeldCache()) return;
        if (rightHandBone == null) AutoFindRefs();
        if (rightHandBone == null) return;

        Vector3 localPos = _hasCachedMeta ? _cachedLocalPos : defaultHeldLocalPosition;
        Vector3 localEuler = _hasCachedMeta ? _cachedLocalEuler : defaultHeldLocalEulerAngles;

        Vector3 worldPos = rightHandBone.TransformPoint(localPos);
        Quaternion worldRot = rightHandBone.rotation * Quaternion.Euler(localEuler);

        _heldCache.transform.SetPositionAndRotation(worldPos, worldRot);
    }

    public bool HasHeldItem()
    {
        return ResolveHeldCache();
    }

    public bool TryFindPickupTarget(out NetworkObjectReference target)
    {
        target = default;

        if (!_ownerMode) return false;
        if (ownerCamera == null) return false;
        if (HasHeldItem()) return false;

        Ray ray = new Ray(ownerCamera.transform.position, ownerCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, pickupDistance, pickupMask, QueryTriggerInteraction.Ignore))
            return false;

        NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned) return false;

        target = netObj;
        return true;
    }

    public bool ServerTryPickup(NetworkObjectReference target)
    {
        if (!IsServer) return false;
        if (ResolveHeldCache()) return false;

        if (!target.TryGet(out NetworkObject netObj)) return false;
        if (netObj == null || !netObj.IsSpawned) return false;

        netObj.ChangeOwnership(OwnerClientId);
        SetHeldPhysics(netObj, true);

        _heldItem.Value = target;

        if (disableCharacterControllerWhilePickingUp && _cc != null && _cc.enabled)
            _cc.enabled = false;

        return true;
    }

    public void ServerTryDrop()
    {
        if (!IsServer) return;
        if (!ResolveHeldCache()) return;

        NetworkObject netObj = _heldCache;

        _attached = false;
        _pendingAttach = false;

        if (!_hasCachedMeta)
            CacheHeldMeta();

        if (TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
        {
            Vector3 dropPos = handPos + transform.forward * dropHandForwardOffset + Vector3.up * dropHandUpOffset;
            netObj.transform.SetPositionAndRotation(dropPos, handRot);
        }
        else
        {
            Vector3 dropPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.0f;
            netObj.transform.SetPositionAndRotation(dropPos, Quaternion.identity);
        }

        SetHeldPhysics(netObj, false);

        Rigidbody rb = netObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 force = transform.forward * throwForwardForce + Vector3.up * throwUpForce;
            rb.AddForce(force, ForceMode.Impulse);
        }

        if (NetworkManager != null)
            netObj.ChangeOwnership(NetworkManager.ServerClientId);

        _heldItem.Value = default;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;
    }

    public int GetCurrentWeaponAnimID()
    {
        if (!ResolveHeldCache()) return 0;
        if (!_hasCachedMeta) CacheHeldMeta();
        return _hasCachedMeta ? _cachedWeaponAnimId : 0;
    }

    public void AnimEvent_AttachHeldItem()
    {
        if (!IsServer) return;
        ForceAttach();
    }

    private void ForceAttach()
    {
        if (!ResolveHeldCache()) return;

        _pendingAttach = false;
        _attached = true;

        if (_cc != null && !_cc.enabled)
            _cc.enabled = true;

        if (TryGetHandWorldPose(out Vector3 handPos, out Quaternion handRot))
            _heldCache.transform.SetPositionAndRotation(handPos, handRot);
    }

    private bool ResolveHeldCache()
    {
        if (_heldCache != null && _heldCache.IsSpawned)
            return true;

        if (_heldItem.Value.TryGet(out NetworkObject netObj))
        {
            _heldCache = netObj;
            _heldPickup = _heldCache != null ? _heldCache.GetComponent<ItemPickupNetwork>() : null;
            return _heldCache != null && _heldCache.IsSpawned;
        }

        _heldCache = null;
        _heldPickup = null;
        return false;
    }

    private bool TryGetHandWorldPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        if (rightHandBone == null) AutoFindRefs();
        if (rightHandBone == null) return false;

        Vector3 localPos = _hasCachedMeta ? _cachedLocalPos : defaultHeldLocalPosition;
        Vector3 localEuler = _hasCachedMeta ? _cachedLocalEuler : defaultHeldLocalEulerAngles;

        pos = rightHandBone.TransformPoint(localPos);
        rot = rightHandBone.rotation * Quaternion.Euler(localEuler);
        return true;
    }

    private void SetHeldPhysics(NetworkObject netObj, bool held)
    {
        Rigidbody rb = netObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (held)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.Sleep();
            }
            else
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
        }

        Collider[] cols = netObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = !held;
    }

    private void CacheHeldMeta()
    {
        _hasCachedMeta = false;
        _heldWeaponData = null;
        _cachedWeaponAnimId = 0;
        _cachedLocalPos = defaultHeldLocalPosition;
        _cachedLocalEuler = defaultHeldLocalEulerAngles;

        if (!ResolveHeldCache()) return;
        if (_heldPickup == null) return;

        _heldWeaponData = _heldPickup.GetWeaponData();
        if (_heldWeaponData == null) return;

        _cachedWeaponAnimId = _heldWeaponData.weaponAnimID;
        _cachedLocalPos = _heldWeaponData.equippedLocalPosition;
        _cachedLocalEuler = _heldWeaponData.equippedLocalEulerAngles;
        _hasCachedMeta = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoFindRefs();
    }
#endif
}