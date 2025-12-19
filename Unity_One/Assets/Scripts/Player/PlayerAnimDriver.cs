using Unity.Netcode;
using UnityEngine;

public class PlayerAnimDriver : NetworkBehaviour
{
    [Tooltip("캐릭터 Animator")]
    [SerializeField] private Animator animator;

    [Tooltip("최대 이동 속도(정규화 기준). Speed = currentSpeed / maxMoveSpeed")]
    [SerializeField] private float maxMoveSpeed = 7f;

    [Header("Animator Parameters")]
    [Tooltip("이동 속도 Float 파라미터 이름")]
    [SerializeField] private string speedParam = "Speed";

    [Tooltip("점프 Trigger 파라미터 이름")]
    [SerializeField] private string jumpTrigger = "Jump";

    [Tooltip("약공격 Trigger 파라미터 이름")]
    [SerializeField] private string lightAttackTrigger = "AttackLight";

    [Tooltip("강공격 Trigger 파라미터 이름")]
    [SerializeField] private string heavyAttackTrigger = "AttackHeavy";

    [Tooltip("피격 Trigger 파라미터 이름")]
    [SerializeField] private string hitReactTrigger = "HitReact";

    [Tooltip("픽업 Trigger 파라미터 이름")]
    [SerializeField] private string pickUpTrigger = "PickUp";

    private int _speedHash;
    private int _jumpHash;
    private int _lightHash;
    private int _heavyHash;
    private int _hitHash;
    private int _pickUpHash;

    private float _lastSentSpeed;
    private float _nextSpeedSendTime;

    private void Awake() => CacheHashes();
    private void OnValidate() => CacheHashes();

    private void CacheHashes()
    {
        _speedHash = SafeHash(speedParam);
        _jumpHash = SafeHash(jumpTrigger);
        _lightHash = SafeHash(lightAttackTrigger);
        _heavyHash = SafeHash(heavyAttackTrigger);
        _hitHash = SafeHash(hitReactTrigger);
        _pickUpHash = SafeHash(pickUpTrigger);
    }

    private int SafeHash(string paramName)
    {
        if (string.IsNullOrWhiteSpace(paramName)) return 0;
        return Animator.StringToHash(paramName);
    }

    private bool HasParam(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null || hash == 0) return false;

        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].nameHash == hash && ps[i].type == type) return true;
        }
        return false;
    }

    private void SetFloatIfExists(int hash, float value)
    {
        if (animator == null) return;
        if (!HasParam(hash, AnimatorControllerParameterType.Float)) return;
        animator.SetFloat(hash, value);
    }

    private void SetTriggerIfExists(int hash)
    {
        if (animator == null) return;
        if (!HasParam(hash, AnimatorControllerParameterType.Trigger)) return;
        animator.SetTrigger(hash);
    }

    public void SetMoveSpeed(float currentSpeed)
    {
        if (animator == null) return;

        float v = (maxMoveSpeed <= 0f) ? 0f : Mathf.Clamp01(currentSpeed / maxMoveSpeed);

        if (IsOwner) SetFloatIfExists(_speedHash, v);

        if (IsServer)
        {
            SetFloatIfExists(_speedHash, v);
        }
        else if (IsOwner)
        {
            if (Time.time >= _nextSpeedSendTime || Mathf.Abs(v - _lastSentSpeed) >= 0.02f)
            {
                _lastSentSpeed = v;
                _nextSpeedSendTime = Time.time + 0.10f;
                SetMoveSpeedServerRpc(v);
            }
        }
    }

    public void PlayJump()
    {
        if (animator == null) return;

        if (IsOwner) SetTriggerIfExists(_jumpHash);

        if (IsServer)
        {
            SetTriggerIfExists(_jumpHash);
        }
        else if (IsOwner)
        {
            PlayJumpServerRpc();
        }
    }

    // 좌클릭 메인 공격: Heavy 우선, 없으면 Light
    public void PlayPrimaryAttack()
    {
        if (animator == null) return;

        if (IsOwner)
        {
            if (HasParam(_heavyHash, AnimatorControllerParameterType.Trigger)) SetTriggerIfExists(_heavyHash);
            else SetTriggerIfExists(_lightHash);
        }

        if (IsServer)
        {
            if (HasParam(_heavyHash, AnimatorControllerParameterType.Trigger)) SetTriggerIfExists(_heavyHash);
            else SetTriggerIfExists(_lightHash);
        }
        else if (IsOwner)
        {
            PlayPrimaryAttackServerRpc();
        }
    }

    public void PlayLightAttack()
    {
        if (animator == null) return;

        if (IsOwner) SetTriggerIfExists(_lightHash);

        if (IsServer) SetTriggerIfExists(_lightHash);
        else if (IsOwner) PlayLightAttackServerRpc();
    }

    public void PlayHeavyAttack()
    {
        if (animator == null) return;

        if (IsOwner) SetTriggerIfExists(_heavyHash);

        if (IsServer) SetTriggerIfExists(_heavyHash);
        else if (IsOwner) PlayHeavyAttackServerRpc();
    }

    public void PlayPickUp()
    {
        if (animator == null) return;

        if (IsOwner) SetTriggerIfExists(_pickUpHash);

        if (IsServer) SetTriggerIfExists(_pickUpHash);
        else if (IsOwner) PlayPickUpServerRpc();
    }

    public void ServerPlayHitReact()
    {
        if (!IsServer) return;
        SetTriggerIfExists(_hitHash);
    }

    [ServerRpc] private void SetMoveSpeedServerRpc(float normalized01) => SetFloatIfExists(_speedHash, normalized01);
    [ServerRpc] private void PlayJumpServerRpc() => SetTriggerIfExists(_jumpHash);

    [ServerRpc]
    private void PlayPrimaryAttackServerRpc()
    {
        if (HasParam(_heavyHash, AnimatorControllerParameterType.Trigger)) SetTriggerIfExists(_heavyHash);
        else SetTriggerIfExists(_lightHash);
    }

    [ServerRpc] private void PlayLightAttackServerRpc() => SetTriggerIfExists(_lightHash);
    [ServerRpc] private void PlayHeavyAttackServerRpc() => SetTriggerIfExists(_heavyHash);
    [ServerRpc] private void PlayPickUpServerRpc() => SetTriggerIfExists(_pickUpHash);
}
