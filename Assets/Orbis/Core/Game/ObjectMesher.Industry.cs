using System;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;

namespace Orbis.Core.Game
{
    /// <summary>Industria, campagna ed energia.</summary>
    public sealed partial class ObjectMesher
    {
        private static readonly uint[] CompanyColors = { 0x11F5FA8, 0x1C8372D, 0x12E7D4F, 0x1E8A317, 0x1262626, 0x12A6F97 };

        // ------------------------------------------------------------------ industria

        private void Industrial(CatalogItem item, uint wall, uint roof)
        {
            string kind = item.Param("kind", "shed");
            switch (kind)
            {
                case "plant": CementWorks(item); return;
                case "winery": Winery(item, wall); return;
                case "oilmill": OilMill(item, wall, roof); return;
                case "workshop": Workshop(item, wall); return;
                case "factory": Factory(item, wall); return;
                default: Warehouse(item, wall); return;
            }
        }

        /// <summary>
        /// Capannone prefabbricato: pannelli in cemento con le finestre a nastro, portoni sezionali con le pensiline di
        /// carico, camion alle baie, lucernari sul tetto, palazzina uffici, piazzale recintato, insegna.
        /// </summary>
        private void Warehouse(CatalogItem item, uint wall)
        {
            float H = item.Height;
            float yard = Math.Min(_d * 0.34f, 18f);
            Asphalt(0, 0, _w, _d);
            float u0 = 1.0f, u1 = _w - 1.0f, v0 = yard, v1 = _d - 1.0f;
            uint col = wall != 0 ? wall : 0x1D3D2CCu;
            float g = Block(u0, v0, u1, v1, H, col, 4, 4, 4, 4, 1, 0x1A3A5A6);
            // fascia colorata in alto e insegna
            uint brand = Pick(CompanyColors, 3);
            Poly(new[] { u0, v0 - 0.03f, g + H - 1.2f, u1, v0 - 0.03f, g + H - 1.2f, u1, v0 - 0.03f, g + H - 0.6f, u0, v0 - 0.03f, g + H - 0.6f }, 0, -1, 0, brand, BuildingDecorator.KindProp, 0);
            Sign(u0 + 2, v0 - 0.04f, u0 + Math.Min(14, (u1 - u0) * 0.4f), v0 - 0.04f, g + H - 3.2f, g + H - 1.5f, brand);
            // portoni con le pensiline e i camion
            bool docks = _w >= 40;
            int doors = Math.Max(2, (int)((u1 - u0 - 10) / (docks ? 7.5f : 9f)));
            float start = u0 + 10;
            float dw = (u1 - start) / doors;
            for (int k = 0; k < doors; k++)
            {
                float a = start + k * dw + dw * 0.18f, b = start + (k + 1) * dw - dw * 0.18f;
                float top = docks ? g + 4.2f : g + 5.0f;
                Poly(new[] { a, v0 - 0.04f, docks ? g + 1.2f : g, b, v0 - 0.04f, docks ? g + 1.2f : g, b, v0 - 0.04f, top, a, v0 - 0.04f, top }, 0, -1, 0, 0x18A9096, BuildingDecorator.KindProp, 0);
                PaintedBox(a - 0.2f, v0 - 1.6f, b + 0.2f, v0, top + 0.3f, top + 0.5f, 0x1C0C4C8);
                if (docks)
                {
                    PaintedBox(a - 0.3f, v0 - 0.5f, b + 0.3f, v0, g + 1.0f, g + 1.2f, 0x1222222);
                    PaintedBox(a - 0.3f, v0 - 0.5f, a, v0, g + 1.2f, top, 0x1222222);
                    PaintedBox(b, v0 - 0.5f, b + 0.3f, v0, g + 1.2f, top, 0x1222222);
                    if (Frac(k * 5 + 1) < 0.35f + 0.1f * _level)
                    {
                        if (yard >= 16.5f) Truck((a + b) / 2, v0 - 8.4f, -(float)Math.PI / 2, Pick(CompanyColors, k), 0x1F2F2F0);
                        else Van((a + b) / 2, v0 - 3.3f, -(float)Math.PI / 2, 0x1F2F2F0, Pick(CompanyColors, k));
                    }
                }
                else if (k == 0) Van((a + b) / 2, v0 - 3.5f, -(float)Math.PI / 2, 0x1F2F2F0, 0);
            }
            // palazzina uffici sull'angolo
            var off = Building(u0, v0 - Math.Min(4f, yard - 2f), u0 + 9.5f, v0 + 6, Math.Min(H, 7.4f), RoofShape.Flat, 0x1E9E8E4, 0x1A9A69E, 2, BuildingUse.Office);
            Dress(off, DressKind.Modern, u0 + 4);
            // lucernari a strisce sul tetto
            for (float v = v0 + 4; v < v1 - 3; v += 7)
                PaintedBox(u0 + 3, v, u1 - 3, v + 1.4f, g + H, g + H + 0.45f, 0x1A9BCC6);
            for (int k = 0; k < 1 + _level / 2; k++) Car(u0 + 2.8f + k * 4.8f, Math.Max(1.3f, yard * 0.2f), 0, 140 + k);
            LotFence(2.0f, Math.Min(10, _w * 0.3f), 0x1556070, false);
        }

        /// <summary>Officina, falegnameria o tipografia: capannone col tetto a capanna, grande portone, insegna e quel che c'è nel cortile.</summary>
        private void Workshop(CatalogItem item, uint wall)
        {
            float H = item.Height;
            Asphalt(0, 0, _w, _d);
            float front = Math.Min(4.5f, _d * 0.3f);
            uint col = wall != 0 ? wall : 0x1E2DFD6u;
            var b = Building(1.0f, front, _w - 1.0f, _d - 0.8f, H, RoofShape.Gabled, col, 0x19A9C9C, 1, BuildingUse.Industrial);
            Dress(b, DressKind.Industrial);
            string key = item.Key ?? "";
            float g = FloorZ(0);
            if (key.Contains("falegn"))
            {
                // cataste di tavole e il silo della segatura
                for (int k = 0; k < 3; k++)
                {
                    float u = 1.5f + k * 2.4f;
                    for (int l = 0; l < 4; l++)
                        PaintedBox(u, 0.6f, u + 2.0f, 2.8f, g + l * 0.28f, g + l * 0.28f + 0.22f, l % 2 == 0 ? 0x1B8925Eu : 0x1A88050u);
                }
                Tank(_w - 2.2f, 1.8f, 1.1f, g, 4.2f, 0x1C9CCCF);
            }
            else if (key.Contains("tipogr"))
            {
                Van(_w * 0.65f, front * 0.5f, 0, 0x1F2F2F0, 0x11F5FA8);
                for (int k = 0; k < 3; k++) Pipe(2.0f + k * 1.4f, 1.4f, 0.55f, g, g + 1.3f, 0x1F0EDE6, 12);
            }
            else
            {
                // auto in attesa e pile di pneumatici
                Car(_w * 0.3f, front * 0.45f, 0, 151);
                if (_level >= 2) Car(_w * 0.72f, front * 0.45f, 0, 152);
                for (int k = 0; k < 4; k++) Pipe(_w - 1.0f, 1.0f, 0.35f, g + k * 0.22f, g + k * 0.22f + 0.2f, 0x1202020, 10);
            }
        }

        /// <summary>
        /// Stabilimento: capannoni con il tetto a shed (falde vetrate verso nord), palazzina uffici, ciminiera, silos per
        /// la farina al pastificio, piazzale con le auto nuove allo stabilimento automobilistico.
        /// </summary>
        private void Factory(CatalogItem item, uint wall)
        {
            float H = item.Height;
            string key = item.Key ?? "";
            bool pasta = key.Contains("pasta");
            bool cars = key.Contains("auto");
            Asphalt(0, 0, _w, _d);
            float front = Math.Min(_d * 0.22f, cars ? 22f : 9f);
            uint col = wall != 0 ? wall : 0x1D6D2C8u;
            float hallH = Math.Min(H * 0.55f, 9f);
            float hu1 = _w - (pasta ? 9f : 5.0f);
            Sawtooth(1.0f, front, hu1, _d - 1.0f, hallH, 2.6f, col);
            // palazzina uffici sul fronte
            var off = Building(1.0f, front - 0.01f - Math.Min(front - 1.5f, 6f), Math.Min(18f, _w * 0.35f), front + 0.01f, Math.Min(H * 0.75f, 11f), RoofShape.Flat,
                0x1E9E6DE, 0x1A9A69E, 3, BuildingUse.Office);
            Dress(off, DressKind.Office, 6);
            float g = FloorZ(0);
            uint brand = Pick(CompanyColors, 9);
            Sign(Math.Min(18f, _w * 0.35f) + 1, front - 0.04f, Math.Min(18f, _w * 0.35f) + 12, front - 0.04f, g + hallH - 2.2f, g + hallH - 0.6f, brand);
            if (pasta)
            {
                // silos della semola con la passerella
                for (int k = 0; k < 3; k++) Tank(_w - 4.5f, front + 3 + k * 5.2f, 2.2f, g, H - 2.0f, 0x1EDEDEA, 1.0f);
                Beam(_w - 4.5f, front + 3, g + H - 1.6f, _w - 4.5f, front + 13.4f, g + H - 1.6f, 0.3f, 0x1A0A4A8);
                Beam(_w - 4.5f, front + 8, g + H - 1.6f, hu1 - 0.5f, front + 8, g + hallH + 2.0f, 0.35f, 0x1C0C4C8);
            }
            else
            {
                // ciminiera in mattoni
                float cu = _w - 2.6f, cv = _d - 3.0f;
                Cylinder(cu, cv, 1.4f, Ground(cu, cv) - 0.3f, _z0 + H, 12, 0x1A35C42, 1f, 0.62f, 0, 12);
            }
            if (cars)
            {
                // auto nuove in file ordinate sul piazzale e torre dell'acqua
                for (float v = 2.5f; v + 2 < front - 1; v += 5.5f)
                    for (float u = Math.Min(20f, _w * 0.36f) + 2; u + 1.2f < _w - 12; u += 2.6f)
                        Car(u, v, (float)Math.PI / 2, (long)(u * 3 + v));
                float tu = _w - 6.0f, tv = front * 0.5f, tg = Ground(tu, tv);
                foreach (float du in new[] { -1.8f, 1.8f })
                    foreach (float dv in new[] { -1.8f, 1.8f })
                        Beam(tu + du, tv + dv, tg, tu + du * 0.6f, tv + dv * 0.6f, tg + H - 4.5f, 0.12f, 0x1A0A4A8);
                Tank(tu, tv, 2.6f, tg + H - 4.5f, 3.2f, 0x1E8E8E8, 1.2f);
            }
            else
                for (int k = 0; k < 2 + _level; k++)
                {
                    float u = Math.Min(20f, _w * 0.36f) + 3 + k * 2.6f;
                    if (u + 1.2f > _w - 1.0f) break;
                    Car(u, Math.Max(2.4f, front * 0.4f), (float)Math.PI / 2, 160 + k);
                }
            LotFence(2.0f, 8f, 0x1556070, false);
        }

        /// <summary>
        /// Capannone a shed: muri con le finestre a nastro fino a <paramref name="wallH"/>, poi una fila di denti con la
        /// falda vetrata verticale rivolta a nord (+V) e la falda inclinata in lamiera verso sud.
        /// </summary>
        private void Sawtooth(float u0, float v0, float u1, float v1, float wallH, float toothH, uint col)
        {
            float g = Block(u0, v0, u1, v1, wallH, col, 4, 4, 4, 4, 1, 0x19EA0A2, false);
            float span = 7.5f;
            int teeth = Math.Max(1, (int)Math.Round((v1 - v0) / span));
            span = (v1 - v0) / teeth;
            float z0 = g + wallH, z1 = z0 + toothH;
            for (int t = 0; t < teeth; t++)
            {
                float a = v0 + t * span, b = a + span;
                // falda inclinata (sale verso nord)
                Poly(new[] { u0, a, z0, u1, a, z0, u1, b, z1, u0, b, z1 }, 0, -0.4f, 1, 0x1A3A5A6, BuildingDecorator.KindProp, 0);
                // vetrata verticale verso nord
                Wall(u1, b, u0, b, z0, z1, z0, 0x1404A52, 10, 1);
                // testate triangolari
                Poly(new[] { u0, a, z0, u0, b, z0, u0, b, z1 }, -1, 0, 0, col, 0, 1, 12);
                Poly(new[] { u1, a, z0, u1, b, z1, u1, b, z0 }, 1, 0, 0, col, 0, 1, 12);
            }
        }

        /// <summary>Cantina sociale: edificio di conferimento, vasche d'acciaio in fila con la passerella, tramoggia per l'uva e il trattore col rimorchio.</summary>
        private void Winery(CatalogItem item, uint wall)
        {
            float H = item.Height;
            Asphalt(0, 0, _w, _d);
            float bu1 = _w * 0.6f;
            var b = Building(1.0f, 3.0f, bu1, _d - 1.0f, H * 0.85f, RoofShape.Gabled, wall != 0 ? wall : 0x1E0CDA8u, 0x1B25A3C, 2, BuildingUse.Industrial);
            Dress(b, DressKind.Industrial);
            float g = FloorZ(0);
            Sign(2.0f, 2.96f, bu1 - 2.0f, 2.96f, g + 4.6f, g + 5.6f, 0x16E1F2F);
            // vasche in acciaio inox
            int cols = Math.Max(2, (int)((_w - bu1 - 1.5f) / 3.6f));
            float tr = Math.Min(1.6f, (_w - bu1 - 1.5f) / cols / 2.3f);
            for (int r = 0; r < 2; r++)
                for (int k = 0; k < cols; k++)
                {
                    float cu = bu1 + 1.5f + tr + k * (2 * tr + 0.7f), cv = _d * (0.35f + 0.35f * r);
                    Tank(cu, cv, tr, g, H - 1.2f, 0x1D5D8DA, tr * 0.5f);
                }
            float pz = g + H - 1.0f;
            Beam(bu1 + 1.0f, _d * 0.525f, pz, _w - 0.8f, _d * 0.525f, pz, 0.4f, 0x1A0A4A8);
            for (float u = bu1 + 1.2f; u < _w - 0.8f; u += 3) Beam(u, _d * 0.525f, g, u, _d * 0.525f, pz, 0.08f, 0x1A0A4A8);
            // tramoggia e trattore col rimorchio dell'uva
            PaintedBox(bu1 + 1.0f, 0.6f, bu1 + 4.0f, 2.6f, g, g + 1.0f, 0x1A0A4A8);
            float tu = bu1 + 8.0f;
            if (tu + 4 < _w)
            {
                OBox(tu, 1.6f, 0, 1.3f, 0.8f, g, g + 2.0f, 0x13E8E4A);
                OBox(tu - 3.4f, 1.6f, 0, 1.6f, 1.0f, g + 0.5f, g + 1.6f, 0x1C0623F);
            }
        }

        /// <summary>Frantoio: edificio in pietra col tetto a capanna, cisterne dell'olio, cassette di olive impilate.</summary>
        private void OilMill(CatalogItem item, uint wall, uint roof)
        {
            Ground(0, 0, _w, _d, 0x1BFAE8C, 0.05f, 10);
            var b = Building(1.0f, 3.0f, _w * 0.72f, _d - 1.0f, item.Height, RoofShape.Gabled, wall != 0 ? wall : 0x1CDBB9Au, roof != 0 ? roof : 0x1B25A3Cu, 1,
                BuildingUse.Industrial, false, float.NaN, null, WallMaterial.Stone);
            Dress(b, DressKind.Industrial);
            float g = FloorZ(0);
            Sign(2.0f, 2.96f, _w * 0.5f, 2.96f, g + 3.2f, g + 4.0f, 0x12E5D3A);
            for (int k = 0; k < 2; k++) Tank(_w * 0.86f, _d * (0.35f + 0.3f * k), 1.5f, g, item.Height * 0.7f, 0x1D5D8DA, 0.6f);
            for (int k = 0; k < 6; k++)
                PaintedBox(1.5f + (k % 3) * 0.7f, 0.8f + (k / 3) * 0.6f, 2.1f + (k % 3) * 0.7f, 1.3f + (k / 3) * 0.6f, g, g + 0.3f + (k % 2) * 0.3f, 0x1B5462F);
        }

        /// <summary>
        /// Cementificio: torre dei preriscaldatori (struttura d'acciaio con i cicloni), forno rotante su piloni, silos del
        /// cemento, nastri trasportatori, ciminiera a bande, piazzale polveroso con le betoniere.
        /// </summary>
        private void CementWorks(CatalogItem item)
        {
            float H = item.Height;
            Ground(0, 0, _w, _d, 0x1BBB7AE, 0.06f, BuildingDecorator.KindProp);
            float g = Base(0, 0, _w, _d);
            // torre dei preriscaldatori
            float tu0 = _w * 0.06f, tu1 = tu0 + 9, tv0 = _d * 0.45f, tv1 = tv0 + 9;
            float th = H * 0.92f;
            for (int k = 0; k < 4; k++)
            {
                float u = (k & 1) == 0 ? tu0 : tu1, v = (k & 2) == 0 ? tv0 : tv1;
                Beam(u, v, g - 0.2f, u, v, g + th, 0.25f, 0x18A9096);
            }
            for (int f = 1; f <= 5; f++)
            {
                float z = g + th * f / 5;
                PaintedBox(tu0, tv0, tu1, tv1, z - 0.3f, z, 0x19AA0A6);
                Beam(tu0, tv0, z - th / 5, tu1, tv0, z, 0.1f, 0x18A9096);
                Beam(tu0, tv1, z - th / 5, tu1, tv1, z, 0.1f, 0x18A9096);
            }
            for (int k = 0; k < 4; k++)
            {
                float z = g + 3 + k * (th - 6) / 4;
                float cu = tu0 + 2.6f + (k % 2) * 3.8f, cv = (tv0 + tv1) / 2;
                Pipe(cu, cv, 1.6f, z + 1.8f, z + 4.4f, 0x1C9C4BA, 14);
                Cylinder(cu, cv, 1.6f, z, z + 1.8f, 14, 0x1C9C4BA, 0.25f, 1f, BuildingDecorator.KindProp, 0);
            }
            // forno rotante orizzontale su tre piloni
            float ku0 = tu1 + 1, ku1 = _w * 0.78f, kv = (tv0 + tv1) / 2, kz = g + 5.0f;
            for (int k = 0; k < 3; k++)
            {
                float u = ku0 + 3 + k * (ku1 - ku0 - 6) / 2;
                PaintedBox(u - 1.2f, kv - 1.6f, u + 1.2f, kv + 1.6f, g - 0.2f, kz - 1.8f, 0x1C6C2BA);
            }
            HorizontalDrum(ku0, ku1, kv, kz, 1.9f, kz - 1.2f, 0x18E8478);
            // silos del cemento
            for (int k = 0; k < 4; k++)
                Tank(_w * 0.62f + (k % 2) * 7.2f, _d * 0.12f + 3.4f + (k / 2) * 7.2f, 3.2f, g, H * 0.75f, 0x1D3CFC8, 1.2f);
            // nastri trasportatori coperti
            Beam(_w * 0.95f, _d * 0.9f, g + 1, tu1 - 1, tv1 - 1, g + th * 0.85f, 0.6f, 0x1A9A69E);
            Beam(ku1, kv, kz, _w * 0.62f, _d * 0.12f + 3.4f, g + H * 0.72f, 0.5f, 0x1A9A69E);
            // cumuli di materiale e ciminiera
            Cone(_w * 0.86f, _d * 0.84f, Math.Min(4.5f, Math.Min(_w, _d) * 0.1f), g, 4.0f, 0x1A89F8E, BuildingDecorator.KindProp, 14);
            float cu2 = _w * 0.86f, cv2 = _d * 0.5f;
            Cylinder(cu2, cv2, 1.6f, g - 0.3f, g + H - 3, 12, 0x1D6D3CC, 1f, 0.7f, 0, 12);
            Pipe(cu2, cv2, 1.12f, g + H - 3, g + H - 1.5f, 0x1C8382E, 12);
            Pipe(cu2, cv2, 1.12f, g + H - 1.5f, g + H, 0x1F2F0EB, 12);
            MixerTruck(_w * 0.4f, _d * 0.14f, 0);
            if (_level >= 2) MixerTruck(_w * 0.4f, _d * 0.24f, 0);
            LotFence(2.0f, 8f, 0x1556070, false);
        }

        /// <summary>Cilindro orizzontale lungo U (forni rotanti, condotte): da u0 a u1 alla quota dell'asse cz.</summary>
        private void HorizontalDrum(float u0, float u1, float cv, float cz, float r, float zMin, uint color)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            const int seg = 14;
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                float v0 = cv + (float)Math.Cos(a0) * r, z0 = cz + (float)Math.Sin(a0) * r;
                float v1 = cv + (float)Math.Cos(a1) * r, z1 = cz + (float)Math.Sin(a1) * r;
                z0 = Math.Max(z0, zMin);
                z1 = Math.Max(z1, zMin);
                var p0 = World(u0, v0);
                var p1 = World(u1, v0);
                var p2 = World(u1, v1);
                var p3 = World(u0, v1);
                double am = (a0 + a1) / 2;
                var n2 = _v * (float)Math.Cos(am);
                QuadW(new Vec3(p0.X, p0.Y, z0), new Vec3(p1.X, p1.Y, z0), new Vec3(p2.X, p2.Y, z1), new Vec3(p3.X, p3.Y, z1),
                    new Vec3(n2.X, n2.Y, (float)Math.Sin(am)), c, BuildingDecorator.KindProp, 0);
            }
        }

        private void MixerTruck(float cu, float cv, float angle)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 3.6f, 1.15f, g, g + 1.0f, 0x1303030);
            OBox(cu + ca * 2.9f, cv + sa * 2.9f, angle, 0.9f, 1.2f, g + 0.6f, g + 3.0f, 0x1F2F2F0);
            OBox(cu - ca * 0.8f, cv - sa * 0.8f, angle, 2.4f, 1.1f, g + 1.0f, g + 3.2f, 0x1E8A317);
            OBox(cu - ca * 0.8f, cv - sa * 0.8f, angle, 1.6f, 1.15f, g + 1.6f, g + 2.9f, 0x1D08A12);
        }

        // ------------------------------------------------------------------ campagna

        /// <summary>Casale o agriturismo in pietra (scala esterna, loggia, pergolato; all'agriturismo anche i tavoli all'aperto).</summary>
        private void Farmhouse(CatalogItem item, uint wall, uint roof) => Casale(item, wall, roof, item.Param("kind", "") == "agriturismo");

        /// <summary>Fienile aperto su pilastri in mattoni con le balle impilate, o stalla lunga con le finestrelle e il paddock.</summary>
        private void Barn(CatalogItem item, uint wall)
        {
            float H = item.Height;
            Ground(0, 0, _w, _d, 0x1B5A57E, 0.05f, 10);
            if (item.Param("kind", "") == "stable")
            {
                float pv = Math.Min(4.5f, _d * 0.35f);
                var b = Building(0.8f, pv, _w - 0.8f, _d - 0.8f, H, RoofShape.Gabled, wall != 0 ? wall : 0x1D8C7A8u, 0x1B25A3C, 1, BuildingUse.Stable);
                Dress(b, DressKind.Farm);
                // paddock recintato con le vacche
                float g = FloorZ(0);
                Lawn(0.3f, 0.3f, _w - 0.3f, pv - 0.3f, 0.06f);
                WoodFence(0.3f, 0.3f, _w - 0.3f, 0.3f);
                WoodFence(0.3f, 0.3f, 0.3f, pv - 0.2f);
                WoodFence(_w - 0.3f, 0.3f, _w - 0.3f, pv - 0.2f);
                for (int k = 0; k < Math.Min(5, 2 + _level); k++)
                    Cow(1.8f + k * (_w - 3.6f) / 4, pv * (0.35f + 0.3f * (k % 2)), k * 0.7f, k);
                Beam(_w * 0.5f - 3, pv - 0.6f, g + 0.5f, _w * 0.5f + 3, pv - 0.6f, g + 0.5f, 0.3f, 0x1707A80);
                return;
            }
            // fienile aperto: pilastri, tetto a capanna sospeso, balle dentro
            uint brick = 0x1B4704F;
            float g2 = Base(0, 0, _w, _d);
            int n = Math.Max(2, (int)(_w / 4.5f));
            for (int k = 0; k <= n; k++)
            {
                float u = 0.8f + (_w - 1.6f) * k / n;
                PaintedBox(u - 0.35f, 0.8f, u + 0.35f, 1.5f, g2 - 0.2f, g2 + H - 2.4f, brick);
                PaintedBox(u - 0.35f, _d - 1.5f, u + 0.35f, _d - 0.8f, g2 - 0.2f, g2 + H - 2.4f, brick);
            }
            Building(0.6f, 0.6f, _w - 0.6f, _d - 0.6f, 2.4f, RoofShape.Gabled, brick, 0x1B25A3C, 1, BuildingUse.Roof, false, g2 + H - 2.6f);
            Flat(0.8f, 0.8f, _w - 0.8f, _d - 0.8f, g2 + 0.05f, 0x1A8946A, BuildingDecorator.KindProp, 0);
            int layers = Math.Min(4, 1 + _level);
            for (int l = 0; l < layers; l++)
                for (float u = 1.6f; u + 2.4f < _w - 1.4f; u += 2.5f)
                    for (float v = 2.0f; v + 1.2f < _d - 1.8f; v += 1.25f)
                        PaintedBox(u, v, u + 2.4f, v + 1.15f, g2 + l * 0.9f, g2 + l * 0.9f + 0.85f, l % 2 == 0 ? 0x1D9B44Au : 0x1CFA840u);
        }

        private void WoodFence(float ua, float va, float ub, float vb)
        {
            float len = (float)Math.Sqrt((ub - ua) * (ub - ua) + (vb - va) * (vb - va));
            int n = Math.Max(1, (int)Math.Ceiling(len / 2.5f));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = ua + (ub - ua) * t, v = va + (vb - va) * t, g = Ground(u, v);
                Beam(u, v, g - 0.2f, u, v, g + 1.15f, 0.06f, 0x17A5A3C);
            }
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                float u0 = ua + (ub - ua) * t0, v0 = va + (vb - va) * t0, u1 = ua + (ub - ua) * t1, v1 = va + (vb - va) * t1;
                float g0 = Ground(u0, v0), g1 = Ground(u1, v1);
                Beam(u0, v0, g0 + 1.0f, u1, v1, g1 + 1.0f, 0.04f, 0x18A6A4A);
                Beam(u0, v0, g0 + 0.55f, u1, v1, g1 + 0.55f, 0.04f, 0x18A6A4A);
            }
        }

        private void Cow(float cu, float cv, float angle, int salt)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            uint coat = salt % 3 == 0 ? 0x16B4A30u : 0x1F2EEE6u;
            OBox(cu, cv, angle, 1.0f, 0.38f, g + 0.75f, g + 1.4f, coat);
            OBox(cu + ca * 1.15f, cv + sa * 1.15f, angle, 0.25f, 0.2f, g + 1.0f, g + 1.45f, 0x1302826);
            foreach (float a in new[] { -0.75f, 0.75f })
                foreach (float s in new[] { -0.25f, 0.25f })
                    Beam(cu + ca * a - sa * s, cv + sa * a + ca * s, g, cu + ca * a - sa * s, cv + sa * a + ca * s, g + 0.78f, 0.07f, 0x1302826);
        }

        private void Sheep(float cu, float cv, float angle)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 0.55f, 0.3f, g + 0.4f, g + 0.85f, 0x1EEE9DC);
            OBox(cu + ca * 0.62f, cv + sa * 0.62f, angle, 0.14f, 0.12f, g + 0.6f, g + 0.85f, 0x1302826);
            foreach (float a in new[] { -0.4f, 0.4f })
                Beam(cu + ca * a, cv + sa * a, g, cu + ca * a, cv + sa * a, g + 0.42f, 0.05f, 0x1302826);
        }

        /// <summary>Pascolo: prato, staccionata in legno, abbeveratoio e il gregge di pecore.</summary>
        private void Pasture()
        {
            Ground(0, 0, _w, _d, 0x18FB466, 0.05f, BuildingDecorator.KindPlants, 0.05f);
            WoodFence(0.2f, 0.2f, _w - 0.2f, 0.2f);
            WoodFence(_w - 0.2f, 0.2f, _w - 0.2f, _d - 0.2f);
            WoodFence(_w - 0.2f, _d - 0.2f, 0.2f, _d - 0.2f);
            WoodFence(0.2f, _d - 0.2f, 0.2f, 0.2f);
            int n = 8 + _level * 3;
            for (int k = 0; k < n; k++)
            {
                float u = 2 + (_w - 4) * Frac(k * 7 + 1), v = 2 + (_d - 4) * Frac(k * 13 + 2);
                Sheep(u, v, Frac(k * 3) * 6.28f);
            }
            float tu = _w * 0.15f, tv = _d * 0.5f, tg = Ground(tu, tv);
            PaintedBox(tu - 1.5f, tv - 0.4f, tu + 1.5f, tv + 0.4f, tg - 0.1f, tg + 0.55f, 0x1C9C2B5);
            Flat(tu - 1.35f, tv - 0.28f, tu + 1.35f, tv + 0.28f, tg + 0.48f, 0x14F8FC0, 11);
            Tree(PlantKind.Broadleaf, _w * 0.8f, _d * 0.25f, 61);
        }

        /// <summary>Vigneto: terra lavorata, filari di viti con i pali di testata.</summary>
        private void Vineyard()
        {
            Ground(0, 0, _w, _d, 0x1A29660, 0.06f, BuildingDecorator.KindProp);
            const float gap = 2.6f;
            Rows(PlantKind.VineRow, gap);
            for (float v = gap / 2; v <= _d - gap / 2 + 0.01f; v += gap)
                foreach (float u in new[] { 0.4f, _w - 0.4f })
                {
                    float g = Ground(u, v);
                    Beam(u, v, g - 0.2f, u, v, g + 1.9f, 0.05f, 0x18A7A6A);
                }
        }

        /// <summary>Campi coltivati: grano a distesa con le carreggiate, mais e girasoli a file alte, lavanda a cuscini, orto a prose.</summary>
        private void Field(string crop)
        {
            Ground(0, 0, _w, _d, 0x19C8058, 0.05f, BuildingDecorator.KindProp);
            switch (crop)
            {
                case "wheat":
                    {
                        // distesa di spighe con le carreggiate del trattore ogni 12 m
                        for (float u = 0.4f; u < _w - 0.4f; u += 12f)
                        {
                            float u1 = Math.Min(_w - 0.4f, u + 11.4f);
                            Range(u, 0.4f, u1, _d - 0.4f, out var lo, out var hi);
                            Foliage(u, 0.4f, u1, _d - 0.4f, lo - 0.1f, (lo + hi) / 2 + 0.8f, 0x1D8B85C, 0.0f);
                        }
                        break;
                    }
                case "corn":
                case "sunflower":
                    {
                        bool sun = crop == "sunflower";
                        float h = sun ? 1.6f : 2.0f;
                        for (float v = 0.6f; v + 0.4f < _d - 0.4f; v += 0.85f)
                        {
                            float g = Mid(0.4f, v, _w - 0.4f, v + 0.4f);
                            Foliage(0.4f, v, _w - 0.4f, v + 0.45f, g - 0.1f, g + h, sun ? 0x1557A2Eu : 0x17E9A40u, 0.0f);
                            if (sun)
                                Poly(new[] { 0.4f, v - 0.08f, g + h + 0.05f, _w - 0.4f, v - 0.08f, g + h + 0.05f, _w - 0.4f, v + 0.53f, g + h + 0.05f, 0.4f, v + 0.53f, g + h + 0.05f },
                                    0, -0.3f, 1, 0x1F0C020, BuildingDecorator.KindPlants, 0.0f);
                        }
                        break;
                    }
                case "lavender":
                case "saffron":
                    {
                        bool laven = crop == "lavender";
                        for (float v = 0.6f; v + 0.6f < _d - 0.4f; v += laven ? 1.6f : 0.9f)
                        {
                            float g = Mid(0.4f, v, _w - 0.4f, v + 0.8f);
                            Foliage(0.4f, v, _w - 0.4f, v + (laven ? 0.9f : 0.45f), g - 0.1f, g + (laven ? 0.6f : 0.18f), laven ? 0x18A6EB8u : 0x19C82C8u, 0.0f);
                        }
                        break;
                    }
                default:
                    {
                        // orto: prose di verdure diverse, pomodori sui tutori, capanno degli attrezzi e cisterna
                        uint[] greens = { 0x15F9C3E, 0x14E8A3A, 0x17FAE4A, 0x1567F30, 0x1689A3C };
                        int k = 0;
                        for (float v = 0.8f; v + 0.8f < _d - 3.0f; v += 1.3f, k++)
                        {
                            float g = Mid(0.5f, v, _w - 3.5f, v + 0.8f);
                            bool tomatoes = k % 4 == 1;
                            Foliage(0.6f, v, _w - 4.0f, v + 0.75f, g - 0.1f, g + (tomatoes ? 1.3f : 0.35f + 0.1f * (k % 3)), greens[k % greens.Length], tomatoes ? 0.5f : 0.05f);
                            if (tomatoes) for (float u = 1; u < _w - 4.0f; u += 1.2f) Beam(u, v + 0.37f, g, u, v + 0.37f, g + 1.6f, 0.02f, 0x19C8058);
                        }
                        float su = _w - 2.0f, sv = _d - 1.8f, sg = Ground(su, sv);
                        PaintedBox(su - 1.4f, sv - 1.2f, su + 1.4f, sv + 1.2f, sg - 0.1f, sg + 2.0f, 0x18A6A4A);
                        Poly(new[] { su - 1.6f, sv - 1.4f, sg + 2.2f, su + 1.6f, sv - 1.4f, sg + 2.2f, su + 1.6f, sv + 1.4f, sg + 2.0f, su - 1.6f, sv + 1.4f, sg + 2.0f }, 0, 0, 1, 0x16B4A30, BuildingDecorator.KindProp, 0);
                        Pipe(_w - 1.0f, 1.2f, 0.5f, Ground(_w - 1, 1.2f), Ground(_w - 1, 1.2f) + 1.2f, 0x13E6E8E, 10);
                        break;
                    }
            }
        }

        private void Silos(CatalogItem item)
        {
            float H = item.Height;
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.08f, 0x1BDBAB3, BuildingDecorator.KindProp, 0);
            float r = Math.Min(_w, _d) * 0.2f;
            var spots = new[] { (0.3f, 0.32f, 1.0f), (0.72f, 0.3f, 0.86f), (0.5f, 0.72f, 0.93f) };
            foreach (var (fu, fv, fh) in spots)
            {
                float cu = _w * fu, cv = _d * fv;
                float h = (H - 1.6f) * fh;
                Tank(cu, cv, r, g, h, 0x1C9CCCF, 1.4f);
                // anelli di rinforzo e scala alla marinara
                for (float z = g + 2; z < g + h; z += 2.4f) Pipe(cu, cv, r * 1.02f, z, z + 0.12f, 0x1A9ADB0, 18);
                Beam(cu + r + 0.25f, cv, g, cu + r + 0.25f, cv, g + h + 0.6f, 0.04f, 0x18A9096);
            }
            // coclea di carico inclinata
            Beam(_w * 0.1f, _d * 0.9f, g + 0.8f, _w * 0.3f, _d * 0.32f, g + H - 1.6f, 0.2f, 0x1A9ADB0);
        }

        /// <summary>Serra: tunnel ad arco affiancati coperti di film bianco, archi metallici, testate vetrate con la porta.</summary>
        private void Greenhouse()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.06f, 0x19C8058, BuildingDecorator.KindProp, 0);
            int tunnels = Math.Max(1, (int)Math.Round(_d / 4.2f));
            float tw = _d / tunnels;
            const int seg = 8;
            for (int t = 0; t < tunnels; t++)
            {
                float v0 = t * tw + 0.15f, v1 = (t + 1) * tw - 0.15f, cv = (v0 + v1) / 2, r = (v1 - v0) / 2;
                float zb = g + 1.6f, rz = Math.Min(r, 2.4f);
                // fianchi verticali e volta
                for (int i = 0; i < seg; i++)
                {
                    double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                    float va = cv + (float)Math.Cos(a0) * r, za = zb + (float)Math.Sin(a0) * rz;
                    float vb = cv + (float)Math.Cos(a1) * r, zbb = zb + (float)Math.Sin(a1) * rz;
                    double am = (a0 + a1) / 2;
                    Poly(new[] { 0.2f, va, za, _w - 0.2f, va, za, _w - 0.2f, vb, zbb, 0.2f, vb, zbb }, 0, (float)Math.Cos(am), (float)Math.Sin(am), 0x1E9EEEA, BuildingDecorator.KindProp, 0);
                }
                Poly(new[] { 0.2f, v0, g, _w - 0.2f, v0, g, _w - 0.2f, v0, zb, 0.2f, v0, zb }, 0, -1, 0, 0x1E4EAE6, BuildingDecorator.KindProp, 0);
                Poly(new[] { 0.2f, v1, g, _w - 0.2f, v1, g, _w - 0.2f, v1, zb, 0.2f, v1, zb }, 0, 1, 0, 0x1E4EAE6, BuildingDecorator.KindProp, 0);
                // archi metallici ogni 3 m
                for (float u = 0.25f; u < _w - 0.2f; u += 3f)
                    for (int i = 0; i < seg; i++)
                    {
                        double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                        Beam(u, cv + (float)Math.Cos(a0) * (r + 0.03f), zb + (float)Math.Sin(a0) * (rz + 0.03f), u, cv + (float)Math.Cos(a1) * (r + 0.03f), zb + (float)Math.Sin(a1) * (rz + 0.03f), 0.03f, 0x1A9ADB0);
                    }
                // testate: vetrata con la porta
                foreach (float u in new[] { 0.2f, _w - 0.2f })
                {
                    Wall(u, u < 1 ? v1 : v0, u, u < 1 ? v0 : v1, g, zb + rz * 0.9f, g, 0x1A9ADB0, 10, 1);
                    Poly(new[] { u + (u < 1 ? -0.02f : 0.02f), cv - 0.7f, g, u + (u < 1 ? -0.02f : 0.02f), cv + 0.7f, g, u + (u < 1 ? -0.02f : 0.02f), cv + 0.7f, g + 2.1f, u + (u < 1 ? -0.02f : 0.02f), cv - 0.7f, g + 2.1f },
                        u < 1 ? -1 : 1, 0, 0, 0x1556070, BuildingDecorator.KindProp, 0);
                }
            }
        }

        /// <summary>Apiario: arnie colorate col tetto sporgente sui cavalletti, in file, e la casetta del miele.</summary>
        private void Beehives()
        {
            Lawn(0, 0, _w, _d);
            uint[] colors = { 0x1E8C030, 0x14F79B5, 0x1D9534A, 0x1F2EDE2, 0x13E8E4A };
            int k = 0;
            for (float u = 0.7f; u + 0.7f < _w - 2.2f; u += 1.3f)
                for (float v = 0.8f; v + 0.7f < _d; v += 2.0f)
                {
                    float g = Mid(u, v, u + 0.6f, v + 0.5f);
                    foreach (float du in new[] { 0.05f, 0.55f })
                        Beam(u + du, v + 0.25f, g - 0.1f, u + du, v + 0.25f, g + 0.4f, 0.03f, 0x17A5A3C);
                    PaintedBox(u, v, u + 0.6f, v + 0.5f, g + 0.4f, g + 1.0f, colors[k++ % colors.Length]);
                    PaintedBox(u - 0.06f, v - 0.06f, u + 0.66f, v + 0.56f, g + 1.0f, g + 1.08f, 0x1D9D5CC);
                    PaintedBox(u + 0.2f, v - 0.01f, u + 0.4f, v, g + 0.45f, g + 0.5f, 0x1202020);
                }
            float hu = _w - 1.1f, hv = _d / 2, hg = Ground(hu, hv);
            PaintedBox(hu - 0.9f, hv - 1.2f, hu + 0.9f, hv + 1.2f, hg - 0.1f, hg + 1.5f, 0x19C7350);
            Poly(new[] { hu - 1.0f, hv - 1.35f, hg + 1.65f, hu + 1.0f, hv - 1.35f, hg + 1.65f, hu + 1.0f, hv + 1.35f, hg + 1.45f, hu - 1.0f, hv + 1.35f, hg + 1.45f }, 0, 0, 1, 0x1B25A3C, 1, 1);
        }

        // ------------------------------------------------------------------ energia

        /// <summary>Pala eolica: il rotore sta dentro la sagoma (raggio ≤ metà lato), la punta all'altezza dell'elemento.</summary>
        private void WindTurbine(float cu, float cv, float height, float halfSide)
        {
            float g = Ground(cu, cv);
            float blade = Math.Max(2f, Math.Min(height * 0.3f, halfSide - 1f));
            float hubZ = height - blade;
            Cylinder(cu, cv, 3.0f, g - 0.3f, g + 0.4f, 16, 0x1C6C2BA, 1, 1, BuildingDecorator.KindProp, 0);
            Cylinder(cu, cv, 1.6f, g - 0.5f, g + hubZ, 12, 0x1F2F4F5, 1.0f, 0.55f);
            // navicella e tre pale fisse (la rotazione arriverà con l'animazione)
            Box(cu - 1.2f, cv - 3f, cu + 1.2f, cv + 2f, g + hubZ - 1.2f, g + hubZ + 1.2f, 0x1F2F4F5, 0);
            Sphere(cu, cv - 3.2f, g + hubZ, 0.9f, 0x1F2F4F5);
            var hub = new Vec2(cu, cv - 3.4f);
            for (int k = 0; k < 3; k++)
            {
                double a = Math.PI / 2 + k * 2 * Math.PI / 3;
                var dir = new Vec2((float)Math.Cos(a), (float)Math.Sin(a));
                Blade(hub, dir, g + hubZ, blade);
            }
            // segnale rosso sulla navicella
            Sphere(cu, cv + 1.6f, g + hubZ + 1.35f, 0.18f, 0x1FF3030, BuildingDecorator.KindLight);
        }

        private void Blade(Vec2 hub, Vec2 dir, float hz, float len)
        {
            uint c = MeshBuffer.Rgb(0xF5F6F7);
            var p0 = World(hub.X, hub.Y);
            var right = _u;
            var tip = p0 + right * (dir.X * len);
            float tipZ = hz + dir.Y * len;
            var side = right * (-dir.Y * 1.1f);
            float sideZ = dir.X * 1.1f;
            var n = _v * -1f;
            int a = _mb.Add(p0.X - side.X * 0.5f, p0.Y - side.Y * 0.5f, hz - sideZ * 0.5f, n.X, n.Y, 0, c, 0, 0, 1, 1, 13, 0.5f, 0, 0);
            int b = _mb.Add(p0.X + side.X * 0.5f, p0.Y + side.Y * 0.5f, hz + sideZ * 0.5f, n.X, n.Y, 0, c, 0, 0, 1, 1, 13, 0.5f, 0, 0);
            var mid = p0 + right * (dir.X * len * 0.25f);
            float midZ = hz + dir.Y * len * 0.25f;
            int m = _mb.Add(mid.X + side.X * 0.55f, mid.Y + side.Y * 0.55f, midZ + sideZ * 0.55f, n.X, n.Y, 0, c, 0, 0, 1, 1, 13, 0.5f, 0, 0);
            int t = _mb.Add(tip.X, tip.Y, tipZ, n.X, n.Y, 0, c, 0, 0, 1, 1, 13, 0.5f, 0, 0);
            _mb.Triangle(a, b, m);
            _mb.Triangle(a, m, t);
            _mb.Triangle(a, m, b);
            _mb.Triangle(a, t, m);
        }

        /// <summary>Campo fotovoltaico: file di pannelli sui supporti, inverter, recinzione e ghiaia.</summary>
        private void SolarField()
        {
            Ground(0, 0, _w, _d, 0x1A9A487, 0.05f, BuildingDecorator.KindPlants, 0.0f);
            SolarRows(0.8f, 0.8f, _w - 0.8f, _d - 2.6f, float.NaN);
            if (_w * _d > 400)
            {
                for (int k = 0; k < 2; k++)
                {
                    float u = 2 + k * 4, g = Ground(u, _d - 1.4f);
                    PaintedBox(u, _d - 2.2f, u + 3, _d - 0.6f, g - 0.1f, g + 2.4f, 0x1F0EEE8);
                }
                LotFence(2.0f, 0, 0x1556070, true, 0.15f);
            }
        }

        /// <param name="z">quota del tetto su cui stanno i pannelli; NaN = a terra, seguendo il terreno</param>
        private void SolarRows(float u0, float v0, float u1, float v1, float z)
        {
            bool onGround = float.IsNaN(z);
            for (float v = v0 + 0.6f; v + 2.2f < v1; v += 3.4f)
            {
                float g = onGround ? Mid(u0, v, u1, v + 2.2f) : z;
                // pannello inclinato verso sud (v crescente = nord), sui pali
                SlopedPanel(u0 + 0.4f, v, u1 - 0.4f, v + 2.2f, g + 0.5f, g + 1.6f);
                for (float u = u0 + 1.0f; u < u1 - 0.6f; u += 3.5f)
                {
                    Beam(u, v + 0.4f, g - 0.2f, u, v + 0.4f, g + 0.7f, 0.04f, 0x1A9ADB0);
                    Beam(u, v + 1.8f, g - 0.2f, u, v + 1.8f, g + 1.4f, 0.04f, 0x1A9ADB0);
                }
            }
        }

        private void SlopedPanel(float u0, float v0, float u1, float v1, float zLow, float zHigh)
        {
            uint c = MeshBuffer.Rgb(0x2B3A5A);
            var p00 = World(u0, v0);
            var p10 = World(u1, v0);
            var p11 = World(u1, v1);
            var p01 = World(u0, v1);
            float dz = zHigh - zLow, dv = v1 - v0;
            var nn = _v * (-dz);
            float nl = (float)Math.Sqrt(dz * dz + dv * dv);
            // tipo 12 = pannello solare (celle disegnate dallo shader)
            int a = _mb.Add(p00.X, p00.Y, zLow, nn.X / nl, nn.Y / nl, dv / nl, c, u0, v0, 0, 0, 12, 0.5f, 1, 0);
            int b = _mb.Add(p10.X, p10.Y, zLow, nn.X / nl, nn.Y / nl, dv / nl, c, u1, v0, 0, 0, 12, 0.5f, 1, 0);
            int d = _mb.Add(p11.X, p11.Y, zHigh, nn.X / nl, nn.Y / nl, dv / nl, c, u1, v1, 0, 0, 12, 0.5f, 1, 0);
            int e = _mb.Add(p01.X, p01.Y, zHigh, nn.X / nl, nn.Y / nl, dv / nl, c, u0, v1, 0, 0, 12, 0.5f, 1, 0);
            _mb.Quad(a, b, d, e);
            uint back = MeshBuffer.Rgb(0x8A9096);
            int a2 = _mb.Add(p00.X, p00.Y, zLow, -nn.X / nl, -nn.Y / nl, -dv / nl, back, 0, 0, 0, 0, 13, 0.5f, 0, 0);
            int b2 = _mb.Add(p10.X, p10.Y, zLow, -nn.X / nl, -nn.Y / nl, -dv / nl, back, 0, 0, 0, 0, 13, 0.5f, 0, 0);
            int d2 = _mb.Add(p11.X, p11.Y, zHigh, -nn.X / nl, -nn.Y / nl, -dv / nl, back, 0, 0, 0, 0, 13, 0.5f, 0, 0);
            int e2 = _mb.Add(p01.X, p01.Y, zHigh, -nn.X / nl, -nn.Y / nl, -dv / nl, back, 0, 0, 0, 0, 13, 0.5f, 0, 0);
            _mb.Quad(a2, e2, d2, b2);
        }

        /// <summary>Condominio con l'impianto fotovoltaico sul tetto.</summary>
        private void SolarCondo(CatalogItem item, uint wall)
        {
            Condo(item, wall, ParseInt(item.Param("floors", "4"), 4), false);
            float z = _building.LastEave;
            SolarRows(2.0f, 3.6f, _w - 2.0f, _d - 2.4f, z + 0.1f);
        }

        /// <summary>
        /// Centrale idroelettrica ad acqua fluente: diga in calcestruzzo con le paratoie, bacino a monte, condotte
        /// forzate, edificio delle turbine a valle, canale di scarico e trasformatore.
        /// </summary>
        private void Hydro(CatalogItem item)
        {
            float H = item.Height;
            Lawn(0, 0, _w, _d);
            float g = Base(0, 0, _w, _d);
            float dv0 = _d * 0.52f, dv1 = _d * 0.64f;
            float crest = g + H * 0.85f;
            // bacino a monte (acqua alta) con le sponde in pietra
            PaintedBox(0.2f, dv1, 0.8f, _d - 0.2f, g - 0.3f, crest - 1.2f, 0x1A9A08C);
            PaintedBox(_w - 0.8f, dv1, _w - 0.2f, _d - 0.2f, g - 0.3f, crest - 1.2f, 0x1A9A08C);
            PaintedBox(0.2f, _d - 0.8f, _w - 0.2f, _d - 0.2f, g - 0.3f, crest - 1.2f, 0x1A9A08C);
            Flat(0.8f, dv1, _w - 0.8f, _d - 0.8f, crest - 1.6f, 0x13F7A8C, 11);
            // diga: paramento a valle inclinato, coronamento, paratoie
            Poly(new[] { 0.2f, dv0, g - 0.3f, _w - 0.2f, dv0, g - 0.3f, _w - 0.2f, dv1 - 1.5f, crest, 0.2f, dv1 - 1.5f, crest }, 0, -1, 0.4f, 0x1C9C6BF, BuildingDecorator.KindProp, 0);
            PaintedBox(0.2f, dv1 - 1.5f, _w - 0.2f, dv1, crest - 0.5f, crest, 0x1D6D3CC);
            Poly(new[] { 0.2f, dv1, crest - 1.6f, _w - 0.2f, dv1, crest - 1.6f, _w - 0.2f, dv1, crest, 0.2f, dv1, crest }, 0, 1, 0, 0x1B4B0A8, BuildingDecorator.KindProp, 0);
            for (int k = 0; k < 3; k++)
            {
                float u = _w * (0.3f + 0.2f * k);
                PaintedBox(u - 1.4f, dv1 - 1.6f, u + 1.4f, dv1 - 1.45f, crest - 3.5f, crest - 0.5f, 0x15E6A72);
                PaintedBox(u - 1.6f, dv1 - 1.5f, u + 1.6f, dv1, crest, crest + 2.4f, 0x1B0B4B8);
            }
            // condotte forzate e centrale a valle
            float pu0 = _w * 0.15f, pv1 = _d * 0.38f;
            var pw = Building(pu0, 2.0f, _w - pu0, pv1, H * 0.42f, RoofShape.Flat, 0x1D9CFBE, 0x1A9A69E, 2, BuildingUse.Industrial);
            Dress(pw, DressKind.Industrial);
            for (int k = 0; k < 2; k++)
            {
                float u = _w * (0.38f + 0.24f * k);
                Beam(u, dv1 - 1.5f, crest - 2.5f, u, pv1, g + H * 0.3f, 0.7f, 0x1556070);
            }
            // canale di scarico e trasformatore
            Flat(_w * 0.35f, 0.2f, _w * 0.65f, 2.0f, g + 0.05f, 0x14F8FC0, 11);
            PaintedBox(1.0f, 0.6f, 3.4f, 2.2f, g, g + 2.6f, 0x18E9196);
        }

        /// <summary>
        /// Centrali termiche. A gas (ciclo combinato): sale turbine, caldaie a recupero con i camini, condensatore ad aria
        /// con le ventole, trasformatori. A biomasse: caldaia alta, nastro dal deposito del cippato, camino. Geotermica:
        /// torri di raffreddamento basse, vapordotti argentati sui sostegni, edificio delle turbine.
        /// </summary>
        private void Thermal(CatalogItem item)
        {
            float H = item.Height;
            int chimneys = ParseInt(item.Param("chimneys", "1"), 1);
            bool geo = item.Param("steam", "false") == "true" || chimneys == 0;
            Ground(0, 0, _w, _d, 0x1BDB9B0, 0.06f, BuildingDecorator.KindProp);
            float g = Base(0, 0, _w, _d);
            if (geo)
            {
                float r0 = Math.Min(_w, _d) * 0.16f;
                CoolingTower(_w * 0.72f, _d * 0.7f, r0, Math.Min(H, r0 * 2.4f));
                CoolingTower(_w * 0.72f, _d * 0.28f, r0 * 0.85f, Math.Min(H * 0.9f, r0 * 2.0f));
                var hall = Building(1.5f, _d * 0.3f, _w * 0.45f, _d * 0.72f, H * 0.45f, RoofShape.Flat, 0x1D9D6CF, 0x1A9A69E, 2, BuildingUse.Industrial);
                Dress(hall, DressKind.Industrial);
                // vapordotti sui sostegni che arrivano dai pozzi
                for (int k = 0; k < 2; k++)
                {
                    float v = _d * (0.12f + 0.76f * k);
                    for (float u = 1; u < _w - 1; u += 4) Beam(u, v, g, u, v, g + 2.0f, 0.08f, 0x1A9ADB0);
                    PipeRun(1, v, _w * 0.45f, v, g + 2.3f, 0.45f);
                    PipeRun(_w * 0.45f, v, _w * 0.45f, _d * 0.5f, g + 2.3f, 0.45f);
                }
                for (int k = 0; k < 2; k++)
                {
                    float u = 2 + k * 3, v = _d * 0.12f;
                    PaintedBox(u - 0.6f, v - 0.6f, u + 0.6f, v + 0.6f, g, g + 1.6f, 0x18A9096);
                }
                return;
            }
            bool gas = chimneys >= 2;
            float hallH = H * (gas ? 0.36f : 0.45f);
            var turb = Building(1.5f, _d * 0.08f, _w * 0.55f, _d * 0.42f, hallH, RoofShape.Flat, gas ? 0x1849BB2u : 0x1C9C4B8u, 0x19A9A96, 3, BuildingUse.Industrial);
            Dress(turb, DressKind.Industrial);
            uint stackCol = 0x1C8C2BA;
            for (int k = 0; k < Math.Max(1, chimneys); k++)
            {
                // caldaia (o generatore di vapore a recupero) col suo camino
                float bu0 = _w * (gas ? 0.58f + 0.2f * k : 0.6f), bu1 = bu0 + _w * (gas ? 0.14f : 0.18f);
                float bv0 = _d * 0.1f, bv1 = _d * (gas ? 0.42f : 0.5f);
                Block(bu0, bv0, bu1, bv1, H * (gas ? 0.48f : 0.62f), 0x1B8BEC2, 4, 4, 4, 4, 1, 0x19A9A96);
                float cu = (bu0 + bu1) / 2, cv = bv1 + 2.6f;
                Cylinder(cu, cv, gas ? 2.4f : 1.6f, g - 0.3f, _z0 + H, 14, stackCol, 1f, 0.92f, 0, 12);
                Pipe(cu, cv, (gas ? 2.4f : 1.6f) * 0.95f, _z0 + H - 3, _z0 + H - 2.2f, 0x18A9096, 14);
            }
            if (gas)
            {
                // condensatore ad aria: piattaforma sui pilastri con le ventole
                float au0 = _w * 0.06f, au1 = _w * 0.5f, av0 = _d * 0.55f, av1 = _d * 0.92f;
                float az = g + H * 0.3f;
                for (float u = au0; u <= au1 + 0.1f; u += (au1 - au0) / 4)
                    for (float v = av0; v <= av1 + 0.1f; v += (av1 - av0) / 2)
                        Beam(u, v, g, u, v, az, 0.35f, 0x1A9ADB0);
                PaintedBox(au0, av0, au1, av1, az, az + 1.0f, 0x1C0C4C8);
                for (float u = au0 + 2.5f; u < au1 - 1; u += 5)
                    for (float v = av0 + 2.5f; v < av1 - 1; v += 5)
                        Cylinder(u, v, 2.0f, az + 1.0f, az + 2.6f, 14, 0x1A9ADB0, 1, 1, BuildingDecorator.KindProp, 0);
                // trasformatori
                for (int k = 0; k < 3; k++)
                    Transformer(_w * (0.62f + 0.11f * k), _d * 0.75f, g);
            }
            else
            {
                // deposito del cippato e nastro coperto fino alla caldaia
                Block(_w * 0.05f, _d * 0.58f, _w * 0.45f, _d * 0.95f, 8.0f, 0x1B8BEC2, 12, 12, 12, 12, 1, 0x19A9A96, false);
                Cone(_w * 0.78f, _d * 0.8f, Math.Min(_w, _d) * 0.14f, g, 4.5f, 0x18C6A44, BuildingDecorator.KindProp, 16);
                Beam(_w * 0.45f, _d * 0.75f, g + 6.5f, _w * 0.62f, _d * 0.45f, g + H * 0.55f, 0.55f, 0x1A9A69E);
                Transformer(_w * 0.88f, _d * 0.56f, g);
            }
            LotFence(2.2f, 8f, 0x1556070, true, 0.15f);
        }

        /// <summary>Tubo orizzontale argentato (vapordotti) tra due punti alla quota z.</summary>
        private void PipeRun(float ua, float va, float ub, float vb, float z, float r) => Beam(ua, va, z, ub, vb, z, r, 0x1C8CCD0);

        /// <summary>Trasformatore di potenza: cassone con i radiatori, isolatori passanti sul coperchio.</summary>
        private void Transformer(float cu, float cv, float g)
        {
            PaintedBox(cu - 1.8f, cv - 1.4f, cu + 1.8f, cv + 1.4f, g, g + 0.4f, 0x1B8B4AA);
            PaintedBox(cu - 1.4f, cv - 1.0f, cu + 1.4f, cv + 1.0f, g + 0.4f, g + 3.2f, 0x17C8A6E);
            for (int k = 0; k < 5; k++)
                PaintedBox(cu - 1.3f + k * 0.62f, cv + 1.0f, cu - 1.1f + k * 0.62f, cv + 1.5f, g + 0.7f, g + 2.9f, 0x1707E62);
            for (int k = -1; k <= 1; k++)
            {
                Pipe(cu + k * 0.8f, cv - 0.3f, 0.12f, g + 3.2f, g + 4.6f, 0x1B05A3C, 8);
                for (float z = g + 3.4f; z < g + 4.5f; z += 0.25f) Pipe(cu + k * 0.8f, cv - 0.3f, 0.2f, z, z + 0.08f, 0x1B05A3C, 8);
            }
        }

        /// <summary>
        /// Centrale nucleare come quelle vere: due torri di raffreddamento a iperboloide (vuote, con i pilastri alla base),
        /// edificio di contenimento del reattore con cupola, sala turbine, edificio ausiliario con il camino a bande
        /// bianche e rosse, stazione elettrica tra le torri, piazzale, strade e recinzione.
        /// </summary>
        private void Nuclear(CatalogItem item, float lv)
        {
            float H = item.Height;
            Ground(0, 0, _w, _d, 0x1BDB9B0, 0.08f, 13);
            // strade interne
            Ground(_w * 0.02f, _d * 0.31f, _w * 0.98f, _d * 0.37f, 0x1707274, 0.12f, 13);
            // torri di raffreddamento
            float r0 = Math.Min(_w * 0.19f, _d * 0.25f);
            float ht = Math.Min(H * 0.78f, r0 * 3.9f);
            CoolingTower(_w * 0.25f, _d * 0.65f, r0, ht);
            CoolingTower(_w * 0.75f, _d * 0.65f, r0, ht);
            // stazione elettrica tra le torri: trasformatori e portali
            float su0 = _w * 0.45f, su1 = _w * 0.55f;
            float sg = Base(su0, _d * 0.4f, su1, _d * 0.95f);
            for (int k = 0; k < 4; k++)
            {
                float v = _d * (0.45f + k * 0.12f);
                PaintedBox(su0 + 1.5f, v, su1 - 1.5f, v + 4f, sg, sg + 3.2f, 0x18E9196);
                Cylinder(su0 + 1f, v + 2f, 0.25f, sg, sg + 9f, 6, 0x1A9AEB4);
                Cylinder(su1 - 1f, v + 2f, 0.25f, sg, sg + 9f, 6, 0x1A9AEB4);
                PaintedBox(su0 + 0.8f, v + 1.8f, su1 - 0.8f, v + 2.2f, sg + 8.6f, sg + 9.2f, 0x1A9AEB4);
            }
            // edificio del reattore: cilindro di cemento con la cupola
            float rr = Math.Min(_w, _d) * 0.11f;
            float rg = Base(_w * 0.19f - rr, _d * 0.18f - rr, _w * 0.19f + rr, _d * 0.18f + rr);
            float rh = Math.Min(H * 0.24f, 36f);
            Cylinder(_w * 0.19f, _d * 0.18f, rr, rg - 0.5f, rg + rh, 32, 0x1DEDAD2);
            Cylinder(_w * 0.19f, _d * 0.18f, rr * 1.01f, rg + rh - 3f, rg + rh - 2.2f, 32, 0x1A5A29B);
            Dome(_w * 0.19f, _d * 0.18f, rr, rg + rh, 0x1E6E2DA, 13);
            // sala turbine (rivestimento metallico azzurro) e edificio ausiliario
            Building(_w * 0.3f, _d * 0.05f, _w * 0.72f, _d * 0.28f, Math.Min(26f, H * 0.18f), RoofShape.Flat, 0x1849BB2, 0x19A9A96, 3, BuildingUse.Industrial);
            Building(_w * 0.75f, _d * 0.05f, _w * 0.95f, _d * 0.26f, Math.Min(16f, H * 0.11f), RoofShape.Flat, 0x1CFCBC2, 0x19A9A96, 3, BuildingUse.Industrial);
            // camino di ventilazione: fusto chiaro, ultime bande bianche e rosse
            float cu = _w * 0.87f, cv = _d * 0.15f;
            float cg = Ground(cu, cv);
            float top = _z0 + H;
            float bands = Math.Min(20f, (top - cg) * 0.15f);
            Cylinder(cu, cv, 2.8f, cg - 0.5f, top - bands, 16, 0x1D6D3CC, 1f, 0.78f);
            for (int k = 0; k < 4; k++)
            {
                float z0 = top - bands + bands * k / 4, z1 = top - bands + bands * (k + 1) / 4;
                float s0 = 0.78f - 0.08f * k / 4, s1 = 0.78f - 0.08f * (k + 1) / 4;
                Cylinder(cu, cv, 2.8f, z0, z1, 16, k % 2 == 0 ? 0x1C8382Eu : 0x1F2F0EBu, s0, s1);
            }
            LotFence(2.4f, 12f, 0x1556070, true, 0.15f);
        }

        /// <summary>Torre di raffreddamento a iperboloide (vuota): base larga con i pilastri, strozzatura, bocca larga in cima.</summary>
        private void CoolingTower(float cu, float cv, float r0, float height)
        {
            float g = Base(cu - r0, cv - r0, cu + r0, cv + r0);
            var c = World(cu, cv);
            const int sides = 32, rings = 12;
            float zt = height * 0.78f, rt = r0 * 0.6f;
            float k = zt / (float)Math.Sqrt((r0 / rt) * (r0 / rt) - 1);
            float R(float z) => rt * (float)Math.Sqrt(1 + ((z - zt) / k) * ((z - zt) / k));
            float legs = Math.Min(6f, height * 0.05f);
            uint shell = MeshBuffer.Rgb(0xD2CFC8), inner = MeshBuffer.Rgb(0x8D8A84), stain = MeshBuffer.Rgb(0xB4B0A8), dark = MeshBuffer.Rgb(0x3B3A37);
            for (int j = 0; j < rings; j++)
            {
                float z0 = legs + (height - legs) * j / rings, z1 = legs + (height - legs) * (j + 1) / rings;
                float ra = R(z0), rb = R(z1);
                float slope = (rb - ra) / (z1 - z0);
                uint col = j == rings - 1 ? stain : shell;
                for (int i = 0; i < sides; i++)
                {
                    double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                    var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                    var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                    float nl = (float)Math.Sqrt(1 + slope * slope);
                    float nx = (float)Math.Cos(am) / nl, ny = (float)Math.Sin(am) / nl, nz = -slope / nl;
                    var p0 = c + d0 * ra;
                    var p1 = c + d1 * ra;
                    var p2 = c + d1 * rb;
                    var p3 = c + d0 * rb;
                    float len = (p1 - p0).Length;
                    int i0 = _mb.Add(p0.X, p0.Y, g + z0, nx, ny, nz, col, 0, 0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i1 = _mb.Add(p1.X, p1.Y, g + z0, nx, ny, nz, col, len, 0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i2 = _mb.Add(p2.X, p2.Y, g + z1, nx, ny, nz, col, len, z1 - z0, len, z1 - z0, 13, 0.5f, 0, 0);
                    int i3 = _mb.Add(p3.X, p3.Y, g + z1, nx, ny, nz, col, 0, z1 - z0, len, z1 - z0, 13, 0.5f, 0, 0);
                    _mb.Quad(i0, i1, i2, i3);
                    // interno (si vede dall'alto): stesso guscio, girato verso il centro
                    var q0 = c + d0 * (ra - 0.4f);
                    var q1 = c + d1 * (ra - 0.4f);
                    var q2 = c + d1 * (rb - 0.4f);
                    var q3 = c + d0 * (rb - 0.4f);
                    int j0 = _mb.Add(q0.X, q0.Y, g + z0, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j1 = _mb.Add(q1.X, q1.Y, g + z0, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j2 = _mb.Add(q2.X, q2.Y, g + z1, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    int j3 = _mb.Add(q3.X, q3.Y, g + z1, -nx, -ny, -nz, inner, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                    _mb.Quad(j0, j3, j2, j1);
                }
            }
            // bordo in cima e pilastri alla base (tra i pilastri il buio dell'ingresso dell'aria)
            float rtop = R(height);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides, am = (a0 + a1) / 2;
                var d0 = new Vec2((float)Math.Cos(a0), (float)Math.Sin(a0));
                var d1 = new Vec2((float)Math.Cos(a1), (float)Math.Sin(a1));
                var o0 = c + d0 * rtop;
                var o1 = c + d1 * rtop;
                var n0 = c + d0 * (rtop - 0.4f);
                var n1 = c + d1 * (rtop - 0.4f);
                int t0 = _mb.Add(o0.X, o0.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t1 = _mb.Add(o1.X, o1.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t2 = _mb.Add(n1.X, n1.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int t3 = _mb.Add(n0.X, n0.Y, g + height, 0, 0, 1, stain, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                _mb.Quad(t0, t1, t2, t3);
                var b0 = c + d0 * r0;
                var b1 = c + d1 * r0;
                var l0 = c + d0 * R(legs);
                var l1 = c + d1 * R(legs);
                uint col = i % 2 == 0 ? shell : dark;
                float nx = (float)Math.Cos(am), ny = (float)Math.Sin(am);
                int k0 = _mb.Add(b0.X, b0.Y, g - 0.5f, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k1 = _mb.Add(b1.X, b1.Y, g - 0.5f, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k2 = _mb.Add(l1.X, l1.Y, g + legs, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                int k3 = _mb.Add(l0.X, l0.Y, g + legs, nx, ny, 0, col, 0, 0, 0, 0, 13, 0.5f, 0, 0);
                _mb.Quad(k0, k1, k2, k3);
            }
            // fondo scuro della vasca (si vede dall'alto, dentro la torre)
            Flat(cu - R(legs) * 0.7f, cv - R(legs) * 0.7f, cu + R(legs) * 0.7f, cv + R(legs) * 0.7f, g + 0.3f, 0x14A5E66, 11);
        }

        /// <summary>Sottostazione elettrica: ghiaia, trasformatori, portali con le sbarre, sezionatori, edificio comandi, recinzione.</summary>
        private void Substation()
        {
            Ground(0, 0, _w, _d, 0x1A9A399, 0.08f, 10);
            float g = Base(0, 0, _w, _d);
            Transformer(_w * 0.3f, _d * 0.35f, g);
            if (_w > 14) Transformer(_w * 0.62f, _d * 0.35f, g);
            // portali e sbarre
            float pz = g + 5.2f;
            foreach (float v in new[] { _d * 0.62f, _d * 0.86f })
            {
                Beam(1.5f, v, g, 1.5f, v, pz, 0.12f, 0x1A9AEB4);
                Beam(_w - 1.5f, v, g, _w - 1.5f, v, pz, 0.12f, 0x1A9AEB4);
                Beam(1.5f, v, pz, _w - 1.5f, v, pz, 0.12f, 0x1A9AEB4);
                for (int k = 0; k < 3; k++)
                {
                    float u = _w * (0.3f + 0.2f * k);
                    Beam(u, v, pz, u, v, pz - 0.9f, 0.08f, 0x1B05A3C);
                    Beam(u, v, pz - 0.9f, u, _d * 0.74f, pz - 1.0f, 0.03f, 0x1606468);
                }
            }
            // edificio comandi
            Block(_w - 5.0f, 0.8f, _w - 0.8f, 4.2f, 3.2f, 0x1E8E4DA, 12, 12, 12, 12, 1, 0x1A9A69E, false);
            LotFence(2.4f, 0, 0x1556070, true, 0.15f);
        }

        /// <summary>Traliccio dell'alta tensione: quattro montanti che si stringono, crociere, mensole con gli isolatori, cimino.</summary>
        private void Pylon(float h)
        {
            float g = Base(0, 0, _w, _d);
            float cu = _w / 2, cv = _d / 2;
            float b = Math.Min(_w, _d) * 0.42f, waist = b * 0.3f;
            uint steel = 0x18A9098;
            float zw = g + h * 0.62f;
            float Half(float z) => z < zw ? b + (waist - b) * (z - g) / (zw - g) : waist;
            foreach (int su in new[] { -1, 1 })
                foreach (int sv in new[] { -1, 1 })
                {
                    Beam(cu + su * b, cv + sv * b, g - 0.3f, cu + su * waist, cv + sv * waist, zw, 0.09f, steel);
                    Beam(cu + su * waist, cv + sv * waist, zw, cu + su * waist * 0.7f, cv + sv * waist * 0.7f, g + h - 0.5f, 0.07f, steel);
                    PaintedBox(cu + su * b - 0.35f, cv + sv * b - 0.35f, cu + su * b + 0.35f, cv + sv * b + 0.35f, g - 0.3f, g + 0.4f, 0x1C6C2BA);
                }
            // crociere sulle quattro facce
            int panels = 6;
            for (int p = 0; p < panels; p++)
            {
                float z0 = g + (zw - g) * p / panels, z1 = g + (zw - g) * (p + 1) / panels;
                float h0 = Half(z0), h1 = Half(z1);
                foreach (int s in new[] { -1, 1 })
                {
                    Beam(cu - h0, cv + s * h0, z0, cu + h1, cv + s * h1, z1, 0.035f, steel);
                    Beam(cu + h0, cv + s * h0, z0, cu - h1, cv + s * h1, z1, 0.035f, steel);
                    Beam(cu + s * h0, cv - h0, z0, cu + s * h1, cv + h1, z1, 0.035f, steel);
                    Beam(cu + s * h0, cv + h0, z0, cu + s * h1, cv - h1, z1, 0.035f, steel);
                }
                Beam(cu - h1, cv - h1, z1, cu + h1, cv - h1, z1, 0.035f, steel);
                Beam(cu - h1, cv + h1, z1, cu + h1, cv + h1, z1, 0.035f, steel);
            }
            // mensole con gli isolatori a catena e i conduttori
            float arm = _w * 0.5f - 0.15f;
            foreach (float za in new[] { g + h * 0.7f, g + h * 0.84f })
            {
                Beam(cu - arm, cv, za, cu + arm, cv, za, 0.12f, steel);
                Beam(cu - arm, cv, za, cu - waist, cv, za + 1.6f, 0.05f, steel);
                Beam(cu + arm, cv, za, cu + waist, cv, za + 1.6f, 0.05f, steel);
                foreach (int s in new[] { -1, 1 })
                {
                    float iu = cu + s * (arm - 0.3f);
                    Pipe(iu, cv, 0.04f, za - 1.8f, za, 0x1606468, 6);
                    for (float z = za - 1.7f; z < za - 0.1f; z += 0.2f) Pipe(iu, cv, 0.14f, z, z + 0.07f, 0x19FB0B8, 8);
                }
            }
            Beam(cu, cv, g + h - 0.5f, cu, cv, g + h, 0.08f, steel);
        }
    }
}
