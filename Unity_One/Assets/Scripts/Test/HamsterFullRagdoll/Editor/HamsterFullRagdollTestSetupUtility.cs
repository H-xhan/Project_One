using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HamsterFullRagdollTestSetupUtility
{
    private const string LogPrefix = "[HamsterFullRagdollTestSetup]";
    private const string SceneDestinationPath = "Assets/Scenes/Play/Test_FullRagdollHamster.unity";
    private const string PrefabSourcePath = "Assets/Prefab/Player/슈가_RagdollPrototype.prefab";
    private const string PrefabDestinationPath = "Assets/Prefab/Test/Hamster_FullRagdoll_Test.prefab";
    private const string ProductionPlayerPrefabPath = "Assets/Prefab/Player/슈가.prefab";
    private const string ControllersObjectName = "Controllers";
    private const string TestPrefabFolder = "Assets/Prefab/Test/";
    private const string TestPrefabName = "Hamster_FullRagdoll_Test";
    private const string FootSupportColliderName = "FootSupportCollider";
    private const string JointFreeShellRootName = "Hamster_JointFreeMotorShell_Test";
    private const string JointFreeShellBodyName = "MotorShellBody";
    private const string JointFreeShellVisualRootName = "VisualPreviewRoot";

    private static readonly string[] SceneSourceCandidates =
    {
        "Assets/Scenes/Play/Test_RagdollSandbox.unity",
        "Assets/Scenes/Test_RagdollSandbox.unity"
    };

    private static readonly HashSet<string> DisableTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "PlayerHub",
        "NetworkObject",
        "NetworkTransform",
        "NetworkAnimator",
        "NetworkRigidbody",
        "NetworkRigidbody2D",
        "CharacterController",
        "SugaActiveRagdollController",
        "RagdollSandboxTestController",
        "PlayerInputModule",
        "PlayerLocomotionModule",
        "PlayerAnimModule",
        "PlayerCombatModule",
        "PlayerInteractModule",
        "PlayerStatusModule",
        "PlayerStaminaModule"
    };

    private static readonly string[] HipsCandidates =
    {
        "BodyCore",
        "Core",
        "Hips",
        "Hip",
        "Pelvis",
        "Belly",
        "Body",
        "몸통",
        "Root"
    };

    private static readonly string[] ChestCandidates =
    {
        "Chest",
        "Spine",
        "UpperBody",
        "BodyChest"
    };

    private static readonly string[] HeadCandidates =
    {
        "Head"
    };

    private static readonly string[] LeftArmCandidates =
    {
        "Arm_L",
        "LeftArm",
        "Left_Arm",
        "Wing_L",
        "LeftWing",
        "UpperArm_L",
        "L_Arm",
        "BK_Wing_L"
    };

    private static readonly string[] RightArmCandidates =
    {
        "Arm_R",
        "RightArm",
        "Right_Arm",
        "Wing_R",
        "RightWing",
        "UpperArm_R",
        "R_Arm",
        "BK_Wing_R"
    };

    private static readonly string[] HurtboxBlockerNameFragments =
    {
        "BodyHurtbox",
        "HeadHurtbox",
        "BodyBlocker",
        "Hurtbox",
        "Hitbox",
        "Blocker"
    };

    private static readonly string[] DiagnosticColliderNameFragments =
    {
        "Tail",
        "BK_Tail",
        "UpperLeg",
        "Upper_Leg",
        "upper_leg",
        "LowerLeg",
        "Lower_Leg",
        "lower_leg",
        "Foot",
        "foot",
        "Toe",
        "Toes",
        "toes"
    };

    private struct MotorConfigurationResult
    {
        public bool Succeeded;
        public bool MotorCreated;
        public HamsterFullRagdollMotor Motor;
        public bool HipsAssigned;
        public bool ChestAssigned;
        public bool CameraAssigned;
        public bool GroundMaskAssigned;
        public int DisabledComponentCount;
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Create Test Copies")]
    private static void CreateTestCopies()
    {
        string sceneSourcePath = ResolveFirstExistingAssetPath(SceneSourceCandidates);
        bool copiedAny = false;

        copiedAny |= CopyAssetIfMissing(sceneSourcePath, SceneDestinationPath, "scene");
        copiedAny |= CopyAssetIfMissing(PrefabSourcePath, PrefabDestinationPath, "prefab");

        if (!copiedAny)
        {
            Debug.Log($"{LogPrefix} Create Test Copies completed. No new assets were copied.");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"{LogPrefix} Create Test Copies completed.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Configure Selected Test Prefab For Motor")]
    private static void ConfigureSelectedTestPrefabForMotor()
    {
        string prefabPath;
        if (!TryGetSelectedTestPrefabPath(out prefabPath))
            return;

        MotorConfigurationResult ignoredResult;
        ConfigureTestPrefabAtPath(prefabPath, requireExactDestinationPath: false, out ignoredResult);
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Prepare Ready-To-Test Scene")]
    private static void PrepareReadyToTestScene()
    {
        if (!EnsureTestCopiesForPrepare())
            return;

        MotorConfigurationResult prefabResult;
        if (!ConfigureTestPrefabAtPath(PrefabDestinationPath, requireExactDestinationPath: true, out prefabResult))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning($"{LogPrefix} Prepare cancelled by user while saving current modified scenes.");
            return;
        }

        Scene testScene = EditorSceneManager.OpenScene(SceneDestinationPath, OpenSceneMode.Single);
        if (!testScene.IsValid() || !string.Equals(NormalizeAssetPath(testScene.path), SceneDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Failed to open destination test scene: {SceneDestinationPath}");
            return;
        }

        Debug.Log($"{LogPrefix} Opened test scene: {SceneDestinationPath}");

        GameObject testInstance = FindDestinationPrefabInstance(testScene);
        int disabledSourceInstances = DisableSourceOrProductionInstances(testScene, testInstance);
        if (testInstance == null)
            testInstance = PlaceHamsterTestInstance(testScene);
        else
            Debug.Log($"{LogPrefix} Reusing existing Hamster_FullRagdoll_Test instance.");

        if (testInstance == null)
            return;

        testInstance.SetActive(true);
        testInstance.name = TestPrefabName;
        testInstance.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        testInstance.transform.localScale = Vector3.one;

        Camera mainCamera = Camera.main;
        Transform cameraTransform = mainCamera != null ? mainCamera.transform : null;
        int groundMask = ResolveSceneGroundMask(testScene);

        MotorConfigurationResult sceneResult;
        ConfigureMotorOnRoot(
            testInstance,
            cameraTransform,
            groundMask,
            allowPrefabCameraFallback: false,
            applyRigidbodyDefaults: true,
            out sceneResult);

        if (cameraTransform != null)
            Debug.Log($"{LogPrefix} Assigned Main Camera to cameraTransform: {GetHierarchyPath(cameraTransform)}");
        else
            Debug.LogWarning($"{LogPrefix} No Main Camera found. cameraTransform remains empty; motor will use its own transform basis.");

        if (groundMask != 0)
            Debug.Log($"{LogPrefix} Assigned groundMask for scene instance. Value={groundMask}");
        else
            Debug.LogWarning($"{LogPrefix} groundMask is empty after scene setup.");

        HamsterFullRagdollMotor phase1Motor;
        bool phase1Applied = ApplyPhase1StandingSupportPresetToSceneInstance(testInstance, testScene, out phase1Motor);

        EditorSceneManager.MarkSceneDirty(testScene);
        if (!EditorSceneManager.SaveScene(testScene))
        {
            Debug.LogError($"{LogPrefix} Failed to save destination test scene: {SceneDestinationPath}");
            return;
        }

        if (phase1Motor != null)
            Selection.activeGameObject = phase1Motor.gameObject;
        else if (sceneResult.Motor != null)
            Selection.activeGameObject = sceneResult.Motor.gameObject;
        else
            Selection.activeGameObject = testInstance;

        Debug.Log($"{LogPrefix} Ready to press Play. TestScene={SceneDestinationPath} TestPrefab={PrefabDestinationPath}");
        Debug.Log($"{LogPrefix} Summary: PrefabMotorCreated={prefabResult.MotorCreated} SceneMotorCreated={sceneResult.MotorCreated} HipsAssigned={sceneResult.HipsAssigned} ChestAssigned={sceneResult.ChestAssigned} CameraAssigned={sceneResult.CameraAssigned} GroundMaskAssigned={sceneResult.GroundMaskAssigned} Phase1SupportApplied={phase1Applied} DisabledInPrefab={prefabResult.DisabledComponentCount} DisabledInSceneInstance={sceneResult.DisabledComponentCount} DisabledSourceInstances={disabledSourceInstances}");
        Debug.Log($"{LogPrefix} Next step: Press Play and test 3~10 sec standing / WASD movement.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Apply Phase 1 Standing Support Preset")]
    private static void ApplyPhase1StandingSupportPreset()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid()
            || !string.Equals(NormalizeAssetPath(activeScene.path), SceneDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"{LogPrefix} Current scene is not the destination test scene. Open {SceneDestinationPath} or run Prepare Ready-To-Test Scene first. CurrentPath={activeScene.path}");
            return;
        }

        GameObject testInstance = FindDestinationPrefabInstance(activeScene);
        if (testInstance == null)
        {
            Debug.LogWarning($"{LogPrefix} Destination prefab scene instance was not found. Run Prepare Ready-To-Test Scene first.");
            return;
        }

        HamsterFullRagdollMotor motor;
        if (!ApplyPhase1StandingSupportPresetToSceneInstance(testInstance, activeScene, out motor))
            return;

        EditorSceneManager.MarkSceneDirty(activeScene);
        if (!EditorSceneManager.SaveScene(activeScene))
        {
            Debug.LogError($"{LogPrefix} Failed to save destination test scene after Phase 1 preset: {SceneDestinationPath}");
            return;
        }

        if (motor != null)
            Selection.activeGameObject = motor.gameObject;

        Debug.Log($"{LogPrefix} Ready for standing test.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Apply Phase 1 Collision Isolation Preset")]
    private static void ApplyPhase1CollisionIsolationPreset()
    {
        Scene activeScene;
        GameObject testInstance;
        if (!TryGetCurrentDestinationTestInstance(out activeScene, out testInstance))
            return;

        HamsterFullRagdollMotor motor;
        if (!ApplyPhase1DiagnosticPresetToSceneInstance(testInstance, activeScene, hipsOnlyPose: false, out motor))
            return;

        if (!SaveCurrentDestinationTestScene(activeScene, "Collision Isolation preset"))
            return;

        if (motor != null)
            Selection.activeGameObject = motor.gameObject;

        Debug.Log($"{LogPrefix} Applied Collision Isolation Preset.");
        Debug.Log($"{LogPrefix} Next: Press Play with no input. If it still launches, collider overlap remains.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Apply Phase 1 Hips Only Pose Preset")]
    private static void ApplyPhase1HipsOnlyPosePreset()
    {
        Scene activeScene;
        GameObject testInstance;
        if (!TryGetCurrentDestinationTestInstance(out activeScene, out testInstance))
            return;

        HamsterFullRagdollMotor motor;
        if (!ApplyPhase1DiagnosticPresetToSceneInstance(testInstance, activeScene, hipsOnlyPose: true, out motor))
            return;

        if (!SaveCurrentDestinationTestScene(activeScene, "Hips Only Pose preset"))
            return;

        if (motor != null)
            Selection.activeGameObject = motor.gameObject;

        Debug.Log($"{LogPrefix} Applied Hips Only Pose Preset.");
        Debug.Log($"{LogPrefix} Next: Press Play with no input. If stable, gradually enable chest pose later.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Apply Phase 1 Hips Shell Isolation Preset")]
    private static void ApplyPhase1HipsShellIsolationPreset()
    {
        Scene activeScene;
        GameObject testInstance;
        if (!TryGetCurrentDestinationTestInstance(out activeScene, out testInstance))
            return;

        HamsterFullRagdollMotor motor;
        if (!ApplyPhase1HipsShellIsolationPresetToSceneInstance(testInstance, activeScene, out motor))
            return;

        if (!SaveCurrentDestinationTestScene(activeScene, "Hips Shell Isolation preset"))
            return;

        if (motor != null)
            Selection.activeGameObject = motor.gameObject;

        Debug.Log($"{LogPrefix} Applied Hips Shell Isolation Preset.");
        Debug.Log($"{LogPrefix} Next test:");
        Debug.Log($"{LogPrefix} 1. Press Play with no input.");
        Debug.Log($"{LogPrefix} 2. Do not press WASD.");
        Debug.Log($"{LogPrefix} 3. Expected: no launch, planarSpeed stays near 0~1.");
        Debug.Log($"{LogPrefix} 4. If still launches, joints or FootSupportCollider/Plane overlap remain.");
        Debug.Log($"{LogPrefix} 5. If stable, then apply Hips Only Pose Preset.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Create Phase 1 Joint-Free Motor Shell")]
    private static void CreatePhase1JointFreeMotorShell()
    {
        Scene activeScene;
        if (!TryGetCurrentDestinationTestScene(out activeScene))
            return;

        HamsterFullRagdollMotor motor;
        if (!CreateOrConfigureJointFreeMotorShell(activeScene, out motor))
            return;

        if (!SaveCurrentDestinationTestScene(activeScene, "Joint-Free Motor Shell setup"))
            return;

        if (motor != null)
            Selection.activeGameObject = motor.gameObject;

        GameObject existingSkinnedInstance = FindDestinationPrefabInstance(activeScene);
        if (existingSkinnedInstance != null && existingSkinnedInstance.activeInHierarchy)
            Debug.LogWarning($"{LogPrefix} Existing skinned ragdoll test instance is still active; disable it when testing shell only.");

        Debug.Log($"{LogPrefix} Shell diagnostic ready.");
        Debug.Log($"{LogPrefix} Next:");
        Debug.Log($"{LogPrefix} 1. Press Play with no input.");
        Debug.Log($"{LogPrefix} 2. Confirm no launch and planarSpeed near 0.");
        Debug.Log($"{LogPrefix} 3. Stop Play.");
        Debug.Log($"{LogPrefix} 4. Set controlStrength=1.");
        Debug.Log($"{LogPrefix} 5. Press Play and tap W for 0.3 sec.");
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Validate Selected Test Prefab")]
    private static void ValidateSelectedTestPrefab()
    {
        string prefabPath;
        if (!TryGetSelectedPrefabPath(out prefabPath))
            return;

        bool looksLikeTestPrefab = IsAllowedTestPrefabPath(prefabPath);
        if (!looksLikeTestPrefab)
            Debug.LogError($"{LogPrefix} Selected prefab does not look like a Hamster full ragdoll test prefab: {prefabPath}");
        else
            Debug.Log($"{LogPrefix} Prefab path check passed: {prefabPath}");

        if (IsForbiddenSourcePrefabPath(prefabPath))
            Debug.LogError($"{LogPrefix} Selected prefab is a protected source/production prefab and must not be configured: {prefabPath}");

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"{LogPrefix} Failed to load prefab contents for validation: {prefabPath}");
                return;
            }

            HamsterFullRagdollMotor motor = prefabRoot.GetComponentInChildren<HamsterFullRagdollMotor>(true);
            if (motor == null)
            {
                Debug.LogError($"{LogPrefix} HamsterFullRagdollMotor is missing.");
            }
            else
            {
                Debug.Log($"{LogPrefix} HamsterFullRagdollMotor found on {GetHierarchyPath(motor.transform)}.");
                SerializedObject motorObject = new SerializedObject(motor);
                Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody");
                Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody");
                int groundMask = GetIntField(motorObject, "groundMask");

                ValidateRequiredRigidbody("hipsBody", hipsBody);
                ValidateRequiredRigidbody("chestBody", chestBody);

                if (groundMask == 0)
                    Debug.LogError($"{LogPrefix} groundMask is empty.");
                else
                    Debug.Log($"{LogPrefix} groundMask is set. Value={groundMask}");
            }

            ValidateDisabledComponentState(prefabRoot);

            Animator animator = prefabRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
                Debug.LogWarning($"{LogPrefix} Animator is missing. TargetRig animation reuse may not be available.");
            else
                Debug.Log($"{LogPrefix} Animator is present and should remain enabled unless a later test explicitly disables it.");

            Component faceExpression = FindComponentByTypeName(prefabRoot, "FaceExpressionController");
            if (faceExpression != null)
                Debug.Log($"{LogPrefix} FaceExpressionController is present and can remain as visual/expression reference.");

            Debug.LogWarning($"{LogPrefix} Manual validation required: Tail collision should be OFF / visual-only in Phase 1.");
            Debug.LogWarning($"{LogPrefix} Manual validation required: puppet self collision needs a dedicated layer or IgnoreCollision setup.");
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    [MenuItem("Project ONE/Hamster Full Ragdoll/Validate Current Test Scene")]
    private static void ValidateCurrentTestScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid()
            || !string.Equals(NormalizeAssetPath(activeScene.path), SceneDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning($"{LogPrefix} Current scene is not Test_FullRagdollHamster. CurrentPath={activeScene.path}");
            return;
        }

        GameObject testInstance = FindDestinationPrefabInstance(activeScene);
        if (testInstance == null)
        {
            Debug.LogError($"{LogPrefix} Destination Hamster_FullRagdoll_Test prefab instance is missing in current test scene.");
            ValidateJointFreeMotorShell(activeScene, null);
            return;
        }

        if (!testInstance.activeInHierarchy)
            Debug.LogError($"{LogPrefix} Destination Hamster_FullRagdoll_Test instance is not active.");
        else
            Debug.Log($"{LogPrefix} Destination Hamster_FullRagdoll_Test instance is active.");

        ValidateNoActiveProtectedTestLikeInstances(activeScene, testInstance);

        Debug.Log($"{LogPrefix} Hamster_FullRagdoll_Test instance found: {GetHierarchyPath(testInstance.transform)}");
        HamsterFullRagdollMotor motor = testInstance.GetComponentInChildren<HamsterFullRagdollMotor>(true);
        if (motor == null)
        {
            Debug.LogError($"{LogPrefix} HamsterFullRagdollMotor is missing in scene instance.");
        }
        else
        {
            SerializedObject motorObject = new SerializedObject(motor);
            Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody");
            Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody");
            Rigidbody headBody = GetObjectReference<Rigidbody>(motorObject, "headBody");
            Rigidbody leftArmBody = GetObjectReference<Rigidbody>(motorObject, "leftArmBody");
            Rigidbody rightArmBody = GetObjectReference<Rigidbody>(motorObject, "rightArmBody");
            Transform cameraTransform = GetObjectReference<Transform>(motorObject, "cameraTransform");
            int groundMask = GetIntField(motorObject, "groundMask");

            ValidateRequiredRigidbody("hipsBody", hipsBody);
            ValidateRequiredRigidbody("chestBody", chestBody);

            if (cameraTransform == null)
                Debug.LogWarning($"{LogPrefix} cameraTransform is not assigned on scene instance motor.");
            else
                Debug.Log($"{LogPrefix} cameraTransform assigned -> {GetHierarchyPath(cameraTransform)}.");

            if (groundMask == 0)
                Debug.LogError($"{LogPrefix} groundMask is empty on scene instance motor.");
            else
                Debug.Log($"{LogPrefix} groundMask is set on scene instance motor. Value={groundMask}");

            ValidatePhase1StandingSupportState(activeScene, testInstance, motorObject, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);
        }

        ValidateDisabledComponentState(testInstance);

        Animator animator = testInstance.GetComponentInChildren<Animator>(true);
        if (animator != null)
            Debug.Log($"{LogPrefix} Animator is present and can remain for TargetRig visual reference.");
        else
            Debug.LogWarning($"{LogPrefix} Animator is missing in scene instance.");

        Component faceExpression = FindComponentByTypeName(testInstance, "FaceExpressionController");
        if (faceExpression != null)
            Debug.Log($"{LogPrefix} FaceExpressionController is present and can remain as visual/expression reference.");

        Debug.LogWarning($"{LogPrefix} Manual validation required: Tail collision should be OFF / visual-only.");
        Debug.LogWarning($"{LogPrefix} Manual validation required: puppet self collision needs a dedicated layer or IgnoreCollision setup.");
        ValidateJointFreeMotorShell(activeScene, testInstance);
    }

    private static bool EnsureTestCopiesForPrepare()
    {
        string sceneSourcePath = ResolveFirstExistingAssetPath(SceneSourceCandidates);
        bool sceneReady = EnsureAssetCopyExists(sceneSourcePath, SceneDestinationPath, "test scene");
        bool prefabReady = EnsureAssetCopyExists(PrefabSourcePath, PrefabDestinationPath, "test prefab");

        if (!sceneReady || !prefabReady)
            return false;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }

    private static bool TryGetCurrentDestinationTestInstance(out Scene activeScene, out GameObject testInstance)
    {
        activeScene = SceneManager.GetActiveScene();
        testInstance = null;
        if (!TryGetCurrentDestinationTestScene(out activeScene))
            return false;

        testInstance = FindDestinationPrefabInstance(activeScene);
        if (testInstance == null)
        {
            Debug.LogWarning($"{LogPrefix} Destination prefab scene instance was not found. Run Prepare Ready-To-Test Scene first.");
            return false;
        }

        return true;
    }

    private static bool TryGetCurrentDestinationTestScene(out Scene activeScene)
    {
        activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid()
            && string.Equals(NormalizeAssetPath(activeScene.path), SceneDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Debug.LogWarning($"{LogPrefix} Current scene is not the destination test scene. Open {SceneDestinationPath} first. CurrentPath={activeScene.path}");
        return false;
    }

    private static bool SaveCurrentDestinationTestScene(Scene scene, string operationName)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        if (EditorSceneManager.SaveScene(scene))
            return true;

        Debug.LogError($"{LogPrefix} Failed to save destination test scene after {operationName}: {SceneDestinationPath}");
        return false;
    }

    private static bool EnsureAssetCopyExists(string sourcePath, string destinationPath, string label)
    {
        if (string.IsNullOrEmpty(sourcePath) || AssetDatabase.LoadMainAssetAtPath(sourcePath) == null)
        {
            Debug.LogError($"{LogPrefix} Source {label} asset not found. Cannot create destination={destinationPath}");
            return false;
        }

        if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null || File.Exists(destinationPath))
        {
            Debug.Log($"{LogPrefix} {ToTitleCase(label)} already exists, using existing: {destinationPath}");
            return true;
        }

        if (!EnsureFolderForAssetPath(destinationPath))
        {
            Debug.LogError($"{LogPrefix} Failed to create destination folder for {destinationPath}");
            return false;
        }

        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
        {
            Debug.LogError($"{LogPrefix} Failed to copy {label}: {sourcePath} -> {destinationPath}");
            return false;
        }

        Debug.Log($"{LogPrefix} Created {label}: {destinationPath}");
        return true;
    }

    private static bool ConfigureTestPrefabAtPath(
        string prefabPath,
        bool requireExactDestinationPath,
        out MotorConfigurationResult result)
    {
        result = default(MotorConfigurationResult);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Cannot configure empty prefab path.");
            return false;
        }

        prefabPath = NormalizeAssetPath(prefabPath);
        if (IsForbiddenSourcePrefabPath(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Refusing to configure protected source/production prefab: {prefabPath}");
            return false;
        }

        if (requireExactDestinationPath && !string.Equals(prefabPath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Prepare can only configure the exact destination prefab: {PrefabDestinationPath}");
            return false;
        }

        if (!requireExactDestinationPath && !IsAllowedTestPrefabPath(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Refusing to configure prefab outside test destination/name guard: {prefabPath}");
            return false;
        }

        if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
        {
            Debug.LogError($"{LogPrefix} Prefab does not exist: {prefabPath}");
            return false;
        }

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"{LogPrefix} Failed to load prefab contents: {prefabPath}");
                return false;
            }

            ConfigureMotorOnRoot(
                prefabRoot,
                cameraTransformOverride: null,
                groundMaskValue: ResolveDefaultGroundMask(),
                allowPrefabCameraFallback: true,
                applyRigidbodyDefaults: true,
                out result);

            Debug.LogWarning($"{LogPrefix} Phase 1 manual check: keep Tail collision OFF / visual-only.");
            Debug.LogWarning($"{LogPrefix} Phase 1 manual check: keep Legs/Feet visual-only or excluded.");
            Debug.LogWarning($"{LogPrefix} Phase 1 manual check: separate puppet self-collision layers before physical stress testing.");

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            if (savedPrefab == null)
            {
                Debug.LogError($"{LogPrefix} Failed to save configured prefab: {prefabPath}");
                result.Succeeded = false;
                return false;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Configured test prefab. DisabledComponents={result.DisabledComponentCount} Path={prefabPath}");
            result.Succeeded = true;
            return true;
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void ConfigureMotorOnRoot(
        GameObject root,
        Transform cameraTransformOverride,
        int groundMaskValue,
        bool allowPrefabCameraFallback,
        bool applyRigidbodyDefaults,
        out MotorConfigurationResult result)
    {
        result = default(MotorConfigurationResult);
        if (root == null)
            return;

        GameObject controllers = GetOrCreateDirectChild(root.transform, ControllersObjectName);
        HamsterFullRagdollMotor motor = controllers.GetComponent<HamsterFullRagdollMotor>();
        if (motor == null)
        {
            motor = controllers.AddComponent<HamsterFullRagdollMotor>();
            result.MotorCreated = true;
            Debug.Log($"{LogPrefix} Added HamsterFullRagdollMotor to {GetHierarchyPath(controllers.transform)}.");
        }
        else
        {
            Debug.Log($"{LogPrefix} Reusing existing HamsterFullRagdollMotor on {GetHierarchyPath(controllers.transform)}.");
        }

        result.Motor = motor;
        result.DisabledComponentCount = DisableProductionAndNetworkComponents(root);

        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        Rigidbody hipsBody = FindRigidbodyByCandidates(rigidbodies, HipsCandidates);
        Rigidbody chestBody = FindRigidbodyByCandidates(rigidbodies, ChestCandidates);
        Rigidbody headBody = FindRigidbodyByCandidates(rigidbodies, HeadCandidates);
        Rigidbody leftArmBody = FindRigidbodyByCandidates(rigidbodies, LeftArmCandidates);
        Rigidbody rightArmBody = FindRigidbodyByCandidates(rigidbodies, RightArmCandidates);

        Transform resolvedCameraTransform = cameraTransformOverride;
        if (resolvedCameraTransform == null && allowPrefabCameraFallback)
        {
            Camera prefabCamera = root.GetComponentInChildren<Camera>(true);
            if (prefabCamera != null)
                resolvedCameraTransform = prefabCamera.transform;
        }

        SerializedObject motorObject = new SerializedObject(motor);
        AssignRigidbodyField(motorObject, "hipsBody", hipsBody, required: true);
        AssignRigidbodyField(motorObject, "chestBody", chestBody, required: true);
        AssignRigidbodyField(motorObject, "headBody", headBody, required: false);
        AssignRigidbodyField(motorObject, "leftArmBody", leftArmBody, required: false);
        AssignRigidbodyField(motorObject, "rightArmBody", rightArmBody, required: false);
        AssignObjectField(motorObject, "cameraTransform", resolvedCameraTransform, required: false);
        AssignGroundMask(motorObject, groundMaskValue);
        ApplyMotorDefaults(motorObject);
        motorObject.ApplyModifiedProperties();

        result.HipsAssigned = hipsBody != null;
        result.ChestAssigned = chestBody != null;
        result.CameraAssigned = resolvedCameraTransform != null;
        result.GroundMaskAssigned = groundMaskValue != 0;

        if (resolvedCameraTransform == null)
            Debug.LogWarning($"{LogPrefix} No cameraTransform assigned. Scene setup can assign Main Camera later.");

        if (applyRigidbodyDefaults)
        {
            ApplyRigidbodyDefaults(hipsBody, "hipsBody", 14f, 0.1f, 6f, CollisionDetectionMode.ContinuousDynamic);
            ApplyRigidbodyDefaults(chestBody, "chestBody", 8f, 0.1f, 5f, CollisionDetectionMode.ContinuousDynamic);
            ApplyRigidbodyDefaults(headBody, "headBody", 6f, 0.05f, 2f, CollisionDetectionMode.ContinuousDynamic);
            ApplyRigidbodyDefaults(leftArmBody, "leftArmBody", 1f, 0.05f, 1f, CollisionDetectionMode.Continuous);
            ApplyRigidbodyDefaults(rightArmBody, "rightArmBody", 1f, 0.05f, 1f, CollisionDetectionMode.Continuous);
        }
    }

    private static bool ApplyPhase1StandingSupportPresetToSceneInstance(
        GameObject testInstance,
        Scene scene,
        out HamsterFullRagdollMotor motor)
    {
        motor = null;
        if (testInstance == null)
        {
            Debug.LogError($"{LogPrefix} Cannot apply Phase 1 preset because test instance is missing.");
            return false;
        }

        string sourcePath = GetPrefabSourcePath(testInstance);
        if (IsProtectedSourceOrProductionPrefabPath(sourcePath))
        {
            Debug.LogError($"{LogPrefix} Refusing to apply Phase 1 preset to protected source/production instance: {GetHierarchyPath(testInstance.transform)} Source={sourcePath}");
            return false;
        }

        if (!string.IsNullOrEmpty(sourcePath)
            && !string.Equals(sourcePath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Refusing to apply Phase 1 preset to non-test prefab instance: {GetHierarchyPath(testInstance.transform)} Source={sourcePath}");
            return false;
        }

        if (string.IsNullOrEmpty(sourcePath))
            Debug.LogWarning($"{LogPrefix} Applying Phase 1 preset to unpacked test-like instance. Prefer using the destination prefab instance for repeatable setup.");

        GameObject controllers = GetOrCreateDirectChild(testInstance.transform, ControllersObjectName);
        motor = controllers.GetComponent<HamsterFullRagdollMotor>();
        if (motor == null)
        {
            motor = controllers.AddComponent<HamsterFullRagdollMotor>();
            Debug.Log($"{LogPrefix} Added HamsterFullRagdollMotor to {GetHierarchyPath(controllers.transform)} for Phase 1 preset.");
        }

        int disabledControllerComponents = DisableProductionAndNetworkComponents(testInstance);

        SerializedObject motorObject = new SerializedObject(motor);
        Rigidbody[] rigidbodies = testInstance.GetComponentsInChildren<Rigidbody>(true);
        Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody") ?? FindRigidbodyByCandidates(rigidbodies, HipsCandidates);
        Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody") ?? FindRigidbodyByCandidates(rigidbodies, ChestCandidates);
        Rigidbody headBody = GetObjectReference<Rigidbody>(motorObject, "headBody") ?? FindRigidbodyByCandidates(rigidbodies, HeadCandidates);
        Rigidbody leftArmBody = GetObjectReference<Rigidbody>(motorObject, "leftArmBody") ?? FindRigidbodyByCandidates(rigidbodies, LeftArmCandidates);
        Rigidbody rightArmBody = GetObjectReference<Rigidbody>(motorObject, "rightArmBody") ?? FindRigidbodyByCandidates(rigidbodies, RightArmCandidates);

        AssignRigidbodyField(motorObject, "hipsBody", hipsBody, required: true);
        AssignRigidbodyField(motorObject, "chestBody", chestBody, required: true);
        AssignRigidbodyField(motorObject, "headBody", headBody, required: false);
        AssignRigidbodyField(motorObject, "leftArmBody", leftArmBody, required: false);
        AssignRigidbodyField(motorObject, "rightArmBody", rightArmBody, required: false);

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            AssignObjectField(motorObject, "cameraTransform", mainCamera.transform, required: false);
            Debug.Log($"{LogPrefix} Assigned Main Camera to cameraTransform: {GetHierarchyPath(mainCamera.transform)}");
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} Main Camera was not found. cameraTransform remains unchanged.");
        }

        int groundMask = ResolveSceneGroundMask(scene);
        AssignGroundMask(motorObject, groundMask);
        Debug.LogWarning($"{LogPrefix} Phase 1: Plane and puppet self collision layers should be separated.");

        ApplyPhase1MotorValues(motorObject);
        motorObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(motor);
        Debug.Log($"{LogPrefix} Assigned Phase 1 motor values.");

        ApplyPhase1RigidbodyDefaults(hipsBody, "hipsBody", 14f, 0.1f, 6f, CollisionDetectionMode.ContinuousDynamic);
        ApplyPhase1RigidbodyDefaults(chestBody, "chestBody", 8f, 0.1f, 5f, CollisionDetectionMode.ContinuousDynamic);
        ApplyPhase1RigidbodyDefaults(headBody, "headBody", 6f, 0.05f, 2f, CollisionDetectionMode.ContinuousDynamic);
        ApplyPhase1RigidbodyDefaults(leftArmBody, "leftArmBody", 1f, 0.05f, 1f, CollisionDetectionMode.Continuous);
        ApplyPhase1RigidbodyDefaults(rightArmBody, "rightArmBody", 1f, 0.05f, 1f, CollisionDetectionMode.Continuous);
        Debug.Log($"{LogPrefix} Rigidbody physics defaults applied.");

        EnsureFootSupportCollider(hipsBody, scene);
        ValidateGroundMaskLayerState(scene, motorObject, hipsBody);
        DisableRootPhase1Components(testInstance, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);
        int disabledHurtboxObjects = DisableHurtboxBlockerObjects(testInstance, controllers.transform, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);
        Debug.Log($"{LogPrefix} Disabled hurtbox/blocker objects count: {disabledHurtboxObjects}");

        EditorUtility.SetDirty(testInstance);
        Debug.LogWarning($"{LogPrefix} Tail collision should remain OFF.");
        Debug.LogWarning($"{LogPrefix} Legs/Feet should remain visual-only or excluded.");
        Debug.Log($"{LogPrefix} Applied Phase 1 Standing Support preset. DisabledControllerComponents={disabledControllerComponents}");
        Debug.Log($"{LogPrefix} Ready for standing test.");
        return true;
    }

    private static bool ApplyPhase1DiagnosticPresetToSceneInstance(
        GameObject testInstance,
        Scene scene,
        bool hipsOnlyPose,
        out HamsterFullRagdollMotor motor)
    {
        motor = null;
        if (testInstance == null)
        {
            Debug.LogError($"{LogPrefix} Cannot apply diagnostic preset because test instance is missing.");
            return false;
        }

        string sourcePath = GetPrefabSourcePath(testInstance);
        if (IsProtectedSourceOrProductionPrefabPath(sourcePath))
        {
            Debug.LogError($"{LogPrefix} Refusing to apply diagnostic preset to protected source/production instance: {GetHierarchyPath(testInstance.transform)} Source={sourcePath}");
            return false;
        }

        if (!string.Equals(sourcePath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Refusing to apply diagnostic preset to non-destination prefab instance: {GetHierarchyPath(testInstance.transform)} Source={sourcePath}");
            return false;
        }

        GameObject controllers = GetOrCreateDirectChild(testInstance.transform, ControllersObjectName);
        motor = controllers.GetComponent<HamsterFullRagdollMotor>();
        if (motor == null)
        {
            motor = controllers.AddComponent<HamsterFullRagdollMotor>();
            Debug.Log($"{LogPrefix} Added HamsterFullRagdollMotor to {GetHierarchyPath(controllers.transform)} for diagnostic preset.");
        }

        int disabledControllerComponents = DisableProductionAndNetworkComponents(testInstance);

        SerializedObject motorObject = new SerializedObject(motor);
        Rigidbody[] rigidbodies = testInstance.GetComponentsInChildren<Rigidbody>(true);
        Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody") ?? FindRigidbodyByCandidates(rigidbodies, HipsCandidates);
        Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody") ?? FindRigidbodyByCandidates(rigidbodies, ChestCandidates);
        Rigidbody headBody = GetObjectReference<Rigidbody>(motorObject, "headBody") ?? FindRigidbodyByCandidates(rigidbodies, HeadCandidates);
        Rigidbody leftArmBody = GetObjectReference<Rigidbody>(motorObject, "leftArmBody") ?? FindRigidbodyByCandidates(rigidbodies, LeftArmCandidates);
        Rigidbody rightArmBody = GetObjectReference<Rigidbody>(motorObject, "rightArmBody") ?? FindRigidbodyByCandidates(rigidbodies, RightArmCandidates);

        AssignRigidbodyField(motorObject, "hipsBody", hipsBody, required: true);
        AssignRigidbodyField(motorObject, "chestBody", chestBody, required: true);
        AssignRigidbodyField(motorObject, "headBody", headBody, required: false);
        AssignRigidbodyField(motorObject, "leftArmBody", leftArmBody, required: false);
        AssignRigidbodyField(motorObject, "rightArmBody", rightArmBody, required: false);
        AssignGroundMask(motorObject, ResolveSceneGroundMask(scene));
        ApplyPhase1DiagnosticMotorValues(motorObject, hipsOnlyPose);
        motorObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(motor);

        EnsureFootSupportCollider(
            hipsBody,
            scene,
            new Vector3(0f, -0.04f, 0f),
            new Vector3(0.25f, 0.04f, 0.18f));

        ApplyDiagnosticRigidbodyCollision(hipsBody, "hipsBody", enableCollision: true);
        ApplyDiagnosticRigidbodyCollision(chestBody, "chestBody", enableCollision: false);
        ApplyDiagnosticRigidbodyCollision(headBody, "headBody", enableCollision: false);
        ApplyDiagnosticRigidbodyCollision(leftArmBody, "leftArmBody", enableCollision: false);
        ApplyDiagnosticRigidbodyCollision(rightArmBody, "rightArmBody", enableCollision: false);

        DisableRootPhase1Components(testInstance, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);
        int disabledColliderCount = DisableDiagnosticNonEssentialColliders(
            testInstance,
            controllers.transform,
            hipsBody,
            chestBody,
            headBody,
            leftArmBody,
            rightArmBody);
        int disabledHurtboxObjects = DisableHurtboxBlockerObjects(testInstance, controllers.transform, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);

        ValidateGroundMaskLayerState(scene, motorObject, hipsBody);
        LogDiagnosticFootSupportSummary(hipsBody);
        EditorUtility.SetDirty(testInstance);

        Debug.Log($"{LogPrefix} hips collision enabled.");
        Debug.Log($"{LogPrefix} chest/head/arms collision disabled.");
        Debug.Log($"{LogPrefix} disabled collider count: {disabledColliderCount} HurtboxBlockerObjects={disabledHurtboxObjects} DisabledControllerComponents={disabledControllerComponents}");
        Debug.Log(hipsOnlyPose
            ? $"{LogPrefix} Applied Hips Only Pose Preset."
            : $"{LogPrefix} Applied Collision Isolation Preset.");
        return true;
    }

    private static void ApplyPhase1DiagnosticMotorValues(SerializedObject serializedObject, bool hipsOnlyPose)
    {
        SetFloatField(serializedObject, "controlStrength", 0f);
        SetBoolField(serializedObject, "enableStandingPoseHold", hipsOnlyPose);
        SetBoolField(serializedObject, "captureInitialPoseOnEnable", hipsOnlyPose);
        SetBoolField(serializedObject, "yawPoseTowardsMoveDirection", false);
        SetFloatField(serializedObject, "turnTorque", 0f);
        SetFloatField(serializedObject, "uprightStrength", 0f);
        SetFloatField(serializedObject, "chestUprightMultiplier", 0f);
        SetFloatField(serializedObject, "stopDragAssist", 0f);
        SetBoolField(serializedObject, "debugLogs", true);
        SetBoolField(serializedObject, "drawDebugGizmos", true);
        SetFloatField(serializedObject, "groundCheckRadius", 0.35f);
        SetFloatField(serializedObject, "groundCheckDistance", 1.2f);
        SetFloatField(serializedObject, "groundedPoseStrengthMultiplier", 1f);
        SetFloatField(serializedObject, "airbornePoseStrengthMultiplier", 1f);

        SetFloatField(serializedObject, "hipsPoseSpring", hipsOnlyPose ? 30f : 0f);
        SetFloatField(serializedObject, "hipsPoseDamping", hipsOnlyPose ? 8f : 0f);
        SetFloatField(serializedObject, "hipsMaxPoseTorque", hipsOnlyPose ? 40f : 0f);
        SetFloatField(serializedObject, "chestPoseSpring", 0f);
        SetFloatField(serializedObject, "chestPoseDamping", 0f);
        SetFloatField(serializedObject, "chestMaxPoseTorque", 0f);
        SetFloatField(serializedObject, "headPoseSpring", 0f);
        SetFloatField(serializedObject, "headPoseDamping", 0f);
        SetFloatField(serializedObject, "headMaxPoseTorque", 0f);
        SetFloatField(serializedObject, "armPoseSpring", 0f);
        SetFloatField(serializedObject, "armPoseDamping", 0f);
        SetFloatField(serializedObject, "armMaxPoseTorque", 0f);
    }

    private static void ApplyDiagnosticRigidbodyCollision(Rigidbody body, string fieldName, bool enableCollision)
    {
        if (body == null)
            return;

        if (enableCollision)
        {
            body.detectCollisions = true;
            body.isKinematic = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        else
        {
            body.detectCollisions = false;
        }

        EditorUtility.SetDirty(body);
        Debug.Log($"{LogPrefix} Diagnostic collision {fieldName}: DetectCollisions={body.detectCollisions} Kinematic={body.isKinematic} UseGravity={body.useGravity} Constraints={body.constraints}");
    }

    private static int DisableDiagnosticNonEssentialColliders(
        GameObject root,
        Transform controllersTransform,
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        int disabledCount = 0;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            Transform colliderTransform = collider.transform;
            if (controllersTransform != null && IsChildOf(colliderTransform, controllersTransform))
                continue;

            if (string.Equals(colliderTransform.name, FootSupportColliderName, StringComparison.Ordinal))
                continue;

            Rigidbody colliderBody = collider.GetComponent<Rigidbody>();
            if (colliderBody != null
                && IsAssignedRigidbody(colliderBody, hipsBody, chestBody, headBody, leftArmBody, rightArmBody))
            {
                continue;
            }

            string hierarchyPath = GetHierarchyPath(colliderTransform);
            bool shouldDisable = NameContainsAny(hierarchyPath, HurtboxBlockerNameFragments)
                || NameContainsAny(hierarchyPath, DiagnosticColliderNameFragments);
            if (!shouldDisable || !collider.enabled)
                continue;

            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            disabledCount++;
            Debug.Log($"{LogPrefix} Disabled diagnostic collider: {collider.GetType().Name} {hierarchyPath}");
        }

        return disabledCount;
    }

    private static void LogDiagnosticFootSupportSummary(Rigidbody hipsBody)
    {
        Transform supportTransform = hipsBody != null ? FindDirectChild(hipsBody.transform, FootSupportColliderName) : null;
        if (supportTransform == null)
        {
            Debug.LogWarning($"{LogPrefix} FootSupportCollider summary skipped because it is missing.");
            return;
        }

        BoxCollider boxCollider = supportTransform.GetComponent<BoxCollider>();
        Debug.Log($"{LogPrefix} FootSupportCollider localPosition={supportTransform.localPosition}");
        if (boxCollider != null)
            Debug.Log($"{LogPrefix} FootSupportCollider size={boxCollider.size}");
    }

    private static bool ApplyPhase1HipsShellIsolationPresetToSceneInstance(
        GameObject testInstance,
        Scene scene,
        out HamsterFullRagdollMotor motor)
    {
        motor = null;
        if (testInstance == null)
        {
            Debug.LogError($"{LogPrefix} Cannot apply Hips Shell Isolation because test instance is missing.");
            return false;
        }

        string sourcePath = GetPrefabSourcePath(testInstance);
        if (!string.Equals(sourcePath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Refusing to apply Hips Shell Isolation to non-destination prefab instance: {GetHierarchyPath(testInstance.transform)} Source={sourcePath}");
            return false;
        }

        GameObject controllers = GetOrCreateDirectChild(testInstance.transform, ControllersObjectName);
        motor = controllers.GetComponent<HamsterFullRagdollMotor>();
        if (motor == null)
        {
            motor = controllers.AddComponent<HamsterFullRagdollMotor>();
            Debug.Log($"{LogPrefix} Added HamsterFullRagdollMotor to {GetHierarchyPath(controllers.transform)} for Hips Shell Isolation preset.");
        }

        int disabledControllerComponents = DisableProductionAndNetworkComponents(testInstance);

        SerializedObject motorObject = new SerializedObject(motor);
        Rigidbody[] rigidbodies = testInstance.GetComponentsInChildren<Rigidbody>(true);
        Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody") ?? FindRigidbodyByCandidates(rigidbodies, HipsCandidates);
        Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody") ?? FindRigidbodyByCandidates(rigidbodies, ChestCandidates);
        Rigidbody headBody = GetObjectReference<Rigidbody>(motorObject, "headBody") ?? FindRigidbodyByCandidates(rigidbodies, HeadCandidates);
        Rigidbody leftArmBody = GetObjectReference<Rigidbody>(motorObject, "leftArmBody") ?? FindRigidbodyByCandidates(rigidbodies, LeftArmCandidates);
        Rigidbody rightArmBody = GetObjectReference<Rigidbody>(motorObject, "rightArmBody") ?? FindRigidbodyByCandidates(rigidbodies, RightArmCandidates);

        AssignRigidbodyField(motorObject, "hipsBody", hipsBody, required: true);
        AssignRigidbodyField(motorObject, "chestBody", chestBody, required: true);
        AssignRigidbodyField(motorObject, "headBody", headBody, required: false);
        AssignRigidbodyField(motorObject, "leftArmBody", leftArmBody, required: false);
        AssignRigidbodyField(motorObject, "rightArmBody", rightArmBody, required: false);
        AssignGroundMask(motorObject, ResolveSceneGroundMask(scene));
        ApplyPhase1HipsShellMotorValues(motorObject);
        motorObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(motor);

        EnsureFootSupportCollider(
            hipsBody,
            scene,
            new Vector3(0f, -0.04f, 0f),
            new Vector3(0.25f, 0.04f, 0.18f));

        ApplyHipsShellDynamicBody(hipsBody);
        int frozenBodyCount = FreezeAllNonHipsRigidbodies(testInstance, hipsBody);
        int jointCount = MinimizeJointInfluence(testInstance, hipsBody);
        int disabledColliderCount = DisableHipsShellNonEssentialColliders(testInstance, controllers.transform, hipsBody);
        int disabledHurtboxObjects = DisableHurtboxBlockerObjects(testInstance, controllers.transform, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);

        ValidateGroundMaskLayerState(scene, motorObject, hipsBody);
        LogDiagnosticFootSupportSummary(hipsBody);
        EditorUtility.SetDirty(testInstance);

        Debug.Log($"{LogPrefix} frozen non-hips rigidbody count: {frozenBodyCount}");
        Debug.Log($"{LogPrefix} dynamic hipsBody path: {(hipsBody != null ? GetHierarchyPath(hipsBody.transform) : "<missing>")}");
        Debug.Log($"{LogPrefix} joint count: {jointCount}");
        Debug.LogWarning($"{LogPrefix} Joints are still present; if hips shell still launches, create a joint-free shell test next.");
        Debug.Log($"{LogPrefix} disabled collider count: {disabledColliderCount} HurtboxBlockerObjects={disabledHurtboxObjects} DisabledControllerComponents={disabledControllerComponents}");
        Debug.Log($"{LogPrefix} Applied Hips Shell Isolation Preset.");
        return true;
    }

    private static bool CreateOrConfigureJointFreeMotorShell(Scene scene, out HamsterFullRagdollMotor motor)
    {
        motor = null;
        GameObject shellRoot = FindGameObjectByName(scene, JointFreeShellRootName);
        bool rootCreated = false;
        if (shellRoot == null)
        {
            shellRoot = new GameObject(JointFreeShellRootName);
            SceneManager.MoveGameObjectToScene(shellRoot, scene);
            rootCreated = true;
        }

        shellRoot.name = JointFreeShellRootName;
        shellRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        shellRoot.transform.localScale = Vector3.one;
        Debug.Log($"{LogPrefix} {(rootCreated ? "Created" : "Reused")} {JointFreeShellRootName}.");

        GameObject body = GetOrCreateDirectChild(shellRoot.transform, JointFreeShellBodyName);
        body.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        body.transform.localScale = Vector3.one;
        Debug.Log($"{LogPrefix} Created/Reused {JointFreeShellBodyName}.");

        GameObject visualRoot = GetOrCreateDirectChild(shellRoot.transform, JointFreeShellVisualRootName);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;
        Debug.Log($"{LogPrefix} Created/Reused {JointFreeShellVisualRootName}.");

        int shellLayer = ResolveShellBodyLayer(scene);
        body.layer = shellLayer;

        Rigidbody bodyRigidbody = body.GetComponent<Rigidbody>();
        bool rigidbodyAdded = false;
        if (bodyRigidbody == null)
        {
            bodyRigidbody = body.AddComponent<Rigidbody>();
            rigidbodyAdded = true;
        }

        ConfigureJointFreeShellRigidbody(bodyRigidbody);
        Debug.Log($"{LogPrefix} {(rigidbodyAdded ? "Added" : "Reused")} Rigidbody.");

        BoxCollider boxCollider = body.GetComponent<BoxCollider>();
        bool colliderAdded = false;
        if (boxCollider == null)
        {
            boxCollider = body.AddComponent<BoxCollider>();
            colliderAdded = true;
        }

        ConfigureJointFreeShellBoxCollider(boxCollider);
        DisableExtraShellBodyColliders(body, boxCollider);
        Debug.Log($"{LogPrefix} {(colliderAdded ? "Added" : "Reused")} BoxCollider.");

        motor = body.GetComponent<HamsterFullRagdollMotor>();
        bool motorAdded = false;
        if (motor == null)
        {
            motor = body.AddComponent<HamsterFullRagdollMotor>();
            motorAdded = true;
        }

        ConfigureJointFreeShellMotor(motor, bodyRigidbody, scene);
        Debug.Log($"{LogPrefix} {(motorAdded ? "Added" : "Reused")} HamsterFullRagdollMotor.");

        LogShellLayerCollision(body, scene);
        EditorUtility.SetDirty(shellRoot);
        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(visualRoot);
        EditorUtility.SetDirty(bodyRigidbody);
        EditorUtility.SetDirty(boxCollider);
        EditorUtility.SetDirty(motor);
        return true;
    }

    private static void ConfigureJointFreeShellRigidbody(Rigidbody bodyRigidbody)
    {
        bodyRigidbody.mass = 14f;
        SetRigidbodyDamping(bodyRigidbody, 0.1f, 6f);
        bodyRigidbody.useGravity = true;
        bodyRigidbody.isKinematic = false;
        bodyRigidbody.detectCollisions = true;
        bodyRigidbody.constraints = RigidbodyConstraints.None;
        bodyRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        bodyRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private static void ConfigureJointFreeShellBoxCollider(BoxCollider boxCollider)
    {
        boxCollider.size = new Vector3(0.45f, 0.55f, 0.35f);
        boxCollider.center = new Vector3(0f, 0.25f, 0f);
        boxCollider.isTrigger = false;
    }

    private static void DisableExtraShellBodyColliders(GameObject body, BoxCollider keptBoxCollider)
    {
        Collider[] colliders = body.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider == keptBoxCollider)
                continue;

            if (!collider.enabled)
                continue;

            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            Debug.LogWarning($"{LogPrefix} Disabled extra MotorShellBody collider: {collider.GetType().Name}");
        }
    }

    private static void ConfigureJointFreeShellMotor(HamsterFullRagdollMotor motor, Rigidbody bodyRigidbody, Scene scene)
    {
        SerializedObject motorObject = new SerializedObject(motor);
        AssignObjectField(motorObject, "hipsBody", bodyRigidbody, required: true);
        AssignObjectField(motorObject, "chestBody", bodyRigidbody, required: true);
        AssignObjectField(motorObject, "headBody", null, required: false);
        AssignObjectField(motorObject, "leftArmBody", null, required: false);
        AssignObjectField(motorObject, "rightArmBody", null, required: false);

        Camera mainCamera = Camera.main;
        AssignObjectField(motorObject, "cameraTransform", mainCamera != null ? mainCamera.transform : null, required: false);
        if (mainCamera != null)
            Debug.Log($"{LogPrefix} Assigned cameraTransform: {GetHierarchyPath(mainCamera.transform)}");
        else
            Debug.LogWarning($"{LogPrefix} Main Camera was not found. Shell motor cameraTransform remains empty.");

        AssignGroundMask(motorObject, ResolveSceneGroundMask(scene));
        SetFloatField(motorObject, "maxWalkSpeed", 1.5f);
        SetFloatField(motorObject, "maxSprintSpeed", 2.2f);
        SetFloatField(motorObject, "walkAcceleration", 8f);
        SetFloatField(motorObject, "sprintAcceleration", 12f);
        SetFloatField(motorObject, "chestForceMultiplier", 0f);
        SetFloatField(motorObject, "turnTorque", 0f);
        SetFloatField(motorObject, "turnDamping", 8f);
        SetFloatField(motorObject, "inputSmoothTime", 0.12f);
        SetFloatField(motorObject, "stopDragAssist", 1f);
        SetFloatField(motorObject, "airControlMultiplier", 0.35f);
        SetFloatField(motorObject, "controlStrength", 0f);
        SetFloatField(motorObject, "uprightStrength", 0f);
        SetFloatField(motorObject, "uprightDamping", 0f);
        SetFloatField(motorObject, "chestUprightMultiplier", 0f);
        SetFloatField(motorObject, "groundCheckRadius", 0.35f);
        SetFloatField(motorObject, "groundCheckDistance", 1.2f);
        SetBoolField(motorObject, "enableStandingPoseHold", false);
        SetBoolField(motorObject, "yawPoseTowardsMoveDirection", false);
        SetBoolField(motorObject, "disableTurnTorqueWhilePoseHold", true);
        SetBoolField(motorObject, "drawDebugGizmos", true);
        SetBoolField(motorObject, "debugLogs", true);
        motorObject.ApplyModifiedProperties();
        Debug.Log($"{LogPrefix} Assigned hipsBody/chestBody to MotorShellBody Rigidbody.");
        Debug.Log($"{LogPrefix} Assigned groundMask from Plane layer.");
    }

    private static int ResolveShellBodyLayer(Scene scene)
    {
        int defaultLayer = LayerMask.NameToLayer("Default");
        int playerLayer = LayerMask.NameToLayer("Player");
        GameObject plane = FindGameObjectByName(scene, "Plane");

        if (plane == null)
            return defaultLayer >= 0 ? defaultLayer : 0;

        if (defaultLayer >= 0 && !Physics.GetIgnoreLayerCollision(defaultLayer, plane.layer))
            return defaultLayer;

        if (playerLayer >= 0 && !Physics.GetIgnoreLayerCollision(playerLayer, plane.layer))
            return playerLayer;

        Debug.LogWarning($"{LogPrefix} Default/Player layers do not collide with Plane layer. Using Default without modifying ProjectSettings.");
        return defaultLayer >= 0 ? defaultLayer : 0;
    }

    private static void LogShellLayerCollision(GameObject body, Scene scene)
    {
        GameObject plane = FindGameObjectByName(scene, "Plane");
        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. Shell layer collision could not be checked.");
            return;
        }

        bool ignored = Physics.GetIgnoreLayerCollision(body.layer, plane.layer);
        Debug.Log($"{LogPrefix} MotorShellBody layer={LayerMask.LayerToName(body.layer)}({body.layer}) Plane layer={LayerMask.LayerToName(plane.layer)}({plane.layer}) CollisionIgnored={ignored}");
        if (ignored)
            Debug.LogError($"{LogPrefix} MotorShellBody layer and Plane layer collision is disabled. ProjectSettings are not modified automatically.");
    }

    private static void ValidateJointFreeMotorShell(Scene scene, GameObject existingSkinnedInstance)
    {
        GameObject shellRoot = FindGameObjectByName(scene, JointFreeShellRootName);
        if (shellRoot == null)
        {
            Debug.LogWarning($"{LogPrefix} Joint-Free Motor Shell is missing: {JointFreeShellRootName}");
            return;
        }

        Debug.Log($"{LogPrefix} Joint-Free Motor Shell found: {GetHierarchyPath(shellRoot.transform)}");
        Transform bodyTransform = FindDirectChild(shellRoot.transform, JointFreeShellBodyName);
        if (bodyTransform == null)
        {
            Debug.LogError($"{LogPrefix} {JointFreeShellBodyName} is missing.");
            return;
        }

        Rigidbody bodyRigidbody = bodyTransform.GetComponent<Rigidbody>();
        BoxCollider boxCollider = bodyTransform.GetComponent<BoxCollider>();
        HamsterFullRagdollMotor motor = bodyTransform.GetComponent<HamsterFullRagdollMotor>();

        if (bodyRigidbody == null)
            Debug.LogError($"{LogPrefix} MotorShellBody Rigidbody is missing.");
        else
            Debug.Log($"{LogPrefix} MotorShellBody Rigidbody found. Kinematic={bodyRigidbody.isKinematic} Gravity={bodyRigidbody.useGravity} DetectCollisions={bodyRigidbody.detectCollisions} Constraints={bodyRigidbody.constraints}");

        if (boxCollider == null)
            Debug.LogError($"{LogPrefix} MotorShellBody BoxCollider is missing.");
        else
            Debug.Log($"{LogPrefix} MotorShellBody BoxCollider found. Size={boxCollider.size} Center={boxCollider.center} Trigger={boxCollider.isTrigger}");

        Collider[] bodyColliders = bodyTransform.GetComponents<Collider>();
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            Collider collider = bodyColliders[i];
            if (collider != null && !(collider is BoxCollider) && collider.enabled)
                Debug.LogError($"{LogPrefix} MotorShellBody has enabled non-BoxCollider: {collider.GetType().Name}");
        }

        if (motor == null)
        {
            Debug.LogError($"{LogPrefix} MotorShellBody HamsterFullRagdollMotor is missing.");
        }
        else
        {
            SerializedObject motorObject = new SerializedObject(motor);
            Rigidbody hipsBody = GetObjectReference<Rigidbody>(motorObject, "hipsBody");
            Rigidbody chestBody = GetObjectReference<Rigidbody>(motorObject, "chestBody");
            int groundMask = GetIntField(motorObject, "groundMask");
            float controlStrength;

            if (hipsBody != bodyRigidbody)
                Debug.LogError($"{LogPrefix} Shell hipsBody should reference MotorShellBody Rigidbody.");
            else
                Debug.Log($"{LogPrefix} Shell hipsBody references MotorShellBody Rigidbody.");

            if (chestBody != bodyRigidbody)
                Debug.LogError($"{LogPrefix} Shell chestBody should reference MotorShellBody Rigidbody.");
            else
                Debug.Log($"{LogPrefix} Shell chestBody references MotorShellBody Rigidbody.");

            if (TryGetFloatField(motorObject, "controlStrength", out controlStrength))
                Debug.Log($"{LogPrefix} Shell controlStrength={controlStrength}");
            else
                Debug.LogWarning($"{LogPrefix} Shell controlStrength field was not found.");

            ValidateShellGroundMask(scene, bodyTransform.gameObject, groundMask);
        }

        Transform visualRoot = FindDirectChild(shellRoot.transform, JointFreeShellVisualRootName);
        if (visualRoot == null)
            Debug.LogWarning($"{LogPrefix} {JointFreeShellVisualRootName} is missing.");
        else
            Debug.Log($"{LogPrefix} {JointFreeShellVisualRootName} exists as visual-only placeholder.");

        if (existingSkinnedInstance != null && existingSkinnedInstance.activeInHierarchy)
            Debug.LogWarning($"{LogPrefix} Existing skinned ragdoll test instance is still active; disable it when testing shell only.");
    }

    private static void ValidateShellGroundMask(Scene scene, GameObject body, int groundMask)
    {
        GameObject plane = FindGameObjectByName(scene, "Plane");
        Debug.Log($"{LogPrefix} Shell groundMask layers={FormatLayerMask(groundMask)} Value={groundMask}");
        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. Shell groundMask/layer validation skipped.");
            return;
        }

        if ((groundMask & (1 << plane.layer)) == 0)
            Debug.LogError($"{LogPrefix} Shell groundMask does not include Plane layer. PlaneLayer={LayerMask.LayerToName(plane.layer)}({plane.layer})");
        else
            Debug.Log($"{LogPrefix} Shell groundMask includes Plane layer.");

        bool ignored = Physics.GetIgnoreLayerCollision(body.layer, plane.layer);
        Debug.Log($"{LogPrefix} Shell layer collision matrix MotorShellBody<->Plane ignored={ignored}");
        if (ignored)
            Debug.LogError($"{LogPrefix} MotorShellBody layer and Plane layer collision is disabled. ProjectSettings are not modified automatically.");
    }

    private static void ApplyPhase1HipsShellMotorValues(SerializedObject serializedObject)
    {
        SetFloatField(serializedObject, "controlStrength", 0f);
        SetBoolField(serializedObject, "enableStandingPoseHold", false);
        SetBoolField(serializedObject, "captureInitialPoseOnEnable", false);
        SetBoolField(serializedObject, "yawPoseTowardsMoveDirection", false);
        SetFloatField(serializedObject, "turnTorque", 0f);
        SetFloatField(serializedObject, "uprightStrength", 0f);
        SetFloatField(serializedObject, "chestUprightMultiplier", 0f);
        SetFloatField(serializedObject, "stopDragAssist", 0f);
        SetBoolField(serializedObject, "debugLogs", true);
        SetBoolField(serializedObject, "drawDebugGizmos", true);
        SetFloatField(serializedObject, "groundCheckRadius", 0.35f);
        SetFloatField(serializedObject, "groundCheckDistance", 1.8f);
        SetFloatField(serializedObject, "groundedPoseStrengthMultiplier", 1f);
        SetFloatField(serializedObject, "airbornePoseStrengthMultiplier", 1f);
        SetFloatField(serializedObject, "hipsPoseSpring", 0f);
        SetFloatField(serializedObject, "hipsPoseDamping", 0f);
        SetFloatField(serializedObject, "hipsMaxPoseTorque", 0f);
        SetFloatField(serializedObject, "chestPoseSpring", 0f);
        SetFloatField(serializedObject, "chestPoseDamping", 0f);
        SetFloatField(serializedObject, "chestMaxPoseTorque", 0f);
        SetFloatField(serializedObject, "headPoseSpring", 0f);
        SetFloatField(serializedObject, "headPoseDamping", 0f);
        SetFloatField(serializedObject, "headMaxPoseTorque", 0f);
        SetFloatField(serializedObject, "armPoseSpring", 0f);
        SetFloatField(serializedObject, "armPoseDamping", 0f);
        SetFloatField(serializedObject, "armMaxPoseTorque", 0f);
    }

    private static void ApplyHipsShellDynamicBody(Rigidbody hipsBody)
    {
        if (hipsBody == null)
        {
            Debug.LogError($"{LogPrefix} hipsBody is missing. Hips Shell Isolation cannot keep a dynamic shell body.");
            return;
        }

        hipsBody.isKinematic = false;
        hipsBody.useGravity = true;
        hipsBody.detectCollisions = true;
        ClearRigidbodyVelocities(hipsBody);
        hipsBody.constraints = RigidbodyConstraints.None;
        hipsBody.interpolation = RigidbodyInterpolation.Interpolate;
        hipsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        hipsBody.mass = 14f;
        SetRigidbodyDamping(hipsBody, 0.1f, 6f);
        EditorUtility.SetDirty(hipsBody);
    }

    private static int FreezeAllNonHipsRigidbodies(GameObject root, Rigidbody hipsBody)
    {
        int frozenCount = 0;
        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            if (body == null || body == hipsBody)
                continue;

            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = false;
            ClearRigidbodyVelocities(body);
            body.constraints = RigidbodyConstraints.FreezeAll;
            EditorUtility.SetDirty(body);
            frozenCount++;
            Debug.Log($"{LogPrefix} Frozen non-hips Rigidbody: {GetHierarchyPath(body.transform)}");
        }

        return frozenCount;
    }

    private static void ClearRigidbodyVelocities(Rigidbody body)
    {
        if (body == null)
            return;

#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = Vector3.zero;
#else
        body.velocity = Vector3.zero;
#endif
        body.angularVelocity = Vector3.zero;
    }

    private static int MinimizeJointInfluence(GameObject root, Rigidbody hipsBody)
    {
        Joint[] joints = root.GetComponentsInChildren<Joint>(true);
        for (int i = 0; i < joints.Length; i++)
        {
            Joint joint = joints[i];
            if (joint == null)
                continue;

            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            EditorUtility.SetDirty(joint);
            string connectedBodyPath = joint.connectedBody != null ? GetHierarchyPath(joint.connectedBody.transform) : "<none>";
            Debug.Log($"{LogPrefix} Joint present: {joint.GetType().Name} Path={GetHierarchyPath(joint.transform)} ConnectedBody={connectedBodyPath} ConnectedToHips={joint.connectedBody == hipsBody}");
        }

        return joints.Length;
    }

    private static int DisableHipsShellNonEssentialColliders(GameObject root, Transform controllersTransform, Rigidbody hipsBody)
    {
        int disabledCount = 0;
        int keptCount = 0;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            Transform colliderTransform = collider.transform;
            if (IsHipsShellKeptCollider(collider, hipsBody))
            {
                if (!collider.enabled)
                {
                    collider.enabled = true;
                    EditorUtility.SetDirty(collider);
                }

                keptCount++;
                Debug.Log($"{LogPrefix} Kept hips shell collider: {collider.GetType().Name} {GetHierarchyPath(colliderTransform)}");
                continue;
            }

            if (controllersTransform != null && IsChildOf(colliderTransform, controllersTransform))
                continue;

            if (!collider.enabled)
                continue;

            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            disabledCount++;
            Debug.Log($"{LogPrefix} Disabled hips shell collider: {collider.GetType().Name} {GetHierarchyPath(colliderTransform)}");
        }

        Debug.Log($"{LogPrefix} kept collider count: {keptCount}");
        return disabledCount;
    }

    private static bool IsHipsShellKeptCollider(Collider collider, Rigidbody hipsBody)
    {
        if (collider == null)
            return false;

        if (string.Equals(collider.transform.name, FootSupportColliderName, StringComparison.Ordinal))
            return collider is BoxCollider;

        return hipsBody != null && collider.gameObject == hipsBody.gameObject;
    }

    private static void ApplyPhase1MotorValues(SerializedObject serializedObject)
    {
        SetBoolField(serializedObject, "enableStandingPoseHold", true);
        SetBoolField(serializedObject, "captureInitialPoseOnEnable", true);
        SetBoolField(serializedObject, "yawPoseTowardsMoveDirection", false);
        SetFloatField(serializedObject, "poseYawSmoothTime", 0.18f);
        SetBoolField(serializedObject, "disableTurnTorqueWhilePoseHold", true);

        SetFloatField(serializedObject, "turnTorque", 0f);
        SetFloatField(serializedObject, "chestForceMultiplier", 0.15f);
        SetFloatField(serializedObject, "stopDragAssist", 1f);
        SetFloatField(serializedObject, "controlStrength", 1f);

        SetFloatField(serializedObject, "uprightStrength", 0f);
        SetFloatField(serializedObject, "chestUprightMultiplier", 0f);
        SetFloatField(serializedObject, "groundCheckRadius", 0.35f);
        SetFloatField(serializedObject, "groundCheckDistance", 1.2f);
        SetFloatField(serializedObject, "groundedPoseStrengthMultiplier", 1f);
        SetFloatField(serializedObject, "airbornePoseStrengthMultiplier", 1f);

        SetFloatField(serializedObject, "hipsPoseSpring", 80f);
        SetFloatField(serializedObject, "hipsPoseDamping", 12f);
        SetFloatField(serializedObject, "hipsMaxPoseTorque", 120f);
        SetFloatField(serializedObject, "chestPoseSpring", 50f);
        SetFloatField(serializedObject, "chestPoseDamping", 8f);
        SetFloatField(serializedObject, "chestMaxPoseTorque", 80f);
        SetFloatField(serializedObject, "headPoseSpring", 20f);
        SetFloatField(serializedObject, "headPoseDamping", 5f);
        SetFloatField(serializedObject, "headMaxPoseTorque", 40f);
        SetFloatField(serializedObject, "armPoseSpring", 10f);
        SetFloatField(serializedObject, "armPoseDamping", 3f);
        SetFloatField(serializedObject, "armMaxPoseTorque", 25f);

        SetBoolField(serializedObject, "drawDebugGizmos", true);
        SetBoolField(serializedObject, "debugLogs", true);
    }

    private static void EnsureFootSupportCollider(Rigidbody hipsBody, Scene scene)
    {
        EnsureFootSupportCollider(
            hipsBody,
            scene,
            new Vector3(0f, -0.08f, 0f),
            new Vector3(0.35f, 0.05f, 0.25f));
    }

    private static void EnsureFootSupportCollider(
        Rigidbody hipsBody,
        Scene scene,
        Vector3 localPosition,
        Vector3 boxSize)
    {
        if (hipsBody == null)
        {
            Debug.LogError($"{LogPrefix} hipsBody is missing. FootSupportCollider creation skipped.");
            return;
        }

        Transform supportTransform = FindDirectChild(hipsBody.transform, FootSupportColliderName);
        bool created = false;
        if (supportTransform == null)
        {
            GameObject supportObject = new GameObject(FootSupportColliderName);
            supportTransform = supportObject.transform;
            created = true;
        }

        supportTransform.name = FootSupportColliderName;
        supportTransform.SetParent(hipsBody.transform, false);
        supportTransform.gameObject.SetActive(true);
        supportTransform.localPosition = localPosition;
        supportTransform.localRotation = Quaternion.identity;
        supportTransform.localScale = Vector3.one;
        supportTransform.gameObject.layer = hipsBody.gameObject.layer;

        BoxCollider boxCollider = supportTransform.GetComponent<BoxCollider>();
        bool colliderAdded = false;
        if (boxCollider == null)
        {
            boxCollider = supportTransform.gameObject.AddComponent<BoxCollider>();
            colliderAdded = true;
        }

        boxCollider.isTrigger = false;
        boxCollider.size = boxSize;
        boxCollider.center = Vector3.zero;
        CorrectFootSupportPlaneOverlap(supportTransform, boxCollider, scene);

        if (supportTransform.GetComponent<Rigidbody>() != null)
            Debug.LogWarning($"{LogPrefix} FootSupportCollider has a Rigidbody. Remove it manually; Phase 1 support should be a compound collider only.");

        Collider[] supportColliders = supportTransform.GetComponents<Collider>();
        if (supportColliders.Length > 1)
            Debug.LogWarning($"{LogPrefix} FootSupportCollider has multiple Colliders. Keep only one BoxCollider for Phase 1.");

        for (int i = 0; i < supportColliders.Length; i++)
        {
            if (supportColliders[i] != null && !(supportColliders[i] is BoxCollider))
                Debug.LogWarning($"{LogPrefix} FootSupportCollider has an extra non-BoxCollider: {supportColliders[i].GetType().Name}. Remove it manually for Phase 1.");
        }

        EditorUtility.SetDirty(supportTransform.gameObject);
        EditorUtility.SetDirty(boxCollider);
        Debug.Log($"{LogPrefix} FootSupportCollider {(created ? "created" : "reused")}: {GetHierarchyPath(supportTransform)}");
        Debug.Log($"{LogPrefix} BoxCollider {(colliderAdded ? "added" : "reused")} on FootSupportCollider.");
        LogFootSupportPlacement(supportTransform, boxCollider, scene);
        Debug.LogWarning($"{LogPrefix} FootSupportCollider may need manual reposition and size tuning in Scene View.");
        ValidateFootSupportGroundCollision(supportTransform.gameObject, scene);
    }

    private static void CorrectFootSupportPlaneOverlap(Transform supportTransform, BoxCollider boxCollider, Scene scene)
    {
        if (supportTransform == null || boxCollider == null)
            return;

        GameObject plane = FindGameObjectByName(scene, "Plane");
        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. FootSupportCollider vertical overlap correction skipped.");
            return;
        }

        float planeY = plane.transform.position.y;
        Physics.SyncTransforms();
        Bounds bounds = boxCollider.bounds;
        float correctionAmount = 0f;
        if (bounds.min.y < planeY + 0.01f)
        {
            correctionAmount = (planeY + 0.02f) - bounds.min.y;
            supportTransform.position += Vector3.up * correctionAmount;
            Physics.SyncTransforms();
        }

        Bounds correctedBounds = boxCollider.bounds;
        Debug.Log($"{LogPrefix} FootSupportCollider PlaneY={planeY:F4} BoundsBeforeMinY={bounds.min.y:F4} BoundsBeforeMaxY={bounds.max.y:F4} AppliedVerticalCorrection={correctionAmount:F4} BoundsAfterMinY={correctedBounds.min.y:F4} BoundsAfterMaxY={correctedBounds.max.y:F4}");
    }

    private static void LogFootSupportPlacement(Transform supportTransform, BoxCollider boxCollider, Scene scene)
    {
        if (supportTransform == null || boxCollider == null)
            return;

        GameObject plane = FindGameObjectByName(scene, "Plane");
        string planeText = plane != null ? plane.transform.position.y.ToString("F4") : "<missing>";
        Bounds bounds = boxCollider.bounds;
        Debug.Log($"{LogPrefix} FootSupportCollider localPosition={supportTransform.localPosition} worldPosition={supportTransform.position}");
        Debug.Log($"{LogPrefix} FootSupportCollider bounds minY={bounds.min.y:F4} maxY={bounds.max.y:F4} PlaneY={planeText}");
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        Transform[] children = parent.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == parent)
                continue;

            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static void DisableRootPhase1Components(
        GameObject root,
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        Animator rootAnimator = root.GetComponent<Animator>();
        if (rootAnimator != null && rootAnimator.enabled)
        {
            rootAnimator.enabled = false;
            EditorUtility.SetDirty(rootAnimator);
            Debug.Log($"{LogPrefix} Disabled root Animator.");
        }

        CapsuleCollider rootCapsule = root.GetComponent<CapsuleCollider>();
        if (rootCapsule != null && rootCapsule.enabled)
        {
            rootCapsule.enabled = false;
            EditorUtility.SetDirty(rootCapsule);
            Debug.Log($"{LogPrefix} Disabled root CapsuleCollider.");
        }

        CharacterController rootCharacterController = root.GetComponent<CharacterController>();
        if (rootCharacterController != null && rootCharacterController.enabled)
        {
            rootCharacterController.enabled = false;
            EditorUtility.SetDirty(rootCharacterController);
            Debug.Log($"{LogPrefix} Disabled root CharacterController.");
        }

        Rigidbody rootBody = root.GetComponent<Rigidbody>();
        if (rootBody != null && !IsAssignedRigidbody(rootBody, hipsBody, chestBody, headBody, leftArmBody, rightArmBody))
        {
            rootBody.isKinematic = true;
            rootBody.useGravity = false;
            rootBody.detectCollisions = false;
            EditorUtility.SetDirty(rootBody);
            Debug.Log($"{LogPrefix} disabled unused root rigidbody physics: {GetHierarchyPath(rootBody.transform)}");
        }
    }

    private static int DisableHurtboxBlockerObjects(
        GameObject root,
        Transform controllersTransform,
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        int disabledCount = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            GameObject candidateObject = candidate.gameObject;
            if (candidateObject == root)
                continue;

            if (controllersTransform != null && IsChildOf(candidate, controllersTransform))
                continue;

            Rigidbody candidateBody = candidateObject.GetComponent<Rigidbody>();
            if (candidateBody != null
                && IsAssignedRigidbody(candidateBody, hipsBody, chestBody, headBody, leftArmBody, rightArmBody))
            {
                continue;
            }

            if (!NameContainsAny(candidateObject.name, HurtboxBlockerNameFragments))
                continue;

            if (IsLikelyPuppetBodyRigidbody(candidateObject))
                continue;

            if (!candidateObject.activeSelf)
                continue;

            candidateObject.SetActive(false);
            EditorUtility.SetDirty(candidateObject);
            disabledCount++;
            Debug.Log($"{LogPrefix} Disabled hurtbox/blocker object: {GetHierarchyPath(candidate)}");
        }

        return disabledCount;
    }

    private static bool IsLikelyPuppetBodyRigidbody(GameObject candidate)
    {
        if (candidate.GetComponent<Rigidbody>() == null)
            return false;

        string normalizedName = NormalizeName(candidate.name);
        return normalizedName == "hips"
            || normalizedName == "hip"
            || normalizedName == "pelvis"
            || normalizedName == "belly"
            || normalizedName == "body"
            || normalizedName == "spine"
            || normalizedName == "chest"
            || normalizedName == "head"
            || normalizedName.Contains("upperarm")
            || normalizedName == "arml"
            || normalizedName == "armr"
            || normalizedName == "leftarm"
            || normalizedName == "rightarm";
    }

    private static bool NameContainsAny(string name, string[] fragments)
    {
        for (int i = 0; i < fragments.Length; i++)
        {
            if (ContainsIgnoreCase(name, fragments[i]))
                return true;
        }

        return false;
    }

    private static bool IsAssignedRigidbody(Rigidbody body, params Rigidbody[] assignedBodies)
    {
        if (body == null)
            return false;

        for (int i = 0; i < assignedBodies.Length; i++)
        {
            if (body == assignedBodies[i])
                return true;
        }

        return false;
    }

    private static void ApplyPhase1RigidbodyDefaults(
        Rigidbody body,
        string fieldName,
        float mass,
        float linearDamping,
        float angularDamping,
        CollisionDetectionMode collisionDetectionMode)
    {
        if (body == null)
            return;

        Debug.Log($"{LogPrefix} Phase 1 Rigidbody before {fieldName}: Path={GetHierarchyPath(body.transform)} Mass={body.mass} Kinematic={body.isKinematic} Gravity={body.useGravity} Constraints={body.constraints}");
        body.mass = mass;
        SetRigidbodyDamping(body, linearDamping, angularDamping);
        body.useGravity = true;
        body.isKinematic = false;
        body.detectCollisions = true;
        body.constraints = RigidbodyConstraints.None;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = collisionDetectionMode;
        EditorUtility.SetDirty(body);
        Debug.Log($"{LogPrefix} Phase 1 Rigidbody after {fieldName}: Mass={mass} LinearDamping={linearDamping} AngularDamping={angularDamping} Collision={collisionDetectionMode} DetectCollisions={body.detectCollisions} Constraints={body.constraints}");
    }

    private static void ValidatePhase1StandingSupportState(
        Scene scene,
        GameObject testInstance,
        SerializedObject motorObject,
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        ValidateFootSupportCollider(hipsBody, scene);
        ValidateRootPhase1Components(testInstance, hipsBody, chestBody, headBody, leftArmBody, rightArmBody);

        float controlStrength;
        bool diagnosticMode = TryGetFloatField(motorObject, "controlStrength", out controlStrength)
            && Mathf.Abs(controlStrength) <= 0.0001f;
        bool poseHoldEnabled;
        TryGetBoolField(motorObject, "enableStandingPoseHold", out poseHoldEnabled);

        if (diagnosticMode)
        {
            Debug.Log($"{LogPrefix} controlStrength == 0. Diagnostic mode is active.");
            if (!poseHoldEnabled)
                Debug.Log($"{LogPrefix} enableStandingPoseHold is false. No-pose collision test mode is active.");
            else
                Debug.Log($"{LogPrefix} enableStandingPoseHold is true. Hips-only pose diagnostic mode may be active.");

            float groundCheckDistance;
            bool hipsShellMode = !poseHoldEnabled
                && TryGetFloatField(motorObject, "groundCheckDistance", out groundCheckDistance)
                && groundCheckDistance >= 1.79f;
            if (hipsShellMode)
                ValidateHipsShellIsolationState(testInstance, motorObject, hipsBody);
            else
            {
                ValidateDiagnosticRigidbodyCollisionState(hipsBody, chestBody, headBody, leftArmBody, rightArmBody);
                ValidateDiagnosticMotorState(motorObject, poseHoldEnabled);
                ValidateNonEssentialColliderState(testInstance);
            }

            Debug.Log($"{LogPrefix} Runtime observation required: verify hasMoveInput=false does not produce high planarSpeed in Play Mode.");
        }
        else
        {
            ValidatePhase1Rigidbody("hipsBody", hipsBody, required: true);
            ValidatePhase1Rigidbody("chestBody", chestBody, required: true);
            ValidatePhase1Rigidbody("headBody", headBody, required: false);
            ValidatePhase1Rigidbody("leftArmBody", leftArmBody, required: false);
            ValidatePhase1Rigidbody("rightArmBody", rightArmBody, required: false);

            ValidateBoolFieldEquals(motorObject, "enableStandingPoseHold", true);
            ValidateFloatFieldEquals(motorObject, "stopDragAssist", 1f);
            ValidateFloatFieldEquals(motorObject, "hipsPoseSpring", 80f);
            ValidateFloatFieldEquals(motorObject, "hipsPoseDamping", 12f);
            ValidateFloatFieldEquals(motorObject, "hipsMaxPoseTorque", 120f);
            ValidateFloatFieldEquals(motorObject, "chestPoseSpring", 50f);
            ValidateFloatFieldEquals(motorObject, "chestPoseDamping", 8f);
            ValidateFloatFieldEquals(motorObject, "chestMaxPoseTorque", 80f);
            ValidateFloatFieldEquals(motorObject, "headPoseSpring", 20f);
            ValidateFloatFieldEquals(motorObject, "headPoseDamping", 5f);
            ValidateFloatFieldEquals(motorObject, "headMaxPoseTorque", 40f);
            ValidateFloatFieldEquals(motorObject, "armPoseSpring", 10f);
            ValidateFloatFieldEquals(motorObject, "armPoseDamping", 3f);
            ValidateFloatFieldEquals(motorObject, "armMaxPoseTorque", 25f);
        }

        ValidateBoolFieldEquals(motorObject, "yawPoseTowardsMoveDirection", false);
        ValidateFloatFieldEquals(motorObject, "turnTorque", 0f);
        ValidateFloatFieldEquals(motorObject, "uprightStrength", 0f);
        ValidateFloatFieldEquals(motorObject, "chestUprightMultiplier", 0f);
        ValidateFloatFieldAtLeast(motorObject, "groundCheckRadius", 0.3f);
        ValidateFloatFieldAtLeast(motorObject, "groundCheckDistance", 1.0f);
        ValidateFloatFieldEquals(motorObject, "groundedPoseStrengthMultiplier", 1f);
        ValidateFloatFieldEquals(motorObject, "airbornePoseStrengthMultiplier", 1f);

        int groundMask = GetIntField(motorObject, "groundMask");
        if (groundMask == 0)
            Debug.LogError($"{LogPrefix} Phase 1 validation failed: groundMask is empty.");
        else
            Debug.Log($"{LogPrefix} Phase 1 validation passed: groundMask is set.");

        ValidateGroundMaskLayerState(scene, motorObject, hipsBody);
    }

    private static void ValidateDiagnosticRigidbodyCollisionState(
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        ValidateDiagnosticBodyCollision("hipsBody", hipsBody, expectedDetectCollisions: true, required: true);
        ValidateDiagnosticBodyCollision("chestBody", chestBody, expectedDetectCollisions: false, required: true);
        ValidateDiagnosticBodyCollision("headBody", headBody, expectedDetectCollisions: false, required: false);
        ValidateDiagnosticBodyCollision("leftArmBody", leftArmBody, expectedDetectCollisions: false, required: false);
        ValidateDiagnosticBodyCollision("rightArmBody", rightArmBody, expectedDetectCollisions: false, required: false);
    }

    private static void ValidateDiagnosticBodyCollision(
        string fieldName,
        Rigidbody body,
        bool expectedDetectCollisions,
        bool required)
    {
        if (body == null)
        {
            if (required)
                Debug.LogError($"{LogPrefix} {fieldName} is missing; cannot validate diagnostic collision state.");
            else
                Debug.LogWarning($"{LogPrefix} {fieldName} is not assigned; optional diagnostic collision validation skipped.");

            return;
        }

        if (body.detectCollisions != expectedDetectCollisions)
            Debug.LogError($"{LogPrefix} {fieldName} detectCollisions should be {expectedDetectCollisions}. Current={body.detectCollisions}");
        else
            Debug.Log($"{LogPrefix} {fieldName} detectCollisions matches diagnostic expected value: {expectedDetectCollisions}");

        if (expectedDetectCollisions)
        {
            if (body.isKinematic)
                Debug.LogError($"{LogPrefix} {fieldName} should be non-kinematic in diagnostic preset.");
            if (!body.useGravity)
                Debug.LogError($"{LogPrefix} {fieldName} useGravity should be true in diagnostic preset.");
            if (body.constraints != RigidbodyConstraints.None)
                Debug.LogError($"{LogPrefix} {fieldName} constraints should be None in diagnostic preset. Current={body.constraints}");
        }
    }

    private static void ValidateDiagnosticMotorState(SerializedObject motorObject, bool poseHoldEnabled)
    {
        ValidateFloatFieldEquals(motorObject, "controlStrength", 0f);
        ValidateFloatFieldEquals(motorObject, "stopDragAssist", 0f);
        ValidateBoolFieldEquals(motorObject, "debugLogs", true);
        ValidateBoolFieldEquals(motorObject, "drawDebugGizmos", true);

        if (!poseHoldEnabled)
        {
            ValidateBoolFieldEquals(motorObject, "enableStandingPoseHold", false);
            Debug.Log($"{LogPrefix} Phase 1 isolation OK: pose hold is disabled.");
            return;
        }

        ValidateBoolFieldEquals(motorObject, "enableStandingPoseHold", true);
        ValidateFloatFieldEquals(motorObject, "hipsPoseSpring", 30f);
        ValidateFloatFieldEquals(motorObject, "hipsPoseDamping", 8f);
        ValidateFloatFieldEquals(motorObject, "hipsMaxPoseTorque", 40f);
        ValidateFloatFieldEquals(motorObject, "chestPoseSpring", 0f);
        ValidateFloatFieldEquals(motorObject, "chestPoseDamping", 0f);
        ValidateFloatFieldEquals(motorObject, "chestMaxPoseTorque", 0f);
        ValidateFloatFieldEquals(motorObject, "headPoseSpring", 0f);
        ValidateFloatFieldEquals(motorObject, "headPoseDamping", 0f);
        ValidateFloatFieldEquals(motorObject, "headMaxPoseTorque", 0f);
        ValidateFloatFieldEquals(motorObject, "armPoseSpring", 0f);
        ValidateFloatFieldEquals(motorObject, "armPoseDamping", 0f);
        ValidateFloatFieldEquals(motorObject, "armMaxPoseTorque", 0f);
    }

    private static void ValidateNonEssentialColliderState(GameObject root)
    {
        int enabledDiagnosticColliders = 0;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (string.Equals(collider.transform.name, FootSupportColliderName, StringComparison.Ordinal))
                continue;

            string hierarchyPath = GetHierarchyPath(collider.transform);
            if (!NameContainsAny(hierarchyPath, DiagnosticColliderNameFragments))
                continue;

            enabledDiagnosticColliders++;
            Debug.LogWarning($"{LogPrefix} Tail/Legs/Feet collider is still enabled: {collider.GetType().Name} {hierarchyPath}");
        }

        if (enabledDiagnosticColliders == 0)
            Debug.Log($"{LogPrefix} Phase 1 isolation OK: Tail/Legs/Feet diagnostic colliders are disabled.");
    }

    private static void ValidateHipsShellIsolationState(GameObject root, SerializedObject motorObject, Rigidbody hipsBody)
    {
        Debug.Log($"{LogPrefix} Hips Shell Isolation validation is active.");
        ValidateFloatFieldEquals(motorObject, "controlStrength", 0f);
        ValidateBoolFieldEquals(motorObject, "enableStandingPoseHold", false);
        ValidateFloatFieldEquals(motorObject, "groundCheckDistance", 1.8f);
        ValidateHipsShellRigidbodies(root, hipsBody);
        ValidateHipsShellColliders(root, hipsBody);
        ValidateHipsShellJoints(root);
        ValidateHipsShellRootRigidbody(root, hipsBody);
    }

    private static void ValidateHipsShellRigidbodies(GameObject root, Rigidbody hipsBody)
    {
        if (hipsBody == null)
        {
            Debug.LogError($"{LogPrefix} Hips Shell validation failed: hipsBody is missing.");
            return;
        }

        if (hipsBody.isKinematic)
            Debug.LogError($"{LogPrefix} hipsBody should be dynamic. isKinematic=true");
        if (!hipsBody.useGravity)
            Debug.LogError($"{LogPrefix} hipsBody useGravity should be true.");
        if (!hipsBody.detectCollisions)
            Debug.LogError($"{LogPrefix} hipsBody detectCollisions should be true.");
        if (hipsBody.constraints != RigidbodyConstraints.None)
            Debug.LogError($"{LogPrefix} hipsBody constraints should be None. Current={hipsBody.constraints}");

        int frozenCount = 0;
        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody body = rigidbodies[i];
            if (body == null || body == hipsBody)
                continue;

            bool frozen = body.isKinematic
                && !body.useGravity
                && !body.detectCollisions
                && body.constraints == RigidbodyConstraints.FreezeAll;
            if (!frozen)
            {
                Debug.LogError($"{LogPrefix} Non-hips Rigidbody is not fully frozen: {GetHierarchyPath(body.transform)} Kinematic={body.isKinematic} Gravity={body.useGravity} DetectCollisions={body.detectCollisions} Constraints={body.constraints}");
                continue;
            }

            frozenCount++;
        }

        Debug.Log($"{LogPrefix} Hips Shell frozen non-hips Rigidbody count: {frozenCount}");
        Debug.Log($"{LogPrefix} Hips Shell dynamic hipsBody path: {GetHierarchyPath(hipsBody.transform)}");
    }

    private static void ValidateHipsShellColliders(GameObject root, Rigidbody hipsBody)
    {
        int keptCount = 0;
        int disabledCount = 0;
        int enabledUnexpectedCount = 0;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (IsHipsShellKeptCollider(collider, hipsBody))
            {
                keptCount++;
                if (!collider.enabled)
                    Debug.LogError($"{LogPrefix} Hips Shell kept collider should be enabled: {collider.GetType().Name} {GetHierarchyPath(collider.transform)}");
                else
                    Debug.Log($"{LogPrefix} Hips Shell kept collider: {collider.GetType().Name} {GetHierarchyPath(collider.transform)}");

                continue;
            }

            if (collider.enabled)
            {
                enabledUnexpectedCount++;
                Debug.LogError($"{LogPrefix} Hips Shell non-kept collider is still enabled: {collider.GetType().Name} {GetHierarchyPath(collider.transform)}");
            }
            else
            {
                disabledCount++;
            }
        }

        Debug.Log($"{LogPrefix} Hips Shell kept collider count: {keptCount}");
        Debug.Log($"{LogPrefix} Hips Shell disabled collider count: {disabledCount}");
        if (enabledUnexpectedCount == 0)
            Debug.Log($"{LogPrefix} Hips Shell collider isolation OK.");
    }

    private static void ValidateHipsShellJoints(GameObject root)
    {
        Joint[] joints = root.GetComponentsInChildren<Joint>(true);
        Debug.Log($"{LogPrefix} Hips Shell joint count: {joints.Length}");
        for (int i = 0; i < joints.Length; i++)
        {
            Joint joint = joints[i];
            if (joint == null)
                continue;

            Debug.LogWarning($"{LogPrefix} Joint remains in Hips Shell test: {joint.GetType().Name} Path={GetHierarchyPath(joint.transform)} EnableCollision={joint.enableCollision} EnablePreprocessing={joint.enablePreprocessing}");
        }

        if (joints.Length > 0)
            Debug.LogWarning($"{LogPrefix} Joints are still present; if hips shell still launches, create a joint-free shell test next.");
    }

    private static void ValidateHipsShellRootRigidbody(GameObject root, Rigidbody hipsBody)
    {
        Rigidbody rootBody = root.GetComponent<Rigidbody>();
        if (rootBody == null || rootBody == hipsBody)
            return;

        bool disabled = rootBody.isKinematic
            && !rootBody.useGravity
            && !rootBody.detectCollisions
            && rootBody.constraints == RigidbodyConstraints.FreezeAll;
        if (!disabled)
            Debug.LogError($"{LogPrefix} Root Rigidbody should be disabled/frozen for Hips Shell Isolation. Kinematic={rootBody.isKinematic} Gravity={rootBody.useGravity} DetectCollisions={rootBody.detectCollisions} Constraints={rootBody.constraints}");
        else
            Debug.Log($"{LogPrefix} Root Rigidbody is disabled/frozen for Hips Shell Isolation.");
    }

    private static void ValidateFootSupportCollider(Rigidbody hipsBody, Scene scene)
    {
        if (hipsBody == null)
        {
            Debug.LogError($"{LogPrefix} FootSupportCollider validation skipped because hipsBody is missing.");
            return;
        }

        Transform supportTransform = FindDirectChild(hipsBody.transform, FootSupportColliderName);
        if (supportTransform == null)
        {
            Debug.LogError($"{LogPrefix} FootSupportCollider is missing under hipsBody.");
            return;
        }

        Vector3 localPosition = supportTransform.localPosition;
        if (localPosition.magnitude > 1f)
            Debug.LogWarning($"{LogPrefix} FootSupportCollider localPosition magnitude is large. LocalPosition={localPosition}");

        if (Mathf.Abs(localPosition.x) > 0.5f)
            Debug.LogWarning($"{LogPrefix} FootSupportCollider localPosition.x is large. LocalPosition={localPosition}");

        BoxCollider boxCollider = supportTransform.GetComponent<BoxCollider>();
        if (boxCollider == null)
            Debug.LogError($"{LogPrefix} FootSupportCollider is missing BoxCollider.");
        else
        {
            Debug.Log($"{LogPrefix} FootSupportCollider has BoxCollider. Size={boxCollider.size} Center={boxCollider.center}");

            if (boxCollider.size.y > 0.12f)
                Debug.LogWarning($"{LogPrefix} FootSupportCollider BoxCollider height is large. Size={boxCollider.size}");

            if (boxCollider.isTrigger)
                Debug.LogError($"{LogPrefix} FootSupportCollider BoxCollider must not be trigger.");
            else
                Debug.Log($"{LogPrefix} FootSupportCollider BoxCollider is non-trigger.");

            ValidateFootSupportPlaneOverlap(boxCollider, scene);
        }

        Collider[] supportColliders = supportTransform.GetComponents<Collider>();
        if (supportColliders.Length != 1)
            Debug.LogError($"{LogPrefix} FootSupportCollider should have exactly one Collider. CurrentCount={supportColliders.Length}");

        for (int i = 0; i < supportColliders.Length; i++)
        {
            if (supportColliders[i] != null && !(supportColliders[i] is BoxCollider))
                Debug.LogError($"{LogPrefix} FootSupportCollider should only use BoxCollider. Extra={supportColliders[i].GetType().Name}");
        }

        if (supportTransform.GetComponent<Rigidbody>() != null)
            Debug.LogError($"{LogPrefix} FootSupportCollider must not have a Rigidbody.");
        else
            Debug.Log($"{LogPrefix} FootSupportCollider has no Rigidbody.");

        ValidateFootSupportGroundCollision(supportTransform.gameObject, scene);
    }

    private static void ValidateFootSupportPlaneOverlap(BoxCollider boxCollider, Scene scene)
    {
        if (boxCollider == null)
            return;

        GameObject plane = FindGameObjectByName(scene, "Plane");
        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. FootSupportCollider Plane overlap validation skipped.");
            return;
        }

        float planeY = plane.transform.position.y;
        Physics.SyncTransforms();
        Bounds bounds = boxCollider.bounds;
        Debug.Log($"{LogPrefix} FootSupportCollider bounds minY={bounds.min.y:F4} maxY={bounds.max.y:F4} PlaneY={planeY:F4}");
        if (bounds.min.y < planeY)
            Debug.LogError($"{LogPrefix} FootSupportCollider penetrates below Plane. BoundsMinY={bounds.min.y:F4} PlaneY={planeY:F4}");
    }

    private static void ValidateFootSupportGroundCollision(GameObject supportObject, Scene scene)
    {
        if (supportObject == null)
            return;

        GameObject plane = FindGameObjectByName(scene, "Plane");
        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. Ground/Puppet layer collision matrix should be checked manually.");
            return;
        }

        bool ignored = Physics.GetIgnoreLayerCollision(supportObject.layer, plane.layer);
        string supportLayerName = LayerMask.LayerToName(supportObject.layer);
        string planeLayerName = LayerMask.LayerToName(plane.layer);
        if (ignored)
        {
            Debug.LogError($"{LogPrefix} FootSupportCollider layer does not collide with Plane layer. FootLayer={supportLayerName}({supportObject.layer}) PlaneLayer={planeLayerName}({plane.layer})");
            return;
        }

        Debug.Log($"{LogPrefix} FootSupportCollider layer can collide with Plane layer. FootLayer={supportLayerName}({supportObject.layer}) PlaneLayer={planeLayerName}({plane.layer})");
    }

    private static void ValidateGroundMaskLayerState(Scene scene, SerializedObject motorObject, Rigidbody hipsBody)
    {
        int groundMask = GetIntField(motorObject, "groundMask");
        GameObject plane = FindGameObjectByName(scene, "Plane");
        Transform supportTransform = hipsBody != null ? FindDirectChild(hipsBody.transform, FootSupportColliderName) : null;

        Debug.Log($"{LogPrefix} Motor GroundMask layers={FormatLayerMask(groundMask)} Value={groundMask}");

        if (plane == null)
        {
            Debug.LogWarning($"{LogPrefix} Plane not found. Cannot compare Plane layer against Motor GroundMask.");
            return;
        }

        string planeLayerName = LayerMask.LayerToName(plane.layer);
        Debug.Log($"{LogPrefix} Plane layer={planeLayerName}({plane.layer})");

        if ((groundMask & (1 << plane.layer)) == 0)
            Debug.LogError($"{LogPrefix} Plane layer is not included in Motor GroundMask. PlaneLayer={planeLayerName}({plane.layer}) GroundMask={FormatLayerMask(groundMask)}");
        else
            Debug.Log($"{LogPrefix} Plane layer is included in Motor GroundMask.");

        if (supportTransform == null)
        {
            Debug.LogWarning($"{LogPrefix} FootSupportCollider missing. Cannot compare FootSupportCollider layer against Plane layer.");
            return;
        }

        string supportLayerName = LayerMask.LayerToName(supportTransform.gameObject.layer);
        bool ignored = Physics.GetIgnoreLayerCollision(supportTransform.gameObject.layer, plane.layer);
        Debug.Log($"{LogPrefix} FootSupportCollider layer={supportLayerName}({supportTransform.gameObject.layer})");
        Debug.Log($"{LogPrefix} Collision matrix FootSupportCollider<->Plane ignored={ignored}");

        if (ignored)
            Debug.LogError($"{LogPrefix} FootSupportCollider layer and Plane layer collision is disabled. ProjectSettings are not modified automatically.");
    }

    private static string FormatLayerMask(int mask)
    {
        if (mask == 0)
            return "<empty>";

        List<string> layerNames = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            int bit = 1 << layer;
            if ((mask & bit) == 0)
                continue;

            string layerName = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName))
                layerName = $"Layer{layer}";

            layerNames.Add($"{layerName}({layer})");
        }

        return string.Join(", ", layerNames);
    }

    private static void ValidateRootPhase1Components(
        GameObject root,
        Rigidbody hipsBody,
        Rigidbody chestBody,
        Rigidbody headBody,
        Rigidbody leftArmBody,
        Rigidbody rightArmBody)
    {
        Animator rootAnimator = root.GetComponent<Animator>();
        if (rootAnimator != null && rootAnimator.enabled)
            Debug.LogError($"{LogPrefix} Root Animator is still enabled.");
        else if (rootAnimator != null)
            Debug.Log($"{LogPrefix} Root Animator is disabled.");
        else
            Debug.Log($"{LogPrefix} Root Animator was not found.");

        CapsuleCollider rootCapsule = root.GetComponent<CapsuleCollider>();
        if (rootCapsule != null && rootCapsule.enabled)
            Debug.LogError($"{LogPrefix} Root CapsuleCollider is still enabled.");
        else if (rootCapsule != null)
            Debug.Log($"{LogPrefix} Root CapsuleCollider is disabled.");
        else
            Debug.Log($"{LogPrefix} Root CapsuleCollider was not found.");

        CharacterController rootCharacterController = root.GetComponent<CharacterController>();
        if (rootCharacterController != null && rootCharacterController.enabled)
            Debug.LogError($"{LogPrefix} Root CharacterController is still enabled.");
        else if (rootCharacterController != null)
            Debug.Log($"{LogPrefix} Root CharacterController is disabled.");

        Rigidbody rootBody = root.GetComponent<Rigidbody>();
        if (rootBody == null || IsAssignedRigidbody(rootBody, hipsBody, chestBody, headBody, leftArmBody, rightArmBody))
            return;

        if (!rootBody.isKinematic || rootBody.detectCollisions || rootBody.useGravity)
            Debug.LogError($"{LogPrefix} Unused root Rigidbody should be kinematic, useGravity=false, detectCollisions=false.");
        else
            Debug.Log($"{LogPrefix} Unused root Rigidbody physics is disabled.");
    }

    private static void ValidatePhase1Rigidbody(string fieldName, Rigidbody body, bool required)
    {
        if (body == null)
        {
            if (required)
                Debug.LogError($"{LogPrefix} {fieldName} is missing; cannot validate physics defaults.");
            else
                Debug.LogWarning($"{LogPrefix} {fieldName} is not assigned; optional physics validation skipped.");

            return;
        }

        if (body.isKinematic)
            Debug.LogError($"{LogPrefix} {fieldName} should be non-kinematic.");
        else
            Debug.Log($"{LogPrefix} {fieldName} is non-kinematic.");

        if (!body.useGravity)
            Debug.LogError($"{LogPrefix} {fieldName} useGravity should be true.");
        else
            Debug.Log($"{LogPrefix} {fieldName} useGravity is true.");

        if (!body.detectCollisions)
            Debug.LogError($"{LogPrefix} {fieldName} detectCollisions should be true.");
        else
            Debug.Log($"{LogPrefix} {fieldName} detectCollisions is true.");

        if (body.constraints != RigidbodyConstraints.None)
            Debug.LogError($"{LogPrefix} {fieldName} constraints should be None. Current={body.constraints}");
        else
            Debug.Log($"{LogPrefix} {fieldName} constraints are None.");
    }

    private static void ValidateBoolFieldEquals(SerializedObject serializedObject, string fieldName, bool expected)
    {
        bool actual;
        if (!TryGetBoolField(serializedObject, fieldName, out actual))
        {
            Debug.LogWarning($"{LogPrefix} Cannot validate bool field because it was not found: {fieldName}");
            return;
        }

        if (actual != expected)
            Debug.LogError($"{LogPrefix} {fieldName} should be {expected}. Current={actual}");
        else
            Debug.Log($"{LogPrefix} {fieldName} matches expected value: {expected}");
    }

    private static void ValidateFloatFieldEquals(SerializedObject serializedObject, string fieldName, float expected)
    {
        float actual;
        if (!TryGetFloatField(serializedObject, fieldName, out actual))
        {
            Debug.LogWarning($"{LogPrefix} Cannot validate float field because it was not found: {fieldName}");
            return;
        }

        if (Mathf.Abs(actual - expected) > 0.0001f)
            Debug.LogError($"{LogPrefix} {fieldName} should be {expected}. Current={actual}");
        else
            Debug.Log($"{LogPrefix} {fieldName} matches expected value: {expected}");
    }

    private static void ValidateFloatFieldAtLeast(SerializedObject serializedObject, string fieldName, float minimum)
    {
        float actual;
        if (!TryGetFloatField(serializedObject, fieldName, out actual))
        {
            Debug.LogWarning($"{LogPrefix} Cannot validate float field because it was not found: {fieldName}");
            return;
        }

        if (actual < minimum)
            Debug.LogError($"{LogPrefix} {fieldName} should be >= {minimum}. Current={actual}");
        else
            Debug.Log($"{LogPrefix} {fieldName} is >= {minimum}. Current={actual}");
    }

    private static void ValidateNoActiveProtectedTestLikeInstances(Scene scene, GameObject destinationInstance)
    {
        bool foundActiveProtectedInstance = false;
        HashSet<GameObject> visitedRoots = new HashSet<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject candidate = transforms[transformIndex].gameObject;
                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(candidate) ?? candidate;
                if (instanceRoot == null || visitedRoots.Contains(instanceRoot))
                    continue;

                visitedRoots.Add(instanceRoot);
                if (instanceRoot == destinationInstance)
                    continue;

                string sourcePath = GetPrefabSourcePath(instanceRoot);
                bool protectedSource = IsProtectedSourceOrProductionPrefabPath(sourcePath);
                bool testLikeName = ContainsIgnoreCase(instanceRoot.name, TestPrefabName);
                if (!protectedSource && !testLikeName)
                    continue;

                if (!instanceRoot.activeInHierarchy)
                    continue;

                foundActiveProtectedInstance = true;
                Debug.LogError($"{LogPrefix} Active protected/source or test-like non-destination instance remains in scene: {GetHierarchyPath(instanceRoot.transform)} Source={sourcePath}");
            }
        }

        if (!foundActiveProtectedInstance)
            Debug.Log($"{LogPrefix} No active protected/source test-like instances remain in current test scene.");
    }

    private static GameObject PlaceHamsterTestInstance(Scene testScene)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDestinationPath);
        if (prefabAsset == null)
        {
            Debug.LogError($"{LogPrefix} Test prefab destination is missing: {PrefabDestinationPath}");
            return null;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset, testScene) as GameObject;
        if (instance == null)
        {
            Debug.LogError($"{LogPrefix} Failed to instantiate test prefab: {PrefabDestinationPath}");
            return null;
        }

        Debug.Log($"{LogPrefix} Placed Hamster_FullRagdoll_Test instance.");
        return instance;
    }

    private static GameObject FindHamsterTestInstance(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject found = FindHamsterTestInstanceInHierarchy(roots[i]);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindDestinationPrefabInstance(Scene scene)
    {
        HashSet<GameObject> visitedRoots = new HashSet<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject candidate = transforms[transformIndex].gameObject;
                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(candidate) ?? candidate;
                if (instanceRoot == null || visitedRoots.Contains(instanceRoot))
                    continue;

                visitedRoots.Add(instanceRoot);
                string sourcePath = GetPrefabSourcePath(instanceRoot);
                if (string.Equals(sourcePath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"{LogPrefix} Found destination prefab instance: {GetHierarchyPath(instanceRoot.transform)}");
                    return instanceRoot;
                }

                if (IsProtectedSourceOrProductionPrefabPath(sourcePath) && ContainsIgnoreCase(instanceRoot.name, TestPrefabName))
                    Debug.LogWarning($"{LogPrefix} Ignored protected source instance with test-like name: {GetHierarchyPath(instanceRoot.transform)} Source={sourcePath}");
            }
        }

        return null;
    }

    private static GameObject FindHamsterTestInstanceInHierarchy(GameObject root)
    {
        if (root == null)
            return null;

        if (IsHamsterTestInstance(root))
            return root;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject candidate = transforms[i].gameObject;
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(candidate);
            GameObject testCandidate = instanceRoot != null ? instanceRoot : candidate;
            if (testCandidate != candidate && IsProtectedSourceOrProductionPrefabPath(GetPrefabSourcePath(testCandidate)))
                continue;

            if (IsHamsterTestInstance(testCandidate))
                return testCandidate;
        }

        return null;
    }

    private static bool IsHamsterTestInstance(GameObject candidate)
    {
        if (candidate == null)
            return false;

        string sourcePath = GetPrefabSourcePath(candidate);
        if (string.Equals(sourcePath, PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"{LogPrefix} Found destination prefab instance: {GetHierarchyPath(candidate.transform)}");
            return true;
        }

        if (IsProtectedSourceOrProductionPrefabPath(sourcePath))
        {
            if (ContainsIgnoreCase(candidate.name, TestPrefabName))
                Debug.LogWarning($"{LogPrefix} Ignored protected source instance with test-like name: {GetHierarchyPath(candidate.transform)} Source={sourcePath}");

            return false;
        }

        if (!string.IsNullOrEmpty(sourcePath))
            return false;

        return ContainsIgnoreCase(candidate.name, TestPrefabName)
            && candidate.GetComponentInChildren<HamsterFullRagdollMotor>(true) != null;
    }

    private static int DisableSourceOrProductionInstances(Scene scene, GameObject testInstance)
    {
        int disabledCount = 0;
        HashSet<GameObject> visitedRoots = new HashSet<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject candidate = transforms[transformIndex].gameObject;
                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(candidate) ?? candidate;
                if (instanceRoot == null || visitedRoots.Contains(instanceRoot))
                    continue;

                visitedRoots.Add(instanceRoot);
                if (testInstance != null && (instanceRoot == testInstance || IsChildOf(instanceRoot.transform, testInstance.transform)))
                    continue;

                if (string.Equals(GetPrefabSourcePath(instanceRoot), PrefabDestinationPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsSourceOrProductionPlayerInstance(instanceRoot))
                    continue;

                if (!instanceRoot.activeSelf)
                    continue;

                instanceRoot.SetActive(false);
                disabledCount++;
                Debug.Log($"{LogPrefix} Disabled source/prototype scene instance: {GetHierarchyPath(instanceRoot.transform)}");
            }
        }

        return disabledCount;
    }

    private static bool IsSourceOrProductionPlayerInstance(GameObject candidate)
    {
        if (candidate == null)
            return false;

        string sourcePath = GetPrefabSourcePath(candidate);
        if (string.Equals(sourcePath, PrefabSourcePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourcePath, ProductionPlayerPrefabPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = candidate.name;
        return ContainsIgnoreCase(name, "슈가_RagdollPrototype")
            || string.Equals(name, "슈가", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPrefabSourcePath(GameObject candidate)
    {
        UnityEngine.Object source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
        return source == null ? string.Empty : NormalizeAssetPath(AssetDatabase.GetAssetPath(source));
    }

    private static bool IsProtectedSourceOrProductionPrefabPath(string sourcePath)
    {
        return string.Equals(sourcePath, ProductionPlayerPrefabPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourcePath, PrefabSourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildOf(Transform candidate, Transform possibleParent)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current == possibleParent)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static int ResolveSceneGroundMask(Scene scene)
    {
        GameObject plane = FindGameObjectByName(scene, "Plane");
        if (plane != null)
        {
            int mask = 1 << plane.layer;
            Debug.Log($"{LogPrefix} Assigned groundMask from Plane layer. PlaneLayer={LayerMask.LayerToName(plane.layer)} Value={mask}");
            return mask;
        }

        Debug.LogWarning($"{LogPrefix} Plane not found. Falling back to Default layer for groundMask.");
        return ResolveDefaultGroundMask();
    }

    private static GameObject FindGameObjectByName(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                if (string.Equals(transforms[transformIndex].name, objectName, StringComparison.Ordinal))
                    return transforms[transformIndex].gameObject;
            }
        }

        return null;
    }

    private static bool IsDestinationTestScene(Scene scene)
    {
        string scenePath = NormalizeAssetPath(scene.path);
        return string.Equals(scenePath, SceneDestinationPath, StringComparison.OrdinalIgnoreCase)
            || ContainsIgnoreCase(scene.name, "Test_FullRagdollHamster");
    }

    private static bool CopyAssetIfMissing(string sourcePath, string destinationPath, string label)
    {
        if (string.IsNullOrEmpty(sourcePath) || AssetDatabase.LoadMainAssetAtPath(sourcePath) == null)
        {
            Debug.LogError($"{LogPrefix} Source {label} asset not found. Copy aborted for destination={destinationPath}");
            return false;
        }

        if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null || File.Exists(destinationPath))
        {
            Debug.Log($"{LogPrefix} Destination {label} already exists. Skipped without overwrite: {destinationPath}");
            return false;
        }

        if (!EnsureFolderForAssetPath(destinationPath))
        {
            Debug.LogError($"{LogPrefix} Failed to create destination folder for {destinationPath}");
            return false;
        }

        bool copied = AssetDatabase.CopyAsset(sourcePath, destinationPath);
        if (!copied)
        {
            Debug.LogError($"{LogPrefix} Failed to copy {label}: {sourcePath} -> {destinationPath}");
            return false;
        }

        Debug.Log($"{LogPrefix} Copied {label}: {sourcePath} -> {destinationPath}");
        return true;
    }

    private static string ResolveFirstExistingAssetPath(string[] candidatePaths)
    {
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            string candidatePath = candidatePaths[i];
            if (AssetDatabase.LoadMainAssetAtPath(candidatePath) == null)
                continue;

            if (i > 0)
                Debug.LogWarning($"{LogPrefix} Preferred scene source was not found. Using fallback source: {candidatePath}");

            return candidatePath;
        }

        return null;
    }

    private static bool TryGetSelectedTestPrefabPath(out string prefabPath)
    {
        if (!TryGetSelectedPrefabPath(out prefabPath))
            return false;

        if (IsForbiddenSourcePrefabPath(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Refusing to configure protected source/production prefab: {prefabPath}");
            return false;
        }

        if (!IsAllowedTestPrefabPath(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Refusing to configure prefab outside test destination/name guard: {prefabPath}");
            return false;
        }

        return true;
    }

    private static bool TryGetSelectedPrefabPath(out string prefabPath)
    {
        prefabPath = null;
        UnityEngine.Object selectedObject = Selection.activeObject;
        if (selectedObject == null)
        {
            Debug.LogError($"{LogPrefix} Select a prefab asset in the Project window first.");
            return false;
        }

        prefabPath = AssetDatabase.GetAssetPath(selectedObject);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError($"{LogPrefix} Selected object is not an asset.");
            return false;
        }

        if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"{LogPrefix} Selected asset is not a prefab: {prefabPath}");
            return false;
        }

        PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(selectedObject);
        if (prefabAssetType == PrefabAssetType.NotAPrefab)
        {
            Debug.LogError($"{LogPrefix} Selected asset is not recognized as a prefab: {prefabPath}");
            return false;
        }

        return true;
    }

    private static bool IsAllowedTestPrefabPath(string prefabPath)
    {
        return prefabPath.StartsWith(TestPrefabFolder, StringComparison.OrdinalIgnoreCase)
            || ContainsIgnoreCase(Path.GetFileNameWithoutExtension(prefabPath), TestPrefabName);
    }

    private static bool IsForbiddenSourcePrefabPath(string prefabPath)
    {
        return string.Equals(prefabPath, ProductionPlayerPrefabPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefabPath, PrefabSourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private static GameObject GetOrCreateDirectChild(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child.gameObject;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(root, false);
        Debug.Log($"{LogPrefix} Created {childName} object under prefab root.");
        return childObject;
    }

    private static int DisableProductionAndNetworkComponents(GameObject prefabRoot)
    {
        int disabledCount = 0;
        Component[] components = prefabRoot.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            if (!DisableTypeNames.Contains(typeName))
                continue;

            if (!IsComponentEnabled(component))
            {
                Debug.Log($"{LogPrefix} {typeName} is already disabled on {GetHierarchyPath(component.transform)}.");
                continue;
            }

            if (TrySetComponentEnabled(component, false))
            {
                EditorUtility.SetDirty(component);
                disabledCount++;
                Debug.Log($"{LogPrefix} Disabled {typeName} on {GetHierarchyPath(component.transform)}.");
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} Could not disable {typeName} on {GetHierarchyPath(component.transform)}. Component was left unchanged.");
            }
        }

        return disabledCount;
    }

    private static void ValidateDisabledComponentState(GameObject prefabRoot)
    {
        Component[] components = prefabRoot.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            string typeName = component.GetType().Name;
            if (!DisableTypeNames.Contains(typeName))
                continue;

            if (IsComponentEnabled(component))
                Debug.LogError($"{LogPrefix} {typeName} is still enabled on {GetHierarchyPath(component.transform)}.");
            else
                Debug.Log($"{LogPrefix} {typeName} is disabled on {GetHierarchyPath(component.transform)}.");
        }
    }

    private static bool TrySetComponentEnabled(Component component, bool enabled)
    {
        if (component is Behaviour behaviour)
        {
            if (behaviour.enabled == enabled)
                return false;

            behaviour.enabled = enabled;
            return true;
        }

        if (component is Collider collider)
        {
            if (collider.enabled == enabled)
                return false;

            collider.enabled = enabled;
            return true;
        }

        try
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty enabledProperty = serializedObject.FindProperty("m_Enabled");
            if (enabledProperty == null || enabledProperty.propertyType != SerializedPropertyType.Boolean)
                return false;

            if (enabledProperty.boolValue == enabled)
                return false;

            enabledProperty.boolValue = enabled;
            serializedObject.ApplyModifiedProperties();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogPrefix} Serialized m_Enabled update failed for {component.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool IsComponentEnabled(Component component)
    {
        if (component is Behaviour behaviour)
            return behaviour.enabled;

        if (component is Collider collider)
            return collider.enabled;

        try
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty enabledProperty = serializedObject.FindProperty("m_Enabled");
            return enabledProperty != null
                && enabledProperty.propertyType == SerializedPropertyType.Boolean
                && enabledProperty.boolValue;
        }
        catch
        {
            return false;
        }
    }

    private static Rigidbody FindRigidbodyByCandidates(Rigidbody[] rigidbodies, string[] candidates)
    {
        Rigidbody exactMatch = FindRigidbodyByCandidatesInternal(rigidbodies, candidates, exactOnly: true);
        if (exactMatch != null)
            return exactMatch;

        return FindRigidbodyByCandidatesInternal(rigidbodies, candidates, exactOnly: false);
    }

    private static Rigidbody FindRigidbodyByCandidatesInternal(Rigidbody[] rigidbodies, string[] candidates, bool exactOnly)
    {
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            string normalizedCandidate = NormalizeName(candidates[candidateIndex]);
            for (int bodyIndex = 0; bodyIndex < rigidbodies.Length; bodyIndex++)
            {
                Rigidbody body = rigidbodies[bodyIndex];
                if (body == null)
                    continue;

                string normalizedBodyName = NormalizeName(body.name);
                string normalizedPath = NormalizeName(GetHierarchyPath(body.transform));
                bool matches = exactOnly
                    ? normalizedBodyName == normalizedCandidate
                    : normalizedBodyName.Contains(normalizedCandidate) || normalizedPath.Contains(normalizedCandidate);

                if (matches)
                    return body;
            }
        }

        return null;
    }

    private static void AssignRigidbodyField(SerializedObject serializedObject, string fieldName, Rigidbody body, bool required)
    {
        AssignObjectField(serializedObject, fieldName, body, required);
        if (body != null)
        {
            Debug.Log($"{LogPrefix} Assigned {fieldName} -> {GetHierarchyPath(body.transform)}.");
            return;
        }

        if (required)
            Debug.LogError($"{LogPrefix} Required Rigidbody was not found for {fieldName}.");
        else
            Debug.LogWarning($"{LogPrefix} Optional Rigidbody was not found for {fieldName}.");
    }

    private static void AssignObjectField(SerializedObject serializedObject, string fieldName, UnityEngine.Object value, bool required)
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            string severity = required ? "Required" : "Optional";
            Debug.LogWarning($"{LogPrefix} {severity} serialized field was not found: {fieldName}");
            return;
        }

        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            Debug.LogWarning($"{LogPrefix} Serialized field is not an object reference: {fieldName}");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void AssignDefaultGroundMask(SerializedObject serializedObject)
    {
        if (AssignGroundMask(serializedObject, ResolveDefaultGroundMask()))
            Debug.LogWarning($"{LogPrefix} groundMask set to Default layer. Recommended later: create/use a dedicated RagdollTestGround layer manually.");
        else
            Debug.LogWarning($"{LogPrefix} Failed to assign Default groundMask automatically, set manually.");
    }

    private static bool AssignGroundMask(SerializedObject serializedObject, int mask)
    {
        SerializedProperty property = serializedObject.FindProperty("groundMask");
        if (property == null)
        {
            Debug.LogWarning($"{LogPrefix} Serialized field was not found: groundMask");
            return false;
        }

        if (property.propertyType == SerializedPropertyType.Integer
            || property.propertyType == SerializedPropertyType.LayerMask)
        {
            property.intValue = mask;
            Debug.Log($"{LogPrefix} Assigned groundMask. Value={mask}");
            return true;
        }

        SerializedProperty bitsProperty = property.FindPropertyRelative("m_Bits");
        if (property.propertyType == SerializedPropertyType.Generic
            && bitsProperty != null
            && bitsProperty.propertyType == SerializedPropertyType.Integer)
        {
            bitsProperty.intValue = mask;
            Debug.Log($"{LogPrefix} Assigned groundMask via LayerMask m_Bits. Value={mask}");
            return true;
        }

        Debug.LogWarning($"{LogPrefix} Failed to assign groundMask automatically, set manually. PropertyType={property.propertyType}");
        return false;
    }

    private static int ResolveDefaultGroundMask()
    {
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer < 0)
        {
            Debug.LogWarning($"{LogPrefix} Default layer was not found. groundMask will be empty.");
            return 0;
        }

        return 1 << defaultLayer;
    }

    private static void ApplyMotorDefaults(SerializedObject serializedObject)
    {
        SetFloatField(serializedObject, "maxWalkSpeed", 2.2f);
        SetFloatField(serializedObject, "maxSprintSpeed", 3.0f);
        SetFloatField(serializedObject, "walkAcceleration", 14f);
        SetFloatField(serializedObject, "sprintAcceleration", 19f);
        SetFloatField(serializedObject, "chestForceMultiplier", 0.35f);
        SetFloatField(serializedObject, "turnTorque", 45f);
        SetFloatField(serializedObject, "turnDamping", 8f);
        SetFloatField(serializedObject, "inputSmoothTime", 0.12f);
        SetFloatField(serializedObject, "stopDragAssist", 4f);
        SetFloatField(serializedObject, "airControlMultiplier", 0.35f);
        SetFloatField(serializedObject, "controlStrength", 1.0f);
        SetFloatField(serializedObject, "uprightStrength", 75f);
        SetFloatField(serializedObject, "uprightDamping", 10f);
        SetFloatField(serializedObject, "chestUprightMultiplier", 0.45f);
        SetFloatField(serializedObject, "groundCheckRadius", 0.15f);
        SetFloatField(serializedObject, "groundCheckDistance", 0.25f);
        SetBoolField(serializedObject, "drawDebugGizmos", true);
        SetBoolField(serializedObject, "debugLogs", false);
    }

    private static void SetFloatField(SerializedObject serializedObject, string fieldName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning($"{LogPrefix} Motor float field was not found: {fieldName}");
            return;
        }

        if (property.propertyType != SerializedPropertyType.Float)
        {
            Debug.LogWarning($"{LogPrefix} Motor field is not float: {fieldName}");
            return;
        }

        property.floatValue = value;
    }

    private static void SetBoolField(SerializedObject serializedObject, string fieldName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning($"{LogPrefix} Motor bool field was not found: {fieldName}");
            return;
        }

        if (property.propertyType != SerializedPropertyType.Boolean)
        {
            Debug.LogWarning($"{LogPrefix} Motor field is not bool: {fieldName}");
            return;
        }

        property.boolValue = value;
    }

    private static T GetObjectReference<T>(SerializedObject serializedObject, string fieldName) where T : UnityEngine.Object
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            return null;

        return property.objectReferenceValue as T;
    }

    private static int GetIntField(SerializedObject serializedObject, string fieldName)
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
            return 0;

        if (property.propertyType == SerializedPropertyType.Integer
            || property.propertyType == SerializedPropertyType.LayerMask)
        {
            return property.intValue;
        }

        SerializedProperty bitsProperty = property.FindPropertyRelative("m_Bits");
        if (property.propertyType == SerializedPropertyType.Generic
            && bitsProperty != null
            && bitsProperty.propertyType == SerializedPropertyType.Integer)
        {
            return bitsProperty.intValue;
        }

        return 0;
    }

    private static bool TryGetBoolField(SerializedObject serializedObject, string fieldName, out bool value)
    {
        value = false;
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            return false;

        value = property.boolValue;
        return true;
    }

    private static bool TryGetFloatField(SerializedObject serializedObject, string fieldName, out float value)
    {
        value = 0f;
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null || property.propertyType != SerializedPropertyType.Float)
            return false;

        value = property.floatValue;
        return true;
    }

    private static void ValidateRequiredRigidbody(string fieldName, Rigidbody body)
    {
        if (body == null)
        {
            Debug.LogError($"{LogPrefix} {fieldName} is not assigned.");
            return;
        }

        Debug.Log($"{LogPrefix} {fieldName} assigned -> {GetHierarchyPath(body.transform)}.");

        if (body.isKinematic)
            Debug.LogError($"{LogPrefix} {fieldName} must be non-kinematic for local full ragdoll motor.");
        else
            Debug.Log($"{LogPrefix} {fieldName} is non-kinematic.");

        if (!body.useGravity)
            Debug.LogError($"{LogPrefix} {fieldName} useGravity should be true.");
        else
            Debug.Log($"{LogPrefix} {fieldName} useGravity is true.");
    }

    private static void ApplyRigidbodyDefaults(
        Rigidbody body,
        string fieldName,
        float mass,
        float linearDamping,
        float angularDamping,
        CollisionDetectionMode collisionDetectionMode)
    {
        if (body == null)
            return;

        Debug.Log($"{LogPrefix} Applying Rigidbody defaults to {fieldName}: {GetHierarchyPath(body.transform)}.");
        body.mass = mass;
        SetRigidbodyDamping(body, linearDamping, angularDamping);
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = collisionDetectionMode;
    }

    private static void SetRigidbodyDamping(Rigidbody body, float linearDamping, float angularDamping)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;
#else
        body.drag = linearDamping;
        body.angularDrag = angularDamping;
#endif
    }

    private static Component FindComponentByTypeName(GameObject root, string typeName)
    {
        Component[] components = root.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component != null && component.GetType().Name == typeName)
                return component;
        }

        return null;
    }

    private static bool EnsureFolderForAssetPath(string assetPath)
    {
        string folderPath = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        return EnsureFolder(folderPath);
    }

    private static bool EnsureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return false;

        folderPath = NormalizeAssetPath(folderPath);
        if (AssetDatabase.IsValidFolder(folderPath))
            return true;

        string parentPath = NormalizeAssetPath(Path.GetDirectoryName(folderPath));
        if (string.IsNullOrEmpty(parentPath) || !EnsureFolder(parentPath))
            return false;

        string folderName = Path.GetFileName(folderPath);
        string guid = AssetDatabase.CreateFolder(parentPath, folderName);
        if (string.IsNullOrEmpty(guid))
            return false;

        Debug.Log($"{LogPrefix} Created folder: {folderPath}");
        return true;
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        char[] buffer = new char[value.Length];
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsLetterOrDigit(c))
                continue;

            buffer[count] = char.ToLowerInvariant(c);
            count++;
        }

        return new string(buffer, 0, count);
    }

    private static bool ContainsIgnoreCase(string value, string fragment)
    {
        return !string.IsNullOrEmpty(value)
            && !string.IsNullOrEmpty(fragment)
            && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return "<null>";

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
