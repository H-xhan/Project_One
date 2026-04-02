using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour, IDamageable
{
    [Header("Knockback")]
    [Tooltip("넉백/다운 시 물리를 적용할 루트 Rigidbody")]
    [SerializeField] private Rigidbody rootRigidbody;

    [Tooltip("평상시 이동을 담당하는 CharacterController")]
    [SerializeField] private CharacterController charController;

    [Tooltip("다운 상태 유지 시간(초)")]
    [SerializeField] private float knockbackDuration = 1.2f;

    [Tooltip("기상 가능 판정으로 볼 Rigidbody 속도 임계값")]
    [SerializeField] private float standUpVelocityThreshold = 0.15f;

    [Tooltip("애니 이벤트가 누락됐을 때 강제로 standing 상태로 복귀시키는 최대 대기 시간")]
    [SerializeField] private float standUpFallbackDuration = 1.2f;

    [Header("Hit Reaction")]
    [Tooltip("피격 트리거를 보낼 애니메이션 모듈")]
    [SerializeField] private PlayerAnimModule animModule;

    [Tooltip("데미지를 받으면 Hit 트리거를 보낼지")]
    [SerializeField] private bool triggerHitOnDamage = false;

    [Tooltip("피격 트리거 최소 간격(초). 너무 짧으면 애니메이션이 과도하게 끊깁니다.")]
    [SerializeField] private float hitReactionCooldown = 0.12f;

    [Tooltip("다운 상태에서도 Hit 트리거를 허용할지")]
    [SerializeField] private bool triggerHitWhileKnocked = false;

    [Header("Elimination")]
    [Tooltip("이 높이 아래로 떨어지면 탈락 처리되는 Y값")]
    [SerializeField] private float eliminationY = -15f;

    private bool isKnocked;
    private bool isStandingUp;
    private bool isEliminated;
    private float knockTimer;
    private float standUpTimer;
    private float nextHitReactionAt;

    private NetworkObject rootNetObj;
    private Transform rootTransform;

    public bool IsKnocked => isKnocked;
    public bool IsStandingUp => isStandingUp;
    public bool IsEliminated => isEliminated;

    public bool CanMove => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanAttack => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanInteract => !isKnocked && !isStandingUp && !isEliminated;

    private void Awake()
    {
        ResolveRefs();
        ApplyStandingPhysicsState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveRefs();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (isEliminated) return;

        UpdateKnockState();
        UpdateStandUpState();
        CheckElimination();
    }

    public void ApplyKnockbackServer(Vector3 impulse)
    {
        if (!IsServer) return;
        if (isEliminated) return;
        if (isKnocked || isStandingUp) return;

        if (rootRigidbody == null || charController == null)
        {
            Debug.LogWarning("[PlayerStatus] Knockback skipped. Missing Rigidbody or CharacterController.");
            return;
        }

        BeginKnockback(impulse);
    }

    public void ForceRecoverServer()
    {
        if (!IsServer) return;

        if (isKnocked)
        {
            knockTimer = 0f;
            BeginStandUpBack();
            return;
        }

        if (isStandingUp)
            FinishStandUp();
    }

    [ContextMenu("Auto Find Refs")]
    private void ResolveRefs()
    {
        if (rootRigidbody == null)
            rootRigidbody = GetComponentInParent<Rigidbody>();

        if (charController == null)
            charController = GetComponentInParent<CharacterController>();

        if (animModule == null)
            animModule = GetComponentInParent<PlayerAnimModule>();

        if (rootNetObj == null)
            rootNetObj = GetComponentInParent<NetworkObject>();

        rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
    }

    private void UpdateKnockState()
    {
        if (!isKnocked) return;

        knockTimer -= Time.deltaTime;
        if (knockTimer > 0f)
            return;

        if (rootRigidbody != null)
        {
            float speed = rootRigidbody.linearVelocity.magnitude;
            if (speed > standUpVelocityThreshold)
                return;
        }

        BeginStandUpBack();
    }

    private void UpdateStandUpState()
    {
        if (!isStandingUp) return;

        standUpTimer -= Time.deltaTime;
        if (standUpTimer > 0f)
            return;

        Debug.LogWarning("[PlayerStatus] Stand up fallback fired.");
        FinishStandUp();
    }

    private void CheckElimination()
    {
        Transform checkTf = rootTransform != null ? rootTransform : transform.root;
        if (checkTf == null) return;

        if (checkTf.position.y < eliminationY)
            HandleElimination();
    }

    private void BeginKnockback(Vector3 impulse)
    {
        isKnocked = true;
        isStandingUp = false;
        knockTimer = knockbackDuration;
        standUpTimer = 0f;

        if (charController != null && charController.enabled)
            charController.enabled = false;

        rootRigidbody.isKinematic = false;
        rootRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rootRigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    private void BeginStandUpBack()
    {
        if (isStandingUp) return;

        isKnocked = false;
        isStandingUp = true;
        knockTimer = 0f;
        standUpTimer = Mathf.Max(0.1f, standUpFallbackDuration);

        if (rootRigidbody != null)
        {
            rootRigidbody.linearVelocity = Vector3.zero;
            rootRigidbody.angularVelocity = Vector3.zero;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        // 기상 애니메이션이 끝날 때까지 CharacterController는 꺼둡니다.
        if (charController != null && charController.enabled)
            charController.enabled = false;

        if (animModule != null)
        {
            animModule.TriggerStandUpBack();
        }
        else
        {
            Debug.LogWarning("[PlayerStatus] animModule is null. Stand up animation skipped.");
            FinishStandUp();
        }
    }

    private void FinishStandUp()
    {
        isStandingUp = false;
        ApplyStandingPhysicsState();
    }

    private void ApplyStandingPhysicsState()
    {
        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (charController != null && !charController.enabled)
            charController.enabled = true;
    }

    private void HandleElimination()
    {
        if (isEliminated) return;
        isEliminated = true;
        isKnocked = false;
        isStandingUp = false;

        Debug.Log($"[PlayerStatus] {name} eliminated.");

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (charController != null && charController.enabled)
            charController.enabled = false;

        if (rootNetObj != null && rootNetObj.IsSpawned)
        {
            rootNetObj.Despawn();
            return;
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer) return;
        if (isEliminated) return;

        Debug.Log($"[PlayerStatus] TakeDamage -> {name}, damage:{damage}");
        TryTriggerHitReaction();
    }

    public void AnimEvent_StandUpBackFinished()
    {
        if (!IsServer) return;
        if (!isStandingUp) return;

        FinishStandUp();
    }

    private void TryTriggerHitReaction()
    {
        if (!triggerHitOnDamage) return;
        if (animModule == null) return;
        if (isKnocked && !triggerHitWhileKnocked) return;
        if (Time.time < nextHitReactionAt) return;

        nextHitReactionAt = Time.time + Mathf.Max(0f, hitReactionCooldown);
        animModule.TriggerHit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}
