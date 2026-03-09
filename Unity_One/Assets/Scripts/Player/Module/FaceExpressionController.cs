using System.Collections;
using UnityEngine;

public class FaceExpressionController : MonoBehaviour
{
    [Header("Renderer")]
    [Tooltip("얼굴이 적용된 Renderer")]
    [SerializeField] private Renderer faceRenderer;

    [Tooltip("Renderer Materials 중 얼굴 머티리얼 인덱스")]
    [SerializeField] private int materialIndex = 0;

    [Header("Atlas")]
    [Tooltip("표정 가로 칸 수")]
    [SerializeField] private int columns = 6;

    [Tooltip("이 모델이 아틀라스 전체 UV를 쓰면 ON, 이미 1칸 UV면 OFF")]
    [SerializeField] private bool uvUsesFullAtlas = false;

    [Tooltip("기본 표정 인덱스")]
    [SerializeField] private int defaultIndex = 0;

    [Header("Tuning")]
    [Tooltip("표정별 미세 오프셋")]
    [SerializeField] private Vector2[] perFaceFineOffset = new Vector2[6];

    [Tooltip("전체 표정 공통 미세 오프셋")]
    [SerializeField] private Vector2 globalFineOffset = Vector2.zero;

    [Header("Hold")]
    [Tooltip("Hold 기능 기본 유지 시간")]
    [SerializeField] private float holdSeconds = 3.5f;

    private Material _mat;

    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    private Coroutine _holdRoutine;
    private int _holdToken;
    private int _currentIndex;

    private void Awake()
    {
        CacheMaterial(true);
        ApplyFaceIndex(defaultIndex);
    }

    private void OnEnable()
    {
        CacheMaterial(true);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyFaceIndex(_currentIndex);
    }

    // 현재 Renderer의 live material을 다시 잡는다
    private void CacheMaterial(bool forceRefresh = false)
    {
        if (!forceRefresh && _mat != null)
            return;

        _mat = null;
        if (faceRenderer == null)
            return;

        Material[] mats = Application.isPlaying
            ? faceRenderer.materials
            : faceRenderer.sharedMaterials;

        if (mats == null || mats.Length == 0)
            return;

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
        if (faceRenderer == null || columns <= 0)
            return;

        // 핵심: 적용할 때마다 현재 live material 다시 캐시
        CacheMaterial(true);

        if (_mat == null)
        {
            Debug.LogWarning("[FaceExpressionController] Face material not found.");
            return;
        }

        _currentIndex = Mathf.Clamp(index, 0, columns - 1);

        float scaleX = uvUsesFullAtlas ? 1f / columns : 1f;
        float baseOffsetX = (float)_currentIndex / columns;

        Vector2 fine = Vector2.zero;
        if (perFaceFineOffset != null && _currentIndex < perFaceFineOffset.Length)
            fine = perFaceFineOffset[_currentIndex];

        Vector2 finalOffset = new Vector2(baseOffsetX, 0f) + fine + globalFineOffset;

        bool applied = false;

        if (_mat.HasProperty(BaseMap))
        {
            _mat.SetTextureScale(BaseMap, new Vector2(scaleX, 1f));
            _mat.SetTextureOffset(BaseMap, finalOffset);
            applied = true;
        }

        if (_mat.HasProperty(MainTex))
        {
            _mat.SetTextureScale(MainTex, new Vector2(scaleX, 1f));
            _mat.SetTextureOffset(MainTex, finalOffset);
            applied = true;
        }

        if (!applied)
        {
            Debug.LogWarning($"[FaceExpressionController] Material '{_mat.name}' has neither _BaseMap nor _MainTex.");
            return;
        }

        Debug.Log($"[FaceExpressionController] Face index={_currentIndex}, offset={finalOffset}, scaleX={scaleX}, mat={_mat.name}");
    }

    public void SetFaceIndex(int index)
    {
        CancelHold();
        ApplyFaceIndex(index);
    }

    public void Face_Default() => SetFaceIndex(defaultIndex);
    public void Face_0() => SetFaceIndex(0);
    public void Face_1() => SetFaceIndex(1);
    public void Face_2() => SetFaceIndex(2);
    public void Face_3() => SetFaceIndex(3);
    public void Face_4() => SetFaceIndex(4);
    public void Face_5() => SetFaceIndex(5);

    public void Face_HoldIndex(int index)
    {
        CancelHold();
        ApplyFaceIndex(index);

        int token = ++_holdToken;
        float sec = Mathf.Max(0f, holdSeconds);
        _holdRoutine = StartCoroutine(HoldRoutine(token, sec));
    }

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

    [ContextMenu("Face/Test 0")] private void Test0() => SetFaceIndex(0);
    [ContextMenu("Face/Test 1")] private void Test1() => SetFaceIndex(1);
    [ContextMenu("Face/Test 2")] private void Test2() => SetFaceIndex(2);
    [ContextMenu("Face/Test 3")] private void Test3() => SetFaceIndex(3);
    [ContextMenu("Face/Test 4")] private void Test4() => SetFaceIndex(4);
    [ContextMenu("Face/Test 5")] private void Test5() => SetFaceIndex(5);

    [ContextMenu("Face/Refresh")] private void Refresh() => ApplyFaceIndex(_currentIndex);
}