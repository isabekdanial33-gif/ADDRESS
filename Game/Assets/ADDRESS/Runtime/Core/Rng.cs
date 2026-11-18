using System;
using UnityEngine;

namespace Address.Core
{
    /// <summary>
    /// Deterministic RNG (xoshiro256** seeded through SplitMix64).
    /// Never use UnityEngine.Random for generation: results must be bit-identical for an address on every machine.
    /// Use <see cref="Fork"/> to get independent named streams so adding a new system never shifts others.
    /// </summary>
    public sealed class Rng
    {
        ulong s0, s1, s2, s3;
        public readonly ulong Seed;

        public Rng(ulong seed)
        {
            Seed = seed;
            ulong x = seed;
            s0 = SplitMix(ref x); s1 = SplitMix(ref x); s2 = SplitMix(ref x); s3 = SplitMix(ref x);
        }

        public static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = Rotl(s3, 45);
            return result;
        }

        /// <summary>Independent child stream; same parent seed + same name => same stream.</summary>
        public Rng Fork(string name) => new Rng(Hashing.Combine(Seed, Hashing.Fnv64(name)));
        public Rng Fork(string name, int index) => new Rng(Hashing.Combine(Hashing.Combine(Seed, Hashing.Fnv64(name)), (ulong)index));

        /// <summary>[0,1)</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));
        public float Value => (float)NextDouble();
        public float Range(float min, float max) => min + (max - min) * Value;
        /// <summary>[min,max) integer</summary>
        public int Range(int min, int max) => max <= min ? min : min + (int)(NextULong() % (ulong)(max - min));
        public bool Chance(float p) => NextDouble() < p;
        public float Sign() => Chance(0.5f) ? 1f : -1f;

        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list) => list[Range(0, list.Count)];

        public int PickWeighted(System.Collections.Generic.IReadOnlyList<float> weights)
        {
            float total = 0; for (int i = 0; i < weights.Count; i++) total += Mathf.Max(0, weights[i]);
            if (total <= 0) return -1;
            float r = Value * total;
            for (int i = 0; i < weights.Count; i++) { r -= Mathf.Max(0, weights[i]); if (r < 0) return i; }
            return weights.Count - 1;
        }

        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = Range(0, i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        public Vector2 InsideUnitCircle()
        {
            float a = Value * Mathf.PI * 2f, r = Mathf.Sqrt(Value);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
    }

    public static class Hashing
    {
        public static ulong Fnv64(string s)
        {
            ulong h = 1469598103934665603UL;
            if (s == null) return h;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        public static ulong Combine(ulong a, ulong b)
        {
            ulong x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
            return Rng.SplitMix(ref x);
        }
    }
}
