using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PostItPeelProgressHUD : MonoBehaviour
{
    [SerializeField, Tooltip("뜯기 진행 중에만 표시할 게이지 루트입니다.")]
    private GameObject progressRoot;

    [SerializeField, Tooltip("뜯기 유지 진행률을 표시할 Filled Image입니다.")]
    private Image progressFillImage;

    [SerializeField, Tooltip("표시할 PlayerHub입니다. 비워두면 로컬 Owner를 자동 탐색합니다.")]
    private PlayerHub targetPlayerHub;

    [SerializeField, Tooltip("로컬 Owner를 찾거나 교체 여부를 다시 확인하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    private PlayerHub _boundPlayerHub;
    private float _nextBindAttemptTime;

    public PlayerHub BoundPlayerHub => _boundPlayerHub;

    private void Awake()
    {
        DisableGraphicRaycast();
        SetProgressVisible(false);
    }

    private void OnEnable()
    {
        DisableGraphicRaycast();
        TryBindPlayerHub();
        RefreshProgress();
    }

    private void OnDisable()
    {
        _boundPlayerHub = null;
        SetProgressVisible(false);
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextBindAttemptTime)
            TryBindPlayerHub();

        RefreshProgress();
    }

    public void ForceRebind()
    {
        _boundPlayerHub = null;
        _nextBindAttemptTime = 0f;
        TryBindPlayerHub();
        RefreshProgress();
    }

    private void TryBindPlayerHub()
    {
        _nextBindAttemptTime =
            Time.unscaledTime + Mathf.Max(0.05f, rebindInterval);
        _boundPlayerHub = ResolveTargetPlayerHub();
    }

    private PlayerHub ResolveTargetPlayerHub()
    {
        if (targetPlayerHub != null)
            return CanBindPlayerHub(targetPlayerHub) ? targetPlayerHub : null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return null;

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null ||
            localClient.PlayerObject == null ||
            !localClient.PlayerObject.IsSpawned)
        {
            return null;
        }

        PlayerHub playerHub =
            localClient.PlayerObject.GetComponentInChildren<PlayerHub>(true);
        if (!CanBindPlayerHub(playerHub) ||
            playerHub.NetworkObject != localClient.PlayerObject)
        {
            return null;
        }

        return playerHub;
    }

    private static bool CanBindPlayerHub(PlayerHub playerHub)
    {
        return playerHub != null &&
               playerHub.isActiveAndEnabled &&
               playerHub.IsSpawned &&
               playerHub.IsOwner;
    }

    private void RefreshProgress()
    {
        if (!CanBindPlayerHub(_boundPlayerHub))
        {
            _boundPlayerHub = null;
            SetProgressVisible(false);
            return;
        }

        PlayerHub.CharacterGrabLocalInputState characterGrabState =
            _boundPlayerHub.CurrentCharacterGrabLocalInputState;
        bool peelVisible = _boundPlayerHub.IsPostItPeelTracking;
        bool characterGrabVisible =
            !peelVisible &&
            _boundPlayerHub.ShouldShowLocalCharacterGrabCharge &&
            IsCharacterGrabChargeState(characterGrabState);
        bool visible = characterGrabVisible || peelVisible;
        if (progressFillImage != null)
        {
            float progress = 0f;
            if (characterGrabVisible)
            {
                progress =
                    characterGrabState ==
                    PlayerHub.CharacterGrabLocalInputState.RequestPending
                        ? 0f
                        : _boundPlayerHub.CharacterGrabChargeProgress01;
            }
            else if (peelVisible)
            {
                progress = _boundPlayerHub.PostItPeelHoldProgress01;
            }

            progressFillImage.fillAmount = Mathf.Clamp01(progress);
        }

        SetProgressVisible(visible);
    }

    private static bool IsCharacterGrabChargeState(
        PlayerHub.CharacterGrabLocalInputState state)
    {
        return state == PlayerHub.CharacterGrabLocalInputState.RequestPending ||
               state == PlayerHub.CharacterGrabLocalInputState.Charging ||
               state == PlayerHub.CharacterGrabLocalInputState.LiftReady ||
               state == PlayerHub.CharacterGrabLocalInputState.LiftRequested;
    }

    private void SetProgressVisible(bool visible)
    {
        if (progressRoot == null || progressRoot == gameObject)
            return;

        if (progressRoot.activeSelf != visible)
            progressRoot.SetActive(visible);
    }

    private void DisableGraphicRaycast()
    {
        if (progressFillImage != null)
            progressFillImage.raycastTarget = false;
    }
}
