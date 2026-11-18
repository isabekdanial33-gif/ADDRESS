using System;
using System.Collections.Generic;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>The nine checks of the placement pipeline (+ extra failure reasons), used for statistics and reports.</summary>
    public enum Check { None, Bounds, Collision, Surface, Support, Rotation, Semantic, Style, Density, Navigation, Clearance, Budget }

    /// <summary>Straight wall segment of a room, seen from inside (normal points into the room).</summary>
    [Serializable]
    public struct WallSeg
    {
        public Vector3 a, b;      // on the floor line, inner face
        public Vector3 normal;    // into the room
        public float height;
        public float Length => Vector3.Distance(a, b);
        public Vector3 Dir => (b - a).normalized;
    }

    /// <summary>Description of a room the engine places into.</summary>
    public class RoomSpec
    {
        public string name;
        public string kind;                 // office / storage / lounge / pool / corridor / utility ...
        public Bounds bounds;               // inner volume (floor at bounds.min.y, ceiling at bounds.max.y)
        public List<WallSeg> walls = new();
        public List<Vector3> doors = new(); // door centres on the floor (for nav reservation)
        public List<Rect> keepOut = new();  // XZ areas nothing may stand in (pool basins)
        public float FloorY => bounds.min.y;
        public float CeilingY => bounds.max.y;
    }

    /// <summary>Candidate pose of an object: pivot (bottom centre) position, rotation, scale.</summary>
    public struct Pose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
        public Pose(Vector3 p, Quaternion r, float s = 1f) { position = p; rotation = r; scale = s; }
    }

    public class PlacementStats
    {
        public int requested, placed, skipped, candidatesTried;
        public readonly Dictionary<Check, int> rejections = new();
        public void Reject(Check c) { rejections.TryGetValue(c, out int n); rejections[c] = n + 1; }
        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var kv in rejections) parts.Add($"{kv.Key}:{kv.Value}");
            return $"requested {requested} placed {placed} skipped {skipped} candidates {candidatesTried} rejections [{string.Join(", ", parts)}]";
        }
    }

    /// <summary>Oriented rectangle on the XZ plane (used for clearance zones and navigation reservation).</summary>
    public struct Rect2
    {
        public Vector2 c, half; public float yawRad;
        public Rect2(Vector2 c, Vector2 half, float yawRad) { this.c = c; this.half = half; this.yawRad = yawRad; }

        public Vector2[] Corners()
        {
            float cs = Mathf.Cos(yawRad), sn = Mathf.Sin(yawRad);
            Vector2 ax = new(cs, -sn), az = new(sn, cs);
            return new[] { c + ax * half.x + az * half.y, c - ax * half.x + az * half.y, c - ax * half.x - az * half.y, c + ax * half.x - az * half.y };
        }

        /// <summary>Separating-axis test between two oriented rectangles.</summary>
        public bool Overlaps(Rect2 o, float margin = 0f)
        {
            var A = Corners(); var B = o.Corners();
            foreach (var axes in new[] { A, B })
                for (int i = 0; i < 2; i++)
                {
                    var e = axes[i + 1] - axes[i]; var n = new Vector2(-e.y, e.x).normalized;
                    Project(A, n, out float a0, out float a1); Project(B, n, out float b0, out float b1);
                    if (a1 + margin <= b0 || b1 + margin <= a0) return false;
                }
            return true;
        }

        static void Project(Vector2[] p, Vector2 n, out float mn, out float mx)
        {
            mn = mx = Vector2.Dot(p[0], n);
            for (int i = 1; i < p.Length; i++) { float d = Vector2.Dot(p[i], n); mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d); }
        }
    }
}
