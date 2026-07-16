using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PostItInventoryHUD : MonoBehaviour
{
    [SerializeField, Tooltip("보유 포스트잇 수를 표시할 TMP 텍스트입니다.")]
    private TMP_Text countText;

    [SerializeField, Tooltip("포스트잇 슬롯 요약을 여러 줄로 표시할 TMP 텍스트입니다.")]
    private TMP_Text slotsText;

    [SerializeField, Tooltip("표시할 포스트잇 인벤토리입니다. 비워두면 로컬 Owner 플레이어를 자동 탐색합니다.")]
    private PlayerPostItInventory targetInventory;

    [SerializeField, Tooltip("로컬 Owner 인벤토리를 찾지 못했을 때 다시 탐색하는 간격입니다.")]
    private float rebindInterval = 0.25f;

    [SerializeField, Tooltip("인벤토리를 찾지 못했을 때 연결된 UI 요소를 숨길지 여부입니다.")]
    private bool hideWhenInventoryMissing = false;

    [SerializeField, Tooltip("인벤토리를 찾지 못했을 때 수량 영역에 표시할 문구입니다.")]
    private string missingInventoryText = "포스트잇 -";

    [SerializeField, Tooltip("포스트잇 수량 앞에 표시할 접두어입니다.")]
    private string countPrefix = "포스트잇 ";

    [SerializeField, Tooltip("비어 있는 슬롯에 표시할 문구입니다.")]
    private string emptySlotText = "빈칸";

    [SerializeField, Tooltip("원래 소유자와 현재 보유자의 일치 여부를 슬롯 요약에 표시합니다.")]
    private bool showOwnershipMarker = true;

    [SerializeField, Tooltip("원래 소유자와 현재 보유자가 같을 때 표시할 문구입니다.")]
    private string originalOwnershipText = "내 것";

    [SerializeField, Tooltip("원래 소유자와 현재 보유자가 다를 때 표시할 문구입니다.")]
    private string stolenOwnershipText = "획득";

    private readonly StringBuilder _slotBuilder = new StringBuilder(256);
    private PlayerPostItInventory _boundInventory;
    private float _nextBindAttemptTime;

    public PlayerPostItInventory BoundInventory => _boundInventory;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        TryBindInventory();
    }

    private void OnDisable()
    {
        UnbindInventory();
    }

    private void Update()
    {
        if (_boundInventory != null)
        {
            return;
        }

        if (Time.unscaledTime < _nextBindAttemptTime)
        {
            return;
        }

        TryBindInventory();
    }

    public void SetTargetInventory(PlayerPostItInventory inventory)
    {
        targetInventory = inventory;
        ForceRebind();
    }

    public void ForceRebind()
    {
        UnbindInventory();
        _nextBindAttemptTime = 0f;
        TryBindInventory();
    }

    public void ForceRefresh()
    {
        RefreshInventoryUI();
    }

    private void ResolveReferences()
    {
        if (countText != null && countText == slotsText)
        {
            slotsText = null;
        }

        if (countText != null)
        {
            return;
        }

        TMP_Text[] textComponents = GetComponentsInChildren<TMP_Text>(true);
        if (textComponents.Length == 1 && textComponents[0] != slotsText)
        {
            countText = textComponents[0];
        }
    }

    private void TryBindInventory()
    {
        _nextBindAttemptTime = Time.unscaledTime + Mathf.Max(0f, rebindInterval);

        PlayerPostItInventory inventory = ResolveTargetInventory();
        if (inventory == null)
        {
            ShowMissingInventoryState();
            return;
        }

        BindInventory(inventory);
    }

    private PlayerPostItInventory ResolveTargetInventory()
    {
        if (targetInventory != null)
        {
            return CanBindInventory(targetInventory) ? targetInventory : null;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return null;
        }

        NetworkClient localClient = networkManager.LocalClient;
        if (localClient == null || localClient.PlayerObject == null)
        {
            return null;
        }

        PlayerPostItInventory inventory =
            localClient.PlayerObject.GetComponentInChildren<PlayerPostItInventory>(true);
        return CanBindInventory(inventory) ? inventory : null;
    }

    private bool CanBindInventory(PlayerPostItInventory inventory)
    {
        if (inventory == null)
        {
            return false;
        }

        if (inventory.IsSpawned && !inventory.IsOwner)
        {
            return false;
        }

        return true;
    }

    private void BindInventory(PlayerPostItInventory inventory)
    {
        if (_boundInventory == inventory)
        {
            RefreshInventoryUI();
            return;
        }

        UnbindInventory();

        _boundInventory = inventory;
        _boundInventory.PostItsChanged += OnPostItsChanged;
        RefreshInventoryUI();
    }

    private void UnbindInventory()
    {
        if (_boundInventory != null)
        {
            _boundInventory.PostItsChanged -= OnPostItsChanged;
        }

        _boundInventory = null;
    }

    private void OnPostItsChanged()
    {
        RefreshInventoryUI();
    }

    private void RefreshInventoryUI()
    {
        if (_boundInventory == null)
        {
            ShowMissingInventoryState();
            return;
        }

        SetUIElementsVisible(true);

        int capacity = _boundInventory.Capacity;
        if (countText != null)
        {
            countText.text = $"{countPrefix ?? string.Empty}{_boundInventory.Count} / {capacity}";
        }

        if (slotsText == null)
        {
            return;
        }

        _slotBuilder.Clear();
        for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
        {
            if (slotIndex > 0)
            {
                _slotBuilder.Append('\n');
            }

            _slotBuilder.Append('[').Append(slotIndex + 1).Append("] ");
            if (!_boundInventory.TryGetPostItAtSlot(slotIndex, out PostItRuntimeData data))
            {
                _slotBuilder.Append(emptySlotText ?? string.Empty);
                continue;
            }

            _slotBuilder
                .Append(GetTypeLabel(data.Type))
                .Append(" · ")
                .Append(GetTopicLabel(data.TopicId));

            if (showOwnershipMarker)
            {
                string ownershipText = data.OriginalOwnerClientId == data.HolderClientId
                    ? originalOwnershipText
                    : stolenOwnershipText;

                if (!string.IsNullOrEmpty(ownershipText))
                {
                    _slotBuilder.Append(" · ").Append(ownershipText);
                }
            }
        }

        slotsText.text = _slotBuilder.ToString();
    }

    private string GetTypeLabel(PostItType type)
    {
        switch (type)
        {
            case PostItType.None:
                return "없음";
            case PostItType.Drawing:
                return "그림";
            case PostItType.Message:
                return "메시지";
            case PostItType.Bonus:
                return "보너스";
            case PostItType.Penalty:
                return "패널티";
            default:
                return type.ToString();
        }
    }

    private string GetTopicLabel(PostItTopicId topicId)
    {
        switch (topicId)
        {
            case PostItTopicId.None:
                return "없음";
            case PostItTopicId.Animal:
                return "동물";
            case PostItTopicId.Food:
                return "음식";
            case PostItTopicId.Object:
                return "사물";
            case PostItTopicId.Emotion:
                return "감정";
            case PostItTopicId.Free:
                return "자유";
            default:
                return topicId.ToString();
        }
    }

    private void ShowMissingInventoryState()
    {
        SetUIElementsVisible(!hideWhenInventoryMissing);

        if (countText != null)
        {
            countText.text = missingInventoryText ?? string.Empty;
        }

        if (slotsText != null)
        {
            slotsText.text = string.Empty;
        }
    }

    private void SetUIElementsVisible(bool visible)
    {
        if (countText != null && countText.gameObject != gameObject)
        {
            countText.gameObject.SetActive(visible);
        }

        if (slotsText != null && slotsText.gameObject != gameObject)
        {
            slotsText.gameObject.SetActive(visible);
        }
    }

    private void OnValidate()
    {
        rebindInterval = Mathf.Max(0f, rebindInterval);
    }
}
