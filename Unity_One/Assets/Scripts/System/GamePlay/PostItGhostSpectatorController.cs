using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PostItGhostSpectatorController : MonoBehaviour
{
    private const string InGameSceneName = "InGame";
    private const string BodyHurtboxName = "BodyHurtbox";
    private const string MotorShellBodyName = "MotorShellBody";
    private const ulong InvalidNetworkId = ulong.MaxValue;
    private const float MinimumBeginRetrySeconds = 0.25f;

    private enum SpectatorMode
    {
        Inactive,
        Free,
        Following
    }

    private struct SpectatorCandidate
    {
        public ulong OwnerClientId;
        public ulong NetworkObjectId;
        public NetworkObject PlayerObject;
        public PlayerStatusModule Status;
        public PlayerPostItInventory Inventory;
        public Transform Root;
        public Collider Hurtbox;
        public Rigidbody Body;
    }

    [Header("Scene References")]
    [SerializeField] private Camera spectatorCamera;
    [SerializeField] private AudioListener spectatorAudioListener;
    [SerializeField] private BoxCollider spectatorBounds;

    [Header("Binding")]
    [SerializeField] private float managerResolveInterval = 0.5f;
    [SerializeField] private float localPlayerResolveInterval = 0.25f;

    [Header("Free Camera")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float verticalMoveSpeed = 5f;
    [SerializeField] private float boostMultiplier = 2.5f;
    [SerializeField] private float lookSensitivity = 0.10f;
    [SerializeField] private float minimumPitch = -80f;
    [SerializeField] private float maximumPitch = 80f;

    [Header("Follow Camera")]
    [SerializeField] private float followDistance = 4.5f;
    [SerializeField] private float minimumFollowDistance = 2.0f;
    [SerializeField] private float maximumFollowDistance = 8.0f;
    [SerializeField] private float followHeight = 1.2f;
    [SerializeField] private float followPositionSmoothTime = 0.12f;
    [SerializeField] private float followRotationSpeed = 12f;
    [SerializeField] private float scrollZoomSpeed = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private readonly List<SpectatorCandidate> _survivors =
        new List<SpectatorCandidate>();

    private GameStateManager _gameStateManager;
    private NetworkManager _networkManager;
    private PlayerHub _localPlayerHub;
    private PlayerStatusModule _localPlayerStatus;
    private ulong _localPlayerNetworkObjectId = InvalidNetworkId;
    private SpectatorMode _mode;
    private SpectatorCandidate _followCandidate;
    private ulong _followTargetOwnerClientId = InvalidNetworkId;
    private ulong _followTargetNetworkObjectId = InvalidNetworkId;
    private float _cameraYaw;
    private float _cameraPitch;
    private float _currentFollowDistance;
    private Vector3 _followPositionVelocity;
    private float _nextManagerResolveTime = float.NegativeInfinity;
    private float _nextLocalPlayerResolveTime = float.NegativeInfinity;
    private float _nextBeginAttemptTime = float.NegativeInfinity;
    private string _lastLoggedState = string.Empty;
    private bool _isSpectating;
    private bool _preserveForeignSpectatorOutputs;
    private bool _lifecycleCleanupComplete;

    public bool IsSpectating => _isSpectating;
    public bool IsFollowingTarget =>
        _isSpectating && _mode == SpectatorMode.Following;
    public ulong FollowTargetOwnerClientId => _followTargetOwnerClientId;
    public Camera ActiveSpectatorCamera => spectatorCamera;

    private void Awake()
    {
        DisableSpectatorOutputs();
        _currentFollowDistance = GetInitialFollowDistance();
    }

    private void OnEnable()
    {
        _lifecycleCleanupComplete = false;
        _nextManagerResolveTime = float.NegativeInfinity;
        _nextLocalPlayerResolveTime = float.NegativeInfinity;
        _nextBeginAttemptTime = float.NegativeInfinity;
    }

    private void Update()
    {
        if (_isSpectating)
        {
            if (!IsActiveSpectatorStateValid())
            {
                ExitSpectator("active state invalid");
                ResetLocalPlayerBinding();
                return;
            }

            if (_mode == SpectatorMode.Following &&
                !IsCandidateValid(_followCandidate))
            {
                SelectNextCandidateAfterInvalidation();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                CycleFollowTarget();

            TickSpectatorCamera(keyboard, Mouse.current);
            return;
        }

        TryEnterSpectator();
    }

    private void OnDisable()
    {
        CleanupLifecycle("controller disabled");
    }

    private void OnDestroy()
    {
        CleanupLifecycle("controller destroyed");
    }

    private void TryEnterSpectator()
    {
        float now = Time.unscaledTime;
        if (now < _nextLocalPlayerResolveTime || now < _nextBeginAttemptTime)
            return;

        _nextLocalPlayerResolveTime =
            now + GetResolveInterval(localPlayerResolveInterval);
        if (!TryResolveEntryState())
            return;

        Camera originalCamera = _localPlayerHub.ActiveLocalCamera;
        if (originalCamera == null || !IsFinitePose(originalCamera.transform))
        {
            ScheduleBeginRetry();
            return;
        }

        spectatorCamera.transform.SetPositionAndRotation(
            originalCamera.transform.position,
            originalCamera.transform.rotation);
        if (!TryClampToSpectatorBounds(
                spectatorCamera.transform.position,
                out Vector3 clampedPosition))
        {
            ScheduleBeginRetry();
            return;
        }

        spectatorCamera.transform.position = clampedPosition;
        InitializeCameraAngles(spectatorCamera.transform.rotation);
        ClearFollowTarget();
        _mode = SpectatorMode.Free;
        _currentFollowDistance = GetInitialFollowDistance();
        _followPositionVelocity = Vector3.zero;

        bool began = _localPlayerHub.TryBeginLocalCameraOverride(
            this,
            spectatorCamera,
            spectatorAudioListener);
        if (!began ||
            !_localPlayerHub.IsLocalCameraOverrideActive ||
            _localPlayerHub.ActiveLocalCamera != spectatorCamera)
        {
            if (began)
                _localPlayerHub.TryEndLocalCameraOverride(this);

            RollBackEnterState();
            ScheduleBeginRetry();
            return;
        }

        _isSpectating = true;
        LogState("entered Free spectator mode");
    }

    private bool TryResolveEntryState()
    {
        if (!IsInGameActiveScene() || !AreSceneReferencesValid())
            return false;

        float now = Time.unscaledTime;
        _networkManager = NetworkManager.Singleton;
        if (_networkManager == null || !_networkManager.IsListening)
            return false;

        if (_gameStateManager == null || !_gameStateManager.IsSpawned)
        {
            _gameStateManager = null;
            if (now < _nextManagerResolveTime)
                return false;

            _nextManagerResolveTime =
                now + GetResolveInterval(managerResolveInterval);
            _gameStateManager = FindFirstObjectByType<GameStateManager>();
        }

        if (_gameStateManager == null ||
            !_gameStateManager.IsSpawned ||
            _gameStateManager.GetState() != GameStateManager.GameState.Playing)
        {
            return false;
        }

        NetworkClient localClient = _networkManager.LocalClient;
        NetworkObject playerObject = localClient != null
            ? localClient.PlayerObject
            : null;
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            !playerObject.IsPlayerObject ||
            playerObject.OwnerClientId != _networkManager.LocalClientId)
        {
            return false;
        }

        if (!TryResolveLocalPlayerComponents(
                playerObject,
                out PlayerHub playerHub,
                out PlayerStatusModule playerStatus))
        {
            return false;
        }

        if (!playerHub.IsOwner || !playerStatus.IsEliminated)
            return false;

        _localPlayerHub = playerHub;
        _localPlayerStatus = playerStatus;
        _localPlayerNetworkObjectId = playerObject.NetworkObjectId;
        if (playerHub.IsLocalCameraOverrideActive)
        {
            _preserveForeignSpectatorOutputs =
                playerHub.ActiveLocalCamera == spectatorCamera;
            return false;
        }

        _preserveForeignSpectatorOutputs = false;
        return true;
    }

    private static bool TryResolveLocalPlayerComponents(
        NetworkObject playerObject,
        out PlayerHub playerHub,
        out PlayerStatusModule playerStatus)
    {
        playerHub = null;
        playerStatus = null;
        if (playerObject == null)
            return false;

        playerHub = playerObject.GetComponent<PlayerHub>();
        if (playerHub == null)
            playerHub = playerObject.GetComponentInChildren<PlayerHub>(true);

        playerStatus = playerObject.GetComponent<PlayerStatusModule>();
        if (playerStatus == null)
        {
            playerStatus =
                playerObject.GetComponentInChildren<PlayerStatusModule>(true);
        }

        return playerHub != null &&
               playerHub.IsSpawned &&
               playerHub.GetComponentInParent<NetworkObject>() == playerObject &&
               playerStatus != null &&
               playerStatus.IsSpawned &&
               playerStatus.GetComponentInParent<NetworkObject>() == playerObject;
    }

    private bool IsActiveSpectatorStateValid()
    {
        if (!IsInGameActiveScene() || !AreSceneReferencesValid())
            return false;

        if (_networkManager == null ||
            _networkManager != NetworkManager.Singleton ||
            !_networkManager.IsListening ||
            _gameStateManager == null ||
            !_gameStateManager.IsSpawned ||
            _gameStateManager.GetState() != GameStateManager.GameState.Playing)
        {
            return false;
        }

        NetworkClient localClient = _networkManager.LocalClient;
        NetworkObject playerObject = localClient != null
            ? localClient.PlayerObject
            : null;
        if (playerObject == null ||
            !playerObject.IsSpawned ||
            playerObject.NetworkObjectId != _localPlayerNetworkObjectId ||
            _localPlayerHub == null ||
            !_localPlayerHub.IsSpawned ||
            !_localPlayerHub.IsOwner ||
            _localPlayerHub.GetComponentInParent<NetworkObject>() != playerObject ||
            _localPlayerStatus == null ||
            !_localPlayerStatus.IsSpawned ||
            !_localPlayerStatus.IsEliminated ||
            _localPlayerStatus.GetComponentInParent<NetworkObject>() != playerObject)
        {
            return false;
        }

        return _localPlayerHub.IsLocalCameraOverrideActive &&
               _localPlayerHub.ActiveLocalCamera == spectatorCamera;
    }

    private void ExitSpectator(string reason)
    {
        bool wasSpectating = _isSpectating || _mode != SpectatorMode.Inactive;
        if (_isSpectating && _localPlayerHub != null)
            _localPlayerHub.TryEndLocalCameraOverride(this);

        _isSpectating = false;
        _mode = SpectatorMode.Inactive;
        _survivors.Clear();
        ClearFollowTarget();
        _followPositionVelocity = Vector3.zero;
        _cameraYaw = 0f;
        _cameraPitch = 0f;
        _currentFollowDistance = GetInitialFollowDistance();
        DisableSpectatorOutputsIfUnowned();

        if (wasSpectating)
            LogState($"exited spectator mode reason={reason}");
    }

    private void RollBackEnterState()
    {
        _isSpectating = false;
        _mode = SpectatorMode.Inactive;
        _survivors.Clear();
        ClearFollowTarget();
        _followPositionVelocity = Vector3.zero;
        DisableSpectatorOutputsIfUnowned();
    }

    private void ResetLocalPlayerBinding()
    {
        _localPlayerHub = null;
        _localPlayerStatus = null;
        _localPlayerNetworkObjectId = InvalidNetworkId;
        _nextLocalPlayerResolveTime = float.NegativeInfinity;
        _nextBeginAttemptTime = float.NegativeInfinity;
    }

    private void CleanupLifecycle(string reason)
    {
        if (_lifecycleCleanupComplete)
            return;

        ExitSpectator(reason);
        ResetLocalPlayerBinding();
        _lifecycleCleanupComplete = true;
    }

    private void ScheduleBeginRetry()
    {
        _nextBeginAttemptTime =
            Time.unscaledTime + GetResolveInterval(localPlayerResolveInterval);
    }

    private void TickSpectatorCamera(Keyboard keyboard, Mouse mouse)
    {
        if (spectatorCamera == null)
            return;

        Vector3 translationInput = ReadTranslationInput(keyboard);
        if (_mode == SpectatorMode.Following &&
            translationInput.sqrMagnitude > 0.0001f)
        {
            InitializeCameraAngles(spectatorCamera.transform.rotation);
            EnterFreeMode("translation input");
        }

        ApplyLookInput(mouse);

        if (_mode == SpectatorMode.Following)
            TickFollowCamera(mouse);
        else if (_mode == SpectatorMode.Free)
            TickFreeCamera(translationInput, keyboard);
    }

    private void ApplyLookInput(Mouse mouse)
    {
        if (mouse == null)
            return;

        Vector2 delta = mouse.delta.ReadValue();
        if (!IsFinite(delta))
            return;

        float sensitivity = IsFinite(lookSensitivity)
            ? Mathf.Max(0f, lookSensitivity)
            : 0f;
        float nextYaw = _cameraYaw + delta.x * sensitivity;
        float nextPitch = _cameraPitch - delta.y * sensitivity;
        if (!IsFinite(nextYaw) || !IsFinite(nextPitch))
            return;

        _cameraYaw = Mathf.Repeat(nextYaw + 180f, 360f) - 180f;
        _cameraPitch = nextPitch;
        GetPitchLimits(out float minPitch, out float maxPitch);
        _cameraPitch = Mathf.Clamp(_cameraPitch, minPitch, maxPitch);
    }

    private Vector3 ReadTranslationInput(Keyboard keyboard)
    {
        if (keyboard == null)
            return Vector3.zero;

        float horizontal =
            (keyboard.dKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed ? 1f : 0f);
        float forward =
            (keyboard.wKey.isPressed ? 1f : 0f) -
            (keyboard.sKey.isPressed ? 1f : 0f);
        float vertical =
            (keyboard.spaceKey.isPressed || keyboard.eKey.isPressed ? 1f : 0f) -
            (keyboard.leftCtrlKey.isPressed || keyboard.qKey.isPressed ? 1f : 0f);
        Vector3 input = new Vector3(horizontal, vertical, forward);
        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

    private void TickFreeCamera(Vector3 input, Keyboard keyboard)
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (!IsFinite(deltaTime) || deltaTime < 0f)
            return;

        Quaternion cameraRotation = Quaternion.Euler(
            _cameraPitch,
            _cameraYaw,
            0f);
        Vector3 planarForward = cameraRotation * Vector3.forward;
        planarForward.y = 0f;
        if (planarForward.sqrMagnitude <= 0.0001f)
            planarForward = Vector3.forward;
        else
            planarForward.Normalize();

        Vector3 planarRight = Vector3.Cross(Vector3.up, planarForward);
        float planarSpeed = IsFinite(moveSpeed) ? Mathf.Max(0f, moveSpeed) : 0f;
        float upSpeed = IsFinite(verticalMoveSpeed)
            ? Mathf.Max(0f, verticalMoveSpeed)
            : 0f;
        float boost = keyboard != null &&
                      (keyboard.leftShiftKey.isPressed ||
                       keyboard.rightShiftKey.isPressed)
            ? (IsFinite(boostMultiplier) ? Mathf.Max(1f, boostMultiplier) : 1f)
            : 1f;

        Vector3 displacement =
            (planarRight * input.x + planarForward * input.z) *
            (planarSpeed * boost * deltaTime) +
            Vector3.up * input.y * (upSpeed * boost * deltaTime);
        Vector3 nextPosition = spectatorCamera.transform.position + displacement;
        if (!IsFinite(nextPosition) ||
            !TryClampToSpectatorBounds(nextPosition, out Vector3 clampedPosition))
        {
            return;
        }

        spectatorCamera.transform.SetPositionAndRotation(
            clampedPosition,
            cameraRotation);
    }

    private void TickFollowCamera(Mouse mouse)
    {
        if (!TryGetFollowFocus(_followCandidate, out Vector3 focus))
        {
            SelectNextCandidateAfterInvalidation();
            return;
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (IsFinite(scroll))
            {
                float zoomSpeed = IsFinite(scrollZoomSpeed)
                    ? Mathf.Max(0f, scrollZoomSpeed)
                    : 0f;
                _currentFollowDistance = Mathf.Clamp(
                    _currentFollowDistance - scroll * zoomSpeed * 0.01f,
                    GetMinimumFollowDistance(),
                    GetMaximumFollowDistance());
            }
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (!IsFinite(deltaTime) || deltaTime < 0f)
            return;

        Quaternion orbitRotation = Quaternion.Euler(
            _cameraPitch,
            _cameraYaw,
            0f);
        Vector3 lookFocus = focus + Vector3.up * GetFiniteOrZero(followHeight);
        Vector3 desiredPosition =
            lookFocus - orbitRotation * Vector3.forward * _currentFollowDistance;
        if (!IsFinite(desiredPosition))
            return;

        Vector3 currentPosition = spectatorCamera.transform.position;
        if (!IsFinite(currentPosition))
            currentPosition = desiredPosition;

        float smoothTime = IsFinite(followPositionSmoothTime)
            ? Mathf.Max(0.0001f, followPositionSmoothTime)
            : 0.0001f;
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            currentPosition,
            desiredPosition,
            ref _followPositionVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
        if (!IsFinite(smoothedPosition) ||
            !TryClampToSpectatorBounds(
                smoothedPosition,
                out Vector3 clampedPosition))
        {
            return;
        }

        Vector3 lookDirection = lookFocus - clampedPosition;
        if (!IsFinite(lookDirection) || lookDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        Quaternion currentRotation = spectatorCamera.transform.rotation;
        if (!IsFinite(currentRotation))
            currentRotation = targetRotation;

        float rotationSpeed = IsFinite(followRotationSpeed)
            ? Mathf.Max(0f, followRotationSpeed)
            : 0f;
        float rotationBlend = 1f - Mathf.Exp(-rotationSpeed * deltaTime);
        Quaternion smoothedRotation = Quaternion.Slerp(
            currentRotation,
            targetRotation,
            Mathf.Clamp01(rotationBlend));
        spectatorCamera.transform.SetPositionAndRotation(
            clampedPosition,
            smoothedRotation);
    }

    private void CycleFollowTarget()
    {
        RebuildSurvivorCandidates();
        if (_survivors.Count == 0)
        {
            EnterFreeMode("no survivors");
            return;
        }

        if (_mode != SpectatorMode.Following)
        {
            SetFollowCandidate(_survivors[0]);
            return;
        }

        int currentIndex = FindCandidateIndex(
            _followTargetOwnerClientId,
            _followTargetNetworkObjectId);
        if (currentIndex >= 0 && currentIndex + 1 < _survivors.Count)
        {
            SetFollowCandidate(_survivors[currentIndex + 1]);
            return;
        }

        EnterFreeMode("follow cycle complete");
    }

    private void SelectNextCandidateAfterInvalidation()
    {
        ulong previousOwnerClientId = _followTargetOwnerClientId;
        ulong previousNetworkObjectId = _followTargetNetworkObjectId;
        RebuildSurvivorCandidates();
        if (_survivors.Count == 0)
        {
            EnterFreeMode("follow target unavailable");
            return;
        }

        for (int index = 0; index < _survivors.Count; index++)
        {
            SpectatorCandidate candidate = _survivors[index];
            if (candidate.OwnerClientId > previousOwnerClientId ||
                (candidate.OwnerClientId == previousOwnerClientId &&
                 candidate.NetworkObjectId > previousNetworkObjectId))
            {
                SetFollowCandidate(candidate);
                return;
            }
        }

        EnterFreeMode("no next survivor");
    }

    private void RebuildSurvivorCandidates()
    {
        _survivors.Clear();
        if (_networkManager == null ||
            !_networkManager.IsListening ||
            _gameStateManager == null ||
            _gameStateManager.GetState() != GameStateManager.GameState.Playing)
        {
            return;
        }

        foreach (KeyValuePair<ulong, NetworkClient> entry in
                 _networkManager.ConnectedClients)
        {
            ulong ownerClientId = entry.Key;
            NetworkClient client = entry.Value;
            if (client == null || client.ClientId != ownerClientId)
                continue;

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null ||
                !playerObject.IsSpawned ||
                !playerObject.IsPlayerObject ||
                playerObject.OwnerClientId != ownerClientId ||
                playerObject.NetworkObjectId == _localPlayerNetworkObjectId)
            {
                continue;
            }

            PlayerStatusModule status =
                playerObject.GetComponent<PlayerStatusModule>();
            if (status == null)
                status = playerObject.GetComponentInChildren<PlayerStatusModule>(true);
            if (status == null ||
                !status.IsSpawned ||
                status.IsEliminated ||
                status.GetComponentInParent<NetworkObject>() != playerObject)
            {
                continue;
            }

            PlayerPostItInventory inventory =
                playerObject.GetComponent<PlayerPostItInventory>();
            if (inventory == null)
            {
                inventory =
                    playerObject.GetComponentInChildren<PlayerPostItInventory>(true);
            }
            if (inventory == null ||
                !inventory.IsSpawned ||
                inventory.PublicVisualCount <= 0 ||
                inventory.GetComponentInParent<NetworkObject>() != playerObject ||
                ContainsCandidateNetworkObjectId(playerObject.NetworkObjectId))
            {
                continue;
            }

            Transform root = playerObject.transform;
            Transform hurtboxTransform = FindChildRecursive(root, BodyHurtboxName);
            Collider hurtbox = hurtboxTransform != null
                ? hurtboxTransform.GetComponent<Collider>()
                : null;
            if (hurtbox != null &&
                hurtbox.GetComponentInParent<NetworkObject>() != playerObject)
            {
                hurtbox = null;
            }

            Transform bodyTransform = FindChildRecursive(root, MotorShellBodyName);
            Rigidbody body = bodyTransform != null
                ? bodyTransform.GetComponent<Rigidbody>()
                : null;
            if (body != null &&
                body.GetComponentInParent<NetworkObject>() != playerObject)
            {
                body = null;
            }

            _survivors.Add(new SpectatorCandidate
            {
                OwnerClientId = ownerClientId,
                NetworkObjectId = playerObject.NetworkObjectId,
                PlayerObject = playerObject,
                Status = status,
                Inventory = inventory,
                Root = root,
                Hurtbox = hurtbox,
                Body = body
            });
        }

        _survivors.Sort(CompareCandidates);
    }

    private bool IsCandidateValid(SpectatorCandidate candidate)
    {
        if (_networkManager == null || !_networkManager.IsListening)
            return false;

        if (!_networkManager.ConnectedClients.TryGetValue(
                candidate.OwnerClientId,
                out NetworkClient client) ||
            client == null ||
            client.ClientId != candidate.OwnerClientId ||
            client.PlayerObject != candidate.PlayerObject)
        {
            return false;
        }

        NetworkObject playerObject = candidate.PlayerObject;
        return playerObject != null &&
               playerObject.IsSpawned &&
               playerObject.IsPlayerObject &&
               playerObject.OwnerClientId == candidate.OwnerClientId &&
               playerObject.NetworkObjectId == candidate.NetworkObjectId &&
               candidate.NetworkObjectId != _localPlayerNetworkObjectId &&
               candidate.Status != null &&
               candidate.Status.IsSpawned &&
               !candidate.Status.IsEliminated &&
               candidate.Status.GetComponentInParent<NetworkObject>() == playerObject &&
               candidate.Inventory != null &&
               candidate.Inventory.IsSpawned &&
               candidate.Inventory.PublicVisualCount > 0 &&
               candidate.Inventory.GetComponentInParent<NetworkObject>() == playerObject &&
               candidate.Root != null;
    }

    private bool TryGetFollowFocus(
        SpectatorCandidate candidate,
        out Vector3 focus)
    {
        focus = default;
        if (!IsCandidateValid(candidate))
            return false;

        if (candidate.Hurtbox != null &&
            candidate.Hurtbox.enabled &&
            candidate.Hurtbox.gameObject.activeInHierarchy &&
            candidate.Hurtbox.GetComponentInParent<NetworkObject>() ==
            candidate.PlayerObject)
        {
            Vector3 hurtboxFocus = candidate.Hurtbox.bounds.center;
            if (IsFinite(hurtboxFocus))
            {
                focus = hurtboxFocus;
                return true;
            }
        }

        if (candidate.Body != null &&
            candidate.Body.gameObject.activeInHierarchy &&
            candidate.Body.GetComponentInParent<NetworkObject>() ==
            candidate.PlayerObject)
        {
            Vector3 bodyFocus = candidate.Body.worldCenterOfMass;
            if (IsFinite(bodyFocus))
            {
                focus = bodyFocus;
                return true;
            }
        }

        Vector3 rootFocus = candidate.Root.position + Vector3.up * 0.8f;
        if (!IsFinite(rootFocus))
            return false;

        focus = rootFocus;
        return true;
    }

    private void SetFollowCandidate(SpectatorCandidate candidate)
    {
        if (!IsCandidateValid(candidate))
        {
            EnterFreeMode("invalid follow candidate");
            return;
        }

        _followCandidate = candidate;
        _followTargetOwnerClientId = candidate.OwnerClientId;
        _followTargetNetworkObjectId = candidate.NetworkObjectId;
        _followPositionVelocity = Vector3.zero;
        _mode = SpectatorMode.Following;
        LogState(
            $"following owner={candidate.OwnerClientId} object={candidate.NetworkObjectId}");
    }

    private void EnterFreeMode(string reason)
    {
        ClearFollowTarget();
        _followPositionVelocity = Vector3.zero;
        _mode = _isSpectating ? SpectatorMode.Free : SpectatorMode.Inactive;
        LogState($"Free mode reason={reason}");
    }

    private void ClearFollowTarget()
    {
        _followCandidate = default;
        _followTargetOwnerClientId = InvalidNetworkId;
        _followTargetNetworkObjectId = InvalidNetworkId;
    }

    private int FindCandidateIndex(
        ulong ownerClientId,
        ulong networkObjectId)
    {
        for (int index = 0; index < _survivors.Count; index++)
        {
            SpectatorCandidate candidate = _survivors[index];
            if (candidate.OwnerClientId == ownerClientId &&
                candidate.NetworkObjectId == networkObjectId)
            {
                return index;
            }
        }

        return -1;
    }

    private bool ContainsCandidateNetworkObjectId(ulong networkObjectId)
    {
        for (int index = 0; index < _survivors.Count; index++)
        {
            if (_survivors[index].NetworkObjectId == networkObjectId)
                return true;
        }

        return false;
    }

    private static int CompareCandidates(
        SpectatorCandidate left,
        SpectatorCandidate right)
    {
        int ownerComparison = left.OwnerClientId.CompareTo(right.OwnerClientId);
        return ownerComparison != 0
            ? ownerComparison
            : left.NetworkObjectId.CompareTo(right.NetworkObjectId);
    }

    private bool AreSceneReferencesValid()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (spectatorCamera == null ||
            spectatorAudioListener == null ||
            spectatorBounds == null ||
            spectatorCamera.gameObject != spectatorAudioListener.gameObject ||
            !activeScene.IsValid() ||
            !activeScene.isLoaded ||
            spectatorCamera.gameObject.scene != activeScene ||
            spectatorBounds.gameObject.scene != activeScene ||
            !spectatorCamera.gameObject.activeInHierarchy ||
            !spectatorBounds.gameObject.activeInHierarchy ||
            spectatorCamera.GetComponentInParent<NetworkObject>() != null)
        {
            return false;
        }

        return AreBoundsValid();
    }

    private bool AreBoundsValid()
    {
        if (spectatorBounds == null)
            return false;

        Vector3 size = spectatorBounds.size;
        Vector3 center = spectatorBounds.center;
        Vector3 scale = spectatorBounds.transform.lossyScale;
        return IsFinitePose(spectatorBounds.transform) &&
               IsFinite(size) &&
               size.x > 0f &&
               size.y > 0f &&
               size.z > 0f &&
               IsFinite(center) &&
               IsFinite(scale) &&
               Mathf.Abs(scale.x) > 0.0001f &&
               Mathf.Abs(scale.y) > 0.0001f &&
               Mathf.Abs(scale.z) > 0.0001f;
    }

    private bool TryClampToSpectatorBounds(
        Vector3 worldPosition,
        out Vector3 clampedWorldPosition)
    {
        clampedWorldPosition = worldPosition;
        if (!IsFinite(worldPosition) || !AreBoundsValid())
            return false;

        Transform boundsTransform = spectatorBounds.transform;
        Vector3 localPosition = boundsTransform.InverseTransformPoint(worldPosition);
        if (!IsFinite(localPosition))
            return false;

        Vector3 halfSize = spectatorBounds.size * 0.5f;
        Vector3 minimum = spectatorBounds.center - halfSize;
        Vector3 maximum = spectatorBounds.center + halfSize;
        localPosition.x = Mathf.Clamp(localPosition.x, minimum.x, maximum.x);
        localPosition.y = Mathf.Clamp(localPosition.y, minimum.y, maximum.y);
        localPosition.z = Mathf.Clamp(localPosition.z, minimum.z, maximum.z);
        Vector3 worldResult = boundsTransform.TransformPoint(localPosition);
        if (!IsFinite(worldResult))
            return false;

        clampedWorldPosition = worldResult;
        return true;
    }

    private void InitializeCameraAngles(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        _cameraYaw = IsFinite(euler.y) ? euler.y : 0f;
        _cameraPitch = IsFinite(euler.x)
            ? NormalizeSignedAngle(euler.x)
            : 0f;
        GetPitchLimits(out float minPitch, out float maxPitch);
        _cameraPitch = Mathf.Clamp(_cameraPitch, minPitch, maxPitch);
    }

    private void GetPitchLimits(out float minPitch, out float maxPitch)
    {
        minPitch = IsFinite(minimumPitch) ? minimumPitch : -80f;
        maxPitch = IsFinite(maximumPitch) ? maximumPitch : 80f;
        if (minPitch > maxPitch)
        {
            float temporary = minPitch;
            minPitch = maxPitch;
            maxPitch = temporary;
        }
    }

    private float GetInitialFollowDistance()
    {
        float initial = IsFinite(followDistance) ? followDistance : 4.5f;
        return Mathf.Clamp(
            initial,
            GetMinimumFollowDistance(),
            GetMaximumFollowDistance());
    }

    private static float GetResolveInterval(float configuredInterval)
    {
        return IsFinite(configuredInterval)
            ? Mathf.Max(MinimumBeginRetrySeconds, configuredInterval)
            : MinimumBeginRetrySeconds;
    }

    private float GetMinimumFollowDistance()
    {
        return IsFinite(minimumFollowDistance)
            ? Mathf.Max(0.1f, minimumFollowDistance)
            : 2f;
    }

    private float GetMaximumFollowDistance()
    {
        float minimum = GetMinimumFollowDistance();
        return IsFinite(maximumFollowDistance)
            ? Mathf.Max(minimum, maximumFollowDistance)
            : Mathf.Max(minimum, 8f);
    }

    private void DisableSpectatorOutputs()
    {
        if (spectatorCamera != null)
            spectatorCamera.enabled = false;
        if (spectatorAudioListener != null)
            spectatorAudioListener.enabled = false;
    }

    private void DisableSpectatorOutputsIfUnowned()
    {
        if (_preserveForeignSpectatorOutputs)
            return;

        if (_localPlayerHub != null &&
            _localPlayerHub.IsLocalCameraOverrideActive &&
            _localPlayerHub.ActiveLocalCamera == spectatorCamera)
        {
            _preserveForeignSpectatorOutputs = true;
            return;
        }

        DisableSpectatorOutputs();
    }

    private static bool IsInGameActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && scene.isLoaded && scene.name == InGameSceneName;
    }

    private static Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        if (parent.name == childName)
            return parent;

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform result = FindChildRecursive(parent.GetChild(index), childName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static float NormalizeSignedAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private static float GetFiniteOrZero(float value)
    {
        return IsFinite(value) ? value : 0f;
    }

    private static bool IsFinitePose(Transform target)
    {
        return target != null &&
               IsFinite(target.position) &&
               IsFinite(target.rotation);
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z) &&
               IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void LogState(string message)
    {
        if (!debugLogs || message == _lastLoggedState)
            return;

        _lastLoggedState = message;
        Debug.Log($"[PostItGhostSpectator] {message}", this);
    }
}
