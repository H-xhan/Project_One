using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterPortraitHUD : MonoBehaviour
{
    public enum PortraitExpression
    {
        Normal,
        LowStamina,
        Hit,
        Grabbed,
        Carrying,
        Danger,
        Success,
        Fail
    }

    [SerializeField, Tooltip("초상화를 표시할 UI Image입니다. 비워두면 자식에서 자동 탐색합니다.")]
    private Image portraitImage;

    [SerializeField, Tooltip("초상화 UI 전체 루트 오브젝트입니다. 비워두면 자기 gameObject를 사용합니다.")]
    private GameObject root;

    [SerializeField, Tooltip("기본 상태에서 표시할 초상화 Sprite입니다.")]
    private Sprite normalSprite;

    [SerializeField, Tooltip("스테미너가 낮을 때 표시할 초상화 Sprite입니다. 비워두면 기본 Sprite를 사용합니다.")]
    private Sprite lowStaminaSprite;

    [SerializeField, Tooltip("피격 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite hitSprite;

    [SerializeField, Tooltip("잡힌 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite grabbedSprite;

    [SerializeField, Tooltip("다른 캐릭터를 들고 있는 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite carryingSprite;

    [SerializeField, Tooltip("폭탄 등 위험 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite dangerSprite;

    [SerializeField, Tooltip("성공 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite successSprite;

    [SerializeField, Tooltip("실패 상태에서 표시할 확장용 초상화 Sprite입니다. 1차 MVP에서는 자동 적용하지 않습니다.")]
    private Sprite failSprite;

    [SerializeField, Tooltip("활성화 시 로컬 플레이어를 자동으로 찾아 바인딩할지 여부입니다.")]
    private bool autoBindLocalPlayer = true;

    [SerializeField, Tooltip("로컬 플레이어 또는 스테미너 모듈을 찾지 못했을 때 다시 탐색하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    [SerializeField, Tooltip("이 값 이하의 스테미너 비율 또는 수치에서 LowStamina 표정을 표시합니다. 1보다 작거나 같으면 정규화 비율로, 1보다 크면 실제 스테미너 수치로 판단합니다.")]
    private float lowStaminaThreshold = 25f;

    [SerializeField, Tooltip("수동 표정 적용 시 기본 유지 시간입니다.")]
    private float expressionHoldSeconds = 0.5f;

    [SerializeField, Tooltip("로컬 플레이어를 찾지 못했을 때 초상화 루트를 숨길지 여부입니다.")]
    private bool hideWhenNoLocalPlayer = true;

    [SerializeField, Tooltip("초상화 HUD 디버그 로그를 출력할지 여부입니다.")]
    private bool portraitDebugLogs = false;

    private PlayerHub _boundPlayerHub;
    private PlayerStaminaModule _boundStaminaModule;
    private PortraitExpression _currentExpression = PortraitExpression.Normal;
    private float _nextBindAttemptTime;
    private float _manualExpressionUntil;
    private bool _loggedWaitingForLocalPlayer;

    private void Awake()
    {
        ResolveRefs();
        ApplyExpressionSprite(PortraitExpression.Normal);
        ApplyRootVisibility(!hideWhenNoLocalPlayer);
    }

    private void OnEnable()
    {
        ResolveRefs();
        _nextBindAttemptTime = 0f;

        if (autoBindLocalPlayer)
        {
            TryBindLocalPlayer();
        }
        else
        {
            RefreshAutomaticExpression();
        }
    }

    private void OnDisable()
    {
        UnbindStaminaModule();
    }

    private void OnDestroy()
    {
        UnbindStaminaModule();
    }

    private void Update()
    {
        if (_manualExpressionUntil > 0f && Time.unscaledTime >= _manualExpressionUntil)
        {
            _manualExpressionUntil = 0f;
            RefreshAutomaticExpression();
        }

        if (!autoBindLocalPlayer || _boundStaminaModule != null)
            return;

        if (Time.unscaledTime < _nextBindAttemptTime)
            return;

        TryBindLocalPlayer();
    }

    public void ForceRebind()
    {
        UnbindStaminaModule();
        _boundPlayerHub = null;
        _loggedWaitingForLocalPlayer = false;
        _nextBindAttemptTime = 0f;
        TryBindLocalPlayer();
    }

    public void SetExpression(PortraitExpression expression, float holdSeconds = 0f)
    {
        float resolvedHoldSeconds = holdSeconds > 0f ? holdSeconds : expressionHoldSeconds;
        _manualExpressionUntil = resolvedHoldSeconds > 0f
            ? Time.unscaledTime + resolvedHoldSeconds
            : 0f;

        ApplyExpressionSprite(expression);
    }

    private void ResolveRefs()
    {
        if (root == null)
            root = gameObject;

        if (portraitImage == null)
            portraitImage = GetComponentInChildren<Image>(true);
    }

    private void TryBindLocalPlayer()
    {
        _nextBindAttemptTime = Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        PlayerHub playerHub = ResolveLocalPlayerHub();
        if (playerHub == null)
        {
            ShowMissingLocalPlayerState();
            LogWaitingForLocalPlayer();
            return;
        }

        PlayerStaminaModule staminaModule = playerHub.GetComponentInChildren<PlayerStaminaModule>(true);
        if (staminaModule == null)
        {
            _boundPlayerHub = playerHub;
            ShowMissingLocalPlayerState();
            Log("Rebind waiting for local player stamina.");
            return;
        }

        _boundPlayerHub = playerHub;
        BindStaminaModule(staminaModule);
        _loggedWaitingForLocalPlayer = false;
        Log("Local player bound.");
    }

    private PlayerHub ResolveLocalPlayerHub()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkObject playerObject = null;
        NetworkClient localClient = networkManager.LocalClient;
        if (localClient != null && localClient.PlayerObject != null)
        {
            playerObject = localClient.PlayerObject;
        }
        else if (networkManager.IsServer &&
                 networkManager.ConnectedClients != null &&
                 networkManager.ConnectedClients.TryGetValue(networkManager.LocalClientId, out NetworkClient connectedClient) &&
                 connectedClient != null)
        {
            playerObject = connectedClient.PlayerObject;
        }

        if (playerObject == null || !playerObject.IsSpawned)
            return null;

        if (playerObject.OwnerClientId != networkManager.LocalClientId)
            return null;

        PlayerHub playerHub = playerObject.GetComponentInChildren<PlayerHub>(true);
        if (!CanBindPlayerHub(playerHub))
            return null;

        return playerHub;
    }

    private void BindStaminaModule(PlayerStaminaModule staminaModule)
    {
        if (_boundStaminaModule == staminaModule)
        {
            ApplyRootVisibility(true);
            RefreshAutomaticExpression();
            return;
        }

        UnbindStaminaModule();

        _boundStaminaModule = staminaModule;
        _boundStaminaModule.StaminaChanged += OnStaminaChanged;
        _boundStaminaModule.MaxStaminaChanged += OnMaxStaminaChanged;

        ApplyRootVisibility(true);
        RefreshAutomaticExpression();
    }

    private void UnbindStaminaModule()
    {
        if (_boundStaminaModule != null)
        {
            _boundStaminaModule.StaminaChanged -= OnStaminaChanged;
            _boundStaminaModule.MaxStaminaChanged -= OnMaxStaminaChanged;
        }

        _boundStaminaModule = null;
    }

    private void OnStaminaChanged(float previousStamina, float currentStamina)
    {
        RefreshAutomaticExpression();
    }

    private void OnMaxStaminaChanged(float previousMaxStamina, float currentMaxStamina)
    {
        RefreshAutomaticExpression();
    }

    private void RefreshAutomaticExpression()
    {
        if (_manualExpressionUntil > 0f && Time.unscaledTime < _manualExpressionUntil)
            return;

        PortraitExpression nextExpression = ShouldUseLowStaminaExpression()
            ? PortraitExpression.LowStamina
            : PortraitExpression.Normal;

        ApplyExpressionSprite(nextExpression);
    }

    private bool ShouldUseLowStaminaExpression()
    {
        if (_boundStaminaModule == null)
            return false;

        float threshold = Mathf.Max(0f, lowStaminaThreshold);
        if (threshold <= 1f)
            return _boundStaminaModule.NormalizedStamina <= threshold;

        return _boundStaminaModule.CurrentStamina <= threshold;
    }

    private void ApplyExpressionSprite(PortraitExpression expression)
    {
        PortraitExpression previousExpression = _currentExpression;
        _currentExpression = expression;

        if (portraitImage == null)
            return;

        Sprite sprite = GetSpriteForExpression(expression);
        if (sprite == null)
            sprite = normalSprite;

        if (sprite != null)
            portraitImage.sprite = sprite;

        if (expression == PortraitExpression.LowStamina && previousExpression != expression)
            Log("Low stamina expression applied.");
    }

    private Sprite GetSpriteForExpression(PortraitExpression expression)
    {
        switch (expression)
        {
            case PortraitExpression.LowStamina:
                return lowStaminaSprite != null ? lowStaminaSprite : normalSprite;
            case PortraitExpression.Hit:
                return hitSprite != null ? hitSprite : normalSprite;
            case PortraitExpression.Grabbed:
                return grabbedSprite != null ? grabbedSprite : normalSprite;
            case PortraitExpression.Carrying:
                return carryingSprite != null ? carryingSprite : normalSprite;
            case PortraitExpression.Danger:
                return dangerSprite != null ? dangerSprite : normalSprite;
            case PortraitExpression.Success:
                return successSprite != null ? successSprite : normalSprite;
            case PortraitExpression.Fail:
                return failSprite != null ? failSprite : normalSprite;
            case PortraitExpression.Normal:
            default:
                return normalSprite;
        }
    }

    private void ShowMissingLocalPlayerState()
    {
        ApplyRootVisibility(!hideWhenNoLocalPlayer);
        ApplyExpressionSprite(PortraitExpression.Normal);
    }

    private void ApplyRootVisibility(bool visible)
    {
        if (root != null && root != gameObject && !transform.IsChildOf(root.transform))
        {
            root.SetActive(visible);
            return;
        }

        // Do not deactivate this component or its parents, or bind retries stop.
        if (portraitImage != null && portraitImage.gameObject != gameObject)
            portraitImage.gameObject.SetActive(visible);
    }

    private bool CanBindPlayerHub(PlayerHub playerHub)
    {
        if (playerHub == null)
            return false;

        if (playerHub.IsSpawned && !playerHub.IsOwner)
            return false;

        return true;
    }

    private void LogWaitingForLocalPlayer()
    {
        if (_loggedWaitingForLocalPlayer)
            return;

        _loggedWaitingForLocalPlayer = true;
        Log("Rebind waiting for local player.");
    }

    private void Log(string message)
    {
        if (!portraitDebugLogs)
            return;

        Debug.Log($"[CharacterPortraitHUD] {message}", this);
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0f, rebindInterval);
        lowStaminaThreshold = Mathf.Max(0f, lowStaminaThreshold);
        expressionHoldSeconds = Mathf.Max(0f, expressionHoldSeconds);
    }
}
