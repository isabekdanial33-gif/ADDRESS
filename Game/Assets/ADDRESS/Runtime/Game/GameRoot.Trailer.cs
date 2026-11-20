using System.Collections;
using System.IO;
using System.Linq;
using Address.Core;
using Address.World;
using UnityEngine;

namespace Address.Game
{
    /// <summary>-trailer: a scripted ~70 s cut of the game rendered to trailer_frames/ (30 fps), encoded afterwards.</summary>
    public partial class GameRoot
    {
        IEnumerator Trailer()
        {
            vdir = Path.Combine(Application.dataPath, "..", "trailer_frames");
            if (Directory.Exists(vdir)) Directory.Delete(vdir, true);
            Directory.CreateDirectory(vdir);
            Time.captureFramerate = 30;
            vrt = new RenderTexture(1280, 720, 24); vtex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            yield return new WaitUntil(() => !travelling && level != null);
            OverlayToCamera();
            StartCoroutine(Recorder());
            // title over the menu room
            yield return Wait(3f);
            Play(new LevelAddress("BR", Hashing.Fnv64("trailer 0")));
            fpc.enabled = false;
            yield return new WaitUntil(() => !travelling);
            hud.Toast("ADDRESS", "каждое место уже существует");
            yield return Pan(4f, 25f);

            // the yellow rooms, then something walks towards you
            yield return Creature(Address.Entities.EntityKind.Smiler, 3.5f);
            hud.Toast("ОНИ ЗДЕСЬ", ""); yield return Creature(Address.Entities.EntityKind.Hound, 3f);
            yield return Creature(Address.Entities.EntityKind.Clark, 3.5f);

            // Verity
            {
                var fwd = Quaternion.Euler(0, level.startYaw, 0) * Vector3.forward;
                var v = Address.Entities.Verity.Spawn(level, level.start + fwd * 2.2f + Vector3.up * 1.2f, false);
                if (v) { v.viewer = cam.transform; v.monsters = () => entities; v.say = (w, l, s) => hud.Say(w, l, s); fpc.SetLook(level.startYaw, 0); yield return Wait(1f); v.Talk(); yield return Wait(4.5f); Destroy(v.gameObject); }
            }

            // montage of worlds
            hud.Toast("ДЕСЯТКИ УРОВНЕЙ", "Level 0, 1, 2, 3, 4, 5, 6, 8, 13, 33, 37, 52, !, Хаб...");
            foreach (var w in new[] { "RN", "PD", "OF", "HT", "SC", "HO", "MN", "CV", "LB", "SB", "LA", "PR" })
            {
                StartCoroutine(Enter(new LevelAddress(w, Hashing.Fnv64("trailer " + w)), null));
                yield return Wait(0.2f); yield return new WaitUntil(() => !travelling);
                fpc.enabled = false;
                yield return w == "RN" ? Dolly(3f) : Pan(2.3f, 18f);
            }

            // fear
            StartCoroutine(Enter(new LevelAddress("LO", Hashing.Fnv64("trailer fear")), null));
            yield return Wait(0.2f); yield return new WaitUntil(() => !travelling); fpc.enabled = false;
            hud.Toast("НЕ ОТВОРАЧИВАЙСЯ", "");
            fearFx.target = 1f; yield return Creature(Address.Entities.EntityKind.Rake, 3.5f); fearFx.target = 0f;

            // the ritual
            hud.Toast("РИТУАЛ ГОРНОГО КОРОЛЯ", "13 хлопков. удар в стену. музыка.");
            var mk = Resources.Load<AudioClip>("Ambience/mountain_king");
            yield return Pan(3f, 40f);

            // crawling through a vent
            {
                var root = BuildDuct(out var ds, out var de, out var dy);
                fpc.ForceCrawl = true; for (int i = 0; i < 20; i++) fpc.Tick(0.05f);
                var cc = player.GetComponent<CharacterController>();
                hud.Toast("ВЕНТИЛЯЦИЯ", "ползи к свету");
                for (float t = 0; t < 3.5f; t += Time.deltaTime) { cc.enabled = false; player.transform.position = ds + new Vector3(0, 0, t * 1.4f); cc.enabled = true; fpc.SetLook(dy, 0); yield return null; }
                fpc.ForceCrawl = false; Destroy(root);
            }

            // mods and shaders
            ToMenu();
            typeof(MenuUI).GetMethod("ShadersPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return Wait(2.5f);
            typeof(MenuUI).GetMethod("ModsPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return Wait(2.5f);
            typeof(MenuUI).GetMethod("Main", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(menu, null);
            yield return Wait(3.5f);
            recording = false; yield return null;
            Debug.Log($"[ADDRESS] trailer: {vframe} frames -> {vdir}");
            Application.Quit();
        }

        IEnumerator Pan(float time, float degrees)
        {
            for (float t = 0; t < time; t += Time.deltaTime) { fpc.SetLook(level.startYaw - degrees * 0.5f + degrees * t / time, 2f); yield return null; }
        }

        IEnumerator Dolly(float time)
        {
            var cc = player.GetComponent<CharacterController>(); var s = player.transform.position; var f = Quaternion.Euler(0, level.startYaw, 0) * Vector3.forward;
            fearFx.target = 0.6f;
            for (float t = 0; t < time; t += Time.deltaTime) { cc.enabled = false; player.transform.position = s + f * t * 3.2f; cc.enabled = true; fpc.SetLook(level.startYaw, 0); yield return null; }
            fearFx.target = 0f;
        }

        /// <summary>A creature walking towards the camera down the most open direction from the start.</summary>
        IEnumerator Creature(Address.Entities.EntityKind kind, float time)
        {
            float bestFree = 0, yaw = level.startYaw;
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                float free = Physics.Raycast(level.start + Vector3.up, d, out var h, 20f, 1 << LevelBuilder.ArchLayer) ? h.distance : 20f;
                if (free > bestFree) { bestFree = free; yaw = i * 45f; }
            }
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward; float far = Mathf.Clamp(bestFree - 1f, 3f, 9f);
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = level.start; cc.enabled = true;
            var e = Address.Entities.EntitySpawner.SpawnKind(level, config.bodyPrefab, kind, level.start + dir * far, Quaternion.LookRotation(-dir), null);
            e.testSpeed = 1.2f;
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                e.transform.position = level.start + dir * Mathf.Lerp(far, 1.6f, t / time);
                fpc.SetLook(yaw, 4f); yield return null;
            }
            Destroy(e.gameObject);
        }
    }
}
