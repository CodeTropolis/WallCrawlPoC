using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public float chunkWidth = 20f;
    public int segmentsPerChunk = 24;
    public int chunksVisible = 3;

    [Header("Terrain Shape")]
    public float amplitude = 4f;
    public float frequency = 0.06f;
    public float terrainYOffset = -5f;
    public float terrainBottom = -12f;

    [Header("Pits")]
    public bool enablePits = true;
    [Tooltip("One pit opportunity per zone. Must be larger than pitMaxWidth.")]
    public float pitZoneWidth = 30f;
    [Range(0f, 1f), Tooltip("Probability that any given zone contains a pit.")]
    public float pitChance = 0.6f;
    [Tooltip("Narrowest possible pit.")]
    public float pitMinWidth = 4f;
    [Tooltip("Widest possible pit.")]
    public float pitMaxWidth = 8f;

    [Header("Appearance")]
    public Color terrainColor = new Color(0.3f, 0.6f, 0.2f);

    private float noiseSeed;
    private Dictionary<int, GameObject> activeChunks = new Dictionary<int, GameObject>();
    private int lastChunkIndex = int.MinValue;
    private Material terrainMaterial;

    void Start()
    {
        noiseSeed = Random.Range(0f, 10000f);
        terrainMaterial = CreateMaterial();
        RefreshChunks();
    }

    void Update()
    {
        int chunkIndex = Mathf.FloorToInt(transform.position.x / chunkWidth);
        if (chunkIndex != lastChunkIndex)
        {
            lastChunkIndex = chunkIndex;

            // Vary pit parameters once per chunk crossing, not every frame.
            float time = chunkIndex * 0.1f;
            pitZoneWidth = 40f + (10f * Mathf.PerlinNoise(noiseSeed + 100f, time));
            pitChance = 0.4f + (0.4f * Mathf.PerlinNoise(noiseSeed + 200f, time));

            RefreshChunks();
        }
    }

    void RefreshChunks()
    {
        int center = Mathf.FloorToInt(transform.position.x / chunkWidth);

        var toRemove = new List<int>();
        foreach (int key in activeChunks.Keys)
            if (Mathf.Abs(key - center) > chunksVisible + 1)
                toRemove.Add(key);
        foreach (int key in toRemove)
        {
            Destroy(activeChunks[key]);
            activeChunks.Remove(key);
        }

        for (int i = center - chunksVisible; i <= center + chunksVisible; i++)
            if (!activeChunks.ContainsKey(i))
                activeChunks[i] = SpawnChunk(i);
    }

    float HeightAt(float worldX)
    {
        return Mathf.PerlinNoise(worldX * frequency + noiseSeed, 0.5f) * amplitude + terrainYOffset;
    }

    // Deterministic pseudo-random float in [0,1) for a given zone index and slot.
    float PseudoRandom(int zone, int slot)
    {
        float v = Mathf.Sin(zone * 127.1f + slot * 311.7f + noiseSeed) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    // Returns all pit (start, end) intervals that overlap [queryStart, queryEnd].
    // Pit positions are fully deterministic based on noiseSeed, so any chunk
    // querying the same range will get identical results — no seams across chunks.
    List<(float start, float end)> GetPitsInRange(float queryStart, float queryEnd)
    {
        var result = new List<(float, float)>();
        if (!enablePits) return result;

        float safeZoneWidth = Mathf.Max(pitZoneWidth, pitMaxWidth + 1f);

        int firstZone = Mathf.FloorToInt(queryStart / safeZoneWidth);
        int lastZone = Mathf.FloorToInt(queryEnd / safeZoneWidth);

        for (int z = firstZone; z <= lastZone; z++)
        {
            if (PseudoRandom(z, 0) > pitChance) continue;

            float width = Mathf.Lerp(pitMinWidth, pitMaxWidth, PseudoRandom(z, 2));
            float offset = PseudoRandom(z, 1) * (safeZoneWidth - width);
            float pitStart = z * safeZoneWidth + offset;
            float pitEnd = pitStart + width;

            if (pitEnd > queryStart && pitStart < queryEnd)
                result.Add((pitStart, pitEnd));
        }

        return result;
    }

    static bool IsInAnyPit(float x, List<(float start, float end)> pits)
    {
        foreach (var (start, end) in pits)
            if (x > start && x < end) return true;
        return false;
    }

    GameObject SpawnChunk(int index)
    {
        float startX = index * chunkWidth;
        float endX = startX + chunkWidth;
        float segW = chunkWidth / segmentsPerChunk;

        // Collect pits that overlap this chunk (with a small margin for edge verts)
        var pits = GetPitsInRange(startX - segW, endX + segW);

        // Build sorted x sample positions: regular grid + pit edge positions
        var xPositions = new List<float>();
        for (int i = 0; i <= segmentsPerChunk; i++)
            xPositions.Add(startX + i * segW);

        foreach (var (pitStart, pitEnd) in pits)
        {
            if (pitStart > startX && pitStart < endX) xPositions.Add(pitStart);
            if (pitEnd > startX && pitEnd < endX) xPositions.Add(pitEnd);
        }

        xPositions.Sort();

        // Remove near-duplicates (positions within 0.001 units of each other)
        for (int i = xPositions.Count - 2; i >= 0; i--)
            if (xPositions[i + 1] - xPositions[i] < 0.001f)
                xPositions.RemoveAt(i + 1);

        // Build mesh: each solid segment becomes an independent quad (4 verts, 2 tris).
        // Independent quads avoid shared-vertex issues at pit edges.
        var vertices = new List<Vector3>();
        var tris = new List<int>();

        for (int i = 0; i < xPositions.Count - 1; i++)
        {
            float x0 = xPositions[i];
            float x1 = xPositions[i + 1];
            float mid = (x0 + x1) * 0.5f;

            if (IsInAnyPit(mid, pits)) continue;

            float y0 = HeightAt(x0);
            float y1 = HeightAt(x1);

            int v = vertices.Count;
            vertices.Add(new Vector3(x0, y0, 0f));           // 0 top-left
            vertices.Add(new Vector3(x1, y1, 0f));           // 1 top-right
            vertices.Add(new Vector3(x0, terrainBottom, 0f)); // 2 bottom-left
            vertices.Add(new Vector3(x1, terrainBottom, 0f)); // 3 bottom-right

            // Winding: normals face -Z (toward camera at negative Z)
            tris.Add(v + 0); tris.Add(v + 1); tris.Add(v + 2);
            tris.Add(v + 1); tris.Add(v + 3); tris.Add(v + 2);
        }

        if (vertices.Count == 0)
            return new GameObject($"TerrainChunk_{index}_empty");

        var mesh = new Mesh { name = $"TerrainChunk_{index}" };
        mesh.vertices = vertices.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject($"TerrainChunk_{index}");
        go.layer = LayerMask.NameToLayer("Ground");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

        // Build solid PolygonCollider2D runs — one per continuous solid surface segment.
        // PolygonCollider2D (top surface + bottom corners) gives the physics engine a
        // solid interior, preventing the Rigidbody oscillation that EdgeCollider2D causes
        // on sloped surfaces.
        var currentRun = new List<Vector2>();
        for (int i = 0; i < xPositions.Count - 1; i++)
        {
            float x0 = xPositions[i];
            float x1 = xPositions[i + 1];
            float mid = (x0 + x1) * 0.5f;

            if (IsInAnyPit(mid, pits))
            {
                if (currentRun.Count >= 2)
                {
                    AddSolidCollider(go, currentRun);
                    currentRun = new List<Vector2>();
                }
                else
                {
                    currentRun.Clear();
                }
            }
            else
            {
                if (currentRun.Count == 0)
                    currentRun.Add(new Vector2(x0, HeightAt(x0)));
                currentRun.Add(new Vector2(x1, HeightAt(x1)));
            }
        }
        if (currentRun.Count >= 2)
            AddSolidCollider(go, currentRun);

        return go;
    }

    void AddSolidCollider(GameObject go, List<Vector2> run)
    {
        var path = new List<Vector2>(run);
        path.Add(new Vector2(run[run.Count - 1].x, terrainBottom));
        path.Add(new Vector2(run[0].x, terrainBottom));
        var pc = go.AddComponent<PolygonCollider2D>();
        pc.pathCount = 1;
        pc.SetPath(0, path.ToArray());
    }

    Material CreateMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = terrainColor;
        return mat;
    }
}
