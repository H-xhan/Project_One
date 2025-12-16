using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkController : NetworkBehaviour
{
    [Tooltip("이동 속도")]
    [SerializeField] private float moveSpeed = 4.5f;

    private void Update()
    {
        if (!IsOwner) return;

        Vector2 move = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) move.y += 1f;
            if (Keyboard.current.sKey.isPressed) move.y -= 1f;
            if (Keyboard.current.dKey.isPressed) move.x += 1f;
            if (Keyboard.current.aKey.isPressed) move.x -= 1f;
        }

        Vector3 dir = new Vector3(move.x, 0f, move.y).normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;
    }
}
