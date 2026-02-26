using UnityEngine;
using UnityEngine.SceneManagement;

public class DontDestroyRoot : MonoBehaviour
{
    private static DontDestroyRoot _instance;

    [Header("UI 씬 커서 설정")]
    [Tooltip("이 씬들에서는 UI 클릭을 위해 커서를 항상 보이게/잠금 해제 상태로 유지합니다.")]
    [SerializeField] private string[] uiSceneNames = { "MainMenu", "RoomLobby" };

    [Tooltip("UI 씬에서 커서를 화면 안에 가두려면 Confined, 자유롭게 두려면 None 권장.")]
    [SerializeField] private bool confineCursorInUIScenes = true;

    [Header("게임 씬 커서 설정")]
    [Tooltip("UI 씬이 아닌 경우(인게임) 커서를 숨기고 잠그려면 체크합니다.")]
    [SerializeField] private bool lockCursorInGameScenes = true;

    private string _cachedSceneName = "";
    private bool _cachedIsUIScene;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ApplyCursorForScene(SceneManager.GetActiveScene().name, true);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        ApplyCursorForScene(newScene.name, true);
    }

    private void LateUpdate()
    {
        // 다른 스크립트(PlayerInput 등)가 Update에서 커서를 다시 잠가버리는 경우가 많아서
        // UI 씬에서는 LateUpdate에서 매 프레임 강제로 되돌립니다.
        string sceneName = SceneManager.GetActiveScene().name;
        ApplyCursorForScene(sceneName, false);
    }

    private void ApplyCursorForScene(string sceneName, bool force)
    {
        bool isUIScene = IsUIScene(sceneName);

        if (!force && sceneName == _cachedSceneName && isUIScene == _cachedIsUIScene)
        {
            // 씬/분류 변화 없으면 굳이 다시 세팅하지 않음
        }
        else
        {
            _cachedSceneName = sceneName;
            _cachedIsUIScene = isUIScene;
        }

        if (isUIScene)
        {
            Cursor.visible = true;
            Cursor.lockState = confineCursorInUIScenes ? CursorLockMode.Confined : CursorLockMode.None;
        }
        else
        {
            if (lockCursorInGameScenes)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }
    }

    private bool IsUIScene(string sceneName)
    {
        for (int i = 0; i < uiSceneNames.Length; i++)
        {
            if (uiSceneNames[i] == sceneName)
                return true;
        }
        return false;
    }
}
