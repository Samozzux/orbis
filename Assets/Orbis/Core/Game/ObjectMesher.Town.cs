using System;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>Case, condomini, grattacieli, negozi, uffici e grandi superfici di vendita.</summary>
    public sealed partial class ObjectMesher
    {
        private static readonly uint[] RowColors = { 0x1E3C08A, 0x1E8BCA4, 0x1F1E6CF, 0x1D9C27E, 0x1E6D5B8, 0x1CFA98C };
        private static readonly uint[] AwningColors = { 0xC8372D, 0x2E7D4F, 0x1F5FA8, 0xE8A317, 0x2A9D8F, 0xD9622B, 0x7B2D5B };

        // ------------------------------------------------------------------ case

        private void House(CatalogItem item, uint wall, uint roof, float lv)
        {
            int floors = ParseInt(item.Param("floors", "1"), 1);
            var shape = ParseRoof(item.Param("roof", "hipped"));
            float h = item.Height;
            switch (item.Param("type", ""))
            {
                case "row": RowHouses(item, wall, roof, floors, Math.Max(2, (int)Math.Round(_w / 6f))); return;
                case "semi": RowHouses(item, wall, roof, floors, 2); return;
                case "modernvilla": ModernVilla(item, wall); return;
                case "modern": ModernHouse(item, wall); return;
                case "casale": Casale(item, wall, roof, false); return;
                case "courtyard": Palazzo(item, wall, roof, floors); return;
                case "condo": Condo(item, wall, floors, false); return;
                case "tower": Condo(item, wall, floors, true); return;
                case "loft": Loft(item, wall, floors); return;
                case "liberty": LibertyVilla(item, wall, roof, floors); return;
                case "skyscraper": GlassTower(item, wall, floors, true); return;
                case "portico": PorticoHouse(item, wall, roof, floors); return;
                case "borgo": TownHouse(h, shape, wall != 0 ? wall : 0x1CDBB9Au, roof, floors, WallMaterial.Stone, false); return;
            }
            TownHouse(h, shape, wall, roof, floors, WallMaterial.Unknown, true);
        }

        /// <summary>
        /// Casa di paese: un po' rientrata dal bordo, vialetto fino al portone, giardinetto con la siepe o il muretto,
        /// gronde, comignoli, fioriere e (dal terzo piano) balconi.
        /// </summary>
        private void TownHouse(float h, RoofShape shape, uint wall, uint roof, int floors, WallMaterial material, bool garden)
        {
            // dal terzo piano ci sono i balconi (sporgono 1,15 m con le piante)
            float side = floors >= 3 ? 1.2f : (_w < 9 ? 0.55f : 0.8f);
            float front = Math.Min(2.2f, Math.Max(1.1f, _d * 0.15f));
            float back = side;
            Yard(side, front, back, garden);
            var b = Building(side, front, _w - side, _d - back, h, shape, wall, roof, floors, floors >= 4 ? BuildingUse.Apartments : BuildingUse.House,
                false, float.NaN, null, material);
            Dress(b, DressKind.Residential);
            // gradino del portone
            float g = Ground(_w / 2, front);
            PaintedBox(_w / 2 - 0.8f, front - 0.45f, _w / 2 + 0.8f, front, g - 0.1f, g + 0.16f, 0x1C9C2B5);
        }

        /// <summary>Giardino attorno alla casa: prato, vialetto in pietra, muretto con cancello sul fronte e siepi ai lati.</summary>
        private void Yard(float side, float front, float back, bool garden)
        {
            if (garden) Lawn(0, 0, _w, _d);
            else Paving(0, 0, _w, _d, 0x1C8BFAE);
            Paving(_w / 2 - 0.7f, 0, _w / 2 + 0.7f, front + 0.2f, 0x1D2C9B8, 0.1f);
            if (!garden) return;
            // muretto intonacato con la copertina, interrotto dal cancello
            float gate = 1.6f;
            foreach (var (a, b) in new[] { (0.05f, _w / 2 - gate / 2), (_w / 2 + gate / 2, _w - 0.05f) })
            {
                if (b - a < 0.3f) continue;
                Range(a, 0.05f, b, 0.3f, out var lo, out var hi);
                PaintedBox(a, 0.05f, b, 0.3f, lo - 0.2f, hi + 0.75f, 0x1E2DCCF);
                PaintedBox(a - 0.02f, 0.03f, b + 0.02f, 0.32f, hi + 0.75f, hi + 0.82f, 0x1BFB6A6);
            }
            // cancello in ferro
            MetalFence(_w / 2 - gate / 2, 0.17f, _w / 2 + gate / 2, 0.17f, 1.1f, 0x1222624, false);
            if (side > 0.5f)
            {
                HedgeRow(0.05f, 0.4f, Math.Min(0.5f, side - 0.05f), _d - 0.05f, 1.2f);
                HedgeRow(_w - Math.Min(0.5f, side - 0.05f), 0.4f, _w - 0.05f, _d - 0.05f, 1.2f);
            }
            if (back > 0.5f) HedgeRow(0.5f, _d - Math.Min(0.5f, back - 0.05f), _w - 0.5f, _d - 0.05f, 1.2f);
        }

        /// <summary>Case a schiera o bifamiliare: unità affiancate con il proprio colore, il proprio ingresso e il giardinetto.</summary>
        private void RowHouses(CatalogItem item, uint wall, uint roof, int floors, int units)
        {
            var shape = ParseRoof(item.Param("roof", "gabled"));
            float side = 0.6f, front = Math.Min(2.6f, _d * 0.24f), back = 0.6f;
            Lawn(0, 0, _w, _d);
            float uw = (_w - 2 * side) / units;
            _party = new float[units - 1];
            for (int k = 1; k < units; k++) _party[k - 1] = side + k * uw;
            for (int k = 0; k < units; k++)
            {
                float u0 = side + k * uw, u1 = u0 + uw;
                uint col = k == 0 && wall != 0 ? wall : RowColors[(int)((k + Frac(3) * 6) % RowColors.Length)];
                float h = item.Height * (k % 2 == 0 ? 1f : 0.95f);
                // vialetto e muretto di ogni unità
                float mid = (u0 + u1) / 2;
                Paving(mid - 0.6f, 0, mid + 0.6f, front + 0.2f, 0x1D2C9B8, 0.1f);
                if (k > 0) HedgeRow(u0 - 0.15f, 0.3f, u0 + 0.15f, front - 0.1f, 0.9f);
                var b = Building(u0, front, u1, _d - back, h, shape, col, roof, floors, BuildingUse.Terrace);
                Dress(b, DressKind.Residential, mid);
                float g = Ground(mid, front);
                PaintedBox(mid - 0.6f, front - 0.4f, mid + 0.6f, front, g - 0.1f, g + 0.15f, 0x1C9C2B5);
            }
            HedgeRow(0.05f, 0.05f, _w - 0.05f, 0.3f, 0.8f);
            _party = null;
        }

        /// <summary>Villa moderna: volumi bianchi a tetto piano sfalsati, grandi vetrate, piscina con il solarium, palme e siepi.</summary>
        private void ModernVilla(CatalogItem item, uint wall)
        {
            uint white = wall != 0 ? wall : 0x1F2F0EAu;
            Lawn(0, 0, _w, _d);
            float vMain = _d * 0.42f;
            var a = Building(1.1f, vMain, _w * 0.64f, _d - 1.1f, Math.Min(item.Height, 7.0f), RoofShape.Flat, white, 0x1D8D4CC, 2, BuildingUse.Office);
            Dress(a, DressKind.Modern);
            float g = FloorZ(0);
            var b = Building(_w * 0.58f, vMain + 1.6f, _w - 1.1f, _d - 1.1f, 3.6f, RoofShape.Flat, 0x1D2C7B4, 0x1D8D4CC, 1, BuildingUse.Office,
                false, float.NaN, null, WallMaterial.Stone);
            Dress(b, DressKind.Modern);
            // vetrata a tutta altezza del soggiorno
            Glazing(1.6f, vMain, _w * 0.5f, vMain, g, g + 2.9f, 1);
            // pensilina a sbalzo sopra la vetrata
            PaintedBox(1.1f, vMain - 1.4f, _w * 0.55f, vMain, g + 3.0f, g + 3.25f, 0x1F4F2EE);
            // solarium e piscina
            float pu0 = _w * 0.56f, pu1 = _w - 1.4f, pv0 = 1.4f, pv1 = vMain - 0.6f;
            if (pu1 - pu0 > 3 && pv1 - pv0 > 2.5f)
            {
                float pg = Base(pu0 - 0.8f, pv0 - 0.6f, pu1 + 0.6f, pv1 + 0.4f);
                Flat(pu0 - 0.8f, pv0 - 0.6f, pu1 + 0.6f, pv1 + 0.4f, pg + 0.1f, 0x1E8E0D0, 10);
                Flat(pu0, pv0, pu1, pv1, pg + 0.06f, 0x14FB3D9, 11);
                PaintedBox(pu0 - 0.05f, pv0 - 0.05f, pu1 + 0.05f, pv0, pg, pg + 0.14f, 0x1F2F0EA);
                for (int k = 0; k < 2; k++)
                {
                    float su = pu0 - 0.6f + k * 1.1f;
                    PaintedBox(su, pv1 - 1.9f, su + 0.65f, pv1 - 0.1f, pg + 0.1f, pg + 0.42f, 0x1F4F4F2);
                }
            }
            // terrazza davanti al soggiorno e vialetto d'ingresso
            Paving(1.1f, vMain - 2.2f, _w * 0.55f, vMain, 0x1D9D0BE, 0.1f);
            Paving(1.6f, 0, 3.2f, vMain - 2.2f, 0x1D2C9B8, 0.1f);
            HedgeRow(0.05f, 0.05f, 1.4f, _d - 0.05f, 1.4f);
            HedgeRow(_w - 0.6f, 0.05f, _w - 0.05f, _d - 0.05f, 1.4f);
            HedgeRow(3.4f, 0.05f, _w - 0.6f, 0.5f, 1.0f);
            Tree(PlantKind.Palm, _w * 0.5f, 1.8f, 1);
            Tree(PlantKind.Olive, _w * 0.3f, _d * 0.2f, 2);
            if (_level >= 3) Car(4.6f, vMain - 3.8f, 0.0f, 31);
        }

        /// <summary>Casa moderna: cubo bianco a due piani con il volume del primo piano a sbalzo, vetrate e terrazza.</summary>
        private void ModernHouse(CatalogItem item, uint wall)
        {
            uint white = wall != 0 ? wall : 0x1EFEDE8u;
            Lawn(0, 0, _w, _d);
            float front = 2.2f;
            var a = Building(0.8f, front, _w * 0.62f, _d - 0.8f, item.Height, RoofShape.Flat, white, 0x1D8D4CC, 2, BuildingUse.Office);
            Dress(a, DressKind.Modern);
            var b = Building(_w * 0.55f, front + 1.2f, _w - 0.8f, _d - 0.8f, item.Height * 0.5f, RoofShape.Flat, 0x1B9A48C, 0x1D8D4CC, 1, BuildingUse.Office,
                false, float.NaN, null, WallMaterial.Wood);
            Dress(b, DressKind.Modern);
            Paving(1.2f, 0, 2.8f, front, 0x1D2C9B8, 0.1f);
            Paving(_w * 0.62f, 0.4f, _w - 0.8f, front + 1.2f, 0x1BDB6A8, 0.1f);
            HedgeRow(0.05f, 0.05f, 1.0f, 0.4f, 0.9f);
            HedgeRow(3.0f, 0.05f, _w - 0.05f, 0.4f, 0.9f);
            Tree(PlantKind.Olive, _w * 0.82f, _d * 0.85f, 4);
            if (_level >= 2 && _w >= 9) Car(_w - 2.4f, 1.6f, 0, 41);
        }

        /// <summary>Casa con il portico ad archi sul fronte (colonne, travi e terrazzo sopra).</summary>
        private void PorticoHouse(CatalogItem item, uint wall, uint roof, int floors)
        {
            float side = 0.7f, front = 3.2f;
            Yard(side, front, side, true);
            var b = Building(side, front, _w - side, _d - side, item.Height, ParseRoof(item.Param("roof", "hipped")), wall, roof, floors, BuildingUse.House);
            Dress(b, DressKind.Residential);
            float g = FloorZ(0), z1 = FloorZ(1);
            uint stone = 0x1E6DCC8;
            int n = Math.Max(3, (int)((_w - 2 * side) / 2.6f));
            for (int k = 0; k <= n; k++)
            {
                float u = side + 0.3f + (_w - 2 * side - 0.6f) * k / n;
                Pipe(u, front - 1.9f, 0.18f, g, z1 - 0.3f, stone, 10);
                PaintedBox(u - 0.24f, front - 2.14f, u + 0.24f, front - 1.66f, z1 - 0.35f, z1 - 0.2f, stone);
            }
            // trave e terrazzo con la balaustra
            PaintedBox(side, front - 2.2f, _w - side, front, z1 - 0.2f, z1, Lighten(stone, 0.05f));
            Flat(side, front - 2.2f, _w - side, front, z1 + 0.01f, 0x1C47A5A, 2);
            PaintedBox(side, front - 2.2f, _w - side, front - 2.05f, z1, z1 + 0.95f, stone);
            Paving(side, front - 2.2f, _w - side, front, 0x1C9A27A, 0.11f);
        }

        /// <summary>Casale abruzzese in pietra: scala esterna fino al loggiato del primo piano, pergolato e aia in terra battuta.</summary>
        private void Casale(CatalogItem item, uint wall, uint roof, bool agriturismo)
        {
            uint stone = wall != 0 ? wall : 0x1CDBB9Au;
            float side = 1.0f, front = Math.Min(3.4f, _d * 0.28f);
            Ground(0, 0, _w, _d, 0x1BFAE8C, 0.05f, 10);
            Lawn(0, _d * 0.55f, _w, _d);
            float bu1 = agriturismo ? _w * 0.62f : _w - side;
            var b = Building(side, front, bu1, _d - side, item.Height, RoofShape.Gabled, stone, roof != 0 ? roof : 0x1B25A3Cu, 2, BuildingUse.Farm,
                false, float.NaN, null, WallMaterial.Stone);
            Dress(b, DressKind.Residential);
            // scala esterna in pietra lungo la facciata fino al pianerottolo del primo piano
            float g = FloorZ(0), z1 = FloorZ(1);
            float su0 = side + 0.4f, steps = Math.Max(6, (int)((z1 - g) / 0.24f));
            float run = Math.Min(4.6f, (bu1 - side) * 0.5f);
            for (int k = 0; k < steps; k++)
            {
                float u = su0 + run * k / steps;
                PaintedBox(u, front - 1.1f, su0 + run, front, g - 0.2f, g + (z1 - g) * (k + 1) / steps, Darken(stone, 0.05f));
            }
            // pianerottolo coperto (loggia) con il parapetto
            float lu0 = su0 + run, lu1 = lu0 + 2.0f;
            PaintedBox(lu0, front - 1.5f, lu1, front, g - 0.2f, z1, Darken(stone, 0.05f));
            PaintedBox(su0, front - 1.15f, lu1, front - 1.0f, z1 - 0.05f, z1 + 0.9f, stone, 13);
            foreach (float u in new[] { lu0 + 0.15f, lu1 - 0.15f })
                Pipe(u, front - 1.35f, 0.12f, z1, z1 + 2.4f, 0x18A6A4A, 8);
            Poly(new[] { lu0 - 0.2f, front - 1.7f, z1 + 2.3f, lu1 + 0.2f, front - 1.7f, z1 + 2.3f, lu1 + 0.2f, front, z1 + 2.75f, lu0 - 0.2f, front, z1 + 2.75f },
                0, -0.3f, 1, roof != 0 ? roof : 0x1B25A3Cu, 1, 1);
            // pergolato con la vite sul lato
            float pu0 = agriturismo ? bu1 + 0.4f : _w * 0.55f, pu1 = agriturismo ? _w - 0.6f : _w - 1.2f;
            float pv0 = agriturismo ? front : 0.6f, pv1 = agriturismo ? _d - 1.2f : front - 0.4f;
            if (pu1 - pu0 > 2 && pv1 - pv0 > 1.5f)
            {
                float pg = Mid(pu0, pv0, pu1, pv1);
                foreach (float u in new[] { pu0, pu1 })
                    foreach (float v in new[] { pv0, pv1 })
                        Beam(u, v, pg - 0.1f, u, v, pg + 2.5f, 0.07f, 0x17A5A3C);
                for (float u = pu0; u <= pu1 + 0.01f; u += Math.Max(0.6f, (pu1 - pu0) / 5))
                    Beam(u, pv0, pg + 2.5f, u, pv1, pg + 2.5f, 0.05f, 0x17A5A3C);
                Foliage(pu0, pv0, pu1, pv1, pg + 2.5f, pg + 2.75f, 0x1557A35, 0.05f);
                if (agriturismo)
                {
                    // tavoli all'aperto sotto il pergolato
                    for (float v = pv0 + 1.2f; v + 1 < pv1; v += 2.6f)
                    {
                        float tu = (pu0 + pu1) / 2;
                        PaintedBox(tu - 1.2f, v - 0.45f, tu + 1.2f, v + 0.45f, pg + 0.7f, pg + 0.76f, 0x19C7350);
                        PaintedBox(tu - 1.0f, v - 0.35f, tu + 1.0f, v + 0.35f, pg, pg + 0.7f, 0x16E5238);
                    }
                }
            }
            // aia: balle di fieno, ulivo e botte
            Pipe(_w - 1.6f, _d * 0.8f, 0.6f, Ground(_w - 1.6f, _d * 0.8f), Ground(_w - 1.6f, _d * 0.8f) + 1.1f, 0x1D9B44A, 10);
            Tree(PlantKind.Olive, side + 1.2f, _d - 1.3f, 7);
            if (_level >= 3) Tree(PlantKind.Cypress, _w - 0.9f, 0.9f, 8);
        }

        /// <summary>Palazzo nobiliare con il cortile interno, il portone ad arco e il bugnato al piano terra.</summary>
        private void Palazzo(CatalogItem item, uint wall, uint roof, int floors)
        {
            float side = 1.2f, front = 1.6f;
            Paving(0, 0, _w, _d, 0x1C8BFAE);
            float u0 = side, u1 = _w - side, v0 = front, v1 = _d - side;
            float depth = Math.Min(6.5f, Math.Min(u1 - u0, v1 - v0) * 0.32f);
            var court = new[] { u0 + depth, v0 + depth, u1 - depth, v1 - depth };
            // quattro ali col tetto a padiglione attorno al cortile (come i palazzi veri)
            uint col = wall != 0 ? wall : 0x1E3C9A0u;
            var shape = ParseRoof(item.Param("roof", "hipped"));
            Building(u0, v1 - depth, u1, v1, item.Height, shape, col, roof, floors, BuildingUse.Apartments);
            Building(u0, v0 + depth - 0.01f, u0 + depth, v1 - depth + 0.01f, item.Height, shape, col, roof, floors, BuildingUse.Apartments);
            Building(u1 - depth, v0 + depth - 0.01f, u1, v1 - depth + 0.01f, item.Height, shape, col, roof, floors, BuildingUse.Apartments);
            var b = Building(u0, v0, u1, v0 + depth, item.Height, shape, col, roof, floors, BuildingUse.Apartments);
            Dress(b, DressKind.Residential);
            float g = FloorZ(0), z1 = FloorZ(1);
            // bugnato: fascia in pietra del piano terra (solo ai lati del portone) e portone ad arco
            uint stone = 0x1D6CBB4;
            float mid = _w / 2;
            PaintedBox(u0 - 0.06f, v0 - 0.08f, mid - 2.2f, v0, g - 0.3f, g + 0.9f, stone);
            PaintedBox(mid + 2.2f, v0 - 0.08f, u1 + 0.06f, v0, g - 0.3f, g + 0.9f, stone);
            PaintedBox(mid - 2.0f, v0 - 0.3f, mid - 1.5f, v0, g, z1 - 0.3f, stone);
            PaintedBox(mid + 1.5f, v0 - 0.3f, mid + 2.0f, v0, g, z1 - 0.3f, stone);
            PaintedBox(mid - 2.0f, v0 - 0.3f, mid + 2.0f, v0, z1 - 0.6f, z1 - 0.1f, stone);
            Poly(new[] { mid - 1.5f, v0 - 0.02f, g, mid + 1.5f, v0 - 0.02f, g, mid + 1.5f, v0 - 0.02f, z1 - 0.6f, mid - 1.5f, v0 - 0.02f, z1 - 0.6f },
                0, -1, 0, 0x1433022, BuildingDecorator.KindProp, 0);
            // balcone sopra il portone
            float zb = z1;
            PaintedBox(mid - 2.2f, v0 - 1.1f, mid + 2.2f, v0, zb - 0.18f, zb, stone);
            for (float u = mid - 2.1f; u <= mid + 2.1f; u += 0.35f) Beam(u, v0 - 1.02f, zb, u, v0 - 1.02f, zb + 0.9f, 0.05f, stone);
            PaintedBox(mid - 2.2f, v0 - 1.1f, mid + 2.2f, v0 - 0.95f, zb + 0.9f, zb + 1.0f, stone);
            // cortile: pavimento, pozzo e un albero
            float cu = (court[0] + court[2]) / 2, cv = (court[1] + court[3]) / 2;
            Flat(court[0], court[1], court[2], court[3], g + 0.05f, 0x1BDB09A, 10);
            Pipe(cu, cv, 0.7f, g, g + 0.8f, stone, 12);
            Tree(PlantKind.Broadleaf, cu + 1.5f, cv + 1.0f, 3);
        }

        /// <summary>Villa liberty: corpo a tre piani con la torretta d'angolo più alta, giardino con la cancellata e le palme.</summary>
        private void LibertyVilla(CatalogItem item, uint wall, uint roof, int floors)
        {
            uint col = wall != 0 ? wall : 0x1EBD3B8u;
            float side = 1.6f, front = 2.6f;
            Lawn(0, 0, _w, _d);
            var b = Building(side, front, _w - side, _d - side, item.Height, RoofShape.Hipped, col, roof, floors, BuildingUse.House);
            Dress(b, DressKind.Residential);
            // torretta d'angolo che sporge dal fronte
            float t = Math.Min(4.2f, (_w - 2 * side) * 0.32f);
            var tw = Building(side - 0.6f, front - 0.8f, side - 0.6f + t, front - 0.8f + t, item.Height + 2.6f, RoofShape.Pyramidal,
                Lighten(col, 0.05f), roof, floors + 1, BuildingUse.House);
            Dress(tw, DressKind.Residential);
            // cancellata in ferro battuto con i pilastri e il viale
            Paving(_w / 2 - 0.9f, 0, _w / 2 + 0.9f, front, 0x1D2C9B8, 0.1f);
            MetalFence(0.2f, 0.2f, _w / 2 - 1.2f, 0.2f, 1.6f, 0x1202422, false);
            MetalFence(_w / 2 + 1.2f, 0.2f, _w - 0.2f, 0.2f, 1.6f, 0x1202422, false);
            foreach (float u in new[] { _w / 2 - 1.3f, _w / 2 + 1.1f })
            {
                float g = Ground(u, 0.2f);
                PaintedBox(u, 0.05f, u + 0.25f, 0.35f, g - 0.2f, g + 1.9f, 0x1E6DCC8);
            }
            HedgeRow(0.05f, 0.5f, 0.6f, _d - 0.05f, 1.6f);
            HedgeRow(_w - 0.6f, 0.5f, _w - 0.05f, _d - 0.05f, 1.6f);
            Tree(PlantKind.Palm, _w - 1.0f, 1.2f, 9);
            Tree(PlantKind.Palm, 1.0f, _d - 1.0f, 10);
        }

        /// <summary>Loft in un'ex fabbrica: mattoni a vista, grandi finestre, terrazzi in ferro e la vecchia ciminiera.</summary>
        private void Loft(CatalogItem item, uint wall, int floors)
        {
            uint brick = wall != 0 ? wall : 0x1A8654Au;
            Paving(0, 0, _w, _d, 0x1B9B4AA);
            float side = 1.1f, front = 1.8f;
            var b = Building(side, front, _w - side - 2.2f, _d - side, item.Height, RoofShape.Flat, brick, 0x1A9A69E, floors, BuildingUse.Office,
                false, float.NaN, null, WallMaterial.Brick);
            Dress(b, DressKind.Modern);
            // terrazzini in ferro sul fronte
            for (int f = 1; f < floors; f++)
            {
                float z = FloorZ(f);
                for (float u = side + 1.0f; u + 3 < _w - side - 2.2f; u += 4.4f)
                {
                    PaintedBox(u, front - 1.0f, u + 3.0f, front, z - 0.12f, z, 0x1303438);
                    for (float x = u; x <= u + 3.01f; x += 0.5f) Beam(x, front - 0.95f, z, x, front - 0.95f, z + 1.0f, 0.02f, 0x1303438);
                    PaintedBox(u, front - 1.0f, u + 3.0f, front - 0.9f, z + 0.95f, z + 1.02f, 0x1303438);
                }
            }
            // ciminiera in mattoni rimasta dalla fabbrica
            float cu = _w - side - 0.9f, cv = _d - side - 1.4f, g = Ground(cu, cv);
            Cylinder(cu, cv, 0.9f, g - 0.3f, _z0 + item.Height * 1.2f, 10, 0x1A35C42, 1f, 0.7f, 0, 12);
            PaintedBox(cu - 1.0f, cv - 1.0f, cu + 1.0f, cv + 1.0f, g - 0.3f, g + 2.2f, 0x1A35C42);
            Tree(PlantKind.Broadleaf, _w - 1.2f, 1.2f, 11);
        }

        /// <summary>
        /// Condominio italiano: tetto piano con il parapetto, balconi continui su fronte e retro a ogni piano, tende da sole
        /// colorate su alcuni balconi, pensilina d'ingresso, aiuole e posti auto. La torre ha balconi su tutti i lati.
        /// </summary>
        private void Condo(CatalogItem item, uint wall, int floors, bool tower)
        {
            float bd = 1.3f, side = tower ? bd + 0.2f : 1.0f, front = 2.6f, back = bd + 0.2f;
            Lawn(0, 0, _w, _d);
            Paving(0, 0, _w, front, 0x1C9C1B2);
            var b = Building(side, front, _w - side, _d - back, item.Height, RoofShape.Flat, wall, 0x1A9A69E, floors, BuildingUse.Apartments);
            Dress(b, DressKind.Modern);
            uint col = _building.LastWallColor | 0x1000000;
            uint rail = Frac(21) < 0.5f ? Lighten(col, 0.25f) : 0x1E9ECECu;
            float u0 = side, u1 = _w - side, v0 = front, v1 = _d - back;
            for (int f = 1; f < floors; f++)
            {
                float z = FloorZ(f);
                BalconyRow(u0 + 0.4f, u1 - 0.4f, v0, -1, z, bd, rail, f * 7 + 1);
                BalconyRow(u0 + 0.4f, u1 - 0.4f, v1, 1, z, bd, rail, f * 7 + 2);
                if (tower)
                {
                    SideBalconies(u0, v0 + 0.6f, v1 - 0.6f, -1, z, bd, rail, f * 7 + 3);
                    SideBalconies(u1, v0 + 0.6f, v1 - 0.6f, 1, z, bd, rail, f * 7 + 4);
                }
            }
            // pensilina e portone d'ingresso
            float g = FloorZ(0), mid = _w / 2;
            PaintedBox(mid - 1.8f, front - 1.8f, mid + 1.8f, front, g + 2.7f, g + 2.95f, 0x1E6E6E2);
            Glazing(mid - 1.1f, front, mid + 1.1f, front, g, g + 2.6f, 1, 0x13A3E42);
            Sign(mid - 0.6f, front - 1.81f, mid + 0.6f, front - 1.81f, g + 2.72f, g + 2.92f, 0x1E9ECEC, false);
            // posti auto sul fronte, aiuole e alberi
            int cars = Math.Min(4, 1 + _level);
            for (int k = 0; k < cars; k++)
            {
                float cu = 2.8f + k * 4.8f;
                if (cu + 2.2f > mid - 2.0f) cu += 4.4f + 2.2f;
                if (cu + 2.3f > _w - 0.3f) break;
                Car(cu, 1.3f, 0, 50 + k);
            }
            Tree(PlantKind.Broadleaf, 0.6f, _d - 0.9f, 12);
            Tree(PlantKind.Broadleaf, _w - 0.6f, _d - 0.9f, 13);
            if (floors >= 6) RoofTanks(u0 + 2, v0 + 2, u1 - 2, v1 - 2, _building.LastEave);
        }

        /// <summary>Balconi su un lato lungo (normale ±V): solette a gruppi di due campate, parapetto, tende e piante.</summary>
        private void BalconyRow(float ua, float ub, float v, int dir, float z, float depth, uint rail, int salt)
        {
            float len = ub - ua;
            int groups = Math.Max(1, (int)Math.Round(len / 6.6f));
            float gw = len / groups;
            for (int k = 0; k < groups; k++)
            {
                float a = ua + k * gw + 0.35f, b = ua + (k + 1) * gw - 0.35f;
                float vo = v + dir * depth;
                float vIn = Math.Min(v, vo), vOut = Math.Max(v, vo);
                PaintedBox(a, vIn, b, vOut, z - 0.2f, z, 0x1DCD8D0);
                float vr0 = dir < 0 ? vIn : vOut - 0.1f, vr1 = vr0 + 0.1f;
                PaintedBox(a, vr0, b, vr1, z, z + 1.0f, rail);
                PaintedBox(a, vIn, a + 0.1f, vOut, z, z + 1.0f, rail);
                PaintedBox(b - 0.1f, vIn, b, vOut, z, z + 1.0f, rail);
                float r = Frac(salt * 31 + k);
                // tende da sole (più frequenti ai livelli alti), vasi di piante
                if (r < 0.18f + 0.08f * _level)
                {
                    uint c = Pick(AwningColors, salt * 13 + k);
                    float zt = z + 2.55f, ze = z + 1.85f;
                    Poly(new[] { a, v, zt, b, v, zt, b, vo, ze, a, vo, ze }, 0, dir * 0.4f, 1, c, BuildingDecorator.KindCanvas, Frac(salt + k) < 0.6f ? 1 : 0);
                    Poly(new[] { a, v, zt, b, v, zt, b, vo, ze, a, vo, ze }, 0, -dir * 0.4f, -1, Darken(c, 0.2f), BuildingDecorator.KindCanvas, 0);
                    Panel(a, vo, b, vo, ze - 0.3f, ze, c, BuildingDecorator.KindCanvas, 0);
                }
                if (Frac(salt * 7 + k * 3) < 0.25f + 0.1f * _level)
                {
                    float pu = a + 0.4f + (b - a - 0.8f) * Frac(salt + k * 5);
                    float pv = v + dir * depth * 0.55f;
                    Foliage(pu - 0.3f, pv - 0.25f, pu + 0.3f, pv + 0.25f, z, z + 0.7f + 0.1f * _level, 0x1476E34, 0.2f + 0.08f * _level);
                }
            }
        }

        /// <summary>Balconi d'angolo su un lato corto (normale ±U) delle torri.</summary>
        private void SideBalconies(float u, float va, float vb, int dir, float z, float depth, uint rail, int salt)
        {
            float uo = u + dir * depth;
            float uIn = Math.Min(u, uo), uOut = Math.Max(u, uo);
            float a = va + 0.2f, b = Math.Min(vb - 0.2f, va + 4.2f);
            PaintedBox(uIn, a, uOut, b, z - 0.2f, z, 0x1DCD8D0);
            float ur0 = dir < 0 ? uIn : uOut - 0.1f;
            PaintedBox(ur0, a, ur0 + 0.1f, b, z, z + 1.0f, rail);
            PaintedBox(uIn, a, uOut, a + 0.1f, z, z + 1.0f, rail);
            PaintedBox(uIn, b - 0.1f, uOut, b, z, z + 1.0f, rail);
            if (Frac(salt) < 0.3f)
                Foliage(u + dir * 0.6f - 0.25f, b - 0.8f, u + dir * 0.6f + 0.25f, b - 0.3f, z, z + 0.8f, 0x1476E34, 0.3f);
        }

        /// <summary>Locale macchine dell'ascensore e serbatoi sul tetto dei palazzi alti.</summary>
        private void RoofTanks(float u0, float v0, float u1, float v1, float z)
        {
            float cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
            PaintedBox(cu - 1.6f, cv - 1.3f, cu + 1.6f, cv + 1.3f, z, z + 2.6f, 0x1D8D4CC);
            Pipe(cu + 2.6f, cv, 0.6f, z, z + 1.4f, 0x1E8E8E8, 12);
            Beam(cu - 1.2f, cv + 0.4f, z + 2.6f, cu - 1.2f, cv + 0.4f, z + 5.0f, 0.03f, 0x1555555);
        }

        /// <summary>
        /// Torre di vetro (uffici o grattacielo): podio in pietra con l'atrio vetrato, torre a facciata continua che di
        /// notte si accende a finestre, rastremazione in cima, coronamento e antenna.
        /// </summary>
        private void GlassTower(CatalogItem item, uint wall, int floors, bool sky)
        {
            float H = item.Height;
            Paving(0, 0, _w, _d, 0x1CFC9BE);
            float podH = sky ? 9.6f : 4.8f;
            int podLevels = sky ? 2 : 1;
            var pod = Building(0.8f, 2.2f, _w - 0.8f, _d - 0.8f, podH, RoofShape.Flat, 0x1BCB6AA, 0x1A9A69E, podLevels, BuildingUse.Office,
                false, float.NaN, null, WallMaterial.Stone);
            Dress(pod, DressKind.Modern);
            float g = FloorZ(0);
            // atrio vetrato e pensilina
            Glazing(_w * 0.25f, 2.2f, _w * 0.75f, 2.2f, g, g + podH - 0.6f, podLevels, 0x1303438);
            PaintedBox(_w * 0.3f, 0.4f, _w * 0.7f, 2.2f, g + 3.6f, g + 3.85f, 0x1D8DCDF);
            uint frame = wall != 0 ? wall : (sky ? 0x1405058u : 0x1506A72u);
            float ins = Math.Min(_w, _d) * (sky ? 0.15f : 0.12f);
            float topBody = sky ? H * 0.82f : H - 1.2f;
            // piani da 3,3 m contati da terra, come le finestre dello shader
            Building(ins, ins + 1.2f, _w - ins, _d - ins, topBody - podH, RoofShape.Flat, frame, 0x1707478, Math.Max(2, (int)Math.Round(topBody / 3.3f)),
                BuildingUse.Greenhouse, false, g + podH, null, WallMaterial.Glass);
            float zTop = g + topBody;
            if (sky)
            {
                // rastremazione e coronamento
                float ins2 = ins + 3.0f;
                Building(ins2, ins2 + 1.2f, _w - ins2, _d - ins2, H * 0.93f - topBody, RoofShape.Flat, frame, 0x1707478, (int)Math.Round(H * 0.93f / 3.3f),
                    BuildingUse.Greenhouse, false, zTop, null, WallMaterial.Glass);
                float zc = g + H * 0.93f;
                foreach (float u in new[] { ins2, _w - ins2 - 0.3f })
                    foreach (float v in new[] { ins2 + 1.2f, _d - ins2 - 0.3f })
                        PaintedBox(u, v, u + 0.3f, v + 0.3f, zc, zc + 3.2f, 0x1C8CCD0);
                PaintedBox(ins2, ins2 + 1.2f, _w - ins2, ins2 + 1.5f, zc + 2.9f, zc + 3.2f, 0x1C8CCD0);
                PaintedBox(ins2, _d - ins2 - 0.3f, _w - ins2, _d - ins2, zc + 2.9f, zc + 3.2f, 0x1C8CCD0);
                Pipe(_w / 2, _d / 2 + 0.6f, 0.25f, zc, g + H + 8f, 0x1B0B4B8, 8, 0.4f);
                Sphere(_w / 2, _d / 2 + 0.6f, g + H + 8.1f, 0.25f, 0x1FF4040, BuildingDecorator.KindLight);
            }
            else
            {
                RoofTanks(ins + 2, ins + 3, _w - ins - 2, _d - ins - 2, zTop);
                // insegna in cima alla torre
                Sign(ins + 2, ins + 1.15f, _w - ins - 2, ins + 1.15f, zTop - 2.4f, zTop - 0.6f, 0x11C2E5A);
            }
            Tree(PlantKind.Broadleaf, 1.2f, 1.0f, 14);
            Tree(PlantKind.Broadleaf, _w - 1.2f, 1.0f, 15);
        }

        // ------------------------------------------------------------------ negozi e servizi commerciali

        private void Shop(CatalogItem item, uint wall, uint roof, float lv)
        {
            switch (item.Param("type", ""))
            {
                case "supermarket": Supermarket(item); return;
                case "mall": Mall(item); return;
                case "cinema": Cinema(item); return;
                case "fuel": FuelStation(item); return;
                case "hotel": Hotel(item, wall); return;
                case "office": Offices(item, wall); return;
                case "glasstower": GlassTower(item, wall, ParseInt(item.Param("floors", "12"), 12), false); return;
            }
            // negozio di città: vetrine al piano terra, appartamenti sopra, marciapiede davanti
            int floors = ParseInt(item.Param("floors", "1"), 1);
            DressKind kind;
            switch (item.Param("dress", "shop"))
            {
                case "food": kind = DressKind.FoodDrink; break;
                case "pharmacy": kind = DressKind.Pharmacy; break;
                case "bank": kind = DressKind.Bank; break;
                case "hotel": kind = DressKind.Hotel; break;
                default: kind = DressKind.Shop; break;
            }
            // il dehors con gli ombrelloni ha bisogno di 2,6 m davanti al locale
            float front = kind == DressKind.FoodDrink ? Math.Min(3.0f, Math.Max(2.6f, _d * 0.34f)) : (kind == DressKind.Hotel ? 2.0f : 1.7f);
            float side = floors >= 3 ? 1.2f : 0.6f;
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            var b = Building(side, front, _w - side, _d - side, item.Height, ParseRoof(item.Param("roof", "hipped")), wall, roof, floors,
                kind == DressKind.Bank ? BuildingUse.Office : BuildingUse.Residential, true);
            Dress(b, kind);
        }

        /// <summary>Uffici: facciata a nastri di finestre, tetto piano con gli impianti, pensilina e insegna.</summary>
        private void Offices(CatalogItem item, uint wall)
        {
            int floors = ParseInt(item.Param("floors", "4"), 4);
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            var b = Building(1.0f, 2.2f, _w - 1.0f, _d - 1.0f, item.Height, RoofShape.Flat, wall != 0 ? wall : 0x1D9D6CFu, 0x1A9A69E, floors, BuildingUse.Office);
            Dress(b, DressKind.Office);
            float g = FloorZ(0);
            Glazing(_w / 2 - 1.6f, 2.2f, _w / 2 + 1.6f, 2.2f, g, g + 2.8f, 1, 0x1303438);
            PaintedBox(_w / 2 - 2.2f, 0.6f, _w / 2 + 2.2f, 2.2f, g + 3.0f, g + 3.25f, 0x1C8CCD0);
            // frangisole verticali sul fronte
            for (float u = 1.6f; u < _w - 1.4f; u += 1.9f)
                PaintedBox(u, 2.0f, u + 0.12f, 2.2f, FloorZ(1), _building.LastEave - 0.3f, 0x1B4B8BC);
        }

        /// <summary>Albergo: balconi con la ringhiera di vetro, pensilina, insegna verticale, bandiere e scritta sul tetto.</summary>
        private void Hotel(CatalogItem item, uint wall)
        {
            int floors = ParseInt(item.Param("floors", "5"), 5);
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            float front = 3.4f, side = 1.2f;
            var b = Building(side, front, _w - side, _d - side, item.Height, RoofShape.Flat, wall != 0 ? wall : 0x1EFE4D2u, 0x1A9A69E, floors, BuildingUse.Hotel);
            Dress(b, DressKind.Hotel);
            for (int f = 1; f < floors; f++)
            {
                float z = FloorZ(f);
                int bays = Math.Max(1, (int)Math.Round((_w - 2 * side) / 3.8f));
                float bw = (_w - 2 * side) / bays;
                for (int k = 0; k < bays; k++)
                {
                    float a = side + k * bw + 0.5f, c = side + (k + 1) * bw - 0.5f;
                    PaintedBox(a, _d - side, c, _d - side + 1.0f, z - 0.15f, z, 0x1E0DCD4);
                    PaintedBox(a, _d - side + 0.9f, c, _d - side + 1.0f, z, z + 1.0f, 0x1A9C4CC);
                }
            }
            // scritta sul tetto
            float zt = _building.LastEave + 0.9f;
            Sign(_w * 0.25f, front + 0.5f, _w * 0.75f, front + 0.5f, zt, zt + 1.3f, 0x11C2E5A);
            Beam(_w * 0.3f, front + 0.7f, zt - 0.9f, _w * 0.3f, front + 0.7f, zt + 1.2f, 0.05f, 0x1555555);
            Beam(_w * 0.7f, front + 0.7f, zt - 0.9f, _w * 0.7f, front + 0.7f, zt + 1.2f, 0.05f, 0x1555555);
            Car(_w * 0.2f, 1.3f, 0, 61);
            Foliage(_w * 0.75f, 0.4f, _w * 0.75f + 1.6f, 0.9f, Ground(_w * 0.8f, 0.6f), Ground(_w * 0.8f, 0.6f) + 0.7f, 0x1476E34, 0.4f);
        }

        /// <summary>
        /// Supermercato: capannone commerciale a tetto piano con la vetrata d'ingresso, pensilina e insegna, parcheggio
        /// davanti con gli stalli, auto e lampioni, ricovero dei carrelli, impianti sul tetto, baia di carico sul lato.
        /// </summary>
        private void Supermarket(CatalogItem item)
        {
            float H = item.Height;
            uint brand = Catalog.ParseColor(item.Param("awning", "#d23c2e"));
            if (brand == 0) brand = 0x1D23C2E;
            float pv = Math.Min(_d * 0.38f, 13f);
            ParkingLot(0, 0, _w, pv, 71, true);
            float u0 = 1, u1 = _w - 1, v0 = pv + 1.2f, v1 = _d - 1;
            Paving(0, pv, _w, v0 + 0.1f, 0x1CFC6B4);
            float g = Block(u0, v0, u1, v1, H, 0x1E8E6E0, 12, 12, 12, 12, 1, 0x1A9A69E);
            float gu0 = _w * 0.3f, gu1 = _w * 0.7f;
            Glazing(gu0, v0, gu1, v0, g, g + H - 2.6f, 1, 0x1303438);
            // fascia colorata del marchio sopra la vetrata e insegna
            Poly(new[] { u0, v0 - 0.04f, g + H - 2.4f, u1, v0 - 0.04f, g + H - 2.4f, u1, v0 - 0.04f, g + H, u0, v0 - 0.04f, g + H }, 0, -1, 0, brand, BuildingDecorator.KindProp, 0);
            Sign(gu0 + 2, v0 - 0.05f, gu1 - 2, v0 - 0.05f, g + H - 2.2f, g + H - 0.3f, Lighten(brand, 0.1f));
            // pensilina d'ingresso su due pilastri
            PaintedBox(gu0, v0 - 3.2f, gu1, v0, g + 3.6f, g + 3.95f, 0x1E6E6E2);
            foreach (float u in new[] { gu0 + 0.4f, gu1 - 0.4f }) Pipe(u, v0 - 2.9f, 0.15f, g, g + 3.6f, 0x1C0C4C8, 8);
            // ricovero dei carrelli
            float cu = _w * 0.12f;
            PaintedBox(cu - 2.2f, pv - 2.4f, cu + 2.2f, pv - 0.4f, Ground(cu, pv) + 2.2f, Ground(cu, pv) + 2.35f, 0x1C0C4C8);
            for (int k = 0; k < 4; k++)
            {
                float x = cu - 1.8f + k * 1.2f;
                PaintedBox(x, pv - 2.2f, x + 0.7f, pv - 0.6f, Ground(x, pv - 1.4f), Ground(x, pv - 1.4f) + 0.95f, 0x1A9B0B6);
            }
            // tetto: macchine del clima e lucernari
            for (int k = 0; k < 4; k++)
            {
                float x = u0 + 3 + k * (u1 - u0 - 6) / 3;
                PaintedBox(x - 1.2f, v1 - 5.5f, x + 1.2f, v1 - 3.5f, g + H, g + H + 1.3f, 0x1D5D8DA);
                PaintedBox(x - 2.5f, v0 + 3, x + 2.5f, v0 + 5, g + H, g + H + 0.4f, 0x19FB6C4);
            }
            // baia di carico sul lato est
            PaintedBox(u1, v1 - 7, u1 + 0.08f, v1 - 3, g, g + 4.2f, 0x18A9096);
            PaintedBox(u1, v1 - 7.2f, u1 + 0.1f, v1 - 2.8f, g + 4.2f, g + 4.4f, 0x1F2B705);
        }

        /// <summary>
        /// Parcheggio asfaltato con gli stalli (strisce dello shader ogni 2,6 m), auto in parte degli stalli, lampioni e
        /// alberi nelle aiuole. Le file di stalli sono profonde 4 m ogni 12 m.
        /// </summary>
        private void ParkingLot(float u0, float v0, float u1, float v1, int salt, bool lamps)
        {
            Ground(u0, v0, u1, v1, 0x16E7176, 0.08f, 9);
            int cols = (int)((u1 - u0) / 2.6f);
            for (float band = 0; band + 5.0f <= v1 - v0 + 0.01f; band += 12f)
            {
                for (int i = 0; i < cols; i++)
                {
                    float r = Frac(salt * 97 + (long)band * 7 + i);
                    if (r > 0.25f + 0.1f * _level) continue;
                    Car(u0 + 2.6f * (i + 0.5f), v0 + band + 3.0f, (float)Math.PI / 2 * (r < 0.15f ? 3 : 1), salt + i * 3 + (long)band);
                }
            }
            if (!lamps) return;
            for (float u = u0 + 6; u < u1 - 3; u += 16)
            {
                float v = Math.Min(v1 - 1.2f, v0 + 7.5f);
                PaintedBox(u - 0.6f, v - 0.6f, u + 0.6f, v + 0.6f, Ground(u, v), Ground(u, v) + 0.18f, 0x1C9C2B5);
                StreetLamp(u, v, Math.Min(5.5f, 8f), false);
            }
        }

        /// <summary>
        /// Centro commerciale: due grandi volumi rivestiti ai lati della galleria vetrata d'ingresso, insegne dei negozi,
        /// lucernari e impianti sul tetto, piazzale con il parcheggio, gli alberi e i lampioni.
        /// </summary>
        private void Mall(CatalogItem item)
        {
            float H = item.Height;
            float pv = _d * 0.36f;
            ParkingLot(0, 0, _w, pv, 81, true);
            float v0 = pv + 2.5f, v1 = _d - 1;
            Paving(0, pv, _w, v0 + 0.1f, 0x1D8D0C0);
            float au0 = _w * 0.42f, au1 = _w * 0.58f;
            float hSide = H * 0.8f;
            float g = Block(1.5f, v0, au0, v1, hSide, 0x1E2DED6, 12, 12, 12, 12, 1, 0x1A9A69E);
            float gR = Block(au1, v0, _w - 1.5f, v1, hSide, 0x1D3CEC6, 12, 12, 12, 12, 1, 0x1A9A69E);
            // galleria vetrata più alta, con il tetto trasparente
            float gA = Block(au0, v0 - 1.5f, au1, v1, H, 0x1303438, 10, 10, 10, 10, 3, 0x19FB6C4, false);
            PaintedBox(au0 - 2, v0 - 6, au1 + 2, v0 - 1.5f, g + 4.6f, g + 5.0f, 0x1E8E8E6);
            foreach (float u in new[] { au0 - 1.6f, au1 + 1.6f }) Pipe(u, v0 - 5.6f, 0.25f, g, g + 4.6f, 0x1C0C4C8, 10);
            // insegne: quella del centro sulla galleria, quelle dei negozi sui volumi laterali
            Sign(au0 + 1, v0 - 1.55f, au1 - 1, v0 - 1.55f, gA + H - 3.2f, gA + H - 0.8f, 0x13C6FD2);
            uint[] brands = { 0x1C8372D, 0x12E7D4F, 0x1E8A317, 0x11F5FA8, 0x17B2D5B, 0x1262626 };
            int k = 0;
            for (float u = 4; u + 8 < au0 - 2; u += 11, k++)
                Sign(u, v0 - 0.02f, u + 8, v0 - 0.02f, g + hSide - 3.2f, g + hSide - 1.4f, brands[k % brands.Length]);
            for (float u = au1 + 3; u + 8 < _w - 3.5f; u += 11, k++)
                Sign(u, v0 - 0.02f, u + 8, v0 - 0.02f, g + hSide - 3.2f, g + hSide - 1.4f, brands[k % brands.Length]);
            // vetrine dei negozi al piano terra lungo il fronte
            Glazing(4, v0, au0 - 2, v0, g, g + 4.2f, 1, 0x1303438);
            Glazing(au1 + 2, v0, _w - 4, v0, gR, gR + 4.2f, 1, 0x1303438);
            // tetto: lucernari e impianti
            for (float u = 6; u < _w - 6; u += 14)
            {
                if (u > au0 - 3 && u < au1 + 3) continue;
                PaintedBox(u - 3, v0 + 6, u + 3, v1 - 6, g + hSide, g + hSide + 0.5f, 0x19FB6C4);
                PaintedBox(u - 1.2f, v1 - 4.5f, u + 1.2f, v1 - 2.5f, g + hSide, g + hSide + 1.6f, 0x1D5D8DA);
            }
            // aiuole con gli alberi lungo il piazzale
            for (float u = 8; u < _w - 6; u += 14)
            {
                Foliage(u - 1.2f, pv + 0.3f, u + 1.2f, pv + 1.6f, Ground(u, pv + 1), Ground(u, pv + 1) + 0.4f, 0x1557A35, 0.3f);
                Tree(PlantKind.Broadleaf, u, pv + 1.0f, (int)u);
            }
        }

        /// <summary>Cinema multisala: grande volume cieco rivestito, foyer vetrato, insegna luminosa, locandine accese, parcheggio.</summary>
        private void Cinema(CatalogItem item)
        {
            float H = item.Height;
            float pv = 9.0f;
            ParkingLot(0, 0, _w, pv - 1.5f, 91, true);
            Paving(0, pv - 1.5f, _w, pv + 2.5f, 0x1D8D0C0);
            float g = Block(1.0f, pv + 2.0f, _w - 1.0f, _d - 1.0f, H, 0x1474B55, 12, 12, 12, 12, 1, 0x1A9A69E);
            // foyer vetrato a due piani
            Block(_w * 0.22f, pv - 1.0f, _w * 0.78f, pv + 2.1f, 6.4f, 0x1303438, 10, 10, 10, 10, 2, 0x1A9A69E, false);
            Sign(_w * 0.26f, pv - 1.05f, _w * 0.74f, pv - 1.05f, g + 6.6f, g + 8.8f, 0x1B3261E);
            Poly(new[] { _w * 0.22f, pv - 1.0f, g + 6.4f, _w * 0.78f, pv - 1.0f, g + 6.4f, _w * 0.78f, pv + 2.0f, g + 6.6f, _w * 0.22f, pv + 2.0f, g + 6.6f },
                0, 0, 1, 0x1D8DCDF, BuildingDecorator.KindProp, 0);
            // fascia luminosa sul bordo del tetto e locandine accese ai lati
            Poly(new[] { 1.0f, pv + 1.96f, g + H - 0.7f, _w - 1.0f, pv + 1.96f, g + H - 0.7f, _w - 1.0f, pv + 1.96f, g + H - 0.4f, 1.0f, pv + 1.96f, g + H - 0.4f },
                0, -1, 0, 0x1E040A0, BuildingDecorator.KindLight, 0.8f);
            for (int k = 0; k < 3; k++)
            {
                float a = 2.0f + k * 2.2f, b = _w - 2.0f - k * 2.2f;
                Poly(new[] { a, pv + 1.96f, g + 2.2f, a + 1.6f, pv + 1.96f, g + 2.2f, a + 1.6f, pv + 1.96f, g + 4.6f, a, pv + 1.96f, g + 4.6f },
                    0, -1, 0, Pick(AwningColors, k), BuildingDecorator.KindLight, 0.6f);
                Poly(new[] { b - 1.6f, pv + 1.96f, g + 2.2f, b, pv + 1.96f, g + 2.2f, b, pv + 1.96f, g + 4.6f, b - 1.6f, pv + 1.96f, g + 4.6f },
                    0, -1, 0, Pick(AwningColors, k + 3), BuildingDecorator.KindLight, 0.6f);
            }
            for (int k = 0; k < 3; k++)
                PaintedBox(4 + k * 10, _d - 6, 6.5f + k * 10, _d - 3.5f, g + H, g + H + 1.4f, 0x1D5D8DA);
        }

        /// <summary>
        /// Distributore di carburante: piazzale d'asfalto, pensilina bianca con la fascia colorata su quattro pilastri,
        /// due isole con le colonnine, negozio con la vetrata, totem dei prezzi, aria e acqua, auto al rifornimento.
        /// </summary>
        private void FuelStation(CatalogItem item)
        {
            uint brand = Catalog.ParseColor(item.Param("awning", "#e0b020"));
            if (brand == 0) brand = 0x1E0B020;
            Asphalt(0, 0, _w, _d);
            float cu0 = 1.5f, cu1 = _w * 0.66f, cv0 = 1.5f, cv1 = _d - 4.0f;
            float g = Base(cu0, cv0, cu1, cv1);
            float zc = g + 5.0f;
            PaintedBox(cu0, cv0, cu1, cv1, zc, zc + 0.35f, 0x1F2F2F0);
            Poly(new[] { cu0, cv0 - 0.02f, zc - 0.25f, cu1, cv0 - 0.02f, zc - 0.25f, cu1, cv0 - 0.02f, zc + 0.75f, cu0, cv0 - 0.02f, zc + 0.75f }, 0, -1, 0, brand, BuildingDecorator.KindLight, 0.4f);
            Poly(new[] { cu1 + 0.02f, cv0, zc - 0.25f, cu1 + 0.02f, cv1, zc - 0.25f, cu1 + 0.02f, cv1, zc + 0.75f, cu1 + 0.02f, cv0, zc + 0.75f }, 1, 0, 0, brand, BuildingDecorator.KindLight, 0.4f);
            Poly(new[] { cu0 - 0.02f, cv0, zc - 0.25f, cu0 - 0.02f, cv1, zc - 0.25f, cu0 - 0.02f, cv1, zc + 0.75f, cu0 - 0.02f, cv0, zc + 0.75f }, -1, 0, 0, brand, BuildingDecorator.KindLight, 0.4f);
            PaintedBox(cu0, cv0, cu1, cv1, zc + 0.35f, zc + 0.75f, 0x1F2F2F0);
            // luci sotto la pensilina
            Poly(new[] { cu0 + 1, cv0 + 1, zc - 0.01f, cu1 - 1, cv0 + 1, zc - 0.01f, cu1 - 1, cv1 - 1, zc - 0.01f, cu0 + 1, cv1 - 1, zc - 0.01f }, 0, 0, -1, 0x1FFF2D8, BuildingDecorator.KindLight, 0.5f);
            float mu = (cu0 + cu1) / 2;
            foreach (float u in new[] { cu0 + 2.0f, cu1 - 2.0f })
                foreach (float v in new[] { cv0 + 1.5f, cv1 - 1.5f })
                    PaintedBox(u - 0.25f, v - 0.25f, u + 0.25f, v + 0.25f, g, zc, 0x1E8E8E8);
            // isole delle pompe
            foreach (float v in new[] { cv0 + (cv1 - cv0) * 0.3f, cv0 + (cv1 - cv0) * 0.72f })
            {
                PaintedBox(mu - 3.0f, v - 0.5f, mu + 3.0f, v + 0.5f, g, g + 0.18f, 0x1CFCFCF);
                foreach (float u in new[] { mu - 1.6f, mu + 1.6f })
                {
                    PaintedBox(u - 0.35f, v - 0.25f, u + 0.35f, v + 0.25f, g + 0.18f, g + 1.8f, 0x1F2F2F2);
                    PaintedBox(u - 0.36f, v - 0.26f, u + 0.36f, v + 0.26f, g + 1.5f, g + 1.8f, brand, BuildingDecorator.KindLight, 0.6f);
                }
            }
            Car(mu, cv0 + (cv1 - cv0) * 0.3f - 1.9f, 0, 101);
            if (_level >= 2) Car(mu + 0.6f, cv0 + (cv1 - cv0) * 0.72f + 1.9f, (float)Math.PI, 102);
            // negozio
            float su0 = cu1 + 0.8f, su1 = _w - 0.6f, sv0 = _d * 0.45f, sv1 = _d - 0.6f;
            float sg = Block(su0, sv0, su1, sv1, 3.8f, 0x1F0EEE8, 10, 12, 12, 10, 1, 0x1A9A69E);
            Sign(su0 + 0.3f, sv0 - 0.02f, su1 - 0.3f, sv0 - 0.02f, sg + 3.85f, sg + 4.6f, brand);
            // totem dei prezzi
            float tu = _w - 1.0f, tv = 1.2f, tg = Ground(tu, tv);
            OBox(tu, tv, 0, 0.6f, 0.18f, tg, tg + 5.6f, brand, BuildingDecorator.KindSign, 1);
            OBox(tu, tv, 0, 0.62f, 0.2f, tg + 5.6f, tg + 6.0f, 0x1F2F2F2);
            // colonnina aria e acqua
            PaintedBox(su0 - 0.6f, 1.0f, su0 - 0.2f, 1.4f, Ground(su0, 1), Ground(su0, 1) + 1.3f, 0x11F5FA8);
        }
    }
}
