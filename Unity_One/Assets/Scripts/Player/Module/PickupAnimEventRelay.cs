using UnityEngine;

public class PickupAnimEventRelay : MonoBehaviour
{
    [Tooltip("부모/자식에서 PlayerInteractModule을 자동 탐색합니다. 수동 지정이 필요하면 여기서 연결하세요.")]
    [SerializeField] private PlayerInteractModule interactModule;

    [Tooltip("부모/자식에서 PlayerStatusModule을 자동 탐색합니다. 기상 애니메이션 이벤트에 사용합니다.")]
    [SerializeField] private PlayerStatusModule statusModule;

    [Tooltip("부모/자식에서 PlayerCombatModule을 자동 탐색합니다. 공격 히트 이벤트에 사용합니다.")]
    [SerializeField] private PlayerCombatModule combatModule;

    private void Awake()
    {
        ResolveRefs();
    }

    private void ResolveRefs()
    {
        if (interactModule == null)
            interactModule = GetComponentInParent<PlayerInteractModule>();
        if (interactModule == null)
            interactModule = GetComponentInChildren<PlayerInteractModule>(true);

        if (statusModule == null)
            statusModule = GetComponentInParent<PlayerStatusModule>();
        if (statusModule == null)
            statusModule = GetComponentInChildren<PlayerStatusModule>(true);

        if (combatModule == null)
            combatModule = GetComponentInParent<PlayerCombatModule>();
        if (combatModule == null)
            combatModule = GetComponentInChildren<PlayerCombatModule>(true);
    }

    public void AnimEvent_AttachHeldItem()
    {
        if (interactModule == null)
            ResolveRefs();

        if (interactModule == null)
        {
            Debug.LogWarning("[PickupAnimEventRelay] interactModule is null");
            return;
        }

        interactModule.AnimEvent_AttachHeldItem();
    }

    public void AnimEvent_AttackHit()
    {
        if (combatModule == null)
            ResolveRefs();

        if (combatModule == null)
        {
            Debug.LogWarning("[PickupAnimEventRelay] combatModule is null");
            return;
        }

        combatModule.DoAttackServer();
    }

    public void AnimEvent_StandUpFinished()
    {
        ForwardStandUpFinished();
    }

    public void AnimEvent_StandUpBackFinished()
    {
        ForwardStandUpFinished();
    }

    public void NewEvent()
    {
        Debug.LogWarning("[PickupAnimEventRelay] Received legacy animation event 'NewEvent'. Rename the clip event to AnimEvent_StandUpBackFinished and Apply the import settings.");
        ForwardStandUpFinished();
    }

    private void ForwardStandUpFinished()
    {
        if (statusModule == null)
            ResolveRefs();

        if (statusModule == null)
        {
            Debug.LogWarning("[PickupAnimEventRelay] statusModule is null");
            return;
        }

        statusModule.AnimEvent_StandUpFinished();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}
