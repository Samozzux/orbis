using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Core.Game;
using Orbis.Core.Traffic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;
using Xunit;

namespace Orbis.Core.Tests
{
    /// <summary>Strade e linee del sindaco (M4 blocco 4c): costi, regole, annulla/ripeti, salvataggio, incroci e biglietti.</summary>
    public class InfrastructureTests
    {
        private static readonly Lazy<TrafficBalance> Traffic = new Lazy<TrafficBalance>(() =>
            TrafficBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "traffic.json"))));

        private static readonly Lazy<TransitBalance> Transit = new Lazy<TransitBalance>(() =>
            TransitBalance.Parse(File.ReadAllText(Path.Combine(PopulationTests.Root, "data", "balance", "transit.json"))));

        private static Balance GameBalance(long cash = 500_000) => new Balance
        {
            // senza premi di livello, così i conti delle prove sono solo costi e rimborsi
            StartCash = cash, XpBase = 100, XpExponent = 1f, RewardPerLevel = 0, Traffic = Traffic.Value, Transit = Transit.Value,
        };

        private static GameSession Reload(GameSession g, Balance b, IWorldAccess world = null) =>
            GameSession.Load(g.Save(DateTime.UtcNow), TestGame.Catalog(), b, world ?? new FakeWorld(), Missions.Parse(TestGame.MissionsJson));

        [Fact]
        public void StradaDelSindaco_CostoAnnullaRipetiESalvataggio()
        {
            var b = GameBalance();
            var g = TestGame.New(b);
            long cash = g.State.Cash;
            double before = Economy.Compute(g.Context).Maintenance;
            var cmd = new BuildRoadCommand { Class = RoadClass.Residential, Points = new float[] { 0, 0, 120, 0, 120, 80 } };
            Assert.Equal(BuildResult.Ok, g.Do(cmd));
            // 200 m di strada residenziale a 300 €/m
            Assert.Equal(cash - 60_000, g.State.Cash);
            var road = Assert.Single(g.State.Roads.Values);
            Assert.Equal(cmd.RoadId, road.Id);
            Assert.Equal(200f, road.Length, 2);
            Assert.Equal(-60_000, g.State.Today().Amounts[(int)LedgerCategory.Construction]);
            // manutenzione: 0,2 km × 20 € al giorno
            Assert.Equal(before + 4.0, Economy.Compute(g.Context).Maintenance, 3);

            Assert.True(g.Undo());
            Assert.Empty(g.State.Roads);
            Assert.Equal(cash, g.State.Cash);
            Assert.Equal(BuildResult.Ok, g.Redo());
            Assert.Equal(road.Id, Assert.Single(g.State.Roads.Values).Id);

            // il salvataggio tiene la strada e la storia dei comandi
            var loaded = Reload(g, b);
            var copy = Assert.Single(loaded.State.Roads.Values);
            Assert.Equal(road.Points, copy.Points);
            Assert.Equal(RoadClass.Residential, copy.Class);
            Assert.True(loaded.Undo());
            Assert.Empty(loaded.State.Roads);
            Assert.Equal(cash, loaded.State.Cash);

            // demolizione con rimborso parziale (20%), annullabile
            long c2 = g.State.Cash;
            Assert.Equal(BuildResult.Ok, g.Do(new DemolishRoadCommand { RoadId = road.Id }));
            Assert.Empty(g.State.Roads);
            Assert.Equal(c2 + 12_000, g.State.Cash);
            Assert.True(g.Undo());
            Assert.Single(g.State.Roads);
            Assert.Equal(c2, g.State.Cash);
            Assert.Equal(g.State.LedgerTotal, g.State.Cash - g.State.StartCash);
        }

        [Fact]
        public void StradaNonValida()
        {
            var g = TestGame.New(GameBalance(50_000));
            Assert.Equal(BuildResult.InvalidPath, g.Do(new BuildRoadCommand { Points = new float[] { 0, 0 } }));
            Assert.Equal(BuildResult.InvalidPath, g.Do(new BuildRoadCommand { Points = new float[] { 0, 0, 2, 0 } }));       // tratto sotto i 4 m
            Assert.Equal(BuildResult.InvalidPath, g.Do(new BuildRoadCommand { Points = new float[] { 0, 0, 6000, 0 } }));    // oltre 5 km
            Assert.Equal(BuildResult.NotAllowed, g.Do(new BuildRoadCommand { Class = RoadClass.Service, Points = new float[] { 0, 0, 50, 0 } }));
            Assert.Equal(BuildResult.NotEnoughCash, g.Do(new BuildRoadCommand { Class = RoadClass.Motorway, Points = new float[] { 0, 0, 100, 0 } }));
            Assert.Empty(g.State.Roads);
            Assert.Equal(50_000, g.State.Cash);
        }

        /// <summary>Mondo con il sistema di coordinate vero: un tile con un lotto di 300×300 m e un edificio di 20×20 m.</summary>
        private sealed class FrameWorld : IWorldAccess
        {
            public readonly WorldFrame Frame = new WorldFrame(14.16, 42.35);
            public readonly TileId Id;
            public readonly double OX, OY;
            public readonly TileData Tile0;

            public FrameWorld()
            {
                Id = Frame.TileAt(14, 0, 0);
                Frame.TileOrigin(Id, out OX, out OY);
                var lotOuter = new float[] { 0, 0, 300, 0, 300, 300, 0, 300 };
                var bOuter = new float[] { 100, 100, 120, 100, 120, 120, 100, 120 };
                Tile0 = new TileData
                {
                    Id = Id,
                    Size = 1800,
                    Lots = new[] { new LotInfo { Id = 7, Outer = lotOuter, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(lotOuter), Area = 90000 } },
                    Buildings = new[] { new BuildingInfo { OsmId = 99, OsmType = OsmType.Way, Outer = bOuter, Holes = Array.Empty<float[]>(), Height = 9, Bounds = Bounds2.Of(bOuter) } },
                    Roads = new[] { new RoadInfo { OsmId = 5, Class = RoadClass.Residential, Width = 6, Points = new float[] { 0, 250, 300, 250 } } },
                    Terrain = TerrainInfo.Flat(1800),
                };
            }

            public TileData Tile(int x, int y) => x == Id.X && y == Id.Y ? Tile0 : null;

            /// <summary>Punti in coordinate del tile → metri della regione.</summary>
            public float[] Region(params float[] local)
            {
                var r = new float[local.Length];
                for (int i = 0; i + 1 < local.Length; i += 2)
                {
                    r[i] = (float)(local[i] + OX);
                    r[i + 1] = (float)(local[i + 1] + OY);
                }
                return r;
            }
        }

        [Fact]
        public void StradaNonPassaSugliEdificiEOccupaIlLotto()
        {
            var world = new FrameWorld();
            var b = GameBalance();
            var g = GameSession.New("Prova", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), DateTime.UtcNow, 1, TestGame.Catalog(), b, world, Missions.Parse(TestGame.MissionsJson));
            g.Context.Frame = world.Frame;
            var lot = new LotKey(world.Id.X, world.Id.Y, 7);
            // attraverso l'edificio reale: no; demolito sì
            Assert.Equal(BuildResult.Occupied, g.Do(new BuildRoadCommand { Points = world.Region(50, 110, 200, 110) }));
            // fuori dai tile caricati: no
            Assert.Equal(BuildResult.LotNotLoaded, g.Do(new BuildRoadCommand { Points = world.Region(50, 150, 50, -400) }));
            // una casa piazzata blocca la strada
            Assert.Equal(BuildResult.Ok, g.Do(new PlaceCommand { Item = "casa", Lot = lot, HalfX = 2 * 200, HalfY = 2 * 140 }));
            Assert.Equal(BuildResult.Occupied, g.Do(new BuildRoadCommand { Points = world.Region(150, 145, 260, 145) }));
            Assert.True(g.Undo());
            // strada libera
            Assert.Equal(BuildResult.Ok, g.Do(new BuildRoadCommand { Points = world.Region(50, 150, 250, 150) }));
            // ora le celle sotto carreggiata e marciapiedi (145,4–154,6 m) sono occupate per i nuovi elementi
            var casa = g.Context.Catalog.Get("casa");
            Assert.Equal(BuildResult.Occupied, Placement.CheckSpace(g.Context, casa, lot, 2 * 140, 2 * 146, 0));
            Assert.Equal(BuildResult.Ok, Placement.CheckSpace(g.Context, casa, lot, 2 * 140, 2 * 160, 0));
            Assert.Equal(BuildResult.Ok, Placement.CheckSpace(g.Context, casa, lot, 2 * 140, 2 * 130, 0));
            Assert.True(g.Undo());
            Assert.Equal(BuildResult.Ok, Placement.CheckSpace(g.Context, casa, lot, 2 * 140, 2 * 146, 0));
        }

        // ------------------------------------------------------------------ grafo

        private static RoadInfo Road(long id, RoadClass cls, params float[] xy) =>
            new RoadInfo { OsmId = id, Class = cls, Points = xy, Name = "r" + id };

        /// <summary>Viale orizzontale da (0,0) a (400,0) e una strada verticale che lo sorvola a x=200 senza toccarlo.</summary>
        private static TileData Cross() => new TileData
        {
            Id = new TileId(14, 0, 0),
            Roads = new[]
            {
                Road(1, RoadClass.Secondary, 0, 0, 400, 0),
                Road(2, RoadClass.Residential, 200, -200, 200, 200),
            },
        };

        private static int Degree(RoadGraph g, int n) => g.First[n + 1] - g.First[n];

        private static bool Reaches(RoadGraph g, int from, int to)
        {
            var seen = new bool[g.NodeCount];
            var queue = new Queue<int>();
            queue.Enqueue(from);
            seen[from] = true;
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                if (n == to) return true;
                for (int e = g.First[n]; e < g.First[n + 1]; e++)
                    if (!seen[g.To[e]])
                    {
                        seen[g.To[e]] = true;
                        queue.Enqueue(g.To[e]);
                    }
            }
            return false;
        }

        [Fact]
        public void IncrociAutomaticiEPonti()
        {
            var tiles = new[] { (Cross(), 0.0, 0.0) };
            var plain = RoadGraph.Build(tiles, Traffic.Value);
            Assert.Equal(4, plain.NodeCount);   // le due strade non si toccano
            Assert.True(plain.Nearest(100, 0, 1) < 0);

            // strada nuova che attraversa il viale a x=100: incrocio a quattro bracci
            var extra = new List<ExtraRoad> { new ExtraRoad { Id = 1, Class = RoadClass.Residential, Points = new float[] { 100, 100, 100, -100 } } };
            var g = RoadGraph.Build(tiles, Traffic.Value, null, extra);
            int x = g.Nearest(100, 0, 1);
            Assert.True(x >= 0);
            Assert.Equal(4, Degree(g, x));
            Assert.True(Reaches(g, g.Nearest(100, 100, 1), g.Nearest(400, 0, 1)));
            Assert.True(Reaches(g, g.Nearest(0, 0, 1), g.Nearest(100, -100, 1)));
            // la strada nuova è divisa in due tratti da 100 m, nei due sensi
            var own = g.EdgesOfExtra(1);
            Assert.Equal(4, own.Count);
            Assert.All(own, e => Assert.Equal(100f, g.Length[e], 1));

            // a T: l'estremo a 3 m dal viale si aggancia al viale (entro 8 m) e lo divide
            extra = new List<ExtraRoad> { new ExtraRoad { Id = 2, Class = RoadClass.Residential, Points = new float[] { 300, 100, 300, 3 } } };
            g = RoadGraph.Build(tiles, Traffic.Value, null, extra);
            int t = g.Nearest(300, 0, 0.5f);
            Assert.True(t >= 0);
            Assert.Equal(3, Degree(g, t));
            Assert.True(Reaches(g, g.Nearest(300, 100, 1), g.Nearest(0, 0, 1)));

            // ponte: sorvola il viale, nessun incrocio; si collega solo agli estremi (qui sulla strada verticale a x=200)
            extra = new List<ExtraRoad> { new ExtraRoad { Id = 3, Class = RoadClass.Residential, Bridge = true, Points = new float[] { 100, 100, 100, -100, 196, -100 } } };
            g = RoadGraph.Build(tiles, Traffic.Value, null, extra);
            Assert.True(g.Nearest(100, 0, 1) < 0);
            Assert.True(Reaches(g, g.Nearest(100, 100, 1), g.Nearest(200, 200, 1)));
            Assert.False(Reaches(g, g.Nearest(100, 100, 1), g.Nearest(400, 0, 1)));

            // due strade nuove che si incrociano tra loro, la seconda a senso unico
            extra = new List<ExtraRoad>
            {
                new ExtraRoad { Id = 4, Class = RoadClass.Residential, Points = new float[] { 50, 50, 350, 50 } },
                new ExtraRoad { Id = 5, Class = RoadClass.Residential, Oneway = 1, Points = new float[] { 250, 100, 250, 30 } },
            };
            g = RoadGraph.Build(tiles, Traffic.Value, null, extra);
            int c = g.Nearest(250, 50, 1);
            Assert.True(c >= 0);
            Assert.Equal(3, Degree(g, c));   // est, ovest e giù lungo il senso unico (verso sud)
            Assert.True(Reaches(g, g.Nearest(250, 100, 1), c));
            Assert.False(Reaches(g, c, g.Nearest(250, 100, 1)));
        }

        // ------------------------------------------------------------------ linee

        /// <summary>Quattro fermate ogni 500 m: 8 minuti da capolinea a capolinea.</summary>
        private static CityLine Line(int headway = 15) => new CityLine
        {
            Stops = new float[] { 0, 0, 500, 0, 1000, 0, 1500, 0 },
            HeadwayMinutes = headway,
            FareCents = 130,
        };

        [Fact]
        public void LineaDelComune_CostiMezziEModifiche()
        {
            var tb = Transit.Value;
            var l = Line();
            Assert.Equal(8.0, Infrastructure.LineMinutes(tb, l), 2);
            // giro: 8 + 8 + 10 di sosta = 26 minuti; ogni 15 minuti servono 2 mezzi
            Assert.Equal(2, Infrastructure.Vehicles(tb, l));
            Assert.Equal(2 * 16 * 30 + 4 * 1, Infrastructure.LineCostPerDay(tb, l), 3);

            var b = GameBalance();
            var g = TestGame.New(b);
            long cash = g.State.Cash;
            var open = new SetLineCommand { Line = l };
            Assert.Equal(BuildResult.Ok, g.Do(open));
            var line = Assert.Single(g.State.Lines.Values);
            Assert.Equal(open.ResultId, line.Id);
            Assert.Equal(line.Id.ToString(), line.Ref);
            Assert.NotEqual(0u, line.Colour);
            // 4 fermate da 6.000 € e 2 mezzi da 60.000 €
            Assert.Equal(cash - 144_000, g.State.Cash);
            Assert.Equal(964, Economy.Compute(g.Context).TransitCosts, 3);

            // più frequente (ogni 10'): un mezzo in più, nessuna fermata nuova
            var faster = line.Clone();
            faster.HeadwayMinutes = 10;
            Assert.Equal(BuildResult.Ok, g.Do(new SetLineCommand { LineId = line.Id, Line = faster }));
            Assert.Equal(cash - 204_000, g.State.Cash);
            Assert.Equal(3, Infrastructure.Vehicles(tb, g.State.Lines[line.Id]));
            Assert.True(g.Undo());
            Assert.Equal(15, g.State.Lines[line.Id].HeadwayMinutes);
            Assert.Equal(cash - 144_000, g.State.Cash);

            // chiusura: si recupera il 20% di fermate e mezzi
            Assert.Equal(BuildResult.Ok, g.Do(new SetLineCommand { LineId = line.Id }));
            Assert.Empty(g.State.Lines);
            Assert.Equal(cash - 144_000 + 28_800, g.State.Cash);
            Assert.Equal(0, Economy.Compute(g.Context).TransitCosts);
            Assert.True(g.Undo());
            Assert.Single(g.State.Lines);

            // regole: frequenza, biglietto, fermate troppo vicine
            Assert.Equal(BuildResult.NotAllowed, g.Do(new SetLineCommand { Line = Line(7) }));
            var expensive = Line();
            expensive.FareCents = 500;
            Assert.Equal(BuildResult.NotAllowed, g.Do(new SetLineCommand { Line = expensive }));
            var close = Line();
            close.Stops = new float[] { 0, 0, 50, 0 };
            Assert.Equal(BuildResult.InvalidPath, g.Do(new SetLineCommand { Line = close }));
            var train = Line();
            train.Mode = TransitMode.Train;
            Assert.Equal(BuildResult.NotAllowed, g.Do(new SetLineCommand { Line = train }));

            // salvataggio, e a fine giornata il servizio si paga
            var loaded = Reload(g, b);
            var copy = Assert.Single(loaded.State.Lines.Values);
            Assert.Equal(line.Stops, copy.Stops);
            Assert.Equal(15, copy.HeadwayMinutes);
            loaded.Advance(TimeSpan.FromHours(24));
            var day = loaded.State.Ledger.First(d => d.Date == new DateTime(2026, 11, 16));
            Assert.Equal(-964, day.Amounts[(int)LedgerCategory.TransitCosts]);
            Assert.Equal(loaded.State.LedgerTotal, loaded.State.Cash - loaded.State.StartCash);
        }

        [Fact]
        public void LaFermataDeveStareVicinoAUnaStrada()
        {
            var world = new FrameWorld();
            var g = GameSession.New("Prova", new WorldConfig(), new DateTime(2026, 11, 16, 10, 0, 0), DateTime.UtcNow, 1, TestGame.Catalog(), GameBalance(), world, Missions.Parse(TestGame.MissionsJson));
            g.Context.Frame = world.Frame;
            var l = Line();
            // la strada del tile è a y = 250: fermate a 260 sì, a 200 no
            l.Stops = world.Region(20, 260, 200, 260);
            Assert.Equal(BuildResult.Ok, g.Do(new SetLineCommand { Line = l }));
            l.Stops = world.Region(20, 260, 200, 200);
            Assert.Equal(BuildResult.NotNearRoad, g.Do(new SetLineCommand { Line = l }));
            // dopo una strada del sindaco lì vicino, sì
            Assert.Equal(BuildResult.Ok, g.Do(new BuildRoadCommand { Points = world.Region(150, 190, 260, 190) }));
            Assert.Equal(BuildResult.Ok, g.Do(new SetLineCommand { Line = l }));
        }

        [Fact]
        public void LineeDelComuneNellaReteNeiDueSensi()
        {
            var net = TransitNetwork.Empty(Transit.Value);
            var l = Line();
            l.Id = 9;
            l.Ref = "C1";
            var g = new GameState();
            g.Lines[9] = l;
            net.SetCityLines(Infrastructure.NetworkLines(g));
            Assert.Equal(2, net.Lines.Count);
            Assert.All(net.Lines, x => Assert.Equal(9, x.CityId));
            int ab = net.Minutes(0, 50, 1500, 50, out var la);
            int ba = net.Minutes(1500, 50, 0, 50, out var lb);
            Assert.True(ab > 0 && ba > 0);
            Assert.Equal(ab, ba);
            Assert.NotEqual(la, lb);
            Assert.True(net.IsCityLine(la) && net.IsCityLine(lb));
            Assert.Equal(130, net.FareCents(la));
            net.Ride(la);
            net.Ride(lb);
            net.Ride(lb);
            Assert.Equal(3, net.CityPassengers(9));
            // ridisegnando le linee i passeggeri restano; chiudendole spariscono dalla rete
            net.SetCityLines(Infrastructure.NetworkLines(g));
            Assert.Equal(3, net.CityPassengers(9));
            net.SetCityLines(null);
            Assert.Empty(net.Lines);
            Assert.Equal(-1, net.Minutes(0, 50, 1500, 50, out _));
        }
    }
}
