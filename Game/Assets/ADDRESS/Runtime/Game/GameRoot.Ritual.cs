using System.Collections;
using System.Linq;
using Address.Core;
using Address.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Address.Game
{
    /// <summary>
    /// The Ritual of the Mountain King (Backrooms wiki: Phenomenon 35 / Entity 51): sit on the floor, clap thirteen
    /// times, mock the King, hit the nearest wall, close your eyes and wait for the music. "In the Hall of the Mountain
    /// King" plays faster and faster and everything here hunts you faster with it. Survive to the last chord and you
    /// noclip into another place. Abuse it - or cheat - and you end up in "YOU CHEATED".
    ///
    /// Cheat codes (typed while playing): NOCLIP, GODMODE, SKIPLEVEL, SPEEDY, MOTHERLODE. Each one is noticed;
    /// three strikes (cheats, rituals done too often, falling out of the world) and the Backrooms send you away.
    /// </summary>
    public partial class GameRoot
    {
        enum Rite { None, Clapping, Mock, Punch, Eyes, Music }
        Rite rite = Rite.None; int claps; float riteT; AudioSource riteMusic; int ritesDone; float lastRiteTime = -999f;
        public static int CheatStrikes;
        float godUntil, speedUntil, noclipUntil; string typed = ""; bool sendingAway;
        static AudioClip clapClip, thudClip;

        void TickRitual()
        {
            var kb = Keyboard.current; if (kb == null || level == null || travelling) return;
            bool still = fpc.Velocity.sqrMagnitude < 0.05f;
            bool sitting = input.Crouch && still;
            riteT += Time.deltaTime;
            switch (rite)
            {
                case Rite.None:
                case Rite.Clapping:
                    if (kb.hKey.wasPressedThisFrame && sitting)
                    {
                        if (rite == Rite.None) { rite = Rite.Clapping; claps = 0; }
                        claps++; riteT = 0;
                        clapClip ??= Resources.Load<AudioClip>("EntitySfx/step_hard_2");
                        if (clapClip) audioDir?.Play(clapClip, 0.7f);
                        hud.Toast(claps < 13 ? $"хлопок {claps}/13" : "тринадцать", claps < 13 ? "сидя на полу, H - хлопать" : "");
                        if (claps >= 13)
                        {
                            rite = Rite.Mock; riteT = 0;
                            hud.Say("Ты", "Горный Король! Ты жалкий трус! Выходи, если не боишься!", 3.5f);
                        }
                    }
                    else if (rite == Rite.Clapping && (riteT > 6f || !input.Crouch)) { rite = Rite.None; hud.Toast("ритуал прерван", "надо сидеть и хлопать без перерыва"); }
                    break;
                case Rite.Mock:
                    if (riteT > 3.6f) { rite = Rite.Punch; riteT = 0; hud.Toast("ударь стену", "подойди вплотную к стене и нажми H"); }
                    break;
                case Rite.Punch:
                    if (kb.hKey.wasPressedThisFrame && Physics.Raycast(cam.transform.position, cam.transform.forward, 1.4f, 1 << LevelBuilder.ArchLayer, QueryTriggerInteraction.Ignore))
                    {
                        thudClip ??= Resources.Load<AudioClip>("EntitySfx/scare_2");
                        if (thudClip) audioDir?.Play(thudClip, 0.6f);
                        if (fpArms) fpArms.Reach(cam.transform.position + cam.transform.forward);
                        rite = Rite.Eyes; riteT = 0; StartCoroutine(CloseEyes());
                    }
                    else if (riteT > 25f) { rite = Rite.None; hud.Toast("ритуал прерван", ""); }
                    break;
                case Rite.Music:
                    if (riteMusic)
                    {
                        // faster and faster: the music and everything that hunts you
                        float k = Mathf.Clamp01(riteMusic.time / Mathf.Max(1f, riteMusic.clip.length));
                        riteMusic.pitch = Mathf.Lerp(1f, 1.45f, k * k);
                        Address.Entities.Stalker.GlobalSpeedMul = Mathf.Lerp(1f, 1.9f, k);
                        if ((!riteMusic.isPlaying && riteT > 5f) || riteT > 160f) { EndRitual(true); }
                    }
                    break;
            }
        }

        IEnumerator CloseEyes()
        {
            hud.Toast("закрой глаза", "и жди музыку");
            yield return Fade(1f, 1.2f);
            yield return new WaitForSeconds(2.2f);
            // abusing it: the third ritual in a short time is cheating
            ritesDone = Time.time - lastRiteTime < 600f ? ritesDone + 1 : 1; lastRiteTime = Time.time;
            if (ritesDone >= 3) { rite = Rite.None; Strike("ритуал горного короля слишком часто"); yield return Fade(0f, 0.5f); yield break; }
            var clip = Resources.Load<AudioClip>("Ambience/mountain_king");
            if (!riteMusic) { riteMusic = gameObject.AddComponent<AudioSource>(); riteMusic.spatialBlend = 0; riteMusic.loop = false; }
            riteMusic.clip = clip; riteMusic.pitch = 1f; riteMusic.volume = 0.8f; if (clip) riteMusic.Play();
            rite = Rite.Music; riteT = 0;
            // the King's servants come for you, already awake
            for (int i = 0; i < 2; i++)
            {
                var p = player.transform.position - player.transform.forward * (10f + i * 4f);
                var c = level.nav.CellOf(p); if (!level.nav.Free(c.x, c.y)) p = level.start;
                var kind = i == 0 ? Address.Entities.EntityKind.Hound : Address.Entities.EntityKind.Rake;
                var e = Address.Entities.EntitySpawner.SpawnKind(level, config.bodyPrefab, kind, p, Quaternion.identity, null);
                e.wakeDelay = 1.5f; e.targets = EntityTargets; e.isLookedAtBy = LooksAt(e); e.onCatch = _ => { EndRitual(false); Caught(e); };
                entities.Add(e);
            }
            yield return Fade(0f, 0.6f);
            hud.Toast("В ПЕЩЕРЕ ГОРНОГО КОРОЛЯ", "доживи до последнего аккорда");
        }

        void EndRitual(bool survived)
        {
            if (riteMusic) riteMusic.Stop();
            Address.Entities.Stalker.GlobalSpeedMul = 1f;
            rite = Rite.None;
            if (!survived || level == null) return;
            // survived: noclip through the floor into somewhere else
            hud.Toast("ты прошёл сквозь стену", "Король отпустил тебя");
            var seed = Hashing.Combine(level.address.Seed, (ulong)(Time.frameCount * 7919));
            var worlds = new[] { "BR", "PD", "WH", "OF", "HT", "LO", "PG", "R2", "MN", "SC" };
            StartCoroutine(Enter(book.NextFree(worlds[(int)(seed % (ulong)worlds.Length)], seed), level.address));
        }

        // ------------------------------------------------------------------ cheats

        void TickCheats()
        {
            if (level == null) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                for (var k = Key.A; k <= Key.Z; k++)
                    if (kb[k].wasPressedThisFrame) { typed += (char)('A' + (k - Key.A)); if (typed.Length > 12) typed = typed.Substring(typed.Length - 12); }
                string Hit(params string[] codes) { foreach (var c in codes) if (typed.EndsWith(c)) { typed = ""; return c; } return null; }
                var code = Hit("NOCLIP", "GODMODE", "SKIPLEVEL", "SPEEDY", "MOTHERLODE");
                if (code != null) Cheat(code);
            }
            // effects
            bool noclip = Time.time < noclipUntil;
            Physics.IgnoreLayerCollision(player.layer, LevelBuilder.ArchLayer, noclip);
            fpc.gravity = noclip ? 0f : 18f;
            fpc.speedMultiplier = Time.time < speedUntil ? 2f : grab.Pushing ? fpc.speedMultiplier : 1f;
        }

        void Cheat(string code)
        {
            {
                switch (code)
                {
                    case "NOCLIP": noclipUntil = Time.time + 12f; hud.Toast("чит: NOCLIP", "стены тебя пропускают... пока"); break;
                    case "GODMODE": godUntil = Time.time + 60f; hud.Toast("чит: GODMODE", "минуту тебя не поймают"); break;
                    case "SPEEDY": speedUntil = Time.time + 30f; hud.Toast("чит: SPEEDY", "бег x2"); break;
                    case "MOTHERLODE": pockets.cables = Mathf.Min(4, pockets.cables + 2); hud.Toast("чит: MOTHERLODE", "+2 кабеля"); break;
                    case "SKIPLEVEL":
                        var ex = level.exits.FirstOrDefault(e => e.reachable && e.role == ExitRole.Forward && e.instance);
                        if (ex != null) { ex.instance.Activate(); Travel(ex.instance); }
                        break;
                }
                Strike("чит " + code);
            }
        }

        bool GodMode => Time.time < godUntil;

        /// <summary>The Backrooms notice: three strikes and you are taken to YOU CHEATED.</summary>
        void Strike(string why)
        {
            CheatStrikes++;
            Debug.Log($"[ADDRESS] cheat strike {CheatStrikes}: {why}");
            if (CheatStrikes >= 3 && level != null && level.aesthetic != "YC" && !sendingAway) { sendingAway = true; StartCoroutine(SentToCheated()); }
            else if (CheatStrikes == 2) hud.Say("???", "Мы видим, что ты делаешь.", 3f);
        }

        /// <summary>-gametest: the ritual, the cheats, YOU CHEATED and being caught, played through without a keyboard.</summary>
        IEnumerator GameTest()
        {
            yield return new WaitUntil(() => !travelling && level != null);
            Play(new LevelAddress("BR", Hashing.Fnv64("game test")));
            yield return new WaitForSeconds(0.3f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);
            var a0 = level.address;
            Debug.Log($"[ADDRESS] gametest: start {a0} state {state}");
            // 1. the ritual: eyes closed -> the King's music -> his servants come -> survive -> another place
            rite = Rite.Eyes; StartCoroutine(CloseEyes());
            yield return new WaitForSeconds(3.8f);
            Debug.Log($"[ADDRESS] gametest ritual: rite {rite} music {(riteMusic && riteMusic.clip ? riteMusic.clip.name : "none")} servants {entities.Count(e => e && (e.kind == Address.Entities.EntityKind.Hound || e.kind == Address.Entities.EntityKind.Rake))}");
            if (rite == Rite.Music) EndRitual(true);
            yield return new WaitForSeconds(0.3f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.3f);
            Debug.Log($"[ADDRESS] gametest ritual survived: {a0} -> {level.address} speedMul {Address.Entities.Stalker.GlobalSpeedMul}");
            // 2. cheats
            int s0 = CheatStrikes;
            Cheat("NOCLIP"); yield return null; yield return null;
            Debug.Log($"[ADDRESS] gametest NOCLIP: walls ignored {Physics.GetIgnoreLayerCollision(player.layer, LevelBuilder.ArchLayer)} gravity {fpc.gravity} strikes {CheatStrikes}");
            Cheat("GODMODE"); yield return null;
            Debug.Log($"[ADDRESS] gametest GODMODE: {GodMode} strikes {CheatStrikes}");
            int cab = pockets.cables; Cheat("MOTHERLODE"); yield return null;
            Debug.Log($"[ADDRESS] gametest MOTHERLODE: cables {cab} -> {pockets.cables} strikes {CheatStrikes}");
            // the third strike: off to YOU CHEATED
            yield return new WaitForSeconds(0.9f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);
            Debug.Log($"[ADDRESS] gametest strikes {s0}->3: now in {level.address} ({level.aesthetic}) strikes reset {CheatStrikes} noclip {Physics.GetIgnoreLayerCollision(player.layer, LevelBuilder.ArchLayer)} entities {entities.Count}");
            {
                var dir = System.IO.Path.Combine(Application.dataPath, "..", "shots"); System.IO.Directory.CreateDirectory(dir);
                var rt = new RenderTexture(1280, 720, 24); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "95_you_cheated.png"), tex.EncodeToPNG());
            }
            // 3. caught: a creature right next to you -> wake in the next Echo of the place
            godUntil = 0;
            StartCoroutine(Enter(new LevelAddress("BS", Hashing.Fnv64("game test catch")), null));
            yield return new WaitForSeconds(0.3f); yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.5f);
            foreach (var e0 in entities) if (e0) Destroy(e0.gameObject);
            entities.Clear();
            var before = level.address;
            var fwd = Quaternion.Euler(0, fpc.transform.eulerAngles.y, 0) * Vector3.forward;
            var sp = Address.Entities.EntitySpawner.SpawnKind(level, config.bodyPrefab, Address.Entities.EntityKind.Spider, player.transform.position + fwd * 3f, Quaternion.LookRotation(-fwd), null);
            sp.wakeDelay = 0; sp.targets = EntityTargets; sp.isLookedAtBy = LooksAt(sp); sp.onCatch = _ => Caught(sp); entities.Add(sp);
            float tw = 0; while (level.address.Equals(before) && tw < 15f) { tw += Time.deltaTime; yield return null; }
            yield return new WaitUntil(() => !travelling); yield return new WaitForSeconds(0.4f);
            Debug.Log($"[ADDRESS] gametest caught after {tw:F1}s: {before} -> {level.address} (generation {before.Generation} -> {level.address.Generation})");
            Debug.Log("[ADDRESS] gametest done");
            Application.Quit();
        }

        IEnumerator SentToCheated()
        {
            yield return new WaitForSeconds(0.8f);
            yield return new WaitUntil(() => !travelling);
            travelling = true;
            noclipUntil = 0; godUntil = 0; speedUntil = 0;
            Physics.IgnoreLayerCollision(player.layer, LevelBuilder.ArchLayer, false); fpc.gravity = 18f;
            if (riteMusic) riteMusic.Stop(); rite = Rite.None; Address.Entities.Stalker.GlobalSpeedMul = 1f;
            travelling = false;
            yield return Enter(new LevelAddress("YC", Hashing.Combine(Hashing.Fnv64("you cheated"), (ulong)System.DateTime.Now.Ticks)), level?.address);
            hud.Toast("ТЫ СЧИТЕРИЛ", "кто-то идёт");
            CheatStrikes = 0; sendingAway = false;
        }
    }
}
