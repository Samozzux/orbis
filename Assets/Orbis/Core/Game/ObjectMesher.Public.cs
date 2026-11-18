using System;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Meshing;

namespace Orbis.Core.Game
{
    /// <summary>Servizi pubblici (scuole, ospedali, caserme, poste), cultura (musei, teatri, chiese) e divertimento.</summary>
    public sealed partial class ObjectMesher
    {
        private void Civic(CatalogItem item, uint wall, uint roof)
        {
            int floors = ParseInt(item.Param("floors", "2"), 2);
            switch (item.Param("kind", "school"))
            {
                case "school": School(item, wall, floors); break;
                case "university": University(item, wall); break;
                case "clinic": Clinic(item, wall, floors); break;
                case "hospital": Hospital(item, wall); break;
                case "police": Carabinieri(item, wall, roof, floors); break;
                case "fire": FireStation(item, wall); break;
                case "post": PostOffice(item, wall, roof, floors); break;
                case "library": Library(item, wall, roof, floors); break;
                case "museum": Museum(item, wall); break;
                case "theatre": Theatre(item, wall, roof); break;
                default: School(item, wall, floors); break;
            }
        }

        /// <summary>Asta con il tricolore e quella con la bandiera europea davanti agli edifici pubblici.</summary>
        private void PublicFlags(float u, float v, float h)
        {
            FlagPole(u, v, h, 1.5f);
            FlagPole(u + 2.2f, v, h, 1.5f, 0x1003399);
        }

        // ------------------------------------------------------------------ scuole

        /// <summary>
        /// Scuola: edificio a nastri di finestre con il tetto piano (l'infanzia bassa e colorata col tetto a falde),
        /// pensilina d'ingresso, bandiere, cortile con gli alberi, recinzione; giochi per i piccoli, campo da basket al liceo.
        /// </summary>
        private void School(CatalogItem item, uint wall, int floors)
        {
            bool kinder = floors <= 1;
            Lawn(0, 0, _w, _d);
            float front = Math.Min(4.5f, _d * 0.22f);
            Paving(0, 0, _w, front, 0x1CFC6B4);
            float depth = kinder ? Math.Max(5f, Math.Min(7f, _d - front - 5.5f)) : Math.Min(_d - front - 3.0f, 11f);
            float u0 = 1.5f, u1 = _w - 1.5f, v0 = front, v1 = front + depth;
            uint col = wall != 0 ? wall : (kinder ? 0x1F0C75Eu : 0x1E9DCC4u);
            var b = Building(u0, v0, u1, v1, item.Height, kinder ? RoofShape.Gabled : RoofShape.Flat, col, kinder ? 0x1C0623Fu : 0x1A9A69Eu,
                floors, kinder ? BuildingUse.Kindergarten : BuildingUse.School);
            Dress(b, DressKind.School);
            float g = FloorZ(0);
            // ala laterale a L per le scuole grandi
            if (_w > 40 && !kinder)
            {
                var w2 = Building(u0, v1 - 0.01f, u0 + Math.Min(14, _w * 0.28f), _d - 1.5f, item.Height, RoofShape.Flat, col, 0x1A9A69E, floors, BuildingUse.School);
                Dress(w2, DressKind.Modern);
            }
            // pensilina d'ingresso
            float mid = _w / 2;
            PaintedBox(mid - 3.0f, front - 2.4f, mid + 3.0f, front, g + 3.0f, g + 3.3f, 0x1E6E6E2);
            foreach (float u in new[] { mid - 2.6f, mid + 2.6f }) Pipe(u, front - 2.1f, 0.14f, g, g + 3.0f, 0x1C0C4C8, 8);
            PublicFlags(1.2f, front - 1.2f, Math.Min(item.Height * 1.2f + 1.5f, 9f));
            LotFence(1.6f, 6f, 0x1305040, false);
            // cortile dietro
            float yv0 = v1 + 1.0f;
            if (kinder)
            {
                Playset(_w * 0.2f, yv0 + 0.5f, _w * 0.55f, _d - 1.0f);
                Tree(PlantKind.Broadleaf, _w * 0.78f, _d - 2.5f, 21);
            }
            else if (_d - yv0 > 9 && _w > 40)
            {
                float cu0 = u0 + 16, cu1 = Math.Min(_w - 1.5f, cu0 + 30);
                float cg = Base(cu0, yv0, cu1, _d - 0.8f);
                Flat(cu0, yv0, cu1, _d - 0.8f, cg + 0.1f, 0x14F7FA6, 8, 3);
                Hoops(cu0, yv0, cu1, _d - 0.8f, cg + 0.1f);
            }
            else
            {
                float t0 = _w > 40 ? 0.42f : 0.2f, dt = _w > 40 ? 0.22f : 0.3f;
                for (int k = 0; k < 3; k++) Tree(PlantKind.Broadleaf, _w * (t0 + dt * k), Math.Min(_d - 1.5f, yv0 + 2.5f), 22 + k);
                for (int k = 0; k < 2; k++) Bench(_w * (t0 + 0.13f + dt * k), yv0 + 0.6f, _w * (t0 + 0.13f + dt * k) + 1.6f, yv0 + 1.1f);
            }
        }

        /// <summary>Giochi per bambini (scivolo, altalena, sabbiera) sul rettangolo indicato.</summary>
        private void Playset(float u0, float v0, float u1, float v1)
        {
            float g = Base(u0, v0, u1, v1);
            Flat(u0, v0, u1, v1, g + 0.08f, 0x1B5553F, BuildingDecorator.KindProp, 0);
            float w = u1 - u0, d = v1 - v0;
            // scivolo: torretta, scaletta e scivolo inclinato
            float su = u0 + w * 0.2f, sv = v0 + d * 0.3f;
            foreach (float du in new[] { -0.5f, 0.5f })
                foreach (float dv in new[] { -0.5f, 0.5f })
                    Beam(su + du, sv + dv, g, su + du, sv + dv, g + 2.2f, 0.05f, 0x1D9534A);
            PaintedBox(su - 0.55f, sv - 0.55f, su + 0.55f, sv + 0.55f, g + 1.3f, g + 1.4f, 0x1E8C030);
            Cone(su, sv, 0.85f, g + 2.2f, 0.7f, 0x14F79B5, BuildingDecorator.KindProp, 4);
            Poly(new[] { su + 0.55f, sv - 0.35f, g + 1.4f, su + 2.6f, sv - 0.35f, g + 0.25f, su + 2.6f, sv + 0.35f, g + 0.25f, su + 0.55f, sv + 0.35f, g + 1.4f },
                0, 0, 1, 0x1E8C030, BuildingDecorator.KindProp, 0);
            for (int k = 0; k < 5; k++) Beam(su - 0.55f - 0.25f * k, sv - 0.3f, g + 1.3f - 0.26f * k, su - 0.55f - 0.25f * k, sv + 0.3f, g + 1.3f - 0.26f * k, 0.03f, 0x1F2F2F2);
            // altalena: telaio a cavalletto e due seggiolini
            float au = u0 + w * 0.7f, av = v0 + d * 0.3f;
            foreach (float s in new[] { -1.4f, 1.4f })
            {
                Beam(au + s, av - 0.8f, g, au + s, av, g + 2.3f, 0.05f, 0x14F79B5);
                Beam(au + s, av + 0.8f, g, au + s, av, g + 2.3f, 0.05f, 0x14F79B5);
            }
            Beam(au - 1.4f, av, g + 2.3f, au + 1.4f, av, g + 2.3f, 0.05f, 0x14F79B5);
            foreach (float s in new[] { -0.6f, 0.6f })
            {
                Beam(au + s - 0.2f, av, g + 2.3f, au + s - 0.2f, av, g + 0.5f, 0.01f, 0x1333333);
                Beam(au + s + 0.2f, av, g + 2.3f, au + s + 0.2f, av, g + 0.5f, 0.01f, 0x1333333);
                PaintedBox(au + s - 0.25f, av - 0.12f, au + s + 0.25f, av + 0.12f, g + 0.45f, g + 0.5f, 0x1222222);
            }
            // sabbiera
            float bu = u0 + w * 0.45f, bv = v0 + d * 0.75f;
            PaintedBox(bu - 1.2f, bv - 1.0f, bu + 1.2f, bv - 0.85f, g, g + 0.3f, 0x19C7350);
            PaintedBox(bu - 1.2f, bv + 0.85f, bu + 1.2f, bv + 1.0f, g, g + 0.3f, 0x19C7350);
            PaintedBox(bu - 1.2f, bv - 0.85f, bu - 1.05f, bv + 0.85f, g, g + 0.3f, 0x19C7350);
            PaintedBox(bu + 1.05f, bv - 0.85f, bu + 1.2f, bv + 0.85f, g, g + 0.3f, 0x19C7350);
            Flat(bu - 1.05f, bv - 0.85f, bu + 1.05f, bv + 0.85f, g + 0.22f, 0x1E3CF9C, BuildingDecorator.KindProp, 0);
        }

        /// <summary>Due canestri (palo, braccio, tabellone, ferro) alle teste di un campo da basket.</summary>
        private void Hoops(float u0, float v0, float u1, float v1, float g)
        {
            bool alongU = u1 - u0 >= v1 - v0;
            float cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
            float L = alongU ? u1 - u0 : v1 - v0;
            float cl = Math.Min(28f, L - 2f);
            for (int s = -1; s <= 1; s += 2)
            {
                float a = cl / 2 + 0.2f;
                float pu = alongU ? cu + s * (a + 1.0f) : cu, pv = alongU ? cv : cv + s * (a + 1.0f);
                float bu = alongU ? cu + s * (a - 1.2f) : cu, bv = alongU ? cv : cv + s * (a - 1.2f);
                Pipe(pu, pv, 0.1f, g, g + 3.6f, 0x1F2F2F2, 8);
                Beam(pu, pv, g + 3.5f, bu, bv, g + 3.5f, 0.06f, 0x1F2F2F2);
                if (alongU) PaintedBox(bu - 0.04f, bv - 0.9f, bu + 0.04f, bv + 0.9f, g + 2.9f, g + 3.95f, 0x1FAFAFA);
                else PaintedBox(bu - 0.9f, bv - 0.04f, bu + 0.9f, bv + 0.04f, g + 2.9f, g + 3.95f, 0x1FAFAFA);
                float ru = alongU ? bu - s * 0.3f : bu, rv = alongU ? bv : bv - s * 0.3f;
                Cylinder(ru, rv, 0.23f, g + 3.03f, g + 3.06f, 10, 0x1E06A1E, 1f, 1f, BuildingDecorator.KindProp, 0);
            }
        }

        /// <summary>Università: corpo principale e due ali attorno al prato del chiostro, padiglione vetrato, piazza con le bandiere.</summary>
        private void University(CatalogItem item, uint wall)
        {
            uint col = wall != 0 ? wall : 0x1D9C9A8u;
            Lawn(0, 0, _w, _d);
            float front = _d * 0.16f;
            Paving(0, 0, _w, front, 0x1D2C9B8);
            float H = item.Height;
            var main = Building(_w * 0.07f, _d * 0.62f, _w * 0.93f, _d - 2.0f, H, RoofShape.Flat, col, 0x1A9A69E, 4, BuildingUse.University);
            Dress(main, DressKind.School);
            var west = Building(_w * 0.07f, front, _w * 0.27f, _d * 0.62f, H * 0.77f, RoofShape.Flat, col, 0x1A9A69E, 3, BuildingUse.University);
            Dress(west, DressKind.Modern);
            var east = Building(_w * 0.73f, front, _w * 0.93f, _d * 0.62f, H * 0.77f, RoofShape.Flat, col, 0x1A9A69E, 3, BuildingUse.University);
            Dress(east, DressKind.Modern);
            // chiostro: viali a croce, alberi e panchine
            float qu0 = _w * 0.27f, qu1 = _w * 0.73f, qv0 = front, qv1 = _d * 0.62f;
            float qm = (qu0 + qu1) / 2, qvm = (qv0 + qv1) / 2;
            Paving(qm - 1.5f, qv0, qm + 1.5f, qv1, 0x1D2C9B8, 0.09f);
            Paving(qu0, qvm - 1.2f, qu1, qvm + 1.2f, 0x1D2C9B8, 0.09f);
            for (int k = 0; k < 4; k++)
            {
                Tree(PlantKind.Broadleaf, qu0 + 4 + k * (qu1 - qu0 - 8) / 3, qv0 + 3.5f, 30 + k);
                Tree(PlantKind.Broadleaf, qu0 + 4 + k * (qu1 - qu0 - 8) / 3, qv1 - 3.5f, 40 + k);
            }
            for (int k = 0; k < 4; k++) Bench(qm + 2.2f, qv0 + 2 + k * 5, qm + 3.8f, qv0 + 2.5f + k * 5);
            // padiglione vetrato della biblioteca
            Block(qm + 4, qvm + 2.5f, qu1 - 4, qv1 - 6, 5.2f, 0x1303438, 10, 10, 10, 10, 1, 0x1A9A69E, false);
            // piazza d'ingresso: bandiere e stele col nome
            PublicFlags(qm - 6, front * 0.5f, 10f);
            FlagPole(qm + 6, front * 0.5f, 10f, 1.5f, 0x1005B9A);
            float sg = Ground(qm, front * 0.6f);
            PaintedBox(qm - 3, front * 0.6f - 0.3f, qm + 3, front * 0.6f + 0.3f, sg, sg + 1.4f, 0x1E8E2D6);
            Sign(qm - 2.6f, front * 0.6f - 0.31f, qm + 2.6f, front * 0.6f - 0.31f, sg + 0.45f, sg + 1.1f, 0x11C2E5A);
        }

        // ------------------------------------------------------------------ sanità

        /// <summary>Ambulatorio: palazzina bianca con la croce rossa accesa, rampa d'accesso e posti auto.</summary>
        private void Clinic(CatalogItem item, uint wall, int floors)
        {
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            float front = 2.6f;
            var b = Building(1.0f, front, _w - 1.0f, _d - 1.0f, item.Height, RoofShape.Flat, wall != 0 ? wall : 0x1F2F0EAu, 0x1A9A69E, floors, BuildingUse.Hospital);
            Dress(b, DressKind.Hospital);
            float g = FloorZ(0);
            // rampa per le carrozzine con il corrimano
            Poly(new[] { 1.4f, front - 1.2f, g - 0.2f, 4.6f, front - 1.2f, g + 0.2f, 4.6f, front, g + 0.2f, 1.4f, front, g - 0.2f }, 0, 0, 1, 0x1C9C2B5, 10, 1);
            Beam(1.4f, front - 1.15f, g + 0.7f, 4.6f, front - 1.15f, g + 1.1f, 0.03f, 0x1B0B4B8);
        }

        /// <summary>
        /// Ospedale: piastra a due piani, monoblocco alto con l'eliporto sul tetto, due torri dei reparti, pronto
        /// soccorso con la pensilina rossa e l'ambulanza, croci rosse accese, parcheggio davanti.
        /// </summary>
        private void Hospital(CatalogItem item, uint wall)
        {
            uint col = wall != 0 ? wall : 0x1F0EEE8u;
            float H = item.Height;
            ParkingLot(1, 0, _w * 0.74f, _d * 0.2f, 111, true);
            Paving(_w * 0.74f, 0, _w, _d * 0.24f, 0x1BDBAB3);
            Lawn(0, _d * 0.2f, _w, _d);
            float pv0 = _d * 0.24f, pv1 = _d - 3.5f;
            var podium = Building(_w * 0.04f, pv0, _w * 0.96f, pv1, 8.2f, RoofShape.Flat, col, 0x1A9A69E, 2, BuildingUse.Hospital);
            Dress(podium, DressKind.Hospital);
            float g = FloorZ(0);
            float tu0 = _w * 0.32f, tu1 = _w * 0.68f, tv0 = pv0 + 4, tv1 = pv1 - 4;
            var tower = Building(tu0, tv0, tu1, tv1, H, RoofShape.Flat, col, 0x1A9A69E, 6, BuildingUse.Hospital);
            Dress(tower, DressKind.Modern);
            foreach (float u in new[] { _w * 0.08f, _w * 0.76f })
            {
                var wing = Building(u, pv0 + 6, u + _w * 0.16f, pv1 - 2, H * 0.68f, RoofShape.Flat, Lighten(col, 0.02f), 0x1A9A69E, 4, BuildingUse.Hospital);
                Dress(wing, DressKind.Modern);
            }
            // eliporto sul monoblocco
            float hu = (tu0 + tu1) / 2, hv = (tv0 + tv1) / 2, hz = g + H;
            float hr = Math.Min(tu1 - tu0, tv1 - tv0) * 0.36f;
            Flat(hu - hr, hv - hr, hu + hr, hv + hr, hz + 0.35f, 0x16A6E72, BuildingDecorator.KindProp, 0);
            HelipadMark(hu, hv, hr * 0.9f, hz + 0.37f);
            // croce rossa accesa sul monoblocco
            float cz = g + H - 3.2f, cu = (tu0 + tu1) / 2;
            Poly(new[] { cu - 1.8f, tv0 - 0.05f, cz - 0.6f, cu + 1.8f, tv0 - 0.05f, cz - 0.6f, cu + 1.8f, tv0 - 0.05f, cz + 0.6f, cu - 1.8f, tv0 - 0.05f, cz + 0.6f },
                0, -1, 0, 0x1E53935, BuildingDecorator.KindLight, 1);
            Poly(new[] { cu - 0.6f, tv0 - 0.06f, cz - 1.8f, cu + 0.6f, tv0 - 0.06f, cz - 1.8f, cu + 0.6f, tv0 - 0.06f, cz + 1.8f, cu - 0.6f, tv0 - 0.06f, cz + 1.8f },
                0, -1, 0, 0x1E53935, BuildingDecorator.KindLight, 1);
            // pronto soccorso: pensilina, insegna rossa, ambulanze
            float eu0 = _w * 0.78f, eu1 = _w * 0.95f;
            PaintedBox(eu0, pv0 - 5.5f, eu1, pv0, g + 4.2f, g + 4.6f, 0x1F2F0EA);
            Poly(new[] { eu0, pv0 - 5.52f, g + 3.9f, eu1, pv0 - 5.52f, g + 3.9f, eu1, pv0 - 5.52f, g + 4.6f, eu0, pv0 - 5.52f, g + 4.6f }, 0, -1, 0, 0x1C62828, BuildingDecorator.KindProp, 0);
            foreach (float u in new[] { eu0 + 0.5f, eu1 - 0.5f }) Pipe(u, pv0 - 5.1f, 0.2f, g, g + 4.2f, 0x1C0C4C8, 8);
            Sign(eu0 + 1, pv0 - 0.02f, eu1 - 1, pv0 - 0.02f, g + 4.8f, g + 5.8f, 0x1C62828);
            Van((eu0 + eu1) / 2, pv0 - 3.2f, (float)Math.PI / 2, 0x1F4F2EE, 0x1E87722);
            if (_level >= 2) Van(eu0 - 3.0f, _d * 0.1f, (float)Math.PI / 2, 0x1F4F2EE, 0x1E87722);
            PublicFlags(_w * 0.4f, pv0 - 1.5f, 9f);
        }

        /// <summary>Segnaletica dell'eliporto: cerchio e "H" bianchi, luci verdi sul bordo.</summary>
        private void HelipadMark(float cu, float cv, float r, float z)
        {
            uint white = MeshBuffer.Rgb(0xF2F2F2);
            var c = World(cu, cv);
            const int n = 24;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                var d0 = _u * (float)Math.Cos(a0) + _v * (float)Math.Sin(a0);
                var d1 = _u * (float)Math.Cos(a1) + _v * (float)Math.Sin(a1);
                var o0 = c + d0 * r;
                var o1 = c + d1 * r;
                var i0 = c + d0 * (r - 0.5f);
                var i1 = c + d1 * (r - 0.5f);
                QuadW(new Vec3(i0.X, i0.Y, z), new Vec3(o0.X, o0.Y, z), new Vec3(o1.X, o1.Y, z), new Vec3(i1.X, i1.Y, z), new Vec3(0, 0, 1), white, BuildingDecorator.KindProp, 0);
                if (i % 3 == 0) Pipe(cu + (float)Math.Cos(a0) * (r + 0.3f), cv + (float)Math.Sin(a0) * (r + 0.3f), 0.1f, z, z + 0.15f, 0x13CE070, 6);
            }
            float s = r * 0.42f;
            Flat(cu - s, cv - s, cu - s + 0.7f, cv + s, z + 0.01f, 0x1F2F2F2, BuildingDecorator.KindProp, 0);
            Flat(cu + s - 0.7f, cv - s, cu + s, cv + s, z + 0.01f, 0x1F2F2F2, BuildingDecorator.KindProp, 0);
            Flat(cu - s, cv - 0.35f, cu + s, cv + 0.35f, z + 0.01f, 0x1F2F2F2, BuildingDecorator.KindProp, 0);
        }

        // ------------------------------------------------------------------ sicurezza

        /// <summary>Caserma dei carabinieri: palazzina col tetto a padiglione, insegna, bandiere, cortile murato col cancello e le gazzelle.</summary>
        private void Carabinieri(CatalogItem item, uint wall, uint roof, int floors)
        {
            Paving(0, 0, _w, _d, 0x1C9C1B2);
            float front = 3.0f, depth = Math.Min(10f, _d * 0.5f);
            var b = Building(1.5f, front, _w - 1.5f, front + depth, item.Height, RoofShape.Hipped, wall != 0 ? wall : 0x1E3C48Eu, roof, floors, BuildingUse.Civic);
            Dress(b, DressKind.Civic);
            float g = FloorZ(0);
            // muro di cinta del cortile con il cancello scorrevole
            float yv0 = front + depth;
            uint wallCol = 0x1E3C48E;
            PaintedBox(0.3f, yv0, 0.6f, _d - 0.3f, g - 0.3f, g + 2.4f, wallCol);
            PaintedBox(_w - 0.6f, yv0, _w - 0.3f, _d - 0.3f, g - 0.3f, g + 2.4f, wallCol);
            PaintedBox(0.3f, _d - 0.6f, _w - 0.3f, _d - 0.3f, g - 0.3f, g + 2.4f, wallCol);
            // gazzelle (blu scuro con la banda rossa) nel cortile e davanti
            for (int k = 0; k < Math.Min(3, 1 + _level / 2); k++)
                CarabinieriCar(3.5f + k * 5.0f, yv0 + 2.6f, 0);
            CarabinieriCar(_w - 4.0f, front * 0.5f, 0);
            PublicFlags(2.0f, front * 0.5f, 8f);
        }

        private void CarabinieriCar(float cu, float cv, float angle)
        {
            CarBody(cu, cv, angle, 0x11D2237);
            float g = Ground(cu, cv);
            OBox(cu, cv, angle, 2.17f, 0.9f, g + 0.62f, g + 0.7f, 0x1B3261E, 13, 0, false);
            OBox(cu - 0.2f, cv, angle, 0.35f, 0.3f, g + 1.45f, g + 1.58f, 0x12A6FD6, BuildingDecorator.KindLight, 1);
        }

        /// <summary>
        /// Caserma dei vigili del fuoco: rimessa con i portoni rossi, palazzina degli uffici, torre di addestramento,
        /// insegna rossa, piazzale con le autopompe.
        /// </summary>
        private void FireStation(CatalogItem item, uint wall)
        {
            Ground(0, 0, _w, _d, 0x1B4B0A8, 0.07f, BuildingDecorator.KindProp);
            float front = 7.0f;
            float gu1 = _w * 0.62f;
            float g = Block(1.0f, front, gu1, _d - 1.0f, 7.2f, 0x1E9E4DA, 12, 12, 12, 12, 1, 0x1A9A69E);
            // portoni rossi della rimessa
            int doors = Math.Max(2, (int)((gu1 - 2) / 5.0f));
            float dw = (gu1 - 2) / doors;
            for (int k = 0; k < doors; k++)
            {
                float a = 1.5f + k * dw + 0.5f, b = 1.5f + (k + 1) * dw - 0.9f;
                Poly(new[] { a, front - 0.04f, g, b, front - 0.04f, g, b, front - 0.04f, g + 4.6f, a, front - 0.04f, g + 4.6f }, 0, -1, 0, 0x1B3261E, BuildingDecorator.KindProp, 0);
                // righe orizzontali della serranda
                for (float z = g + 0.6f; z < g + 4.5f; z += 0.6f)
                    Poly(new[] { a, front - 0.05f, z, b, front - 0.05f, z, b, front - 0.05f, z + 0.05f, a, front - 0.05f, z + 0.05f }, 0, -1, 0, 0x18E1D16, BuildingDecorator.KindProp, 0);
            }
            Sign(2.0f, front - 0.06f, gu1 - 1.0f, front - 0.06f, g + 5.2f, g + 6.4f, 0x1B3261E);
            var office = Building(gu1, front, _w - 1.0f, front + Math.Min(12f, _d - front - 7f), 8.2f, RoofShape.Flat, wall != 0 ? wall : 0x1E9E4DAu, 0x1A9A69E, 2, BuildingUse.Civic);
            Dress(office, DressKind.Civic, (gu1 + _w) / 2);
            // torre di addestramento con le finestre aperte e la ringhiera
            float tu0 = _w - 6.5f, tu1 = _w - 1.0f, tv0 = _d - 6.5f, tv1 = _d - 1.0f;
            float tg = Block(tu0, tv0, tu1, tv1, item.Height * 1.15f, 0x1C9C2B5, 12, 12, 12, 12, 1, 0x1A9A69E, false);
            for (int f = 1; f < 5; f++)
            {
                float z = tg + f * item.Height * 1.15f / 5;
                Poly(new[] { tu0 + 1.5f, tv0 - 0.03f, z - 1.6f, tu1 - 1.5f, tv0 - 0.03f, z - 1.6f, tu1 - 1.5f, tv0 - 0.03f, z, tu0 + 1.5f, tv0 - 0.03f, z }, 0, -1, 0, 0x1202224, BuildingDecorator.KindProp, 0);
            }
            // autopompe davanti ai portoni
            for (int k = 0; k < Math.Min(doors, 1 + _level / 2); k++)
                FireTruck(1.5f + (k + 0.5f) * dw - 0.2f, front - 3.6f, -(float)Math.PI / 2);
            PublicFlags(gu1 + 1.0f, 1.0f, 8f);
        }

        private void FireTruck(float cu, float cv, float angle)
        {
            float g = Ground(cu, cv);
            float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            OBox(cu, cv, angle, 3.2f, 1.15f, g, g + 0.6f, 0x1202020);
            OBox(cu - ca * 0.6f, cv - sa * 0.6f, angle, 2.6f, 1.25f, g + 0.5f, g + 2.9f, 0x1C62828);
            OBox(cu + ca * 2.45f, cv + sa * 2.45f, angle, 0.95f, 1.25f, g + 0.5f, g + 2.6f, 0x1C62828);
            OBox(cu + ca * 3.3f, cv + sa * 3.3f, angle, 0.08f, 1.1f, g + 1.5f, g + 2.4f, 0x126303A);
            OBox(cu - ca * 0.6f, cv - sa * 0.6f, angle, 2.62f, 1.27f, g + 1.2f, g + 1.4f, 0x1F2F2F2, 13, 0, false);
            OBox(cu - ca * 0.6f, cv - sa * 0.6f, angle, 2.4f, 0.35f, g + 2.9f, g + 3.1f, 0x1C0C4C8);
            OBox(cu + ca * 2.45f, cv + sa * 2.45f, angle, 0.2f, 0.8f, g + 2.6f, g + 2.78f, 0x12A6FD6, BuildingDecorator.KindLight, 1);
        }

        // ------------------------------------------------------------------ uffici pubblici e cultura

        /// <summary>Ufficio postale: palazzina con l'insegna gialla, la cassetta rossa e lo sportello automatico.</summary>
        private void PostOffice(CatalogItem item, uint wall, uint roof, int floors)
        {
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            float front = 2.4f;
            var b = Building(0.8f, front, _w - 0.8f, _d - 0.8f, item.Height, RoofShape.Hipped, wall != 0 ? wall : 0x1ECE2CCu, roof, floors, BuildingUse.Office, true);
            Dress(b, DressKind.Residential);
            float g = FloorZ(0), z1 = FloorZ(1);
            Sign(1.2f, front - 0.02f, _w - 1.2f, front - 0.02f, z1 - 0.9f, z1 - 0.15f, 0x1F5CB00);
            float mu = _w - 1.6f, mv = 0.8f, mg = Ground(mu, mv);
            Pipe(mu, mv, 0.05f, mg, mg + 1.0f, 0x1333333, 6);
            PaintedBox(mu - 0.25f, mv - 0.2f, mu + 0.25f, mv + 0.2f, mg + 1.0f, mg + 1.6f, 0x1C8202A);
            Cone(mu, mv, 0.3f, mg + 1.6f, 0.15f, 0x1C8202A, BuildingDecorator.KindProp, 8);
            PaintedBox(1.6f, front - 0.25f, 2.6f, front, g, g + 2.0f, 0x1F5CB00);
        }

        /// <summary>Biblioteca comunale: palazzo con il portico a colonne sul fronte, la balaustra e le bandiere.</summary>
        private void Library(CatalogItem item, uint wall, uint roof, int floors)
        {
            Paving(0, 0, _w, _d, 0x1D2C9B8);
            float front = 4.0f;
            uint col = wall != 0 ? wall : 0x1E6D8BCu;
            var b = Building(1.2f, front, _w - 1.2f, _d - 1.2f, item.Height, RoofShape.Hipped, col, roof, floors, BuildingUse.Civic);
            Dress(b, DressKind.Civic);
            float g = FloorZ(0), z1 = FloorZ(1);
            Colonnade(_w * 0.2f, _w * 0.8f, front - 3.0f, front, g, z1, 0x1F2EDE2, true);
            Steps(_w * 0.2f, 0.3f, _w * 0.8f, front - 3.0f, g, 0x1E0D8C8);
            PublicFlags(1.0f, 0.8f, 8f);
            for (int k = 0; k < 2; k++) Bench(1.5f + k * (_w - 5), front - 2.2f, 3.1f + k * (_w - 5), front - 1.7f);
        }

        /// <summary>Portico a colonne con la trabeazione (e la balaustra sopra) tra v0 e v1, dalla quota z0 alla z1.</summary>
        private void Colonnade(float u0, float u1, float v0, float v1, float z0, float z1, uint stone, bool balustrade)
        {
            int n = Math.Max(3, (int)((u1 - u0) / 3.2f));
            float r = Math.Min(0.45f, (z1 - z0) * 0.06f);
            for (int k = 0; k <= n; k++)
            {
                float u = u0 + 0.5f + (u1 - u0 - 1.0f) * k / n;
                PaintedBox(u - r * 1.3f, v0 + 0.5f - r * 1.3f, u + r * 1.3f, v0 + 0.5f + r * 1.3f, z0, z0 + 0.4f, stone);
                Cylinder(u, v0 + 0.5f, r, z0 + 0.4f, z1 - 0.9f, 12, stone, 1f, 0.9f, BuildingDecorator.KindProp, 0);
                PaintedBox(u - r * 1.25f, v0 + 0.5f - r * 1.25f, u + r * 1.25f, v0 + 0.5f + r * 1.25f, z1 - 0.9f, z1 - 0.7f, stone);
            }
            PaintedBox(u0, v0, u1, v1, z1 - 0.7f, z1, Lighten(stone, 0.04f));
            if (!balustrade) return;
            PaintedBox(u0, v0, u1, v0 + 0.25f, z1, z1 + 0.15f, stone);
            for (float u = u0 + 0.2f; u < u1 - 0.1f; u += 0.4f) Pipe(u, v0 + 0.12f, 0.08f, z1 + 0.15f, z1 + 0.8f, stone, 6);
            PaintedBox(u0, v0, u1, v0 + 0.25f, z1 + 0.8f, z1 + 0.95f, stone);
        }

        /// <summary>Scalinata che sale dal bordo v0 al piano v1 (quota z), gradini da 16 cm.</summary>
        private void Steps(float u0, float v0, float u1, float v1, float z, uint color)
        {
            float g = Ground((u0 + u1) / 2, v0);
            int n = Math.Max(1, (int)Math.Round((z - g) / 0.16f));
            if (z - g < 0.05f) n = 3;
            float h = Math.Max(0.16f, (z - g) / n);
            float zz = z - n * h;
            for (int k = 0; k < n; k++)
            {
                float va = v0 + (v1 - v0) * k / n;
                PaintedBox(u0, va, u1, v1, zz - 0.2f, zz + (k + 1) * h, k % 2 == 0 ? color : Darken(color, 0.04f));
            }
        }

        /// <summary>
        /// Museo neoclassico: corpo in travertino con le finestre alte, pronao con sei colonne, frontone e scalinata,
        /// stendardi delle mostre, piazza con i lampioni.
        /// </summary>
        private void Museum(CatalogItem item, uint wall)
        {
            uint stone = wall != 0 ? wall : 0x1E8DDC8u;
            Paving(0, 0, _w, _d, 0x1D8D0C0);
            float H = item.Height;
            float front = _d * 0.3f;
            var b = Building(2.0f, front, _w - 2.0f, _d - 1.5f, H * 0.82f, RoofShape.Hipped, stone, 0x1B8664A, 2, BuildingUse.Museum);
            Dress(b, DressKind.Civic);
            float g = FloorZ(0), zTop = _building.LastEave;
            // pronao: stilobate, colonne, trabeazione e frontone con la falda
            float pu0 = _w * 0.3f, pu1 = _w * 0.7f, pv0 = front - 5.2f;
            float zs = g + 1.1f;
            PaintedBox(pu0 - 0.6f, pv0 - 0.4f, pu1 + 0.6f, front, g - 0.3f, zs, Darken(stone, 0.03f));
            Steps(pu0 - 0.6f, pv0 - 2.6f, pu1 + 0.6f, pv0 - 0.4f, zs, Darken(stone, 0.05f));
            Colonnade(pu0, pu1, pv0, front, zs, zTop, stone, false);
            float zr = zTop + (pu1 - pu0) * 0.16f;
            float mid = (pu0 + pu1) / 2;
            Poly(new[] { pu0, pv0, zTop, pu1, pv0, zTop, mid, pv0, zr }, 0, -1, 0, stone, 0, 1, 12);
            Poly(new[] { pu0, pv0, zTop, mid, pv0, zr, mid, front, zr, pu0, front, zTop }, -0.4f, 0, 1, 0x1B8664A, 1, 1);
            Poly(new[] { pu1, pv0, zTop, pu1, front, zTop, mid, front, zr, mid, pv0, zr }, 0.4f, 0, 1, 0x1B8664A, 1, 1);
            Sign(mid - 3, pv0 - 0.01f, mid + 3, pv0 - 0.01f, zTop - 0.6f, zTop - 0.1f, 0x1C8B890, false);
            // stendardi delle mostre ai lati del pronao
            uint[] banners = { 0x1B3261E, 0x11F4E8C, 0x12E7D4F };
            for (int k = 0; k < 3; k++)
            {
                float a = 2.5f + k * 2.6f, c = _w - 2.5f - k * 2.6f;
                Poly(new[] { a, front - 0.1f, g + 3.5f, a + 1.2f, front - 0.1f, g + 3.5f, a + 1.2f, front - 0.1f, g + 8.5f, a, front - 0.1f, g + 8.5f }, 0, -1, 0, banners[k], BuildingDecorator.KindCanvas, 0);
                Poly(new[] { c - 1.2f, front - 0.1f, g + 3.5f, c, front - 0.1f, g + 3.5f, c, front - 0.1f, g + 8.5f, c - 1.2f, front - 0.1f, g + 8.5f }, 0, -1, 0, banners[(k + 1) % 3], BuildingDecorator.KindCanvas, 0);
            }
            foreach (float u in new[] { 3.0f, _w - 3.0f }) StreetLamp(u, 1.5f, 4.2f, true);
        }

        /// <summary>
        /// Teatro all'italiana: sala col tetto a padiglione, torre scenica più alta dietro, facciata con il portico
        /// ad arcate e la loggia, locandine accese e insegna.
        /// </summary>
        private void Theatre(CatalogItem item, uint wall, uint roof)
        {
            uint col = wall != 0 ? wall : 0x1E7C9A4u;
            Paving(0, 0, _w, _d, 0x1D2C9B8);
            float H = item.Height;
            float front = 4.5f;
            var hall = Building(2.0f, front, _w - 2.0f, _d - 1.5f, H * 0.7f, RoofShape.Hipped, col, roof, 3, BuildingUse.Theatre);
            Dress(hall, DressKind.Civic);
            float g = FloorZ(0);
            Building(_w * 0.24f, _d * 0.5f, _w * 0.76f, _d - 1.5f, H, RoofShape.Gabled, Darken(col, 0.04f), roof, 4, BuildingUse.Garage);
            // portico ad arcate e loggia sopra
            float z1 = g + 5.2f;
            Colonnade(_w * 0.18f, _w * 0.82f, front - 3.6f, front, g, z1, 0x1F0E6D6, true);
            Flat(_w * 0.18f, front - 3.6f, _w * 0.82f, front, z1 + 0.01f, 0x1C47A5A, 2);
            Sign(_w * 0.32f, front - 3.62f, _w * 0.68f, front - 3.62f, z1 - 0.65f, z1 - 0.05f, 0x17A1F2B);
            // locandine accese ai lati dell'ingresso
            for (int k = 0; k < 2; k++)
            {
                float a = 2.6f + k * 1.6f, c = _w - 2.6f - k * 1.6f;
                Poly(new[] { a, front - 0.05f, g + 0.8f, a + 1.1f, front - 0.05f, g + 0.8f, a + 1.1f, front - 0.05f, g + 2.4f, a, front - 0.05f, g + 2.4f }, 0, -1, 0, Pick(AwningColors, k), BuildingDecorator.KindLight, 0.6f);
                Poly(new[] { c - 1.1f, front - 0.05f, g + 0.8f, c, front - 0.05f, g + 0.8f, c, front - 0.05f, g + 2.4f, c - 1.1f, front - 0.05f, g + 2.4f }, 0, -1, 0, Pick(AwningColors, k + 2), BuildingDecorator.KindLight, 0.6f);
            }
            foreach (float u in new[] { 1.2f, _w - 1.2f }) StreetLamp(u, 1.2f, 4.0f, true);
        }

        // ------------------------------------------------------------------ chiese

        /// <summary>
        /// Chiesa: navata col tetto a capanna e la facciata a timpano (rosone, portale, lesene), abside semicircolare,
        /// sacrestia, campanile con la cella campanaria e la cuspide, sagrato con i gradini. La cappella ha il campanile a vela.
        /// </summary>
        private void Church(CatalogItem item, uint wall)
        {
            uint stone = wall != 0 ? wall : 0x1E3D3B4u;
            uint roofC = 0x1B8664A;
            bool small = item.Param("size", "") == "small" || _w < 14;
            Paving(0, 0, _w, _d, 0x1D2C9B8);
            float H = item.Height;
            if (small)
            {
                float nu0 = _w * 0.22f, nu1 = _w * 0.78f, nv0 = 1.4f, nv1 = _d - 0.6f;
                float hn = Math.Min(H * 0.6f, 7f);
                Building(nu0, nv0, nu1, nv1, hn, RoofShape.Gabled, stone, roofC, 1, BuildingUse.Chapel);
                float g = FloorZ(0), eave = _building.LastEave;
                Facade(nu0, nu1, nv0, g, eave, g + hn, stone, 0.7f);
                // campanile a vela sopra la facciata con la campana
                float mid = (nu0 + nu1) / 2, zt = g + hn;
                PaintedBox(mid - 1.0f, nv0, mid - 0.6f, nv0 + 0.5f, zt - 0.6f, g + H - 0.6f, stone);
                PaintedBox(mid + 0.6f, nv0, mid + 1.0f, nv0 + 0.5f, zt - 0.6f, g + H - 0.6f, stone);
                PaintedBox(mid - 1.1f, nv0 - 0.05f, mid + 1.1f, nv0 + 0.55f, g + H - 0.6f, g + H - 0.2f, stone);
                Cone(mid, nv0 + 0.25f, 0.18f, g + H - 0.2f, 0.4f, stone, BuildingDecorator.KindProp, 6);
                Cone(mid, nv0 + 0.25f, 0.42f, g + H - 1.7f, 0.9f, 0x1866A3A, BuildingDecorator.KindProp, 10);
                Steps(mid - 1.6f, 0.2f, mid + 1.6f, nv0, g, 0x1D6CBB4);
                return;
            }
            // navata (lungo V, facciata a sud), abside, sacrestia
            float u0 = 2.5f, u1 = u0 + Math.Min(10.5f, _w * 0.36f), v0 = 2.6f;
            float ar = (u1 - u0) * 0.34f;
            float v1 = _d - 0.8f - ar;
            float hNave = Math.Min(H * 0.55f, 17f);
            Building(u0, v0, u1, v1, hNave, RoofShape.Gabled, stone, roofC, 2, BuildingUse.Church);
            float gN = FloorZ(0), eaveN = _building.LastEave;
            Facade(u0, u1, v0, gN, eaveN, gN + hNave, stone, 1.0f);
            // abside: mezzo cilindro con il catino
            float am = (u0 + u1) / 2, ah = eaveN - gN - 1.2f;
            HalfCylinder(am, v1, ar, gN - 0.3f, gN + ah, stone);
            HalfDome(am, v1, ar, gN + ah, roofC);
            // sacrestia tra navata e campanile
            float tu0 = _w - 8.0f, tu1 = _w - 1.6f;
            Building(u1, v0 + 6.0f, tu0 + 0.4f, v1, Math.Min(7.5f, hNave * 0.5f), RoofShape.Hipped, stone, roofC, 1, BuildingUse.Church);
            // campanile
            float tv0 = v0 + 0.6f, tv1 = tv0 + (tu1 - tu0);
            float tg = Base(tu0, tv0, tu1, tv1);
            float belfry = H * 0.78f;
            Building(tu0, tv0, tu1, tv1, belfry - 2.4f, RoofShape.Flat, stone, roofC, 5, BuildingUse.Tower);
            float zb = tg + belfry - 2.4f;
            // cella campanaria: pilastri agli angoli con le bifore aperte, campane dentro
            float t = 0.9f;
            foreach (float u in new[] { tu0, tu1 - t })
                foreach (float v in new[] { tv0, tv1 - t })
                    PaintedBox(u, v, u + t, v + t, zb, zb + 3.6f, stone);
            PaintedBox(tu0 + t, tv0 + t, tu1 - t, tv1 - t, zb, zb + 0.2f, Darken(stone, 0.1f));
            Cone((tu0 + tu1) / 2, (tv0 + tv1) / 2, 0.9f, zb + 1.2f, 1.3f, 0x1866A3A, BuildingDecorator.KindProp, 10);
            PaintedBox(tu0 - 0.15f, tv0 - 0.15f, tu1 + 0.15f, tv1 + 0.15f, zb + 3.6f, zb + 4.1f, Lighten(stone, 0.05f));
            // cuspide a piramide
            float cz = zb + 4.1f;
            float cu = (tu0 + tu1) / 2, cv = (tv0 + tv1) / 2;
            float top = Math.Max(cz + 2.0f, tg + H);
            Poly(new[] { tu0, tv0, cz, tu1, tv0, cz, cu, cv, top }, 0, -1, 0.4f, roofC, 1, 1);
            Poly(new[] { tu1, tv0, cz, tu1, tv1, cz, cu, cv, top }, 1, 0, 0.4f, roofC, 1, 1);
            Poly(new[] { tu1, tv1, cz, tu0, tv1, cz, cu, cv, top }, 0, 1, 0.4f, roofC, 1, 1);
            Poly(new[] { tu0, tv1, cz, tu0, tv0, cz, cu, cv, top }, -1, 0, 0.4f, roofC, 1, 1);
            Beam(cu, cv, top, cu, cv, top + 1.4f, 0.05f, 0x1404040);
            Beam(cu - 0.4f, cv, top + 1.0f, cu + 0.4f, cv, top + 1.0f, 0.04f, 0x1404040);
            // sagrato con i gradini e due lampioni
            Steps(u0 - 0.5f, 0.3f, u1 + 0.5f, v0, gN, 0x1D6CBB4);
            StreetLamp(u0 - 1.4f, 1.2f, 4.0f, true);
            StreetLamp(u1 + 1.4f, 1.2f, 4.0f, true);
            Tree(PlantKind.Cypress, _w - 1.2f, _d - 1.4f, 51);
        }

        /// <summary>Facciata a timpano sul fronte della navata: lesene, cornice, rosone, portale con la lunetta.</summary>
        private void Facade(float u0, float u1, float v, float g, float eave, float ridge, uint stone, float scale)
        {
            float vf = v - 0.25f;
            uint light = Lighten(stone, 0.08f);
            // lesene ai lati e cornice orizzontale
            PaintedBox(u0 - 0.15f, vf, u0 + 0.7f * scale, v, g - 0.3f, eave, light);
            PaintedBox(u1 - 0.7f * scale, vf, u1 + 0.15f, v, g - 0.3f, eave, light);
            PaintedBox(u0 - 0.15f, vf - 0.1f, u1 + 0.15f, v, eave - 0.5f * scale, eave, light);
            // timpano che sporge un po' oltre il tetto, con la cornice inclinata
            float mid = (u0 + u1) / 2;
            Poly(new[] { u0 - 0.15f, vf, eave, u1 + 0.15f, vf, eave, mid, vf, ridge + 0.4f }, 0, -1, 0, stone, 0, 1, 12);
            Beam(u0 - 0.2f, vf - 0.05f, eave, mid, vf - 0.05f, ridge + 0.45f, 0.14f * scale, light);
            Beam(mid, vf - 0.05f, ridge + 0.45f, u1 + 0.2f, vf - 0.05f, eave, 0.14f * scale, light);
            Cone(mid, vf + 0.1f, 0.15f, ridge + 0.45f, 0.01f, light, BuildingDecorator.KindProp, 4);
            Beam(mid, vf, ridge + 0.4f, mid, vf, ridge + 1.6f * scale, 0.05f, 0x1404040);
            Beam(mid - 0.4f * scale, vf, ridge + 1.2f * scale, mid + 0.4f * scale, vf, ridge + 1.2f * scale, 0.05f, 0x1404040);
            // rosone: vetrata circolare scura con il bordo chiaro, si accende la sera
            float rz = g + (eave - g) * 0.72f, rr = Math.Min((u1 - u0) * 0.16f, (eave - g) * 0.16f);
            Disc(mid, vf - 0.02f, rz, rr * 1.25f, light, BuildingDecorator.KindProp, 0);
            Disc(mid, vf - 0.03f, rz, rr, 0x1C89B3C, BuildingDecorator.KindLight, 0.35f);
            // portale con la cornice e la lunetta
            float pw = Math.Min(2.6f, (u1 - u0) * 0.24f) * scale, ph = Math.Min(4.6f, (eave - g) * 0.36f);
            PaintedBox(mid - pw / 2 - 0.4f, vf - 0.2f, mid + pw / 2 + 0.4f, vf, g, g + ph + 0.4f, light);
            Poly(new[] { mid - pw / 2, vf - 0.21f, g, mid + pw / 2, vf - 0.21f, g, mid + pw / 2, vf - 0.21f, g + ph, mid - pw / 2, vf - 0.21f, g + ph }, 0, -1, 0, 0x14A3424, BuildingDecorator.KindProp, 0);
            Disc(mid, vf - 0.21f, g + ph + 0.05f, pw * 0.45f, 0x1A88A5A, BuildingDecorator.KindProp, 0, true);
        }

        /// <summary>Disco verticale rivolto verso -V (rosoni, orologi, lunette); <paramref name="half"/> = solo la metà superiore.</summary>
        private void Disc(float cu, float v, float cz, float r, uint color, float kind, float param, bool half = false)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, v);
            var n = new Vec3(-_v.X, -_v.Y, 0);
            const int seg = 20;
            int count = half ? seg / 2 : seg;
            for (int i = 0; i < count; i++)
            {
                double t0 = 2 * Math.PI * i / seg, t1 = 2 * Math.PI * (i + 1) / seg;
                var p0 = center + _u * ((float)Math.Cos(t0) * r);
                var p1 = center + _u * ((float)Math.Cos(t1) * r);
                var q0 = new Vec3(p0.X, p0.Y, cz + (float)Math.Sin(t0) * r);
                var q1 = new Vec3(p1.X, p1.Y, cz + (float)Math.Sin(t1) * r);
                var cc = new Vec3(center.X, center.Y, cz);
                QuadW(cc, q0, q1, q1, n, c, kind, param);
            }
        }

        /// <summary>Mezzo cilindro verso +V (abside): muro cieco in pietra.</summary>
        private void HalfCylinder(float cu, float cv, float r, float z0, float z1, uint color)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int seg = 10;
            for (int i = 0; i < seg; i++)
            {
                double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                var d0 = _u * (float)Math.Cos(a0) + _v * (float)Math.Sin(a0);
                var d1 = _u * (float)Math.Cos(a1) + _v * (float)Math.Sin(a1);
                var p0 = center + d0 * r;
                var p1 = center + d1 * r;
                var dm = d0 + d1;
                float len = (p1 - p0).Length;
                var n = new Vec3(dm.X, dm.Y, 0);
                QuadW(new Vec3(p0.X, p0.Y, z0), new Vec3(p1.X, p1.Y, z0), new Vec3(p1.X, p1.Y, z1), new Vec3(p0.X, p0.Y, z1), n, c, 0, 1, len, z1 - z0, 12);
            }
        }

        /// <summary>Catino dell'abside (quarto di sfera verso +V) in coppi.</summary>
        private void HalfDome(float cu, float cv, float r, float z0, uint color)
        {
            uint c = MeshBuffer.Rgb(color & 0xFFFFFF);
            var center = World(cu, cv);
            const int seg = 10, rings = 4;
            for (int k = 0; k < rings; k++)
            {
                double b0 = Math.PI / 2 * k / rings, b1 = Math.PI / 2 * (k + 1) / rings;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                    Vec3 P(double a, double b)
                    {
                        var d = _u * (float)(Math.Cos(a) * Math.Cos(b)) + _v * (float)(Math.Sin(a) * Math.Cos(b));
                        return new Vec3(center.X + d.X * r * 1.05f, center.Y + d.Y * r * 1.05f, z0 + (float)Math.Sin(b) * r * 0.75f);
                    }
                    var q0 = P(a0, b0);
                    var q1 = P(a1, b0);
                    var q2 = P(a1, b1);
                    var q3 = P(a0, b1);
                    var mid = P((a0 + a1) / 2, (b0 + b1) / 2);
                    var n = new Vec3(mid.X - center.X, mid.Y - center.Y, mid.Z - z0);
                    QuadW(q0, q1, q2, q3, n, c, 1, 1, (q1 - q0).Length, (q3 - q0).Length);
                }
            }
        }

        // ------------------------------------------------------------------ spettacolo

        /// <summary>
        /// Anfiteatro all'aperto: cavea semicircolare a gradoni in pietra rivolta verso il palco, scalette radiali,
        /// muro esterno, palco con la quinta, lampioni.
        /// </summary>
        private void Amphitheatre(CatalogItem item)
        {
            Lawn(0, 0, _w, _d);
            float g = Base(0, 0, _w, _d);
            float cu = _w / 2, cv = _d * 0.24f;
            float R = Math.Min(_w / 2 - 0.8f, _d - cv - 0.8f), r0 = R * 0.3f;
            float H = Math.Min(item.Height - 0.5f, R * 0.32f);
            uint stone = 0x1D9CFBE;
            // gradoni: anello da r0 (basso) a R (alto), mezzo cerchio verso +V
            var center = World(cu, cv);
            const int seg = 24, bands = 6;
            for (int j = 0; j < bands; j++)
            {
                float ra = r0 + (R - r0) * j / bands, rb = r0 + (R - r0) * (j + 1) / bands;
                float za = g + 0.6f + H * j / bands, zb = g + 0.6f + H * (j + 1) / bands;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                    var d0 = _u * (float)Math.Cos(a0) + _v * (float)Math.Sin(a0);
                    var d1 = _u * (float)Math.Cos(a1) + _v * (float)Math.Sin(a1);
                    var p0 = center + d0 * ra;
                    var p1 = center + d1 * ra;
                    var p2 = center + d1 * rb;
                    var p3 = center + d0 * rb;
                    uint col = MeshBuffer.Rgb(i % 6 == 3 ? 0xEDE6DAu : (stone & 0xFFFFFF));
                    QuadW(new Vec3(p0.X, p0.Y, za), new Vec3(p1.X, p1.Y, za), new Vec3(p2.X, p2.Y, zb), new Vec3(p3.X, p3.Y, zb),
                        new Vec3(-(d0.X + d1.X), -(d0.Y + d1.Y), 4), col, BuildingDecorator.KindSeats, BuildingMesher.SeatRowHeight);
                }
            }
            // muro esterno curvo e testate
            for (int i = 0; i < seg; i++)
            {
                double a0 = Math.PI * i / seg, a1 = Math.PI * (i + 1) / seg;
                var d0 = _u * (float)Math.Cos(a0) + _v * (float)Math.Sin(a0);
                var d1 = _u * (float)Math.Cos(a1) + _v * (float)Math.Sin(a1);
                var p0 = center + d0 * R;
                var p1 = center + d1 * R;
                var dm = d0 + d1;
                QuadW(new Vec3(p0.X, p0.Y, g - 0.3f), new Vec3(p1.X, p1.Y, g - 0.3f), new Vec3(p1.X, p1.Y, g + 0.6f + H), new Vec3(p0.X, p0.Y, g + 0.6f + H),
                    new Vec3(dm.X, dm.Y, 0), MeshBuffer.Rgb(0xCDBF9F), 0, 1, (p1 - p0).Length, H + 0.9f, 12);
            }
            foreach (int s in new[] { -1, 1 })
                Poly(new[] { cu + s * r0, cv, g - 0.3f, cu + s * R, cv, g - 0.3f, cu + s * R, cv, g + 0.6f + H, cu + s * r0, cv, g + 0.6f }, 0, -1, 0, 0x1CDBF9F, 0, 1, 12);
            // orchestra e palco con la quinta
            Flat(cu - r0, cv - 0.01f, cu + r0, cv + r0 * 0.9f, g + 0.12f, 0x1D5CBB8, 10);
            float sw = Math.Min(R * 0.9f, 16f);
            PaintedBox(cu - sw / 2, 1.2f, cu + sw / 2, cv - 0.2f, g - 0.2f, g + 1.1f, 0x18A6A4A);
            Flat(cu - sw / 2, 1.2f, cu + sw / 2, cv - 0.2f, g + 1.11f, 0x19C7350, BuildingDecorator.KindProp, 0);
            PaintedBox(cu - sw / 2, 0.4f, cu + sw / 2, 1.2f, g - 0.2f, g + 5.0f, stone);
            for (int k = -2; k <= 2; k++)
                Poly(new[] { cu + k * sw / 5 - 0.9f, 1.19f, g + 1.1f, cu + k * sw / 5 + 0.9f, 1.19f, g + 1.1f, cu + k * sw / 5 + 0.9f, 1.19f, g + 4.0f, cu + k * sw / 5 - 0.9f, 1.19f, g + 4.0f },
                    0, 1, 0, 0x14A3E32, BuildingDecorator.KindProp, 0);
            foreach (float u in new[] { cu - sw / 2 - 1.5f, cu + sw / 2 + 1.5f }) StreetLamp(u, 1.0f, 5.5f, false);
        }

        /// <summary>
        /// Luna park: ruota panoramica con i raggi e le cabine (accesa la sera), giostra dei cavalli con il tendone a
        /// strisce, torre di caduta, autoscontri, bancarelle, ingresso con l'insegna e festoni di luci.
        /// </summary>
        private void Funfair(CatalogItem item)
        {
            Paving(0, 0, _w, _d, 0x1C9B9A0);
            float H = item.Height;
            // ruota panoramica nel piano U-quota
            float r = Math.Min(Math.Min(_w, _d) * 0.3f, (H - 3f) / 2f), wu = _w * 0.25f, wv = _d * 0.58f;
            float g = Ground(wu, wv), zc = g + r + 2.2f;
            const int spokes = 16;
            for (int side = -1; side <= 1; side += 2)
            {
                float v = wv + side * 1.1f;
                for (int k = 0; k < spokes; k++)
                {
                    double a0 = 2 * Math.PI * k / spokes, a1 = 2 * Math.PI * (k + 1) / spokes;
                    float u0 = wu + (float)Math.Cos(a0) * r, z0 = zc + (float)Math.Sin(a0) * r;
                    float u1 = wu + (float)Math.Cos(a1) * r, z1 = zc + (float)Math.Sin(a1) * r;
                    Beam(u0, v, z0, u1, v, z1, 0.14f, 0x1F4F4F2, BuildingDecorator.KindLight, 0.5f);
                    Beam(wu, v, zc, u0, v, z0, 0.06f, 0x1E8E8E8);
                }
                // cavalletto
                Beam(wu - r * 0.5f, wv + side * 2.6f, g, wu, wv + side * 1.2f, zc, 0.22f, 0x1D9534A);
                Beam(wu + r * 0.5f, wv + side * 2.6f, g, wu, wv + side * 1.2f, zc, 0.22f, 0x1D9534A);
            }
            Beam(wu, wv - 1.3f, zc, wu, wv + 1.3f, zc, 0.35f, 0x1B0B4B8);
            uint[] cabins = { 0x1D9534A, 0x14F79B5, 0x1E8C030, 0x13E8E4A, 0x1E87ABE };
            for (int k = 0; k < spokes; k += 2)
            {
                double a = 2 * Math.PI * k / spokes;
                float cu = wu + (float)Math.Cos(a) * r, cz = zc + (float)Math.Sin(a) * r;
                Beam(cu, wv - 1.1f, cz, cu, wv + 1.1f, cz, 0.05f, 0x1B0B4B8);
                PaintedBox(cu - 0.7f, wv - 0.75f, cu + 0.7f, wv + 0.75f, cz - 1.9f, cz - 0.3f, cabins[(k / 2) % cabins.Length]);
                PaintedBox(cu - 0.75f, wv - 0.8f, cu + 0.75f, wv + 0.8f, cz - 0.3f, cz - 0.15f, 0x1F2F2F2);
            }
            // giostra dei cavalli
            float gu = _w * 0.68f, gv = _d * 0.32f, gg = Ground(gu, gv);
            float gr = Math.Min(6f, _d * 0.12f);
            Cylinder(gu, gv, gr, gg - 0.2f, gg + 0.6f, 20, 0x1E6D5B8, 1, 1, BuildingDecorator.KindProp, 0);
            Pipe(gu, gv, 0.9f, gg + 0.6f, gg + 4.6f, 0x1E8C030, 12);
            Cone(gu, gv, gr + 0.6f, gg + 4.4f, 2.6f, 0x1C8372D, BuildingDecorator.KindCanvas, 16, 1);
            Cylinder(gu, gv, gr + 0.6f, gg + 3.9f, gg + 4.4f, 20, 0x1E8C030, 1, 1, BuildingDecorator.KindLight, 0);
            for (int k = 0; k < 10; k++)
            {
                double a = 2 * Math.PI * k / 10;
                float hu = gu + (float)Math.Cos(a) * gr * 0.75f, hv = gv + (float)Math.Sin(a) * gr * 0.75f;
                Beam(hu, hv, gg + 0.6f, hu, hv, gg + 4.0f, 0.03f, 0x1D4B24C);
                OBox(hu, hv, (float)a + 1.57f, 0.55f, 0.16f, gg + 1.3f, gg + 1.85f, k % 2 == 0 ? 0x1F2F2F2u : 0x16B4A30u);
            }
            // torre di caduta
            float tu = _w * 0.87f, tv = _d * 0.78f, tg = Ground(tu, tv);
            Pipe(tu, tv, 1.0f, tg, g + H - 0.5f, 0x1B0B4B8, 12);
            Cylinder(tu, tv, 2.2f, tg + 6, tg + 7.2f, 16, 0x1D9534A, 1, 1, BuildingDecorator.KindProp, 0);
            Cylinder(tu, tv, 1.4f, g + H - 1.5f, g + H - 0.5f, 12, 0x1E8C030, 1, 1, BuildingDecorator.KindLight, 0.5f);
            // autoscontri
            float au0 = _w * 0.46f, au1 = au0 + 14, av0 = _d * 0.66f, av1 = av0 + 10;
            float ag = Base(au0, av0, au1, av1);
            Flat(au0, av0, au1, av1, ag + 0.2f, 0x1333740, BuildingDecorator.KindProp, 0);
            foreach (float u in new[] { au0 + 0.2f, au1 - 0.2f })
                foreach (float v in new[] { av0 + 0.2f, av1 - 0.2f })
                    Pipe(u, v, 0.15f, ag, ag + 4.2f, 0x1E8E8E8, 8);
            PaintedBox(au0 - 0.3f, av0 - 0.3f, au1 + 0.3f, av1 + 0.3f, ag + 4.2f, ag + 4.6f, 0x14F79B5);
            Poly(new[] { au0 - 0.3f, av0 - 0.32f, ag + 4.2f, au1 + 0.3f, av0 - 0.32f, ag + 4.2f, au1 + 0.3f, av0 - 0.32f, ag + 4.6f, au0 - 0.3f, av0 - 0.32f, ag + 4.6f },
                0, -1, 0, 0x1FFD27A, BuildingDecorator.KindLight, 0.8f);
            for (int k = 0; k < 6; k++)
                OBox(au0 + 2 + (k % 3) * 4.5f, av0 + 2.5f + (k / 3) * 5, k * 0.9f, 0.9f, 0.6f, ag + 0.2f, ag + 0.75f, cabins[k % cabins.Length]);
            // bancarelle lungo il viale d'ingresso
            for (int k = 0; k < 4; k++)
            {
                float su = _w * 0.42f + k * 4.2f, sv = 3.0f, sg = Ground(su, sv);
                PaintedBox(su - 1.6f, sv - 1.2f, su + 1.6f, sv + 1.2f, sg, sg + 2.4f, 0x1F2EDE2);
                Poly(new[] { su - 1.8f, sv - 2.0f, sg + 2.5f, su + 1.8f, sv - 2.0f, sg + 2.5f, su + 1.8f, sv + 1.2f, sg + 3.0f, su - 1.8f, sv + 1.2f, sg + 3.0f },
                    0, 0, 1, Pick(AwningColors, k), BuildingDecorator.KindCanvas, 1);
            }
            // ingresso con l'insegna accesa
            float eu = _w * 0.12f, eg = Ground(eu, 1.0f);
            PaintedBox(eu - 4.2f, 0.6f, eu - 3.4f, 1.4f, eg, eg + 5.5f, 0x1D9534A);
            PaintedBox(eu + 3.4f, 0.6f, eu + 4.2f, 1.4f, eg, eg + 5.5f, 0x1D9534A);
            PaintedBox(eu - 4.2f, 0.6f, eu + 4.2f, 1.4f, eg + 5.5f, eg + 6.8f, 0x14F79B5);
            Sign(eu - 3.8f, 0.58f, eu + 3.8f, 0.58f, eg + 5.6f, eg + 6.7f, 0x14F79B5);
            // festone di lampadine lungo il viale
            for (float u = 2; u < _w - 2; u += 0.9f)
            {
                float lg = Ground(u, 6.2f);
                float sag = 0.5f * (float)Math.Sin(Math.PI * ((u - 2) % 9) / 9);
                PaintedBox(u - 0.07f, 6.13f, u + 0.07f, 6.27f, lg + 4.2f - sag, lg + 4.35f - sag, 0x1FFE3A3, BuildingDecorator.KindLight, 1);
            }
            for (float u = 2; u < _w - 1; u += 9) Pipe(u, 6.2f, 0.08f, Ground(u, 6.2f), Ground(u, 6.2f) + 4.4f, 0x1404850, 6);
        }

        /// <summary>Discoteca: volume scuro rivestito, linee al neon sul bordo, insegna accesa, pensilina con il tappeto, parcheggio.</summary>
        private void Club(CatalogItem item, uint wall)
        {
            float H = item.Height;
            ParkingLot(0, 0, _w, 7.0f, 131, true);
            Paving(0, 7.0f, _w, 9.0f, 0x1BDB6A8);
            float g = Block(1.0f, 8.5f, _w - 1.0f, _d - 1.0f, H, wall != 0 ? wall : 0x12E2C36u, 12, 12, 12, 12, 1, 0x1A9A69E);
            foreach (var (c, z) in new[] { (0x1B23CFFu, H - 0.5f), (0x125D9E0u, H - 1.1f) })
            {
                Poly(new[] { 1.0f, 8.47f, g + z, _w - 1.0f, 8.47f, g + z, _w - 1.0f, 8.47f, g + z + 0.18f, 1.0f, 8.47f, g + z + 0.18f }, 0, -1, 0, c, BuildingDecorator.KindLight, 1);
                Poly(new[] { 0.97f, 8.5f, g + z, 0.97f, _d - 1.0f, g + z, 0.97f, _d - 1.0f, g + z + 0.18f, 0.97f, 8.5f, g + z + 0.18f }, -1, 0, 0, c, BuildingDecorator.KindLight, 1);
            }
            float mid = _w / 2;
            Sign(mid - 4, 8.46f, mid + 4, 8.46f, g + 3.6f, g + 5.4f, 0x1B23CFF);
            PaintedBox(mid - 2.5f, 6.6f, mid + 2.5f, 8.5f, g + 2.8f, g + 3.1f, 0x1202024);
            Poly(new[] { mid - 1.0f, 8.46f, g, mid + 1.0f, 8.46f, g, mid + 1.0f, 8.46f, g + 2.6f, mid - 1.0f, 8.46f, g + 2.6f }, 0, -1, 0, 0x1141418, BuildingDecorator.KindProp, 0);
            Flat(mid - 1.0f, 6.6f, mid + 1.0f, 8.5f, g + 0.12f, 0x18E1D2C, BuildingDecorator.KindProp, 0);
            foreach (float u in new[] { mid - 1.4f, mid + 1.4f })
                for (int k = 0; k < 3; k++) Pipe(u, 6.8f + k * 0.6f, 0.04f, g, g + 1.0f, 0x1D4B24C, 6);
        }
    }
}
