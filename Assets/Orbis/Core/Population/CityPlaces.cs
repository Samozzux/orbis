using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geo;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Population
{
    /// <summary>
    /// Case, posti di lavoro e scuole di un comune presi dai tile del region pack (edifici e attività reali di
    /// OpenStreetMap). Le stime (superficie per abitante, posti per m²) vengono da <see cref="PopulationBalance"/>.
    /// </summary>
    public sealed class CityPlaces
    {
        public readonly List<Dwelling> Dwellings = new List<Dwelling>();
        public readonly List<Place> Workplaces = new List<Place>();
        public readonly List<Place> Schools = new List<Place>();
        /// <summary>Peso residenziale di ogni casa (1 per gli edifici sicuramente residenziali, meno per quelli incerti).</summary>
        public readonly List<float> DwellingWeights = new List<float>();

        private readonly WorldFrame _frame;
        private readonly MunicipalityInfo _municipality;
        private readonly PopulationBalance _b;

        /// <summary>Elenco vuoto da riempire a mano (test, città senza pack).</summary>
        public CityPlaces(PopulationBalance balance) => _b = balance;

        public CityPlaces(RegionInfo region, MunicipalityInfo municipality, PopulationBalance balance)
        {
            _frame = region.CreateFrame();
            _municipality = municipality;
            _b = balance;
        }

        /// <summary>Tile del pack che toccano il riquadro del comune (da caricare prima di <see cref="Add"/>).</summary>
        public static List<TileId> TilesFor(RegionInfo region, MunicipalityInfo m)
        {
            var frame = region.CreateFrame();
            frame.FromLonLat(m.MinLon, m.MinLat, out var x0, out var y0);
            frame.FromLonLat(m.MaxLon, m.MaxLat, out var x1, out var y1);
            var a = frame.TileAt(region.Zoom, x0, y0);
            var c = frame.TileAt(region.Zoom, x1, y1);
            var list = new List<TileId>();
            for (int x = Math.Min(a.X, c.X); x <= Math.Max(a.X, c.X); x++)
                for (int y = Math.Min(a.Y, c.Y); y <= Math.Max(a.Y, c.Y); y++)
                {
                    var id = new TileId(region.Zoom, x, y);
                    if (region.Tiles.ContainsKey(id)) list.Add(id);
                }
            return list;
        }

        /// <summary>Aggiunge case, lavori e scuole del tile che cadono nel comune.</summary>
        public void Add(TileData tile)
        {
            if (_frame == null) throw new InvalidOperationException("CityPlaces senza regione: i tile non si possono aggiungere");
            _frame.TileOrigin(tile.Id, out var ox, out var oy);
            var counted = new bool[tile.Buildings.Length];
            for (int i = 0; i < tile.Buildings.Length; i++)
            {
                var b = tile.Buildings[i];
                if (b.HasFlag(BuildingFlag.Underground)) continue;
                float cx = (b.Bounds.MinX + b.Bounds.MaxX) * 0.5f, cy = (b.Bounds.MinY + b.Bounds.MaxY) * 0.5f;
                double wx = ox + cx, wy = oy + cy;
                if (!Inside(wx, wy)) continue;
                double footprint = Area(b.Outer);
                foreach (var h in b.Holes) footprint -= Area(h);
                if (footprint < _b.MinFootprintM2) continue;
                int levels = Levels(b);
                double floor = footprint * levels;
                string use = b.Use.ToString();

                if (_b.ResidentialWeight.TryGetValue(use, out var weight) && weight > 0
                    && (b.Use != BuildingUse.Unknown || footprint <= _b.UnknownMaxFootprintM2))
                {
                    Dwellings.Add(new Dwelling
                    {
                        OsmId = b.OsmId,
                        TileX = tile.Id.X,
                        TileY = tile.Id.Y,
                        X = (float)wx,
                        Y = (float)wy,
                        FloorArea = (float)floor,
                        Units = Math.Max(1, (int)Math.Round(floor / _b.UnitM2)),
                        Use = b.Use,
                        Address = b.Address ?? "",
                    });
                    DwellingWeights.Add((float)weight);
                    continue;
                }

                if (_b.JobsPerM2.TryGetValue(use, out var perM2) && perM2 > 0)
                {
                    counted[i] = true;
                    Workplaces.Add(NewPlace(b.OsmId, tile, wx, wy, PlaceLevel.Work, use, b.Name,
                        Math.Max(1, Math.Min(_b.MaxJobsPerPlace, (int)Math.Round(floor * perM2)))));
                }
                var level = SchoolLevel(b.Use);
                if (level != PlaceLevel.Work)
                {
                    double perStudent = level == PlaceLevel.Kindergarten ? _b.KindergartenChildrenPerM2
                        : level == PlaceLevel.University ? _b.UniversityStudentsPerM2 : _b.StudentsPerM2;
                    Schools.Add(NewPlace(b.OsmId, tile, wx, wy, level, use, b.Name, Math.Max(10, (int)Math.Round(floor * perStudent))));
                }
            }

            foreach (var p in tile.Pois)
            {
                double wx = ox + p.X, wy = oy + p.Y;
                if (!Inside(wx, wy) || string.IsNullOrEmpty(p.Category)) continue;
                bool inCounted = p.Building >= 0 && p.Building < counted.Length && counted[p.Building];
                var schoolLevel = PoiSchoolLevel(p.Category);
                if (schoolLevel != PlaceLevel.Work && !(p.Building >= 0 && p.Building < tile.Buildings.Length
                                                       && SchoolLevel(tile.Buildings[p.Building].Use) != PlaceLevel.Work))
                {
                    _b.PoiStudents.TryGetValue(p.Category, out var students);
                    if (students > 0) Schools.Add(NewPlace(p.OsmId, tile, wx, wy, schoolLevel, p.Category, p.Name, students));
                }
                if (inCounted)
                {
                    // l'edificio è già un posto di lavoro: prende il nome dell'attività se non ne ha uno
                    var host = FindPlace(tile, tile.Buildings[p.Building].OsmId);
                    if (host != null && string.IsNullOrEmpty(host.Name)) host.Name = p.Name ?? "";
                    continue;
                }
                int jobs = PoiJobs(p.Category);
                if (jobs > 0) Workplaces.Add(NewPlace(p.OsmId, tile, wx, wy, PlaceLevel.Work, p.Category, p.Name, jobs));
            }
        }

        private Place FindPlace(TileData tile, long osmId)
        {
            for (int i = Workplaces.Count - 1; i >= 0; i--)
                if (Workplaces[i].OsmId == osmId && Workplaces[i].TileX == tile.Id.X && Workplaces[i].TileY == tile.Id.Y) return Workplaces[i];
            return null;
        }

        private Place NewPlace(long osmId, TileData tile, double x, double y, PlaceLevel level, string category, string name, int capacity)
        {
            int min = 0;
            if (_b.MinEducation.TryGetValue(category, out var m)) min = m;
            return new Place
            {
                OsmId = osmId,
                TileX = tile.Id.X,
                TileY = tile.Id.Y,
                X = (float)x,
                Y = (float)y,
                Level = level,
                Category = category,
                Name = name ?? "",
                Capacity = capacity,
                MinEducation = (Education)Math.Max(0, Math.Min(4, min)),
            };
        }

        private int PoiJobs(string category)
        {
            if (_b.PoiJobs.TryGetValue(category, out var j)) return j;
            int slash = category.IndexOf('/');
            return slash > 0 && _b.PoiJobs.TryGetValue(category.Substring(0, slash), out j) ? j : 0;
        }

        private bool Inside(double x, double y)
        {
            if (_municipality == null) return true;
            _frame.ToLonLat(x, y, out var lon, out var lat);
            return _municipality.Contains(lon, lat);
        }

        private int Levels(BuildingInfo b)
        {
            int levels = b.Levels > 0 ? b.Levels : (int)Math.Round(Math.Max(0, b.Height - b.MinHeight) / _b.FloorHeightM);
            return Math.Max(1, Math.Min(_b.MaxLevels, levels));
        }

        private static PlaceLevel SchoolLevel(BuildingUse use)
        {
            switch (use)
            {
                case BuildingUse.Kindergarten: return PlaceLevel.Kindergarten;
                case BuildingUse.School: return PlaceLevel.School;
                case BuildingUse.University: return PlaceLevel.University;
                default: return PlaceLevel.Work;
            }
        }

        private static PlaceLevel PoiSchoolLevel(string category)
        {
            switch (category)
            {
                case "amenity/kindergarten": return PlaceLevel.Kindergarten;
                case "amenity/school": return PlaceLevel.School;
                case "amenity/college":
                case "amenity/university": return PlaceLevel.University;
                default: return PlaceLevel.Work;
            }
        }

        /// <summary>Area con la formula dell'area di Gauss (anello [x0, y0, x1, y1…], metri²).</summary>
        public static double Area(float[] ring)
        {
            double s = 0;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
                s += (double)ring[2 * j] * ring[2 * i + 1] - (double)ring[2 * i] * ring[2 * j + 1];
            return Math.Abs(s) * 0.5;
        }
    }
}
