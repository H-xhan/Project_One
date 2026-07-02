using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ProjectOneProceduralIconGraphic : MaskableGraphic
{
    public enum IconType
    {
        None,
        Coin,
        Clock,
        Play,
        Paw,
        People,
        Home,
        Exit,
        Lightning,
        Check,
        Dots,
        Crown
    }

    [SerializeField] private IconType iconType = IconType.Paw;
    [SerializeField] private Color accentColor = Color.white;
    [SerializeField] private Color secondaryColor = new Color(0.10f, 0.16f, 0.35f, 1f);
    [SerializeField] private float strokeWidth = 8f;
    [SerializeField] private int segments = 28;

    public void Configure(IconType type, Color main, Color secondary, float stroke)
    {
        iconType = type;
        accentColor = main;
        secondaryColor = secondary;
        strokeWidth = Mathf.Max(1f, stroke);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float s = Mathf.Min(r.width, r.height);
        Vector2 c = r.center;
        Color32 main = accentColor * color;
        Color32 secondary = secondaryColor * color;
        int safeSegments = Mathf.Clamp(segments, 12, 72);

        switch (iconType)
        {
            case IconType.Coin:
                AddCircle(vh, c, s * 0.45f, safeSegments, main);
                AddRing(vh, c, s * 0.45f, s * 0.35f, safeSegments, secondary);
                AddPaw(vh, c, s * 0.62f, secondary);
                break;
            case IconType.Clock:
                AddCircle(vh, c, s * 0.43f, safeSegments, main);
                AddRing(vh, c, s * 0.43f, s * 0.34f, safeSegments, secondary);
                AddLine(vh, c, c + new Vector2(0f, s * 0.22f), strokeWidth, secondary);
                AddLine(vh, c, c + new Vector2(s * 0.18f, -s * 0.08f), strokeWidth, secondary);
                break;
            case IconType.Play:
                AddTriangle(vh, c + new Vector2(-s * 0.18f, -s * 0.28f), c + new Vector2(-s * 0.18f, s * 0.28f), c + new Vector2(s * 0.30f, 0f), main);
                break;
            case IconType.Paw:
                AddPaw(vh, c, s, main);
                break;
            case IconType.People:
                AddPeople(vh, c, s, main);
                break;
            case IconType.Home:
                AddHome(vh, c, s, main);
                break;
            case IconType.Exit:
                AddExit(vh, c, s, main);
                break;
            case IconType.Lightning:
                AddLightning(vh, c, s, main);
                break;
            case IconType.Check:
                AddLine(vh, c + new Vector2(-s * 0.28f, -s * 0.02f), c + new Vector2(-s * 0.08f, -s * 0.24f), strokeWidth * 1.3f, main);
                AddLine(vh, c + new Vector2(-s * 0.08f, -s * 0.24f), c + new Vector2(s * 0.30f, s * 0.22f), strokeWidth * 1.3f, main);
                break;
            case IconType.Dots:
                AddCircle(vh, c + new Vector2(0f, s * 0.24f), s * 0.065f, safeSegments, main);
                AddCircle(vh, c, s * 0.065f, safeSegments, main);
                AddCircle(vh, c + new Vector2(0f, -s * 0.24f), s * 0.065f, safeSegments, main);
                break;
            case IconType.Crown:
                AddCrown(vh, c, s, main);
                break;
        }
    }

    private void AddPaw(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        AddCircle(vh, c + new Vector2(0f, -s * 0.08f), s * 0.18f, 28, col);
        AddCircle(vh, c + new Vector2(-s * 0.22f, s * 0.16f), s * 0.10f, 24, col);
        AddCircle(vh, c + new Vector2(-s * 0.07f, s * 0.25f), s * 0.10f, 24, col);
        AddCircle(vh, c + new Vector2(s * 0.09f, s * 0.25f), s * 0.10f, 24, col);
        AddCircle(vh, c + new Vector2(s * 0.24f, s * 0.14f), s * 0.10f, 24, col);
    }

    private void AddPeople(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        AddCircle(vh, c + new Vector2(0f, s * 0.17f), s * 0.13f, 24, col);
        AddCircle(vh, c + new Vector2(-s * 0.24f, s * 0.07f), s * 0.10f, 24, col);
        AddCircle(vh, c + new Vector2(s * 0.24f, s * 0.07f), s * 0.10f, 24, col);
        AddRect(vh, new Rect(c.x - s * 0.20f, c.y - s * 0.27f, s * 0.40f, s * 0.28f), col);
        AddRect(vh, new Rect(c.x - s * 0.42f, c.y - s * 0.30f, s * 0.24f, s * 0.22f), col);
        AddRect(vh, new Rect(c.x + s * 0.18f, c.y - s * 0.30f, s * 0.24f, s * 0.22f), col);
    }

    private void AddHome(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        AddTriangle(vh, c + new Vector2(-s * 0.38f, -s * 0.02f), c + new Vector2(0f, s * 0.34f), c + new Vector2(s * 0.38f, -s * 0.02f), col);
        AddRect(vh, new Rect(c.x - s * 0.27f, c.y - s * 0.35f, s * 0.54f, s * 0.34f), col);
        AddRect(vh, new Rect(c.x - s * 0.07f, c.y - s * 0.35f, s * 0.14f, s * 0.22f), new Color32(255, 255, 255, 180));
    }

    private void AddExit(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        AddLine(vh, c + new Vector2(-s * 0.33f, s * 0.35f), c + new Vector2(-s * 0.33f, -s * 0.35f), strokeWidth, col);
        AddLine(vh, c + new Vector2(-s * 0.33f, s * 0.35f), c + new Vector2(s * 0.05f, s * 0.35f), strokeWidth, col);
        AddLine(vh, c + new Vector2(-s * 0.33f, -s * 0.35f), c + new Vector2(s * 0.05f, -s * 0.35f), strokeWidth, col);
        AddLine(vh, c + new Vector2(-s * 0.05f, 0f), c + new Vector2(s * 0.35f, 0f), strokeWidth, col);
        AddTriangle(vh, c + new Vector2(s * 0.35f, 0f), c + new Vector2(s * 0.12f, s * 0.18f), c + new Vector2(s * 0.12f, -s * 0.18f), col);
    }

    private static void AddLightning(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        Vector2[] p =
        {
            c + new Vector2(s * 0.08f, s * 0.43f),
            c + new Vector2(-s * 0.22f, s * 0.02f),
            c + new Vector2(-s * 0.02f, s * 0.02f),
            c + new Vector2(-s * 0.12f, -s * 0.43f),
            c + new Vector2(s * 0.25f, -s * 0.02f),
            c + new Vector2(s * 0.03f, -s * 0.02f),
        };
        AddPolygon(vh, p, col);
    }

    private static void AddCrown(VertexHelper vh, Vector2 c, float s, Color32 col)
    {
        Vector2[] p =
        {
            c + new Vector2(-s * 0.38f, -s * 0.20f),
            c + new Vector2(-s * 0.30f, s * 0.22f),
            c + new Vector2(-s * 0.10f, -s * 0.02f),
            c + new Vector2(0f, s * 0.32f),
            c + new Vector2(s * 0.10f, -s * 0.02f),
            c + new Vector2(s * 0.30f, s * 0.22f),
            c + new Vector2(s * 0.38f, -s * 0.20f),
        };
        AddPolygon(vh, p, col);
        AddRect(vh, new Rect(c.x - s * 0.36f, c.y - s * 0.32f, s * 0.72f, s * 0.13f), col);
    }

    private static void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments, Color32 col)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, col, new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, col, Vector2.zero);
        }
        for (int i = 1; i <= segments; i++)
            vh.AddTriangle(start, start + i, start + i + 1);
    }

    private static void AddRing(VertexHelper vh, Vector2 center, float outer, float inner, int segments, Color32 col)
    {
        int start = vh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vh.AddVert(center + dir * outer, col, Vector2.zero);
            vh.AddVert(center + dir * inner, col, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
        {
            int o0 = start + i * 2;
            int i0 = o0 + 1;
            int o1 = start + (i + 1) * 2;
            int i1 = o1 + 1;
            vh.AddTriangle(o0, o1, i1);
            vh.AddTriangle(o0, i1, i0);
        }
    }

    private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 col)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        AddQuad(vh, a - normal, a + normal, b + normal, b - normal, col);
    }

    private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero);
        vh.AddVert(b, col, Vector2.zero);
        vh.AddVert(c, col, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }

    private static void AddRect(VertexHelper vh, Rect rect, Color32 col)
    {
        AddQuad(vh,
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMax, rect.yMin), col);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero);
        vh.AddVert(b, col, Vector2.zero);
        vh.AddVert(c, col, Vector2.zero);
        vh.AddVert(d, col, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddPolygon(VertexHelper vh, Vector2[] points, Color32 col)
    {
        if (points == null || points.Length < 3)
            return;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < points.Length; i++)
            center += points[i];
        center /= points.Length;

        int start = vh.currentVertCount;
        vh.AddVert(center, col, Vector2.zero);
        for (int i = 0; i < points.Length; i++)
            vh.AddVert(points[i], col, Vector2.zero);

        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            vh.AddTriangle(start, start + 1 + i, start + 1 + next);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        strokeWidth = Mathf.Max(1f, strokeWidth);
        segments = Mathf.Clamp(segments, 12, 72);
        SetVerticesDirty();
    }
#endif
}
