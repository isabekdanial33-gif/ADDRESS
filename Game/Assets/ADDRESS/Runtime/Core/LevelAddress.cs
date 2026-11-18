using System;
using System.Globalization;
using System.Text;

namespace Address.Core
{
    /// <summary>
    /// A level address in the Library: aesthetic + 64-bit seed + Echo generation + generator version.
    /// Text form: "BR-7F3A-91C2-0D44-E5B1/g0/v1".
    /// </summary>
    [Serializable]
    public struct LevelAddress : IEquatable<LevelAddress>
    {
        public const int CurrentGeneratorVersion = 1;

        public string Aesthetic;   // short code: BR (Backrooms), PR (Poolrooms), FA (Frutiger Aero) ...
        public ulong Seed;
        public int Generation;     // Echo generation, 0 = original
        public int Version;        // generator version the place was first generated with

        public LevelAddress(string aesthetic, ulong seed, int generation = 0, int version = CurrentGeneratorVersion)
        { Aesthetic = aesthetic; Seed = seed; Generation = generation; Version = version; }

        /// <summary>Seed actually used by generation: depends on everything except the generation (Echo is applied on top of g-1).</summary>
        public ulong BaseSeed => Hashing.Combine(Hashing.Combine(Seed, Hashing.Fnv64(Aesthetic)), (ulong)Version);

        public LevelAddress NextEcho() => new LevelAddress(Aesthetic, Seed, Generation + 1, Version);

        public override string ToString()
        {
            string h = Seed.ToString("X16");
            return $"{Aesthetic}-{h.Substring(0, 4)}-{h.Substring(4, 4)}-{h.Substring(8, 4)}-{h.Substring(12, 4)}/g{Generation}/v{Version}";
        }

        public static bool TryParse(string text, out LevelAddress a)
        {
            a = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().ToUpperInvariant();
            string[] parts = text.Split('/');
            string[] head = parts[0].Split('-');
            if (head.Length != 5 || head[0].Length < 2) return false;
            string hex = string.Concat(head[1], head[2], head[3], head[4]);
            if (hex.Length != 16 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong seed)) return false;
            int g = 0, v = CurrentGeneratorVersion;
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.StartsWith("G") && int.TryParse(p.Substring(1), out int gg)) g = gg;
                else if (p.StartsWith("V") && int.TryParse(p.Substring(1), out int vv)) v = vv;
                else return false;
            }
            a = new LevelAddress(head[0], seed, g, v);
            return true;
        }

        /// <summary>
        /// Library-of-Babel search: any phrase maps to one address that "always existed".
        /// If the text is already a valid address it is parsed as such.
        /// </summary>
        public static LevelAddress FromPhrase(string phrase, Func<ulong, string> aestheticForSeed)
        {
            if (TryParse(phrase, out var parsed)) return parsed;
            string norm = Normalize(phrase);
            ulong seed = Hashing.Combine(Hashing.Fnv64("babel"), Hashing.Fnv64(norm));
            return new LevelAddress(aestheticForSeed(seed), seed);
        }

        static string Normalize(string s)
        {
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in (s ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC))
            {
                if (char.IsWhiteSpace(c)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; }
                else { sb.Append(c); space = false; }
            }
            return sb.ToString().TrimEnd();
        }

        public bool Equals(LevelAddress o) => Aesthetic == o.Aesthetic && Seed == o.Seed && Generation == o.Generation && Version == o.Version;
        public override bool Equals(object obj) => obj is LevelAddress o && Equals(o);
        public override int GetHashCode() => (int)(Hashing.Combine(BaseSeed, (ulong)Generation) & 0x7fffffff);
    }
}
