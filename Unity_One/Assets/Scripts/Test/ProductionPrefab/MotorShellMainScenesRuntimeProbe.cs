using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public sealed class MotorShellMainScenesRuntimeProbe : MonoBehaviour
{
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private float logInterval = 0.5f;

    private const float MinLogInterval = 0.05f;
    private const string RootPrefix = "MSProbe/Root";
    private const string NetPrefix = "MSProbe/Net";
    private const string CameraPrefix = "MSProbe/Camera";
    private const string VisualPrefix = "MSProbe/Visual";
    private const string MotorPrefix = "MSProbe/Motor";
    private const string RendererPrefix = "MSProbe/Renderer";
    private const string VisualHeightPrefix = "MSVisualHeight";
    private const string PickupPrefix = "MSPickup";

    private readonly StringBuilder _builder = new StringBuilder(2048);
    private float _nextLogTime;

    private void OnEnable()
    {
        _nextLogTime = 0f;
    }

    private void Update()
    {
        if (!debugLogs)
            return;

        if (Time.unscaledTime < _nextLogTime)
            return;

        _nextLogTime = Time.unscaledTime + Mathf.Max(MinLogInterval, logInterval);
        LogSnapshot();
    }

    private void LogSnapshot()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        PlayerHub playerHub = GetComponent<PlayerHub>();
        Transform cameraPivot = FindChildByName("CameraPivot");
        Camera playerCamera = FindPlayerCamera(cameraPivot);
        AudioListener playerAudioListener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;
        HamsterFullRagdollMotor motor = GetComponentInChildren<HamsterFullRagdollMotor>(true);
        Transform motorShellBody = FindChildByName("MotorShellBody");
        Rigidbody motorShellRigidbody = motorShellBody != null ? motorShellBody.GetComponent<Rigidbody>() : null;
        Transform visualRoot = FindChildByName("VisualPreviewRoot");
        Animator animator = visualRoot != null
            ? visualRoot.GetComponentInChildren<Animator>(true)
            : GetComponentInChildren<Animator>(true);
        HamsterVisualFollower visualFollower = GetComponentInChildren<HamsterVisualFollower>(true);
        HamsterVisualClipStateDriver clipStateDriver = GetComponentInChildren<HamsterVisualClipStateDriver>(true);
        BoxCollider bodyCollider = motorShellBody != null ? motorShellBody.GetComponent<BoxCollider>() : null;
        PlayerInteractModule interactModule = GetComponentInChildren<PlayerInteractModule>(true);
        PickupAnimEventRelay pickupRelay = GetComponentInChildren<PickupAnimEventRelay>(true);

        LogRoot();
        LogNetwork(networkObject);
        LogCamera(playerHub, cameraPivot, playerCamera, playerAudioListener);
        LogVisual(visualRoot, animator, visualFollower, clipStateDriver);
        LogRendererState(visualRoot);
        LogVisualHeight(motor, motorShellBody, visualRoot, visualFollower, bodyCollider);
        LogPickup(playerCamera, animator, interactModule, pickupRelay);
        LogMotor(motor, motorShellRigidbody);
    }

    private void LogRoot()
    {
        _builder.Length = 0;
        AppendPrefix(RootPrefix)
            .Append(" scene=")
            .Append(gameObject.scene.name)
            .Append(" activeSelf=")
            .Append(gameObject.activeSelf)
            .Append(" activeInHierarchy=")
            .Append(gameObject.activeInHierarchy)
            .Append(" rootPosition=")
            .Append(FormatVector3(transform.position))
            .Append(" rootRotation=")
            .Append(FormatQuaternion(transform.rotation))
            .Append(" rootLocalScale=")
            .Append(FormatVector3(transform.localScale))
            .Append(" rootTag=")
            .Append(gameObject.tag)
            .Append(" rootLayer=")
            .Append(gameObject.layer);

        Debug.Log(_builder.ToString(), this);
    }

    private void LogNetwork(NetworkObject networkObject)
    {
        _builder.Length = 0;
        AppendPrefix(NetPrefix)
            .Append(" NetworkObject.exists=")
            .Append(networkObject != null);

        if (networkObject != null)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            _builder.Append(" IsSpawned=")
                .Append(networkObject.IsSpawned)
                .Append(" IsOwner=")
                .Append(networkObject.IsOwner)
                .Append(" OwnerClientId=")
                .Append(networkObject.OwnerClientId)
                .Append(" NetworkManager.Singleton.exists=")
                .Append(networkManager != null)
                .Append(" NetworkManager.Singleton.IsServer=")
                .Append(IsNetworkManagerServer());
        }

        Debug.Log(_builder.ToString(), this);
    }

    private void LogCamera(
        PlayerHub playerHub,
        Transform cameraPivot,
        Camera playerCamera,
        AudioListener playerAudioListener)
    {
        _builder.Length = 0;
        AppendPrefix(CameraPrefix);
        AppendBehaviour("PlayerHub", playerHub);

        Transform cameraRoot = ResolveCameraRoot(playerHub, cameraPivot);
        _builder.Append(" cameraRoot.name=")
            .Append(cameraRoot != null ? cameraRoot.name : "<null>");

        AppendTransform("CameraPivot", cameraPivot);
        AppendCamera("PlayerCamera", playerCamera);

        Camera mainCamera = Camera.main;
        _builder.Append(" Camera.main=")
            .Append(mainCamera != null ? mainCamera.name : "<null>")
            .Append(" Camera.main.worldPosition=")
            .Append(mainCamera != null ? FormatVector3(mainCamera.transform.position) : "<null>")
            .Append(" Camera.main.rotation=")
            .Append(mainCamera != null ? FormatQuaternion(mainCamera.transform.rotation) : "<null>")
            .Append(" AudioListener.exists=")
            .Append(playerAudioListener != null)
            .Append(" AudioListener.enabled=")
            .Append(playerAudioListener != null && playerAudioListener.enabled);

        Debug.Log(_builder.ToString(), this);
    }

    private void LogVisualHeight(
        HamsterFullRagdollMotor motor,
        Transform motorShellBody,
        Transform visualRoot,
        HamsterVisualFollower visualFollower,
        BoxCollider bodyCollider)
    {
        Bounds rendererBounds;
        bool hasRendererBounds = TryGetRendererBounds(visualRoot, out rendererBounds);
        float rendererMinY = hasRendererBounds ? rendererBounds.min.y : float.NaN;
        float boxBottomY = bodyCollider != null ? bodyCollider.bounds.min.y : float.NaN;
        float groundPointY = TryGetGroundPointY(motor, out float resolvedGroundPointY)
            ? resolvedGroundPointY
            : float.NaN;

        bool hasVisualLocalOffset = TryGetMemberValue(visualFollower, null, "visualLocalOffset", out Vector3 visualLocalOffset);
        bool hasSpeedHeight = TryGetMemberValue(visualFollower, null, "enableSpeedBasedVisualHeight", out bool enableSpeedBasedVisualHeight);
        bool hasIdleYOffset = TryGetMemberValue(visualFollower, null, "idleVisualYOffset", out float idleVisualYOffset);
        bool hasMovingYOffset = TryGetMemberValue(visualFollower, null, "movingVisualYOffset", out float movingVisualYOffset);

        _builder.Length = 0;
        AppendPrefix(VisualHeightPrefix)
            .Append(" MotorShellBody.localPosition=")
            .Append(motorShellBody != null ? FormatVector3(motorShellBody.localPosition) : "<null>")
            .Append(" VisualPreviewRoot.localPosition=")
            .Append(visualRoot != null ? FormatVector3(visualRoot.localPosition) : "<null>")
            .Append(" HamsterVisualFollower.exists=")
            .Append(visualFollower != null)
            .Append(" visualLocalOffset=")
            .Append(hasVisualLocalOffset ? FormatVector3(visualLocalOffset) : "<unavailable>")
            .Append(" enableSpeedBasedVisualHeight=")
            .Append(hasSpeedHeight ? enableSpeedBasedVisualHeight.ToString() : "<unavailable>")
            .Append(" idleVisualYOffset=")
            .Append(hasIdleYOffset ? FormatFloat(idleVisualYOffset) : "<unavailable>")
            .Append(" movingVisualYOffset=")
            .Append(hasMovingYOffset ? FormatFloat(movingVisualYOffset) : "<unavailable>");

        Debug.Log(_builder.ToString(), this);

        _builder.Length = 0;
        AppendPrefix(VisualHeightPrefix)
            .Append(" rendererBounds.exists=")
            .Append(hasRendererBounds)
            .Append(" rendererBounds.minY=")
            .Append(FormatFloat(rendererMinY))
            .Append(" groundHitPointY=")
            .Append(FormatFloat(groundPointY))
            .Append(" BoxCollider.bottomY=")
            .Append(FormatFloat(boxBottomY))
            .Append(" rendererMinusGround=")
            .Append(FormatDelta(rendererMinY, groundPointY))
            .Append(" colliderMinusGround=")
            .Append(FormatDelta(boxBottomY, groundPointY));

        Debug.Log(_builder.ToString(), this);
    }

    private void LogPickup(
        Camera playerCamera,
        Animator animator,
        PlayerInteractModule interactModule,
        PickupAnimEventRelay pickupRelay)
    {
        bool hasOwnerCamera = TryGetMemberValue(interactModule, null, "ownerCamera", out Camera ownerCamera);
        bool hasPickupMask = TryGetMemberValue(interactModule, null, "pickupMask", out LayerMask pickupMask);
        bool hasRightHandBone = TryGetMemberValue(interactModule, null, "rightHandBone", out Transform rightHandBone);
        bool hasLeftHandBone = TryGetMemberValue(interactModule, null, "leftHandBone", out Transform leftHandBone);
        bool hasPreferLocalVisual = TryGetMemberValue(interactModule, null, "preferLocalHeldVisual", out bool preferLocalHeldVisual);
        bool hasFallbackWorldAttach = TryGetMemberValue(interactModule, null, "fallbackToWorldAttachWhenNoVisual", out bool fallbackWorldAttach);

        Transform rightWeaponSocket = FindChildByName("RightWeaponSocket");
        Transform weaponPointRight = FindChildByName("WeaponPoint_R");
        Transform weaponPointLeft = FindChildByName("WeaponPoint_L");
        Transform itemDropAnchor = FindChildByName("ItemDropAnchor");

        _builder.Length = 0;
        AppendPrefix(PickupPrefix);
        AppendBehaviour("PlayerInteractModule", interactModule);
        AppendBehaviour("PickupAnimEventRelay", pickupRelay);
        _builder.Append(" PlayerCamera.exists=")
            .Append(playerCamera != null)
            .Append(" ownerCamera=")
            .Append(hasOwnerCamera && ownerCamera != null ? ownerCamera.name : "<null>")
            .Append(" ownerCameraIsPlayerCamera=")
            .Append(hasOwnerCamera && ownerCamera != null && ownerCamera == playerCamera)
            .Append(" pickupMask=")
            .Append(hasPickupMask ? pickupMask.value.ToString() : "<unavailable>")
            .Append(" Animator.exists=")
            .Append(animator != null)
            .Append(" Animator.isHuman=")
            .Append(animator != null && animator.isHuman);

        Debug.Log(_builder.ToString(), this);

        _builder.Length = 0;
        AppendPrefix(PickupPrefix)
            .Append(" rightHandBone=")
            .Append(hasRightHandBone && rightHandBone != null ? rightHandBone.name : "<null>")
            .Append(" leftHandBone=")
            .Append(hasLeftHandBone && leftHandBone != null ? leftHandBone.name : "<null>")
            .Append(" RightWeaponSocket.exists=")
            .Append(rightWeaponSocket != null)
            .Append(" WeaponPoint_R.exists=")
            .Append(weaponPointRight != null)
            .Append(" WeaponPoint_L.exists=")
            .Append(weaponPointLeft != null)
            .Append(" ItemDropAnchor.exists=")
            .Append(itemDropAnchor != null)
            .Append(" preferLocalHeldVisual=")
            .Append(hasPreferLocalVisual ? preferLocalHeldVisual.ToString() : "<unavailable>")
            .Append(" fallbackToWorldAttachWhenNoVisual=")
            .Append(hasFallbackWorldAttach ? fallbackWorldAttach.ToString() : "<unavailable>")
            .Append(" blocker=")
            .Append(GetPickupBlocker(interactModule, playerCamera, animator, rightWeaponSocket, weaponPointRight, weaponPointLeft));

        Debug.Log(_builder.ToString(), this);
    }

    private void LogVisual(
        Transform visualRoot,
        Animator animator,
        HamsterVisualFollower visualFollower,
        HamsterVisualClipStateDriver clipStateDriver)
    {
        _builder.Length = 0;
        AppendPrefix(VisualPrefix);
        AppendTransform("VisualPreviewRoot", visualRoot);
        AppendAnimator(animator);
        AppendBehaviour("HamsterVisualFollower", visualFollower);
        AppendBehaviour("HamsterVisualClipStateDriver", clipStateDriver);

        Debug.Log(_builder.ToString(), this);
    }

    private void LogMotor(HamsterFullRagdollMotor motor, Rigidbody motorShellRigidbody)
    {
        _builder.Length = 0;
        AppendPrefix(MotorPrefix);
        AppendBehaviour("HamsterFullRagdollMotor", motor);
        AppendRigidbody("MotorShellBody.Rigidbody", motorShellRigidbody);
        AppendMotorState(motor);

        Debug.Log(_builder.ToString(), this);
    }

    private StringBuilder AppendPrefix(string prefix)
    {
        return _builder.Append('[')
            .Append(prefix)
            .Append(':')
            .Append(name)
            .Append(']');
    }

    private Transform FindChildByName(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
                return children[i];
        }

        return null;
    }

    private Camera FindPlayerCamera(Transform cameraPivot)
    {
        Transform playerCameraTransform = FindChildByName("PlayerCamera");
        Camera playerCamera = playerCameraTransform != null ? playerCameraTransform.GetComponent<Camera>() : null;
        if (playerCamera != null)
            return playerCamera;

        return cameraPivot != null ? cameraPivot.GetComponentInChildren<Camera>(true) : GetComponentInChildren<Camera>(true);
    }

    private void AppendBehaviour(string label, Behaviour behaviour)
    {
        _builder.Append(' ')
            .Append(label)
            .Append(".exists=")
            .Append(behaviour != null);

        if (behaviour != null)
        {
            _builder.Append(' ')
                .Append(label)
                .Append(".enabled=")
                .Append(behaviour.enabled);
        }
    }

    private void AppendTransform(string label, Transform target)
    {
        _builder.Append(' ')
            .Append(label)
            .Append(".exists=")
            .Append(target != null);

        if (target == null)
            return;

        _builder.Append(' ')
            .Append(label)
            .Append(".activeSelf=")
            .Append(target.gameObject.activeSelf)
            .Append(' ')
            .Append(label)
            .Append(".activeInHierarchy=")
            .Append(target.gameObject.activeInHierarchy)
            .Append(' ')
            .Append(label)
            .Append(".worldPosition=")
            .Append(FormatVector3(target.position))
            .Append(' ')
            .Append(label)
            .Append(".localPosition=")
            .Append(FormatVector3(target.localPosition));
    }

    private void AppendCamera(string label, Camera camera)
    {
        _builder.Append(' ')
            .Append(label)
            .Append(".exists=")
            .Append(camera != null);

        if (camera == null)
            return;

        _builder.Append(' ')
            .Append(label)
            .Append(".enabled=")
            .Append(camera.enabled)
            .Append(' ')
            .Append(label)
            .Append(".activeSelf=")
            .Append(camera.gameObject.activeSelf)
            .Append(' ')
            .Append(label)
            .Append(".activeInHierarchy=")
            .Append(camera.gameObject.activeInHierarchy)
            .Append(' ')
            .Append(label)
            .Append(".tag=")
            .Append(camera.gameObject.tag)
            .Append(' ')
            .Append(label)
            .Append(".worldPosition=")
            .Append(FormatVector3(camera.transform.position))
            .Append(' ')
            .Append(label)
            .Append(".localPosition=")
            .Append(FormatVector3(camera.transform.localPosition))
            .Append(' ')
            .Append(label)
            .Append(".rotation=")
            .Append(FormatQuaternion(camera.transform.rotation));
    }

    private void AppendRigidbody(string label, Rigidbody rigidbody)
    {
        _builder.Append(' ')
            .Append(label)
            .Append(".exists=")
            .Append(rigidbody != null);

        if (rigidbody == null)
            return;

        string velocitySource;
        Vector3 velocity = GetRigidbodyVelocity(rigidbody, out velocitySource);

        _builder.Append(' ')
            .Append(label)
            .Append(".isKinematic=")
            .Append(rigidbody.isKinematic)
            .Append(' ')
            .Append(label)
            .Append(".useGravity=")
            .Append(rigidbody.useGravity)
            .Append(' ')
            .Append(label)
            .Append(".constraints=")
            .Append(rigidbody.constraints)
            .Append(' ')
            .Append(label)
            .Append(".position=")
            .Append(FormatVector3(rigidbody.position))
            .Append(' ')
            .Append(label)
            .Append('.')
            .Append(velocitySource)
            .Append('=')
            .Append(FormatVector3(velocity))
            .Append(' ')
            .Append(label)
            .Append(".sleeping=")
            .Append(rigidbody.IsSleeping());
    }

    private void AppendMotorState(HamsterFullRagdollMotor motor)
    {
        if (motor == null)
            return;

        bool hasRawInput = TryGetMemberValue(motor, "RawInput", "_lastRawMoveInput", out Vector2 rawInput);
        bool hasSmoothedInput = TryGetMemberValue(motor, "SmoothedInput", "_smoothedMoveInput", out Vector2 smoothedInput);
        bool hasMoveInput = TryGetMemberValue(motor, "HasMoveInput", null, out bool reflectedHasMoveInput)
            ? reflectedHasMoveInput
            : hasSmoothedInput
                ? smoothedInput.sqrMagnitude > 0.0001f
                : motor.SmoothedMoveWorldDirection.sqrMagnitude > 0.0001f;
        float planarSpeed = TryGetMemberValue(motor, "PlanarSpeed", null, out float reflectedPlanarSpeed)
            ? reflectedPlanarSpeed
            : motor.CurrentPlanarSpeed;
        bool hasSelectedMaxSpeed = TryGetMemberValue(motor, "SelectedMaxSpeed", "_lastSelectedMaxSpeed", out float selectedMaxSpeed);
        bool sprintHeld = TryGetMemberValue(motor, "SprintHeld", "_lastSprintHeld", out bool reflectedSprintHeld)
            ? reflectedSprintHeld
            : motor.IsSprintHeld;

        _builder.Append(" MotorState.rawInput=")
            .Append(hasRawInput ? FormatVector2(rawInput) : "<unavailable>")
            .Append(" MotorState.smoothedInput=")
            .Append(hasSmoothedInput ? FormatVector2(smoothedInput) : "<unavailable>")
            .Append(" MotorState.hasMoveInput=")
            .Append(hasMoveInput)
            .Append(" MotorState.planarSpeed=")
            .Append(planarSpeed.ToString("F3"))
            .Append(" MotorState.grounded=")
            .Append(motor.IsGrounded)
            .Append(" MotorState.selectedMaxSpeed=")
            .Append(hasSelectedMaxSpeed ? selectedMaxSpeed.ToString("F3") : "<unavailable>")
            .Append(" MotorState.sprintHeld=")
            .Append(sprintHeld);
    }

    private void AppendAnimator(Animator animator)
    {
        _builder.Append(" Animator.exists=")
            .Append(animator != null);

        if (animator == null)
            return;

        _builder.Append(" Animator.enabled=")
            .Append(animator.enabled)
            .Append(" Animator.controller=")
            .Append(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "<null>");
    }

    private void LogRendererState(Transform visualRoot)
    {
        _builder.Length = 0;
        AppendPrefix(RendererPrefix);

        if (visualRoot == null)
        {
            _builder.Append(" Renderer.totalCount=0 Renderer.enabledCount=0 Renderer.activeInHierarchyCount=0 Renderer.isVisibleCount=0")
                .Append(" FirstRenderer.name=<null>")
                .Append(" FirstRenderer.enabled=False")
                .Append(" FirstRenderer.activeInHierarchy=False")
                .Append(" FirstRenderer.layer=-1")
                .Append(" FirstRenderer.boundsCenter=<null>")
                .Append(" FirstRenderer.boundsSize=<null>");
            Debug.Log(_builder.ToString(), this);
            return;
        }

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        int enabledCount = 0;
        int activeInHierarchyCount = 0;
        int visibleCount = 0;
        Renderer firstRenderer = null;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (firstRenderer == null)
                firstRenderer = renderer;

            if (renderer.enabled)
                enabledCount++;

            if (renderer.gameObject.activeInHierarchy)
                activeInHierarchyCount++;

            if (renderer.isVisible)
                visibleCount++;
        }

        _builder.Append(" Renderer.totalCount=")
            .Append(renderers.Length)
            .Append(" Renderer.enabledCount=")
            .Append(enabledCount)
            .Append(" Renderer.activeInHierarchyCount=")
            .Append(activeInHierarchyCount)
            .Append(" Renderer.isVisibleCount=")
            .Append(visibleCount);

        if (firstRenderer != null)
        {
            Bounds bounds = firstRenderer.bounds;
            _builder.Append(" FirstRenderer.name=")
                .Append(firstRenderer.name)
                .Append(" FirstRenderer.enabled=")
                .Append(firstRenderer.enabled)
                .Append(" FirstRenderer.activeInHierarchy=")
                .Append(firstRenderer.gameObject.activeInHierarchy)
                .Append(" FirstRenderer.layer=")
                .Append(firstRenderer.gameObject.layer)
                .Append(" FirstRenderer.boundsCenter=")
                .Append(FormatVector3(bounds.center))
                .Append(" FirstRenderer.boundsSize=")
                .Append(FormatVector3(bounds.size));
        }
        else
        {
            _builder.Append(" FirstRenderer.name=<null>")
                .Append(" FirstRenderer.enabled=False")
                .Append(" FirstRenderer.activeInHierarchy=False")
                .Append(" FirstRenderer.layer=-1")
                .Append(" FirstRenderer.boundsCenter=<null>")
                .Append(" FirstRenderer.boundsSize=<null>");
        }

        Debug.Log(_builder.ToString(), this);
    }

    private bool TryGetRendererBounds(Transform visualRoot, out Bounds bounds)
    {
        bounds = default;
        if (visualRoot == null)
            return false;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private bool TryGetGroundPointY(HamsterFullRagdollMotor motor, out float groundPointY)
    {
        groundPointY = float.NaN;
        if (motor == null)
            return false;

        if (!TryGetMemberValue(motor, null, "_lastGroundHit", out bool groundHit) || !groundHit)
            return false;

        if (!TryGetMemberValue(motor, null, "_lastGroundProbeOrigin", out Vector3 probeOrigin))
            return false;

        if (!TryGetMemberValue(motor, null, "_lastGroundHitDistance", out float hitDistance))
            return false;

        if (!TryGetMemberValue(motor, null, "_lastGroundProbeRadius", out float probeRadius))
            probeRadius = 0f;

        groundPointY = probeOrigin.y - hitDistance - Mathf.Max(0f, probeRadius);
        return !float.IsNaN(groundPointY) && !float.IsInfinity(groundPointY);
    }

    private string GetPickupBlocker(
        PlayerInteractModule interactModule,
        Camera playerCamera,
        Animator animator,
        Transform rightWeaponSocket,
        Transform weaponPointRight,
        Transform weaponPointLeft)
    {
        if (interactModule == null)
            return "PlayerInteractModule missing on target prefab";

        if (playerCamera == null)
            return "PlayerCamera missing";

        if (rightWeaponSocket == null && weaponPointRight == null && weaponPointLeft == null && (animator == null || !animator.isHuman))
            return "no RightWeaponSocket/WeaponPoint/humanoid hand fallback";

        return "<none>";
    }

    private static bool IsNetworkManagerServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsServer;
    }

    private static Transform ResolveCameraRoot(PlayerHub playerHub, Transform fallback)
    {
        if (playerHub != null && TryGetMemberValue(playerHub, "CameraRoot", "cameraRoot", out Transform cameraRoot))
            return cameraRoot;

        return fallback;
    }

    private static bool TryGetMemberValue<T>(object target, string propertyName, string fieldName, out T value)
    {
        value = default;
        if (target == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (!string.IsNullOrEmpty(propertyName))
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, flags);
            if (TryReadMemberValue(property, target, out value))
                return true;
        }

        if (!string.IsNullOrEmpty(fieldName))
        {
            FieldInfo field = target.GetType().GetField(fieldName, flags);
            if (TryReadMemberValue(field, target, out value))
                return true;
        }

        return false;
    }

    private static bool TryReadMemberValue<T>(MemberInfo member, object target, out T value)
    {
        value = default;
        try
        {
            object rawValue = null;
            if (member is PropertyInfo property)
                rawValue = property.GetValue(target, null);
            else if (member is FieldInfo field)
                rawValue = field.GetValue(target);

            if (rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static Vector3 GetRigidbodyVelocity(Rigidbody rigidbody, out string source)
    {
        source = "velocityUnavailable";
        if (rigidbody == null)
            return Vector3.zero;

        if (TryGetRigidbodyVector3Property(rigidbody, "linearVelocity", out Vector3 linearVelocity))
        {
            source = "linearVelocity";
            return linearVelocity;
        }

        if (TryGetRigidbodyVector3Property(rigidbody, "velocity", out Vector3 velocity))
        {
            source = "velocity";
            return velocity;
        }

        return Vector3.zero;
    }

    private static bool TryGetRigidbodyVector3Property(Rigidbody rigidbody, string propertyName, out Vector3 value)
    {
        value = Vector3.zero;
        PropertyInfo property = typeof(Rigidbody).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
            return false;

        object propertyValue = property.GetValue(rigidbody, null);
        if (propertyValue is Vector3 vector)
        {
            value = vector;
            return true;
        }

        return false;
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({value.x:F3},{value.y:F3})";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3})";
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return $"({value.x:F3},{value.y:F3},{value.z:F3},{value.w:F3})";
    }

    private static string FormatFloat(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? "<null>" : value.ToString("F3");
    }

    private static string FormatDelta(float value, float baseline)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(baseline) || float.IsInfinity(baseline))
            return "<null>";

        return (value - baseline).ToString("F3");
    }
}
