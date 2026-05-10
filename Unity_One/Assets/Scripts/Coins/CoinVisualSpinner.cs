using UnityEngine;

public class CoinVisualSpinner : MonoBehaviour
{
    [SerializeField, Tooltip("코인 비주얼이 초당 회전하는 속도입니다.")]
    private float spinSpeed = 120f;

    [SerializeField, Tooltip("월드 Y축 기준으로 회전할지 여부입니다. 세워진 코인 비주얼에는 켜두는 것을 권장합니다.")]
    private bool rotateAroundWorldUp = true;

    [SerializeField, Tooltip("생성 시 코인마다 초기 회전 각도를 다르게 줄지 여부입니다.")]
    private bool randomizeInitialAngle = true;

    [SerializeField, Tooltip("회전 방향을 반대로 돌릴지 여부입니다.")]
    private bool reverseDirection = false;

    private bool isSpinEnabled = true;

    public float SpinSpeed
    {
        get => spinSpeed;
        set => spinSpeed = Mathf.Abs(value);
    }

    public bool IsSpinEnabled => isSpinEnabled;

    public void SetSpinEnabled(bool enabled)
    {
        isSpinEnabled = enabled;
    }

    private void Awake()
    {
        spinSpeed = Mathf.Abs(spinSpeed);

        if (!randomizeInitialAngle)
        {
            return;
        }

        float initialAngle = Random.Range(0f, 360f);
        Rotate(initialAngle);
    }

    private void Update()
    {
        if (!isSpinEnabled || spinSpeed == 0f)
        {
            return;
        }

        float direction = reverseDirection ? -1f : 1f;
        float deltaAngle = spinSpeed * direction * Time.deltaTime;
        Rotate(deltaAngle);
    }

    private void OnValidate()
    {
        spinSpeed = Mathf.Abs(spinSpeed);
    }

    private void Rotate(float angle)
    {
        Space rotationSpace = rotateAroundWorldUp ? Space.World : Space.Self;
        transform.Rotate(Vector3.up, angle, rotationSpace);
    }
}
