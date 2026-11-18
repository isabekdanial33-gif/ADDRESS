using System.Collections.Generic;
using Address.Core;
using Address.Placement;
using UnityEngine;

namespace Address.World
{
    /// <summary>
    /// Frutiger Aero as an endless open world: rolling green hills, lakes and rivers under a bright sky, white
    /// glass houses scattered over it - and the ways out are inside the houses. The land is generated in 48 m chunks
    /// around the player as they walk (deterministic from the address, identical for co-op peers) and dropped
    /// again far behind.
    /// </summary>
    public class AeroWorld : MonoBehaviour
    {
        public const float Chunk = 48f; const int Res = 24; const int Radius = 2, DropRadius = 4;
        public float waterLevel = -0.8f;
        public Transform viewer;
        public LevelBuilder.Result level;
        public System.Func<ExitPlan, Transform, ExitTrigger> buildExit;
        LevelKit kit; ulong seed; Material grass; GameObject water; Transform propsRoot;
        readonly Dictionary<Vector2Int, GameObject> chunks = new();
        readonly Queue<Vector2Int> todo = new();
        readonly List<PlacementProfile> decor = new();
        int exitCount;

        public void Init(LevelBuilder.Result res, LevelKit k, Material grassMat, Transform props)
        {
            level = res; kit = k; seed = res.address.BaseSeed; grass = grassMat; propsRoot = props;
            if (kit.db) foreach (var g in new[] { "plant", "planter", "tube", "orb", "bench" }) foreach (var p in kit.db.Group(g)) if (p && p.prefab && p.AllowsStyle("frutiger")) decor.Add(p);
            if (kit.waterMat)
            {
                water = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(water.GetComponent<Collider>());
                water.name = "Water"; water.transform.SetParent(transform, false);
                water.transform.rotation = Quaternion.Euler(90, 0, 0); water.transform.localScale = new Vector3(Chunk * 12, Chunk * 12, 1);
                water.transform.position = new Vector3(0, waterLevel, 0);
                // a lake is not a pool: big soft ripples, calmer surface, deeper blue-green
                var lake = new Material(kit.waterMat); lake.SetFloat("_Tiling", 14f); lake.SetFloat("_NormalScale", 0.25f); lake.SetFloat("_Speed", 0.02f);
                lake.SetColor("_ShallowColor", new Color(0.45f, 0.82f, 0.86f)); lake.SetColor("_DeepColor", new Color(0.04f, 0.3f, 0.42f)); lake.SetFloat("_Absorb", 0.6f);
                water.GetComponent<Renderer>().sharedMaterial = lake; water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for (int z = -Radius; z <= Radius; z++) for (int x = -Radius; x <= Radius; x++) BuildChunk(new Vector2Int(x, z));
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ the land

        float N(float x, float z, float f, ulong salt) => Mathf.PerlinNoise(x * f + (seed % 9973) * 0.37f + salt * 17.3f, z * f + (seed % 7919) * 0.21f + salt * 5.1f);

        /// <summary>Height of the ground: soft hills, a river network carved below the water line, flat start.</summary>
        public float Height(float x, float z)
        {
            float h = (N(x, z, 0.008f, 1) - 0.45f) * 9f + (N(x, z, 0.03f, 2) - 0.5f) * 2f + (N(x, z, 0.12f, 3) - 0.5f) * 0.3f;
            // rivers: where a slow noise crosses its middle, cut a smooth channel
            float r = Mathf.Abs(N(x + N(x, z, 0.02f, 9) * 30f, z, 0.005f, 4) - 0.5f);
            if (r < 0.07f) h = Mathf.Lerp(h, waterLevel - 1.2f, Mathf.SmoothStep(0, 1, 1 - r / 0.07f) * Mathf.SmoothStep(0, 1, 1 - r / 0.07f));   // wide, gently sloped banks
            // the arrival spot is a gentle dry meadow
            float d = Mathf.Sqrt(x * x + z * z);
            if (d < 26f) h = Mathf.Lerp(Mathf.Max(h, 0.6f), h, Mathf.SmoothStep(0, 1, (d - 10f) / 16f));
            return h;
        }

        void Update()
        {
            if (!viewer) return;
            var p = viewer.position; var c = new Vector2Int(Mathf.FloorToInt(p.x / Chunk), Mathf.FloorToInt(p.z / Chunk));
            if (water) water.transform.position = new Vector3(Mathf.Round(p.x / Chunk) * Chunk, waterLevel, Mathf.Round(p.z / Chunk) * Chunk);
            if (todo.Count == 0)
            {
                for (int z = -Radius; z <= Radius; z++) for (int x = -Radius; x <= Radius; x++) { var k = c + new Vector2Int(x, z); if (!chunks.ContainsKey(k) && !todo.Contains(k)) todo.Enqueue(k); }
                var drop = new List<Vector2Int>();
                foreach (var kv in chunks) if (Mathf.Abs(kv.Key.x - c.x) > DropRadius || Mathf.Abs(kv.Key.y - c.y) > DropRadius) drop.Add(kv.Key);
                foreach (var k in drop) { DropExits(chunks[k]); Destroy(chunks[k]); chunks.Remove(k); }
            }
            if (todo.Count > 0) BuildChunk(todo.Dequeue());   // one chunk per frame: no hitch
        }

        void DropExits(GameObject chunk)
        {
            level.exits.RemoveAll(e => !e.instance || e.instance.transform.IsChildOf(chunk.transform));
        }

        void BuildChunk(Vector2Int c)
        {
            if (chunks.ContainsKey(c)) return;
            var go = new GameObject($"Chunk {c.x},{c.y}") { layer = LevelBuilder.ArchLayer };
            go.transform.SetParent(transform, false);
            chunks[c] = go;
            // terrain mesh with world-space UVs (seamless grass across chunks)
            var verts = new Vector3[(Res + 1) * (Res + 1)]; var uv = new Vector2[verts.Length]; var tris = new int[Res * Res * 6];
            float step = Chunk / Res, ox = c.x * Chunk, oz = c.y * Chunk;
            for (int z = 0, i = 0; z <= Res; z++) for (int x = 0; x <= Res; x++, i++)
            {
                float wx = ox + x * step, wz = oz + z * step;
                verts[i] = new Vector3(wx, Height(wx, wz), wz); uv[i] = new Vector2(wx / 3.2f, wz / 3.2f);
            }
            for (int z = 0, t = 0; z < Res; z++) for (int x = 0; x < Res; x++)
            {
                int a = z * (Res + 1) + x;
                tris[t++] = a; tris[t++] = a + Res + 1; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = a + Res + 1; tris[t++] = a + Res + 2;
            }
            var mesh = new Mesh { name = "terrain" }; mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = grass;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            var rng = new Rng(Hashing.Combine(seed, Hashing.Combine((ulong)(c.x * 73856093), (ulong)(c.y * 19349663))));
            // a house (two near the arrival spot so a way out is always close)
            bool must = c == new Vector2Int(0, 1) || c == new Vector2Int(-1, -1);
            if (must || rng.Chance(0.45f)) House(go.transform, c, rng);
            // decoration on dry, gentle ground
            int n = rng.Range(3, 9);
            for (int k = 0; k < n && decor.Count > 0; k++)
            {
                float x = ox + rng.Range(2f, Chunk - 2f), z = oz + rng.Range(2f, Chunk - 2f), y = Height(x, z);
                if (y < waterLevel + 0.4f || Slope(x, z) > 0.25f || (x * x + z * z) < 9f) continue;
                var p = decor[rng.Range(0, decor.Count)];
                var d = Instantiate(p.prefab, new Vector3(x, y - 0.03f, z), Quaternion.Euler(0, rng.Range(0f, 360f), 0), go.transform);
                foreach (var po in d.GetComponentsInChildren<PlacedObject>()) Destroy(po);
            }
        }

        float Slope(float x, float z) => Mathf.Max(Mathf.Abs(Height(x + 1, z) - Height(x - 1, z)), Mathf.Abs(Height(x, z + 1) - Height(x, z - 1))) * 0.5f;

        void House(Transform parent, Vector2Int c, Rng rng)
        {
            if (!kit.aeroHousePrefab) return;
            for (int tries = 0; tries < 12; tries++)
            {
                float x = c.x * Chunk + rng.Range(8f, Chunk - 8f), z = c.y * Chunk + rng.Range(8f, Chunk - 8f);
                if (x * x + z * z < 14f * 14f) continue;                       // not on top of the arrival spot
                float h0 = Height(x - 4, z - 4), h1 = Height(x + 4, z - 4), h2 = Height(x - 4, z + 4), h3 = Height(x + 4, z + 4), hc = Height(x, z);
                float lo = Mathf.Min(Mathf.Min(h0, h1), Mathf.Min(h2, h3)), hi = Mathf.Max(Mathf.Max(h0, h1), Mathf.Max(h2, h3));
                if (lo < waterLevel + 0.5f || hi - lo > 1.2f) continue;
                float y = hi - 0.05f;                                            // plinth sits on the highest corner; the rest is buried
                var rot = Quaternion.Euler(0, rng.Range(0, 4) * 90f + rng.Range(-20f, 20f), 0);
                var house = Instantiate(kit.aeroHousePrefab, new Vector3(x, y, z), rot, parent); house.name = "AeroHouse";
                foreach (var t in house.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LevelBuilder.ArchLayer;
                foreach (var r in house.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.Contains("glass") && kit.glassMat) ms[i] = kit.glassMat;
                    r.sharedMaterials = ms;
                    if (!r.GetComponent<Collider>()) { var mf = r.GetComponent<MeshFilter>(); if (mf) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; }
                }
                // a plinth skirt into the hillside so the house never floats
                var skirt = GameObject.CreatePrimitive(PrimitiveType.Cube); skirt.name = "skirt"; skirt.layer = LevelBuilder.ArchLayer;
                skirt.transform.SetParent(house.transform, false); skirt.transform.localPosition = new Vector3(0, -(hi - lo) * 0.5f - 0.05f, 0); skirt.transform.localScale = new Vector3(7.5f, hi - lo + 0.3f, 6.5f);
                skirt.GetComponent<Renderer>().sharedMaterial = house.GetComponentInChildren<Renderer>().sharedMaterial;
                // a ramp from the ground up to the doorstep (the plinth is higher than one can step)
                var doorFront = house.transform.TransformPoint(new Vector3(0, 0, 3.3f));
                float ground = Height(doorFront.x, doorFront.z), top = house.transform.position.y + 0.27f, rise = top - ground;
                if (rise > 0.12f)
                {
                    float len = Mathf.Max(1.2f, rise * 3.2f), ang = Mathf.Atan2(rise, len) * Mathf.Rad2Deg;
                    var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "ramp"; ramp.layer = LevelBuilder.ArchLayer;
                    ramp.transform.SetParent(house.transform, false);
                    ramp.transform.localRotation = Quaternion.Euler(ang, 0, 0);
                    ramp.transform.localScale = new Vector3(2.0f, 0.12f, Mathf.Sqrt(len * len + rise * rise));
                    ramp.transform.position = house.transform.TransformPoint(new Vector3(0, 0, 3.0f + len * 0.5f)) + Vector3.up * (top - rise * 0.5f - house.transform.position.y - 0.06f);
                    ramp.GetComponent<Renderer>().sharedMaterial = skirt.GetComponent<Renderer>().sharedMaterial;
                }
                // the way out stands inside, against the back wall, facing the door
                var back = house.transform.TransformPoint(new Vector3(0, 0.27f, -2.85f)); var normal = house.transform.forward;
                var plan = new ExitPlan
                {
                    index = 100 + exitCount, form = ExitForm.Archway, width = 1.8f, position = back, normal = normal, reachable = true,
                    role = exitCount == 0 ? ExitRole.Forward : (rng.Chance(0.75f) ? ExitRole.Forward : ExitRole.Loop)
                };
                exitCount++;
                var trig = buildExit?.Invoke(plan, house.transform);
                if (trig) level.exits.Add(plan);
                return;
            }
        }
    }
}
