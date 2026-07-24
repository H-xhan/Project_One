using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialCheckpoint : MonoBehaviour
{
    [SerializeField] private TutorialDirector director;
    [SerializeField] private Collider triggerCollider;

    private bool _consumed;

    public bool IsConsumed => _consumed;

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();
    }

    private void OnValidate()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();
    }

    public void ResetCheckpoint()
    {
        _consumed = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed ||
            director == null ||
            triggerCollider == null ||
            !triggerCollider.isTrigger ||
            other == null)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        NetworkClient localClient = networkManager != null
            ? networkManager.LocalClient
            : null;
        NetworkObject localPlayerObject = localClient != null
            ? localClient.PlayerObject
            : null;
        NetworkObject enteredObject = other.GetComponentInParent<NetworkObject>();

        if (networkManager == null ||
            !networkManager.IsListening ||
            localPlayerObject == null ||
            !localPlayerObject.IsSpawned ||
            !localPlayerObject.IsPlayerObject ||
            !localPlayerObject.IsOwner ||
            enteredObject == null ||
            enteredObject != localPlayerObject)
        {
            return;
        }

        _consumed = true;
        director.NotifyMoveCheckpointEntered(this, enteredObject);
    }
}
