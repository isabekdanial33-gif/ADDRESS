using System.Collections.Generic;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>
    /// 2D walkability grid (0.25 m) used by the Navigation check. Cells can be RESERVED (must stay free: corridors
    /// between doors, exit approaches) and BLOCKED (occupied by a floor object up to head height).
    /// </summary>
    public class NavGrid
    {
        public readonly float cell;
        public readonly Vector2 origin;
        public readonly int w, h;
        readonly byte[] state; // bit0 = walkable floor, bit1 = reserved, bit2 = blocked

        public NavGrid(Bounds area, float cell = 0.25f)
        {
            this.cell = cell;
            origin = new Vector2(area.min.x, area.min.z);
            w = Mathf.CeilToInt(area.size.x / cell); h = Mathf.CeilToInt(area.size.z / cell);
            state = new byte[w * h];
        }

        public bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < w && z < h;
        public Vector2Int CellOf(Vector3 p) => new(Mathf.FloorToInt((p.x - origin.x) / cell), Mathf.FloorToInt((p.z - origin.y) / cell));
        public Vector3 Center(int x, int z, float y = 0) => new(origin.x + (x + 0.5f) * cell, y, origin.y + (z + 0.5f) * cell);

        public void SetWalkable(Bounds b, bool v) => ForRect(b, i => state[i] = (byte)(v ? state[i] | 1 : state[i] & ~1));
        public bool Walkable(int x, int z) => InGrid(x, z) && (state[x + z * w] & 1) != 0;
        public bool Reserved(int x, int z) => InGrid(x, z) && (state[x + z * w] & 2) != 0;
        public bool Blocked(int x, int z) => InGrid(x, z) && (state[x + z * w] & 4) != 0;
        public bool Free(int x, int z) => Walkable(x, z) && !Blocked(x, z);

        void ForRect(Bounds b, System.Action<int> f)
        {
            var a = CellOf(b.min); var c = CellOf(b.max - Vector3.one * 1e-4f);
            for (int z = Mathf.Max(0, a.y); z <= Mathf.Min(h - 1, c.y); z++)
                for (int x = Mathf.Max(0, a.x); x <= Mathf.Min(w - 1, c.x); x++) f(x + z * w);
        }

        /// <summary>Cells whose centre lies inside the oriented rectangle (plus margin).</summary>
        public IEnumerable<Vector2Int> CellsIn(Rect2 r, float margin = 0f)
        {
            var corners = r.Corners();
            Vector2 mn = corners[0], mx = corners[0];
            foreach (var c in corners) { mn = Vector2.Min(mn, c); mx = Vector2.Max(mx, c); }
            mn -= Vector2.one * margin; mx += Vector2.one * margin;
            int x0 = Mathf.FloorToInt((mn.x - origin.x) / cell), x1 = Mathf.FloorToInt((mx.x - origin.x) / cell);
            int z0 = Mathf.FloorToInt((mn.y - origin.y) / cell), z1 = Mathf.FloorToInt((mx.y - origin.y) / cell);
            float cs = Mathf.Cos(r.yawRad), sn = Mathf.Sin(r.yawRad);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!InGrid(x, z)) continue;
                    var p = new Vector2(origin.x + (x + 0.5f) * cell, origin.y + (z + 0.5f) * cell) - r.c;
                    // into rect local (inverse of yaw rotation used in Rect2.Corners)
                    float lx = p.x * cs - p.y * sn, lz = p.x * sn + p.y * cs;
                    if (Mathf.Abs(lx) <= r.half.x + margin && Mathf.Abs(lz) <= r.half.y + margin) yield return new Vector2Int(x, z);
                }
        }

        /// <summary>
        /// Remove walkability wherever architecture stands (walls, door jambs, exits): every walkable cell is tested with a
        /// physics box at body height. Cells on both sides of a wall line become solid, so paths can only cross at openings.
        /// </summary>
        public int CarveObstacles(int archMask, float floorY = 0f)
        {
            int carved = 0;
            var half = new Vector3(cell * 0.49f, 0.7f, cell * 0.49f);
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + z * w;
                    if ((state[i] & 1) == 0) continue;
                    if (Physics.CheckBox(Center(x, z, floorY + 0.95f), half, Quaternion.identity, archMask, QueryTriggerInteraction.Ignore))
                    { state[i] = (byte)(state[i] & ~1); carved++; }
                }
            return carved;
        }

        public bool TouchesReserved(Rect2 r)
        {
            foreach (var c in CellsIn(r)) if (Reserved(c.x, c.y)) return true;
            return false;
        }

        public void Block(Rect2 r) { foreach (var c in CellsIn(r)) state[c.x + c.y * w] |= 4; }
        public void Unblock(Rect2 r) { foreach (var c in CellsIn(r)) state[c.x + c.y * w] = (byte)(state[c.x + c.y * w] & ~4); }

        /// <summary>Reserve a corridor of given width along a polyline of cells found by BFS between two points.</summary>
        public bool ReservePath(Vector3 from, Vector3 to, float width)
        {
            var path = FindPath(CellOf(from), CellOf(to), ignoreBlocked: true);
            if (path == null) return false;
            int r = Mathf.Max(0, Mathf.CeilToInt((width * 0.5f) / cell) - 1);
            foreach (var p in path)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (Walkable(p.x + dx, p.y + dz)) state[(p.x + dx) + (p.y + dz) * w] |= 2;
            return true;
        }

        /// <summary>Free cell whose 8 neighbours are free too (room for a 0.28 m capsule).</summary>
        public bool Roomy(int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (!Free(x + dx, z + dz)) return false;
            return true;
        }

        public List<Vector2Int> FindPath(Vector2Int a, Vector2Int b, bool ignoreBlocked = false, bool roomy = false)
        {
            if (!Walkable(a.x, a.y) || !Walkable(b.x, b.y)) return null;
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var q = new Queue<Vector2Int>(); q.Enqueue(a); prev[a] = a;
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == b)
                {
                    var path = new List<Vector2Int>(); var p = b;
                    while (p != a) { path.Add(p); p = prev[p]; }
                    path.Add(a); path.Reverse(); return path;
                }
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (prev.ContainsKey(n) || !Walkable(n.x, n.y)) continue;
                    if (!ignoreBlocked && Blocked(n.x, n.y)) continue;
                    if (roomy && n != b && !Roomy(n.x, n.y)) continue;
                    prev[n] = c; q.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>Fraction of walkable cells reachable from a start cell without crossing blocked cells.</summary>
        public float ReachableFraction(Vector3 start, out int unreachable)
        {
            var s = CellOf(start); unreachable = 0;
            if (!Free(s.x, s.y)) return 0;
            var seen = new bool[w * h]; var q = new Queue<Vector2Int>(); q.Enqueue(s); seen[s.x + s.y * w] = true;
            int reach = 0, total = 0;
            for (int i = 0; i < w * h; i++) if ((state[i] & 1) != 0 && (state[i] & 4) == 0) total++;
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (q.Count > 0)
            {
                var c = q.Dequeue(); reach++;
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (!Free(n.x, n.y) || seen[n.x + n.y * w]) continue;
                    seen[n.x + n.y * w] = true; q.Enqueue(n);
                }
            }
            unreachable = total - reach;
            return total == 0 ? 1 : (float)reach / total;
        }
    }
}
