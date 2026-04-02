using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour, IDamageable
{
    [Header("Knockback")]
    [Tooltip("넉백/다운 시 물리를 적용할 루트 Rigidbody")]
    [SerializeField] private Rigidbody rootRigidbody;

    [Tooltip("평상시 이동을 담당하는 CharacterController")]
    [SerializeField] private CharacterController charController;

    [Tooltip("이동 모듈. 기상/다운 전환 때 잔여 속도를 초기화합니다.")]
    [SerializeField] private PlayerLocomotionModule locomotionModule;

    [Tooltip("다운 상태 유지 시간(초)")]
    [SerializeField] private float knockbackDuration = 1.2f;

    [Tooltip("이 속도 이하로 떨어져야 기상 루프를 시작합니다.")]
    [SerializeField] private float standUpVelocityThreshold = 0.15f;

    [Header("Knockback Tuning")]
    [Tooltip("질량 영향을 무시하고 바로 속도를 부여합니다. 작은 캐릭터 넉백에 더 잘 맞습니다.")]
    [SerializeField] private bool useVelocityChange = true;

    [Tooltip("수평 넉백 세기. 뒤로 미는 힘을 얼마나 줄지 조절합니다.")]
    [SerializeField] private float horizontalLaunchScale = 0.65f;

    [Tooltip("위로 띄우는 최소 속도. 이 값이 낮으면 미끄러지듯 밀립니다.")]
    [SerializeField] private float minimumUpwardLaunch = 2.8f;

    [Tooltip("기본 위쪽 힘에 추가로 더해줄 상승 보정값입니다.")]
    [SerializeField] private float upwardLaunchBonus = 1.1f;

    [Tooltip("옆으로/앞뒤로 구르도록 주는 회전 토크 세기입니다.")]
    [SerializeField] private float tumbleTorque = 10f;

    [Tooltip("약간의 랜덤 회전을 섞어 파티 애니멀즈처럼 덜 기계적으로 보이게 합니다.")]
    [SerializeField] private float randomYawTorque = 2.5f;

    [Tooltip("진짜 물리로 굴릴지 여부. 꺼두면 회전은 고정된 채로 밀려납니다.")]
    [SerializeField] private bool allowKnockRotation = true;

    [Header("Stand Up")]
    [Tooltip("기상 트리거를 보낼 애니메이션 모듈")]
    [SerializeField] private PlayerAnimModule animModule;

    [Tooltip("다운 종료 후 Back Stand Up 애니메이션으로 기상할지")]
    [SerializeField] private bool useBackStandUp = true;

    [Tooltip("기상 애니메이션 중 CharacterController를 계속 끌지")]
    [SerializeField] private bool disableControllerDuringStandUp = true;

    [Tooltip("애니메이션 이벤트가 누락됐을 때 강제로 standing으로 복귀시키는 최대 대기 시간(초)")]
    [SerializeField] private float standUpFallbackTime = 2.0f;

    [Header("Hit Reaction")]
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
    private float nextHitReactionAt;
    private float standUpTimer;

    private NetworkObject rootNetObj;
    private Transform rootTransform;
    private RigidbodyConstraints cachedConstraints;

    public bool IsKnocked => isKnocked;
    public bool IsStandingUp => isStandingUp;
    public bool IsEliminated => isEliminated;
    public bool CanMove => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanAttack => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanInteract => !isKnocked && !isStandingUp && !isEliminated;

    private void Awake()
    {
        ResolveRefs();
        if (rootRigidbody != null)
            cachedConstraints = rootRigidbody.constraints;
        ApplyStandingPhysicsState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ResolveRefs();
        if (rootRigidbody != null)
            cachedConstraints = rootRigidbody.constraints;
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
        if (isKnocked) return;

        if (rootRigidbody == null || charController == null)
        {
            Debug.LogWarning("[PlayerStatus] Knockback skipped. Missing Rigidbody or CharacterController.");
            return;
        }

        if (isStandingUp)
            isStandingUp = false;

        BeginKnockback(impulse);
    }

    public void ForceRecoverServer()
    {
        if (!IsServer) return;
        if (isEliminated) return;

        if (isKnocked)
        {
            BeginStandUpBack();
            return;
        }

        if (isStandingUp)
            FinishStandUpImmediate();
    }

    [ContextMenu("Auto Find Refs")]
    private void ResolveRefs()
    {
        if (rootRigidbody == null)
            rootRigidbody = GetComponentInParent<Rigidbody>();

        if (charController == null)
            charController = GetComponentInParent<CharacterController>();

        if (locomotionModule == null)
            locomotionModule = GetComponentInParent<PlayerLocomotionModule>();

        if (animModule == null)
            animModule = GetComponentInParent<PlayerAnimModule>();

        if (rootNetObj == null)
            rootNetObj = GetComponentInParent<NetworkObject>();

        rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
    }

    private void UpdateKnockState()
    {
        if (!isKnocked) return;
        if (isStandingUp) return;

        knockTimer -= Time.deltaTime;
        if (knockTimer > 0f)
            return;

        float speed = 0f;
        if (rootRigidbody != null && !rootRigidbody.isKinematic)
            speed = rootRigidbody.linearVelocity.magnitude;

        if (speed > standUpVelocityThreshold)
            return;

        BeginStandUpBack();
    }

    private void UpdateStandUpState()
    {
        if (!isStandingUp) return;

        standUpTimer -= Time.deltaTime;
        if (standUpTimer > 0f)
            return;

        Debug.LogWarning("[PlayerStatus] Stand up fallback fired.");
        FinishStandUpImmediate();
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
        knockTimer = Mathf.Max(0.01f, knockbackDuration);
        standUpTimer = 0f;

        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (charController != null && charController.enabled)
            charController.enabled = false;

        if (rootRigidbody == null)
            return;

        rootRigidbody.isKinematic = false;
        rootRigidbody.useGravity = true;
        rootRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rootRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rootRigidbody.WakeUp();
        rootRigidbody.linearVelocity = Vector3.zero;
        rootRigidbody.angularVelocity = Vector3.zero;

        rootRigidbody.constraints = allowKnockRotation
            ? RigidbodyConstraints.None
            : cachedConstraints;

        Vector3 flat = new Vector3(impulse.x, 0f, impulse.z);
        Vector3 launch = flat * horizontalLaunchScale;
        launch.y = Mathf.Max(minimumUpwardLaunch, impulse.y + upwardLaunchBonus);

        if (useVelocityChange)
            rootRigidbody.AddForce(launch, ForceMode.VelocityChange);
        else
            rootRigidbody.AddForce(launch, ForceMode.Impulse);

        if (allowKnockRotation)
        {
            Vector3 axis = flat.sqrMagnitude > 0.0001f
                ? Vector3.Cross(flat.normalized, Vector3.up)
                : rootTransform != null ? rootTransform.right : Vector3.right;

            Vector3 torque = axis * tumbleTorque + Vector3.up * Random.Range(-randomYawTorque, randomYawTorque);

            if (useVelocityChange)
                rootRigidbody.AddTorque(torque, ForceMode.VelocityChange);
            else
                rootRigidbody.AddTorque(torque, ForceMode.Impulse);

            Debug.Log($"[PlayerStatus] Knockback launch:{launch}, torque:{torque}, mode:{(useVelocityChange ? "VelocityChange" : "Impulse")}, mass:{rootRigidbody.mass}");
            return;
        }

        Debug.Log($"[PlayerStatus] Knockback launch:{launch}, mode:{(useVelocityChange ? "VelocityChange" : "Impulse")}, mass:{rootRigidbody.mass}");
    }

    private void BeginStandUpBack()
    {
        if (!IsServer) return;
        if (isStandingUp) return;
        if (isEliminated) return;

        isKnocked = false;
        isStandingUp = true;
        standUpTimer = Mathf.Max(0.2f, standUpFallbackTime);

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
            rootRigidbody.isKinematic = true;
            rootRigidbody.Sleep();
        }

        if (disableControllerDuringStandUp && charController != null && charController.enabled)
            charController.enabled = false;

        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (animModule != null && useBackStandUp)
        {
            animModule.TriggerStandUpBack();
            return;
        }

        FinishStandUpImmediate();
    }

    public void AnimEvent_StandUpFinished()
    {
        if (!IsServer) return;
        if (!isStandingUp) return;

        FinishStandUpImmediate();
    }

    public void AnimEvent_StandUpBackFinished()
    {
        AnimEvent_StandUpFinished();
    }

    private void FinishStandUpImmediate()
    {
        isKnocked = false;
        isStandingUp = false;
        standUpTimer = 0f;
        ApplyStandingPhysicsState();
    }

    private void ApplyStandingPhysicsState()
    {
        if (locomotionModule != null)
            locomotionModule.ResetMotionServer();

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
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
        standUpTimer = 0f;

        Debug.Log($"[PlayerStatus] {name} eliminated.");

        if (rootRigidbody != null)
        {
            if (!rootRigidbody.isKinematic)
            {
                rootRigidbody.linearVelocity = Vector3.zero;
                rootRigidbody.angularVelocity = Vector3.zero;
            }

            rootRigidbody.constraints = cachedConstraints;
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

    private void TryTriggerHitReaction()
    {
        if (!triggerHitOnDamage) return;
        if (animModule == null) return;
        if (isStandingUp) return;
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
