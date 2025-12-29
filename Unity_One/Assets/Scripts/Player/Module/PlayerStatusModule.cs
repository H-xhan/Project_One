using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour
{
    [Header("Ragdoll Settings")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform hipsBone; // [중요] 랙돌의 중심(Pelvis/Hips) 뼈를 연결하세요

    [Header("Coin Settings")]
    [SerializeField] private GameObject coinPrefab; // 떨어뜨릴 코인 프리팹

    private Rigidbody[] _ragdollRbs;
    private Collider[] _ragdollColls;
    private bool _isRagdoll = false;

    private void Awake()
    {
        // 내 몸에 있는 모든 랙돌 부품 찾아오기
        _ragdollRbs = GetComponentsInChildren<Rigidbody>();
        _ragdollColls = GetComponentsInChildren<Collider>();

        // 시작할 때는 랙돌 끄기 (애니메이션 모드)
        ToggleRagdoll(false);
    }

    // 외부(CombatModule)에서 때릴 때 호출하는 함수
    public void TakeHit(Vector3 hitForce)
    {
        if (_isRagdoll) return; // 이미 넘어져 있으면 패스

        // 서버에게 "나 맞았어! 랙돌 켜줘!" 요청
        TakeHitServerRpc(hitForce);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TakeHitServerRpc(Vector3 hitForce)
    {
        // 모든 클라이언트에게 랙돌 켜라고 명령
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
        ToggleRagdoll(true);

        // 1. 날리기
        Rigidbody hipsRb = null;
        if (hipsBone != null && hipsBone.TryGetComponent(out hipsRb))
        {
            hipsRb.AddForce(hitForce, ForceMode.Impulse);
        }

        // 2. 멈출 때까지 기다리기 (속도 체크)
        float safetyTimer = 0f;
        yield return new WaitForSeconds(0.2f); // 최소 0.2초는 날아가게 둠

        while (safetyTimer < 5.0f)
        {
            safetyTimer += Time.deltaTime;
            // 속도가 거의 0이 되면 멈춘 것으로 간주
            if (hipsRb != null && hipsRb.linearVelocity.magnitude < 0.1f)
            {
                break;
            }
            yield return null;
        }

        // 3. [핵심] 자세 판별하기 (앞? 뒤?)
        bool isFaceUp = CheckFaceUp(); // 하늘을 보고 있나?

        if (IsServer) // 코인 생성 권한은 서버에만
        {
            if (isFaceUp)
            {
                // 등 대고 누웠으면 코인 드랍! (대성공)
                SpawnCoin();
            }
            else
            {
                // 엎드렸을 때도 주고 싶으면 여기에 SpawnCoin(); 추가하면 됩니다.
                // 지금은 "꽝" 느낌으로 로그만 띄웁니다.
                Debug.Log("엎어져서 코인이 안 나옵니다! (꽝)");
            }
        }

        yield return new WaitForSeconds(1.0f); // 잠시 숨 고르기

        // 4. 상황에 맞는 기상 애니메이션 실행
        ToggleRagdoll(false); // 물리 끄고 애니메이션 ON

        if (animator != null)
        {
            if (isFaceUp)
            {
                // 하늘 보고 누웠다 -> 등으로 일어나기
                animator.SetTrigger("StandUpBack");
            }
            else
            {
                // 땅 보고 엎드렸다 -> 배로 일어나기
                animator.SetTrigger("StandUpFront");
            }
        }

        _isRagdoll = false;
    }

    // 앞/뒤 판별 함수 (업그레이드)
    private bool CheckFaceUp()
    {
        if (hipsBone == null) return false;

        // Hips의 앞쪽(Forward)이 하늘(Up)과 같은 방향이면 "하늘을 보고 누운 것"
        float dot = Vector3.Dot(hipsBone.forward, Vector3.up);
        return dot > 0.0f; // 양수면 하늘, 음수면 땅을 봄
    }
    private void ToggleRagdoll(bool state)
    {
        // 애니메이터가 켜지면 물리가 꺼지고, 애니메이터가 꺼지면 물리가 켜짐
        if (animator != null) animator.enabled = !state;

        foreach (var rb in _ragdollRbs)
        {
            rb.isKinematic = !state; // 랙돌일 때는 Kinematic 끄기(물리 적용)
        }
    }

    // 등이 땅에 닿았는지 판별하는 로직
    private bool CheckBackOnGround()
    {
        if (hipsBone == null) return false;

        // Hips(골반)의 앞쪽(Forward) 벡터가 하늘(Up)을 보고 있으면 누운 것!
        // (캐릭터 뼈대마다 축이 다를 수 있으니 테스트 필요: 보통 Forward나 Up을 씀)
        float dot = Vector3.Dot(hipsBone.forward, Vector3.up);

        // 0.5 이상이면 하늘을 꽤 정면으로 보고 있다는 뜻
        return dot > 0.5f;
    }

    private void SpawnCoin()
    {
        if (coinPrefab == null) return;
        Debug.Log("코인 드랍! (등이 닿았다!)");

        GameObject coin = Instantiate(coinPrefab, transform.position + Vector3.up, Quaternion.identity);
        coin.GetComponent<NetworkObject>().Spawn();
    }
}