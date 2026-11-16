using System;
using System.Collections.Generic;

namespace Orbis.Core.Boot
{
    /// <summary>Un passo del caricamento iniziale, con il suo peso sulla barra di avanzamento.</summary>
    public sealed class BootStep
    {
        public BootStep(string id, float weight, string labelKey)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Id obbligatorio.", nameof(id));
            if (weight <= 0f) throw new ArgumentOutOfRangeException(nameof(weight), "Il peso deve essere positivo.");
            Id = id;
            Weight = weight;
            LabelKey = labelKey ?? string.Empty;
        }

        public string Id { get; }
        public float Weight { get; }

        /// <summary>Chiave della tabella Localization con il testo da mostrare.</summary>
        public string LabelKey { get; }
    }

    /// <summary>
    /// Calcola l'avanzamento complessivo (0–1) di una sequenza di passi pesati.
    /// Logica pura: la schermata di caricamento si limita a disegnare <see cref="Overall"/>.
    /// </summary>
    public sealed class BootProgress
    {
        private readonly List<BootStep> _steps = new List<BootStep>();
        private readonly Dictionary<string, float> _fractions = new Dictionary<string, float>();
        private readonly float _totalWeight;

        public BootProgress(IEnumerable<BootStep> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            foreach (var step in steps)
            {
                if (_fractions.ContainsKey(step.Id))
                    throw new ArgumentException($"Passo duplicato: {step.Id}", nameof(steps));
                _steps.Add(step);
                _fractions[step.Id] = 0f;
                _totalWeight += step.Weight;
            }
            if (_steps.Count == 0)
                throw new ArgumentException("Serve almeno un passo.", nameof(steps));
        }

        public IReadOnlyList<BootStep> Steps => _steps;

        /// <summary>Passo in corso (il primo non completato), null se tutto è finito.</summary>
        public BootStep Current
        {
            get
            {
                foreach (var step in _steps)
                    if (_fractions[step.Id] < 1f) return step;
                return null;
            }
        }

        public bool IsComplete => Current == null;

        public float Overall
        {
            get
            {
                var done = 0f;
                foreach (var step in _steps)
                    done += step.Weight * _fractions[step.Id];
                return Clamp01(done / _totalWeight);
            }
        }

        /// <summary>Aggiorna l'avanzamento di un passo. Non torna mai indietro.</summary>
        public void Report(string stepId, float fraction)
        {
            if (!_fractions.TryGetValue(stepId, out var current))
                throw new KeyNotFoundException($"Passo sconosciuto: {stepId}");
            var next = Clamp01(fraction);
            if (next > current)
                _fractions[stepId] = next;
        }

        public void Complete(string stepId) => Report(stepId, 1f);

        /// <summary>
        /// Passo a cui corrisponde un avanzamento complessivo <paramref name="overall"/> (0–1):
        /// serve a mostrare l'etichetta giusta mentre la barra animata insegue il valore reale.
        /// Restituisce null a 1.
        /// </summary>
        public BootStep StepAt(float overall)
        {
            var target = Clamp01(overall) * _totalWeight;
            var cumulative = 0f;
            foreach (var step in _steps)
            {
                cumulative += step.Weight;
                if (target < cumulative - 1e-4f)
                    return step;
            }
            return null;
        }

        public float FractionOf(string stepId) =>
            _fractions.TryGetValue(stepId, out var f) ? f : throw new KeyNotFoundException($"Passo sconosciuto: {stepId}");

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
