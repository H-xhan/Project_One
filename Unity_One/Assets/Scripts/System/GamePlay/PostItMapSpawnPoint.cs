using UnityEngine;

[DisallowMultipleComponent]
public sealed class PostItMapSpawnPoint : MonoBehaviour
{
    [SerializeField] private int spawnOrder;

    public int SpawnOrder => spawnOrder;
}
