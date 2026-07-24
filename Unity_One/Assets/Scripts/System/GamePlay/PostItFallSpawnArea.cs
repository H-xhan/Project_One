using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class PostItFallSpawnArea : MonoBehaviour
{
    [SerializeField] private int spawnOrder;
    [SerializeField] private float edgePadding = 0.75f;

    public int SpawnOrder => spawnOrder;
    public BoxCollider SourceVolume => GetComponent<BoxCollider>();
    public float EffectiveEdgePadding =>
        IsFinite(edgePadding) ? Mathf.Max(0f, edgePadding) : 0f;
    public Scene SourceScene => gameObject.scene;

    public bool IsUsable
    {
        get
        {
            if (this == null ||
                gameObject == null ||
                transform == null ||
                !isActiveAndEnabled ||
                !gameObject.activeInHierarchy)
            {
                return false;
            }

            Scene sourceScene = SourceScene;
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
                return false;

            BoxCollider sourceVolume = SourceVolume;
            if (sourceVolume == null || !sourceVolume.enabled)
                return false;

            Vector3 center = sourceVolume.center;
            Vector3 size = sourceVolume.size;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 lossyScale = transform.lossyScale;
            return IsFinite(center) &&
                   IsFinite(size) &&
                   size.x > 0f &&
                   size.y > 0f &&
                   size.z > 0f &&
                   IsFinite(position) &&
                   IsFinite(rotation) &&
                   IsFinite(lossyScale) &&
                   Mathf.Abs(lossyScale.x) > 0.0001f &&
                   Mathf.Abs(lossyScale.y) > 0.0001f &&
                   Mathf.Abs(lossyScale.z) > 0.0001f;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z) &&
               IsFinite(value.w);
    }
}
