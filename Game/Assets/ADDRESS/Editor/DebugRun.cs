using System.Linq;
using Address.Core;
using Address.World;
using UnityEngine;

namespace Address.EditorTools
{
    /// <summary>batch: build one Level Run corridor and report why its exit is (not) reachable.</summary>
    public static class DebugRun
    {
        public static void Run()
        {
            var lv = new LevelBuilder(LevelFarm.Kit()).Build(new LevelAddress("RN", Hashing.Fnv64("debug run")));
            var L = lv.layout;
            Debug.Log($"[ADDRESS] dbg RN: rooms {L.rooms.Count} openings {L.openings.Count} ({string.Join(",", L.openings.Select(o => o.kind + "@" + o.line + (o.vertical ? "v" : "h") + "[" + o.from + "-" + o.to + "]"))})");
            Debug.Log($"[ADDRESS] dbg RN: room0 {L.rooms[0].World} room1 {L.rooms[1].World} start {lv.start} yaw {lv.startYaw}");
            foreach (var e in lv.exits) Debug.Log($"[ADDRESS] dbg RN: exit {e.form} at {e.position} n {e.normal} reachable {e.reachable}");
            var nav = lv.nav; var s = nav.CellOf(lv.start);
            Debug.Log($"[ADDRESS] dbg RN: start walkable {nav.Walkable(s.x, s.y)} free {nav.Free(s.x, s.y)} reach {nav.ReachableFraction(lv.start, out int un):F2} unreachable {un}");
            // walk along the corridor centre and report the first blocked cell and what is there
            for (float z = lv.start.z; z < L.rooms[L.rooms.Count - 1].World.yMax; z += 0.25f)
            {
                var p = new Vector3(lv.start.x, 0, z); var c = nav.CellOf(p);
                if (!nav.Walkable(c.x, c.y) || nav.Blocked(c.x, c.y))
                {
                    var cols = Physics.OverlapBox(p + Vector3.up, new Vector3(0.3f, 0.9f, 0.3f));
                    Debug.Log($"[ADDRESS] dbg RN: blocked at z {z:F2} walkable {nav.Walkable(c.x, c.y)} blocked {nav.Blocked(c.x, c.y)} colliders [{string.Join(",", cols.Select(k => k.name + "/" + k.transform.root.name))}]");
                    break;
                }
            }
            Object.DestroyImmediate(lv.root);
        }
    }
}
