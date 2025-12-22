using Unity.Netcode;
using UnityEngine;

public sealed class PlayerAnimModule
{
    private readonly PlayerHub _hub;

    private float _lastSentSpeed;
    private float _nextSpeedSendTime;

    public PlayerAnimModule(PlayerHub hub)
    {
        _hub = hub;
    }

    public void SetMoveSpeed(float currentSpeed)
    {
        if (_hub.Anim == null) return;

        float v = (_hub.MaxMoveSpeed <= 0f) ? 0f : Mathf.Clamp01(currentSpeed / _hub.MaxMoveSpeed);

        if (_hub.IsOwner)
            _hub.Anim.SetFloat(_hub.SpeedHash, v);

        if (_hub.IsServer)
        {
            _hub.Anim.SetFloat(_hub.SpeedHash, v);
        }
        else if (_hub.IsOwner)
        {
            if (Time.time >= _nextSpeedSendTime || Mathf.Abs(v - _lastSentSpeed) >= 0.02f)
            {
                _lastSentSpeed = v;
                _nextSpeedSendTime = Time.time + 0.10f;
                SetMoveSpeedRpc(v);
            }
        }
    }

    public void PlayJump()
    {
        if (_hub.Anim == null) return;

        if (_hub.IsOwner)
            _hub.Anim.SetTrigger(_hub.JumpHash);

        if (_hub.IsServer)
        {
            _hub.Anim.SetTrigger(_hub.JumpHash);
        }
        else if (_hub.IsOwner)
        {
            PlayJumpRpc();
        }
    }

    public void PlayPrimaryAttack(bool heavy)
    {
        if (_hub.Anim == null) return;

        int hash = heavy ? _hub.HeavyHash : _hub.LightHash;

        if (_hub.IsOwner)
            _hub.Anim.SetTrigger(hash);

        if (_hub.IsServer)
        {
            _hub.Anim.SetTrigger(hash);
        }
        else if (_hub.IsOwner)
        {
            PlayAttackRpc(heavy);
        }
    }

    public void PlayPickUp()
    {
        if (_hub.Anim == null) return;

        if (_hub.IsOwner)
            _hub.Anim.SetTrigger(_hub.PickUpHash);

        if (_hub.IsServer)
        {
            _hub.Anim.SetTrigger(_hub.PickUpHash);
        }
        else if (_hub.IsOwner)
        {
            PlayPickUpRpc();
        }
    }

    public void ServerPlayHitReact()
    {
        if (!_hub.IsServer) return;
        if (_hub.Anim == null) return;
        _hub.Anim.SetTrigger(_hub.HitHash);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SetMoveSpeedRpc(float normalized01)
    {
        if (_hub.Anim == null) return;
        _hub.Anim.SetFloat(_hub.SpeedHash, normalized01);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayJumpRpc()
    {
        if (_hub.Anim == null) return;
        _hub.Anim.SetTrigger(_hub.JumpHash);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayAttackRpc(bool heavy)
    {
        if (_hub.Anim == null) return;
        _hub.Anim.SetTrigger(heavy ? _hub.HeavyHash : _hub.LightHash);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayPickUpRpc()
    {
        if (_hub.Anim == null) return;
        _hub.Anim.SetTrigger(_hub.PickUpHash);
    }
}
