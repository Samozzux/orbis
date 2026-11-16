using System;
using UnityEngine;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Orologio di gioco (docs/01 §4): predefinito 1 ora di gioco = 1 minuto reale, pausa, x2, x4, x10.
    /// In M1 parte dall'ora attuale del dispositivo; lo scheduler completo arriva in M2.
    /// </summary>
    public sealed class GameClock
    {
        public static readonly float[] Speeds = { 0f, 1f, 2f, 4f, 10f };
        private const double GameSecondsPerRealSecond = 60.0; // 1 h di gioco = 1 min reale

        public DateTime Now { get; private set; }
        public int SpeedIndex { get; private set; } = 1;
        public float Speed => Speeds[SpeedIndex];
        public bool Paused => SpeedIndex == 0;

        public event Action<DateTime> HourChanged;

        public GameClock(DateTime start) => Now = start;

        public void SetSpeed(int index) => SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);

        public void Tick(float realDeltaSeconds)
        {
            if (Paused) return;
            var before = Now;
            Now = Now.AddSeconds(realDeltaSeconds * GameSecondsPerRealSecond * Speed);
            if (Now.Hour != before.Hour || (Now - before).TotalHours >= 1) HourChanged?.Invoke(Now);
        }

        /// <summary>Salta avanti (es. "vai alla sera"): solo per prove e per il pulsante dell'ora.</summary>
        public void Advance(TimeSpan span)
        {
            Now = Now.Add(span);
            HourChanged?.Invoke(Now);
        }
    }
}
