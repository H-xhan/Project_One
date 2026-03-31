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

    [Header("Elimination")]
    [Tooltip("이 높이 아래로 떨어지면 탈락 처리되는 Y값")]
    [SerializeField] private float eliminationY = -15f;

    private bool isKnocked;
    private bool isEliminated;
    private float knockTimer;

    private NetworkObject rootNetObj;
    private Transform rootTransform;

    public bool IsKnocked => isKnocked;
    public bool IsEliminated => isEliminated;
    public bool CanMove => !isKnocked && !isEliminated;
    public bool CanAttack => !isKnocked && !isEliminated;
    public bool CanInteract => !isKnocked && !isEliminated;

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

        BeginKnockback(impulse);
    }

    public void ForceRecoverServer()
    {
        if (!IsServer) return;
        if (!isKnocked) return;

        RecoverFromKnock();
    }

    private void ResolveRefs()
    {
        if (rootRigidbody == null)
            rootRigidbody = GetComponentInParent<Rigidbody>();

        if (charController == null)
            charController = GetComponentInParent<CharacterController>();

        if (rootNetObj == null)
            rootNetObj = GetComponentInParent<NetworkObject>();

        rootTransform = rootNetObj != null ? rootNetObj.transform : transform.root;
    }

    private void UpdateKnockState()
    {
        if (!isKnocked) return;

        knockTimer -= Time.deltaTime;
        if (knockTimer <= 0f)
            RecoverFromKnock();
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
        knockTimer = knockbackDuration;

        if (charController != null && charController.enabled)
            charController.enabled = false;

        rootRigidbody.isKinematic = false;
        rootRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rootRigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    private void RecoverFromKnock()
    {
        isKnocked = false;
        ApplyStandingPhysicsState();
    }

    private void ApplyStandingPhysicsState()
    {
        if (rootRigidbody != null)
        {
            // Dynamic 상태일 때만 속도를 0으로 정리
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

        Debug.Log($"[PlayerStatus] {name} eliminated.");

        if (rootRigidbody != null)
        {
            // Dynamic 상태일 때만 속도를 0으로 정리
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
        Debug.Log($"[PlayerStatus] TakeDamage -> {name}, damage:{damage}");
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}