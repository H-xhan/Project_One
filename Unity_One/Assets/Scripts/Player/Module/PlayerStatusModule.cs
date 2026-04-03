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

    [Tooltip("약간의 랜덤 회전을 섞어 덜 기계적으로 보이게 합니다.")]
    [SerializeField] private float randomYawTorque = 2.5f;

    [Tooltip("진짜 물리로 굴릴지 여부. 꺼두면 회전은 고정된 채로 밀려납니다.")]
    [SerializeField] private bool allowKnockRotation = true;

    [Header("Stand Up")]
    [Tooltip("기상 트리거를 보낼 애니메이션 모듈")]
    [SerializeField] private PlayerAnimModule animModule;

    [Tooltip("기상 애니메이션 상태 확인에 사용할 Animator")]
    [SerializeField] private Animator rootAnimator;

    [Tooltip("다운 종료 후 Back Stand Up 애니메이션으로 기상할지")]
    [SerializeField] private bool useBackStandUp = true;

    [Tooltip("기상 애니메이션 중 CharacterController를 계속 끌지")]
    [SerializeField] private bool disableControllerDuringStandUp = true;

    [Tooltip("애니메이션 이벤트가 누락됐을 때 강제로 standing으로 복귀시키는 최대 대기 시간(초)")]
    [SerializeField] private float standUpFallbackTime = 3.6f;

    [Tooltip("기상 시작 시 루트 회전을 지면 기준으로 바로 세웁니다.")]
    [SerializeField] private bool snapUprightOnStandUp = true;

    [Header("Ground Snap")]
    [Tooltip("기상 완료 시 루트를 바닥에 맞춰 한 번 내려붙일지")]
    [SerializeField] private bool snapToGroundOnStandUpFinish = true;

    [Tooltip("기상 애니 재생 중에도 루트를 바닥에 계속 붙일지")]
    [SerializeField] private bool snapToGroundDuringStandUp = true;

    [Tooltip("기상 애니에서 이 normalized time 이후부터 ground snap을 시작합니다.")]
    [SerializeField] private float standUpSnapStartNormalized = 0.10f;

    [Tooltip("기상 완료 직후 CharacterController를 켠 다음 아래로 살짝 눌러 바닥에 붙입니다.")]
    [SerializeField] private float postStandUpDownNudge = 0.02f;

    [Tooltip("바닥 검사 시작 높이")]
    [SerializeField] private float groundProbeHeight = 1.2f;

    [Tooltip("바닥 검사 최대 거리")]
    [SerializeField] private float groundProbeDistance = 3.0f;

    [Tooltip("기상 후 바닥에 살짝 띄워둘 보정값")]
    [SerializeField] private float groundContactOffset = 0.01f;

    [Tooltip("바닥 스냅에 사용할 레이어 마스크. 비워두면 기본 Raycast 레이어를 사용합니다.")]
    [SerializeField] private LayerMask groundMask;

    [Tooltip("루트/자식 자신의 콜라이더는 바닥 판정에서 무시합니다.")]
    [SerializeField] private bool ignoreOwnCollidersOnGroundSnap = true;

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
    private int backStandUpStateHash;

    public bool IsKnocked => isKnocked;
    public bool IsStandingUp => isStandingUp;
    public bool IsEliminated => isEliminated;
    public bool CanMove => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanAttack => !isKnocked && !isStandingUp && !isEliminated;
    public bool CanInteract => !isKnocked && !isStandingUp && !isEliminated;

    private void Awake()
    {
        ResolveRefs();
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
        if (rootRigidbody != null)
            cachedConstraints = rootRigidbody.constraints;
        ApplyStandingPhysicsState();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ResolveRefs();
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
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

        if (rootAnimator == null)
            rootAnimator = GetComponentInParent<Animator>();

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

        if (rootAnimator != null)
        {
            AnimatorStateInfo state = rootAnimator.GetCurrentAnimatorStateInfo(0);
            bool inBackStandUp = state.shortNameHash == backStandUpStateHash || state.IsName("Base Layer.Back Stand Up") || state.IsName("Back Stand Up");

            if (inBackStandUp)
            {
                if (snapToGroundDuringStandUp && state.normalizedTime >= standUpSnapStartNormalized && state.normalizedTime < 0.98f)
                    SnapRootToGround();

                if (state.normalizedTime >= 0.92f)
                {
                    FinishStandUpImmediate();
                    return;
                }
            }
        }

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

        if (snapUprightOnStandUp)
            SnapRootUpright();

        if (snapToGroundDuringStandUp)
            SnapRootToGround();

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

    private void SnapRootUpright()
    {
        if (rootTransform == null)
            rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
        if (rootTransform == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(rootTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(rootTransform.up, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Quaternion upright = Quaternion.LookRotation(forward.normalized, Vector3.up);

        if (rootRigidbody != null)
            rootRigidbody.MoveRotation(upright);

        rootTransform.rotation = upright;
    }

    private void SnapRootToGround()
    {
        if (!snapToGroundOnStandUpFinish && !snapToGroundDuringStandUp)
            return;

        if (rootTransform == null)
            return;

        Vector3 origin = rootTransform.position + Vector3.up * Mathf.Max(0.1f, groundProbeHeight);
        float distance = Mathf.Max(0.2f, groundProbeDistance);
        int mask = groundMask.value == 0 ? Physics.DefaultRaycastLayers : groundMask.value;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, mask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            if (ignoreOwnCollidersOnGroundSnap && hit.collider.transform.root == rootTransform)
                continue;

            float bottomOffset = 0f;
            if (charController != null)
            {
                float halfHeight = Mathf.Max(charController.radius, charController.height * 0.5f);
                bottomOffset = charController.center.y - halfHeight;
            }

            float snappedY = hit.point.y - bottomOffset + groundContactOffset;
            Vector3 snappedPos = new Vector3(rootTransform.position.x, snappedY, rootTransform.position.z);

            if (rootRigidbody != null)
                rootRigidbody.position = snappedPos;

            rootTransform.position = snappedPos;
            Physics.SyncTransforms();
            return;
        }
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
        if (snapUprightOnStandUp)
            SnapRootUpright();

        if (snapToGroundOnStandUpFinish)
            SnapRootToGround();

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
        {
            charController.enabled = true;
            if (postStandUpDownNudge > 0f)
                charController.Move(Vector3.down * postStandUpDownNudge);
        }
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
        backStandUpStateHash = Animator.StringToHash("Back Stand Up");
    }
#endif
}
