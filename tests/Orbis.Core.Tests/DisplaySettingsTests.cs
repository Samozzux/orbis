using Orbis.Core.Settings;
using Xunit;

namespace Orbis.Core.Tests
{
    public class DisplaySettingsTests
    {
        [Fact]
        public void Predefinito_RotazioneAttivaEAvvioInVerticale()
        {
            var s = new DisplaySettings();
            Assert.True(s.AutoRotate);
            Assert.Equal(FixedOrientation.Portrait, s.StartOrientation);
            Assert.Equal(new AllowedOrientations(true, true), s.Allowed);
        }

        [Fact]
        public void SpegnereLaRotazione_BloccaLOrientamentoCorrente()
        {
            var s = new DisplaySettings();
            s.SetAutoRotate(false, FixedOrientation.Landscape);
            Assert.False(s.AutoRotate);
            Assert.Equal(FixedOrientation.Landscape, s.Fixed);
            Assert.Equal(new AllowedOrientations(false, true), s.Allowed);
            Assert.Equal(FixedOrientation.Landscape, s.StartOrientation);
        }

        [Fact]
        public void ConRotazioneSpenta_SiPuoScegliereVerticaleOOrizzontale()
        {
            var s = new DisplaySettings();
            s.SetAutoRotate(false, FixedOrientation.Landscape);
            s.SetFixed(FixedOrientation.Portrait);
            Assert.Equal(new AllowedOrientations(true, false), s.Allowed);
        }

        [Fact]
        public void RiaccendereLaRotazione_PermetteEntrambi()
        {
            var s = new DisplaySettings();
            s.SetAutoRotate(false, FixedOrientation.Landscape);
            s.SetAutoRotate(true, FixedOrientation.Portrait);
            Assert.Equal(new AllowedOrientations(true, true), s.Allowed);
            Assert.Equal(FixedOrientation.Landscape, s.Fixed); // la scelta resta per la prossima volta
        }

        [Theory]
        [InlineData(true, FixedOrientation.Portrait)]
        [InlineData(false, FixedOrientation.Landscape)]
        [InlineData(false, FixedOrientation.Portrait)]
        public void SalvataggioERilettura(bool auto, FixedOrientation fixedOrientation)
        {
            var s = new DisplaySettings();
            s.SetAutoRotate(false, fixedOrientation);
            s.SetAutoRotate(auto, fixedOrientation);
            var r = DisplaySettings.Deserialize(s.Serialize());
            Assert.Equal(s.AutoRotate, r.AutoRotate);
            Assert.Equal(s.Fixed, r.Fixed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("boh")]
        [InlineData("x:y:z")]
        public void TestoNonValido_ValoriPredefiniti(string text)
        {
            var r = DisplaySettings.Deserialize(text);
            Assert.True(r.AutoRotate);
            Assert.Equal(FixedOrientation.Portrait, r.Fixed);
        }
    }
}
