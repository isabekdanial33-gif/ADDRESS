using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Address.Placement;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    [Serializable]
    public class PhotoRecord
    {
        public string file, address, date; public int score; public List<string> subjects = new();
    }

    [Serializable]
    public class PhotoAlbum
    {
        public List<PhotoRecord> photos = new();
        static string Dir => Path.Combine(Application.persistentDataPath, "photos");
        static string Index => Path.Combine(Dir, "album.json");
        public static PhotoAlbum Load() { try { if (File.Exists(Index)) return JsonUtility.FromJson<PhotoAlbum>(File.ReadAllText(Index)); } catch { } return new PhotoAlbum(); }
        public void Save() { Directory.CreateDirectory(Dir); File.WriteAllText(Index, JsonUtility.ToJson(this, true)); }
        public static string NewFile() { Directory.CreateDirectory(Dir); return Path.Combine(Dir, $"photo_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"); }
    }

    /// <summary>
    /// The camera in the player's hands. A photo is scored by what it shows:
    /// sum over visible subjects of rarity x how much of the frame it fills x how well it is framed (thirds/centre),
    /// with a bonus for exits, intentional anomalies and things the Echo changed. Only photos enter the Archive.
    /// </summary>
    public static class PhotoScoring
    {
        public static int Rarity(PlacedObject po)
        {
            if (po.intentionalAnomaly) return 40;
            if (po.placedBy != null && po.placedBy.StartsWith("echo/")) return 15;
            switch (po.profile.group) { case "toy": case "camera": return 12; case "tv": case "kettle": case "clock": return 4; default: return 1; }
        }

        public static (int score, List<string> subjects) Score(Camera cam, LevelBuilder.Result level)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            float total = 0; var subj = new List<string>();
            void Consider(Bounds b, Transform t, int rarity, string name)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, b)) return;
                var c = b.center; var dir = c - cam.transform.position; float dist = dir.magnitude;
                if (dist > 18f) return;
                // occlusion: something solid between lens and subject (ignore the subject itself)
                if (Physics.Raycast(cam.transform.position, dir.normalized, out var h, dist - b.extents.magnitude * 0.6f, (1 << 8) | (1 << 9)) && !h.transform.IsChildOf(t)) return;
                var vp = cam.WorldToViewportPoint(c);
                float size = Mathf.Clamp01(b.extents.magnitude * 2f / (dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f));
                float thirds = Mathf.Min(Mathf.Abs(vp.x - 1 / 3f), Mathf.Abs(vp.x - 2 / 3f), Mathf.Abs(vp.x - 0.5f)) + Mathf.Min(Mathf.Abs(vp.y - 1 / 3f), Mathf.Abs(vp.y - 2 / 3f), Mathf.Abs(vp.y - 0.5f));
                float framing = Mathf.Lerp(1.4f, 0.6f, Mathf.Clamp01(thirds * 3f));
                float s = rarity * (10f + 90f * size) * framing;
                if (s > 25) subj.Add(name);
                total += s;
            }
            foreach (var po in level.engine.placed)
            {
                if (!po) continue;
                var rs = po.GetComponentsInChildren<Renderer>(); if (rs.Length == 0 || !rs[0].enabled) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Consider(b, po.transform, Rarity(po), po.profile.id);
            }
            foreach (var e in level.exits)
                if (e.instance) Consider(new Bounds(e.position + Vector3.up * 1f, new Vector3(e.width, 2f, 0.3f)), e.instance.transform, e.instance.IsOpen ? 60 : 25, "exit:" + e.form);
            return (Mathf.RoundToInt(total / 10f), subj.Distinct().Take(8).ToList());
        }
    }
}
