using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DevTestUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField, Tooltip("호스트가 만든 Join Code를 표시할 입력창(TMP). 읽기 전용으로 쓰세요")]
    private TMP_InputField createRoomCodeOutput;

    [SerializeField, Tooltip("참가할 Join Code를 입력하는 입력창(TMP)")]
    private TMP_InputField joinCodeInput;

    [SerializeField, Tooltip("호스트 버튼")]
    private Button hostButton;

    [SerializeField, Tooltip("조인 버튼")]
    private Button joinButton;

    [SerializeField, Tooltip("상태 텍스트(TMP). 없어도 됨")]
    private TMP_Text statusText;

    [Header("Relay")]
    [SerializeField, Tooltip("호스트 생성 시 최대 접속자 수(호스트 제외)")]
    private int maxConnections = 3;

    private void Awake()
    {
        if (createRoomCodeOutput != null)
        {
            createRoomCodeOutput.readOnly = true;
            createRoomCodeOutput.interactable = true; // 복사 가능하게
        }

        if (hostButton != null) hostButton.onClick.AddListener(Host);
        if (joinButton != null) joinButton.onClick.AddListener(Join);
    }

    public async void Host()
    {
        if (RelayManager.Instance == null)
        {
            SetStatus("RelayManager 없음");
            return;
        }

        SetInteract(false);
        SetStatus("호스트 생성 중...");

        string code = await RelayManager.Instance.CreateRelay(maxConnections);

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("호스트 생성 실패");
            SetInteract(true);
            return;
        }

        if (createRoomCodeOutput != null)
            createRoomCodeOutput.text = code;

        GUIUtility.systemCopyBuffer = code; // 자동 복사
        SetStatus($"호스트 생성 완료: {code} (자동 복사됨)");
        SetInteract(true);
    }

    public async void Join()
    {
        if (RelayManager.Instance == null)
        {
            SetStatus("RelayManager 없음");
            return;
        }

        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : "";
        code = code.ToUpperInvariant();

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("코드를 입력해줘");
            return;
        }

        SetInteract(false);
        SetStatus("참가 시도 중...");

        bool ok = await RelayManager.Instance.JoinRelayAsync(code);

        SetStatus(ok ? "참가 성공" : "참가 실패 (코드/네트워크 확인)");
        SetInteract(true);
    }

    private void SetInteract(bool on)
    {
        if (hostButton != null) hostButton.interactable = on;
        if (joinButton != null) joinButton.interactable = on;
        if (joinCodeInput != null) joinCodeInput.interactable = on;
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log($"[DevTestUI] {msg}");
    }
}
