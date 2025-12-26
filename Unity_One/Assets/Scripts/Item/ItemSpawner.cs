using System.Collections.Generic; // [필수] 리스트 사용을 위해 필요
using Unity.Netcode;
using UnityEngine;

public class ItemSpawner : NetworkBehaviour
{
    // 인스펙터에서 아이템별로 설정을 잡기 위한 껍데기(클래스)
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("알아보기 쉬운 이름 (예: 시작 지점 망치)")]
        public string name = "Item";

        [Tooltip("소환할 아이템 프리팹 (반드시 NetworkObject가 있어야 함)")]
        public GameObject prefab;

        [Tooltip("소환될 위치")]
        public Vector3 position;

        [Tooltip("소환될 회전값")]
        public Vector3 rotation;
    }

    [Header("Spawn List")]
    [Tooltip("여기에서 + 버튼을 눌러 아이템을 추가하세요")]
    [SerializeField] private List<SpawnEntry> itemsToSpawn = new List<SpawnEntry>();

    public override void OnNetworkSpawn()
    {
        // 서버만 물건을 배달(소환)할 수 있습니다.
        if (!IsServer) return;

        foreach (var entry in itemsToSpawn)
        {
            if (entry.prefab == null) continue;

            SpawnItem(entry);
        }
    }

    private void SpawnItem(SpawnEntry entry)
    {
        // 1. 회전값 변환 (Vector3 -> Quaternion)
        Quaternion rot = Quaternion.Euler(entry.rotation);

        // 2. 아이템 생성 (Instantiate)
        GameObject newItem = Instantiate(entry.prefab, entry.position, rot);

        // 3. 네트워크 등록 (Spawn) - 이게 제일 중요!
        NetworkObject netObj = newItem.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            Debug.Log($"[ItemSpawner] {entry.name} 소환 완료! 위치: {entry.position}");
        }
        else
        {
            Debug.LogError($"[ItemSpawner] {entry.name} 프리팹에 NetworkObject 컴포넌트가 없습니다!");
        }
    }

    // [보너스] 씬 뷰에서 소환될 위치를 미리 보여주는 기능
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        foreach (var entry in itemsToSpawn)
        {
            // 아이템이 생길 위치에 동그라미 그리기
            Gizmos.DrawWireSphere(entry.position, 0.5f);
            Gizmos.DrawRay(entry.position, Vector3.up * 1f); // 기둥 표시
        }
    }
}