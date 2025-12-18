using Unity.Netcode;
using UnityEngine;

public class PlayerAnimDriver : NetworkBehaviour
{
    [Tooltip("캐릭터 Animator")]
    [SerializeField] private Animator animator;

    [Tooltip("최대 이동 속도(정규화 기준)")]
    [SerializeField] private float maxMoveSpeed = 5f;

    [Tooltip("Speed 파라미터 댐핑(부드럽게)")]
    [SerializeField] private float speedDamp = 0.08f;

    [Tooltip("Speed 전송 간격(초). 너무 촘촘하면 트래픽 증가")]
    [SerializeField] private float speedSendInterval = 0.08f;

    [Tooltip("Speed 값 변화가 이 값보다 작으면 전송 생략")]
    [SerializeField] private float speedChangeThreshold = 0.02f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int HitSweepHash = Animator.StringToHash("HitSweep");
    private static readonly int AttackHeavyHash = Animator.StringToHash("AttackHeavy");

    private float _lastSentSpeed;
    private float _nextSendTime;

    public void SetMoveSpeed(float currentSpeed)
    {
        if (!IsOwner || animator == null) return;

        float v = (maxMoveSpeed <= 0f) ? 0f : Mathf.Clamp01(currentSpeed / maxMoveSpeed);

        // 로컬 즉시 반응
        animator.SetFloat(SpeedHash, v, speedDamp, Time.deltaTime);

        // 서버에도 전달 (서버가 세팅 -> NetworkAnimator가 전파)
        TrySendSpeed(v);
    }

    public void SetMoveSpeed(float currentSpeed, float referenceMaxSpeed)
    {
        if (!IsOwner || animator == null) return;

        if (referenceMaxSpeed > 0f)
            maxMoveSpeed = referenceMaxSpeed; // sprintSpeed 기준으로 정규화

        SetMoveSpeed(currentSpeed); // 기존 1개짜리 호출
    }

    public void PlayJump()
    {
        if (!IsOwner || animator == null) return;

        animator.SetTrigger(JumpHash);
        PlayTriggerServerRpc(JumpHash);
    }

    public void PlayHeavyAttack()
    {
        if (!IsOwner || animator == null) return;

        animator.SetTrigger(AttackHeavyHash);
        PlayTriggerServerRpc(AttackHeavyHash);
    }

    public void PlayHitSweep()
    {
        if (!IsOwner || animator == null) return;

        animator.SetTrigger(HitSweepHash);
        PlayTriggerServerRpc(HitSweepHash);
    }

    private void TrySendSpeed(float v)
    {
        if (IsServer) return; // Host는 서버에서 직접 전파되므로 RPC 불필요

        if (Time.time < _nextSendTime) return;
        if (Mathf.Abs(v - _lastSentSpeed) < speedChangeThreshold) return;

        _lastSentSpeed = v;
        _nextSendTime = Time.time + speedSendInterval;

        SetSpeedServerRpc(v);
    }

    [ServerRpc]
    private void SetSpeedServerRpc(float normalizedSpeed)
    {
        if (animator == null) return;
        animator.SetFloat(SpeedHash, normalizedSpeed);
    }

    [ServerRpc]
    private void PlayTriggerServerRpc(int triggerHash)
    {
        if (animator == null) return;
        animator.SetTrigger(triggerHash);
    }
}
