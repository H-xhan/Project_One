using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Animator (비어있으면 자동 탐색)")]
    [SerializeField] private Animator animator;

    [Tooltip("Ragdoll 기준 뼈(보통 Hips/Pelvis). 반드시 연결 권장")]
    [SerializeField] private Transform hipsBone;

    [Tooltip("CharacterController (비어있으면 자동 탐색)")]
    [SerializeField] private CharacterController characterController;

    [Tooltip("메인 콜라이더(루트). 없으면 자동 탐색")]
    [SerializeField] private Collider mainCollider;

    [Header("Ragdoll Tuning")]
    [Tooltip("랙돌 최소 유지 시간(초)")]
    [SerializeField] private float minRagdollTime = 0.2f;

    [Tooltip("랙돌 최대 유지 시간(초)")]
    [SerializeField] private float maxRagdollTime = 5.0f;

    [Tooltip("멈췄다고 판단하는 속도 기준")]
    [SerializeField] private float settleSpeed = 0.15f;

    [Tooltip("랙돌 중 선형 감쇠(Drag)")]
    [SerializeField] private float ragdollLinearDamping = 1.0f;

    [Tooltip("랙돌 중 각 감쇠(Angular Drag)")]
    [SerializeField] private float ragdollAngularDamping = 5.0f;

    [Tooltip("기상 시 루트 XZ를 hips 위치로 스냅(서버만)")]
    [SerializeField] private bool snapRootXZToHipsOnRecover = true;

    [Header("Coin Settings")]
    [Tooltip("코인 프리팹(드랍용). NetworkObject 포함 필요")]
    [SerializeField] private GameObject coinPrefab;

    private Rigidbody[] _ragdollRbs;
    private Collider[] _ragdollColls;
    private bool _isRagdoll;

    public bool IsRagdoll => _isRagdoll;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();
        if (mainCollider == null)
        {
            // 루트(플레이어 본체)에 붙은 콜라이더를 우선 찾기
            var rootNo = GetComponentInParent<NetworkObject>();
            if (rootNo != null) mainCollider = rootNo.GetComponent<Collider>();
            if (mainCollider == null) mainCollider = GetComponentInParent<Collider>();
        }

        Transform searchRoot = hipsBone != null ? hipsBone.root : transform;

        _ragdollRbs = searchRoot.GetComponentsInChildren<Rigidbody>(true);
        _ragdollColls = searchRoot.GetComponentsInChildren<Collider>(true);

        SetRagdollState(false);
    }

    public void TakeHit(Vector3 hitForce)
    {
        if (_isRagdoll) return;

        if (IsServer)
        {
            ToggleRagdollClientRpc(hitForce);
        }
        else
        {
            TakeHitServerRpc(hitForce);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TakeHitServerRpc(Vector3 hitForce)
    {
        ToggleRagdollClientRpc(hitForce);
    }

    [ClientRpc]
    private void ToggleRagdollClientRpc(Vector3 hitForce)
    {
        StartCoroutine(RagdollRoutine(hitForce));
    }

    private IEnumerator RagdollRoutine(Vector3 hitForce)
    {
        _isRagdoll = true;
        SetRagdollState(true);

        Rigidbody hipsRb = null;
        if (hipsBone != null) hipsBone.TryGetComponent(out hipsRb);

        if (hipsRb != null)
        {
            hipsRb.AddForce(hitForce, ForceMode.Impulse);
        }

        float timer = 0f;
        yield return new WaitForSeconds(minRagdollTime);

        while (timer < maxRagdollTime)
        {
            timer += Time.deltaTime;

            if (hipsRb != null && hipsRb.linearVelocity.magnitude < settleSpeed)
                break;

            yield return null;
        }

        bool isFaceUp = CheckFaceUp();

        if (IsServer && isFaceUp)
        {
            SpawnCoin();
        }

        if (IsServer && snapRootXZToHipsOnRecover && hipsBone != null)
        {
            Vector3 p = transform.position;
            p.x = hipsBone.position.x;
            p.z = hipsBone.position.z;
            transform.position = p;
        }

        SetRagdollState(false);

        if (animator != null)
        {
            animator.ResetTrigger("StandUpBack");
            animator.ResetTrigger("StandUpFront");

            if (isFaceUp) animator.SetTrigger("StandUpBack");
            else animator.SetTrigger("StandUpFront");
        }

        _isRagdoll = false;
    }

    private void SetRagdollState(bool state)
    {
        if (animator != null) animator.enabled = !state;

        if (characterController != null) characterController.enabled = !state;
        if (mainCollider != null) mainCollider.enabled = !state;

        for (int i = 0; i < _ragdollRbs.Length; i++)
        {
            var rb = _ragdollRbs[i];
            if (rb == null) continue;

            if (rb.transform == transform) continue;

            rb.isKinematic = !state;

            if (state)
            {
                rb.linearDamping = ragdollLinearDamping;
                rb.angularDamping = ragdollAngularDamping;
            }
            else
            {
                // [수정] Kinematic이 아닐 때(물리가 켜졌을 때)만 속도를 건드립니다.
                // 이 체크가 없으면 게임 시작 시 Is Kinematic 상태인 플레이어에게 
                // 속도를 주려다 경고 로그가 대량으로 발생합니다.
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        for (int i = 0; i < _ragdollColls.Length; i++)
        {
            var col = _ragdollColls[i];
            if (col == null) continue;

            if (mainCollider != null && col == mainCollider) continue;
            if (characterController != null && col.transform == characterController.transform) continue;

            col.enabled = state;
        }
    }

    private bool CheckFaceUp()
    {
        if (hipsBone == null) return false;

        float dot = Vector3.Dot(hipsBone.forward, Vector3.up);
        return dot > 0.0f;
    }

    private void SpawnCoin()
    {
        if (coinPrefab == null) return;

        var coin = Instantiate(coinPrefab, transform.position + Vector3.up, Quaternion.identity);
        var no = coin.GetComponent<NetworkObject>();
        if (no != null) no.Spawn();
    }
}
