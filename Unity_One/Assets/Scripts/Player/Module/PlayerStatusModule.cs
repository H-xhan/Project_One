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
    [SerializeField] private float recoverTime = 3.0f; // 넘어지고 일어나는 시간

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

    [ServerRpc(RequireOwnership = false)]
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

        // 1. 애니메이션 끄고 물리 켜기 (흐물흐물)
        ToggleRagdoll(true);

        // 2. 맞은 방향으로 힘 가하기 (밀려나기)
        if (hipsBone != null && hipsBone.TryGetComponent(out Rigidbody hipsRb))
        {
            hipsRb.AddForce(hitForce, ForceMode.Impulse);
        }

        // 3. 넘어지는 동안 등 닿았는지 체크 (약 3초간)
        float timer = 0f;
        bool coinDropped = false;

        while (timer < recoverTime)
        {
            timer += Time.deltaTime;

            // [핵심] 등이 땅에 닿았는가? (서버에서만 체크해서 코인 생성)
            if (IsServer && !coinDropped && CheckBackOnGround())
            {
                coinDropped = true;
                SpawnCoin();
            }

            yield return null;
        }

        // 4. 다시 일어나기
        ToggleRagdoll(false);
        // (일어나는 애니메이션인 'Stand Up' 트리거 등을 여기서 실행하면 더 자연스러움)
        // if(animator != null) animator.SetTrigger("StandUp"); 

        _isRagdoll = false;
    }

    // 랙돌 On/Off 스위치
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