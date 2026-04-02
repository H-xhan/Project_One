using UnityEngine;

public class PickupAnimEventRelay : MonoBehaviour
{
    [Tooltip("부모/자식에서 PlayerInteractModule을 자동 탐색합니다. 수동 지정이 필요하면 여기서 연결하세요.")]
    [SerializeField] private PlayerInteractModule interactModule;

    [Tooltip("부모/자식에서 PlayerStatusModule을 자동 탐색합니다. 기상 애니메이션 이벤트에 사용합니다.")]
    [SerializeField] private PlayerStatusModule statusModule;

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
    }

    // Animation Event에서 호출할 함수 이름(클립 이벤트 Function에 이 이름을 넣기)
    public void AnimEvent_AttachHeldItem()
    {
        Debug.Log("[PickupAnimEventRelay] AnimEvent fired");

        if (interactModule == null)
        {
            Debug.LogWarning("[PickupAnimEventRelay] interactModule is null");
            return;
        }

        interactModule.AnimEvent_AttachHeldItem();
    }

    // Back Stand Up 클립 마지막 프레임 이벤트에서 호출
    public void AnimEvent_StandUpBackFinished()
    {
        if (statusModule == null)
        {
            Debug.LogWarning("[PickupAnimEventRelay] statusModule is null");
            return;
        }

        statusModule.AnimEvent_StandUpBackFinished();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveRefs();
    }
#endif
}
