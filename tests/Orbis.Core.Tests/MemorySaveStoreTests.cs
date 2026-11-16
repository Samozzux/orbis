using System;
using System.Threading.Tasks;
using Orbis.Core.Persistence;
using Xunit;

namespace Orbis.Core.Tests
{
    public class MemorySaveStoreTests
    {
        [Fact]
        public async Task ScriviLeggiCancella()
        {
            ISaveStore store = new MemorySaveStore();
            Assert.False(await store.ExistsAsync("saves/a/state"));
            Assert.Null(await store.ReadAsync("saves/a/state"));

            await store.WriteAsync("saves/a/state", new byte[] { 1, 2, 3 });
            Assert.True(await store.ExistsAsync("saves/a/state"));
            Assert.Equal(new byte[] { 1, 2, 3 }, await store.ReadAsync("saves/a/state"));

            await store.DeleteAsync("saves/a/state");
            Assert.False(await store.ExistsAsync("saves/a/state"));
        }

        [Fact]
        public async Task DatiCopiati_NonCondivisiConIlChiamante()
        {
            var store = new MemorySaveStore();
            var data = new byte[] { 9 };
            await store.WriteAsync("k", data);
            data[0] = 0;
            var read = await store.ReadAsync("k");
            Assert.Equal(9, read[0]);
            read[0] = 5;
            Assert.Equal(9, (await store.ReadAsync("k"))[0]);
        }

        [Fact]
        public async Task List_FiltraPerPrefissoInOrdine()
        {
            var store = new MemorySaveStore();
            await store.WriteAsync("saves/b", new byte[0]);
            await store.WriteAsync("saves/a", new byte[0]);
            await store.WriteAsync("cache/x", new byte[0]);
            Assert.Equal(new[] { "saves/a", "saves/b" }, await store.ListAsync("saves/"));
            Assert.Equal(3, (await store.ListAsync(null)).Count);
        }

        [Fact]
        public async Task ChiaveVuota_Eccezione()
        {
            var store = new MemorySaveStore();
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(" ", new byte[0]));
            await Assert.ThrowsAsync<ArgumentNullException>(() => store.WriteAsync("k", null));
        }
    }
}
