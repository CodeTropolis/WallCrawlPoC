using UnityEngine;

/// <summary>
/// Generates a procedural star-field texture using individual pixel writes and
/// renders it as a fullscreen quad behind all other content.
/// Attach to the Main Camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class StarBackground : MonoBehaviour
{
    [Header("Texture")]
    public int textureWidth  = 1920;
    public int textureHeight = 1080;

    [Header("Stars")]
    public int   totalStars         = 700;
    public int   seed               = 0;
    [Range(0f, 1f)] public float brightFraction  = 0.06f;   // fully saturated stars
    [Range(0f, 1f)] public float mediumFraction  = 0.20f;   // medium-intensity stars

    void Start()
    {
        Texture2D tex = GenerateStarTexture();
        CreateBackgroundQuad(tex);
    }

    // ── Texture generation ────────────────────────────────────────────────────

    Texture2D GenerateStarTexture()
    {
        var tex = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "StarFieldTexture"
        };

        Color[] pixels = new Color[textureWidth * textureHeight];
        // Initialise to opaque black.
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(0f, 0f, 0f, 1f);

        Random.InitState(seed);

        int brightCount = Mathf.RoundToInt(totalStars * brightFraction);
        int mediumCount = Mathf.RoundToInt(totalStars * mediumFraction);
        int dimCount    = totalStars - brightCount - mediumCount;

        PlaceStars(pixels, brightCount,  StarType.Bright);
        PlaceStars(pixels, mediumCount,  StarType.Medium);
        PlaceStars(pixels, dimCount,     StarType.Dim);

        tex.SetPixels(pixels);
        tex.Apply(false);
        return tex;
    }

    enum StarType { Dim, Medium, Bright }

    void PlaceStars(Color[] pixels, int count, StarType type)
    {
        int margin = 3;
        for (int i = 0; i < count; i++)
        {
            int x = Random.Range(margin, textureWidth  - margin);
            int y = Random.Range(margin, textureHeight - margin);

            switch (type)
            {
                case StarType.Dim:
                    // Single pixel, low brightness.
                    float dimB = Random.Range(0.10f, 0.40f);
                    WritePixel(pixels, x, y, dimB);
                    break;

                case StarType.Medium:
                    // Single pixel, mid brightness. Occasionally 2 px.
                    float medB = Random.Range(0.40f, 0.72f);
                    WritePixel(pixels, x, y, medB);
                    if (medB > 0.58f)
                        WritePixelDim(pixels, x + 1, y, medB * 0.30f);
                    break;

                case StarType.Bright:
                    // Full-white core with a cross-shaped pixel glow.
                    WritePixel(pixels, x, y, 1.00f);
                    WritePixelDim(pixels, x + 1, y,     0.55f);
                    WritePixelDim(pixels, x - 1, y,     0.55f);
                    WritePixelDim(pixels, x,     y + 1, 0.55f);
                    WritePixelDim(pixels, x,     y - 1, 0.55f);
                    WritePixelDim(pixels, x + 2, y,     0.18f);
                    WritePixelDim(pixels, x - 2, y,     0.18f);
                    WritePixelDim(pixels, x,     y + 2, 0.18f);
                    WritePixelDim(pixels, x,     y - 2, 0.18f);
                    break;
            }
        }
    }

    // Writes brightness, keeping the maximum value so bright stars win overlaps.
    void WritePixel(Color[] pixels, int x, int y, float brightness)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        int idx = y * textureWidth + x;
        if (pixels[idx].r < brightness)
            pixels[idx] = new Color(brightness, brightness, brightness, 1f);
    }

    // Same as WritePixel but never overwrites a brighter value (used for glow arms).
    void WritePixelDim(Color[] pixels, int x, int y, float brightness)
    {
        WritePixel(pixels, x, y, brightness);
    }

    // ── Quad creation ─────────────────────────────────────────────────────────

    void CreateBackgroundQuad(Texture2D tex)
    {
        Camera cam = GetComponent<Camera>();
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        var quad = new GameObject("StarBackground");
        quad.transform.SetParent(transform, false);
        // Push behind gameplay content; sortingOrder handles draw order in URP 2D.
        quad.transform.localPosition = new Vector3(0f, 0f, 50f);

        var mesh = new Mesh { name = "StarBackgroundQuad" };
        mesh.SetVertices(new[]
        {
            new Vector3(-halfW, -halfH, 0f),
            new Vector3(-halfW,  halfH, 0f),
            new Vector3( halfW,  halfH, 0f),
            new Vector3( halfW, -halfH, 0f),
        });
        mesh.SetUVs(0, new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();

        quad.AddComponent<MeshFilter>().mesh = mesh;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Texture");

        var mat      = new Material(shader) { mainTexture = tex };
        var renderer = quad.AddComponent<MeshRenderer>();
        renderer.material     = mat;
        renderer.sortingOrder = -1000;
    }
}
