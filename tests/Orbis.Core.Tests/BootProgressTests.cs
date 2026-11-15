using System;
using Orbis.Core.Boot;
using Xunit;

namespace Orbis.Core.Tests
{
    public class BootProgressTests
    {
        private static BootProgress Create() => new BootProgress(new[]
        {
            new BootStep("platform", 1f, "loading.platform"),
            new BootStep("localization", 1f, "loading.texts"),
            new BootStep("world", 2f, "loading.world"),
        });

        [Fact]
        public void AllInizio_AvanzamentoZeroEPrimoPassoCorrente()
        {
            var p = Create();
            Assert.Equal(0f, p.Overall);
            Assert.Equal("platform", p.Current.Id);
            Assert.False(p.IsComplete);
        }

        [Fact]
        public void Avanzamento_RispettaIPesi()
        {
            var p = Create();
            p.Complete("platform");
            Assert.Equal(0.25f, p.Overall, 3);
            p.Report("world", 0.5f);
            Assert.Equal(0.5f, p.Overall, 3);
            Assert.Equal("localization", p.Current.Id);
        }

        [Fact]
        public void Avanzamento_NonTornaIndietroERestaTraZeroEUno()
        {
            var p = Create();
            p.Report("world", 0.8f);
            p.Report("world", 0.2f);
            Assert.Equal(0.8f, p.FractionOf("world"), 3);
            p.Report("platform", 5f);
            Assert.Equal(1f, p.FractionOf("platform"));
            p.Report("localization", float.NaN);
            Assert.Equal(0f, p.FractionOf("localization"));
        }

        [Fact]
        public void TuttiCompletati_IsComplete()
        {
            var p = Create();
            foreach (var s in p.Steps) p.Complete(s.Id);
            Assert.True(p.IsComplete);
            Assert.Null(p.Current);
            Assert.Equal(1f, p.Overall);
        }

        [Theory]
        [InlineData(0f, "platform")]
        [InlineData(0.2f, "platform")]
        [InlineData(0.25f, "localization")]
        [InlineData(0.6f, "world")]
        [InlineData(0.99f, "world")]
        public void StepAt_TrovaIlPassoDellaBarra(float overall, string expected)
        {
            Assert.Equal(expected, Create().StepAt(overall).Id);
        }

        [Fact]
        public void StepAt_AllaFine_Null()
        {
            Assert.Null(Create().StepAt(1f));
        }

        [Fact]
        public void PassiNonValidi_Eccezione()
        {
            Assert.Throws<ArgumentException>(() => new BootProgress(new BootStep[0]));
            Assert.Throws<ArgumentException>(() => new BootProgress(new[] { new BootStep("a", 1f, ""), new BootStep("a", 1f, "") }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BootStep("a", 0f, ""));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Create().Report("nessuno", 1f));
        }
    }
}
