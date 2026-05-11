using Unity.Netcode;
using UnityEngine;

public class PlayerCharacterVisualController : MonoBehaviour
{
    [SerializeField, Tooltip("캐릭터 선택 ID를 제공하는 시스템입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private CharacterSelectionSystem characterSelectionSystem;

    [SerializeField, Tooltip("캐릭터 ID에 따라 켜고 끌 visual-only 오브젝트 목록입니다. 배열 인덱스가 characterId입니다.")]
    private GameObject[] characterVisuals;

    [SerializeField, Tooltip("선택값을 찾지 못했거나 범위를 벗어났을 때 사용할 기본 캐릭터 ID입니다.")]
    private int fallbackCharacterId = 0;

    [SerializeField, Tooltip("Start 시점에 현재 선택값을 즉시 visual에 반영할지 여부입니다.")]
    private bool applyOnStart = true;

    [SerializeField, Tooltip("선택값 변경을 확인하는 갱신 간격입니다.")]
    private float refreshInterval = 0.2f;

    [SerializeField, Tooltip("유효한 visual을 찾지 못했을 때 모든 visual을 숨길지 여부입니다.")]
    private bool hideAllWhenNoValidVisual = false;

    private NetworkObject _ownerNetworkObject;
    private float _nextRefreshTime;
    private int _currentCharacterId = -1;

    public int CurrentCharacterId => _currentCharacterId;

    private void Awake()
    {
        ResolveRefs();
        ClampSettings();
    }

    private void Start()
    {
        ResolveRefs();

        if (applyOnStart)
            ForceRefresh();
    }

    private void Update()
    {
        if (refreshInterval > 0f && Time.time < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.time + Mathf.Max(0f, refreshInterval);
        ForceRefresh();
    }

    public void ForceRefresh()
    {
        ResolveRefs();

        int characterId = ResolveSelectedCharacterId();
        if (characterId == _currentCharacterId)
            return;

        ApplyCharacterVisual(characterId);
    }

    public void ApplyCharacterVisual(int characterId)
    {
        int resolvedCharacterId = ResolveValidCharacterId(characterId);
        bool hasValidVisual = IsUsableVisualIndex(resolvedCharacterId);

        if (!hasValidVisual && !hideAllWhenNoValidVisual)
        {
            resolvedCharacterId = ResolveValidCharacterId(fallbackCharacterId);
            hasValidVisual = IsUsableVisualIndex(resolvedCharacterId);
        }

        SetCharacterVisuals(resolvedCharacterId, hasValidVisual);
        _currentCharacterId = hasValidVisual ? resolvedCharacterId : -1;
    }

    private void ResolveRefs()
    {
        if (characterSelectionSystem == null)
            characterSelectionSystem = FindFirstObjectByType<CharacterSelectionSystem>();

        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInParent<NetworkObject>();

        if (_ownerNetworkObject == null)
            _ownerNetworkObject = GetComponentInChildren<NetworkObject>(true);
    }

    private int ResolveSelectedCharacterId()
    {
        if (characterSelectionSystem == null || _ownerNetworkObject == null || !_ownerNetworkObject.IsSpawned)
            return fallbackCharacterId;

        ulong ownerClientId = _ownerNetworkObject.OwnerClientId;
        if (!characterSelectionSystem.TryGetSelectedCharacter(ownerClientId, out int characterId))
            return fallbackCharacterId;

        return ResolveValidCharacterId(characterId);
    }

    private int ResolveValidCharacterId(int characterId)
    {
        if (IsValidVisualIndex(characterId))
            return characterId;

        if (IsValidVisualIndex(fallbackCharacterId))
            return fallbackCharacterId;

        return 0;
    }

    private bool IsValidVisualIndex(int characterId)
    {
        return characterVisuals != null &&
               characterId >= 0 &&
               characterId < characterVisuals.Length;
    }

    private bool IsUsableVisualIndex(int characterId)
    {
        return IsValidVisualIndex(characterId) && IsSafeVisualTarget(characterVisuals[characterId]);
    }

    private void SetCharacterVisuals(int activeCharacterId, bool hasValidVisual)
    {
        if (characterVisuals == null)
            return;

        for (int i = 0; i < characterVisuals.Length; i++)
        {
            GameObject visual = characterVisuals[i];
            if (!IsSafeVisualTarget(visual))
                continue;

            bool shouldBeActive = hasValidVisual && i == activeCharacterId;
            if (visual.activeSelf != shouldBeActive)
                visual.SetActive(shouldBeActive);
        }
    }

    private bool IsSafeVisualTarget(GameObject visual)
    {
        if (visual == null)
            return false;

        if (visual == gameObject)
            return false;

        if (_ownerNetworkObject != null && visual == _ownerNetworkObject.gameObject)
            return false;

        if (visual.transform == transform.root)
            return false;

        if (visual.GetComponentInChildren<NetworkObject>(true) != null)
            return false;

        if (visual.GetComponentInChildren<PlayerHub>(true) != null)
            return false;

        if (visual.GetComponentInChildren<PlayerInputModule>(true) != null ||
            visual.GetComponentInChildren<PlayerLocomotionModule>(true) != null ||
            visual.GetComponentInChildren<PlayerAnimModule>(true) != null ||
            visual.GetComponentInChildren<PlayerCombatModule>(true) != null ||
            visual.GetComponentInChildren<PlayerInteractModule>(true) != null ||
            visual.GetComponentInChildren<PlayerStatusModule>(true) != null ||
            visual.GetComponentInChildren<PlayerCoinWalletModule>(true) != null ||
            visual.GetComponentInChildren<PlayerStaminaModule>(true) != null)
        {
            return false;
        }

        string visualName = visual.name;
        return visualName != "Module" &&
               !visualName.Contains("Hurtbox") &&
               !visualName.Contains("WeaponPoint") &&
               !visualName.Contains("Camera");
    }

    private void ClampSettings()
    {
        if (refreshInterval < 0f)
            refreshInterval = 0f;

        if (fallbackCharacterId < 0)
            fallbackCharacterId = 0;
    }

    private void OnValidate()
    {
        ClampSettings();
    }
}
