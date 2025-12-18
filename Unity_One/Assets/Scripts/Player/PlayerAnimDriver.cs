using Unity.Netcode;
using UnityEngine;

public class PlayerAnimDriver : NetworkBehaviour
{
    [Tooltip("캐릭터 Animator (비워두면 자동 탐색)")]
    [SerializeField] private Animator animator;

    [Tooltip("속도 정규화 기준(달리기 최고속도 권장)")]
    [SerializeField] private float maxMoveSpeed = 7.0f;

    [Tooltip("Speed 보간 시간")]
    [SerializeField] private float speedDamp = 0.08f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int HitSweepHash = Animator.StringToHash("HitSweep");
    private static readonly int AttackHeavyHash = Animator.StringToHash("AttackHeavy");

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public void SetMoveSpeed(float currentSpeed)
    {
        SetMoveSpeed(currentSpeed, maxMoveSpeed);
    }

    public void SetMoveSpeed(float currentSpeed, float maxSpeed)
    {
        if (!IsOwner) return;
        if (animator == null) return;

        float denom = Mathf.Max(0.01f, maxSpeed);
        float v = Mathf.Clamp01(currentSpeed / denom);

        animator.SetFloat(SpeedHash, v, speedDamp, Time.deltaTime);
    }

    public void PlayJump()
    {
        if (!IsOwner) return;
        if (animator == null) return;

        animator.SetTrigger(JumpHash);
    }

    public void PlayHeavyAttack()
    {
        if (!IsOwner) return;
        if (animator == null) return;

        animator.SetTrigger(AttackHeavyHash);
    }

    public void PlayHitSweep()
    {
        if (!IsOwner) return;
        if (animator == null) return;

        animator.SetTrigger(HitSweepHash);
    }
}
