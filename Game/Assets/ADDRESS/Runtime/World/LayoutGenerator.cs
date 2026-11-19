using System.Collections.Generic;
using System.Linq;
using Address.Core;
using Address.Placement;
using UnityEngine;

namespace Address.World
{
    /// <summary>
    /// Backrooms floor plan: BSP partition into rooms on a 0.5 m grid, adjacency graph -> spanning tree + extra loops,
    /// each connection becomes a door, a wide opening or a fully merged wall. Pure data (no GameObjects):
    /// the result is deterministic for a seed and can be validated before anything is built.
    /// </summary>
    public class LayoutGenerator
    {
        public const float Cell = 0.5f, WallT = 0.15f, DoorW = 1.0f;

        public class Room
        {
            public int id; public RectInt cells; public string kind; public RoomSpec spec;
            public bool dark;          // part of a dark zone: dead and failing lights
            public Rect World => new(cells.xMin * Cell, cells.yMin * Cell, cells.width * Cell, cells.height * Cell);
        }

        public enum OpeningKind { Door, Wide, Merge }

        public class Opening
        {
            public int a, b; public OpeningKind kind;
            public bool vertical;      // boundary line is along Z (x = const)
            public float line;         // x (vertical) or z (horizontal) of the boundary, world
            public float from, to;     // extent along the boundary, world
            public Vector3 Center => vertical ? new Vector3(line, 0, (from + to) * 0.5f) : new Vector3((from + to) * 0.5f, 0, line);
        }

        public class Layout
        {
            public int w, h;                    // cells
            public int[,] cellRoom;             // room id per cell (-1 = solid)
            public List<Room> rooms = new();
            public List<Opening> openings = new();
            public int startRoom;
            public float height = 2.75f;
            public Bounds WorldBounds => new(new Vector3(w * Cell * 0.5f, height * 0.5f, h * Cell * 0.5f), new Vector3(w * Cell, height, h * Cell));
        }

        // BSP room size and door share (the parking garage uses huge open sections)
        public float minRoomM = 3.5f, maxRoomLo = 6.5f, maxRoomHi = 10f, doorShare = 0.5f;
        public string forceKind;

        public Layout Generate(Rng rng, int widthM, int depthM)
        {
            var L = new Layout { w = Mathf.RoundToInt(widthM / Cell), h = Mathf.RoundToInt(depthM / Cell) };
            L.cellRoom = new int[L.w, L.h];
            var leaves = new List<RectInt>();
            halls.Clear(); hallsLeft = rng.Chance(0.75f) ? (rng.Chance(0.35f) ? 2 : 1) : 0;
            Split(new RectInt(0, 0, L.w, L.h), rng, leaves, 0);
            foreach (var r in leaves)
            {
                var room = new Room { id = L.rooms.Count, cells = r };
                L.rooms.Add(room);
                for (int x = r.xMin; x < r.xMax; x++) for (int z = r.yMin; z < r.yMax; z++) L.cellRoom[x, z] = room.id;
            }

            // adjacency (shared boundary >= 1.5 m)
            var edges = new List<Opening>();
            for (int i = 0; i < L.rooms.Count; i++)
                for (int j = i + 1; j < L.rooms.Count; j++)
                {
                    var o = Shared(L.rooms[i].cells, L.rooms[j].cells);
                    if (o != null && o.to - o.from >= 1.5f) { o.a = i; o.b = j; edges.Add(o); }
                }

            // spanning tree (randomised Kruskal) + extra loops
            rng.Shuffle(edges);
            var parent = Enumerable.Range(0, L.rooms.Count).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var chosen = new List<Opening>();
            foreach (var e in edges) { int ra = Find(e.a), rb = Find(e.b); if (ra != rb) { parent[ra] = rb; chosen.Add(e); } }
            foreach (var e in edges) if (!chosen.Contains(e) && rng.Chance(0.35f)) chosen.Add(e);

            foreach (var e in chosen)
            {
                float len = e.to - e.from;
                float r = rng.Value;
                e.kind = r < doorShare ? OpeningKind.Door : r < doorShare + (1 - doorShare) * 0.7f ? OpeningKind.Wide : OpeningKind.Merge;
                if (e.kind == OpeningKind.Door || len < DoorW + 0.8f)
                {
                    e.kind = OpeningKind.Door;
                    // door centre on a cell boundary so the gap is exactly two 0.5 m cells = DoorW
                    float c = rng.Range(e.from + 0.4f + DoorW * 0.5f, e.to - 0.4f - DoorW * 0.5f);
                    c = Mathf.Clamp(Mathf.Round(c / Cell) * Cell, e.from + DoorW * 0.5f, e.to - DoorW * 0.5f);
                    e.from = c - DoorW * 0.5f; e.to = c + DoorW * 0.5f;
                }
                else if (e.kind == OpeningKind.Wide)
                {
                    float wdt = Mathf.Max(1.8f, len * rng.Range(0.4f, 0.85f));
                    wdt = Mathf.Round(wdt / Cell) * Cell;
                    float c = rng.Range(e.from + wdt * 0.5f, e.to - wdt * 0.5f);
                    c = Mathf.Round(c / 0.25f) * 0.25f;
                    e.from = Mathf.Max(e.from, c - wdt * 0.5f); e.to = Mathf.Min(e.to, c + wdt * 0.5f);
                }
                // Merge keeps the full shared extent
                L.openings.Add(e);
            }

            // room kinds: Backrooms are mostly empty, with occasional furnished pockets
            foreach (var room in L.rooms)
            {
                var wr = room.World;
                if (forceKind != null) { room.kind = forceKind; continue; }
                if (Mathf.Min(wr.width, wr.height) < 3.2f) { room.kind = "corridor"; continue; }
                if (halls.Contains(room.cells)) { room.kind = rng.Chance(0.55f) ? "showroom" : "pillars"; continue; }
                float k = rng.Value, area = wr.width * wr.height;
                room.kind = k < 0.32f ? "empty" : k < 0.47f ? "office" : k < 0.56f ? "storage" : k < 0.62f ? "lounge" : k < 0.68f ? "breakroom"
                          : k < 0.73f ? "meeting" : k < 0.77f ? "server" : k < 0.85f ? "showroom" : k < 0.92f ? "dump" : k < 0.97f ? "pillars" : "wrong";
                if (room.kind == "showroom" && area < 22f) room.kind = "lounge";
                if (room.kind == "pillars" && (wr.width < 5.5f || wr.height < 5.5f)) room.kind = "empty";
            }
            L.startRoom = rng.Range(0, L.rooms.Count);
            MarkDark(L, rng.Fork("dark"));
            return L;
        }

        /// <summary>
        /// Dark zones: one or two clusters of connected rooms (grown through openings) where most tubes are dead.
        /// The start room and its neighbours are always lit; a dark office turns into junk or nothing.
        /// </summary>
        static void MarkDark(Layout L, Rng rng)
        {
            if (L.rooms.Count < 6) return;
            int clusters = rng.Chance(0.5f) ? 2 : 1;
            int target = Mathf.Max(2, Mathf.RoundToInt(L.rooms.Count * rng.Range(0.14f, 0.26f)));
            var adj = new List<int>[L.rooms.Count];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
            foreach (var o in L.openings) { adj[o.a].Add(o.b); adj[o.b].Add(o.a); }
            int marked = 0;
            for (int c = 0; c < clusters; c++)
            {
                int seed = rng.Range(0, L.rooms.Count);
                var q = new Queue<int>(); q.Enqueue(seed);
                while (q.Count > 0 && marked < target * (c + 1) / clusters)
                {
                    int r = q.Dequeue();
                    if (L.rooms[r].dark || r == L.startRoom || adj[L.startRoom].Contains(r)) continue;
                    L.rooms[r].dark = true; marked++;
                    if (L.rooms[r].kind == "office" || L.rooms[r].kind == "meeting") L.rooms[r].kind = rng.Chance(0.5f) ? "dump" : "empty";
                    foreach (var n in adj[r]) if (!L.rooms[n].dark && rng.Chance(0.8f)) q.Enqueue(n);
                }
            }
        }

        /// <summary>
        /// Endless hotel: a grid of 2 m corridors (rows every 7-9 m, cross corridors every 10-14 m), everything else is
        /// solid. Corridor segments and crossings are rooms; neighbours are fully open to each other.
        /// </summary>
        public static Layout Hotel(Rng rng, int sizeM)
        {
            var L = new Layout { w = Mathf.RoundToInt(sizeM / Cell), h = Mathf.RoundToInt(sizeM / Cell), height = 2.6f };
            L.cellRoom = new int[L.w, L.h];
            for (int x = 0; x < L.w; x++) for (int z = 0; z < L.h; z++) L.cellRoom[x, z] = -1;
            const int CW = 4;                                   // 2 m wide
            var rows = new List<int>(); var cols = new List<int>();
            for (int z = 2; z + CW < L.h - 1; z += rng.Range(14, 19)) rows.Add(z);
            for (int x = 2; x + CW < L.w - 1; x += rng.Range(20, 29)) cols.Add(x);
            void Add(RectInt r, string kind)
            {
                if (r.width <= 0 || r.height <= 0) return;
                var room = new Room { id = L.rooms.Count, cells = r, kind = kind };
                L.rooms.Add(room);
                for (int x = r.xMin; x < r.xMax; x++) for (int z = r.yMin; z < r.yMax; z++) L.cellRoom[x, z] = room.id;
            }
            int left = cols[0], right = cols[cols.Count - 1] + CW, bottom = rows[0], top = rows[rows.Count - 1] + CW;
            // crossings
            foreach (var z in rows) foreach (var x in cols) Add(new RectInt(x, z, CW, CW), "hall");
            // horizontal segments between crossings
            foreach (var z in rows) for (int i = 0; i + 1 < cols.Count; i++) Add(new RectInt(cols[i] + CW, z, cols[i + 1] - cols[i] - CW, CW), "hall");
            // vertical segments; a few are missing (dead ends, detours), but never on the first column
            foreach (var x in cols) for (int j = 0; j + 1 < rows.Count; j++)
                if (x == cols[0] || rng.Chance(0.8f)) Add(new RectInt(x, rows[j] + CW, CW, rows[j + 1] - rows[j] - CW), "hall");
            for (int i = 0; i < L.rooms.Count; i++)
                for (int j = i + 1; j < L.rooms.Count; j++)
                {
                    var o = Shared(L.rooms[i].cells, L.rooms[j].cells);
                    if (o != null && o.to - o.from >= 1.5f) { o.a = i; o.b = j; o.kind = OpeningKind.Merge; L.openings.Add(o); }
                }
            L.startRoom = rng.Range(0, L.rooms.Count);
            return L;
        }

        /// <summary>Frutiger Aero park: ONE open space (no rooms, no ceiling), walled only at its edge.</summary>
        public static Layout Park(int sizeM)
        {
            var L = new Layout { w = Mathf.RoundToInt(sizeM / Cell), h = Mathf.RoundToInt(sizeM / Cell), height = 3.4f };
            L.cellRoom = new int[L.w, L.h];
            L.rooms.Add(new Room { id = 0, cells = new RectInt(0, 0, L.w, L.h), kind = "atrium" });
            L.startRoom = 0;
            return L;
        }

        /// <summary>One enormous empty hall (rare "vast" place) plus a narrow side room so exits have walls to sit on.</summary>
        public static Layout Vast(int sizeM)
        {
            var L = new Layout { w = Mathf.RoundToInt(sizeM / Cell), h = Mathf.RoundToInt(sizeM / Cell) };
            L.cellRoom = new int[L.w, L.h];
            int split = L.w - Mathf.RoundToInt(4f / Cell);
            var hall = new Room { id = 0, cells = new RectInt(0, 0, split, L.h), kind = "empty" };
            var side = new Room { id = 1, cells = new RectInt(split, 0, L.w - split, L.h), kind = "corridor" };
            L.rooms.Add(hall); L.rooms.Add(side);
            for (int x = 0; x < L.w; x++) for (int z = 0; z < L.h; z++) L.cellRoom[x, z] = x < split ? 0 : 1;
            float c = L.h * Cell * 0.5f;
            L.openings.Add(new Opening { a = 0, b = 1, kind = OpeningKind.Door, vertical = true, line = split * Cell, from = c - DoorW * 0.5f, to = c + DoorW * 0.5f });
            L.startRoom = 0;
            return L;
        }

        readonly List<RectInt> halls = new(); int hallsLeft;

        void Split(RectInt r, Rng rng, List<RectInt> leaves, int depth)
        {
            // a big hall (showroom / columns) now and then: stop splitting a 9..15 m block
            float wm = r.width * Cell, hm = r.height * Cell;
            if (hallsLeft > 0 && depth >= 1 && wm >= 9f && hm >= 8f && wm <= 15f && hm <= 15f && rng.Chance(0.6f)) { hallsLeft--; halls.Add(r); leaves.Add(r); return; }
            int minCells = Mathf.RoundToInt(minRoomM / Cell), maxCells = Mathf.RoundToInt(rng.Range(maxRoomLo, maxRoomHi) / Cell);
            bool canX = r.width >= 2 * minCells, canZ = r.height >= 2 * minCells;
            bool big = r.width > maxCells || r.height > maxCells;
            if ((!canX && !canZ) || (!big && rng.Chance(0.35f)) || depth > 12) { leaves.Add(r); return; }
            bool alongX = canX && (!canZ || (r.width > r.height ? rng.Chance(0.75f) : rng.Chance(0.25f)));
            if (alongX)
            {
                int s = rng.Range(r.xMin + minCells, r.xMax - minCells + 1);
                Split(new RectInt(r.xMin, r.yMin, s - r.xMin, r.height), rng, leaves, depth + 1);
                Split(new RectInt(s, r.yMin, r.xMax - s, r.height), rng, leaves, depth + 1);
            }
            else
            {
                int s = rng.Range(r.yMin + minCells, r.yMax - minCells + 1);
                Split(new RectInt(r.xMin, r.yMin, r.width, s - r.yMin), rng, leaves, depth + 1);
                Split(new RectInt(r.xMin, s, r.width, r.yMax - s), rng, leaves, depth + 1);
            }
        }

        static Opening Shared(RectInt a, RectInt b)
        {
            if (a.xMax == b.xMin || b.xMax == a.xMin)
            {
                int x = a.xMax == b.xMin ? a.xMax : b.xMax;
                int z0 = Mathf.Max(a.yMin, b.yMin), z1 = Mathf.Min(a.yMax, b.yMax);
                if (z1 > z0) return new Opening { vertical = true, line = x * Cell, from = z0 * Cell, to = z1 * Cell };
            }
            if (a.yMax == b.yMin || b.yMax == a.yMin)
            {
                int z = a.yMax == b.yMin ? a.yMax : b.yMax;
                int x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
                if (x1 > x0) return new Opening { vertical = false, line = z * Cell, from = x0 * Cell, to = x1 * Cell };
            }
            return null;
        }

        // ------------------------------------------------------------------ wall geometry

        /// <summary>A wall run on a grid line, centred on the line (thickness WallT).</summary>
        public struct WallRun { public bool vertical; public float line, from, to; }

        /// <summary>All wall runs: every grid edge between different rooms (or the outside) that is not inside an opening.</summary>
        public static List<WallRun> Walls(Layout L)
        {
            var runs = new List<WallRun>();
            // vertical lines x = i (between columns i-1 and i)
            for (int i = 0; i <= L.w; i++)
            {
                float? start = null;
                for (int j = 0; j <= L.h; j++)
                {
                    bool wall = false;
                    if (j < L.h)
                    {
                        int left = i > 0 ? L.cellRoom[i - 1, j] : -1, right = i < L.w ? L.cellRoom[i, j] : -1;
                        wall = left != right && !InOpening(L, true, i * Cell, (j + 0.5f) * Cell);
                    }
                    if (wall && start == null) start = j * Cell;
                    if (!wall && start != null) { runs.Add(new WallRun { vertical = true, line = i * Cell, from = start.Value, to = j * Cell }); start = null; }
                }
            }
            for (int j = 0; j <= L.h; j++)
            {
                float? start = null;
                for (int i = 0; i <= L.w; i++)
                {
                    bool wall = false;
                    if (i < L.w)
                    {
                        int down = j > 0 ? L.cellRoom[i, j - 1] : -1, up = j < L.h ? L.cellRoom[i, j] : -1;
                        wall = down != up && !InOpening(L, false, j * Cell, (i + 0.5f) * Cell);
                    }
                    if (wall && start == null) start = i * Cell;
                    if (!wall && start != null) { runs.Add(new WallRun { vertical = false, line = j * Cell, from = start.Value, to = i * Cell }); start = null; }
                }
            }
            return runs;
        }

        static bool InOpening(Layout L, bool vertical, float line, float along)
        {
            foreach (var o in L.openings)
                if (o.vertical == vertical && Mathf.Abs(o.line - line) < 1e-3f && along > o.from && along < o.to) return true;
            return false;
        }

        /// <summary>RoomSpec per room: inner volume and inner wall faces (for against-wall placement and wall decor).</summary>
        public static void BuildSpecs(Layout L, List<WallRun> runs)
        {
            foreach (var room in L.rooms)
            {
                var wr = room.World;
                var spec = new RoomSpec
                {
                    name = $"{room.id}_{room.kind}",
                    kind = room.kind == "empty" || room.kind == "corridor" ? "office" : room.kind,
                    bounds = new Bounds(new Vector3(wr.center.x, L.height * 0.5f, wr.center.y), new Vector3(wr.width - WallT, L.height, wr.height - WallT))
                };
                // inner faces: wall runs lying on this room's border, clipped to the room's extent, offset inward
                foreach (var r in runs)
                {
                    if (r.vertical)
                    {
                        bool leftSide = Mathf.Abs(r.line - wr.xMin) < 1e-3f, rightSide = Mathf.Abs(r.line - wr.xMax) < 1e-3f;
                        if (!leftSide && !rightSide) continue;
                        float a = Mathf.Max(r.from, wr.yMin), b = Mathf.Min(r.to, wr.yMax);
                        if (b - a < 0.3f) continue;
                        // trim the corners so items never run into the perpendicular walls
                        a = Mathf.Max(a, wr.yMin + WallT * 0.5f); b = Mathf.Min(b, wr.yMax - WallT * 0.5f);
                        float x = leftSide ? r.line + WallT * 0.5f : r.line - WallT * 0.5f;
                        var n = leftSide ? Vector3.right : Vector3.left;
                        spec.walls.Add(new WallSeg { a = new Vector3(x, 0, a), b = new Vector3(x, 0, b), normal = n, height = L.height });
                    }
                    else
                    {
                        bool low = Mathf.Abs(r.line - wr.yMin) < 1e-3f, high = Mathf.Abs(r.line - wr.yMax) < 1e-3f;
                        if (!low && !high) continue;
                        float a = Mathf.Max(r.from, wr.xMin), b = Mathf.Min(r.to, wr.xMax);
                        if (b - a < 0.3f) continue;
                        a = Mathf.Max(a, wr.xMin + WallT * 0.5f); b = Mathf.Min(b, wr.xMax - WallT * 0.5f);
                        float z = low ? r.line + WallT * 0.5f : r.line - WallT * 0.5f;
                        var n = low ? Vector3.forward : Vector3.back;
                        spec.walls.Add(new WallSeg { a = new Vector3(a, 0, z), b = new Vector3(b, 0, z), normal = n, height = L.height });
                    }
                }
                foreach (var o in L.openings) if (o.a == room.id || o.b == room.id) spec.doors.Add(o.Center);
                room.spec = spec;
            }
        }
    }
}
