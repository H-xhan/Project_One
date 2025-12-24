using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class ThirdPersonCameraFollow : MonoBehaviour
{
    [Tooltip("따라갈 대상(플레이어 루트)")]
    public Transform target;

    [Tooltip("피치 회전용 피벗(이 오브젝트)")]
    public Transform pivot;

    [Tooltip("카메라 트랜스폼(피벗의 자식)")]
    public Transform cam;

    [Tooltip("카메라 오프셋(피벗 기준)")]
    public Vector3 cameraLocalOffset = new Vector3(0f, 0f, -3.5f);

    [Tooltip("피치 제한(위)")]
    public float pitchMin = -30f;

    [Tooltip("피치 제한(아래)")]
    public float pitchMax = 70f;

    [Tooltip("감도(Yaw)")]
    public float yawSensitivity = 180f;

    [Tooltip("감도(Pitch)")]
    public float pitchSensitivity = 120f;

    [Tooltip("마우스 델타 -> 축 스케일")]
    public float mouseDeltaToAxis = 0.1f;

    private float _yaw;
    private float _pitch;

    private NetworkObject _netObj;
    private PlayerHub _hub;

    private void Awake()
    {
        _netObj = GetComponentInParent<NetworkObject>();
        _hub = GetComponentInParent<PlayerHub>();
    }

    private void OnEnable()
    {
        if (cam != null)
            cam.localPosition = cameraLocalOffset;
    }

    private void LateUpdate()
    {
        if (_netObj != null && !_netObj.IsOwner)
            return;

        if (_hub != null && !_hub.IsCursorLocked)
            return;

        if (target == null || pivot == null || cam == null)
            return;

        Vector2 delta = Vector2.zero;
        if (Mouse.current != null)
            delta = Mouse.current.delta.ReadValue() * mouseDeltaToAxis;

        _yaw += delta.x * yawSensitivity * Time.deltaTime;
        _pitch -= delta.y * pitchSensitivity * Time.deltaTime;
        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

        target.rotation = Quaternion.Euler(0f, _yaw, 0f);
        pivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        cam.localPosition = cameraLocalOffset;
        cam.localRotation = Quaternion.identity;
    }
}
