using System.Collections.Generic;
using UnityEngine;

public sealed class HamsterProceduralPoseController : MonoBehaviour
{
    private const float MinimumSpeedForPose = 0.01f;
    private const float MaxProceduralDegrees = 75f;
    private const float TwoPi = 6.28318530718f;

    [Header("References")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Rigidbody targetBody;
    [SerializeField] private HamsterFullRagdollMotor motorStateSource;
    [SerializeField] private bool autoFindReferences = true;
    [SerializeField] private bool autoFindBonesOnEnable = true;

    [Header("Runtime")]
    [SerializeField] private bool enableProceduralPose = true;
    [SerializeField] private bool applyInLateUpdate = true;
    [SerializeField] private bool resetBonesOnDisable = true;
    [SerializeField] private bool useMotorStateWhenAvailable = true;

    [Header("Bone Groups")]
    [SerializeField] private bool animateChest = true;
    [SerializeField] private bool animateHead = true;
    [SerializeField] private bool animateArms = true;
    [SerializeField] private bool animateLegs = true;
    [SerializeField] private bool animateFeet = true;
    [SerializeField] private bool excludeTailAndEars = true;

    [Header("Walk Pose")]
    [SerializeField] private float walkSpeedForFullPose = 1.5f;
    [SerializeField] private float walkStepFrequency = 5.5f;
    [SerializeField] private float walkArmSwingDegrees = 12f;
    [SerializeField] private float walkLegSwingDegrees = 10f;
    [SerializeField] private float walkKneeBendDegrees = 6f;
    [SerializeField] private float walkChestPitchDegrees = 2f;
    [SerializeField] private float walkHeadCounterPitchDegrees = 1.5f;

    [Header("Run Pose")]
    [SerializeField] private float runSpeedForFullPose = 2.8f;
    [SerializeField] private float runStepFrequency = 8.5f;
    [SerializeField] private float runArmSwingDegrees = 24f;
    [SerializeField] private float runLegSwingDegrees = 18f;
    [SerializeField] private float runKneeBendDegrees = 10f;
    [SerializeField] private float runChestPitchDegrees = 7f;
    [SerializeField] private float runHeadCounterPitchDegrees = 3f;
    [SerializeField] private float runPoseSprintBoost = 1.0f;

    [Header("Jump Pose")]
    [SerializeField] private float jumpUpArmRaiseDegrees = 24f;
    [SerializeField] private float jumpUpLegTuckDegrees = 14f;
    [SerializeField] private float jumpUpKneeBendDegrees = 16f;
    [SerializeField] private float jumpUpChestPitchDegrees = -4f;
    [SerializeField] private float jumpUpHeadPitchDegrees = 3f;
    [SerializeField] private float jumpBlendSmoothTime = 0.08f;

    [Header("Fall Pose")]
    [SerializeField] private float fallArmSpreadDegrees = 18f;
    [SerializeField] private float fallLegExtendDegrees = 10f;
    [SerializeField] private float fallChestPitchDegrees = 5f;
    [SerializeField] private float fallHeadPitchDegrees = -2f;
    [SerializeField] private float fallBlendSmoothTime = 0.10f;

    [Header("Landing Pose")]
    [SerializeField] private float landingArmDownDegrees = 12f;
    [SerializeField] private float landingLegBendDegrees = 18f;
    [SerializeField] private float landingKneeBendDegrees = 20f;
    [SerializeField] private float landingChestPitchDegrees = 8f;
    [SerializeField] private float landingDuration = 0.16f;
    [SerializeField] private float landingBlendSmoothTime = 0.08f;

    [Header("Pose Blend")]
    [SerializeField] private float locomotionBlendSmoothTime = 0.08f;
    [SerializeField] private float sprintBlendSmoothTime = 0.10f;
    [SerializeField] private float airborneThreshold = 0.15f;
    [SerializeField] private float groundedVelocityDeadZone = 0.05f;
    [SerializeField] private float maxPoseDeltaTime = 0.05f;

    [Header("Axis / Invert")]
    [SerializeField] private bool invertArmSwing = false;
    [SerializeField] private bool invertLegSwing = false;
    [SerializeField] private bool invertChestPitch = false;
    [SerializeField] private bool invertHeadPitch = false;
    [SerializeField] private bool swapLeftRight = false;

    [Header("Animator")]
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private bool warnIfAnimatorEnabled = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private float debugLogInterval = 1.0f;
    [SerializeField] private bool drawDebugGizmos = false;

    private struct BonePoseTarget
    {
        public Transform transform;
        public Quaternion initialLocalRotation;
        public string debugName;

        public bool IsValid => transform != null;
    }

    private enum BoneSide
    {
        Any,
        Left,
        Right
    }

    private struct MotionSample
    {
        public bool hasGroundedState;
        public bool isGrounded;
        public bool isSprintHeld;
        public float planarSpeed;
        public float verticalVelocity;
        public string source;
    }

    private BonePoseTarget _root;
    private BonePoseTarget _hips;
    private BonePoseTarget _spine;
    private BonePoseTarget _chest;
    private BonePoseTarget _neck;
    private BonePoseTarget _head;
    private BonePoseTarget _leftShoulder;
    private BonePoseTarget _rightShoulder;
    private BonePoseTarget _leftUpperArm;
    private BonePoseTarget _rightUpperArm;
    private BonePoseTarget _leftLowerArm;
    private BonePoseTarget _rightLowerArm;
    private BonePoseTarget _leftHand;
    private BonePoseTarget _rightHand;
    private BonePoseTarget _leftUpperLeg;
    private BonePoseTarget _rightUpperLeg;
    private BonePoseTarget _leftLowerLeg;
    private BonePoseTarget _rightLowerLeg;
    private BonePoseTarget _leftFoot;
    private BonePoseTarget _rightFoot;
    private BonePoseTarget _leftToes;
    private BonePoseTarget _rightToes;

    private bool _hasBoneCache;
    private bool _hasPreviousGroundedState;
    private bool _previousGrounded;
    private bool _warnedAnimatorMayFight;
    private bool _missingVisualRootLogged;
    private bool _missingTargetBodyLogged;
    private bool _missingBoneCacheLogged;
    private float _stepPhase;
    private float _locomotion01;
    private float _locomotionVelocity;
    private float _sprint01;
    private float _sprintVelocity;
    private float _jumpWeight;
    private float _jumpWeightVelocity;
    private float _fallWeight;
    private float _fallWeightVelocity;
    private float _landingWeight;
    private float _landingWeightVelocity;
    private float _landingTimer;
    private float _nextDebugLogTime;

    private void Awake()
    {
        if (autoFindReferences)
            ResolveReferences();
    }

    private void OnEnable()
    {
        if (autoFindReferences)
            ResolveReferences();

        if (autoFindBonesOnEnable)
            RebuildBoneCache();

        ResetRuntimeState();
        WarnIfAnimatorMayFight();
    }

    private void Start()
    {
        if (!_hasBoneCache && autoFindBonesOnEnable)
            RebuildBoneCache();
    }

    private void Update()
    {
        if (!applyInLateUpdate)
            TickPose(Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (applyInLateUpdate)
            TickPose(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (resetBonesOnDisable)
            ResetControlledBones();
    }

    [ContextMenu("Find Procedural Pose Bones")]
    private void RebuildBoneCache()
    {
        ClearBoneTargets();

        if (autoFindReferences)
            ResolveReferences();

        if (visualRoot == null)
        {
            LogMissingVisualRoot();
            return;
        }

        Transform[] allTransforms = visualRoot.GetComponentsInChildren<Transform>(true);
        List<Transform> candidates = new List<Transform>(allTransforms.Length);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform candidate = allTransforms[i];
            if (candidate == null || candidate == visualRoot)
                continue;

            if (ShouldSkipDirectPoseTransform(candidate))
            {
                LogSkippedBone(candidate);
                continue;
            }

            candidates.Add(candidate);
        }

        _root = CreateTarget(FindBestBone(candidates, BoneSide.Any, "root", "root", "armature", "skeleton"), "root");
        _hips = CreateTarget(FindBestBone(candidates, BoneSide.Any, "hips", "hips", "pelvis", "hip"), "hips");
        _spine = CreateTarget(FindBestBone(candidates, BoneSide.Any, "spine", "spine", "spine1", "spine01", "body"), "spine");
        _chest = CreateTarget(FindBestBone(candidates, BoneSide.Any, "chest", "chest", "upperbody", "spine2", "spine02", "spine3", "spine03"), "chest");
        _neck = CreateTarget(FindBestBone(candidates, BoneSide.Any, "neck", "neck"), "neck");
        _head = CreateTarget(FindBestBone(candidates, BoneSide.Any, "head", "head"), "head");

        _leftShoulder = CreateTarget(FindBestBone(candidates, BoneSide.Left, "shoulder.L", "shoulder", "clavicle"), "shoulder.L");
        _rightShoulder = CreateTarget(FindBestBone(candidates, BoneSide.Right, "shoulder.R", "shoulder", "clavicle"), "shoulder.R");
        _leftUpperArm = CreateTarget(FindBestBone(candidates, BoneSide.Left, "upper_arm.L", "upperarm", "armupper", "uparm"), "upper_arm.L");
        _rightUpperArm = CreateTarget(FindBestBone(candidates, BoneSide.Right, "upper_arm.R", "upperarm", "armupper", "uparm"), "upper_arm.R");
        _leftLowerArm = CreateTarget(FindBestBone(candidates, BoneSide.Left, "lower_arm.L", "lowerarm", "forearm", "armfore"), "lower_arm.L");
        _rightLowerArm = CreateTarget(FindBestBone(candidates, BoneSide.Right, "lower_arm.R", "lowerarm", "forearm", "armfore"), "lower_arm.R");
        _leftHand = CreateTarget(FindBestBone(candidates, BoneSide.Left, "hand.L", "hand", "wrist"), "hand.L");
        _rightHand = CreateTarget(FindBestBone(candidates, BoneSide.Right, "hand.R", "hand", "wrist"), "hand.R");

        _leftUpperLeg = CreateTarget(FindBestBone(candidates, BoneSide.Left, "upper_leg.L", "upperleg", "legupper", "upleg", "thigh"), "upper_leg.L");
        _rightUpperLeg = CreateTarget(FindBestBone(candidates, BoneSide.Right, "upper_leg.R", "upperleg", "legupper", "upleg", "thigh"), "upper_leg.R");
        _leftLowerLeg = CreateTarget(FindBestBone(candidates, BoneSide.Left, "lower_leg.L", "lowerleg", "leglower", "calf", "shin"), "lower_leg.L");
        _rightLowerLeg = CreateTarget(FindBestBone(candidates, BoneSide.Right, "lower_leg.R", "lowerleg", "leglower", "calf", "shin"), "lower_leg.R");
        _leftFoot = CreateTarget(FindBestBone(candidates, BoneSide.Left, "foot.L", "foot", "ankle"), "foot.L");
        _rightFoot = CreateTarget(FindBestBone(candidates, BoneSide.Right, "foot.R", "foot", "ankle"), "foot.R");
        _leftToes = CreateTarget(FindBestBone(candidates, BoneSide.Left, "toes.L", "toes", "toe"), "toes.L");
        _rightToes = CreateTarget(FindBestBone(candidates, BoneSide.Right, "toes.R", "toes", "toe"), "toes.R");

        _hasBoneCache = true;
        _missingBoneCacheLogged = false;

        LogBoneResult(_root);
        LogBoneResult(_hips);
        LogBoneResult(_spine);
        LogBoneResult(_chest);
        LogBoneResult(_neck);
        LogBoneResult(_head);
        LogBoneResult(_leftShoulder);
        LogBoneResult(_rightShoulder);
        LogBoneResult(_leftUpperArm);
        LogBoneResult(_rightUpperArm);
        LogBoneResult(_leftLowerArm);
        LogBoneResult(_rightLowerArm);
        LogBoneResult(_leftHand);
        LogBoneResult(_rightHand);
        LogBoneResult(_leftUpperLeg);
        LogBoneResult(_rightUpperLeg);
        LogBoneResult(_leftLowerLeg);
        LogBoneResult(_rightLowerLeg);
        LogBoneResult(_leftFoot);
        LogBoneResult(_rightFoot);
        LogBoneResult(_leftToes);
        LogBoneResult(_rightToes);
    }

    private void TickPose(float rawDeltaTime)
    {
        if (!enableProceduralPose)
            return;

        if (autoFindReferences)
            ResolveReferences();

        if (!_hasBoneCache && autoFindBonesOnEnable)
            RebuildBoneCache();

        if (!_hasBoneCache)
        {
            LogMissingBoneCache();
            return;
        }

        float deltaTime = Mathf.Clamp(rawDeltaTime, 0f, maxPoseDeltaTime);
        MotionSample sample = ReadMotionSample();
        UpdatePoseWeights(sample, deltaTime);
        ApplyProceduralPose(sample);
        LogDebugState(sample);
    }

    private void UpdatePoseWeights(MotionSample sample, float deltaTime)
    {
        bool isMoving = sample.planarSpeed > groundedVelocityDeadZone;
        float fullPoseSpeed = Mathf.Lerp(walkSpeedForFullPose, runSpeedForFullPose, sample.isSprintHeld ? 1f : 0f);
        float speed01 = isMoving ? Mathf.Clamp01(sample.planarSpeed / Mathf.Max(MinimumSpeedForPose, fullPoseSpeed)) : 0f;
        float targetSprint01 = sample.isSprintHeld ? 1f : 0f;

        _sprint01 = SmoothDamp(_sprint01, targetSprint01, ref _sprintVelocity, sprintBlendSmoothTime, deltaTime);
        _locomotion01 = SmoothDamp(_locomotion01, speed01, ref _locomotionVelocity, locomotionBlendSmoothTime, deltaTime);

        bool isAirborne = sample.hasGroundedState && !sample.isGrounded;
        bool justLanded = false;
        if (sample.hasGroundedState)
        {
            if (!_hasPreviousGroundedState)
            {
                _previousGrounded = sample.isGrounded;
                _hasPreviousGroundedState = true;
            }

            justLanded = !_previousGrounded && sample.isGrounded;
            _previousGrounded = sample.isGrounded;
        }
        else
        {
            _hasPreviousGroundedState = false;
        }

        if (justLanded && landingDuration > 0f)
            _landingTimer = landingDuration;

        if (_landingTimer > 0f)
            _landingTimer = Mathf.Max(0f, _landingTimer - deltaTime);

        float targetJump = isAirborne && sample.verticalVelocity > airborneThreshold ? 1f : 0f;
        float targetFall = isAirborne && sample.verticalVelocity < -airborneThreshold ? 1f : 0f;
        float targetLanding = landingDuration > 0f ? Mathf.Clamp01(_landingTimer / landingDuration) : 0f;

        _jumpWeight = SmoothDamp(_jumpWeight, targetJump, ref _jumpWeightVelocity, jumpBlendSmoothTime, deltaTime);
        _fallWeight = SmoothDamp(_fallWeight, targetFall, ref _fallWeightVelocity, fallBlendSmoothTime, deltaTime);
        _landingWeight = SmoothDamp(_landingWeight, targetLanding, ref _landingWeightVelocity, landingBlendSmoothTime, deltaTime);

        float frequency = Mathf.Lerp(walkStepFrequency, runStepFrequency, _sprint01);
        _stepPhase += deltaTime * frequency * Mathf.Lerp(0.4f, 1f, _locomotion01);
        if (_stepPhase > TwoPi)
            _stepPhase -= TwoPi * Mathf.Floor(_stepPhase / TwoPi);
    }

    private void ApplyProceduralPose(MotionSample sample)
    {
        float sin = Mathf.Sin(_stepPhase);
        float cos = Mathf.Cos(_stepPhase);
        bool isAirborne = sample.hasGroundedState && !sample.isGrounded;
        float airborneLocomotionFade = isAirborne ? 0.2f : 1f;
        float landingLocomotionFade = 1f - Mathf.Clamp01(_landingWeight * 0.65f);
        float locomotionWeight = _locomotion01 * airborneLocomotionFade * landingLocomotionFade;
        float sprintPose01 = _sprint01;
        float sprintBoost = Mathf.Lerp(1f, Mathf.Max(0f, runPoseSprintBoost), sprintPose01);
        float armSign = invertArmSwing ? -1f : 1f;
        float legSign = invertLegSwing ? -1f : 1f;
        float chestSign = invertChestPitch ? -1f : 1f;
        float headSign = invertHeadPitch ? -1f : 1f;

        float armSwing = Mathf.Lerp(walkArmSwingDegrees, runArmSwingDegrees, sprintPose01) * locomotionWeight * sprintBoost;
        float legSwing = Mathf.Lerp(walkLegSwingDegrees, runLegSwingDegrees, sprintPose01) * locomotionWeight * sprintBoost;
        float kneeBend = Mathf.Lerp(walkKneeBendDegrees, runKneeBendDegrees, sprintPose01) * locomotionWeight * sprintBoost;
        float chestPitch = Mathf.Lerp(walkChestPitchDegrees, runChestPitchDegrees, sprintPose01) * locomotionWeight * chestSign;
        float headCounterPitch = -Mathf.Lerp(walkHeadCounterPitchDegrees, runHeadCounterPitchDegrees, sprintPose01) * locomotionWeight * headSign;
        float chestRoll = cos * Mathf.Lerp(0.4f, 1.2f, sprintPose01) * locomotionWeight;

        float leftArmSwing = sin * armSwing * armSign;
        float rightArmSwing = -sin * armSwing * armSign;
        float leftLegSwing = -sin * legSwing * legSign;
        float rightLegSwing = sin * legSwing * legSign;
        float leftKnee = Mathf.Max(0f, sin) * kneeBend * legSign;
        float rightKnee = Mathf.Max(0f, -sin) * kneeBend * legSign;

        if (swapLeftRight)
        {
            Swap(ref leftArmSwing, ref rightArmSwing);
            Swap(ref leftLegSwing, ref rightLegSwing);
            Swap(ref leftKnee, ref rightKnee);
        }

        float jumpArmPitch = -jumpUpArmRaiseDegrees * _jumpWeight * armSign;
        float jumpLegPitch = -jumpUpLegTuckDegrees * _jumpWeight * legSign;
        float jumpKneePitch = jumpUpKneeBendDegrees * _jumpWeight * legSign;
        float jumpChestPitch = jumpUpChestPitchDegrees * _jumpWeight * chestSign;
        float jumpHeadPitch = jumpUpHeadPitchDegrees * _jumpWeight * headSign;

        float fallArmPitch = fallArmSpreadDegrees * 0.35f * _fallWeight * armSign;
        float fallArmRoll = fallArmSpreadDegrees * _fallWeight;
        float fallLegPitch = fallLegExtendDegrees * _fallWeight * legSign;
        float fallChestPitch = fallChestPitchDegrees * _fallWeight * chestSign;
        float fallHeadPitch = fallHeadPitchDegrees * _fallWeight * headSign;

        float landingArmPitch = landingArmDownDegrees * _landingWeight * armSign;
        float landingLegPitch = -landingLegBendDegrees * _landingWeight * legSign;
        float landingKneePitch = landingKneeBendDegrees * _landingWeight * legSign;
        float landingChestPitch = landingChestPitchDegrees * _landingWeight * chestSign;

        Quaternion spineRotation = Quaternion.identity;
        Quaternion chestRotation = Quaternion.identity;
        Quaternion neckRotation = Quaternion.identity;
        Quaternion headRotation = Quaternion.identity;
        Quaternion leftShoulderRotation = Quaternion.identity;
        Quaternion rightShoulderRotation = Quaternion.identity;
        Quaternion leftUpperArmRotation = Quaternion.identity;
        Quaternion rightUpperArmRotation = Quaternion.identity;
        Quaternion leftLowerArmRotation = Quaternion.identity;
        Quaternion rightLowerArmRotation = Quaternion.identity;
        Quaternion leftHandRotation = Quaternion.identity;
        Quaternion rightHandRotation = Quaternion.identity;
        Quaternion leftUpperLegRotation = Quaternion.identity;
        Quaternion rightUpperLegRotation = Quaternion.identity;
        Quaternion leftLowerLegRotation = Quaternion.identity;
        Quaternion rightLowerLegRotation = Quaternion.identity;
        Quaternion leftFootRotation = Quaternion.identity;
        Quaternion rightFootRotation = Quaternion.identity;
        Quaternion leftToesRotation = Quaternion.identity;
        Quaternion rightToesRotation = Quaternion.identity;

        if (animateChest)
        {
            spineRotation =
                BuildRotation(chestPitch * 0.35f, 0f, chestRoll * 0.35f)
                * BuildRotation(jumpChestPitch * 0.35f, 0f, 0f)
                * BuildRotation(fallChestPitch * 0.35f, 0f, 0f)
                * BuildRotation(landingChestPitch * 0.35f, 0f, 0f);

            chestRotation =
                BuildRotation(chestPitch, 0f, chestRoll)
                * BuildRotation(jumpChestPitch, 0f, 0f)
                * BuildRotation(fallChestPitch, 0f, 0f)
                * BuildRotation(landingChestPitch, 0f, 0f);
        }

        if (animateHead)
        {
            neckRotation =
                BuildRotation(headCounterPitch * 0.35f, 0f, -chestRoll * 0.25f)
                * BuildRotation(jumpHeadPitch * 0.35f, 0f, 0f)
                * BuildRotation(fallHeadPitch * 0.35f, 0f, 0f);

            headRotation =
                BuildRotation(headCounterPitch, 0f, -chestRoll * 0.35f)
                * BuildRotation(jumpHeadPitch, 0f, 0f)
                * BuildRotation(fallHeadPitch, 0f, 0f);
        }

        if (animateArms)
        {
            leftShoulderRotation =
                BuildRotation(leftArmSwing * 0.35f, 0f, 0f)
                * BuildRotation(jumpArmPitch * 0.35f, 0f, 0f)
                * BuildRotation(fallArmPitch * 0.25f, 0f, -fallArmRoll * 0.35f)
                * BuildRotation(landingArmPitch * 0.25f, 0f, 0f);

            rightShoulderRotation =
                BuildRotation(rightArmSwing * 0.35f, 0f, 0f)
                * BuildRotation(jumpArmPitch * 0.35f, 0f, 0f)
                * BuildRotation(fallArmPitch * 0.25f, 0f, fallArmRoll * 0.35f)
                * BuildRotation(landingArmPitch * 0.25f, 0f, 0f);

            leftUpperArmRotation =
                BuildRotation(leftArmSwing, 0f, 0f)
                * BuildRotation(jumpArmPitch, 0f, 0f)
                * BuildRotation(fallArmPitch, 0f, -fallArmRoll)
                * BuildRotation(landingArmPitch, 0f, 0f);

            rightUpperArmRotation =
                BuildRotation(rightArmSwing, 0f, 0f)
                * BuildRotation(jumpArmPitch, 0f, 0f)
                * BuildRotation(fallArmPitch, 0f, fallArmRoll)
                * BuildRotation(landingArmPitch, 0f, 0f);

            leftLowerArmRotation =
                BuildRotation(-leftArmSwing * 0.25f, 0f, 0f)
                * BuildRotation(jumpArmPitch * 0.25f, 0f, 0f)
                * BuildRotation(fallArmPitch * 0.35f, 0f, -fallArmRoll * 0.25f)
                * BuildRotation(landingArmPitch * 0.25f, 0f, 0f);

            rightLowerArmRotation =
                BuildRotation(-rightArmSwing * 0.25f, 0f, 0f)
                * BuildRotation(jumpArmPitch * 0.25f, 0f, 0f)
                * BuildRotation(fallArmPitch * 0.35f, 0f, fallArmRoll * 0.25f)
                * BuildRotation(landingArmPitch * 0.25f, 0f, 0f);

            leftHandRotation = BuildRotation(-leftArmSwing * 0.12f, 0f, 0f);
            rightHandRotation = BuildRotation(-rightArmSwing * 0.12f, 0f, 0f);
        }

        if (animateLegs)
        {
            leftUpperLegRotation =
                BuildRotation(leftLegSwing, 0f, 0f)
                * BuildRotation(jumpLegPitch, 0f, 0f)
                * BuildRotation(fallLegPitch, 0f, 0f)
                * BuildRotation(landingLegPitch, 0f, 0f);

            rightUpperLegRotation =
                BuildRotation(rightLegSwing, 0f, 0f)
                * BuildRotation(jumpLegPitch, 0f, 0f)
                * BuildRotation(fallLegPitch, 0f, 0f)
                * BuildRotation(landingLegPitch, 0f, 0f);

            leftLowerLegRotation =
                BuildRotation(leftKnee, 0f, 0f)
                * BuildRotation(jumpKneePitch, 0f, 0f)
                * BuildRotation(-fallLegPitch * 0.35f, 0f, 0f)
                * BuildRotation(landingKneePitch, 0f, 0f);

            rightLowerLegRotation =
                BuildRotation(rightKnee, 0f, 0f)
                * BuildRotation(jumpKneePitch, 0f, 0f)
                * BuildRotation(-fallLegPitch * 0.35f, 0f, 0f)
                * BuildRotation(landingKneePitch, 0f, 0f);
        }

        if (animateFeet)
        {
            leftFootRotation =
                BuildRotation(-leftLegSwing * 0.35f - leftKnee * 0.15f, 0f, 0f)
                * BuildRotation(-jumpLegPitch * 0.25f, 0f, 0f)
                * BuildRotation(-landingKneePitch * 0.25f, 0f, 0f);

            rightFootRotation =
                BuildRotation(-rightLegSwing * 0.35f - rightKnee * 0.15f, 0f, 0f)
                * BuildRotation(-jumpLegPitch * 0.25f, 0f, 0f)
                * BuildRotation(-landingKneePitch * 0.25f, 0f, 0f);

            leftToesRotation = BuildRotation(-leftLegSwing * 0.15f, 0f, 0f);
            rightToesRotation = BuildRotation(-rightLegSwing * 0.15f, 0f, 0f);
        }

        ApplyTarget(ref _spine, spineRotation);
        ApplyTarget(ref _chest, chestRotation);
        ApplyTarget(ref _neck, neckRotation);
        ApplyTarget(ref _head, headRotation);
        ApplyTarget(ref _leftShoulder, leftShoulderRotation);
        ApplyTarget(ref _rightShoulder, rightShoulderRotation);
        ApplyTarget(ref _leftUpperArm, leftUpperArmRotation);
        ApplyTarget(ref _rightUpperArm, rightUpperArmRotation);
        ApplyTarget(ref _leftLowerArm, leftLowerArmRotation);
        ApplyTarget(ref _rightLowerArm, rightLowerArmRotation);
        ApplyTarget(ref _leftHand, leftHandRotation);
        ApplyTarget(ref _rightHand, rightHandRotation);
        ApplyTarget(ref _leftUpperLeg, leftUpperLegRotation);
        ApplyTarget(ref _rightUpperLeg, rightUpperLegRotation);
        ApplyTarget(ref _leftLowerLeg, leftLowerLegRotation);
        ApplyTarget(ref _rightLowerLeg, rightLowerLegRotation);
        ApplyTarget(ref _leftFoot, leftFootRotation);
        ApplyTarget(ref _rightFoot, rightFootRotation);
        ApplyTarget(ref _leftToes, leftToesRotation);
        ApplyTarget(ref _rightToes, rightToesRotation);
    }

    private MotionSample ReadMotionSample()
    {
        if (useMotorStateWhenAvailable && motorStateSource != null)
        {
            return new MotionSample
            {
                hasGroundedState = true,
                isGrounded = motorStateSource.IsGrounded,
                isSprintHeld = motorStateSource.IsSprintHeld,
                planarSpeed = Mathf.Max(0f, motorStateSource.CurrentPlanarSpeed),
                verticalVelocity = motorStateSource.CurrentVerticalVelocity,
                source = "motor"
            };
        }

        if (targetBody != null)
        {
            Vector3 velocity = targetBody.linearVelocity;
            velocity.y = 0f;
            return new MotionSample
            {
                hasGroundedState = false,
                isGrounded = false,
                isSprintHeld = false,
                planarSpeed = velocity.magnitude,
                verticalVelocity = targetBody.linearVelocity.y,
                source = "rigidbody-fallback"
            };
        }

        LogMissingTargetBody();
        return new MotionSample
        {
            hasGroundedState = false,
            isGrounded = false,
            isSprintHeld = false,
            planarSpeed = 0f,
            verticalVelocity = 0f,
            source = "none"
        };
    }

    private void ResolveReferences()
    {
        if (targetBody == null)
            targetBody = GetComponent<Rigidbody>();

        if (targetBody == null)
            targetBody = GetComponentInParent<Rigidbody>();

        if (motorStateSource == null)
            motorStateSource = GetComponent<HamsterFullRagdollMotor>();

        if (motorStateSource == null)
            motorStateSource = GetComponentInParent<HamsterFullRagdollMotor>();

        if (visualRoot == null)
            visualRoot = FindVisualRoot();

        if (visualAnimator == null && visualRoot != null)
            visualAnimator = visualRoot.GetComponentInChildren<Animator>(true);
    }

    private Transform FindVisualRoot()
    {
        Transform searchRoot = targetBody != null ? targetBody.transform : transform;
        Transform found = FindDescendantByNormalizedName(searchRoot, "visualpreviewroot");
        if (found != null)
            return found;

        if (transform.parent != null)
            return FindDescendantByNormalizedName(transform.parent, "visualpreviewroot");

        return null;
    }

    private static Transform FindDescendantByNormalizedName(Transform searchRoot, string normalizedName)
    {
        if (searchRoot == null)
            return null;

        Transform[] transforms = searchRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null && NormalizeBoneName(candidate.name) == normalizedName)
                return candidate;
        }

        return null;
    }

    private Transform FindBestBone(List<Transform> candidates, BoneSide side, string debugName, params string[] boneNames)
    {
        Transform best = null;
        int bestScore = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            Transform candidate = candidates[i];
            int score = ScoreBoneCandidate(candidate, side, boneNames);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int ScoreBoneCandidate(Transform candidate, BoneSide side, string[] boneNames)
    {
        if (candidate == null)
            return 0;

        string rawName = candidate.name;
        string normalizedName = NormalizeBoneName(rawName);

        if (side == BoneSide.Left && !IsLeftBoneName(rawName, normalizedName))
            return 0;

        if (side == BoneSide.Right && !IsRightBoneName(rawName, normalizedName))
            return 0;

        int bestScore = 0;
        for (int i = 0; i < boneNames.Length; i++)
        {
            string normalizedBoneName = NormalizeBoneName(boneNames[i]);
            if (normalizedName == normalizedBoneName)
                bestScore = Mathf.Max(bestScore, 100 + normalizedBoneName.Length);
            else if (normalizedName.Contains(normalizedBoneName))
                bestScore = Mathf.Max(bestScore, 70 + normalizedBoneName.Length);
        }

        if (bestScore > 0 && side != BoneSide.Any)
            bestScore += 10;

        return bestScore;
    }

    private BonePoseTarget CreateTarget(Transform foundTransform, string debugName)
    {
        return new BonePoseTarget
        {
            transform = foundTransform,
            initialLocalRotation = foundTransform != null ? foundTransform.localRotation : Quaternion.identity,
            debugName = debugName
        };
    }

    private void ClearBoneTargets()
    {
        _root = default(BonePoseTarget);
        _hips = default(BonePoseTarget);
        _spine = default(BonePoseTarget);
        _chest = default(BonePoseTarget);
        _neck = default(BonePoseTarget);
        _head = default(BonePoseTarget);
        _leftShoulder = default(BonePoseTarget);
        _rightShoulder = default(BonePoseTarget);
        _leftUpperArm = default(BonePoseTarget);
        _rightUpperArm = default(BonePoseTarget);
        _leftLowerArm = default(BonePoseTarget);
        _rightLowerArm = default(BonePoseTarget);
        _leftHand = default(BonePoseTarget);
        _rightHand = default(BonePoseTarget);
        _leftUpperLeg = default(BonePoseTarget);
        _rightUpperLeg = default(BonePoseTarget);
        _leftLowerLeg = default(BonePoseTarget);
        _rightLowerLeg = default(BonePoseTarget);
        _leftFoot = default(BonePoseTarget);
        _rightFoot = default(BonePoseTarget);
        _leftToes = default(BonePoseTarget);
        _rightToes = default(BonePoseTarget);
        _hasBoneCache = false;
    }

    private bool ShouldSkipDirectPoseTransform(Transform candidate)
    {
        if (!excludeTailAndEars || candidate == null || candidate == visualRoot)
            return false;

        if (ContainsExcludedSelfKeyword(candidate.name))
            return true;

        Transform current = candidate.parent;
        while (current != null && current != visualRoot)
        {
            if (ContainsExcludedSelfKeyword(current.name))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static bool ContainsExcludedSelfKeyword(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string lower = value.ToLowerInvariant();
        return lower.Contains("tail")
            || value.Contains("꼬리")
            || lower.Contains("ear")
            || value.Contains("귀")
            || lower.Contains("boing")
            || lower.Contains("bk_")
            || lower.Contains("boing_visual");
    }

    private static string NormalizeBoneName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty);
    }

    private static bool IsLeftBoneName(string rawName, string normalizedName)
    {
        string lower = rawName.ToLowerInvariant();
        return lower.Contains("left")
            || lower.EndsWith(".l")
            || lower.EndsWith("_l")
            || lower.EndsWith("-l")
            || lower.EndsWith(" l")
            || lower.StartsWith("l.")
            || lower.StartsWith("l_")
            || lower.StartsWith("l-")
            || lower.StartsWith("l ")
            || normalizedName.StartsWith("left")
            || normalizedName.EndsWith("left")
            || normalizedName.EndsWith("l")
            || (normalizedName.StartsWith("l") && !normalizedName.StartsWith("lower"));
    }

    private static bool IsRightBoneName(string rawName, string normalizedName)
    {
        string lower = rawName.ToLowerInvariant();
        return lower.Contains("right")
            || lower.EndsWith(".r")
            || lower.EndsWith("_r")
            || lower.EndsWith("-r")
            || lower.EndsWith(" r")
            || lower.StartsWith("r.")
            || lower.StartsWith("r_")
            || lower.StartsWith("r-")
            || lower.StartsWith("r ")
            || normalizedName.StartsWith("right")
            || normalizedName.EndsWith("right")
            || normalizedName.EndsWith("r")
            || (normalizedName.StartsWith("r") && !normalizedName.StartsWith("root"));
    }

    private void ResetRuntimeState()
    {
        _hasPreviousGroundedState = false;
        _previousGrounded = false;
        _stepPhase = 0f;
        _locomotion01 = 0f;
        _locomotionVelocity = 0f;
        _sprint01 = 0f;
        _sprintVelocity = 0f;
        _jumpWeight = 0f;
        _jumpWeightVelocity = 0f;
        _fallWeight = 0f;
        _fallWeightVelocity = 0f;
        _landingWeight = 0f;
        _landingWeightVelocity = 0f;
        _landingTimer = 0f;
    }

    private void ResetControlledBones()
    {
        ResetTarget(ref _spine);
        ResetTarget(ref _chest);
        ResetTarget(ref _neck);
        ResetTarget(ref _head);
        ResetTarget(ref _leftShoulder);
        ResetTarget(ref _rightShoulder);
        ResetTarget(ref _leftUpperArm);
        ResetTarget(ref _rightUpperArm);
        ResetTarget(ref _leftLowerArm);
        ResetTarget(ref _rightLowerArm);
        ResetTarget(ref _leftHand);
        ResetTarget(ref _rightHand);
        ResetTarget(ref _leftUpperLeg);
        ResetTarget(ref _rightUpperLeg);
        ResetTarget(ref _leftLowerLeg);
        ResetTarget(ref _rightLowerLeg);
        ResetTarget(ref _leftFoot);
        ResetTarget(ref _rightFoot);
        ResetTarget(ref _leftToes);
        ResetTarget(ref _rightToes);
    }

    private static void ApplyTarget(ref BonePoseTarget target, Quaternion proceduralRotation)
    {
        if (!target.IsValid)
            return;

        target.transform.localRotation = target.initialLocalRotation * proceduralRotation;
    }

    private static void ResetTarget(ref BonePoseTarget target)
    {
        if (!target.IsValid)
            return;

        target.transform.localRotation = target.initialLocalRotation;
    }

    private static Quaternion BuildRotation(float pitchDegrees, float yawDegrees, float rollDegrees)
    {
        return Quaternion.Euler(
            ClampPoseAngle(pitchDegrees),
            ClampPoseAngle(yawDegrees),
            ClampPoseAngle(rollDegrees));
    }

    private static float ClampPoseAngle(float value)
    {
        return Mathf.Clamp(value, -MaxProceduralDegrees, MaxProceduralDegrees);
    }

    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float deltaTime)
    {
        if (smoothTime <= 0f || deltaTime <= 0f)
        {
            velocity = 0f;
            return target;
        }

        return Mathf.SmoothDamp(current, target, ref velocity, smoothTime, Mathf.Infinity, deltaTime);
    }

    private static void Swap(ref float a, ref float b)
    {
        float temp = a;
        a = b;
        b = temp;
    }

    private void WarnIfAnimatorMayFight()
    {
        if (!debugLogs || !warnIfAnimatorEnabled || _warnedAnimatorMayFight)
            return;

        if (visualAnimator == null && visualRoot != null)
            visualAnimator = visualRoot.GetComponentInChildren<Animator>(true);

        if (visualAnimator == null || !visualAnimator.enabled || visualAnimator.runtimeAnimatorController == null)
            return;

        _warnedAnimatorMayFight = true;
        Debug.LogWarning("[HamsterProceduralPoseController] Animator may fight procedural pose. Disable the visual Animator or use an Idle-only controller if the pose is overwritten during testing.", this);
    }

    private void LogDebugState(MotionSample sample)
    {
        if (!debugLogs || Time.time < _nextDebugLogTime)
            return;

        _nextDebugLogTime = Time.time + Mathf.Max(0.02f, debugLogInterval);
        Debug.Log(
            $"[HamsterProceduralPoseController] speed={sample.planarSpeed:F2} sprint={_sprint01:F2} grounded={sample.isGrounded} groundedKnown={sample.hasGroundedState} vertical={sample.verticalVelocity:F2} locomotion={_locomotion01:F2} jump={_jumpWeight:F2} fall={_fallWeight:F2} land={_landingWeight:F2} source={sample.source} bones arms={animateArms} legs={animateLegs} chest={animateChest}",
            this);
    }

    private void LogBoneResult(BonePoseTarget target)
    {
        if (!debugLogs)
            return;

        if (target.IsValid)
            Debug.Log($"[HamsterProceduralPoseController] Found bone {target.debugName} path={GetTransformPath(target.transform)}", this);
        else
            Debug.Log($"[HamsterProceduralPoseController] Missing bone {target.debugName}", this);
    }

    private void LogSkippedBone(Transform skipped)
    {
        if (!debugLogs || skipped == null)
            return;

        Debug.Log($"[HamsterProceduralPoseController] Skipped tail/ear bone path={GetTransformPath(skipped)}", this);
    }

    private void LogMissingVisualRoot()
    {
        if (_missingVisualRootLogged || !debugLogs)
            return;

        _missingVisualRootLogged = true;
        Debug.LogWarning("[HamsterProceduralPoseController] visualRoot is missing. Assign VisualPreviewRoot before testing procedural pose.", this);
    }

    private void LogMissingTargetBody()
    {
        if (_missingTargetBodyLogged || !debugLogs)
            return;

        _missingTargetBodyLogged = true;
        Debug.LogWarning("[HamsterProceduralPoseController] targetBody is missing. Motion fallback cannot read Rigidbody velocity.", this);
    }

    private void LogMissingBoneCache()
    {
        if (_missingBoneCacheLogged || !debugLogs)
            return;

        _missingBoneCacheLogged = true;
        Debug.LogWarning("[HamsterProceduralPoseController] bone cache is empty. Enable autoFindBonesOnEnable or use the context menu to find bones.", this);
    }

    private static string GetTransformPath(Transform target)
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

    private void OnValidate()
    {
        walkSpeedForFullPose = Mathf.Max(MinimumSpeedForPose, walkSpeedForFullPose);
        walkStepFrequency = Mathf.Max(0f, walkStepFrequency);
        walkArmSwingDegrees = Mathf.Max(0f, walkArmSwingDegrees);
        walkLegSwingDegrees = Mathf.Max(0f, walkLegSwingDegrees);
        walkKneeBendDegrees = Mathf.Max(0f, walkKneeBendDegrees);
        walkChestPitchDegrees = Mathf.Clamp(walkChestPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        walkHeadCounterPitchDegrees = Mathf.Max(0f, walkHeadCounterPitchDegrees);

        runSpeedForFullPose = Mathf.Max(MinimumSpeedForPose, runSpeedForFullPose);
        runStepFrequency = Mathf.Max(0f, runStepFrequency);
        runArmSwingDegrees = Mathf.Max(0f, runArmSwingDegrees);
        runLegSwingDegrees = Mathf.Max(0f, runLegSwingDegrees);
        runKneeBendDegrees = Mathf.Max(0f, runKneeBendDegrees);
        runChestPitchDegrees = Mathf.Clamp(runChestPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        runHeadCounterPitchDegrees = Mathf.Max(0f, runHeadCounterPitchDegrees);
        runPoseSprintBoost = Mathf.Max(0f, runPoseSprintBoost);

        jumpUpArmRaiseDegrees = Mathf.Max(0f, jumpUpArmRaiseDegrees);
        jumpUpLegTuckDegrees = Mathf.Max(0f, jumpUpLegTuckDegrees);
        jumpUpKneeBendDegrees = Mathf.Max(0f, jumpUpKneeBendDegrees);
        jumpUpChestPitchDegrees = Mathf.Clamp(jumpUpChestPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        jumpUpHeadPitchDegrees = Mathf.Clamp(jumpUpHeadPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        jumpBlendSmoothTime = Mathf.Max(0f, jumpBlendSmoothTime);

        fallArmSpreadDegrees = Mathf.Max(0f, fallArmSpreadDegrees);
        fallLegExtendDegrees = Mathf.Max(0f, fallLegExtendDegrees);
        fallChestPitchDegrees = Mathf.Clamp(fallChestPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        fallHeadPitchDegrees = Mathf.Clamp(fallHeadPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        fallBlendSmoothTime = Mathf.Max(0f, fallBlendSmoothTime);

        landingArmDownDegrees = Mathf.Max(0f, landingArmDownDegrees);
        landingLegBendDegrees = Mathf.Max(0f, landingLegBendDegrees);
        landingKneeBendDegrees = Mathf.Max(0f, landingKneeBendDegrees);
        landingChestPitchDegrees = Mathf.Clamp(landingChestPitchDegrees, -MaxProceduralDegrees, MaxProceduralDegrees);
        landingDuration = Mathf.Max(0f, landingDuration);
        landingBlendSmoothTime = Mathf.Max(0f, landingBlendSmoothTime);

        locomotionBlendSmoothTime = Mathf.Max(0f, locomotionBlendSmoothTime);
        sprintBlendSmoothTime = Mathf.Max(0f, sprintBlendSmoothTime);
        airborneThreshold = Mathf.Max(0f, airborneThreshold);
        groundedVelocityDeadZone = Mathf.Max(0f, groundedVelocityDeadZone);
        maxPoseDeltaTime = Mathf.Clamp(maxPoseDeltaTime, 0.005f, 0.1f);
        debugLogInterval = Mathf.Max(0.02f, debugLogInterval);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || visualRoot == null)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(visualRoot.position, 0.08f);
    }
}
