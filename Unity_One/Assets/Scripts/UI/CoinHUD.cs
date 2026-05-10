using TMPro;
using Unity.Netcode;
using UnityEngine;

public class CoinHUD : MonoBehaviour
{
    [SerializeField, Tooltip("보유 코인 수를 표시할 TMP 텍스트입니다. 비워두면 자식에서 자동 탐색합니다.")]
    private TMP_Text coinText;

    [SerializeField, Tooltip("표시할 플레이어 허브입니다. 비워두면 로컬 Owner 플레이어를 자동 탐색합니다.")]
    private PlayerHub targetPlayerHub;

    [SerializeField, Tooltip("코인 수 앞에 표시할 접두어입니다.")]
    private string coinPrefix = "Coins: ";

    [SerializeField, Tooltip("로컬 플레이어 또는 코인 지갑을 아직 찾지 못했을 때 표시할 문구입니다.")]
    private string missingWalletText = "Coins: -";

    [SerializeField, Tooltip("로컬 플레이어 지갑을 찾지 못했을 때 다시 탐색하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    [SerializeField, Tooltip("코인 지갑을 찾지 못했을 때 HUD 오브젝트를 숨길지 여부입니다.")]
    private bool hideWhenWalletMissing = false;

    private PlayerCoinWalletModule _boundWallet;
    private float _nextBindAttemptTime;

    public PlayerCoinWalletModule BoundWallet => _boundWallet;

    private void Awake()
    {
        ResolveRefs();
    }

    private void OnEnable()
    {
        ResolveRefs();
        TryBindWallet();
    }

    private void OnDisable()
    {
        UnbindWallet();
    }

    private void Update()
    {
        if (_boundWallet != null)
            return;

        if (Time.unscaledTime < _nextBindAttemptTime)
            return;

        TryBindWallet();
    }

    public void ForceRebind()
    {
        UnbindWallet();
        _nextBindAttemptTime = 0f;
        TryBindWallet();
    }

    public void SetTargetPlayerHub(PlayerHub playerHub)
    {
        targetPlayerHub = playerHub;
        ForceRebind();
    }

    private void ResolveRefs()
    {
        if (coinText == null)
            coinText = GetComponentInChildren<TMP_Text>(true);
    }

    private void TryBindWallet()
    {
        _nextBindAttemptTime = Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        PlayerHub playerHub = ResolveTargetPlayerHub();
        if (playerHub == null)
        {
            ShowMissingWalletState();
            return;
        }

        PlayerCoinWalletModule wallet = playerHub.CoinWalletModule;
        if (wallet == null)
        {
            ShowMissingWalletState();
            return;
        }

        BindWallet(wallet);
    }

    private PlayerHub ResolveTargetPlayerHub()
    {
        if (targetPlayerHub != null)
            return CanBindPlayerHub(targetPlayerHub) ? targetPlayerHub : null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null)
            return null;

        PlayerHub playerHub = localClient.PlayerObject.GetComponentInChildren<PlayerHub>(true);
        if (!CanBindPlayerHub(playerHub))
            return null;

        return playerHub;
    }

    private void BindWallet(PlayerCoinWalletModule wallet)
    {
        if (_boundWallet == wallet)
        {
            RefreshCoinText(wallet.CurrentCoins);
            return;
        }

        UnbindWallet();

        _boundWallet = wallet;
        _boundWallet.CoinsChanged += OnCoinsChanged;
        RefreshCoinText(_boundWallet.CurrentCoins);
    }

    private void UnbindWallet()
    {
        if (_boundWallet != null)
            _boundWallet.CoinsChanged -= OnCoinsChanged;

        _boundWallet = null;
    }

    private void OnCoinsChanged(int previousCoins, int currentCoins)
    {
        RefreshCoinText(currentCoins);
    }

    private void RefreshCoinText(int coins)
    {
        if (coinText == null)
            return;

        if (!coinText.gameObject.activeSelf)
            coinText.gameObject.SetActive(true);

        coinText.text = $"{coinPrefix}{coins}";
    }

    private void ShowMissingWalletState()
    {
        if (coinText == null)
            return;

        coinText.text = missingWalletText;
        coinText.gameObject.SetActive(!hideWhenWalletMissing);
    }

    private bool CanBindPlayerHub(PlayerHub playerHub)
    {
        if (playerHub == null)
            return false;

        if (playerHub.IsSpawned && !playerHub.IsOwner)
            return false;

        return true;
    }
}
