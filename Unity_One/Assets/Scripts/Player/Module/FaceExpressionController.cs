using System.Collections;
using UnityEngine;

public class FaceExpressionController : MonoBehaviour
{
    [Header("Renderer")]
    [Tooltip("얼굴이 적용된 Renderer (지금은 평면의 SkinnedMeshRenderer)")]
    [SerializeField] private Renderer faceRenderer;

    [Tooltip("Renderer Materials 중 얼굴 머티리얼 인덱스 (Element 0이면 0)")]
    [SerializeField] private int materialIndex = 0;

    [Header("Atlas")]
    [Tooltip("표정 가로 칸 수 (f1.png는 6)")]
    [SerializeField] private int columns = 6;

    [Tooltip("이 모델은 UV가 이미 1칸으로 잡힌 타입이면 OFF")]
    [SerializeField] private bool uvUsesFullAtlas = false;

    [Tooltip("기본 표정 인덱스")]
    [SerializeField] private int defaultIndex = 0;

    [Header("Tuning")]
    [Tooltip("표정별 미세 오프셋(칸 내부 보정). 크기 예: X는 -0.03~+0.03 사이부터")]
    [SerializeField] private Vector2[] perFaceFineOffset = new Vector2[6];

    [Tooltip("전체 표정에 공통으로 적용되는 미세 오프셋(전체가 한쪽으로 밀릴 때)")]
    [SerializeField] private Vector2 globalFineOffset = Vector2.zero;

    [Header("Hold")]
    [Tooltip("Hold 기능으로 표정을 유지할 기본 시간(초). 예: 3.5")]
    [SerializeField] private float holdSeconds = 3.5f;

    private Material _mat;

    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    private Coroutine _holdRoutine;
    private int _holdToken;
    private int _currentIndex;

    private void Awake()
    {
        CacheMaterial();
        ApplyFaceIndex(defaultIndex);
    }

    private void OnEnable()
    {
        CacheMaterial();
    }

    private void OnValidate()
    {
        // 플레이 중 튜닝 값(오프셋 등)을 바꾸면 바로 반영되게
        if (!Application.isPlaying) return;
        ApplyFaceIndex(_currentIndex);
    }

    private void CacheMaterial()
    {
        _mat = null;
        if (faceRenderer == null) return;

        // Play 모드에서는 materials(인스턴스), Edit/프리팹에서는 sharedMaterials(안전)
        Material[] mats = Application.isPlaying ? faceRenderer.materials : faceRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0) return;

        if (materialIndex < 0 || materialIndex >= mats.Length)
            materialIndex = 0;

        _mat = mats[materialIndex];
    }

    private void CancelHold()
    {
        _holdToken++;
        if (_holdRoutine != null)
        {
            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }

    private void ApplyFaceIndex(int index)
    {
        if (faceRenderer == null || columns <= 0) return;
        if (_mat == null) CacheMaterial();
        if (_mat == null) return;

        _currentIndex = Mathf.Clamp(index, 0, columns - 1);

        float scaleX = uvUsesFullAtlas ? 1f / columns : 1f;
        float baseOffsetX = (float)_currentIndex / columns;

        Vector2 fine = Vector2.zero;
        if (perFaceFineOffset != null && _currentIndex < perFaceFineOffset.Length)
            fine = perFaceFineOffset[_currentIndex];

        Vector2 finalOffset = new Vector2(baseOffsetX, 0f) + fine + globalFineOffset;

        if (_mat.HasProperty(BaseMap))
        {
            _mat.SetTextureScale(BaseMap, new Vector2(scaleX, 1f));
            _mat.SetTextureOffset(BaseMap, finalOffset);
        }

        if (_mat.HasProperty(MainTex))
        {
            _mat.SetTextureScale(MainTex, new Vector2(scaleX, 1f));
            _mat.SetTextureOffset(MainTex, finalOffset);
        }
    }

    public void SetFaceIndex(int index)
    {
        CancelHold();
        ApplyFaceIndex(index);
    }

    // 애니 이벤트에서 "표정 바꾸기" (기존 그대로)
    public void Face_Default() => SetFaceIndex(defaultIndex);
    public void Face_0() => SetFaceIndex(0);
    public void Face_1() => SetFaceIndex(1);
    public void Face_2() => SetFaceIndex(2);
    public void Face_3() => SetFaceIndex(3);
    public void Face_4() => SetFaceIndex(4);
    public void Face_5() => SetFaceIndex(5);

    // 핵심: 특정 표정을 holdSeconds 동안 유지 후 기본 표정으로 복귀
    public void Face_HoldIndex(int index)
    {
        CancelHold();
        ApplyFaceIndex(index);

        int token = ++_holdToken;
        float sec = Mathf.Max(0f, holdSeconds);
        _holdRoutine = StartCoroutine(HoldRoutine(token, sec));
    }

    // 핵심: Face_4를 원하는 시간(초) 만큼 유지 후 기본 표정으로 복귀 (이벤트에서 Float로 입력 가능)
    public void Face_4_HoldSeconds(float seconds)
    {
        CancelHold();
        ApplyFaceIndex(4);

        int token = ++_holdToken;
        float sec = Mathf.Max(0f, seconds);
        _holdRoutine = StartCoroutine(HoldRoutine(token, sec));
    }

    private IEnumerator HoldRoutine(int token, float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (token != _holdToken)
            yield break;

        ApplyFaceIndex(defaultIndex);
        _holdRoutine = null;
    }

    // 테스트 메뉴 (기존 그대로)
    [ContextMenu("Face/Test 0")] private void Test0() => SetFaceIndex(0);
    [ContextMenu("Face/Test 1")] private void Test1() => SetFaceIndex(1);
    [ContextMenu("Face/Test 2")] private void Test2() => SetFaceIndex(2);
    [ContextMenu("Face/Test 3")] private void Test3() => SetFaceIndex(3);
    [ContextMenu("Face/Test 4")] private void Test4() => SetFaceIndex(4);
    [ContextMenu("Face/Test 5")] private void Test5() => SetFaceIndex(5);

    [ContextMenu("Face/Refresh")] private void Refresh() => ApplyFaceIndex(_currentIndex);
}