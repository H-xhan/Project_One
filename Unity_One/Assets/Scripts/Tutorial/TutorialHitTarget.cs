using System;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialHitTarget :
    MonoBehaviour,
    IDamageable,
    TutorialDirector.ITutorialHitSource
{
    private const int RequiredHitCount = 2;

    [SerializeField] private TutorialDirector director;

    private int _hitCount;
    private int _lastAcceptedFrame = -1;

    public int HitCount => _hitCount;

    public event Action<int> HitAccepted;

    public void TakeDamage(float damage)
    {
        if (!CanAcceptHit(damage))
            return;

        int currentFrame = Time.frameCount;
        if (_lastAcceptedFrame == currentFrame)
            return;

        _lastAcceptedFrame = currentFrame;
        _hitCount++;
        HitAccepted?.Invoke(_hitCount);
    }

    private bool CanAcceptHit(float damage)
    {
        if (!isActiveAndEnabled ||
            director == null ||
            !director.isActiveAndEnabled ||
            !director.IsRunning ||
            director.CurrentStep != TutorialDirector.TutorialStep.Attack ||
            _hitCount >= RequiredHitCount ||
            !IsFinitePositive(damage))
        {
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null &&
               networkManager.IsListening &&
               networkManager.IsServer;
    }

    private static bool IsFinitePositive(float value)
    {
        return value > 0f &&
               !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }
}
