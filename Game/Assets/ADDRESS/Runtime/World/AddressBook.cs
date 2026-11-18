using System;
using System.Collections.Generic;
using System.IO;
using Address.Core;
using UnityEngine;

namespace Address.World
{
    /// <summary>
    /// The player's record of the Library: visited addresses, the last place, the Echo genealogy.
    /// Exits always lead to an address nobody has visited yet (in this save); taken numbers are skipped.
    /// </summary>
    [Serializable]
    public class AddressBook
    {
        public List<string> visited = new();
        public string last;
        public List<string> echoLinks = new();      // "parent -> child" for the Archive genealogy
        [NonSerialized] HashSet<string> set;

        static string PathFile => System.IO.Path.Combine(Application.persistentDataPath, "address_book.json");

        public static AddressBook Load()
        {
            try { if (File.Exists(PathFile)) return JsonUtility.FromJson<AddressBook>(File.ReadAllText(PathFile)) ?? new AddressBook(); }
            catch (Exception e) { Debug.LogWarning("address book unreadable, starting fresh: " + e.Message); }
            return new AddressBook();
        }

        public void Save()
        {
            try { File.WriteAllText(PathFile, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning("address book not saved: " + e.Message); }
        }

        HashSet<string> Set => set ??= new HashSet<string>(visited);

        public bool Visited(LevelAddress a) => Set.Contains(a.ToString());

        public void Visit(LevelAddress a)
        {
            var k = a.ToString();
            if (Set.Add(k)) visited.Add(k);
            last = k;
            Save();
        }

        /// <summary>First free address in the deterministic sequence seed, seed+1', ... (aesthetic kept).</summary>
        public LevelAddress NextFree(string aesthetic, ulong seed)
        {
            for (ulong k = 0; k < 10000; k++)
            {
                var a = new LevelAddress(aesthetic, k == 0 ? seed : Hashing.Combine(seed, k));
                if (!Visited(a)) return a;
            }
            return new LevelAddress(aesthetic, Hashing.Combine(seed, (ulong)DateTime.Now.Ticks));
        }

        public void LinkEcho(LevelAddress parent, LevelAddress child) { echoLinks.Add($"{parent} -> {child}"); Save(); }
    }
}
