using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerInputModule : MonoBehaviour
{
    [Header("Cursor")]
    [Tooltip("시작 시 커서를 잠글지 여부 (인게임 기본은 true 권장)")]
    [SerializeField] private bool defaultCursorLocked = true;

    [Tooltip("커서 잠금 토글 허용 여부 (로비에서 UI 클릭 필요하면 true 권장)")]
    [SerializeField] private bool allowCursorToggle = true;

    [Tooltip("커서 잠금 토글 키")]
    [SerializeField] private Key toggleCursorKey = Key.Escape;

    [Tooltip("해당 씬에서는 시작 시 커서를 강제로 풀어둠 (예: RoomLobby, MainMenu)")]
    [SerializeField] private string[] forceCursorUnlockedScenes = new[] { "RoomLobby", "MainMenu" };

    [Header("Mouse")]
    [Tooltip("마우스 X 감도")]
    [SerializeField] private float mouseSensitivityX = 2.0f;

    [Tooltip("마우스 Y 감도")]
    [SerializeField] private float mouseSensitivityY = 2.0f;

    [Header("Keys")]
    [Tooltip("달리기 키")]
    [SerializeField] private Key sprintKey = Key.LeftShift;

    [Tooltip("점프 키")]
    [SerializeField] private Key jumpKey = Key.Space;

    [Tooltip("공격 키")]
    [SerializeField] private int attackMouseButton = 0;

    [Tooltip("상호작용/픽업 키")]
    [SerializeField] private int interactMouseButton = 1;

    [Tooltip("드랍 키")]
    [SerializeField] private Key dropKey = Key.G;

    private NetworkObject _netObj;
    private bool _cursorLocked;
    private bool _cursorInitDone;

    public bool IsCursorLocked => _cursorLocked;

    private void Awake()
    {
        _netObj = GetComponent<NetworkObject>();
        // 멀티에서 "상대 플레이어"도 내 클라이언트에 스폰되므로
        // Awake에서 커서 잠그는 행동은 절대 하면 안 됨.
        // (오너 판별된 뒤에만 처리)
    }

    private void Update()
    {
        if (!IsLocalOwner())
            return;

        if (!_cursorInitDone)
            InitCursorStateForScene();

        if (allowCursorToggle && Keyboard.current != null && Keyboard.current[toggleCursorKey].wasPressedThisFrame)
        {
            SetCursorLock(!_cursorLocked);
        }
    }

    private bool IsLocalOwner()
    {
        // 네트워크가 아닌 단독 테스트(에디터) 상황은 오너 취급
        if (_netObj == null)
            return true;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return true;

        return _netObj.IsOwner;
    }

    private void InitCursorStateForScene()
    {
        bool startLocked = defaultCursorLocked;

        string sceneName = SceneManager.GetActiveScene().name;
        if (forceCursorUnlockedScenes != null)
        {
            for (int i = 0; i < forceCursorUnlockedScenes.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(forceCursorUnlockedScenes[i]) &&
                    forceCursorUnlockedScenes[i] == sceneName)
                {
                    startLocked = false;
                    break;
                }
            }
        }

        SetCursorLock(startLocked);
        _cursorInitDone = true;
    }

    private void SetCursorLock(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    public void ReadInputs(
        out Vector2 move,
        out float yawDelta,
        out float pitchDelta,
        out bool jumpPressed,
        out bool sprintHeld,
        out bool attackPressed,
        out bool interactPressed,
        out bool dropPressed
    )
    {
        // 오너가 아니면 입력/커서 영향 절대 금지
        if (!IsLocalOwner())
        {
            move = Vector2.zero;
            yawDelta = 0f;
            pitchDelta = 0f;
            jumpPressed = false;
            sprintHeld = false;
            attackPressed = false;
            interactPressed = false;
            dropPressed = false;
            return;
        }

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        // 이동
        float x = 0f;
        float y = 0f;

        if (kb != null)
        {
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            if (kb.wKey.isPressed) y += 1f;
        }

        move = new Vector2(x, y);
        if (move.sqrMagnitude > 1f) move.Normalize();

        // 마우스
        if (_cursorLocked && mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yawDelta = delta.x * mouseSensitivityX;
            pitchDelta = delta.y * mouseSensitivityY;
        }
        else
        {
            yawDelta = 0f;
            pitchDelta = 0f;
        }

        // 액션
        jumpPressed = (kb != null) && kb[jumpKey].wasPressedThisFrame;
        sprintHeld = (kb != null) && kb[sprintKey].isPressed;

        attackPressed = (mouse != null) && ReadMouseButton(mouse, attackMouseButton);
        interactPressed = (mouse != null) && ReadMouseButton(mouse, interactMouseButton);

        dropPressed = (kb != null) && kb[dropKey].wasPressedThisFrame;
    }

    private bool ReadMouseButton(Mouse mouse, int button)
    {
        switch (button)
        {
            case 0: return mouse.leftButton.wasPressedThisFrame;
            case 1: return mouse.rightButton.wasPressedThisFrame;
            case 2: return mouse.middleButton.wasPressedThisFrame;
            default: return false;
        }
    }
}
