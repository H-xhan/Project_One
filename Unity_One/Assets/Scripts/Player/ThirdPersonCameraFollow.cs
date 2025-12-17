using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCameraFollow : MonoBehaviour
{
    [Tooltip("따라갈 대상(비우면 로컬 오너 플레이어 자동 탐색)")]
    public Transform target;

    [Tooltip("거리")]
    public float distance = 4f;

    [Tooltip("높이")]
    public float height = 1.6f;

    [Tooltip("어깨 오프셋(+면 오른쪽)")]
    public float shoulder = 0.3f;

    [Tooltip("피치 감도(마우스 Y)")]
    public float pitchSensitivity = 5f;

    [Tooltip("피치 제한")]
    public float minPitch = -20f;

    [Tooltip("피치 제한")]
    public float maxPitch = 30f;

    [Tooltip("카메라 보간 속도")]
    public float followSmooth = 12f;

    private float _pitch;

    private void LateUpdate()
    {
        if (target == null)
        {
            TryBindLocalPlayer();
            if (target == null) return;
        }

        UpdatePitch();

        Vector3 focus = target.position + Vector3.up * height;

        Quaternion yawRot = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Quaternion pitchRot = Quaternion.Euler(_pitch, 0f, 0f);

        Vector3 offset =
            yawRot * pitchRot * (Vector3.back * distance) +
            yawRot * (Vector3.right * shoulder);

        Vector3 desiredPos = focus + offset;

        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-followSmooth * Time.deltaTime));
        transform.LookAt(focus);
    }

    private void UpdatePitch()
    {
        if (Mouse.current == null) return;

        float mouseY = Mouse.current.delta.ReadValue().y;
        _pitch -= mouseY * pitchSensitivity * Time.deltaTime;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
    }

    private void TryBindLocalPlayer()
    {
        var players = Object.FindObjectsByType<PlayerNetworkController>(FindObjectsSortMode.None);

        foreach (var p in players)
        {
            if (p != null && p.IsOwner)
            {
                target = p.transform;
                return;
            }
        }
    }

}
