using Unity.Netcode.Components;
using UnityEngine;

public class PlayerAnimModule : MonoBehaviour
{
    [Header("Animator")]
    [Tooltip("플레이어 Animator 컴포넌트")]
    [SerializeField] private Animator animator;

    [Tooltip("Netcode용 NetworkAnimator(트리거/파라미터 동기화)")]
    [SerializeField] private NetworkAnimator networkAnimator;

    [Tooltip("속도 계산에 사용할 CharacterController(없으면 자동 탐색)")]
    [SerializeField] private CharacterController characterController;

    [Header("Params")]
    [Tooltip("이동 BlendTree에 사용하는 Float 파라미터 이름")]
    [SerializeField] private string speedParam = "Speed";

    [Tooltip("지면 판정 Bool 파라미터 이름")]
    [SerializeField] private string groundedParam = "IsGrounded";

    [Header("Triggers")]
    [Tooltip("점프 Trigger 파라미터 이름")]
    [SerializeField] private string jumpTrigger = "Jump";

    [Tooltip("줍기 Trigger 파라미터 이름")]
    [SerializeField] private string pickUpTrigger = "PickUp";

    private int _speedHash;
    private int _groundHash;
    private int _jumpHash;
    private int _pickUpHash;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponentInParent<NetworkAnimator>();
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();

        _speedHash = Animator.StringToHash(speedParam);
        _groundHash = Animator.StringToHash(groundedParam);
        _jumpHash = Animator.StringToHash(jumpTrigger);
        _pickUpHash = Animator.StringToHash(pickUpTrigger);
    }

    public void TickServer(PlayerLocomotionModule locomotion)
    {
        if (animator == null || locomotion == null) return;

        float planarSpeed = locomotion.PlanarSpeed;

        if (planarSpeed <= 0.001f && characterController != null)
        {
            Vector3 v = characterController.velocity;
            planarSpeed = new Vector2(v.x, v.z).magnitude;
        }

        animator.SetFloat(_speedHash, planarSpeed);
        animator.SetBool(_groundHash, locomotion.IsGrounded);
    }

    public void TriggerJump()
    {
        if (networkAnimator != null) networkAnimator.SetTrigger(_jumpHash);
        else if (animator != null) animator.SetTrigger(_jumpHash);
    }

    public void TriggerAttack(int weaponID)
    {
        if (animator == null) return;

        animator.SetInteger("WeaponType", weaponID);
        animator.SetTrigger("Attack");
    }

    public void TriggerPickUp()
    {
        if (networkAnimator != null) networkAnimator.SetTrigger(_pickUpHash);
        else if (animator != null) animator.SetTrigger(_pickUpHash);
    }
}
