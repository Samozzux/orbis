using System;

namespace Orbis.Core.Population
{
    /// <summary>
    /// Generatore casuale deterministico (SplitMix64, docs/03 §12): stessa sequenza su tutte le piattaforme,
    /// così la stessa partita con lo stesso seed produce la stessa città.
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(ulong seed) => _state = seed;

        /// <summary>Generatore indipendente per un sistema (seed della partita + sale del sistema).</summary>
        public static Rng For(int seed, string system)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in system)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return new Rng((ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ h);
        }

        public ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Numero in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Intero in [0, max).</summary>
        public int Next(int max) => max <= 0 ? 0 : (int)(NextDouble() * max);

        /// <summary>Intero in [min, max] (estremi compresi).</summary>
        public int Range(int min, int max) => max <= min ? min : min + Next(max - min + 1);

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Normale (Box–Muller).</summary>
        public double Gaussian(double mean = 0, double sd = 1)
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Indice scelto in proporzione ai pesi (-1 se sono tutti zero).</summary>
        public int Pick(double[] weights)
        {
            double total = 0;
            foreach (var w in weights) total += w > 0 ? w : 0;
            if (total <= 0) return -1;
            double r = NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                r -= weights[i];
                if (r < 0) return i;
            }
            for (int i = weights.Length - 1; i >= 0; i--)
                if (weights[i] > 0) return i;
            return -1;
        }

        /// <summary>Mescola l'array (Fisher–Yates).</summary>
        public void Shuffle<T>(T[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                var t = items[i];
                items[i] = items[j];
                items[j] = t;
            }
        }
    }

    /// <summary>
    /// Albero di Fenwick sui pesi: estrazione proporzionale al peso e aggiornamento in O(log n)
    /// (posti liberi nelle case, posti di lavoro ancora vacanti).
    /// </summary>
    public sealed class WeightedPicker
    {
        private readonly double[] _tree;
        private readonly double[] _weights;
        private double _total;

        public WeightedPicker(int count)
        {
            _tree = new double[count + 1];
            _weights = new double[count];
        }

        public int Count => _weights.Length;
        public double Total => _total;
        public double Weight(int i) => _weights[i];

        public void Set(int i, double weight)
        {
            if (weight < 0) weight = 0;
            double delta = weight - _weights[i];
            if (delta == 0) return;
            _weights[i] = weight;
            _total += delta;
            for (int k = i + 1; k < _tree.Length; k += k & -k) _tree[k] += delta;
        }

        /// <summary>Indice estratto in proporzione ai pesi, -1 se il totale è zero.</summary>
        public int Pick(Rng rng)
        {
            if (_total <= 1e-9) return -1;
            double r = rng.NextDouble() * _total;
            int pos = 0;
            int step = 1;
            while (step * 2 < _tree.Length) step *= 2;
            for (; step > 0; step >>= 1)
            {
                int next = pos + step;
                if (next < _tree.Length && _tree[next] <= r)
                {
                    pos = next;
                    r -= _tree[next];
                }
            }
            // pos = ultimo indice (base 1) con somma ≤ r: l'estratto è pos (base 0); salta i pesi nulli per arrotondamenti
            int idx = Math.Min(pos, _weights.Length - 1);
            while (idx < _weights.Length - 1 && _weights[idx] <= 0) idx++;
            while (idx > 0 && _weights[idx] <= 0) idx--;
            return _weights[idx] > 0 ? idx : -1;
        }
    }
}
