using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class DeskGimmickManager : NetworkBehaviour
{
    private enum PresentationPhase
    {
        Telegraph = 0,
        Response = 1,
        Scan = 2,
        Resolve = 3,
        End = 4
    }

    [Header("Modules")]
    [SerializeField] private DeveloperIntrusionGimmick developerIntrusion;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    private bool isDeveloperIntrusionRunning;
    private Coroutine _developerIntrusionWatchRoutine;
    private DeveloperIntrusionGimmick.Phase _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
    private bool _sentEndPresentation;

    [ContextMenu("Debug Start Developer Intrusion")]
    public void DebugStartDeveloperIntrusion()
    {
        if (!Application.isPlaying)
        {
            LogWarning("[DeskGimmickManager] Debug start ignored. Application is not playing.");
            return;
        }

        if (!IsSpawned)
        {
            LogWarning("[DeskGimmickManager] Debug start ignored. NetworkObject is not spawned.");
            return;
        }

        if (IsServer)
        {
            StartDeveloperIntrusionServer();
            return;
        }

        DebugStartDeveloperIntrusionServerRpc();
    }

    public void StartDeveloperIntrusionServer()
    {
        if (!IsServer)
            return;

        if (!IsSpawned)
        {
            LogWarning("[DeskGimmickManager] Developer Intrusion start skipped. NetworkObject is not spawned.");
            return;
        }

        ResolveRefs();

        if (developerIntrusion == null)
        {
            LogWarning("[DeskGimmickManager] DeveloperIntrusionGimmick reference is missing.");
            return;
        }

        if (isDeveloperIntrusionRunning || developerIntrusion.IsRunning)
        {
            Log("[DeskGimmickManager] Developer Intrusion start ignored. Already running.");
            return;
        }

        List<PlayerStatusModule> players = CollectActivePlayers();
        if (players.Count == 0)
        {
            LogWarning("[DeskGimmickManager] Developer Intrusion start skipped. No valid players found.");
            return;
        }

        isDeveloperIntrusionRunning = true;
        _lastObservedPhase = developerIntrusion.CurrentPhase;
        _sentEndPresentation = false;

        developerIntrusion.StartGimmick(players, false);

        if (_developerIntrusionWatchRoutine != null)
            StopCoroutine(_developerIntrusionWatchRoutine);

        _developerIntrusionWatchRoutine = StartCoroutine(WatchDeveloperIntrusionRoutine());
        Log($"[DeskGimmickManager] Developer Intrusion started. players:{players.Count}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void DebugStartDeveloperIntrusionServerRpc()
    {
        StartDeveloperIntrusionServer();
    }

    private IEnumerator WatchDeveloperIntrusionRoutine()
    {
        while (developerIntrusion != null && developerIntrusion.IsRunning)
        {
            DeveloperIntrusionGimmick.Phase currentPhase = developerIntrusion.CurrentPhase;
            if (currentPhase != _lastObservedPhase)
            {
                _lastObservedPhase = currentPhase;
                SendPresentationForPhase(currentPhase);
            }

            yield return null;
        }

        if (!_sentEndPresentation)
        {
            PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.End);
            _sentEndPresentation = true;
        }

        isDeveloperIntrusionRunning = false;
        _developerIntrusionWatchRoutine = null;
        _lastObservedPhase = DeveloperIntrusionGimmick.Phase.Idle;
        Log("[DeskGimmickManager] Developer Intrusion ended.");
    }

    private void SendPresentationForPhase(DeveloperIntrusionGimmick.Phase phase)
    {
        switch (phase)
        {
            case DeveloperIntrusionGimmick.Phase.Telegraph:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Telegraph);
                break;
            case DeveloperIntrusionGimmick.Phase.Response:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Response);
                break;
            case DeveloperIntrusionGimmick.Phase.Scan:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Scan);
                break;
            case DeveloperIntrusionGimmick.Phase.Resolve:
                PlayDeveloperIntrusionPresentationClientRpc((int)PresentationPhase.Resolve);
                break;
        }
    }

    [ClientRpc]
    private void PlayDeveloperIntrusionPresentationClientRpc(int phaseValue)
    {
        ResolveRefs();

        if (developerIntrusion == null)
        {
            LogWarning("[DeskGimmickManager] Cannot play presentation. DeveloperIntrusionGimmick reference is missing.");
            return;
        }

        PresentationPhase phase = (PresentationPhase)phaseValue;
        switch (phase)
        {
            case PresentationPhase.Telegraph:
                developerIntrusion.PlayTelegraphPresentation();
                break;
            case PresentationPhase.Response:
                developerIntrusion.PlayResponsePresentation();
                break;
            case PresentationPhase.Scan:
                developerIntrusion.PlayScanPresentation();
                break;
            case PresentationPhase.Resolve:
                developerIntrusion.PlayResolvePresentation();
                break;
            case PresentationPhase.End:
                developerIntrusion.PlayEndPresentation();
                break;
        }
    }

    private List<PlayerStatusModule> CollectActivePlayers()
    {
        List<PlayerStatusModule> players = new List<PlayerStatusModule>();
        PlayerStatusModule[] found = FindPlayerStatusModules();

        if (found == null)
            return players;

        for (int i = 0; i < found.Length; i++)
        {
            PlayerStatusModule status = found[i];
            if (status == null)
                continue;

            if (status.IsEliminated)
                continue;

            players.Add(status);
        }

        return players;
    }

    private PlayerStatusModule[] FindPlayerStatusModules()
    {
#if UNITY_6000_0_OR_NEWER
        return FindObjectsByType<PlayerStatusModule>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<PlayerStatusModule>();
#endif
    }

    private void ResolveRefs()
    {
        if (developerIntrusion == null)
            developerIntrusion = GetComponentInChildren<DeveloperIntrusionGimmick>(true);

        if (developerIntrusion == null)
        {
            developerIntrusion = FindFirstDeveloperIntrusion();
            if (developerIntrusion != null)
                LogWarning("[DeskGimmickManager] DeveloperIntrusionGimmick was found by scene fallback. Inspector reference is recommended.");
        }
    }

    private DeveloperIntrusionGimmick FindFirstDeveloperIntrusion()
    {
#if UNITY_6000_0_OR_NEWER
        DeveloperIntrusionGimmick[] found = FindObjectsByType<DeveloperIntrusionGimmick>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        DeveloperIntrusionGimmick[] found = FindObjectsOfType<DeveloperIntrusionGimmick>(true);
#endif
        if (found == null || found.Length == 0)
            return null;

        return found[0];
    }

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.LogWarning(message, this);
    }
}
