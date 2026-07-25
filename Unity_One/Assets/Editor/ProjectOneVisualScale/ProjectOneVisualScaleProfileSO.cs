using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemScaleMode
{
    RelativeToCharacter = 0,
    FixedFinalScale = 1
}

[Serializable]
public sealed class ItemScaleRule
{
    [SerializeField] private string ruleId;
    [SerializeField] private string displayName;
    [SerializeField] private bool enabled = true;

    [SerializeField] private GameObject worldPrefab;
    [SerializeField] private string worldVisualTransformPath;

    [SerializeField] private GameObject equippedPrefab;
    [SerializeField] private string equippedVisualTransformPath;

    [SerializeField] private ItemScaleMode mode;
    [SerializeField] private float ratioToCharacter = 1f;
    [SerializeField] private float fixedFinalScale = 1f;
    [SerializeField] private float maximumFinalScale;

    [SerializeField, HideInInspector]
    private Vector3 referenceWorldVisualLocalScale;

    [SerializeField, HideInInspector]
    private Vector3 referenceEquippedVisualLocalScale;

    [SerializeField, HideInInspector]
    private Vector3 capturedWorldVisualLocalScale;

    [SerializeField, HideInInspector]
    private Vector3 capturedEquippedVisualLocalScale;

    public string RuleId => ruleId;
    public string DisplayName => displayName;
    public bool Enabled => enabled;
    public GameObject WorldPrefab => worldPrefab;
    public string WorldVisualTransformPath => worldVisualTransformPath;
    public GameObject EquippedPrefab => equippedPrefab;
    public string EquippedVisualTransformPath => equippedVisualTransformPath;
    public ItemScaleMode Mode => mode;
    public float RatioToCharacter => ratioToCharacter;
    public float FixedFinalScale => fixedFinalScale;
    public float MaximumFinalScale => maximumFinalScale;
    public Vector3 ReferenceWorldVisualLocalScale => referenceWorldVisualLocalScale;
    public Vector3 ReferenceEquippedVisualLocalScale => referenceEquippedVisualLocalScale;
    public Vector3 CapturedWorldVisualLocalScale => capturedWorldVisualLocalScale;
    public Vector3 CapturedEquippedVisualLocalScale => capturedEquippedVisualLocalScale;
}

[Serializable]
public sealed class ItemPresetValue
{
    [SerializeField] private string ruleId;
    [SerializeField] private ItemScaleMode mode;
    [SerializeField] private float ratioToCharacter = 1f;
    [SerializeField] private float fixedFinalScale = 1f;
    [SerializeField] private float maximumFinalScale;

    public string RuleId => ruleId;
    public ItemScaleMode Mode => mode;
    public float RatioToCharacter => ratioToCharacter;
    public float FixedFinalScale => fixedFinalScale;
    public float MaximumFinalScale => maximumFinalScale;
}

[Serializable]
public sealed class VisualScalePreset
{
    [SerializeField] private string presetId;
    [SerializeField] private string displayName;
    [SerializeField] private float characterVisualScale = 1f;
    [SerializeField] private float bodyPostItRatioToCharacter = 1f;
    [SerializeField] private float worldPostItRatioToCharacter = 1f;
    [SerializeField] private ItemPresetValue[] itemValues = Array.Empty<ItemPresetValue>();

    public string PresetId => presetId;
    public string DisplayName => displayName;
    public float CharacterVisualScale => characterVisualScale;
    public float BodyPostItRatioToCharacter => bodyPostItRatioToCharacter;
    public float WorldPostItRatioToCharacter => worldPostItRatioToCharacter;
    public IReadOnlyList<ItemPresetValue> ItemValues => itemValues;
}

[CreateAssetMenu(
    menuName = "Triad Canvas/Project One/Visual Scale Profile",
    fileName = "ProjectOneVisualScaleProfile")]
public sealed class ProjectOneVisualScaleProfileSO : ScriptableObject
{
    [Header("Production Bindings")]
    [SerializeField] private GameObject productionPlayerPrefab;
    [SerializeField] private string characterVisualTransformPath;
    [SerializeField] private string presenterSerializedPropertyName =
        "bodyVisualScaleMultiplier";
    [SerializeField] private string worldPresenterSerializedPropertyName =
        "worldVisualScaleMultiplier";
    [SerializeField] private List<ItemScaleRule> itemRules =
        new List<ItemScaleRule>();

    [Header("Master Visual Scale")]
    [SerializeField] private float characterVisualScale = 1.25f;
    [SerializeField] private float bodyPostItRatioToCharacter = 1.08f;
    [SerializeField] private float worldPostItRatioToCharacter = 1.04f;

    [Header("Presets")]
    [SerializeField] private VisualScalePreset presetA;
    [SerializeField] private VisualScalePreset presetB;
    [SerializeField] private VisualScalePreset presetC;

    [Header("Captured Baseline")]
    [SerializeField, HideInInspector] private bool baselineCaptured;
    [SerializeField, HideInInspector] private string baselineSourceHead;
    [SerializeField, HideInInspector]
    private Vector3 referenceCharacterVisualLocalScale;
    [SerializeField, HideInInspector]
    private float referenceBodyPostItMultiplier;
    [SerializeField, HideInInspector]
    private float referenceWorldPostItMultiplier;
    [SerializeField, HideInInspector]
    private Vector3 capturedCharacterVisualLocalScale;
    [SerializeField, HideInInspector]
    private float capturedBodyPostItMultiplier;
    [SerializeField, HideInInspector]
    private float capturedWorldPostItMultiplier;
    [SerializeField, HideInInspector]
    private float capturedCharacterVisualScale;
    [SerializeField, HideInInspector]
    private float capturedBodyPostItRatioToCharacter;
    [SerializeField, HideInInspector]
    private float capturedWorldPostItRatioToCharacter;

    public GameObject ProductionPlayerPrefab => productionPlayerPrefab;
    public string CharacterVisualTransformPath => characterVisualTransformPath;
    public string PresenterSerializedPropertyName =>
        presenterSerializedPropertyName;
    public string WorldPresenterSerializedPropertyName =>
        worldPresenterSerializedPropertyName;
    public IReadOnlyList<ItemScaleRule> ItemRules => itemRules;

    public float CharacterVisualScale => characterVisualScale;
    public float BodyPostItRatioToCharacter =>
        bodyPostItRatioToCharacter;
    public float WorldPostItRatioToCharacter =>
        worldPostItRatioToCharacter;

    public VisualScalePreset PresetA => presetA;
    public VisualScalePreset PresetB => presetB;
    public VisualScalePreset PresetC => presetC;

    public bool BaselineCaptured => baselineCaptured;
    public string BaselineSourceHead => baselineSourceHead;
    public Vector3 ReferenceCharacterVisualLocalScale =>
        referenceCharacterVisualLocalScale;
    public float ReferenceBodyPostItMultiplier =>
        referenceBodyPostItMultiplier;
    public float ReferenceWorldPostItMultiplier =>
        referenceWorldPostItMultiplier;
    public Vector3 CapturedCharacterVisualLocalScale =>
        capturedCharacterVisualLocalScale;
    public float CapturedBodyPostItMultiplier =>
        capturedBodyPostItMultiplier;
    public float CapturedWorldPostItMultiplier =>
        capturedWorldPostItMultiplier;
    public float CapturedCharacterVisualScale =>
        capturedCharacterVisualScale;
    public float CapturedBodyPostItRatioToCharacter =>
        capturedBodyPostItRatioToCharacter;
    public float CapturedWorldPostItRatioToCharacter =>
        capturedWorldPostItRatioToCharacter;
}
