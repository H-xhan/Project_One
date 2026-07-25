using System;
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

    [Header("Debug")]
    [Tooltip("디버그 로그 출력 여부입니다.")]
    [SerializeField] private bool enableDebugLogs = false;

    private Material _mat;

    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    private Coroutine _holdRoutine;
    private int _holdToken;
    private int _currentIndex = -1;
    private bool _sourceUvBoundsResolved;
    private bool _hasSourceUvBounds;
    private Rect _sourceUvBounds;

    public int CurrentExpressionId => _currentIndex;

    public event Action<int> ExpressionChanged;

    public Texture ExpressionAtlasTexture
    {
        get
        {
            if (_mat == null)
                return null;

            Texture texture = _mat.HasProperty(BaseMap)
                ? _mat.GetTexture(BaseMap)
                : null;

            if (texture == null && _mat.HasProperty(MainTex))
                texture = _mat.GetTexture(MainTex);

            return texture;
        }
    }

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
        ApplyFaceIndex(_currentIndex >= 0 ? _currentIndex : defaultIndex);
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
        if (faceRenderer == null)
            return;

        // 핵심: 적용할 때마다 현재 live material 다시 캐시
        CacheMaterial(true);

        if (_mat == null)
        {
            Debug.LogWarning("[FaceExpressionController] Face material not found.");
            return;
        }

        if (!TryResolveExpressionMaterialUv(index, out int resolvedIndex, out Vector2 textureScale, out Vector2 finalOffset))
            return;

        bool applied = false;

        if (_mat.HasProperty(BaseMap))
        {
            _mat.SetTextureScale(BaseMap, textureScale);
            _mat.SetTextureOffset(BaseMap, finalOffset);
            applied = true;
        }

        if (_mat.HasProperty(MainTex))
        {
            _mat.SetTextureScale(MainTex, textureScale);
            _mat.SetTextureOffset(MainTex, finalOffset);
            applied = true;
        }

        if (!applied)
        {
            Debug.LogWarning($"[FaceExpressionController] Material '{_mat.name}' has neither _BaseMap nor _MainTex.");
            return;
        }

        int previousIndex = _currentIndex;
        _currentIndex = resolvedIndex;

        Log($"[FaceExpressionController] Face index={_currentIndex}, offset={finalOffset}, scaleX={textureScale.x}, mat={_mat.name}");

        if (previousIndex != _currentIndex)
            ExpressionChanged?.Invoke(_currentIndex);
    }

    public bool TryGetExpressionUvRect(int expressionId, out Rect uvRect)
    {
        uvRect = default;

        if (!TryResolveExpressionMaterialUv(
                expressionId,
                out _,
                out Vector2 textureScale,
                out Vector2 textureOffset) ||
            !TryGetSourceUvBounds(out Rect sourceUvBounds))
        {
            return false;
        }

        uvRect = new Rect(
            textureOffset.x + sourceUvBounds.xMin * textureScale.x,
            textureOffset.y + sourceUvBounds.yMin * textureScale.y,
            sourceUvBounds.width * textureScale.x,
            sourceUvBounds.height * textureScale.y);

        return IsFiniteNormalizedRect(uvRect);
    }

    private bool TryResolveExpressionMaterialUv(
        int index,
        out int resolvedIndex,
        out Vector2 textureScale,
        out Vector2 textureOffset)
    {
        resolvedIndex = 0;
        textureScale = Vector2.one;
        textureOffset = Vector2.zero;

        if (columns <= 0)
            return false;

        resolvedIndex = Mathf.Clamp(index, 0, columns - 1);
        textureScale.x = uvUsesFullAtlas ? 1f / columns : 1f;

        Vector2 fine = Vector2.zero;
        if (perFaceFineOffset != null && resolvedIndex < perFaceFineOffset.Length)
            fine = perFaceFineOffset[resolvedIndex];

        textureOffset = new Vector2((float)resolvedIndex / columns, 0f) +
                        fine +
                        globalFineOffset;
        return true;
    }

    private bool TryGetSourceUvBounds(out Rect uvBounds)
    {
        if (!_sourceUvBoundsResolved)
        {
            _sourceUvBoundsResolved = true;
            _hasSourceUvBounds = TryResolveSourceUvBounds(out _sourceUvBounds);
        }

        uvBounds = _sourceUvBounds;
        return _hasSourceUvBounds;
    }

    private bool TryResolveSourceUvBounds(out Rect uvBounds)
    {
        uvBounds = default;

        Mesh mesh = null;
        if (faceRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            mesh = skinnedMeshRenderer.sharedMesh;
        }
        else if (faceRenderer != null)
        {
            MeshFilter meshFilter = faceRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null)
                mesh = meshFilter.sharedMesh;
        }

        if (mesh == null)
            return false;

        Mesh readableMesh = mesh;
        Mesh bakedMesh = null;

        try
        {
            if (!mesh.isReadable)
            {
                if (!(faceRenderer is SkinnedMeshRenderer
                      unreadableSkinnedMeshRenderer))
                {
                    return false;
                }

                bakedMesh = new Mesh();
                unreadableSkinnedMeshRenderer.BakeMesh(bakedMesh);
                readableMesh = bakedMesh;
            }

            Vector2[] uvs = readableMesh.uv;
            if (uvs == null || uvs.Length == 0)
                return false;

            if (materialIndex < 0 ||
                materialIndex >= readableMesh.subMeshCount)
            {
                return false;
            }

            int[] triangles = readableMesh.GetTriangles(materialIndex);
            if (triangles == null || triangles.Length == 0)
                return false;

            int firstVertexIndex = triangles[0];
            if (firstVertexIndex < 0 || firstVertexIndex >= uvs.Length)
                return false;

            Vector2 min = uvs[firstVertexIndex];
            Vector2 max = uvs[firstVertexIndex];

            for (int i = 1; i < triangles.Length; i++)
            {
                int vertexIndex = triangles[i];
                if (vertexIndex < 0 || vertexIndex >= uvs.Length)
                    return false;

                min = Vector2.Min(min, uvs[vertexIndex]);
                max = Vector2.Max(max, uvs[vertexIndex]);
            }

            uvBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return IsFiniteNormalizedRect(uvBounds);
        }
        finally
        {
            if (bakedMesh != null)
                Destroy(bakedMesh);
        }
    }

    private static bool IsFiniteNormalizedRect(Rect rect)
    {
        return IsFinite(rect.xMin) &&
               IsFinite(rect.yMin) &&
               IsFinite(rect.xMax) &&
               IsFinite(rect.yMax) &&
               rect.width > 0f &&
               rect.height > 0f &&
               rect.xMin >= 0f &&
               rect.yMin >= 0f &&
               rect.xMax <= 1f &&
               rect.yMax <= 1f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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

    private void Log(string message)
    {
        if (!enableDebugLogs)
            return;

        Debug.Log(message, this);
    }

    [ContextMenu("Face/Test 0")] private void Test0() => SetFaceIndex(0);
    [ContextMenu("Face/Test 1")] private void Test1() => SetFaceIndex(1);
    [ContextMenu("Face/Test 2")] private void Test2() => SetFaceIndex(2);
    [ContextMenu("Face/Test 3")] private void Test3() => SetFaceIndex(3);
    [ContextMenu("Face/Test 4")] private void Test4() => SetFaceIndex(4);
    [ContextMenu("Face/Test 5")] private void Test5() => SetFaceIndex(5);

    [ContextMenu("Face/Refresh")] private void Refresh() => ApplyFaceIndex(_currentIndex);
}
