using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DevTestUI : MonoBehaviour
{
    [Header("UI Refs")]
    [Tooltip("Host 생성된 JoinCode를 표시할 InputField")]
    [SerializeField] private TMP_InputField createRoomInput;

    [Tooltip("JoinCode를 입력할 InputField")]
    [SerializeField] private TMP_InputField codeJoinInput;

    [Tooltip("Host 버튼")]
    [SerializeField] private Button createButton;

    [Tooltip("Join 버튼")]
    [SerializeField] private Button joinButton;

    [Header("Options")]
    [Tooltip("Host 생성 시 최대 접속자 수")]
    [SerializeField] private int maxConnections = 4;

    private void Awake()
    {
        if (createButton != null) createButton.onClick.AddListener(OnClickHost);
        if (joinButton != null) joinButton.onClick.AddListener(OnClickJoin);
    }

    private async void OnClickHost()
    {
        if (RelayManager.Instance == null)
        {
            Debug.LogError("[DevTestUI] RelayManager.Instance가 없습니다.");
            return;
        }

        string code = await RelayManager.Instance.CreateRelay(maxConnections);
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogError("[DevTestUI] Host 생성 실패");
            return;
        }

        if (createRoomInput != null)
        {
            createRoomInput.text = code;
        }

        GUIUtility.systemCopyBuffer = code;
        Debug.Log($"[DevTestUI] 호스트 생성 완료: {code} (자동 복사됨)");
    }

    private async void OnClickJoin()
    {
        if (RelayManager.Instance == null)
        {
            Debug.LogError("[DevTestUI] RelayManager.Instance가 없습니다.");
            return;
        }

        string code = codeJoinInput != null ? codeJoinInput.text : "";
        code = code.Trim();

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("[DevTestUI] JoinCode가 비어있습니다.");
            return;
        }

        bool ok = await RelayManager.Instance.JoinRelayAsync(code);
        Debug.Log(ok ? "[DevTestUI] Join 성공" : "[DevTestUI] Join 실패");
    }
}
