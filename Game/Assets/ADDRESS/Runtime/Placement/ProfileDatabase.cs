using System.Collections.Generic;
using System.Linq;
using Address.Core;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>All placement profiles, indexed by id and semantic group. Built by the editor ProfileBuilder.</summary>
    [CreateAssetMenu(menuName = "ADDRESS/Profile Database")]
    public class ProfileDatabase : ScriptableObject
    {
        public List<PlacementProfile> profiles = new();

        Dictionary<string, PlacementProfile> byId;
        Dictionary<string, List<PlacementProfile>> byGroup;

        void Index()
        {
            if (byId != null) return;
            byId = new(); byGroup = new();
            foreach (var p in profiles)
            {
                if (!p || p.disabled || !p.prefab) continue;
                byId[p.id] = p;
                if (!byGroup.TryGetValue(p.group, out var l)) byGroup[p.group] = l = new List<PlacementProfile>();
                l.Add(p);
            }
        }

        public void Reindex() { byId = null; byGroup = null; }

        public PlacementProfile Get(string id) { Index(); return byId.TryGetValue(id, out var p) ? p : null; }

        public IReadOnlyList<PlacementProfile> Group(string group)
        {
            Index();
            return byGroup.TryGetValue(group, out var l) ? l : (IReadOnlyList<PlacementProfile>)System.Array.Empty<PlacementProfile>();
        }

        /// <summary>Random member of a group compatible with style/mood (and optionally a room kind). Null if none fits.</summary>
        public PlacementProfile Pick(string group, Rng rng, string style, string mood, string roomKind = null)
        {
            var all = Group(group).Where(p => p.AllowsStyle(style) && p.AllowsMood(mood)).ToList();
            if (roomKind != null)
            {
                var inRoom = all.Where(p => p.rooms.Count == 0 || p.rooms.Contains(roomKind)).ToList();
                if (inRoom.Count > 0) all = inRoom;
            }
            return all.Count == 0 ? null : all[rng.Range(0, all.Count)];
        }

        public PlacementProfile PickAny(Rng rng, string style, string mood, System.Func<PlacementProfile, bool> filter)
        {
            Index();
            var all = byId.Values.Where(p => p.AllowsStyle(style) && p.AllowsMood(mood) && filter(p)).OrderBy(p => p.id).ToList();
            return all.Count == 0 ? null : all[rng.Range(0, all.Count)];
        }
    }
}
