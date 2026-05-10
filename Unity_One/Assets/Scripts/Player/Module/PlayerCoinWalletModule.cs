using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerCoinWalletModule : NetworkBehaviour
{
    [Header("Coin Wallet")]
    [SerializeField, Tooltip("플레이어가 시작할 때 보유하는 코인 수입니다.")]
    private int startingCoins = 5;

    [SerializeField, Tooltip("플레이어가 최대로 보유할 수 있는 코인 수입니다.")]
    private int maxCoins = 999;

    [SerializeField, Tooltip("낙사 시 현재 보유 코인에서 드랍할 비율입니다.")]
    private float fallDropRatio = 0.3f;

    [SerializeField, Tooltip("낙사 시 최소로 드랍할 코인 수입니다.")]
    private int fallDropMinimum = 1;

    [SerializeField, Tooltip("낙사 시 최대로 드랍할 코인 수입니다.")]
    private int fallDropMaximum = 8;

    [SerializeField, Tooltip("코인이 0개가 되었을 때 탈락 대상으로 볼지 여부입니다.")]
    private bool eliminateWhenEmpty = true;

    private readonly NetworkVariable<int> _currentCoins =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    private bool _hasInitializedCoins;

    public int CurrentCoins => _currentCoins.Value;
    public bool IsEmpty => CurrentCoins <= 0;

    public event Action<int, int> CoinsChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _currentCoins.OnValueChanged += OnCoinsValueChanged;

        if (IsServer && !_hasInitializedCoins)
        {
            ServerResetCoins();
            _hasInitializedCoins = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentCoins.OnValueChanged -= OnCoinsValueChanged;

        base.OnNetworkDespawn();
    }

    public void ServerResetCoins()
    {
        ServerResetCoins(startingCoins);
    }

    public void ServerResetCoins(int amount)
    {
        if (!IsServer) return;

        _currentCoins.Value = ClampCoins(amount);
    }

    public bool ServerTryAddCoins(int amount, out int addedAmount)
    {
        addedAmount = 0;

        if (!IsServer) return false;
        if (amount <= 0) return false;

        int previousCoins = CurrentCoins;
        int nextCoins = ClampCoins((long)previousCoins + amount);
        addedAmount = nextCoins - previousCoins;

        if (addedAmount <= 0) return false;

        _currentCoins.Value = nextCoins;
        return true;
    }

    public int ServerRemoveCoins(int amount)
    {
        if (!IsServer) return 0;
        if (amount <= 0) return 0;

        int previousCoins = CurrentCoins;
        int nextCoins = ClampCoins(previousCoins - amount);
        int removedAmount = previousCoins - nextCoins;

        if (removedAmount <= 0) return 0;

        _currentCoins.Value = nextCoins;
        return removedAmount;
    }

    public int ServerPreviewFallDropAmount()
    {
        int currentCoins = CurrentCoins;
        if (currentCoins <= 0) return 0;

        int minDrop = Mathf.Max(0, fallDropMinimum);
        int maxDrop = Mathf.Max(minDrop, fallDropMaximum);
        int dropAmount = Mathf.CeilToInt(currentCoins * Mathf.Max(0f, fallDropRatio));

        dropAmount = Mathf.Clamp(dropAmount, minDrop, maxDrop);
        return Mathf.Min(dropAmount, currentCoins);
    }

    public int ServerApplyFallPenalty(out bool shouldEliminate)
    {
        shouldEliminate = false;

        if (!IsServer) return 0;

        if (CurrentCoins <= 0)
        {
            shouldEliminate = true;
            return 0;
        }

        int removedAmount = ServerRemoveCoins(ServerPreviewFallDropAmount());
        shouldEliminate = eliminateWhenEmpty && CurrentCoins <= 0;
        return removedAmount;
    }

    private void OnCoinsValueChanged(int previousValue, int newValue)
    {
        CoinsChanged?.Invoke(previousValue, newValue);
    }

    private int ClampCoins(long amount)
    {
        int maximumCoins = Mathf.Max(0, maxCoins);
        if (amount <= 0) return 0;
        if (amount >= maximumCoins) return maximumCoins;

        return (int)amount;
    }
}
