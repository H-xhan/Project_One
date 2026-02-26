using UnityEngine;

public class PickupAnimEventRelay : MonoBehaviour
{
    [Tooltip("부모에서 PlayerInteractModule을 자동 탐색합니다. 수동 지정이 필요하면 여기서 연결하세요.")]
    [SerializeField] private PlayerInteractModule interactModule;

    private void Awake()
    {
        if (interactModule == null)
            interactModule = GetComponentInParent<PlayerInteractModule>();
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
}
