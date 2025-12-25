using Unity.Netcode;
using UnityEngine;

public class ItemSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject hammerPrefab;

    public override void OnNetworkSpawn()
    {
        // 서버만 아이템을 소환할 수 있음
        if (IsServer)
        {
            // 위치는 대충 (0, 1, 3) 앞에 소환
            GameObject hammer = Instantiate(hammerPrefab, new Vector3(0, 1, -3), Quaternion.identity);

            // [중요] 네트워크에 소환 등록 (이러면 플레이어랑 같은 등급이 됨)
            hammer.GetComponent<NetworkObject>().Spawn();
        }
    }
}