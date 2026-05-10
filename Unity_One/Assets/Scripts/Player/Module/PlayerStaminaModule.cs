using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerStaminaModule : NetworkBehaviour
{
    [Header("Stamina")]
    [SerializeField, Tooltip("플레이어가 가질 수 있는 최대 스테미너입니다.")]
    private float maxStamina = 100f;

    [SerializeField, Tooltip("스폰 또는 라운드 시작 시 적용할 시작 스테미너입니다.")]
    private float startingStamina = 100f;

    [SerializeField, Tooltip("서버에서 초당 회복되는 스테미너 양입니다.")]
    private float regenerationPerSecond = 20f;

    [SerializeField, Tooltip("스테미너를 소비한 뒤 회복이 다시 시작되기까지의 대기 시간입니다.")]
    private float regenerationDelayAfterSpend = 0.5f;

    [SerializeField, Tooltip("서버에서 스테미너를 자동 회복할지 여부입니다.")]
    private bool regenerateAutomatically = true;

    [SerializeField, Tooltip("서버에서 스테미너 자동 회복을 갱신하는 간격입니다.")]
    private float serverRegenTickInterval = 0.1f;

    private readonly NetworkVariable<float> _currentStamina =
        new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<float> _maxStamina =
        new NetworkVariable<float>(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool _hasInitializedStamina;
    private bool _hasSubscribedToValueChanges;
    private bool _isRegenerationPaused;
    private float _nextRegenerationAllowedTime;
    private float _nextRegenerationTickTime;
    private float _lastRegenerationTickTime;

    public float CurrentStamina => _currentStamina.Value;
    public float MaxStamina => _maxStamina.Value;
    public float NormalizedStamina => MaxStamina > 0f ? Mathf.Clamp01(CurrentStamina / MaxStamina) : 0f;
    public bool IsEmpty => CurrentStamina <= 0f;
    public bool IsFull => CurrentStamina >= MaxStamina;

    public event Action<float, float> StaminaChanged;
    public event Action<float, float> MaxStaminaChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        SubscribeToValueChanges();

        if (IsServer && !_hasInitializedStamina)
        {
            ServerInitializeStamina();
            _hasInitializedStamina = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromValueChanges();

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        UpdateAutomaticRegeneration();
    }

    public void ServerResetStamina()
    {
        ServerResetStamina(startingStamina);
    }

    public void ServerResetStamina(float amount)
    {
        if (!IsServer)
        {
            return;
        }

        _currentStamina.Value = ClampStamina(amount);
        _nextRegenerationAllowedTime = Time.time;
        ResetRegenerationTickClock();
    }

    public bool ServerCanSpendStamina(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        if (!IsServer || !IsFiniteFloat(amount))
        {
            return false;
        }

        return CurrentStamina >= amount;
    }

    public bool ServerTrySpendStamina(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        if (!IsServer || !IsFiniteFloat(amount))
        {
            return false;
        }

        if (!ServerCanSpendStamina(amount))
        {
            return false;
        }

        _currentStamina.Value = ClampStamina(CurrentStamina - amount);
        _nextRegenerationAllowedTime = Time.time + Mathf.Max(0f, regenerationDelayAfterSpend);
        ResetRegenerationTickClock();
        return true;
    }

    public float ServerRestoreStamina(float amount)
    {
        if (!IsServer || amount <= 0f || !IsFiniteFloat(amount))
        {
            return 0f;
        }

        float previousStamina = CurrentStamina;
        float nextStamina = ClampStamina(previousStamina + amount);
        float restoredAmount = nextStamina - previousStamina;

        if (restoredAmount <= 0f)
        {
            return 0f;
        }

        _currentStamina.Value = nextStamina;
        return restoredAmount;
    }

    public void ServerSetMaxStamina(float value, bool clampCurrentToMax = true)
    {
        if (!IsServer)
        {
            return;
        }

        float nextMaxStamina = SanitizeMaxStamina(value);
        _maxStamina.Value = nextMaxStamina;

        if (clampCurrentToMax)
        {
            _currentStamina.Value = ClampStamina(CurrentStamina);
        }
    }

    public void ServerSetAutomaticRegeneration(bool enabled)
    {
        if (!IsServer)
        {
            return;
        }

        regenerateAutomatically = enabled;
        ResetRegenerationTickClock();
    }

    public void ServerSetRegenerationPaused(bool paused)
    {
        if (!IsServer)
        {
            return;
        }

        _isRegenerationPaused = paused;
        ResetRegenerationTickClock();
    }

    private void ServerInitializeStamina()
    {
        _maxStamina.Value = SanitizeMaxStamina(maxStamina);
        ServerResetStamina(startingStamina);
    }

    private void UpdateAutomaticRegeneration()
    {
        if (!regenerateAutomatically || _isRegenerationPaused)
        {
            ResetRegenerationTickClock();
            return;
        }

        if (CurrentStamina >= MaxStamina)
        {
            ResetRegenerationTickClock();
            return;
        }

        float now = Time.time;
        if (now < _nextRegenerationAllowedTime)
        {
            ResetRegenerationTickClock();
            return;
        }

        float tickInterval = Mathf.Max(0.02f, serverRegenTickInterval);
        if (_nextRegenerationTickTime <= 0f)
        {
            _lastRegenerationTickTime = now;
            _nextRegenerationTickTime = now + tickInterval;
            return;
        }

        if (now < _nextRegenerationTickTime)
        {
            return;
        }

        float tickDelta = Mathf.Max(0f, now - _lastRegenerationTickTime);
        _lastRegenerationTickTime = now;
        _nextRegenerationTickTime = now + tickInterval;

        if (tickDelta <= 0f)
        {
            return;
        }

        ServerRestoreStamina(Mathf.Max(0f, regenerationPerSecond) * tickDelta);
    }

    private void SubscribeToValueChanges()
    {
        if (_hasSubscribedToValueChanges)
        {
            return;
        }

        _currentStamina.OnValueChanged += OnStaminaValueChanged;
        _maxStamina.OnValueChanged += OnMaxStaminaValueChanged;
        _hasSubscribedToValueChanges = true;
    }

    private void UnsubscribeFromValueChanges()
    {
        if (!_hasSubscribedToValueChanges)
        {
            return;
        }

        _currentStamina.OnValueChanged -= OnStaminaValueChanged;
        _maxStamina.OnValueChanged -= OnMaxStaminaValueChanged;
        _hasSubscribedToValueChanges = false;
    }

    private void OnStaminaValueChanged(float previousValue, float newValue)
    {
        StaminaChanged?.Invoke(previousValue, newValue);
    }

    private void OnMaxStaminaValueChanged(float previousValue, float newValue)
    {
        MaxStaminaChanged?.Invoke(previousValue, newValue);
    }

    private float ClampStamina(float value)
    {
        if (!IsFiniteFloat(value))
        {
            return 0f;
        }

        return Mathf.Clamp(value, 0f, MaxStamina);
    }

    private static float SanitizeMaxStamina(float value)
    {
        return IsFiniteFloat(value) ? Mathf.Max(1f, value) : 1f;
    }

    private static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void ResetRegenerationTickClock()
    {
        _nextRegenerationTickTime = 0f;
        _lastRegenerationTickTime = 0f;
    }

    private void OnValidate()
    {
        maxStamina = Mathf.Max(1f, maxStamina);
        startingStamina = Mathf.Clamp(startingStamina, 0f, maxStamina);
        regenerationPerSecond = Mathf.Max(0f, regenerationPerSecond);
        regenerationDelayAfterSpend = Mathf.Max(0f, regenerationDelayAfterSpend);
        serverRegenTickInterval = Mathf.Max(0.02f, serverRegenTickInterval);
    }
}
