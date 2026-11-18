using System;
using System.Collections.Generic;
using UnityEngine;

namespace Address.Placement
{
    public enum SupportType { Floor, Surface, Wall, Ceiling, Inside }
    public enum RotationRule { Free, Step90, Step45, FaceWall, FaceAnchor, Fixed }
    public enum MassClass { Small, Medium, Heavy, Fixed }
    public enum ObjectState { Normal, Fallen, Tilted, Broken }

    [Serializable]
    public struct LocalBox
    {
        public Vector3 center;
        public Vector3 size;
    }

    /// <summary>
    /// Horizontal area that can carry other objects (desk top, shelf board, seat), extracted from the mesh.
    /// Cells are on a regular XZ grid in the object's local space (pivot = bottom centre).
    /// </summary>
    [Serializable]
    public class SurfaceRegion
    {
        public string kind = "top";      // "top" (nothing above) or "shelf" (covered) or "seat"
        public float height;             // local Y of the surface
        public float clearance;          // free height above the surface
        public float cell;               // grid cell size (m)
        public Vector2 origin;           // local XZ of cell (0,0) min corner
        public int w, h;
        public List<int> cells = new();  // indices (x + z*w) of supported cells
        public float area;

        [NonSerialized] HashSet<int> _set;
        public bool Has(int x, int z)
        {
            if (x < 0 || z < 0 || x >= w || z >= h) return false;
            _set ??= new HashSet<int>(cells);
            return _set.Contains(x + z * w);
        }

        /// <summary>True if the local XZ rectangle (with margin) lies entirely on supported cells.</summary>
        public bool ContainsRect(Vector2 min, Vector2 max)
        {
            int x0 = Mathf.FloorToInt((min.x - origin.x) / cell), x1 = Mathf.CeilToInt((max.x - origin.x) / cell) - 1;
            int z0 = Mathf.FloorToInt((min.y - origin.y) / cell), z1 = Mathf.CeilToInt((max.y - origin.y) / cell) - 1;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) if (!Has(x, z)) return false;
            return true;
        }

        public Vector2 CellCenter(int index) => origin + new Vector2((index % w + 0.5f) * cell, (index / w + 0.5f) * cell);
    }

    /// <summary>
    /// Everything the generator needs to know to place one model logically.
    /// Geometry fields are computed automatically by the editor ProfileBuilder;
    /// semantic fields come from Data/profiles.json.
    /// </summary>
    public class PlacementProfile : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public GameObject prefab;
        public string group;                 // semantic group ("chair", "desk", "paper") - used by Echo "similar object"

        [Header("Geometry (auto)")]
        public Vector3 size;                 // AABB size in local space, pivot = bottom centre
        public Vector3 centerOfMass;         // local
        public List<LocalBox> boxes = new(); // compound collider approximation
        public List<SurfaceRegion> surfaces = new();
        public Vector2[] footprint;          // convex hull of the floor-contact area (local XZ)

        [Header("Placement rules (semantic)")]
        public SupportType support = SupportType.Floor;
        public RotationRule rotation = RotationRule.Free;
        public float maxTiltDeg = 3f;
        public float minSpacing = 0.05f;     // Poisson radius extra to bounds
        public float clearanceFront;         // free space in front (+Z) that must stay empty (chairs, cabinets)
        public float wallGap = 0.01f;        // distance from wall when FaceWall
        public MassClass massClass = MassClass.Small;
        public float mass = 1f;
        public bool canStack;                // same object may stand on itself (boxes)
        public bool openable;                // container with lazy contents
        public float scaleJitter;            // allowed uniform scale variation (0..0.05)
        public float wallMin = 1.2f, wallMax = 1.9f;
        public bool disabled;

        [Header("Tags")]
        public List<string> styles = new();  // "backrooms","poolrooms","frutiger",...
        public List<string> moods = new() { "neutral" }; // light / neutral / dark
        public List<string> rooms = new();   // "office","storage","lounge","pool",...

        [Header("States")]
        public float fallenChance;           // can appear lying (chairs, bins) when a scenario allows mess
        public Vector3 fallenEuler;          // local rotation for the fallen pose (pivot recomputed at runtime)

        public float Footprint => size.x * size.z;

        public bool AllowsStyle(string style) => styles.Count == 0 || styles.Contains(style);
        public bool AllowsMood(string levelMood)
        {
            // light levels never get dark objects; dark levels accept everything
            if (levelMood == "light") return !moods.Contains("dark");
            if (levelMood == "neutral") return !moods.Contains("dark") || moods.Contains("neutral");
            return true;
        }
    }
}
