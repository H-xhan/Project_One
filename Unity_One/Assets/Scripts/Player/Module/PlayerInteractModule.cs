using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// [변경] MonoBehaviour -> NetworkBehaviour (네트워크 변수 사용을 위해)
public class PlayerInteractModule : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("플레이어 카메라 (자동 탐색)")]
    [SerializeField] private Camera ownerCamera;

    [Tooltip("줍기 사거리")]
    [SerializeField] private float pickupDistance = 20f;

    [Tooltip("아이템 레이어 마스크")]
    [SerializeField] private LayerMask pickupMask = ~0;

    [Header("Hand")]
    [Tooltip("아이템이 붙을 오른손 뼈 위치 (필수!)")]
    [SerializeField] private Transform rightHandBone;

    // [핵심] 현재 잡고 있는 아이템을 모든 사람(서버+클라이언트)이 알 수 있게 공유
    public NetworkVariable<NetworkObjectReference> CurrentHeldItem = new NetworkVariable<NetworkObjectReference>();

    private bool _ownerMode;
    private NetworkObject _spawnedItemObj; // 실제 찾은 오브젝트 캐싱용

    private void Awake()
    {
        if (ownerCamera == null)
        {
            if (transform.parent != null) ownerCamera = transform.parent.GetComponentInChildren<Camera>(true);
            else ownerCamera = GetComponentInChildren<Camera>(true);
        }
    }

    public override void OnNetworkSpawn()
    {
        // 아이템이 바뀌면(주우면) 처리할 함수 연결
        CurrentHeldItem.OnValueChanged += OnHeldItemChanged;
    }

    public override void OnNetworkDespawn()
    {
        CurrentHeldItem.OnValueChanged -= OnHeldItemChanged;
    }

    public void SetOwnerMode(bool ownerMode)
    {
        _ownerMode = ownerMode;
        if (!_ownerMode) ownerCamera = null;
    }

    public void Tick(bool interactPressed)
    {
        // 디버깅용
    }

    // [매 프레임 실행] 아이템을 오른손 위치로 '강제 이동' (애니메이션 따라가기)
    private void LateUpdate()
    {
        // 잡은 아이템이 없거나, 오른손 뼈가 없으면 패스
        if (_spawnedItemObj == null || rightHandBone == null) return;

        // 아이템의 위치와 회전을 오른손 뼈와 똑같이 맞춤 (본드처럼)
        _spawnedItemObj.transform.position = rightHandBone.position;
        _spawnedItemObj.transform.rotation = rightHandBone.rotation;
    }

    // 아이템을 주웠을 때(변수 값이 바뀌었을 때) 실행되는 함수
    private void OnHeldItemChanged(NetworkObjectReference oldVal, NetworkObjectReference newVal)
    {
        // NetworkObjectReference에서 실제 오브젝트를 꺼냄
        if (newVal.TryGet(out NetworkObject itemNo))
        {
            _spawnedItemObj = itemNo;

            // 물리/충돌 끄기 (손에 들고 있을 때 방해 안 되게)
            var rb = itemNo.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var colliders = itemNo.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.enabled = false;

            // 그림자나 겹침 방지 등을 위해 필요하다면 추가 설정
            Debug.Log($"[Client/Server] 아이템({itemNo.name}) 장착 동기화 완료!");
        }
        else
        {
            _spawnedItemObj = null; // 아이템 놓았을 때
        }
    }

    public bool TryFindPickupTarget(out NetworkObjectReference target)
    {
        target = default;
        if (!_ownerMode || ownerCamera == null) return false;

        Ray ray = ownerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance, pickupMask, QueryTriggerInteraction.Collide))
        {
            NetworkObject no = hit.collider.GetComponentInParent<NetworkObject>();
            if (no != null)
            {
                target = new NetworkObjectReference(no);
                return true;
            }
        }
        return false;
    }

    // 서버에서 실행되는 진짜 줍기 로직
    public bool ServerTryPickup(NetworkObjectReference target)
    {
        if (!target.TryGet(out NetworkObject no) || no == null) return false;

        // 이미 뭔가 들고 있으면 줍기 실패 (원하면 교체 로직 추가 가능)
        if (CurrentHeldItem.Value.TryGet(out NetworkObject current) && current != null)
        {
            Debug.Log("이미 손에 아이템이 있습니다.");
            return false;
        }

        Debug.Log($"서버: 아이템({no.name}) 줍기 시도...");

        // [1] 씬 이동 시 삭제 방지
        DontDestroyOnLoad(no.gameObject);

        // [2] NetworkTransform 끄기 (이제 수동으로 위치 맞출 거니까)
        var netTransform = no.GetComponent<NetworkTransform>();
        if (netTransform != null) netTransform.enabled = false;

        // [3] 소유권 가져오기
        var playerNo = GetComponentInParent<NetworkObject>();
        if (playerNo != null) no.ChangeOwnership(playerNo.OwnerClientId);

        // [4] 부모 설정 (여기가 중요! 뼈가 아니라 '플레이어 본체'로 설정)
        // NetworkObject는 NetworkObject 밑에만 들어갈 수 있음
        if (no.TrySetParent(playerNo.transform, false))
        {
            // [5] 모든 클라이언트에게 "나 이거 들었어!"라고 알림 (변수 업데이트)
            CurrentHeldItem.Value = target;

            Debug.Log("장착 성공! (부모: 플레이어 / 위치: LateUpdate로 고정)");
            return true;
        }

        return false;
    }
}