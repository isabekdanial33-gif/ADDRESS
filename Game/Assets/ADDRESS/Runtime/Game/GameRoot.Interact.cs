using System.Collections;
using System.Linq;
using Address.Interact;
using Address.Placement;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>E-interactions: switches, seats, searching shelves, cables and computers.</summary>
    public partial class GameRoot : IInteractHost
    {
        readonly Inventory pockets = new();
        public bool botE;                    // autoplay presses E
        ComputerUI computer; Seat seatedOn; Vector3 standBackPos;
        int spawnedUid = 900000;

        public Camera Cam => cam;
        public Transform Player => player ? player.transform : null;
        public Inventory Pockets => pockets;
        public void Toast(string big, string small) => hud.Toast(big, small);
        public void PlaySfx(AudioClip c, Vector3 at, float vol = 1f) { if (c) audioDir?.PlayAt(c, at, vol); }

        bool EPressed() { bool e = input.InteractPressed || botE; botE = false; return e; }

        /// <summary>Pockets: G puts the held small thing away, 1-4 takes a pocket's thing back into the hands.</summary>
        void TickPockets()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current; if (kb == null) return;
            if (kb.gKey.wasPressedThisFrame && grab.Held)
            {
                if (pockets.Full) hud.Toast("карманы полны", "4 из 4");
                else if (grab.Held.profile.massClass != MassClass.Small) hud.Toast("не влезет в карман", Inventory.Name(grab.Held));
                else if (grab.Held.profile.group == "cable") { var c = grab.PocketHeld(); if (c) { pockets.cables++; Destroy(c.gameObject); } }
                else { var p = grab.PocketHeld(); if (p) pockets.items.Add(p); }
            }
            var keys = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key };
            for (int i = 0; i < keys.Length; i++)
            {
                if (!keys[i].wasPressedThisFrame || grab.Held) continue;
                int idx = i - pockets.cables;
                if (i < pockets.cables) { hud.Toast("кабель", "подключите им устройство: E на мониторе / клавиатуре / мыши"); continue; }
                if (idx >= 0 && idx < pockets.items.Count) { var p = pockets.items[idx]; pockets.items.RemoveAt(idx); grab.TakeOut(p); }
            }
            hud.SetPockets(pockets.Names().ToList());
        }

        /// <summary>Returns true when the interaction layer took this frame (seated / prompt shown / used).</summary>
        bool TickInteract(Ray ray, bool exitAimed)
        {
            if (computer != null) return true;
            if (fpc.Seated)
            {
                hud.Aim(false, "E", "встать");
                if (EPressed() || input.JumpPressed || input.Move.sqrMagnitude > 0.5f) StandUp();
                return true;
            }
            if (exitAimed || grab.Held || grab.Pushing) return false;
            Interactable ia = null;
            // first usable thing along the view ray within 40 cm of the first hit (the shelf behind the books on it)
            var hits = Physics.RaycastAll(ray, 2.2f, (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer), QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.distance > hits[0].distance + 0.4f) break;
                ia = h.collider.GetComponentInParent<Interactable>();
                if (ia && ia.Verb(this) != null) break; ia = null;
            }
            string verb = ia ? ia.Verb(this) : null;
            if (verb == null) return false;
            hud.Aim(true, "E", verb + (grab.Aimed && grab.Aimed.profile.massClass != MassClass.Fixed ? "     ЛКМ — взять" : ""));
            if (EPressed()) { if (fpArms) fpArms.Reach(ia.transform.position + Vector3.up * 0.3f); ia.Use(this); }
            return true;
        }

        // ------------------------------------------------------------------ seats

        public void SitOn(Seat s)
        {
            if (fpc.Seated || grab.Held) return;
            seatedOn = s; standBackPos = player.transform.position;
            var seat = s.SeatPoint; var fwd = s.po.transform.forward; fwd.y = 0; fwd.Normalize();
            float floorY = s.po.transform.position.y;
            fpc.Sit(new Vector3(seat.x, floorY, seat.z) + fwd * 0.04f, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, seat.y - floorY);
            grab.enabled = false;
            Debug.Log($"[ADDRESS] sit on {s.po.name} seat h {seat.y - floorY:F2}");
        }

        void StandUp()
        {
            if (!fpc.Seated) return;
            var s = seatedOn; Vector3 to = standBackPos;
            if (s)
            {
                var fwd = s.po.transform.forward; fwd.y = 0; fwd.Normalize();
                var front = new Vector3(s.SeatPoint.x, s.po.transform.position.y, s.SeatPoint.z) + fwd * 0.65f;
                if (!Physics.CheckCapsule(front + Vector3.up * 0.32f, front + Vector3.up * 1.45f, 0.27f, (1 << LevelBuilder.ArchLayer) | (1 << LevelBuilder.PropsLayer), QueryTriggerInteraction.Ignore)) to = front;
            }
            fpc.Stand(to); grab.enabled = true; seatedOn = null;
        }

        // ------------------------------------------------------------------ searching

        public void Search(Container c)
        {
            if (c.searched || travelling) return;
            StartCoroutine(SearchRoutine(c));
        }

        IEnumerator SearchRoutine(Container c)
        {
            c.searched = true; fpc.enabled = false; grab.enabled = false;
            PlaySfx(Sfx.Rustle, c.transform.position + Vector3.up, 0.9f);
            hud.Aim(false, "…", "ищешь");
            if (fpArms) fpArms.Reach(c.transform.position + Vector3.up * 0.4f);
            if (c.open) yield return c.open.Open();
            yield return new WaitForSeconds(c.open && c.open.HasParts ? 0.5f : 1.1f);
            fpc.enabled = state == State.Playing; grab.enabled = state == State.Playing;
            switch (c.loot)
            {
                case "cable":
                    if (c.open && c.open.HasParts) { SpawnFound(c, "cable"); hud.Toast("внутри кабель", "E — взять в карман"); break; }
                    if (pockets.Full) { hud.Toast("кабель", "карманы полны — оставил на полке"); SpawnFound(c, "cable"); }
                    else { pockets.cables++; hud.Toast("+ кабель", pockets.Describe()); }
                    break;
                case "mouse": SpawnFound(c, "mouse"); hud.Toast("нашёл мышь", "подключите её к компьютеру кабелем"); break;
                case "keyboard": SpawnFound(c, "keyboard"); hud.Toast("нашёл клавиатуру", "подключите её к компьютеру кабелем"); break;
                default: hud.Toast("пусто", "только пыль"); break;
            }
            Debug.Log($"[ADDRESS] searched {c.po.name}: '{c.loot}'");
        }

        void SpawnFound(Container c, string group)
        {
            var list = config.kit.db.Group(group); if (list == null || list.Count == 0) return;
            var p = list[0];
            var fwd = c.po.transform.forward; fwd.y = 0; fwd.Normalize();
            var pos = c.open && c.open.HasParts ? c.open.LootPoint : c.po.transform.position + fwd * (c.po.profile.size.z * 0.5f + 0.35f) + Vector3.up * 0.9f;
            var go = Instantiate(p.prefab, pos, Quaternion.LookRotation(-fwd), level.root.transform);
            go.name = p.id;
            var po = go.GetComponent<PlacedObject>() ?? go.AddComponent<PlacedObject>();
            po.profile = p; po.uid = ++spawnedUid; level.engine.placed.Add(po);
            var rb = go.AddComponent<Rigidbody>(); rb.mass = Mathf.Max(0.1f, p.mass); rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            InteractBuilder.Decorate(po);
        }

        // ------------------------------------------------------------------ computers

        public void UseComputer(ComputerStation s)
        {
            if (computer != null || s == null) return;
            computer = new ComputerUI(s);
            botE = false;
            computer.onClose = () =>
            {
                computer = null; botE = false; hud.SetVisible(true); cam.enabled = true;
                if (state == State.Playing) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; fpc.enabled = true; grab.enabled = true; }
            };
            hud.SetVisible(false); fpc.enabled = false; grab.enabled = false; cam.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = s.HasMouse;
            Debug.Log($"[ADDRESS] computer opened (kb {s.HasKeyboard}, mouse {s.HasMouse})");
        }
    }
}
