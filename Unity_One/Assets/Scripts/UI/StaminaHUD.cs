using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class StaminaHUD : MonoBehaviour
{
    [SerializeField, Tooltip("스테미너 게이지의 채워지는 영역 Image입니다. Image Type은 Filled, Fill Method는 Horizontal을 권장합니다.")]
    private Image staminaFillImage = null;

    [SerializeField, Tooltip("스테미너 수치를 표시할 TMP 텍스트입니다. 비워두면 자식에서 자동 탐색합니다.")]
    private TMP_Text staminaText;

    [SerializeField, Tooltip("표시할 플레이어 허브입니다. 비워두면 로컬 Owner 플레이어를 자동 탐색합니다.")]
    private PlayerHub targetPlayerHub;

    [SerializeField, Tooltip("스테미너 수치 앞에 표시할 접두어입니다.")]
    private string staminaPrefix = "스테미너 ";

    [SerializeField, Tooltip("로컬 플레이어 또는 스테미너 모듈을 아직 찾지 못했을 때 표시할 문구입니다.")]
    private string missingStaminaText = "스테미너 -";

    [SerializeField, Tooltip("스테미너 수치를 정수로 표시할지 여부입니다.")]
    private bool showAsInteger = true;

    [SerializeField, Tooltip("현재 스테미너와 함께 최대 스테미너를 표시할지 여부입니다.")]
    private bool showMaxStamina = true;

    [SerializeField, Tooltip("로컬 플레이어 스테미너 모듈을 찾지 못했을 때 다시 탐색하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    [SerializeField, Tooltip("스테미너 모듈을 찾지 못했을 때 스테미너 UI 요소를 숨길지 여부입니다.")]
    private bool hideWhenStaminaMissing = false;

    [SerializeField, Tooltip("스테미너 게이지가 목표 값으로 부드럽게 변화할지 여부입니다.")]
    private bool smoothFill = true;

    [SerializeField, Tooltip("스테미너 게이지가 목표 값으로 따라가는 속도입니다.")]
    private float fillSmoothSpeed = 12f;

    private PlayerStaminaModule _boundStaminaModule;
    private float _nextBindAttemptTime;
    private float _targetFillAmount = 1f;

    public PlayerStaminaModule BoundStaminaModule => _boundStaminaModule;

    private void Awake()
    {
        ResolveRefs();
    }

    private void OnEnable()
    {
        ResolveRefs();
        TryBindStaminaModule();
    }

    private void OnDisable()
    {
        UnbindStaminaModule();
    }

    private void Update()
    {
        if (_boundStaminaModule == null)
        {
            if (Time.unscaledTime >= _nextBindAttemptTime)
            {
                TryBindStaminaModule();
            }

            return;
        }

        if (smoothFill && staminaFillImage != null)
        {
            UpdateSmoothFill();
        }
    }

    public void ForceRebind()
    {
        UnbindStaminaModule();
        _nextBindAttemptTime = 0f;
        TryBindStaminaModule();
    }

    public void SetTargetPlayerHub(PlayerHub playerHub)
    {
        targetPlayerHub = playerHub;
        ForceRebind();
    }

    public void ForceRefresh()
    {
        RefreshStaminaUI();
    }

    private void ResolveRefs()
    {
        if (staminaText == null)
        {
            staminaText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void TryBindStaminaModule()
    {
        _nextBindAttemptTime = Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        PlayerHub playerHub = ResolveTargetPlayerHub();
        if (playerHub == null)
        {
            ShowMissingStaminaState();
            return;
        }

        PlayerStaminaModule staminaModule = playerHub.StaminaModule;
        if (staminaModule == null)
        {
            ShowMissingStaminaState();
            return;
        }

        BindStaminaModule(staminaModule);
    }

    private PlayerHub ResolveTargetPlayerHub()
    {
        if (targetPlayerHub != null)
        {
            return CanBindPlayerHub(targetPlayerHub) ? targetPlayerHub : null;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return null;
        }

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null)
        {
            return null;
        }

        PlayerHub playerHub = localClient.PlayerObject.GetComponentInChildren<PlayerHub>(true);
        if (!CanBindPlayerHub(playerHub))
        {
            return null;
        }

        return playerHub;
    }

    private void BindStaminaModule(PlayerStaminaModule staminaModule)
    {
        if (_boundStaminaModule == staminaModule)
        {
            RefreshStaminaUI();
            return;
        }

        UnbindStaminaModule();

        _boundStaminaModule = staminaModule;
        _boundStaminaModule.StaminaChanged += OnStaminaChanged;
        _boundStaminaModule.MaxStaminaChanged += OnMaxStaminaChanged;
        RefreshStaminaUI();
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
        if (_boundStaminaModule == null)
        {
            ShowMissingStaminaState();
            return;
        }

        RefreshStaminaUI(currentStamina, _boundStaminaModule.MaxStamina);
    }

    private void OnMaxStaminaChanged(float previousMaxStamina, float currentMaxStamina)
    {
        if (_boundStaminaModule == null)
        {
            ShowMissingStaminaState();
            return;
        }

        RefreshStaminaUI(_boundStaminaModule.CurrentStamina, currentMaxStamina);
    }

    private void RefreshStaminaUI()
    {
        if (_boundStaminaModule == null)
        {
            ShowMissingStaminaState();
            return;
        }

        SetUIElementsVisible(true);
        RefreshStaminaText(_boundStaminaModule.CurrentStamina, _boundStaminaModule.MaxStamina);
        RefreshStaminaFill(_boundStaminaModule.NormalizedStamina);
        ApplyTargetFillImmediately();
    }

    private void RefreshStaminaUI(float currentStamina, float maxStamina)
    {
        SetUIElementsVisible(true);
        RefreshStaminaText(currentStamina, maxStamina);

        float normalized = maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
        RefreshStaminaFill(normalized);
    }

    private void RefreshStaminaText(float currentStamina, float maxStamina)
    {
        if (staminaText == null)
        {
            return;
        }

        string prefix = staminaPrefix ?? string.Empty;
        if (showAsInteger)
        {
            int current = Mathf.RoundToInt(currentStamina);
            if (showMaxStamina)
            {
                staminaText.text = $"{prefix}{current} / {Mathf.RoundToInt(maxStamina)}";
                return;
            }

            staminaText.text = $"{prefix}{current}";
            return;
        }

        if (showMaxStamina)
        {
            staminaText.text = $"{prefix}{currentStamina:0.0} / {maxStamina:0.0}";
            return;
        }

        staminaText.text = $"{prefix}{currentStamina:0.0}";
    }

    private void RefreshStaminaFill(float normalized)
    {
        _targetFillAmount = Mathf.Clamp01(normalized);

        if (staminaFillImage == null)
        {
            return;
        }

        if (!smoothFill)
        {
            staminaFillImage.fillAmount = _targetFillAmount;
        }
    }

    private void ShowMissingStaminaState()
    {
        _targetFillAmount = 0f;
        SetUIElementsVisible(!hideWhenStaminaMissing);
        ApplyTargetFillImmediately();

        if (staminaText != null)
        {
            staminaText.text = missingStaminaText;
        }
    }

    private void SetUIElementsVisible(bool visible)
    {
        if (staminaFillImage != null && staminaFillImage.gameObject != gameObject)
        {
            staminaFillImage.gameObject.SetActive(visible);
        }

        if (staminaText != null && staminaText.gameObject != gameObject)
        {
            staminaText.gameObject.SetActive(visible);
        }
    }

    private bool CanBindPlayerHub(PlayerHub playerHub)
    {
        if (playerHub == null)
        {
            return false;
        }

        if (playerHub.IsSpawned && !playerHub.IsOwner)
        {
            return false;
        }

        return true;
    }

    private void UpdateSmoothFill()
    {
        float speed = Mathf.Max(0f, fillSmoothSpeed);
        if (speed <= 0f)
        {
            ApplyTargetFillImmediately();
            return;
        }

        float blend = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        staminaFillImage.fillAmount = Mathf.Lerp(staminaFillImage.fillAmount, _targetFillAmount, blend);
    }

    private void ApplyTargetFillImmediately()
    {
        if (staminaFillImage != null)
        {
            staminaFillImage.fillAmount = _targetFillAmount;
        }
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0f, rebindInterval);
        fillSmoothSpeed = Mathf.Max(0f, fillSmoothSpeed);
    }
}
