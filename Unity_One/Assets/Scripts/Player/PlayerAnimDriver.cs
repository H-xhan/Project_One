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

    [Header("Legacy Fallback")]
    [Tooltip("기존에 쓰던 약공격 트리거가 있다면 입력 (예: HitSweep). 없으면 비워도 됨")]
    [SerializeField] private string legacyLightAttackTrigger = "HitSweep";

    private int _speedHash;
    private int _jumpHash;
    private int _lightHash;
    private int _heavyHash;
    private int _hitHash;
    private int _legacyLightHash;

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
        _legacyLightHash = SafeHash(legacyLightAttackTrigger);
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

    private void SetTriggerIfExists(params int[] candidates)
    {
        if (animator == null) return;

        for (int i = 0; i < candidates.Length; i++)
        {
            int h = candidates[i];
            if (h == 0) continue;
            if (!HasParam(h, AnimatorControllerParameterType.Trigger)) continue;

            animator.SetTrigger(h);
            return;
        }
    }

    // --------------------
    // Public API
    // --------------------

    public void SetMoveSpeed(float currentSpeed)
    {
        if (animator == null) return;

        float v = (maxMoveSpeed <= 0f) ? 0f : Mathf.Clamp01(currentSpeed / maxMoveSpeed);

        // 로컬 즉시 반응
        if (IsOwner) SetFloatIfExists(_speedHash, v);

        // 서버에도 전달해서 NetworkAnimator가 모두에게 전파되게 함
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

    public void PlayLightAttack()
    {
        if (animator == null) return;

        // AttackLight가 없으면 legacy(HitSweep)로라도 동작
        if (IsOwner) SetTriggerIfExists(_lightHash, _legacyLightHash);

        if (IsServer)
        {
            SetTriggerIfExists(_lightHash, _legacyLightHash);
        }
        else if (IsOwner)
        {
            PlayLightAttackServerRpc();
        }
    }

    public void PlayHeavyAttack()
    {
        if (animator == null) return;

        if (IsOwner) SetTriggerIfExists(_heavyHash);

        if (IsServer)
        {
            SetTriggerIfExists(_heavyHash);
        }
        else if (IsOwner)
        {
            PlayHeavyAttackServerRpc();
        }
    }

    // 서버에서 “맞은 대상”에게 피격 트리거를 쏘는 용도
    public void ServerPlayHitReact()
    {
        if (!IsServer) return;
        SetTriggerIfExists(_hitHash);
    }

    // --------------------
    // RPC
    // --------------------

    [ServerRpc]
    private void SetMoveSpeedServerRpc(float normalized01)
    {
        SetFloatIfExists(_speedHash, normalized01);
    }

    [ServerRpc]
    private void PlayJumpServerRpc()
    {
        SetTriggerIfExists(_jumpHash);
    }

    [ServerRpc]
    private void PlayLightAttackServerRpc()
    {
        SetTriggerIfExists(_lightHash, _legacyLightHash);
    }

    [ServerRpc]
    private void PlayHeavyAttackServerRpc()
    {
        SetTriggerIfExists(_heavyHash);
    }
}
