using Unity.Netcode.Components; // [필수]
using UnityEngine;

public class PlayerAnimModule : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private NetworkAnimator networkAnimator; // [추가] 네트워크 동기화용

    [Header("Params")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string sprintParam = "IsSprinting";
    [SerializeField] private string groundedParam = "IsGrounded";

    [Header("Triggers")]
    [SerializeField] private string jumpTrigger = "Jump";
    [SerializeField] private string attackLightTrigger = "AttackLight";
    [SerializeField] private string pickUpTrigger = "PickUp";

    private int _speedHash;
    private int _sprintHash;
    private int _groundHash;
    private int _jumpHash;
    private int _attackLightHash;
    private int _pickUpHash;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInParent<Animator>();
        // [추가] 부모에 있는 NetworkAnimator 찾기
        if (networkAnimator == null) networkAnimator = GetComponentInParent<NetworkAnimator>();

        _speedHash = Animator.StringToHash(speedParam);
        _sprintHash = Animator.StringToHash(sprintParam);
        _groundHash = Animator.StringToHash(groundedParam);
        _jumpHash = Animator.StringToHash(jumpTrigger);
        _attackLightHash = Animator.StringToHash(attackLightTrigger);
        _pickUpHash = Animator.StringToHash(pickUpTrigger);
    }

    public void TickServer(PlayerLocomotionModule locomotion)
    {
        if (animator == null || locomotion == null) return;
        animator.SetFloat(_speedHash, locomotion.PlanarSpeed);
        animator.SetBool(_groundHash, locomotion.IsGrounded);
    }

    // [핵심] 일반 Animator 대신 NetworkAnimator를 통해 트리거 발동 (동기화)
    public void TriggerJump()
    {
        if (networkAnimator != null) networkAnimator.SetTrigger(_jumpHash);
        else if (animator != null) animator.SetTrigger(_jumpHash);
    }

    public void TriggerAttack(int weaponID)
    {
        // 1. "무기 타입"을 먼저 애니메이터에 알려줌 (Int 파라미터 필요)
        animator.SetInteger("WeaponType", weaponID);

        // 2. 그 다음 "공격해!" 하고 방아쇠를 당김
        animator.SetTrigger("Attack");
    }

    public void TriggerPickUp()
    {
        if (networkAnimator != null) networkAnimator.SetTrigger(_pickUpHash);
        else if (animator != null) animator.SetTrigger(_pickUpHash);
    }
}