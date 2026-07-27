using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class CharacterPortraitLiveRenderer : MonoBehaviour
{
    private const string ExpectedModelRootName = "슈가";
    private const string ExpectedVisualRootName = "VisualPreviewRoot";
    private const string ExpectedBodyRootName = "MotorShellBody";
    private const string ExpectedWingRendererPath = "날개";
    private const string ExpectedBodyRendererPath = "몸통";
    private const string ExpectedFaceRendererPath = "평면";
    private const int ExpectedRendererCount = 3;
    private const int FaceMaterialIndex = 0;
    private const int MinimumNetworkFaceExpressionId = 0;
    private const int MaximumNetworkFaceExpressionId = 4;
    private const int PortraitLayer = 5;
    private const uint PortraitRenderingLayerMask = 1u << 31;
    private const int PortraitLightRenderingLayerMask =
        unchecked((int)(1u << 31));
    private const int RenderTextureWidth = 336;
    private const int RenderTextureHeight = 256;
    private const int RenderTextureDepth = 24;
    private const int RenderTextureAntiAliasing = 2;
    private const float MaximumPresentationOffset = 0.25f;
    private const float MinimumOrthographicSize = 0.62f;
    private const float MaximumOrthographicSize = 0.9f;
    private const float BoundsMargin = 1.15f;
    private const float CameraDistance = 2.5f;
    private const float CameraHeightOffset = 0.12f;
    private const float FacingEpsilon = 0.0001f;

    private static readonly Vector3 StagePosition =
        new Vector3(10000f, 10000f, 10000f);
    private static readonly int BaseMap =
        Shader.PropertyToID("_BaseMap");
    private static readonly int MainTex =
        Shader.PropertyToID("_MainTex");

    private struct PosePair
    {
        public Transform Source;
        public Transform Clone;

        public PosePair(Transform source, Transform clone)
        {
            Source = source;
            Clone = clone;
        }
    }

    private struct RendererPair
    {
        public SkinnedMeshRenderer Source;
        public SkinnedMeshRenderer Clone;
        public int BlendShapeCount;

        public RendererPair(
            SkinnedMeshRenderer source,
            SkinnedMeshRenderer clone)
        {
            Source = source;
            Clone = clone;
            BlendShapeCount =
                source != null && source.sharedMesh != null
                    ? source.sharedMesh.blendShapeCount
                    : 0;
        }
    }

    private NetworkObject _boundPlayerObject;
    private Transform _sourceBodyRoot;
    private Transform _sourceVisualRoot;
    private Transform _sourceModelRoot;
    private FaceExpressionController _sourceFaceController;
    private HamsterFullRagdollMotor _sourceMotor;
    private SkinnedMeshRenderer _sourceFaceRenderer;
    private Material _sourceFaceMaterial;
    private Material _cloneFaceMaterial;

    private GameObject _stageRoot;
    private Transform _motionRoot;
    private Camera _portraitCamera;
    private RenderTexture _outputTexture;
    private PosePair[] _posePairs = Array.Empty<PosePair>();
    private RendererPair[] _rendererPairs =
        Array.Empty<RendererPair>();
    private Material[] _ownedMaterials = Array.Empty<Material>();

    private Vector3 _sourceVisualBindLocalPosition;
    private Vector3 _focusPointInVisualLocalSpace;
    private Vector3 _lastValidPlanarFacing = Vector3.forward;
    private float _orthographicSize = MinimumOrthographicSize;
    private bool _isBound;
    private bool _renderingRequested;
    private bool _renderCallbackSubscribed;
    private bool _debugLogs;
    private int _lastPoseCopyFrame = -1;
    private int _lastAppliedFaceExpressionId = -1;
    private string _lastFailureReason = string.Empty;

    public bool IsReady =>
        _isBound &&
        IsExactLocalOwnerBindingValid() &&
        _sourceVisualRoot != null &&
        _sourceModelRoot != null &&
        _sourceFaceController != null &&
        _motionRoot != null &&
        _portraitCamera != null &&
        _outputTexture != null &&
        _outputTexture.IsCreated() &&
        _posePairs.Length > 0 &&
        _rendererPairs.Length == ExpectedRendererCount;

    public RenderTexture OutputTexture => _outputTexture;
    public NetworkObject BoundPlayerObject => _boundPlayerObject;
    public int SourceRendererCount => _rendererPairs.Length;
    public int MappedTransformCount => _posePairs.Length;
    public bool IsRenderingActive =>
        _portraitCamera != null && _portraitCamera.enabled;
    public string LastFailureReason => _lastFailureReason;

    private void OnEnable()
    {
        SubscribeRenderCallback();
        ApplyRenderingState();
    }

    private void OnDisable()
    {
        UnsubscribeRenderCallback();

        if (_portraitCamera != null)
            _portraitCamera.enabled = false;
    }

    private void OnDestroy()
    {
        Unbind();
        DestroyRenderResources();
        UnsubscribeRenderCallback();
    }

    public void SetDebugLogging(bool enabled)
    {
        _debugLogs = enabled;
    }

    public bool Bind(
        NetworkObject playerObject,
        Transform visualRoot,
        Transform modelRoot,
        FaceExpressionController faceController)
    {
        if (IsSameBinding(
                playerObject,
                visualRoot,
                modelRoot,
                faceController) &&
            IsReady)
        {
            ApplyRenderingState();
            return true;
        }

        Unbind();
        _lastFailureReason = string.Empty;

        if (!TryValidateSource(
                playerObject,
                visualRoot,
                modelRoot,
                faceController,
                out Transform bodyRoot,
                out SkinnedMeshRenderer[] sourceRenderers,
                out SkinnedMeshRenderer faceRenderer,
                out string failureReason))
        {
            return FailBind(failureReason);
        }

        if (!EnsureRenderResources(out failureReason))
            return FailBind(failureReason);

        _boundPlayerObject = playerObject;
        _sourceBodyRoot = bodyRoot;
        _sourceVisualRoot = visualRoot;
        _sourceModelRoot = modelRoot;
        _sourceFaceController = faceController;
        _sourceFaceRenderer = faceRenderer;
        _sourceMotor =
            playerObject.GetComponentInChildren<
                HamsterFullRagdollMotor>(true);
        _sourceVisualBindLocalPosition = visualRoot.localPosition;
        _lastValidPlanarFacing =
            ResolveInitialPlanarFacing(playerObject, visualRoot);

        if (!TryBuildRenderOnlyClone(
                sourceRenderers,
                out failureReason))
        {
            Unbind();
            return FailBind(failureReason);
        }

        ResolveFixedFraming(sourceRenderers);
        _sourceFaceController.ExpressionChanged +=
            HandleSourceExpressionChanged;

        int currentExpressionId =
            _sourceFaceController.CurrentExpressionId;
        if (IsSupportedFaceExpression(currentExpressionId))
        {
            if (!TryApplyFaceExpression(
                    currentExpressionId,
                    out failureReason))
            {
                Unbind();
                return FailBind(failureReason);
            }
        }
        else
        {
            _lastAppliedFaceExpressionId =
                MinimumNetworkFaceExpressionId;
        }

        _isBound = true;
        _lastPoseCopyFrame = -1;
        CopyFinalSourcePose();
        ApplyRenderingState();

        Log(
            $"Bound player={playerObject.NetworkObjectId}, " +
            $"renderers={_rendererPairs.Length}, " +
            $"transforms={_posePairs.Length}, " +
            $"face={_lastAppliedFaceExpressionId}, " +
            $"rt={RenderTextureWidth}x{RenderTextureHeight}.");
        return true;
    }

    public void Unbind()
    {
        if (_sourceFaceController != null)
        {
            _sourceFaceController.ExpressionChanged -=
                HandleSourceExpressionChanged;
        }

        if (_portraitCamera != null)
            _portraitCamera.enabled = false;

        DestroyClone();

        _boundPlayerObject = null;
        _sourceBodyRoot = null;
        _sourceVisualRoot = null;
        _sourceModelRoot = null;
        _sourceFaceController = null;
        _sourceMotor = null;
        _sourceFaceRenderer = null;
        _sourceFaceMaterial = null;
        _isBound = false;
        _lastPoseCopyFrame = -1;
        _lastAppliedFaceExpressionId = -1;
    }

    public void SetRenderingActive(bool active)
    {
        _renderingRequested = active;
        ApplyRenderingState();
    }

    private bool IsSameBinding(
        NetworkObject playerObject,
        Transform visualRoot,
        Transform modelRoot,
        FaceExpressionController faceController)
    {
        return _isBound &&
               _boundPlayerObject == playerObject &&
               _sourceVisualRoot == visualRoot &&
               _sourceModelRoot == modelRoot &&
               _sourceFaceController == faceController;
    }

    private static bool TryValidateSource(
        NetworkObject playerObject,
        Transform visualRoot,
        Transform modelRoot,
        FaceExpressionController faceController,
        out Transform bodyRoot,
        out SkinnedMeshRenderer[] sourceRenderers,
        out SkinnedMeshRenderer faceRenderer,
        out string failureReason)
    {
        bodyRoot = null;
        sourceRenderers = null;
        faceRenderer = null;
        failureReason = string.Empty;

        if (playerObject == null || !playerObject.IsSpawned)
        {
            failureReason =
                "Local Player NetworkObject is missing or not spawned.";
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        NetworkClient localClient =
            networkManager != null ? networkManager.LocalClient : null;
        if (networkManager == null ||
            !networkManager.IsListening ||
            localClient == null ||
            localClient.PlayerObject != playerObject ||
            playerObject.OwnerClientId != networkManager.LocalClientId)
        {
            failureReason =
                "PlayerObject is not the exact LocalClient owner.";
            return false;
        }

        if (visualRoot == null ||
            visualRoot.name != ExpectedVisualRootName ||
            !visualRoot.gameObject.activeInHierarchy)
        {
            failureReason =
                "Active VisualPreviewRoot could not be validated.";
            return false;
        }

        if (modelRoot == null ||
            modelRoot.name != ExpectedModelRootName ||
            !modelRoot.gameObject.activeInHierarchy ||
            !modelRoot.IsChildOf(visualRoot))
        {
            failureReason =
                "Active single-Sugar model root could not be validated.";
            return false;
        }

        if (faceController == null ||
            !faceController.isActiveAndEnabled ||
            faceController.transform != modelRoot)
        {
            failureReason =
                "Active FaceExpressionController is not on Sugar root.";
            return false;
        }

        bodyRoot = visualRoot.parent;
        if (bodyRoot == null ||
            bodyRoot.name != ExpectedBodyRootName)
        {
            failureReason =
                "VisualPreviewRoot is not under MotorShellBody.";
            return false;
        }

        if (!IsUnderExactPlayerRoot(playerObject, visualRoot) ||
            !IsUnderExactPlayerRoot(playerObject, modelRoot) ||
            !IsUnderExactPlayerRoot(playerObject, faceController.transform))
        {
            failureReason =
                "Visual source does not belong to the bound PlayerObject.";
            return false;
        }

        sourceRenderers =
            modelRoot.GetComponentsInChildren<
                SkinnedMeshRenderer>(true);
        if (sourceRenderers.Length != ExpectedRendererCount)
        {
            failureReason =
                $"Expected {ExpectedRendererCount} Sugar renderers, " +
                $"found {sourceRenderers.Length}.";
            return false;
        }

        bool foundWing = false;
        bool foundBody = false;
        bool foundFace = false;

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = sourceRenderers[i];
            if (renderer == null ||
                renderer.sharedMesh == null ||
                !renderer.gameObject.activeInHierarchy)
            {
                failureReason =
                    "A required Sugar renderer is inactive or has no mesh.";
                return false;
            }

            string path = GetRelativePath(modelRoot, renderer.transform);
            switch (path)
            {
                case ExpectedWingRendererPath:
                    foundWing = true;
                    break;
                case ExpectedBodyRendererPath:
                    foundBody = true;
                    break;
                case ExpectedFaceRendererPath:
                    if (foundFace)
                    {
                        failureReason =
                            "More than one Sugar face renderer was found.";
                        return false;
                    }

                    foundFace = true;
                    faceRenderer = renderer;
                    break;
                default:
                    failureReason =
                        $"Unexpected Sugar renderer path '{path}'.";
                    return false;
            }
        }

        if (!foundWing || !foundBody || !foundFace)
        {
            failureReason =
                "Sugar renderer whitelist is incomplete.";
            return false;
        }

        Material[] faceMaterials = faceRenderer.sharedMaterials;
        if (faceMaterials == null ||
            faceMaterials.Length <= FaceMaterialIndex ||
            faceMaterials[FaceMaterialIndex] == null)
        {
            failureReason =
                "Sugar face material slot 0 is invalid.";
            return false;
        }

        if (!IsSupportedFaceExpression(
                faceController.CurrentExpressionId))
        {
            failureReason =
                $"Initial Face{faceController.CurrentExpressionId} " +
                "is outside the approved Face0-4 range.";
            return false;
        }

        return true;
    }

    private static bool IsUnderExactPlayerRoot(
        NetworkObject playerObject,
        Transform source)
    {
        return playerObject != null &&
               source != null &&
               source.GetComponentInParent<NetworkObject>() ==
               playerObject;
    }

    private bool EnsureRenderResources(out string failureReason)
    {
        failureReason = string.Empty;

        if (_stageRoot == null)
            CreateStage();

        if (_outputTexture == null)
        {
            _outputTexture = new RenderTexture(
                RenderTextureWidth,
                RenderTextureHeight,
                RenderTextureDepth,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = "LocalCharacterPortrait_RT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = RenderTextureAntiAliasing,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                useDynamicScale = false
            };

            if (!_outputTexture.Create())
            {
                failureReason =
                    "Runtime portrait RenderTexture creation failed.";
                DestroyRuntimeObject(_outputTexture);
                _outputTexture = null;
                return false;
            }
        }

        if (_portraitCamera == null)
            CreatePortraitCamera();

        if (_portraitCamera == null)
        {
            failureReason =
                "Runtime portrait Camera creation failed.";
            return false;
        }

        _portraitCamera.targetTexture = _outputTexture;
        return true;
    }

    private void CreateStage()
    {
        _stageRoot =
            new GameObject("[Local Character Portrait Stage]");
        ConfigureRuntimeObject(_stageRoot);
        _stageRoot.transform.SetPositionAndRotation(
            StagePosition,
            Quaternion.identity);
        _stageRoot.transform.localScale = Vector3.one;

        CreatePortraitLight(
            "Portrait Key Light",
            Quaternion.Euler(35f, -35f, 0f),
            new Color(1f, 0.93f, 0.84f),
            1.1f);
        CreatePortraitLight(
            "Portrait Fill Light",
            Quaternion.Euler(20f, 145f, 0f),
            new Color(0.72f, 0.84f, 1f),
            0.45f);
    }

    private void CreatePortraitCamera()
    {
        GameObject cameraObject =
            new GameObject("Local Character Portrait Camera");
        ConfigureRuntimeObject(cameraObject);
        cameraObject.transform.SetParent(
            _stageRoot.transform,
            false);

        _portraitCamera = cameraObject.AddComponent<Camera>();
        _portraitCamera.enabled = false;
        _portraitCamera.orthographic = true;
        _portraitCamera.orthographicSize = MinimumOrthographicSize;
        _portraitCamera.aspect =
            (float)RenderTextureWidth / RenderTextureHeight;
        _portraitCamera.clearFlags = CameraClearFlags.SolidColor;
        _portraitCamera.backgroundColor = Color.clear;
        _portraitCamera.cullingMask = 1 << PortraitLayer;
        _portraitCamera.nearClipPlane = 0.01f;
        _portraitCamera.farClipPlane = 10f;
        _portraitCamera.depth = -100f;
        _portraitCamera.allowHDR = false;
        _portraitCamera.allowMSAA = true;
        _portraitCamera.allowDynamicResolution = false;
        _portraitCamera.useOcclusionCulling = false;

        UniversalAdditionalCameraData cameraData =
            cameraObject.AddComponent<
                UniversalAdditionalCameraData>();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.requiresColorOption =
            CameraOverrideOption.Off;
        cameraData.requiresDepthOption =
            CameraOverrideOption.Off;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.allowXRRendering = false;
    }

    private void CreatePortraitLight(
        string objectName,
        Quaternion rotation,
        Color color,
        float intensity)
    {
        GameObject lightObject = new GameObject(objectName);
        ConfigureRuntimeObject(lightObject);
        lightObject.transform.SetParent(
            _stageRoot.transform,
            false);
        lightObject.transform.rotation = rotation;

        Light portraitLight = lightObject.AddComponent<Light>();
        portraitLight.type = LightType.Directional;
        portraitLight.color = color;
        portraitLight.intensity = intensity;
        portraitLight.shadows = LightShadows.None;
        portraitLight.cullingMask = 1 << PortraitLayer;
        portraitLight.renderingLayerMask =
            PortraitLightRenderingLayerMask;
    }

    private bool TryBuildRenderOnlyClone(
        SkinnedMeshRenderer[] sourceRenderers,
        out string failureReason)
    {
        failureReason = string.Empty;
        DestroyClone();

        HashSet<Transform> requiredTransforms =
            new HashSet<Transform>();
        requiredTransforms.Add(_sourceModelRoot);

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = sourceRenderers[i];
            if (!TryAddRequiredTransformChain(
                    renderer.transform,
                    requiredTransforms) ||
                !TryAddRequiredTransformChain(
                    renderer.rootBone,
                    requiredTransforms))
            {
                failureReason =
                    "A renderer transform or root bone is outside Sugar.";
                return false;
            }

            Transform[] bones = renderer.bones;
            if (bones == null || bones.Length == 0)
            {
                failureReason =
                    $"Renderer '{renderer.name}' has no bone mapping.";
                return false;
            }

            for (int boneIndex = 0;
                 boneIndex < bones.Length;
                 boneIndex++)
            {
                if (bones[boneIndex] == null ||
                    !TryAddRequiredTransformChain(
                        bones[boneIndex],
                        requiredTransforms))
                {
                    failureReason =
                        $"Renderer '{renderer.name}' has an invalid bone.";
                    return false;
                }
            }
        }

        GameObject motionObject =
            new GameObject("Portrait Motion Root");
        ConfigureRuntimeObject(motionObject);
        motionObject.transform.SetParent(
            _stageRoot.transform,
            false);
        _motionRoot = motionObject.transform;

        Transform[] sourceHierarchy =
            _sourceModelRoot.GetComponentsInChildren<
                Transform>(true);
        Dictionary<Transform, Transform> cloneBySource =
            new Dictionary<Transform, Transform>(
                requiredTransforms.Count);
        List<PosePair> posePairs =
            new List<PosePair>(requiredTransforms.Count);

        for (int i = 0; i < sourceHierarchy.Length; i++)
        {
            Transform source = sourceHierarchy[i];
            if (!requiredTransforms.Contains(source))
                continue;

            Transform cloneParent;
            if (source == _sourceModelRoot)
            {
                cloneParent = _motionRoot;
            }
            else if (!cloneBySource.TryGetValue(
                         source.parent,
                         out cloneParent))
            {
                failureReason =
                    $"Clone parent missing for '{source.name}'.";
                DestroyClone();
                return false;
            }

            GameObject cloneObject =
                new GameObject(source.name);
            ConfigureRuntimeObject(cloneObject);
            cloneObject.transform.SetParent(cloneParent, false);
            CopyLocalTransform(source, cloneObject.transform);
            cloneBySource.Add(source, cloneObject.transform);
            posePairs.Add(
                new PosePair(source, cloneObject.transform));
        }

        if (cloneBySource.Count != requiredTransforms.Count)
        {
            failureReason =
                $"Pose mapping mismatch: source=" +
                $"{requiredTransforms.Count}, " +
                $"clone={cloneBySource.Count}.";
            DestroyClone();
            return false;
        }

        List<RendererPair> rendererPairs =
            new List<RendererPair>(sourceRenderers.Length);
        List<Material> ownedMaterials =
            new List<Material>(1);

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            SkinnedMeshRenderer sourceRenderer =
                sourceRenderers[i];
            if (!cloneBySource.TryGetValue(
                    sourceRenderer.transform,
                    out Transform cloneTransform))
            {
                failureReason =
                    $"Renderer transform mapping failed for " +
                    $"'{sourceRenderer.name}'.";
                DestroyOwnedMaterialList(ownedMaterials);
                DestroyClone();
                return false;
            }

            SkinnedMeshRenderer cloneRenderer =
                cloneTransform.gameObject.AddComponent<
                    SkinnedMeshRenderer>();
            if (!TryConfigureCloneRenderer(
                    sourceRenderer,
                    cloneRenderer,
                    cloneBySource,
                    ownedMaterials,
                    out failureReason))
            {
                DestroyOwnedMaterialList(ownedMaterials);
                DestroyClone();
                return false;
            }

            rendererPairs.Add(
                new RendererPair(
                    sourceRenderer,
                    cloneRenderer));
        }

        _posePairs = posePairs.ToArray();
        _rendererPairs = rendererPairs.ToArray();
        _ownedMaterials = ownedMaterials.ToArray();
        _motionRoot.gameObject.SetActive(false);
        return true;
    }

    private bool TryConfigureCloneRenderer(
        SkinnedMeshRenderer source,
        SkinnedMeshRenderer clone,
        Dictionary<Transform, Transform> cloneBySource,
        List<Material> ownedMaterials,
        out string failureReason)
    {
        failureReason = string.Empty;

        Transform[] sourceBones = source.bones;
        Transform[] cloneBones =
            new Transform[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
            if (!cloneBySource.TryGetValue(
                    sourceBones[i],
                    out cloneBones[i]))
            {
                failureReason =
                    $"Bone mapping failed for '{source.name}'.";
                return false;
            }
        }

        if (!cloneBySource.TryGetValue(
                source.rootBone,
                out Transform cloneRootBone))
        {
            failureReason =
                $"Root bone mapping failed for '{source.name}'.";
            return false;
        }

        clone.sharedMesh = source.sharedMesh;
        clone.rootBone = cloneRootBone;
        clone.bones = cloneBones;
        clone.localBounds = source.localBounds;
        clone.quality = source.quality;
        clone.updateWhenOffscreen = true;
        clone.enabled = source.enabled;
        clone.shadowCastingMode = ShadowCastingMode.Off;
        clone.receiveShadows = false;
        clone.lightProbeUsage = LightProbeUsage.Off;
        clone.reflectionProbeUsage = ReflectionProbeUsage.Off;
        clone.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;
        clone.allowOcclusionWhenDynamic = false;
        clone.renderingLayerMask =
            PortraitRenderingLayerMask;

        Material[] cloneMaterials = source.sharedMaterials;
        string rendererPath =
            GetRelativePath(_sourceModelRoot, source.transform);
        if (rendererPath == ExpectedFaceRendererPath)
        {
            Material[] sourceLiveMaterials = source.materials;
            if (sourceLiveMaterials.Length <= FaceMaterialIndex ||
                sourceLiveMaterials[FaceMaterialIndex] == null)
            {
                failureReason =
                    "Live Sugar face material slot 0 is invalid.";
                return false;
            }

            _sourceFaceMaterial =
                sourceLiveMaterials[FaceMaterialIndex];
            _cloneFaceMaterial =
                new Material(_sourceFaceMaterial)
                {
                    name =
                        $"{_sourceFaceMaterial.name} " +
                        "(Local Portrait)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            cloneMaterials[FaceMaterialIndex] =
                _cloneFaceMaterial;
            ownedMaterials.Add(_cloneFaceMaterial);
        }

        clone.sharedMaterials = cloneMaterials;
        return true;
    }

    private bool TryAddRequiredTransformChain(
        Transform source,
        HashSet<Transform> requiredTransforms)
    {
        Transform current = source;
        while (current != null)
        {
            requiredTransforms.Add(current);
            if (current == _sourceModelRoot)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void ResolveFixedFraming(
        SkinnedMeshRenderer[] sourceRenderers)
    {
        Bounds combinedBounds = sourceRenderers[0].bounds;
        for (int i = 1; i < sourceRenderers.Length; i++)
            combinedBounds.Encapsulate(sourceRenderers[i].bounds);

        _focusPointInVisualLocalSpace =
            _sourceVisualRoot.InverseTransformPoint(
                combinedBounds.center);
        float radiusInVisualSpace =
            _sourceVisualRoot.InverseTransformVector(
                combinedBounds.extents).magnitude;
        _orthographicSize = Mathf.Clamp(
            radiusInVisualSpace * BoundsMargin,
            MinimumOrthographicSize,
            MaximumOrthographicSize);
        _portraitCamera.orthographicSize = _orthographicSize;
    }

    private void SubscribeRenderCallback()
    {
        if (_renderCallbackSubscribed)
            return;

        RenderPipelineManager.beginCameraRendering +=
            HandleBeginCameraRendering;
        _renderCallbackSubscribed = true;
    }

    private void UnsubscribeRenderCallback()
    {
        if (!_renderCallbackSubscribed)
            return;

        RenderPipelineManager.beginCameraRendering -=
            HandleBeginCameraRendering;
        _renderCallbackSubscribed = false;
    }

    private void HandleBeginCameraRendering(
        ScriptableRenderContext context,
        Camera renderingCamera)
    {
        if (renderingCamera != _portraitCamera ||
            _lastPoseCopyFrame == Time.frameCount)
        {
            return;
        }

        _lastPoseCopyFrame = Time.frameCount;
        CopyFinalSourcePose();
    }

    private void CopyFinalSourcePose()
    {
        if (!_isBound ||
            !IsExactLocalOwnerBindingValid() ||
            _sourceBodyRoot == null ||
            _sourceVisualRoot == null ||
            _motionRoot == null)
        {
            if (_portraitCamera != null)
                _portraitCamera.enabled = false;
            return;
        }

        Vector3 baselineVisualPosition =
            _sourceBodyRoot.TransformPoint(
                _sourceVisualBindLocalPosition);
        Vector3 presentationDelta =
            _sourceVisualRoot.position - baselineVisualPosition;
        if (!IsFiniteVector(presentationDelta))
            presentationDelta = Vector3.zero;

        presentationDelta =
            Vector3.ClampMagnitude(
                presentationDelta,
                MaximumPresentationOffset);

        _motionRoot.position =
            _stageRoot.transform.position + presentationDelta;
        _motionRoot.rotation = _sourceVisualRoot.rotation;
        _motionRoot.localScale =
            SanitizeScale(_sourceVisualRoot.lossyScale);

        for (int i = 0; i < _posePairs.Length; i++)
        {
            PosePair pair = _posePairs[i];
            if (pair.Source == null || pair.Clone == null)
                continue;

            CopyLocalTransform(pair.Source, pair.Clone);
        }

        for (int i = 0; i < _rendererPairs.Length; i++)
        {
            RendererPair pair = _rendererPairs[i];
            if (pair.Source == null || pair.Clone == null)
                continue;

            pair.Clone.enabled =
                pair.Source.enabled &&
                pair.Source.gameObject.activeInHierarchy;

            for (int blendShapeIndex = 0;
                 blendShapeIndex < pair.BlendShapeCount;
                 blendShapeIndex++)
            {
                pair.Clone.SetBlendShapeWeight(
                    blendShapeIndex,
                    pair.Source.GetBlendShapeWeight(
                        blendShapeIndex));
            }
        }

        UpdateCameraPose();
    }

    private void UpdateCameraPose()
    {
        Vector3 planarFacing = ResolvePlanarFacing();
        Vector3 focus =
            _motionRoot.TransformPoint(
                _focusPointInVisualLocalSpace);
        Vector3 cameraPosition =
            focus +
            planarFacing * CameraDistance +
            Vector3.up * CameraHeightOffset;
        Vector3 lookDirection = focus - cameraPosition;

        if (!IsFiniteVector(cameraPosition) ||
            !IsFiniteVector(lookDirection) ||
            lookDirection.sqrMagnitude <= FacingEpsilon)
        {
            return;
        }

        _portraitCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up));
        _portraitCamera.orthographicSize = _orthographicSize;
    }

    private Vector3 ResolveInitialPlanarFacing(
        NetworkObject playerObject,
        Transform visualRoot)
    {
        HamsterFullRagdollMotor motor =
            playerObject.GetComponentInChildren<
                HamsterFullRagdollMotor>(true);
        if (TryNormalizePlanar(
                motor != null
                    ? motor.DesiredFacingDirection
                    : Vector3.zero,
                out Vector3 facing) ||
            TryNormalizePlanar(
                motor != null
                    ? motor.SmoothedMoveWorldDirection
                    : Vector3.zero,
                out facing) ||
            TryNormalizePlanar(visualRoot.forward, out facing) ||
            TryNormalizePlanar(playerObject.transform.forward, out facing))
        {
            return facing;
        }

        return Vector3.forward;
    }

    private Vector3 ResolvePlanarFacing()
    {
        if (_sourceMotor != null)
        {
            if (TryNormalizePlanar(
                    _sourceMotor.DesiredFacingDirection,
                    out Vector3 facing) ||
                TryNormalizePlanar(
                    _sourceMotor.SmoothedMoveWorldDirection,
                    out facing))
            {
                _lastValidPlanarFacing = facing;
                return facing;
            }
        }

        if (TryNormalizePlanar(
                _lastValidPlanarFacing,
                out Vector3 lastFacing))
        {
            return lastFacing;
        }

        if (TryNormalizePlanar(
                _sourceVisualRoot != null
                    ? _sourceVisualRoot.forward
                    : Vector3.zero,
                out Vector3 visualFacing))
        {
            _lastValidPlanarFacing = visualFacing;
            return visualFacing;
        }

        if (TryNormalizePlanar(
                _boundPlayerObject != null
                    ? _boundPlayerObject.transform.forward
                    : Vector3.forward,
                out Vector3 playerFacing))
        {
            _lastValidPlanarFacing = playerFacing;
            return playerFacing;
        }

        return Vector3.forward;
    }

    private void HandleSourceExpressionChanged(int expressionId)
    {
        if (!IsSupportedFaceExpression(expressionId))
        {
            Log(
                $"Ignored out-of-scope Face{expressionId}; " +
                "portrait keeps the last Face0-4 state.");
            return;
        }

        if (!TryApplyFaceExpression(
                expressionId,
                out string failureReason))
        {
            _lastFailureReason = failureReason;
            Debug.LogWarning(
                $"[CharacterPortraitLiveRenderer] {failureReason}",
                this);
        }
    }

    private bool TryApplyFaceExpression(
        int expressionId,
        out string failureReason)
    {
        failureReason = string.Empty;

        if (_sourceFaceRenderer == null ||
            _cloneFaceMaterial == null)
        {
            failureReason =
                "Face renderer or clone material is missing.";
            return false;
        }

        Material[] liveMaterials = _sourceFaceRenderer.materials;
        if (liveMaterials.Length <= FaceMaterialIndex ||
            liveMaterials[FaceMaterialIndex] == null)
        {
            failureReason =
                "Live face material slot 0 became invalid.";
            return false;
        }

        _sourceFaceMaterial =
            liveMaterials[FaceMaterialIndex];
        CopyTextureTransform(
            _sourceFaceMaterial,
            _cloneFaceMaterial,
            BaseMap);
        CopyTextureTransform(
            _sourceFaceMaterial,
            _cloneFaceMaterial,
            MainTex);
        _lastAppliedFaceExpressionId = expressionId;
        return true;
    }

    private static void CopyTextureTransform(
        Material source,
        Material destination,
        int propertyId)
    {
        if (source == null ||
            destination == null ||
            !source.HasProperty(propertyId) ||
            !destination.HasProperty(propertyId))
        {
            return;
        }

        destination.SetTexture(
            propertyId,
            source.GetTexture(propertyId));
        destination.SetTextureScale(
            propertyId,
            source.GetTextureScale(propertyId));
        destination.SetTextureOffset(
            propertyId,
            source.GetTextureOffset(propertyId));
    }

    private void ApplyRenderingState()
    {
        bool shouldRender =
            isActiveAndEnabled &&
            _renderingRequested &&
            IsReady;

        if (_motionRoot != null &&
            _motionRoot.gameObject.activeSelf != shouldRender)
        {
            _motionRoot.gameObject.SetActive(shouldRender);
        }

        if (_portraitCamera != null)
            _portraitCamera.enabled = shouldRender;
    }

    private bool FailBind(string failureReason)
    {
        _lastFailureReason = failureReason ?? "Unknown bind failure.";
        Debug.LogError(
            $"[CharacterPortraitLiveRenderer] " +
            $"{_lastFailureReason}",
            this);
        return false;
    }

    private void DestroyClone()
    {
        if (_motionRoot != null)
        {
            _motionRoot.gameObject.SetActive(false);
            DestroyRuntimeObject(_motionRoot.gameObject);
        }

        _motionRoot = null;
        _posePairs = Array.Empty<PosePair>();
        _rendererPairs = Array.Empty<RendererPair>();

        for (int i = 0; i < _ownedMaterials.Length; i++)
        {
            if (_ownedMaterials[i] != null)
                DestroyRuntimeObject(_ownedMaterials[i]);
        }

        _ownedMaterials = Array.Empty<Material>();
        _cloneFaceMaterial = null;
    }

    private static void DestroyOwnedMaterialList(
        List<Material> materials)
    {
        for (int i = 0; i < materials.Count; i++)
        {
            if (materials[i] != null)
                DestroyRuntimeObject(materials[i]);
        }

        materials.Clear();
    }

    private void DestroyRenderResources()
    {
        if (_portraitCamera != null)
            _portraitCamera.targetTexture = null;

        if (_outputTexture != null)
        {
            if (_outputTexture.IsCreated())
                _outputTexture.Release();

            DestroyRuntimeObject(_outputTexture);
            _outputTexture = null;
        }

        if (_stageRoot != null)
            DestroyRuntimeObject(_stageRoot);

        _stageRoot = null;
        _portraitCamera = null;
    }

    private static void ConfigureRuntimeObject(GameObject target)
    {
        target.layer = PortraitLayer;
        target.hideFlags = HideFlags.HideAndDontSave;
    }

    private static void CopyLocalTransform(
        Transform source,
        Transform destination)
    {
        destination.localPosition =
            IsFiniteVector(source.localPosition)
                ? source.localPosition
                : Vector3.zero;
        destination.localRotation =
            IsFiniteQuaternion(source.localRotation)
                ? source.localRotation
                : Quaternion.identity;
        destination.localScale =
            SanitizeScale(source.localScale);
    }

    private static Vector3 SanitizeScale(Vector3 scale)
    {
        if (!IsFiniteVector(scale))
            return Vector3.one;

        return new Vector3(
            Mathf.Abs(scale.x) > Mathf.Epsilon
                ? scale.x
                : 1f,
            Mathf.Abs(scale.y) > Mathf.Epsilon
                ? scale.y
                : 1f,
            Mathf.Abs(scale.z) > Mathf.Epsilon
                ? scale.z
                : 1f);
    }

    private static bool TryNormalizePlanar(
        Vector3 candidate,
        out Vector3 normalized)
    {
        normalized = Vector3.zero;
        if (!IsFiniteVector(candidate))
            return false;

        Vector3 planar =
            Vector3.ProjectOnPlane(candidate, Vector3.up);
        if (!IsFiniteVector(planar) ||
            planar.sqrMagnitude <= FacingEpsilon)
        {
            return false;
        }

        normalized = planar.normalized;
        return true;
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFiniteQuaternion(Quaternion value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z) &&
               IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private static bool IsSupportedFaceExpression(int expressionId)
    {
        return expressionId >= MinimumNetworkFaceExpressionId &&
               expressionId <= MaximumNetworkFaceExpressionId;
    }

    private bool IsExactLocalOwnerBindingValid()
    {
        if (_boundPlayerObject == null ||
            !_boundPlayerObject.IsSpawned)
        {
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        NetworkClient localClient =
            networkManager != null ? networkManager.LocalClient : null;
        return networkManager != null &&
               networkManager.IsListening &&
               localClient != null &&
               localClient.PlayerObject == _boundPlayerObject &&
               _boundPlayerObject.OwnerClientId ==
               networkManager.LocalClientId;
    }

    private static string GetRelativePath(
        Transform root,
        Transform target)
    {
        if (root == null || target == null)
            return string.Empty;

        if (target == root)
            return string.Empty;

        List<string> segments = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            segments.Add(current.name);
            current = current.parent;
        }

        if (current != root)
            return string.Empty;

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static void DestroyRuntimeObject(
        UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private void Log(string message)
    {
        if (!_debugLogs)
            return;

        Debug.Log(
            $"[CharacterPortraitLiveRenderer] {message}",
            this);
    }
}
