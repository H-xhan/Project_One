using Unity.Netcode;
using UnityEngine;

public class GameSceneManager : NetworkBehaviour
{
    [Header("소환할 캐릭터 프리팹")]
    [SerializeField] private GameObject characterPrefab; // 에디터에서 드래그해서 넣을 변수

    private void Start()
    {
        // 씬이 켜지면, 호스트(서버)가 판단해서 캐릭터를 소환함
        if (IsServer)
        {
            SpawnAllPlayers();
        }
    }

    private void SpawnAllPlayers()
    {
        // 현재 접속해 있는 모든 사람(호스트+참가자)의 ID를 하나씩 꺼냄
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // 1. 내가 지정한(characterPrefab) 수달을 생성
            GameObject playerInstance = Instantiate(characterPrefab);

            // 2. 위치 설정 (일단 (0,2,0) 공중에)
            playerInstance.transform.position = new Vector3(0, 2, 0);

            // 3. 네트워크 소환! (이 캐릭터의 주인은 clientId야!)
            playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }
    }
}