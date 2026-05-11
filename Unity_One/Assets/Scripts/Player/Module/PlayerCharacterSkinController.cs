using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerCharacterSkinController : MonoBehaviour
{
    [Serializable]
    public class SkinMaterialSet
    {
        [Tooltip("스킨 이름입니다. 디버그와 인스펙터 구분용입니다.")]
        public string skinName;

        [Tooltip("타겟 Renderer 순서에 맞춰 적용할 Material 목록입니다.")]
        public Material[] materials;
    }

    [SerializeField, Tooltip("캐릭터 선택 ID를 제공하는 시스템입니다. 비워두면 씬에서 자동 탐색합니다.")]
    private CharacterSelectionSystem characterSelectionSystem;

    [SerializeField, Tooltip("스킨 Material을 적용할 Renderer 목록입니다. 몸통, 날개, 평면 등 실제 Mesh가 있는 Renderer만 넣습니다.")]
    private Renderer[] targetRenderers;

    [SerializeField, Tooltip("characterId에 대응되는 스킨 Material 세트 목록입니다. 배열 인덱스가 characterId입니다.")]
    private SkinMaterialSet[] skinMaterialSets;

    [SerializeField, Tooltip("선택값을 찾지 못했거나 범위를 벗어났을 때 사용할 기본 스킨 ID입니다.")]
    private int fallbackCharacterId = 0;

    [SerializeField, Tooltip("Start 시점에 현재 선택값을 즉시 스킨에 반영할지 여부입니다.")]
    private bool applyOnStart = true;

    [SerializeField, Tooltip("선택값 변경을 확인하는 갱신 간격입니다.")]
    private float refreshInterval = 0.2f;

    [SerializeField, Tooltip("런타임 적용 시 sharedMaterials를 사용할지 여부입니다. 보통 false를 권장합니다.")]
    private bool useSharedMaterials = false;

    [SerializeField, Tooltip("비활성화될 때 시작 시점의 원래 Material로 되돌릴지 여부입니다.")]
    private bool restoreOriginalMaterialsOnDisable = false;

    [SerializeField, Tooltip("스킨 적용 디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    private int _currentCharacterId = -1;
    private float _nextRefreshTime;
    private Material[][] _originalMaterials;
    private NetworkObject _cachedNetworkObject;

    public int CurrentCharacterId => _currentCharacterId;

    private void Awake()
    {
        ClampSettings();
        ResolveRefs();
        CacheOriginalMaterials();
    }

    private void Start()
    {
        ResolveRefs();

        if (applyOnStart)
            ForceRefresh();
    }

    private void OnDisable()
    {
        if (restoreOriginalMaterialsOnDisable)
            RestoreOriginalMaterials();
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

        int characterId = ResolveCharacterId();
        if (characterId == _currentCharacterId)
            return;

        ApplySkinInternal(characterId);
    }

    public void ApplySkin(int characterId)
    {
        ApplySkinInternal(NormalizeCharacterId(characterId));
    }

    public void RestoreOriginalMaterials()
    {
        if (_originalMaterials == null || targetRenderers == null)
            return;

        int count = Mathf.Min(targetRenderers.Length, _originalMaterials.Length);
        for (int i = 0; i < count; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            Material[] originalMaterials = _originalMaterials[i];
            if (targetRenderer == null || originalMaterials == null)
                continue;

            if (useSharedMaterials)
                targetRenderer.sharedMaterials = CopyMaterials(originalMaterials);
            else
                targetRenderer.materials = CopyMaterials(originalMaterials);
        }

        _currentCharacterId = -1;
    }

    public void SetCharacterSelectionSystem(CharacterSelectionSystem system)
    {
        characterSelectionSystem = system;
        ForceRefresh();
    }

    private void ResolveRefs()
    {
        if (characterSelectionSystem == null)
            characterSelectionSystem = FindFirstObjectByType<CharacterSelectionSystem>();

        if (_cachedNetworkObject == null)
            _cachedNetworkObject = GetComponentInParent<NetworkObject>();

        if (_cachedNetworkObject == null)
            _cachedNetworkObject = GetComponentInChildren<NetworkObject>(true);
    }

    private void CacheOriginalMaterials()
    {
        if (targetRenderers == null)
        {
            _originalMaterials = Array.Empty<Material[]>();
            return;
        }

        _originalMaterials = new Material[targetRenderers.Length][];
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            if (targetRenderer == null)
            {
                _originalMaterials[i] = Array.Empty<Material>();
                continue;
            }

            Material[] sourceMaterials = useSharedMaterials ? targetRenderer.sharedMaterials : targetRenderer.materials;
            _originalMaterials[i] = CopyMaterials(sourceMaterials);
        }
    }

    private ulong? TryGetOwnerClientId()
    {
        if (_cachedNetworkObject == null || !_cachedNetworkObject.IsSpawned)
            return null;

        return _cachedNetworkObject.OwnerClientId;
    }

    private int ResolveCharacterId()
    {
        ulong? ownerClientId = TryGetOwnerClientId();
        if (characterSelectionSystem == null || !ownerClientId.HasValue)
            return NormalizeCharacterId(fallbackCharacterId);

        if (!characterSelectionSystem.TryGetSelectedCharacter(ownerClientId.Value, out int characterId))
            return NormalizeCharacterId(fallbackCharacterId);

        return NormalizeCharacterId(characterId);
    }

    private int NormalizeCharacterId(int characterId)
    {
        if (IsValidSkin(characterId))
            return characterId;

        if (IsValidSkin(fallbackCharacterId))
            return fallbackCharacterId;

        return -1;
    }

    private bool IsValidSkin(int characterId)
    {
        if (skinMaterialSets == null || characterId < 0 || characterId >= skinMaterialSets.Length)
            return false;

        SkinMaterialSet skinMaterialSet = skinMaterialSets[characterId];
        return skinMaterialSet != null &&
               skinMaterialSet.materials != null &&
               skinMaterialSet.materials.Length > 0;
    }

    private void ApplySkinInternal(int characterId)
    {
        int normalizedCharacterId = NormalizeCharacterId(characterId);
        if (normalizedCharacterId < 0)
        {
            LogSkin("적용 가능한 스킨 Material 세트가 없습니다.");
            return;
        }

        SkinMaterialSet skinMaterialSet = skinMaterialSets[normalizedCharacterId];
        if (skinMaterialSet == null || skinMaterialSet.materials == null)
            return;

        int applyCount = targetRenderers == null ? 0 : Mathf.Min(targetRenderers.Length, skinMaterialSet.materials.Length);
        for (int i = 0; i < applyCount; i++)
        {
            ApplyMaterialsToRenderer(targetRenderers[i], skinMaterialSet.materials[i]);
        }

        _currentCharacterId = normalizedCharacterId;
        LogSkin($"스킨 적용: {normalizedCharacterId} {skinMaterialSet.skinName}");
    }

    private void ApplyMaterialsToRenderer(Renderer targetRenderer, Material sourceMaterial)
    {
        if (targetRenderer == null || sourceMaterial == null)
            return;

        Material[] currentMaterials = useSharedMaterials ? targetRenderer.sharedMaterials : targetRenderer.materials;
        Material[] nextMaterials;

        if (currentMaterials == null || currentMaterials.Length == 0)
        {
            nextMaterials = new[] { sourceMaterial };
        }
        else
        {
            nextMaterials = CopyMaterials(currentMaterials);
            nextMaterials[0] = sourceMaterial;
        }

        if (useSharedMaterials)
            targetRenderer.sharedMaterials = nextMaterials;
        else
            targetRenderer.materials = nextMaterials;
    }

    private static Material[] CopyMaterials(Material[] sourceMaterials)
    {
        if (sourceMaterials == null || sourceMaterials.Length == 0)
            return Array.Empty<Material>();

        Material[] copy = new Material[sourceMaterials.Length];
        Array.Copy(sourceMaterials, copy, sourceMaterials.Length);
        return copy;
    }

    private void LogSkin(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log($"[PlayerCharacterSkin] {message}", this);
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
