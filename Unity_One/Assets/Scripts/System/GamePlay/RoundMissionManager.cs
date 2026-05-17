using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum MissionFamily
{
    LastLocation = 0,
    LastHeldItem = 1,
    CarryToZone = 2,
    RichInDangerZone = 3,
    KnockOff = 4,
    GuessMission = 5
}

public enum MissionZoneKind
{
    Normal = 0,
    Danger = 1
}

public enum MissionResultState
{
    NotEvaluated = 0,
    Success = 1,
    Failed = 2
}

[Serializable]
public struct MissionZoneDefinition
{
    [Tooltip("미션에서 참조할 구역 ID입니다.")]
    public string zoneId;

    [Tooltip("UI나 로그에서 표시할 구역 이름입니다.")]
    public string displayName;

    [Tooltip("일반 구역인지 위험 구역인지 구분합니다.")]
    public MissionZoneKind zoneKind;

    [Tooltip("구역 판정에 사용할 Collider 목록입니다.")]
    public Collider[] colliders;

    [Tooltip("선택 시 구역 Gizmo를 표시할지 여부입니다.")]
    public bool showGizmo;

    [Tooltip("구역 Gizmo 색상입니다.")]
    public Color gizmoColor;

    public bool Contains(PlayerStatusModule status)
    {
        if (status == null)
            return false;

        return Contains(status.transform);
    }

    public bool Contains(Transform target)
    {
        if (target == null || colliders == null)
            return false;

        Vector3 point = target.position;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider zoneCollider = colliders[i];
            if (!CanUseCollider(zoneCollider))
                continue;

            if (zoneCollider.bounds.Contains(point))
                return true;

            Vector3 closestPoint = zoneCollider.ClosestPoint(point);
            if ((closestPoint - point).sqrMagnitude <= 0.0001f)
                return true;
        }

        return false;
    }

    private static bool CanUseCollider(Collider zoneCollider)
    {
        return zoneCollider != null &&
            zoneCollider.enabled &&
            zoneCollider.gameObject.activeInHierarchy;
    }
}

[Serializable]
public struct MissionTemplate
{
    [Tooltip("미션 계열입니다.")]
    public MissionFamily family;

    [Tooltip("미션 템플릿 ID입니다.")]
    public string missionId;

    [Tooltip("미션 표시 이름입니다.")]
    public string displayName;

    [Tooltip("미션 설명 형식입니다. {zone}, {itemId}, {coins}, {fallCount}, {target}, {guess} 토큰을 사용할 수 있습니다.")]
    public string descriptionFormat;

    [Tooltip("미션 성공 보상 코인입니다.")]
    public int rewardCoins;

    [Tooltip("필요한 구역 ID입니다. 비워두면 계열에 맞는 구역을 자동 선택합니다.")]
    public string requiredZoneId;

    [Tooltip("필요한 아이템 ID입니다.")]
    public int requiredItemId;

    [Tooltip("필요한 최소 코인 수입니다.")]
    public int requiredCoins;

    [Tooltip("필요한 낙사 기여 횟수입니다.")]
    public int requiredFallCount;

    [Tooltip("미션 배정 가중치입니다.")]
    public int weight;

    [Tooltip("이 미션 템플릿을 배정에 사용할지 여부입니다.")]
    public bool enabled;
}

[Serializable]
public struct MissionAssignment
{
    public ulong clientId;
    public MissionFamily family;
    public string missionId;
    public string displayName;
    public string description;
    public int rewardCoins;
    public string requiredZoneId;
    public int requiredItemId;
    public int requiredCoins;
    public int requiredFallCount;
    public ulong guessTargetClientId;
    public MissionFamily guessedFamily;
}

[Serializable]
public struct MissionResult
{
    public ulong clientId;
    public MissionFamily family;
    public string missionId;
    public MissionResultState resultState;
    public bool succeeded;
    public int rewardCoins;
    public int finalCoins;
    public string reason;
}

public class RoundMissionManager : NetworkBehaviour
{
    private const ulong InvalidClientId = ulong.MaxValue;

    [SerializeField, Tooltip("라운드 미션 시스템을 사용할지 여부입니다.")]
    private bool enableMissions = true;

    [SerializeField, Tooltip("미션 성공 시 보상 코인을 실제 지갑에 지급할지 여부입니다.")]
    private bool applyMissionRewards = true;

    [SerializeField, Tooltip("미션 보상 적용 후 최종 코인 수로 승자를 계산할지 여부입니다.")]
    private bool useFinalCoinsAsWinner = true;

    [SerializeField, Tooltip("미션 판정에 사용할 위치/위험 구역 목록입니다.")]
    private MissionZoneDefinition[] missionZones = Array.Empty<MissionZoneDefinition>();

    [SerializeField, Tooltip("라운드에서 배정할 수 있는 미션 템플릿 목록입니다.")]
    private MissionTemplate[] missionTemplates = Array.Empty<MissionTemplate>();

    [SerializeField, Tooltip("미션 템플릿 보상이 0 이하일 때 사용할 기본 보상 코인입니다.")]
    private int fallbackRewardCoins = 8;

    [SerializeField, Tooltip("상대 미션 맞추기에서 자기 자신을 대상으로 선택하지 못하게 할지 여부입니다.")]
    private bool guessMissionRequiresDifferentTarget = true;

    [SerializeField, Tooltip("미션 배정/판정 디버그 로그를 출력할지 여부입니다.")]
    private bool enableDebugLogs = false;

    private readonly List<ulong> _participantClientIds = new List<ulong>();
    private readonly Dictionary<ulong, MissionAssignment> _assignmentsByClientId = new Dictionary<ulong, MissionAssignment>();
    private readonly Dictionary<ulong, MissionResult> _resultsByClientId = new Dictionary<ulong, MissionResult>();
    private readonly List<MissionResult> _resultsSnapshot = new List<MissionResult>();
    private readonly List<MissionResult> _localResultsSnapshot = new List<MissionResult>();
    private readonly Dictionary<ulong, int> _fallContributionCounts = new Dictionary<ulong, int>();
    private readonly HashSet<ulong> _submittedGuessClientIds = new HashSet<ulong>();

    private bool _isMissionRoundActive;
    private bool _hasEvaluatedResults;
    private bool _hasLocalMissionAssignment;
    private bool _hasLocalResultsSnapshot;
    private MissionAssignment _localMissionAssignment;

    public bool IsMissionRoundActive => _isMissionRoundActive;
    public bool HasEvaluatedResults => _hasEvaluatedResults;
    public bool HasLocalMissionAssignment => _hasLocalMissionAssignment;
    public bool HasLocalResultsSnapshot => _hasLocalResultsSnapshot;

    public event Action<MissionAssignment> LocalMissionAssignmentChanged;
    public event Action LocalMissionResultsChanged;

    private void Awake()
    {
        EnsureDefaultMissionTemplates();
    }

    public void ServerBeginRoundMissions(IReadOnlyList<ulong> participantClientIds)
    {
        if (!IsServer)
            return;

        ServerClearRoundMissions();

        if (!enableMissions || participantClientIds == null || participantClientIds.Count == 0)
            return;

        EnsureDefaultMissionTemplates();
        CaptureParticipants(participantClientIds);

        if (_participantClientIds.Count == 0)
            return;

        List<MissionTemplate> eligibleTemplates = BuildEligibleTemplateList();
        if (eligibleTemplates.Count == 0)
            return;

        List<MissionTemplate> coverageTemplates = BuildFamilyCoverageTemplateList(eligibleTemplates);
        for (int i = 0; i < _participantClientIds.Count; i++)
        {
            ulong clientId = _participantClientIds[i];
            MissionTemplate template = i < coverageTemplates.Count
                ? coverageTemplates[i]
                : PickMissionTemplate();

            MissionAssignment assignment = BuildAssignment(clientId, template);
            _assignmentsByClientId[clientId] = assignment;
            Log($"[RoundMission] Assigned client:{clientId} family:{assignment.family} mission:{assignment.missionId}");
        }

        _isMissionRoundActive = _assignmentsByClientId.Count > 0;
        if (_isMissionRoundActive)
            SendAssignmentsToClientsServer();
    }

    public void ServerClearRoundMissions()
    {
        if (!IsServer)
            return;

        _participantClientIds.Clear();
        _assignmentsByClientId.Clear();
        _resultsByClientId.Clear();
        _resultsSnapshot.Clear();
        ClearLocalMissionResultsCache();
        _fallContributionCounts.Clear();
        _submittedGuessClientIds.Clear();
        _isMissionRoundActive = false;
        _hasEvaluatedResults = false;

        SendClearLocalMissionAssignmentsServer();
        SendClearLocalMissionResultsServer();
    }

    public bool ServerEvaluateMissionsAndApplyRewards()
    {
        if (!IsServer || !enableMissions)
            return false;

        _resultsByClientId.Clear();
        _resultsSnapshot.Clear();

        for (int i = 0; i < _participantClientIds.Count; i++)
        {
            ulong clientId = _participantClientIds[i];
            MissionResult result = EvaluateClientMissionServer(clientId);
            _resultsByClientId[clientId] = result;
            _resultsSnapshot.Add(result);
        }

        _hasEvaluatedResults = true;
        _isMissionRoundActive = false;
        SendMissionResultsToClientsServer();
        return true;
    }

    public bool ServerResolveFinalCoinWinner(out ulong winnerClientId, out bool isDraw)
    {
        winnerClientId = InvalidClientId;
        isDraw = true;

        if (!IsServer || !enableMissions || !useFinalCoinsAsWinner || !_hasEvaluatedResults)
            return false;

        int validCount = 0;
        int bestCoins = int.MinValue;
        bool hasTie = false;

        for (int i = 0; i < _participantClientIds.Count; i++)
        {
            ulong clientId = _participantClientIds[i];
            if (!TryGetPlayerWallet(clientId, out _))
                continue;

            int coins = _resultsByClientId.TryGetValue(clientId, out MissionResult result)
                ? result.finalCoins
                : GetCurrentCoinsServer(clientId);

            validCount++;
            if (coins > bestCoins)
            {
                bestCoins = coins;
                winnerClientId = clientId;
                hasTie = false;
            }
            else if (coins == bestCoins)
            {
                hasTie = true;
            }
        }

        if (validCount == 0 || hasTie)
        {
            winnerClientId = InvalidClientId;
            isDraw = true;
            return true;
        }

        isDraw = false;
        return true;
    }

    public bool TryGetAssignment(ulong clientId, out MissionAssignment assignment)
    {
        return _assignmentsByClientId.TryGetValue(clientId, out assignment);
    }

    public bool TryGetLocalMissionAssignment(out MissionAssignment assignment)
    {
        assignment = _localMissionAssignment;
        return _hasLocalMissionAssignment;
    }

    public void RequestLocalMissionRefresh()
    {
        LocalMissionAssignmentChanged?.Invoke(_localMissionAssignment);
    }

    public void RequestLocalMissionResultsRefresh()
    {
        LocalMissionResultsChanged?.Invoke();
    }

    public bool TryGetResult(ulong clientId, out MissionResult result)
    {
        return _resultsByClientId.TryGetValue(clientId, out result);
    }

    public IReadOnlyList<MissionResult> GetResultsSnapshot()
    {
        if (IsServer)
            return _resultsSnapshot;

        if (_hasLocalResultsSnapshot)
            return _localResultsSnapshot;

        return _resultsSnapshot;
    }

    public void ServerRecordFallContribution(ulong actorClientId, ulong targetClientId)
    {
        if (!IsServer || !_isMissionRoundActive)
            return;

        if (actorClientId == InvalidClientId || targetClientId == InvalidClientId)
            return;

        if (guessMissionRequiresDifferentTarget && actorClientId == targetClientId)
            return;

        if (!_assignmentsByClientId.ContainsKey(actorClientId) || !_assignmentsByClientId.ContainsKey(targetClientId))
            return;

        _fallContributionCounts.TryGetValue(actorClientId, out int currentCount);
        _fallContributionCounts[actorClientId] = currentCount + 1;
        Log($"[RoundMission] Fall contribution actor:{actorClientId} target:{targetClientId} count:{currentCount + 1}");
    }

    public bool ServerSubmitMissionGuess(ulong guesserClientId, ulong targetClientId, MissionFamily guessedFamily)
    {
        if (!IsServer || !_isMissionRoundActive)
            return false;

        if (!_assignmentsByClientId.TryGetValue(guesserClientId, out MissionAssignment assignment))
            return false;

        if (assignment.family != MissionFamily.GuessMission)
            return false;

        if (_submittedGuessClientIds.Contains(guesserClientId))
            return false;

        if (guessMissionRequiresDifferentTarget && guesserClientId == targetClientId)
            return false;

        if (!_assignmentsByClientId.ContainsKey(targetClientId))
            return false;

        assignment.guessTargetClientId = targetClientId;
        assignment.guessedFamily = guessedFamily;
        _assignmentsByClientId[guesserClientId] = assignment;
        _submittedGuessClientIds.Add(guesserClientId);

        Log($"[RoundMission] Guess submitted guesser:{guesserClientId} target:{targetClientId} guessed:{guessedFamily}");
        return true;
    }

    private void SendAssignmentsToClientsServer()
    {
        if (!IsServer || !IsSpawned)
            return;

        foreach (KeyValuePair<ulong, MissionAssignment> assignmentPair in _assignmentsByClientId)
        {
            SendAssignmentToClientServer(assignmentPair.Value);
        }
    }

    private void SendAssignmentToClientServer(MissionAssignment assignment)
    {
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { assignment.clientId }
            }
        };

        ReceiveLocalMissionAssignmentClientRpc(
            assignment.clientId,
            (int)assignment.family,
            assignment.missionId ?? string.Empty,
            assignment.displayName ?? string.Empty,
            assignment.description ?? string.Empty,
            assignment.rewardCoins,
            assignment.requiredZoneId ?? string.Empty,
            assignment.requiredItemId,
            assignment.requiredCoins,
            assignment.requiredFallCount,
            assignment.guessTargetClientId,
            (int)assignment.guessedFamily,
            clientRpcParams);
    }

    private void SendClearLocalMissionAssignmentsServer()
    {
        if (!IsServer || !IsSpawned)
            return;

        SendClearLocalMissionAssignmentsClientRpc();
    }

    private void SendMissionResultsToClientsServer()
    {
        if (!IsServer || !IsSpawned)
            return;

        ClearMissionResultsClientRpc();

        for (int i = 0; i < _resultsSnapshot.Count; i++)
        {
            MissionResult result = _resultsSnapshot[i];
            ReceiveMissionResultClientRpc(
                result.clientId,
                (int)result.family,
                result.missionId ?? string.Empty,
                GetMissionResultDisplayName(result),
                (int)result.resultState,
                result.succeeded,
                result.rewardCoins,
                result.finalCoins,
                result.reason ?? string.Empty);
        }

        CompleteMissionResultsClientRpc();
    }

    private void SendClearLocalMissionResultsServer()
    {
        if (!IsServer || !IsSpawned)
            return;

        ClearMissionResultsClientRpc();
    }

    [ClientRpc]
    private void ReceiveLocalMissionAssignmentClientRpc(
        ulong clientId,
        int family,
        string missionId,
        string displayName,
        string description,
        int rewardCoins,
        string requiredZoneId,
        int requiredItemId,
        int requiredCoins,
        int requiredFallCount,
        ulong guessTargetClientId,
        int guessedFamily,
        ClientRpcParams clientRpcParams = default)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.LocalClientId != clientId)
            return;

        _localMissionAssignment = new MissionAssignment
        {
            clientId = clientId,
            family = ToMissionFamily(family),
            missionId = missionId ?? string.Empty,
            displayName = displayName ?? string.Empty,
            description = description ?? string.Empty,
            rewardCoins = rewardCoins,
            requiredZoneId = requiredZoneId ?? string.Empty,
            requiredItemId = requiredItemId,
            requiredCoins = requiredCoins,
            requiredFallCount = requiredFallCount,
            guessTargetClientId = guessTargetClientId,
            guessedFamily = ToMissionFamily(guessedFamily)
        };
        _hasLocalMissionAssignment = true;

        LocalMissionAssignmentChanged?.Invoke(_localMissionAssignment);
    }

    [ClientRpc]
    private void SendClearLocalMissionAssignmentsClientRpc()
    {
        ClearLocalMissionAssignment();
    }

    private void ClearLocalMissionAssignment()
    {
        _localMissionAssignment = default;
        _hasLocalMissionAssignment = false;
        LocalMissionAssignmentChanged?.Invoke(_localMissionAssignment);
    }

    [ClientRpc]
    private void ClearMissionResultsClientRpc()
    {
        ClearLocalMissionResultsCache();
    }

    [ClientRpc]
    private void ReceiveMissionResultClientRpc(
        ulong clientId,
        int familyValue,
        string missionId,
        string displayName,
        int resultStateValue,
        bool succeeded,
        int rewardCoins,
        int finalCoins,
        string reason)
    {
        _localResultsSnapshot.Add(BuildMissionResultFromRpc(
            clientId,
            familyValue,
            missionId,
            displayName,
            resultStateValue,
            succeeded,
            rewardCoins,
            finalCoins,
            reason));
    }

    [ClientRpc]
    private void CompleteMissionResultsClientRpc()
    {
        _hasLocalResultsSnapshot = true;
        LocalMissionResultsChanged?.Invoke();
    }

    private void ClearLocalMissionResultsCache()
    {
        _localResultsSnapshot.Clear();
        _hasLocalResultsSnapshot = false;
    }

    private MissionResult BuildMissionResultFromRpc(
        ulong clientId,
        int familyValue,
        string missionId,
        string displayName,
        int resultStateValue,
        bool succeeded,
        int rewardCoins,
        int finalCoins,
        string reason)
    {
        MissionFamily family = SafeMissionFamilyFromInt(familyValue);
        MissionResultState resultState = SafeMissionResultStateFromInt(resultStateValue);
        string fallbackName = !string.IsNullOrWhiteSpace(displayName) ? displayName : family.ToString();

        return new MissionResult
        {
            clientId = clientId,
            family = family,
            missionId = string.IsNullOrWhiteSpace(missionId) ? fallbackName : missionId,
            resultState = resultState,
            succeeded = succeeded,
            rewardCoins = rewardCoins,
            finalCoins = finalCoins,
            reason = string.IsNullOrWhiteSpace(reason) ? "결과 사유 없음" : reason
        };
    }

    private string GetMissionResultDisplayName(MissionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.missionId))
            return result.missionId;

        return result.family.ToString();
    }

    private MissionResult EvaluateClientMissionServer(ulong clientId)
    {
        if (!_assignmentsByClientId.TryGetValue(clientId, out MissionAssignment assignment))
        {
            return BuildResult(clientId, default, false, 0, "미션 배정 정보가 없습니다.");
        }

        bool succeeded = EvaluateMissionServer(assignment, out string reason);
        int awardedCoins = 0;
        if (succeeded)
        {
            if (applyMissionRewards)
                AddRewardCoinsServer(clientId, assignment.rewardCoins, out awardedCoins);
            else
                awardedCoins = Mathf.Max(0, assignment.rewardCoins);
        }

        int finalCoins = GetCurrentCoinsServer(clientId);
        MissionResult result = new MissionResult
        {
            clientId = clientId,
            family = assignment.family,
            missionId = assignment.missionId,
            resultState = succeeded ? MissionResultState.Success : MissionResultState.Failed,
            succeeded = succeeded,
            rewardCoins = awardedCoins,
            finalCoins = finalCoins,
            reason = reason
        };

        Log($"[RoundMission] Evaluated client:{clientId} success:{succeeded} reward:{awardedCoins} finalCoins:{finalCoins} reason:{reason}");
        return result;
    }

    private bool EvaluateMissionServer(MissionAssignment assignment, out string reason)
    {
        reason = string.Empty;

        switch (assignment.family)
        {
            case MissionFamily.LastLocation:
                return EvaluateLastLocation(assignment, out reason);
            case MissionFamily.LastHeldItem:
                return EvaluateLastHeldItem(assignment, out reason);
            case MissionFamily.CarryToZone:
                return EvaluateCarryToZone(assignment, out reason);
            case MissionFamily.RichInDangerZone:
                return EvaluateRichInDangerZone(assignment, out reason);
            case MissionFamily.KnockOff:
                return EvaluateKnockOff(assignment, out reason);
            case MissionFamily.GuessMission:
                return EvaluateGuessMission(assignment, out reason);
            default:
                reason = "알 수 없는 미션 계열입니다.";
                return false;
        }
    }

    private bool EvaluateLastLocation(MissionAssignment assignment, out string reason)
    {
        if (string.IsNullOrWhiteSpace(assignment.requiredZoneId))
        {
            reason = "필요한 미션 구역이 설정되지 않았습니다.";
            return false;
        }

        if (IsClientInsideZone(assignment.clientId, assignment.requiredZoneId))
        {
            reason = "종료 순간 지정 구역 안에 있었습니다.";
            return true;
        }

        reason = "종료 순간 지정 구역 안에 있지 않았습니다.";
        return false;
    }

    private bool EvaluateLastHeldItem(MissionAssignment assignment, out string reason)
    {
        return EvaluateHeldItemRequirement(assignment, out reason);
    }

    private bool EvaluateCarryToZone(MissionAssignment assignment, out string reason)
    {
        bool inZone = IsClientInsideZone(assignment.clientId, assignment.requiredZoneId);
        bool hasItem = EvaluateHeldItemRequirement(assignment, out string itemReason);

        if (inZone && hasItem)
        {
            reason = "종료 순간 지정 구역에서 지정 아이템을 들고 있었습니다.";
            return true;
        }

        if (!inZone && !hasItem)
        {
            reason = "지정 구역 조건과 아이템 조건을 모두 만족하지 못했습니다.";
            return false;
        }

        reason = !inZone ? "종료 순간 지정 구역 안에 있지 않았습니다." : itemReason;
        return false;
    }

    private bool EvaluateRichInDangerZone(MissionAssignment assignment, out string reason)
    {
        int currentCoins = GetCurrentCoinsServer(assignment.clientId);
        int requiredCoins = Mathf.Max(0, assignment.requiredCoins);
        bool hasEnoughCoins = currentCoins >= requiredCoins;
        bool inDangerZone = IsClientInsideZone(assignment.clientId, assignment.requiredZoneId);

        if (hasEnoughCoins && inDangerZone)
        {
            reason = "종료 순간 충분한 코인을 들고 위험 구역 안에 있었습니다.";
            return true;
        }

        if (!hasEnoughCoins && !inDangerZone)
        {
            reason = $"코인이 부족하고 위험 구역 안에 있지 않았습니다. 현재:{currentCoins}, 필요:{requiredCoins}";
            return false;
        }

        reason = !hasEnoughCoins
            ? $"필요 코인이 부족했습니다. 현재:{currentCoins}, 필요:{requiredCoins}"
            : "종료 순간 위험 구역 안에 있지 않았습니다.";
        return false;
    }

    private bool EvaluateKnockOff(MissionAssignment assignment, out string reason)
    {
        int requiredFallCount = Mathf.Max(1, assignment.requiredFallCount);
        _fallContributionCounts.TryGetValue(assignment.clientId, out int recordedFallCount);

        if (recordedFallCount >= requiredFallCount)
        {
            reason = "라운드 중 낙사 기여 조건을 달성했습니다.";
            return true;
        }

        reason = "라운드 중 낙사 기여 기록이 부족합니다.";
        return false;
    }

    private bool EvaluateGuessMission(MissionAssignment assignment, out string reason)
    {
        if (!_submittedGuessClientIds.Contains(assignment.clientId))
        {
            reason = "상대 미션 계열을 제출하지 않았습니다.";
            return false;
        }

        if (!_assignmentsByClientId.TryGetValue(assignment.guessTargetClientId, out MissionAssignment targetAssignment))
        {
            reason = "추측 대상의 미션 정보를 찾을 수 없습니다.";
            return false;
        }

        if (assignment.guessedFamily == targetAssignment.family)
        {
            reason = "상대 미션 계열을 맞혔습니다.";
            return true;
        }

        reason = $"상대 미션 계열 추측이 틀렸습니다. 실제:{targetAssignment.family}, 추측:{assignment.guessedFamily}";
        return false;
    }

    private bool EvaluateHeldItemRequirement(MissionAssignment assignment, out string reason)
    {
        if (assignment.requiredItemId <= 0)
        {
            reason = "필요한 아이템 ID가 설정되지 않았습니다.";
            return false;
        }

        if (!TryGetHeldItemIdServer(assignment.clientId, out int heldItemId, out string heldItemReason))
        {
            reason = heldItemReason;
            return false;
        }

        if (heldItemId == assignment.requiredItemId)
        {
            reason = "종료 순간 지정 아이템을 들고 있었습니다.";
            return true;
        }

        reason = $"다른 아이템을 들고 있었습니다. 현재:{heldItemId}, 필요:{assignment.requiredItemId}";
        return false;
    }

    private MissionResult BuildResult(ulong clientId, MissionAssignment assignment, bool succeeded, int rewardCoins, string reason)
    {
        return new MissionResult
        {
            clientId = clientId,
            family = assignment.family,
            missionId = assignment.missionId,
            resultState = succeeded ? MissionResultState.Success : MissionResultState.Failed,
            succeeded = succeeded,
            rewardCoins = rewardCoins,
            finalCoins = GetCurrentCoinsServer(clientId),
            reason = reason
        };
    }

    private void CaptureParticipants(IReadOnlyList<ulong> participantClientIds)
    {
        for (int i = 0; i < participantClientIds.Count; i++)
        {
            ulong clientId = participantClientIds[i];
            if (clientId == InvalidClientId || _participantClientIds.Contains(clientId))
                continue;

            _participantClientIds.Add(clientId);
        }
    }

    private MissionAssignment BuildAssignment(ulong clientId, MissionTemplate template)
    {
        MissionAssignment assignment = new MissionAssignment
        {
            clientId = clientId,
            family = template.family,
            missionId = string.IsNullOrWhiteSpace(template.missionId) ? template.family.ToString() : template.missionId,
            displayName = string.IsNullOrWhiteSpace(template.displayName) ? GetDefaultDisplayName(template.family) : template.displayName,
            rewardCoins = GetRewardCoins(template),
            requiredZoneId = ResolveRequiredZoneId(template),
            requiredItemId = template.requiredItemId,
            requiredCoins = Mathf.Max(0, template.requiredCoins),
            requiredFallCount = Mathf.Max(1, template.requiredFallCount),
            guessTargetClientId = ResolveDefaultGuessTarget(clientId),
            guessedFamily = MissionFamily.LastLocation
        };

        assignment.description = BuildMissionDescription(assignment);
        return assignment;
    }

    private int GetRewardCoins(MissionTemplate template)
    {
        return template.rewardCoins > 0 ? template.rewardCoins : Mathf.Max(0, fallbackRewardCoins);
    }

    private string ResolveRequiredZoneId(MissionTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.requiredZoneId))
            return template.requiredZoneId;

        switch (template.family)
        {
            case MissionFamily.LastLocation:
            case MissionFamily.CarryToZone:
                return PickZoneId(MissionZoneKind.Normal);
            case MissionFamily.RichInDangerZone:
                return PickZoneId(MissionZoneKind.Danger);
            default:
                return string.Empty;
        }
    }

    private ulong ResolveDefaultGuessTarget(ulong clientId)
    {
        if (_participantClientIds.Count <= 0)
            return InvalidClientId;

        List<ulong> candidates = new List<ulong>();
        for (int i = 0; i < _participantClientIds.Count; i++)
        {
            ulong candidate = _participantClientIds[i];
            if (guessMissionRequiresDifferentTarget && candidate == clientId)
                continue;

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return InvalidClientId;

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private string PickZoneId(MissionZoneKind preferredKind)
    {
        if (missionZones == null || missionZones.Length == 0)
            return string.Empty;

        List<string> matchingZoneIds = new List<string>();
        List<string> fallbackZoneIds = new List<string>();
        for (int i = 0; i < missionZones.Length; i++)
        {
            MissionZoneDefinition zone = missionZones[i];
            if (string.IsNullOrWhiteSpace(zone.zoneId))
                continue;

            if (zone.zoneKind == preferredKind)
                matchingZoneIds.Add(zone.zoneId);

            fallbackZoneIds.Add(zone.zoneId);
        }

        if (matchingZoneIds.Count > 0)
            return matchingZoneIds[UnityEngine.Random.Range(0, matchingZoneIds.Count)];

        if (fallbackZoneIds.Count > 0)
            return fallbackZoneIds[UnityEngine.Random.Range(0, fallbackZoneIds.Count)];

        return string.Empty;
    }

    private List<MissionTemplate> BuildEligibleTemplateList()
    {
        List<MissionTemplate> eligibleTemplates = new List<MissionTemplate>();
        if (missionTemplates == null)
            return eligibleTemplates;

        for (int i = 0; i < missionTemplates.Length; i++)
        {
            MissionTemplate template = missionTemplates[i];
            if (!template.enabled)
                continue;

            eligibleTemplates.Add(template);
        }

        return eligibleTemplates;
    }

    private List<MissionTemplate> BuildFamilyCoverageTemplateList(List<MissionTemplate> eligibleTemplates)
    {
        List<MissionTemplate> coverageTemplates = new List<MissionTemplate>();
        if (eligibleTemplates == null || eligibleTemplates.Count == 0)
            return coverageTemplates;

        for (int familyValue = 0; familyValue <= (int)MissionFamily.GuessMission; familyValue++)
        {
            MissionFamily family = (MissionFamily)familyValue;
            for (int i = 0; i < eligibleTemplates.Count; i++)
            {
                if (eligibleTemplates[i].family != family)
                    continue;

                coverageTemplates.Add(eligibleTemplates[i]);
                break;
            }
        }

        return coverageTemplates;
    }

    private MissionTemplate PickMissionTemplate()
    {
        List<MissionTemplate> eligibleTemplates = BuildEligibleTemplateList();
        if (eligibleTemplates.Count == 0)
            return default;

        int totalWeight = 0;
        for (int i = 0; i < eligibleTemplates.Count; i++)
            totalWeight += Mathf.Max(0, eligibleTemplates[i].weight);

        if (totalWeight <= 0)
            return eligibleTemplates[UnityEngine.Random.Range(0, eligibleTemplates.Count)];

        int selectedWeight = UnityEngine.Random.Range(0, totalWeight);
        for (int i = 0; i < eligibleTemplates.Count; i++)
        {
            MissionTemplate template = eligibleTemplates[i];
            selectedWeight -= Mathf.Max(0, template.weight);
            if (selectedWeight < 0)
                return template;
        }

        return eligibleTemplates[eligibleTemplates.Count - 1];
    }

    private bool TryGetPlayerHub(ulong clientId, out PlayerHub hub)
    {
        hub = null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return false;

        if (!networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client == null)
            return false;

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        hub = playerObject.GetComponentInChildren<PlayerHub>(true);
        return hub != null;
    }

    private bool TryGetPlayerStatus(ulong clientId, out PlayerStatusModule status)
    {
        status = null;

        if (!TryGetPlayerHub(clientId, out PlayerHub hub))
            return false;

        status = hub.GetComponentInChildren<PlayerStatusModule>(true);
        return status != null;
    }

    private bool TryGetPlayerWallet(ulong clientId, out PlayerCoinWalletModule wallet)
    {
        wallet = null;

        if (!TryGetPlayerHub(clientId, out PlayerHub hub))
            return false;

        wallet = hub.CoinWalletModule != null
            ? hub.CoinWalletModule
            : hub.GetComponentInChildren<PlayerCoinWalletModule>(true);

        return wallet != null;
    }

    private bool TryGetPlayerInteract(ulong clientId, out PlayerInteractModule interact)
    {
        interact = null;

        if (!TryGetPlayerHub(clientId, out PlayerHub hub))
            return false;

        interact = hub.GetComponentInChildren<PlayerInteractModule>(true);
        return interact != null;
    }

    private bool TryGetHeldItemIdServer(ulong clientId, out int itemId, out string reason)
    {
        itemId = 0;
        reason = string.Empty;

        if (!TryGetPlayerInteract(clientId, out PlayerInteractModule interact))
        {
            reason = "현재 들고 있는 아이템 ID를 확인할 수 없습니다.";
            return false;
        }

        if (interact.TryGetHeldItemId(out itemId) && itemId > 0)
            return true;

        if (!interact.HasHeldItem())
        {
            reason = "현재 들고 있는 아이템이 없습니다.";
            return false;
        }

        WeaponItemDataSO weaponData = interact.GetHeldWeaponData();
        if (weaponData != null && weaponData.itemId > 0)
        {
            itemId = weaponData.itemId;
            return true;
        }

        reason = "현재 들고 있는 아이템 ID를 확인할 수 없습니다.";
        return false;
    }

    private bool IsClientInsideZone(ulong clientId, string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return false;

        MissionZoneDefinition zone = FindZone(zoneId);
        if (string.IsNullOrWhiteSpace(zone.zoneId))
            return false;

        if (!TryGetPlayerStatus(clientId, out PlayerStatusModule status))
            return false;

        return zone.Contains(status);
    }

    private MissionZoneDefinition FindZone(string zoneId)
    {
        if (missionZones == null || string.IsNullOrWhiteSpace(zoneId))
            return default;

        for (int i = 0; i < missionZones.Length; i++)
        {
            MissionZoneDefinition zone = missionZones[i];
            if (string.Equals(zone.zoneId, zoneId, StringComparison.Ordinal))
                return zone;
        }

        return default;
    }

    private string BuildMissionDescription(MissionAssignment assignment)
    {
        MissionTemplate template = FindTemplate(assignment.missionId, assignment.family);
        string format = string.IsNullOrWhiteSpace(template.descriptionFormat)
            ? GetDefaultDescriptionFormat(assignment.family)
            : template.descriptionFormat;

        return format
            .Replace("{zone}", GetZoneDisplayName(assignment.requiredZoneId))
            .Replace("{itemId}", assignment.requiredItemId.ToString())
            .Replace("{coins}", assignment.requiredCoins.ToString())
            .Replace("{fallCount}", assignment.requiredFallCount.ToString())
            .Replace("{target}", FormatClientId(assignment.guessTargetClientId))
            .Replace("{guess}", assignment.guessedFamily.ToString());
    }

    private MissionTemplate FindTemplate(string missionId, MissionFamily family)
    {
        if (missionTemplates == null)
            return default;

        for (int i = 0; i < missionTemplates.Length; i++)
        {
            MissionTemplate template = missionTemplates[i];
            if (!string.IsNullOrWhiteSpace(missionId) &&
                string.Equals(template.missionId, missionId, StringComparison.Ordinal))
                return template;
        }

        for (int i = 0; i < missionTemplates.Length; i++)
        {
            if (missionTemplates[i].family == family)
                return missionTemplates[i];
        }

        return default;
    }

    private string GetZoneDisplayName(string zoneId)
    {
        MissionZoneDefinition zone = FindZone(zoneId);
        if (!string.IsNullOrWhiteSpace(zone.displayName))
            return zone.displayName;

        return string.IsNullOrWhiteSpace(zoneId) ? "미지정 구역" : zoneId;
    }

    private string FormatClientId(ulong clientId)
    {
        return clientId == InvalidClientId ? "미지정" : $"Player {clientId}";
    }

    private MissionFamily ToMissionFamily(int value)
    {
        return SafeMissionFamilyFromInt(value);
    }

    private MissionFamily SafeMissionFamilyFromInt(int value)
    {
        if (value < (int)MissionFamily.LastLocation || value > (int)MissionFamily.GuessMission)
            return MissionFamily.LastLocation;

        return (MissionFamily)value;
    }

    private MissionResultState SafeMissionResultStateFromInt(int value)
    {
        if (value < (int)MissionResultState.NotEvaluated || value > (int)MissionResultState.Failed)
            return MissionResultState.NotEvaluated;

        return (MissionResultState)value;
    }

    private void AddRewardCoinsServer(ulong clientId, int rewardCoins, out int addedCoins)
    {
        addedCoins = 0;

        if (rewardCoins <= 0)
            return;

        if (!TryGetPlayerWallet(clientId, out PlayerCoinWalletModule wallet))
            return;

        wallet.ServerTryAddCoins(rewardCoins, out addedCoins);
    }

    private int GetCurrentCoinsServer(ulong clientId)
    {
        if (!TryGetPlayerWallet(clientId, out PlayerCoinWalletModule wallet))
            return 0;

        return wallet.CurrentCoins;
    }

    private string GetDefaultDisplayName(MissionFamily family)
    {
        switch (family)
        {
            case MissionFamily.LastLocation:
                return "마지막 위치";
            case MissionFamily.LastHeldItem:
                return "마지막 소지품";
            case MissionFamily.CarryToZone:
                return "몰래 운반";
            case MissionFamily.RichInDangerZone:
                return "위험한 부자";
            case MissionFamily.KnockOff:
                return "떨어트리기";
            case MissionFamily.GuessMission:
                return "상대 미션 맞추기";
            default:
                return "비밀 미션";
        }
    }

    private string GetDefaultDescriptionFormat(MissionFamily family)
    {
        switch (family)
        {
            case MissionFamily.LastLocation:
                return "종료 순간 {zone} 안에 있으면 성공";
            case MissionFamily.LastHeldItem:
                return "종료 순간 itemId {itemId} 아이템을 들고 있으면 성공";
            case MissionFamily.CarryToZone:
                return "종료 순간 {zone} 안에서 itemId {itemId} 아이템을 들고 있으면 성공";
            case MissionFamily.RichInDangerZone:
                return "종료 순간 코인 {coins}개 이상을 보유하고 {zone} 안에 있으면 성공";
            case MissionFamily.KnockOff:
                return "라운드 중 상대를 {fallCount}회 낙사시키면 성공";
            case MissionFamily.GuessMission:
                return "{target}의 미션 계열을 한 번 맞히면 성공";
            default:
                return "비밀 미션을 달성하면 성공";
        }
    }

    private void EnsureDefaultMissionTemplates()
    {
        if (missionTemplates != null && missionTemplates.Length > 0)
            return;

        missionTemplates = new[]
        {
            CreateDefaultTemplate(MissionFamily.LastLocation, "mission_last_location", "마지막 위치", 8, string.Empty, 0, 0, 0),
            CreateDefaultTemplate(MissionFamily.LastHeldItem, "mission_last_held_item", "마지막 소지품", 8, string.Empty, 0, 0, 0),
            CreateDefaultTemplate(MissionFamily.CarryToZone, "mission_carry_to_zone", "몰래 운반", 12, string.Empty, 0, 0, 0),
            CreateDefaultTemplate(MissionFamily.RichInDangerZone, "mission_rich_in_danger_zone", "위험한 부자", 15, string.Empty, 0, 10, 0),
            CreateDefaultTemplate(MissionFamily.KnockOff, "mission_knock_off", "떨어트리기", 10, string.Empty, 0, 0, 1),
            CreateDefaultTemplate(MissionFamily.GuessMission, "mission_guess_mission", "상대 미션 맞추기", 12, string.Empty, 0, 0, 0)
        };
    }

    private MissionTemplate CreateDefaultTemplate(
        MissionFamily family,
        string missionId,
        string displayName,
        int rewardCoins,
        string requiredZoneId,
        int requiredItemId,
        int requiredCoins,
        int requiredFallCount)
    {
        return new MissionTemplate
        {
            family = family,
            missionId = missionId,
            displayName = displayName,
            descriptionFormat = GetDefaultDescriptionFormat(family),
            rewardCoins = rewardCoins,
            requiredZoneId = requiredZoneId,
            requiredItemId = requiredItemId,
            requiredCoins = requiredCoins,
            requiredFallCount = requiredFallCount,
            weight = 1,
            enabled = true
        };
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void OnValidate()
    {
        fallbackRewardCoins = Mathf.Max(0, fallbackRewardCoins);
        EnsureDefaultMissionTemplates();

        if (missionTemplates == null)
            return;

        for (int i = 0; i < missionTemplates.Length; i++)
        {
            MissionTemplate template = missionTemplates[i];
            template.rewardCoins = Mathf.Max(0, template.rewardCoins);
            template.requiredCoins = Mathf.Max(0, template.requiredCoins);
            template.requiredFallCount = Mathf.Max(0, template.requiredFallCount);
            template.weight = Mathf.Max(0, template.weight);
            missionTemplates[i] = template;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (missionZones == null)
            return;

        for (int i = 0; i < missionZones.Length; i++)
        {
            MissionZoneDefinition zone = missionZones[i];
            if (!zone.showGizmo || zone.colliders == null)
                continue;

            Gizmos.color = zone.gizmoColor;
            for (int colliderIndex = 0; colliderIndex < zone.colliders.Length; colliderIndex++)
            {
                Collider zoneCollider = zone.colliders[colliderIndex];
                if (zoneCollider == null)
                    continue;

                Gizmos.DrawWireCube(zoneCollider.bounds.center, zoneCollider.bounds.size);
            }
        }
    }
}
