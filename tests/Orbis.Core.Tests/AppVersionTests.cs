using Orbis.Core.Versioning;
using Xunit;

namespace Orbis.Core.Tests
{
    public class AppVersionTests
    {
        [Theory]
        [InlineData("1.2.3", 1, 2, 3)]
        [InlineData("v0.1.0", 0, 1, 0)]
        [InlineData("2.5", 2, 5, 0)]
        [InlineData("7", 7, 0, 0)]
        [InlineData("1.0.0-dev", 1, 0, 0)]
        [InlineData(" 3.4.5+build.9 ", 3, 4, 5)]
        public void Parse_AccettaIFormatiPrevisti(string text, int major, int minor, int patch)
        {
            var v = AppVersion.Parse(text);
            Assert.Equal(new AppVersion(major, minor, patch), v);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1.2.3.4")]
        [InlineData("1.-2.3")]
        [InlineData("1..3")]
        public void TryParse_RifiutaTestiNonValidi(string text)
        {
            Assert.False(AppVersion.TryParse(text, out _));
        }

        [Fact]
        public void Confronto_SegueMajorMinorPatch()
        {
            Assert.True(AppVersion.Parse("1.10.0") > AppVersion.Parse("1.9.9"));
            Assert.True(AppVersion.Parse("2.0.0") > AppVersion.Parse("1.99.99"));
            Assert.True(AppVersion.Parse("0.1.1").IsAtLeast(AppVersion.Parse("0.1.0")));
            Assert.False(AppVersion.Parse("0.0.9").IsAtLeast(AppVersion.Parse("0.1.0")));
            Assert.Equal("0.3.0", AppVersion.Parse("v0.3").ToString());
        }
    }
}
