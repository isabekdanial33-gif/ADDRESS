using System.Collections;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>
    /// Behind an open vent hatch there is a real duct: a narrow metal shaft with a bend and a few dim lamps, built away
    /// from the level. The explorer has to crawl through it; light at the far end is the way into the next place.
    /// </summary>
    public partial class GameRoot
    {
        bool ductDone;   // the duct was crawled: the next Travel through this hatch really leaves

        IEnumerator DuctRoutine(ExitTrigger exit)
        {
            travelling = true;
            audioDir?.PlayAt(config.audio?.vent, exit.transform.position + Vector3.up * 0.4f, 0.8f);
            yield return Fade(1f, 0.35f);
            var root = BuildDuct(out var start, out var end, out float startYaw);
            var cc = player.GetComponent<CharacterController>();
            fpc.ForceCrawl = true;
            cc.enabled = false; player.transform.position = start; cc.enabled = true;
            fpc.SetLook(startYaw, 0);
            for (int i = 0; i < 20; i++) { fpc.Tick(0.05f); }   // drop to crawling height before the screen opens
            yield return Fade(0f, 0.5f);
            travelling = false;
            hud.Toast("вентиляция", "ползи к свету");
            float t = 0;
            while (t < 120f && level != null)
            {
                t += Time.deltaTime;
                var p = player.transform.position;
                if (new Vector2(p.x - end.x, p.z - end.z).magnitude < 0.9f) break;
                yield return null;
            }
            fpc.ForceCrawl = false;
            if (level == null || !exit) yield break;
            ductDone = true; travelling = false;
            Travel(exit);
            ductDone = false;
            if (root) Destroy(root, 3f);
        }

        /// <summary>The shaft: 0.9 m wide, 0.75 m high, two legs with a bend, built 60 m above the level.</summary>
        GameObject BuildDuct(out Vector3 start, out Vector3 end, out float startYaw)
        {
            var root = new GameObject("Duct"); root.transform.SetParent(level.root.transform, false);
            var o = new Vector3(level.start.x, 60f, level.start.z);   // above the level (anything below -5 m counts as falling out of the world)
            const float W = 0.9f, H = 0.75f, T = 0.05f;
            var mat = config.kit.pipeMat ? config.kit.pipeMat : config.kit.wallMat;
            int arch = LevelBuilder.ArchLayer;
            void Box(Vector3 c, Vector3 size)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "duct"; g.layer = arch;
                g.transform.SetParent(root.transform, false); g.transform.position = c; g.transform.localScale = size;
                g.GetComponent<Renderer>().sharedMaterial = mat;
            }
            // leg A along +Z (6 m), leg B along +X (5 m) from A's end
            float la = 6f, lb = 5f;
            // floor and ceiling of both legs
            Box(o + new Vector3(0, -T / 2, la / 2), new Vector3(W + 2 * T, T, la + W));
            Box(o + new Vector3(0, H + T / 2, la / 2), new Vector3(W + 2 * T, T, la + W));
            Box(o + new Vector3(lb / 2 + W / 2, -T / 2, la), new Vector3(lb, T, W + 2 * T));
            Box(o + new Vector3(lb / 2 + W / 2, H + T / 2, la), new Vector3(lb, T, W + 2 * T));
            // walls of leg A (the left wall runs on past the bend), back wall
            Box(o + new Vector3(-W / 2 - T / 2, H / 2, la / 2), new Vector3(T, H, la + W + 2 * T));
            Box(o + new Vector3(W / 2 + T / 2, H / 2, (la - W / 2) / 2 - W / 4), new Vector3(T, H, la - W / 2 - W / 2));
            Box(o + new Vector3(0, H / 2, -W / 2 - T / 2), new Vector3(W, H, T));
            // walls of leg B
            Box(o + new Vector3(lb / 2, H / 2, la + W / 2 + T / 2), new Vector3(lb + W, H, T));
            Box(o + new Vector3(lb / 2 + W / 2, H / 2, la - W / 2 - T / 2), new Vector3(lb, H, T));
            // the far end: a bright square of light
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(root.transform, false);
            glow.transform.position = o + new Vector3(lb + W / 2 + 0.02f, H / 2, la); glow.transform.rotation = Quaternion.LookRotation(Vector3.right);
            glow.transform.localScale = new Vector3(W, H, 1);
            if (config.kit.lightMat) glow.GetComponent<Renderer>().sharedMaterial = config.kit.lightMat;
            Box(o + new Vector3(lb + W / 2 + 0.1f, H / 2, la), new Vector3(0.1f, H, W));
            // a few dim lamps: one in the middle of A, one at the bend, daylight at the end
            void Lamp(Vector3 p, Color c, float i, float r)
            {
                var lg = new GameObject("ductLight"); lg.transform.SetParent(root.transform, false); lg.transform.position = p;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = r; l.shadows = LightShadows.None;
            }
            Lamp(o + new Vector3(0, H - 0.1f, 2.5f), new Color(1f, 0.75f, 0.45f), 0.8f, 3f);
            Lamp(o + new Vector3(0.3f, H - 0.1f, la), new Color(1f, 0.6f, 0.35f), 0.4f, 2.2f);
            Lamp(o + new Vector3(lb - 0.4f, H * 0.5f, la), new Color(0.95f, 0.97f, 1f), 1.1f, 3f);
            start = o + new Vector3(0, 0.02f, 0.1f);
            end = o + new Vector3(lb - 0.3f, 0, la);
            startYaw = 0f;
            Physics.SyncTransforms();
            return root;
        }
    }
}
