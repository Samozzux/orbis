using System;

namespace Orbis.Core.Layout
{
    /// <summary>Classi di layout dell'interfaccia (docs/01 §12).</summary>
    public enum ScreenClass
    {
        /// <summary>Telefono (o finestra piccola).</summary>
        Compact,
        /// <summary>Tablet (o finestra media).</summary>
        Medium,
        /// <summary>Desktop / schermi grandi: pannelli laterali fissi.</summary>
        Wide,
    }

    public enum ScreenOrientationKind
    {
        Landscape,
        Portrait,
    }

    /// <summary>Misure dello schermo in punti logici (1 punto ≈ 1 pt iOS / 1 dp Android / 1 px CSS).</summary>
    public readonly struct ScreenMetrics
    {
        public ScreenMetrics(float widthPt, float heightPt, float pixelsPerPoint, ScreenClass screenClass)
        {
            WidthPt = widthPt;
            HeightPt = heightPt;
            PixelsPerPoint = pixelsPerPoint;
            Class = screenClass;
        }

        public float WidthPt { get; }
        public float HeightPt { get; }
        public float PixelsPerPoint { get; }
        public ScreenClass Class { get; }
        public float ShortSidePt => Math.Min(WidthPt, HeightPt);
        public float LongSidePt => Math.Max(WidthPt, HeightPt);
        public ScreenOrientationKind Orientation => HeightPt > WidthPt ? ScreenOrientationKind.Portrait : ScreenOrientationKind.Landscape;
    }

    /// <summary>
    /// Sceglie il layout dalla dimensione reale in punti, non dalla piattaforma.
    /// Soglie: lato corto &lt; 500 pt → Compact; lato lungo ≥ 1280 e lato corto ≥ 700 → Wide; altrimenti Medium.
    /// </summary>
    public static class ScreenClassifier
    {
        public const float CompactMaxShortSide = 500f;
        public const float WideMinLongSide = 1280f;
        public const float WideMinShortSide = 700f;

        public static ScreenClass Classify(float widthPt, float heightPt)
        {
            var shortSide = Math.Min(widthPt, heightPt);
            var longSide = Math.Max(widthPt, heightPt);
            if (shortSide < CompactMaxShortSide)
                return ScreenClass.Compact;
            if (longSide >= WideMinLongSide && shortSide >= WideMinShortSide)
                return ScreenClass.Wide;
            return ScreenClass.Medium;
        }

        /// <summary>
        /// Converte pixel in punti. <paramref name="pixelsPerPoint"/> viene dalla piattaforma
        /// (es. 3 su iPhone 15 Pro, devicePixelRatio sul web); valori non validi valgono 1.
        /// </summary>
        public static ScreenMetrics Measure(int widthPx, int heightPx, float pixelsPerPoint)
        {
            if (widthPx <= 0 || heightPx <= 0)
                return new ScreenMetrics(0f, 0f, 1f, ScreenClass.Compact);
            var ppp = float.IsNaN(pixelsPerPoint) || pixelsPerPoint < 0.5f || pixelsPerPoint > 8f ? 1f : pixelsPerPoint;
            var w = widthPx / ppp;
            var h = heightPx / ppp;
            return new ScreenMetrics(w, h, ppp, Classify(w, h));
        }
    }
}
