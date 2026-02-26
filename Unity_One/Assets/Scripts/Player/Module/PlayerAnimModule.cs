using Unity.Netcode;
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

    [Header("Grab Target Sync")]
    [Tooltip("무기/오브젝트가 붙는 손 소켓(WeaponPoint). 비워두면 RightHandBone을 사용합니다.")]
    [SerializeField] private Transform weaponPoint;

    [Tooltip("오른손 본(비워두면 휴머노이드에서 자동 탐색)")]
    [SerializeField] private Transform rightHandBone;

    [Tooltip("오른손 그랩 타겟(런타임 스폰된 RightHandGrabTarget). 오너가 기준점 위치/회전으로 갱신합니다.")]
    [SerializeField] private Transform rightHandGrabTarget;

    [Tooltip("그랩 타겟을 매 프레임 갱신할지")]
    [SerializeField] private bool updateGrabTarget = true;

    [Tooltip("그랩 타겟 갱신을 LateUpdate에서 할지(애니메이션 적용 후 위치 정확도 상승)")]
    [SerializeField] private bool updateGrabTargetInLateUpdate = true;

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

    private NetworkObject _netObj;

    private void Awake()
    {
        AutoFindRefs();
        CacheHashes();
    }

    private void AutoFindRefs()
    {
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponentInParent<NetworkAnimator>();
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();

        _netObj = GetComponentInParent<NetworkObject>();

        if (rightHandBone == null && animator != null && animator.isHuman)
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

    private void CacheHashes()
    {
        _speedHash = Animator.StringToHash(speedParam);
        _groundHash = Animator.StringToHash(groundedParam);
        _jumpHash = Animator.StringToHash(jumpTrigger);
        _pickUpHash = Animator.StringToHash(pickUpTrigger);
    }

    private void Update()
    {
        if (updateGrabTarget && !updateGrabTargetInLateUpdate)
            UpdateGrabTargetTransform();
    }

    private void LateUpdate()
    {
        if (updateGrabTarget && updateGrabTargetInLateUpdate)
            UpdateGrabTargetTransform();
    }

    private void UpdateGrabTargetTransform()
    {
        if (rightHandGrabTarget == null) return;

        if (_netObj == null) return;
        if (!_netObj.IsSpawned) return;

        // 오너만 기준점 갱신(네트워크 동기화는 NetworkTransform이 처리)
        if (!_netObj.IsOwner) return;

        Transform src = weaponPoint != null ? weaponPoint : rightHandBone;
        if (src == null) return;

        rightHandGrabTarget.SetPositionAndRotation(src.position, src.rotation);
    }

    // PlayerHub가 런타임 스폰한 GrabTarget을 주입할 때 사용
    public void SetGrabTarget(Transform t)
    {
        rightHandGrabTarget = t;
    }

    // 인스펙터에서 못 넣었을 때 외부에서 소켓 주입 가능
    public void SetWeaponPoint(Transform t)
    {
        weaponPoint = t;
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoFindRefs();
        CacheHashes();
    }
#endif
}
