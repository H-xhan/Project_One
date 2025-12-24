using UnityEngine;

public class PlayerAnimModule
{
    private readonly PlayerHub _hub;
    private readonly Animator _anim;

    private readonly int _speedHash = Animator.StringToHash("Speed");
    private readonly int _isSprintingHash = Animator.StringToHash("IsSprinting");
    private readonly int _jumpHash = Animator.StringToHash("Jump");
    private readonly int _attackLightHash = Animator.StringToHash("AttackLight");
    private readonly int _attackHeavyHash = Animator.StringToHash("AttackHeavy");
    private readonly int _pickUpHash = Animator.StringToHash("PickUp");

    private bool _hasSpeed;
    private bool _hasSprinting;
    private bool _hasJump;
    private bool _hasAttackLight;
    private bool _hasAttackHeavy;
    private bool _hasPickUp;

    private bool _missingWarned;

    public PlayerAnimModule(PlayerHub hub)
    {
        _hub = hub;
        _anim = hub.Animator;
        CacheParams();
    }

    private void CacheParams()
    {
        if (_anim == null) return;

        foreach (var p in _anim.parameters)
        {
            if (p.nameHash == _speedHash) _hasSpeed = true;
            else if (p.nameHash == _isSprintingHash) _hasSprinting = true;
            else if (p.nameHash == _jumpHash) _hasJump = true;
            else if (p.nameHash == _attackLightHash) _hasAttackLight = true;
            else if (p.nameHash == _attackHeavyHash) _hasAttackHeavy = true;
            else if (p.nameHash == _pickUpHash) _hasPickUp = true;
        }
    }

    private void WarnMissingOnce()
    {
        if (_missingWarned) return;
        _missingWarned = true;

        Debug.LogWarning("[Anim] Animator 파라미터가 일부 없습니다. " +
                         "필수: Speed(float), IsSprinting(bool), Jump(trigger), AttackLight(trigger), PickUp(trigger) " +
                         "옵션: AttackHeavy(trigger)");
    }

    public void SetLocomotion(float planarSpeed01, bool isSprinting)
    {
        if (_anim == null) return;

        if (!_hasSpeed || !_hasSprinting)
            WarnMissingOnce();

        if (_hasSpeed)
            _anim.SetFloat(_speedHash, planarSpeed01);

        if (_hasSprinting)
            _anim.SetBool(_isSprintingHash, isSprinting);
    }

    public void TriggerJump()
    {
        if (_anim == null) return;

        if (!_hasJump)
        {
            WarnMissingOnce();
            return;
        }

        _anim.SetTrigger(_jumpHash);
    }

    public void TriggerAttackLight()
    {
        if (_anim == null) return;

        if (!_hasAttackLight)
        {
            WarnMissingOnce();
            return;
        }

        _anim.SetTrigger(_attackLightHash);
    }

    public void TriggerAttackHeavy()
    {
        if (_anim == null) return;

        if (!_hasAttackHeavy)
        {
            WarnMissingOnce();
            return;
        }

        _anim.SetTrigger(_attackHeavyHash);
    }

    public void TriggerPickUp()
    {
        if (_anim == null) return;

        if (!_hasPickUp)
        {
            WarnMissingOnce();
            return;
        }

        _anim.SetTrigger(_pickUpHash);
    }
}
