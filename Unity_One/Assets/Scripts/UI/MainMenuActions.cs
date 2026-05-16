using UnityEngine;
using UnityEngine.UI;

public class MainMenuActions : MonoBehaviour
{
    private const string DefaultMasterVolumePrefsKey = "ProjectOne.MasterVolume";

    [SerializeField, Tooltip("설정 버튼을 눌렀을 때 표시할 설정 패널입니다.")]
    private GameObject settingsPanel;

    [SerializeField, Tooltip("설정 패널 표시/숨김에 사용할 CanvasGroup입니다. 비워두면 settingsPanel에서 자동 탐색하거나 추가합니다.")]
    private CanvasGroup settingsCanvasGroup;

    [SerializeField, Tooltip("전체 게임 볼륨을 조절할 Slider입니다.")]
    private Slider masterVolumeSlider;

    [SerializeField, Tooltip("설정 패널을 여는 버튼입니다. 비워두면 수동 OnClick 연결을 사용할 수 있습니다.")]
    private Button settingsButton;

    [SerializeField, Tooltip("설정 패널을 닫는 버튼입니다. 비워두면 수동 OnClick 연결을 사용할 수 있습니다.")]
    private Button closeSettingsButton;

    [SerializeField, Tooltip("게임을 종료하는 버튼입니다. 비워두면 수동 OnClick 연결을 사용할 수 있습니다.")]
    private Button quitButton;

    [SerializeField, Tooltip("시작할 때 설정 패널을 숨길지 여부입니다.")]
    private bool hideSettingsOnStart = true;

    [SerializeField, Tooltip("설정 패널을 비활성화하지 않고 CanvasGroup으로 숨길지 여부입니다.")]
    private bool useCanvasGroupForSettings = true;

    [SerializeField, Tooltip("저장된 볼륨 값이 없을 때 사용할 기본 마스터 볼륨입니다.")]
    private float defaultMasterVolume = 1f;

    [SerializeField, Tooltip("마스터 볼륨 값을 PlayerPrefs에 저장할지 여부입니다.")]
    private bool saveVolumeToPlayerPrefs = true;

    [SerializeField, Tooltip("마스터 볼륨을 저장할 PlayerPrefs 키입니다.")]
    private string masterVolumePrefsKey = DefaultMasterVolumePrefsKey;

    private bool _listenersRegistered;
    private bool _settingsVisible;
    private float _currentMasterVolume;

    private void Awake()
    {
        ResolveSettingsReferences();
        _currentMasterVolume = LoadMasterVolume();
        ApplyMasterVolume(_currentMasterVolume, false);
    }

    private void OnEnable()
    {
        ResolveSettingsReferences();
        RegisterListeners();
    }

    private void Start()
    {
        if (hideSettingsOnStart)
        {
            HideSettings();
        }
        else
        {
            ApplySettingsVisibility(ReadSettingsVisibility());
        }
    }

    private void OnDisable()
    {
        UnregisterListeners();
    }

    public void ShowSettings()
    {
        ApplySettingsVisibility(true);
    }

    public void HideSettings()
    {
        ApplySettingsVisibility(false);
    }

    public void ToggleSettings()
    {
        ApplySettingsVisibility(!_settingsVisible);
    }

    public void SetMasterVolume(float value)
    {
        ApplyMasterVolume(value, saveVolumeToPlayerPrefs);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    private void RegisterListeners()
    {
        if (_listenersRegistered)
        {
            return;
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(ShowSettings);
        }

        if (closeSettingsButton != null)
        {
            closeSettingsButton.onClick.AddListener(HideSettings);
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(QuitGame);
        }

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(_currentMasterVolume);
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);
        }

        _listenersRegistered = true;
    }

    private void UnregisterListeners()
    {
        if (!_listenersRegistered)
        {
            return;
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(ShowSettings);
        }

        if (closeSettingsButton != null)
        {
            closeSettingsButton.onClick.RemoveListener(HideSettings);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
        }

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.RemoveListener(SetMasterVolume);
        }

        _listenersRegistered = false;
    }

    private void ApplySettingsVisibility(bool visible)
    {
        _settingsVisible = visible;

        if (settingsPanel == null)
        {
            return;
        }

        if (useCanvasGroupForSettings && settingsCanvasGroup != null)
        {
            settingsPanel.SetActive(true);
            settingsCanvasGroup.alpha = visible ? 1f : 0f;
            settingsCanvasGroup.interactable = visible;
            settingsCanvasGroup.blocksRaycasts = visible;
            return;
        }

        settingsPanel.SetActive(visible);
    }

    private float LoadMasterVolume()
    {
        float fallbackVolume = Mathf.Clamp01(defaultMasterVolume);
        if (!saveVolumeToPlayerPrefs || string.IsNullOrEmpty(masterVolumePrefsKey))
        {
            return fallbackVolume;
        }

        return Mathf.Clamp01(PlayerPrefs.GetFloat(masterVolumePrefsKey, fallbackVolume));
    }

    private void SaveMasterVolume(float value)
    {
        if (!saveVolumeToPlayerPrefs || string.IsNullOrEmpty(masterVolumePrefsKey))
        {
            return;
        }

        PlayerPrefs.SetFloat(masterVolumePrefsKey, Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    private void ResolveSettingsReferences()
    {
        if (settingsPanel == null)
        {
            return;
        }

        if (settingsCanvasGroup == null)
        {
            settingsCanvasGroup = settingsPanel.GetComponent<CanvasGroup>();
        }

        if (useCanvasGroupForSettings && settingsCanvasGroup == null)
        {
            settingsCanvasGroup = settingsPanel.AddComponent<CanvasGroup>();
        }
    }

    private void ApplyMasterVolume(float value, bool save)
    {
        _currentMasterVolume = Mathf.Clamp01(value);
        AudioListener.volume = _currentMasterVolume;

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(_currentMasterVolume);
        }

        if (save)
        {
            SaveMasterVolume(_currentMasterVolume);
        }
    }

    private bool ReadSettingsVisibility()
    {
        if (settingsPanel == null)
        {
            return false;
        }

        if (useCanvasGroupForSettings && settingsCanvasGroup != null)
        {
            return settingsCanvasGroup.alpha > 0f;
        }

        return settingsPanel.activeSelf;
    }

    private void OnValidate()
    {
        defaultMasterVolume = Mathf.Clamp01(defaultMasterVolume);

        if (string.IsNullOrEmpty(masterVolumePrefsKey))
        {
            masterVolumePrefsKey = DefaultMasterVolumePrefsKey;
        }
    }
}
