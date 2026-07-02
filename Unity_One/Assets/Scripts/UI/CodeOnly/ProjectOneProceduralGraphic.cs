using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ProjectOneProceduralGraphic : MaskableGraphic
{
    [SerializeField] private Color fillColor = new Color(1f, 0.94f, 0.82f, 1f);
    [SerializeField] private Color borderColor = new Color(0.72f, 0.65f, 0.55f, 0.55f);
    [SerializeField] private float borderWidth = 2f;
    [SerializeField] private float cornerRadius = 24f;
    [SerializeField] private int cornerSegments = 8;

    [Header("Notebook Lines")]
    [SerializeField] private bool drawNotebookLines;
    [SerializeField] private Color notebookLineColor = new Color(0.72f, 0.65f, 0.55f, 0.24f);
    [SerializeField] private float lineSpacing = 38f;
    [SerializeField] private float lineThickness = 1.4f;
    [SerializeField] private Vector4 linePadding = new Vector4(32f, 58f, 32f, 38f); // left, top, right, bottom

    private readonly List<Vector2> outerPoints = new List<Vector2>(64);
    private readonly List<Vector2> innerPoints = new List<Vector2>(64);

    public void Configure(Color fill, Color border, float radius, float width, bool notebookLines)
    {
        fillColor = fill;
        borderColor = border;
        cornerRadius = Mathf.Max(0f, radius);
        borderWidth = Mathf.Max(0f, width);
        drawNotebookLines = notebookLines;
        SetVerticesDirty();
    }

    public void SetFill(Color fill)
    {
        fillColor = fill;
        SetVerticesDirty();
    }

    public void SetNotebookLines(bool enabled)
    {
        drawNotebookLines = enabled;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        float radius = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
        int segments = Mathf.Clamp(cornerSegments, 2, 18);

        BuildRoundedRectPoints(rect, radius, segments, outerPoints);
        AddFilledPolygon(vh, outerPoints, fillColor * color);

        if (drawNotebookLines)
            AddNotebookLines(vh, rect, radius);

        if (borderWidth > 0.01f && borderColor.a > 0.001f)
        {
            Rect innerRect = new Rect(rect.xMin + borderWidth, rect.yMin + borderWidth, rect.width - borderWidth * 2f, rect.height - borderWidth * 2f);
            if (innerRect.width > 1f && innerRect.height > 1f)
            {
                float innerRadius = Mathf.Max(0f, radius - borderWidth);
                BuildRoundedRectPoints(innerRect, innerRadius, segments, innerPoints);
                AddBorder(vh, outerPoints, innerPoints, borderColor * color);
            }
        }
    }

    private static void BuildRoundedRectPoints(Rect rect, float radius, int segments, List<Vector2> points)
    {
        points.Clear();

        if (radius <= 0.01f)
        {
            points.Add(new Vector2(rect.xMax, rect.yMax));
            points.Add(new Vector2(rect.xMin, rect.yMax));
            points.Add(new Vector2(rect.xMin, rect.yMin));
            points.Add(new Vector2(rect.xMax, rect.yMin));
            return;
        }

        AddArc(points, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f, segments);
        AddArc(points, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f, segments);
        AddArc(points, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f, segments);
        AddArc(points, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f, segments);
    }

    private static void AddArc(List<Vector2> points, Vector2 center, float radius, float fromDegrees, float toDegrees, int segments)
    {
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(fromDegrees, toDegrees, t) * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private static void AddFilledPolygon(VertexHelper vh, List<Vector2> points, Color32 color)
    {
        if (points.Count < 3)
            return;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            center += points[i];
        center /= points.Count;

        int centerIndex = vh.currentVertCount;
        vh.AddVert(center, color, new Vector2(0.5f, 0.5f));

        for (int i = 0; i < points.Count; i++)
            vh.AddVert(points[i], color, Vector2.zero);

        for (int i = 0; i < points.Count; i++)
        {
            int next = (i + 1) % points.Count;
            vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + next);
        }
    }

    private static void AddBorder(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color32 color)
    {
        int count = Mathf.Min(outer.Count, inner.Count);
        if (count < 3)
            return;

        int start = vh.currentVertCount;
        for (int i = 0; i < count; i++)
        {
            vh.AddVert(outer[i], color, Vector2.zero);
            vh.AddVert(inner[i], color, Vector2.zero);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int o0 = start + i * 2;
            int i0 = o0 + 1;
            int o1 = start + next * 2;
            int i1 = o1 + 1;

            vh.AddTriangle(o0, o1, i1);
            vh.AddTriangle(o0, i1, i0);
        }
    }

    private void AddNotebookLines(VertexHelper vh, Rect rect, float radius)
    {
        float left = rect.xMin + linePadding.x;
        float right = rect.xMax - linePadding.z;
        float top = rect.yMax - linePadding.y;
        float bottom = rect.yMin + linePadding.w;

        if (right <= left || top <= bottom)
            return;

        Color32 lineColor = notebookLineColor * color;
        for (float y = top; y >= bottom; y -= Mathf.Max(8f, lineSpacing))
        {
            Rect lineRect = new Rect(left, y, right - left, Mathf.Max(1f, lineThickness));
            AddRect(vh, lineRect, lineColor);
        }
    }

    private static void AddRect(VertexHelper vh, Rect rect, Color32 color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector2(rect.xMin, rect.yMin), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin, rect.yMax), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMax), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMin), color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        cornerRadius = Mathf.Max(0f, cornerRadius);
        borderWidth = Mathf.Max(0f, borderWidth);
        cornerSegments = Mathf.Clamp(cornerSegments, 2, 18);
        SetVerticesDirty();
    }
#endif
}
