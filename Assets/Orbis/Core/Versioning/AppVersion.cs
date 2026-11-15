using System;
using System.Globalization;

namespace Orbis.Core.Versioning
{
    /// <summary>
    /// Versione dell'app nel formato MAJOR.MINOR.PATCH (docs/07 §7).
    /// Usata per confrontare la versione installata con il campo minAppVersion dei manifest.
    /// </summary>
    public readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public AppVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0)
                throw new ArgumentOutOfRangeException(nameof(major), "I numeri di versione non possono essere negativi.");
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public static AppVersion Zero => new AppVersion(0, 0, 0);

        /// <summary>
        /// Accetta "1.2.3", "v1.2.3", "1.2" e ignora suffissi tipo "-dev" o "+build".
        /// </summary>
        public static bool TryParse(string text, out AppVersion version)
        {
            version = Zero;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0)
                s = s.Substring(0, cut);

            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 3)
                return false;

            var numbers = new int[3];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }

            version = new AppVersion(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        public static AppVersion Parse(string text)
        {
            if (!TryParse(text, out var v))
                throw new FormatException($"Versione non valida: '{text}'");
            return v;
        }

        /// <summary>Vero se questa versione è uguale o successiva a <paramref name="minimum"/>.</summary>
        public bool IsAtLeast(AppVersion minimum) => CompareTo(minimum) >= 0;

        public int CompareTo(AppVersion other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            return c != 0 ? c : Patch.CompareTo(other.Patch);
        }

        public bool Equals(AppVersion other) => CompareTo(other) == 0;
        public override bool Equals(object obj) => obj is AppVersion other && Equals(other);
        public override int GetHashCode() => (Major * 397 ^ Minor) * 397 ^ Patch;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";

        public static bool operator ==(AppVersion a, AppVersion b) => a.Equals(b);
        public static bool operator !=(AppVersion a, AppVersion b) => !a.Equals(b);
        public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
        public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
        public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    }
}
