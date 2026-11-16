using Orbis.Core.Layout;
using Xunit;

namespace Orbis.Core.Tests
{
    public class ScreenClassifierTests
    {
        [Theory]
        [InlineData(852, 393, ScreenClass.Compact)]   // iPhone 15 Pro orizzontale
        [InlineData(393, 852, ScreenClass.Compact)]   // iPhone 15 Pro verticale (browser)
        [InlineData(915, 412, ScreenClass.Compact)]   // Android medio-alto orizzontale
        [InlineData(1180, 820, ScreenClass.Medium)]   // iPad 11"
        [InlineData(1133, 744, ScreenClass.Medium)]   // iPad mini
        [InlineData(1376, 1032, ScreenClass.Wide)]    // iPad Pro 13"
        [InlineData(1920, 960, ScreenClass.Wide)]     // browser desktop
        [InlineData(1000, 700, ScreenClass.Medium)]   // finestra desktop piccola
        public void Classify_DaPunti(float w, float h, ScreenClass expected)
        {
            Assert.Equal(expected, ScreenClassifier.Classify(w, h));
        }

        [Fact]
        public void Measure_ConvertePixelInPunti()
        {
            var m = ScreenClassifier.Measure(2556, 1179, 3f); // iPhone 15 Pro
            Assert.Equal(852f, m.WidthPt, 1);
            Assert.Equal(393f, m.HeightPt, 1);
            Assert.Equal(ScreenClass.Compact, m.Class);
            Assert.Equal(ScreenOrientationKind.Landscape, m.Orientation);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-2f)]
        [InlineData(float.NaN)]
        [InlineData(100f)]
        public void Measure_ScalaNonValida_UsaUno(float ppp)
        {
            var m = ScreenClassifier.Measure(1920, 1080, ppp);
            Assert.Equal(1f, m.PixelsPerPoint);
            Assert.Equal(ScreenClass.Wide, m.Class);
        }

        [Fact]
        public void Measure_DimensioniNulle_NonEsplode()
        {
            var m = ScreenClassifier.Measure(0, 0, 2f);
            Assert.Equal(ScreenClass.Compact, m.Class);
        }
    }
}
