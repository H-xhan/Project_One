using Unity.Netcode;
using UnityEngine;

public class PlayerStatusModule : NetworkBehaviour
{
    [Header("Knockback")]
    [SerializeField] private Rigidbody rootRigidbody;
    [SerializeField] private CharacterController charController;
    [SerializeField] private float knockbackDuration = 1.2f;

    private bool isKnocked;
    private float knockTimer;

    private NetworkObject _rootNetObj;

    private void Awake()
    {
        // Module(자식)에 붙어있어도 Root에서 찾아오기
        if (!rootRigidbody) rootRigidbody = GetComponentInParent<Rigidbody>();
        if (!charController) charController = GetComponentInParent<CharacterController>();
        _rootNetObj = GetComponentInParent<NetworkObject>();
    }

    private void Update()
    {
        if (!IsServer) return;

        if (isKnocked)
        {
            knockTimer -= Time.deltaTime;
            if (knockTimer <= 0f)
                RecoverFromKnock();
        }

        // 낙사 판정(서버에서만)
        Transform checkTf = (_rootNetObj != null) ? _rootNetObj.transform : transform.root;
        if (checkTf.position.y < -15f)
            HandleElimination();
    }

    // 서버에서만 호출 (CombatModule.DoAttackServer -> 여기)
    public void ApplyKnockbackServer(Vector3 impulse)
    {
        if (!IsServer) return;
        ApplyKnockbackInternal(impulse);
    }

    private void ApplyKnockbackInternal(Vector3 impulse)
    {
        if (isKnocked) return;
        if (rootRigidbody == null || charController == null) return;

        isKnocked = true;
        knockTimer = knockbackDuration;

        // 피격 동안에는 물리가 주도권
        charController.enabled = false;
        rootRigidbody.isKinematic = false;
        rootRigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        rootRigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    private void RecoverFromKnock()
    {
        isKnocked = false;

        if (rootRigidbody != null)
        {
            rootRigidbody.linearVelocity = Vector3.zero;
            rootRigidbody.angularVelocity = Vector3.zero;
            rootRigidbody.isKinematic = true;
        }

        if (charController != null)
            charController.enabled = true;
    }

    private void HandleElimination()
    {
        Debug.Log($"[Server] {name} Eliminated!");

        // Root NetworkObject 기준으로 Despawn
        if (_rootNetObj != null && _rootNetObj.IsSpawned)
            _rootNetObj.Despawn();
        else if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }
}
