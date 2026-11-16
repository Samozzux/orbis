using System;

namespace Orbis.Core.World.Sky
{
    public enum WeatherKind
    {
        Clear = 0,
        PartlyCloudy,
        Cloudy,
        Rain,
        Fog,
    }

    /// <summary>Stato del meteo con i parametri visivi (0..1) che il rendering interpola.</summary>
    public struct WeatherState
    {
        public WeatherKind Kind;
        public float Clouds;
        public float Rain;
        public float Fog;
        public float Wetness;

        public static WeatherState For(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Clear: return new WeatherState { Kind = k, Clouds = 0.12f };
                case WeatherKind.PartlyCloudy: return new WeatherState { Kind = k, Clouds = 0.45f };
                case WeatherKind.Cloudy: return new WeatherState { Kind = k, Clouds = 0.85f, Fog = 0.15f };
                case WeatherKind.Rain: return new WeatherState { Kind = k, Clouds = 1f, Rain = 1f, Fog = 0.35f, Wetness = 1f };
                default: return new WeatherState { Kind = k, Clouds = 0.5f, Fog = 1f, Wetness = 0.3f };
            }
        }
    }

    /// <summary>
    /// Meteo simulato di base (M1; il meteo reale da Open-Meteo arriva in M4): catena di Markov oraria
    /// con probabilità che dipendono dalla stagione (Abruzzo: estate secca, autunno e inverno più piovosi).
    /// Deterministica dato il seme: stesso seme e stesse ore → stesso meteo.
    /// </summary>
    public sealed class WeatherSimulator
    {
        private uint _state;
        private int _hoursInState;

        public WeatherKind Current { get; private set; }

        public WeatherSimulator(uint seed, WeatherKind start = WeatherKind.Clear)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
            Current = start;
        }

        private float Next()
        {
            // xorshift32
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Avanza di un'ora di gioco. <paramref name="month"/> 1..12, <paramref name="hour"/> 0..23.</summary>
        public WeatherKind StepHour(int month, int hour)
        {
            _hoursInState++;
            // un tempo dura almeno 3 ore
            if (_hoursInState < 3) return Current;
            float wet = month >= 10 || month <= 3 ? 1.6f : (month >= 6 && month <= 8 ? 0.45f : 1f);
            float r = Next();
            var next = Current;
            switch (Current)
            {
                case WeatherKind.Clear:
                    if (r < 0.05f) next = WeatherKind.PartlyCloudy;
                    else if (r < 0.065f && (hour < 9 || hour > 20) && month != 7 && month != 8) next = WeatherKind.Fog;
                    break;
                case WeatherKind.PartlyCloudy:
                    if (r < 0.07f) next = WeatherKind.Clear;
                    else if (r < 0.07f + 0.05f * wet) next = WeatherKind.Cloudy;
                    break;
                case WeatherKind.Cloudy:
                    if (r < 0.06f) next = WeatherKind.PartlyCloudy;
                    else if (r < 0.06f + 0.07f * wet) next = WeatherKind.Rain;
                    break;
                case WeatherKind.Rain:
                    if (r < 0.14f) next = WeatherKind.Cloudy;
                    break;
                case WeatherKind.Fog:
                    if (r < (hour >= 9 && hour <= 16 ? 0.4f : 0.08f)) next = WeatherKind.PartlyCloudy;
                    break;
            }
            if (next != Current)
            {
                Current = next;
                _hoursInState = 0;
            }
            return Current;
        }
    }
}
