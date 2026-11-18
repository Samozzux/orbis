using System;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Meshing;
using Orbis.Core.World.Tiles;

namespace Orbis.Core.Game
{
    /// <summary>Sport, trasporti, arredo urbano e natura.</summary>
    public sealed partial class ObjectMesher
    {
        // ------------------------------------------------------------------ campi sportivi

        private void Pitch(string sport)
        {
            float g = Base(0, 0, _w, _d);
            switch (sport)
            {
                case "tennis": TennisCourt(g); break;
                case "basket": BasketCourt(g); break;
                case "skate": SkatePark(g); break;
                case "football5": FiveASide(g); break;
                default: FootballPitch(g); break;
            }
        }

        /// <summary>Torre faro con i proiettori rivolti verso il punto (tu, tv): si accendono la sera.</summary>
        private void Floodlight(float u, float v, float h, float tu, float tv, float headW)
        {
            float g = Ground(u, v);
            Pipe(u, v, 0.16f, g - 0.2f, g + h - 0.8f, 0x1A9AEB4, 8, 0.6f);
            float du = tu - u, dv = tv - v, l = (float)Math.Sqrt(du * du + dv * dv);
            du /= l;
            dv /= l;
            float su = -dv * headW / 2, sv = du * headW / 2;
            float pu = u + du * 0.3f, pv = v + dv * 0.3f;
            Beam(pu - su, pv - sv, g + h - 0.9f, pu + su, pv + sv, g + h - 0.9f, 0.06f, 0x14A4F55);
            Poly(new[] { pu - su, pv - sv, g + h - 0.8f, pu + su, pv + sv, g + h - 0.8f, pu + su, pv + sv, g + h, pu - su, pv - sv, g + h },
                du, dv, 0, 0x1FFF2D2, BuildingDecorator.KindLight, 1f);
            Poly(new[] { pu - su, pv - sv, g + h - 0.8f, pu + su, pv + sv, g + h - 0.8f, pu + su, pv + sv, g + h, pu - su, pv - sv, g + h },
                -du, -dv, 0, 0x14A4F55, BuildingDecorator.KindProp, 0);
        }

        /// <summary>Porta da calcio sulla linea di fondo u = uLine, rete verso <paramref name="back"/> (±1 lungo U).</summary>
        private void Goal(float uLine, float cv, float halfW, float h, int back, float g)
        {
            uint white = 0x1F4F4F2, net = 0x1DADCDC;
            float depth = Math.Min(Math.Min(2.0f, h * 0.8f), back < 0 ? uLine - 0.15f : _w - uLine - 0.15f) * back;
            Beam(uLine, cv - halfW, g, uLine, cv - halfW, g + h, 0.06f, white);
            Beam(uLine, cv + halfW, g, uLine, cv + halfW, g + h, 0.06f, white);
            Beam(uLine, cv - halfW, g + h, uLine, cv + halfW, g + h, 0.06f, white);
            Panel(uLine + depth, cv - halfW, uLine + depth, cv + halfW, g, g + h * 0.6f, net);
            Poly(new[] { uLine, cv - halfW, g + h, uLine, cv + halfW, g + h, uLine + depth, cv + halfW, g + h * 0.6f, uLine + depth, cv - halfW, g + h * 0.6f }, back * 0.5f, 0, 1, net, BuildingDecorator.KindProp, 0);
            Poly(new[] { uLine, cv - halfW, g, uLine + depth, cv - halfW, g, uLine + depth, cv - halfW, g + h * 0.6f, uLine, cv - halfW, g + h }, 0, -1, 0, net, BuildingDecorator.KindProp, 0);
            Poly(new[] { uLine, cv + halfW, g, uLine + depth, cv + halfW, g, uLine + depth, cv + halfW, g + h * 0.6f, uLine, cv + halfW, g + h }, 0, 1, 0, net, BuildingDecorator.KindProp, 0);
        }

        /// <summary>Campo da calcio regolamentare: linee, porte con le reti, panchine, reti parapalloni dietro le porte, torri faro.</summary>
        private void FootballPitch(float g)
        {
            Flat(0, 0, _w, _d, g + 0.12f, 0x15FA24C, 8, 1);
            float cv = _d / 2;
            Goal(3f, cv, 3.66f, 2.44f, -1, g + 0.12f);
            Goal(_w - 3f, cv, 3.66f, 2.44f, 1, g + 0.12f);
            // panchine coperte a bordo campo
            foreach (int s in new[] { -1, 1 })
            {
                float cu = _w / 2 + s * 9f;
                PaintedBox(cu - 3.5f, 0.3f, cu + 3.5f, 0.5f, g, g + 2.0f, 0x1D8DCDF);
                Poly(new[] { cu - 3.5f, 0.3f, g + 2.0f, cu + 3.5f, 0.3f, g + 2.0f, cu + 3.5f, 2.1f, g + 1.8f, cu - 3.5f, 2.1f, g + 1.8f }, 0, 0, 1, 0x1A9C4CC, BuildingDecorator.KindProp, 0);
                PaintedBox(cu - 3.3f, 0.5f, cu + 3.3f, 1.0f, g, g + 0.45f, 0x11F4E8C);
            }
            // reti parapalloni dietro le porte
            foreach (float u in new[] { 0.4f, _w - 0.4f })
            {
                foreach (float v in new[] { cv - 12, cv, cv + 12 }) Beam(u, v, g, u, v, g + 6.0f, 0.06f, 0x1505A60);
                Panel(u, cv - 12, u, cv + 12, g + 0.2f, g + 6.0f, 0x13E4A44);
            }
            foreach (float u in new[] { 2.4f, _w - 2.4f })
                foreach (float v in new[] { 2.4f, _d - 2.4f })
                    Floodlight(u, v, 15f, _w / 2, cv, 3.2f);
        }

        /// <summary>Calcetto in erba sintetica: recinzione alta con la rete, porte piccole, quattro pali delle luci.</summary>
        private void FiveASide(float g)
        {
            Flat(0, 0, _w, _d, g + 0.1f, 0x157A044, 8, 5);
            float cv = _d / 2;
            Goal(1.0f, cv, 1.5f, 2.0f, -1, g + 0.1f);
            Goal(_w - 1.0f, cv, 1.5f, 2.0f, 1, g + 0.1f);
            LotFence(4.0f, 0, 0x1384A40, true, 0.2f, 1.0f);
            foreach (float u in new[] { 1.0f, _w - 1.0f })
                foreach (float v in new[] { 1.0f, _d - 1.0f })
                    Floodlight(u, v, 8f, _w / 2, cv, 1.6f);
        }

        /// <summary>Tennis in terra rossa: linee, rete con i paletti, recinzione alta verde, sedia dell'arbitro, panchine, luci.</summary>
        private void TennisCourt(float g)
        {
            Flat(0, 0, _w, _d, g + 0.1f, 0x1C0663F, 8, 2);
            float cu = _w / 2, cv = _d / 2, z = g + 0.1f;
            bool alongU = _w >= _d;
            if (alongU)
            {
                foreach (int s in new[] { -1, 1 }) Pipe(cu, cv + s * 6.4f, 0.05f, z, z + 1.07f, 0x1303438, 8);
                Panel(cu, cv - 6.4f, cu, cv + 6.4f, z + 0.12f, z + 0.95f, 0x1202224);
                Beam(cu, cv - 6.4f, z + 0.98f, cu, cv + 6.4f, z + 0.98f, 0.04f, 0x1F4F4F2);
                // sedia dell'arbitro
                float au = cu, av = cv - 7.3f;
                foreach (float du in new[] { -0.3f, 0.3f }) Beam(au + du, av, z, au + du, av, z + 1.8f, 0.04f, 0x1E8E8E8);
                PaintedBox(au - 0.35f, av - 0.3f, au + 0.35f, av + 0.3f, z + 1.8f, z + 1.9f, 0x1E8E8E8);
                for (int k = 0; k < 2; k++) Bench(cu - 5 + k * 8.4f, 0.6f, cu - 3.4f + k * 8.4f, 1.1f);
            }
            LotFence(3.6f, 0, 0x1263F2E, true, 0.2f, 2.0f);
            foreach (float u in new[] { _w * 0.25f, _w * 0.75f })
                foreach (float v in new[] { 0.4f, _d - 0.4f })
                    Floodlight(u, v, 7f, u, cv, 1.2f);
        }

        /// <summary>Basket all'aperto: fondo in resina, linee, due canestri, recinzione, panchine, luci.</summary>
        private void BasketCourt(float g)
        {
            Flat(0, 0, _w, _d, g + 0.1f, 0x13E6E8E, 8, 3);
            Hoops(0, 0, _w, _d, g + 0.1f);
            LotFence(2.4f, 3.0f, 0x1384A40, false);
            for (int k = 0; k < 2; k++) Bench(_w / 2 - 6 + k * 10.4f, _d - 1.2f, _w / 2 - 4.4f + k * 10.4f, _d - 0.7f);
            foreach (float u in new[] { 0.5f, _w - 0.5f }) Floodlight(u, _d / 2, 7f, _w / 2, _d / 2, 1.4f);
        }

        /// <summary>Skate park in cemento: due quarter pipe con il coping, funbox con le rampe, rail, gradini col corrimano, graffiti.</summary>
        private void SkatePark(float g)
        {
            Flat(0, 0, _w, _d, g + 0.1f, 0x1B4B2AC, 8, 4);
            float z = g + 0.1f, R = Math.Min(2.2f, _w * 0.08f);
            uint conc = 0x1C2C0BA;
            foreach (int side in new[] { -1, 1 })
            {
                float edge = side < 0 ? 0.6f : _w - 0.6f;
                const int seg = 6;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = Math.PI / 2 * i / seg, a1 = Math.PI / 2 * (i + 1) / seg;
                    float u0 = edge - side * (R - R * (float)Math.Sin(a0)), z0 = z + R - R * (float)Math.Cos(a0);
                    float u1 = edge - side * (R - R * (float)Math.Sin(a1)), z1 = z + R - R * (float)Math.Cos(a1);
                    double am = (a0 + a1) / 2;
                    Poly(new[] { u0, 2.0f, z0, u0, _d - 2.0f, z0, u1, _d - 2.0f, z1, u1, 2.0f, z1 }, -side * (float)Math.Sin(am), 0, (float)Math.Cos(am), conc, BuildingDecorator.KindProp, 0);
                }
                float du0 = Math.Min(edge, edge + side * 0.6f), du1 = Math.Max(edge, edge + side * 0.6f);
                PaintedBox(du0, 2.0f, du1, _d - 2.0f, g, z + R, Darken(conc, 0.05f));
                Beam(edge, 2.0f, z + R + 0.03f, edge, _d - 2.0f, z + R + 0.03f, 0.04f, 0x1909498);
                Poly(new[] { edge, 2.0f, g, edge, 2.0f, z + R, edge - side * R, 2.0f, z, edge - side * R, 2.0f, g }, 0, -1, 0, Darken(conc, 0.08f), BuildingDecorator.KindProp, 0);
                Poly(new[] { edge, _d - 2.0f, g, edge, _d - 2.0f, z + R, edge - side * R, _d - 2.0f, z, edge - side * R, _d - 2.0f, g }, 0, 1, 0, Darken(conc, 0.08f), BuildingDecorator.KindProp, 0);
                // graffiti sul retro
                uint[] spray = { 0x1E8466A, 0x13FA7D6, 0x1F2C230, 0x16BCB77 };
                for (int k = 0; k < 3; k++)
                {
                    float va = 3 + k * (_d - 6) / 3, vb = va + (_d - 6) / 3 - 0.6f;
                    float uu = side < 0 ? 0.59f : _w - 0.59f;
                    Poly(new[] { uu, va, g + 0.3f, uu, vb, g + 0.3f, uu, vb, z + R - 0.3f, uu, va, z + R - 0.3f }, side, 0, 0, spray[(k + (side < 0 ? 0 : 2)) % 4], BuildingDecorator.KindProp, 0);
                }
            }
            // funbox al centro con due rampe
            float cu = _w / 2, cv = _d / 2, fh = 0.8f;
            PaintedBox(cu - 2.0f, cv - 1.6f, cu + 2.0f, cv + 1.6f, g, z + fh, conc);
            foreach (int s in new[] { -1, 1 })
                Poly(new[] { cu + s * 2.0f, cv - 1.6f, z + fh, cu + s * 2.0f, cv + 1.6f, z + fh, cu + s * 4.2f, cv + 1.6f, z, cu + s * 4.2f, cv - 1.6f, z }, s, 0, 1, conc, BuildingDecorator.KindProp, 0);
            // rail e gradini col corrimano
            float rv = _d * 0.82f;
            foreach (float u in new[] { cu - 4, cu + 4 }) Beam(u, rv, z, u, rv, z + 0.4f, 0.04f, 0x1505458);
            Beam(cu - 4.5f, rv, z + 0.42f, cu + 4.5f, rv, z + 0.42f, 0.03f, 0x1909498);
            for (int k = 0; k < 3; k++) PaintedBox(cu - 3.0f, 1.0f + k * 0.4f, cu + 3.0f, 2.2f, g, z + 0.6f - k * 0.2f, conc);
            Beam(cu, 0.9f, z + 1.3f, cu, 2.2f, z + 0.9f, 0.03f, 0x1909498);
            LotFence(1.2f, 3f, 0x1404850, false);
        }

        /// <summary>
        /// Piscina scoperta: vasca da 25 m con le corsie, i galleggianti e i blocchi di partenza, vasca per i bambini,
        /// solarium con i lettini e gli ombrelloni, spogliatoi, torretta del bagnino, siepe e palme.
        /// </summary>
        private void Pool()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.14f, 0x1E8E0D0, 10);
            float pu0 = 2.5f, pu1 = Math.Min(_w * 0.58f, pu0 + 25f), pv0 = 2.5f, pv1 = Math.Min(_d * 0.5f, pv0 + 12.5f);
            float wz = g + 0.18f;
            Flat(pu0, pv0, pu1, pv1, wz, 0x146A8D8, 11);
            int lanes = Math.Max(3, (int)((pv1 - pv0) / 2.5f));
            float lw = (pv1 - pv0) / lanes;
            for (int k = 0; k <= lanes; k++)
            {
                float v = pv0 + k * lw;
                if (k > 0 && k < lanes)
                    for (float u = pu0 + 0.2f; u < pu1 - 0.2f; u += 1.0f)
                        PaintedBox(u, v - 0.05f, u + 0.5f, v + 0.05f, wz, wz + 0.08f, ((int)(u - pu0) / 5) % 2 == 0 ? 0x1D9343Au : 0x1F2F2F2u);
                if (k < lanes) Flat(pu0 + 2, v + lw / 2 - 0.12f, pu1 - 2, v + lw / 2 + 0.12f, wz + 0.005f, 0x11E4E8C, BuildingDecorator.KindProp, 0);
                if (k < lanes) PaintedBox(pu0 - 0.6f, v + lw / 2 - 0.25f, pu0 - 0.1f, v + lw / 2 + 0.25f, g + 0.14f, g + 0.85f, 0x1F4F4F2);
            }
            // scalette
            foreach (float v in new[] { pv0 + 0.6f, pv1 - 0.6f })
            {
                Beam(pu1 - 0.2f, v - 0.3f, wz - 0.4f, pu1 + 0.1f, v - 0.3f, g + 1.0f, 0.025f, 0x1C8CCD0);
                Beam(pu1 - 0.2f, v + 0.3f, wz - 0.4f, pu1 + 0.1f, v + 0.3f, g + 1.0f, 0.025f, 0x1C8CCD0);
            }
            // vasca dei bambini
            float ku0 = pu1 + 2.5f, ku1 = Math.Min(_w - 2, ku0 + 8), kv0 = pv0, kv1 = pv0 + 6;
            if (ku1 - ku0 > 3) Flat(ku0, kv0, ku1, kv1, wz + 0.02f, 0x166C2E0, 11);
            // solarium
            for (float u = pu0; u + 1 < pu1; u += 2.6f)
            {
                PaintedBox(u, pv1 + 2.2f, u + 0.7f, pv1 + 4.1f, g + 0.14f, g + 0.45f, 0x1F4F4F2);
                if (((int)((u - pu0) / 2.6f)) % 2 == 0)
                {
                    float uu = u + 1.65f, vv = pv1 + 3.1f;
                    Pipe(uu, vv, 0.03f, g + 0.14f, g + 2.3f, 0x1E8E8E8, 6);
                    Cone(uu, vv, 1.2f, g + 2.05f, 0.4f, Pick(AwningColors, (long)u), BuildingDecorator.KindCanvas, 8, 1);
                }
            }
            // spogliatoi con la fascia blu e l'insegna
            float su0 = _w * 0.62f, su1 = _w - 1.0f, sv0 = _d * 0.64f, sv1 = _d - 1.0f;
            float sg = Block(su0, sv0, su1, sv1, 3.4f, 0x1F2F0EA, 12, 12, 12, 12, 1, 0x1A9A69E);
            Poly(new[] { su0, sv0 - 0.03f, sg + 2.6f, su1, sv0 - 0.03f, sg + 2.6f, su1, sv0 - 0.03f, sg + 3.4f, su0, sv0 - 0.03f, sg + 3.4f }, 0, -1, 0, 0x11F5FA8, BuildingDecorator.KindProp, 0);
            Glazing(su0 + 1, sv0, su0 + 4, sv0, sg, sg + 2.4f, 1);
            // torretta del bagnino
            float lu = (pu0 + pu1) / 2, lv = pv1 + 1.0f;
            foreach (float du in new[] { -0.35f, 0.35f }) Beam(lu + du, lv, g + 0.14f, lu + du, lv, g + 1.9f, 0.04f, 0x1F4F4F2);
            PaintedBox(lu - 0.45f, lv - 0.35f, lu + 0.45f, lv + 0.35f, g + 1.9f, g + 2.0f, 0x1F4F4F2);
            Cone(lu, lv, 0.9f, g + 2.9f, 0.35f, 0x1D9343A, BuildingDecorator.KindCanvas, 8, 0);
            Pipe(lu, lv, 0.02f, g + 2.0f, g + 2.9f, 0x1E8E8E8, 6);
            // siepe di confine e palme
            HedgeRow(0.05f, 0.05f, _w - 0.05f, 0.6f, 1.6f);
            HedgeRow(0.05f, 0.6f, 0.6f, _d - 0.05f, 1.6f);
            Tree(PlantKind.Palm, _w - 2.0f, 2.2f, 71);
            Tree(PlantKind.Palm, 1.6f, _d - 1.6f, 72);
        }

        // ------------------------------------------------------------------ impianti al coperto

        private void Hall(CatalogItem item, uint wall)
        {
            switch (item.Param("kind", "gym"))
            {
                case "club": Club(item, wall); return;
                case "arena": Arena(item, wall); return;
                default: Gym(item, wall); return;
            }
        }

        /// <summary>Palestra: capannone a volta con le finestre alte a nastro, ingresso vetrato con la pensilina, insegna, rastrelliera.</summary>
        private void Gym(CatalogItem item, uint wall)
        {
            Paving(0, 0, _w, _d, 0x1CFC6B4);
            float front = 3.0f;
            var b = Building(1.0f, front, _w - 1.0f, _d - 1.0f, item.Height, RoofShape.Round, wall != 0 ? wall : 0x1E3E1DCu, 0x1A9ADB0, 2, BuildingUse.Sports);
            Dress(b, DressKind.Modern);
            float g = FloorZ(0), mid = _w / 2;
            Glazing(mid - 3, front, mid + 3, front, g, g + 3.0f, 1, 0x1303438);
            PaintedBox(mid - 3.6f, front - 2.0f, mid + 3.6f, front, g + 3.2f, g + 3.45f, 0x1E8E8E6);
            Sign(mid - 3.2f, front - 0.03f, mid + 3.2f, front - 0.03f, g + 3.7f, g + 4.6f, 0x1E8A317);
            BikeRackAt(1.2f, 0.4f, 5f);
        }

        /// <summary>
        /// Palazzetto dello sport: tamburo ellittico (zoccolo in cemento, fascia vetrata, coronamento) con la copertura a
        /// cupola ribassata e i costoloni, ingresso vetrato con l'insegna, bandiere, parcheggio.
        /// </summary>
        private void Arena(CatalogItem item, uint wall)
        {
            float H = item.Height;
            ParkingLot(0, 0, _w, 7.0f, 191, true);
            Paving(0, 7.0f, _w, _d, 0x1D2C9B8);
            float cu = _w / 2, cv = _d / 2 + 3.0f, ru = _w / 2 - 2.5f, rv = _d / 2 - 6.5f;
            float g = Base(cu - ru, cv - rv, cu + ru, cv + rv);
            const int seg = 40;
            float zr = g + H * 0.62f;
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                float u0 = cu + (float)Math.Cos(a0) * ru, v0 = cv + (float)Math.Sin(a0) * rv;
                float u1 = cu + (float)Math.Cos(a1) * ru, v1 = cv + (float)Math.Sin(a1) * rv;
                Wall(u0, v0, u1, v1, g - 0.3f, g + 3.0f, g, 0x1C9C4BA, 12, 1);
                Wall(u0, v0, u1, v1, g + 3.0f, zr - 1.4f, g, 0x1303A44, 10, 2);
                Wall(u0, v0, u1, v1, zr - 1.4f, zr, g, wall != 0 ? wall : 0x1EDEBE6u, 12, 1);
            }
            // cupola ribassata con i costoloni
            const int rings = 6;
            uint roofC = MeshBuffer.Rgb(0xC9CED2);
            for (int k = 0; k < rings; k++)
            {
                double b0 = Math.PI / 2 * k / rings, b1 = Math.PI / 2 * (k + 1) / rings;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    Vec3 P(double a, double b)
                    {
                        var w = World(cu + (float)(Math.Cos(a) * Math.Cos(b)) * (ru + 0.3f), cv + (float)(Math.Sin(a) * Math.Cos(b)) * (rv + 0.3f));
                        return new Vec3(w.X, w.Y, zr + (float)Math.Sin(b) * (g + H - zr));
                    }
                    var q0 = P(a0, b0);
                    var q1 = P(a1, b0);
                    var q2 = P(a1, b1);
                    var q3 = P(a0, b1);
                    var c0 = World(cu, cv);
                    var mid = P((a0 + a1) / 2, (b0 + b1) / 2);
                    QuadW(q0, q1, q2, q3, new Vec3(mid.X - c0.X, mid.Y - c0.Y, (mid.Z - zr) * 3 + 0.5f), (i % 5 == 0) ? MeshBuffer.Rgb(0xA9B0B6) : roofC, BuildingDecorator.KindProp, 0);
                }
            }
            // ingresso vetrato, insegna, bandiere
            float ev = cv - rv;
            Block(cu - 9, ev - 2.5f, cu + 9, ev + 3.0f, 6.0f, 0x1303438, 10, 10, 10, 10, 2, 0x1D8DCDF);
            Sign(cu - 8, ev - 2.55f, cu + 8, ev - 2.55f, g + 6.3f, g + 7.6f, 0x11F4E8C);
            PublicFlags(cu - 15, ev - 3.0f, 9f);
            FlagPole(cu + 13, ev - 3.0f, 9f, 1.5f, 0x1005B9A);
        }

        /// <summary>
        /// Pista d'atletica: anello in tartan a otto corsie come le piste vere, campo da calcio nel prato interno con le
        /// porte, torri faro agli angoli.
        /// </summary>
        private void Track()
        {
            Lawn(0, 0, _w, _d);
            float g = Base(0, 0, _w, _d);
            var p0 = World(0.3f, 0.3f);
            var p1 = World(_w - 0.3f, 0.3f);
            var p2 = World(_w - 0.3f, _d - 0.3f);
            var p3 = World(0.3f, _d - 0.3f);
            var outer = new[] { p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y };
            if (Polygon2D.SignedArea(outer) < 0) outer = new[] { p0.X, p0.Y, p3.X, p3.Y, p2.X, p2.Y, p1.X, p1.Y };
            var area = new AreaInfo { Kind = AreaKind.Track, Outer = outer, Holes = Array.Empty<float[]>(), Bounds = Bounds2.Of(outer) };
            LandmarkMesher.AthleticsTrack(area, _ground, _mb);
            // campo nel prato interno, dentro le curve
            bool alongU = _w >= _d;
            float L = alongU ? _w : _d, W = alongU ? _d : _w;
            float r = W / 2 - 0.3f, ri = r - Math.Min(9.76f, r * 0.3f);
            float straight = L / 2 - 0.3f - r;
            float hw = Math.Min(37f, ri * 0.8f);
            float hl = Math.Min(55.5f, straight + (float)Math.Sqrt(Math.Max(0, ri * ri - hw * hw)) - 1.0f);
            float cu = _w / 2, cv = _d / 2;
            if (hl > 10 && hw > 6)
            {
                if (alongU)
                {
                    Flat(cu - hl, cv - hw, cu + hl, cv + hw, g + 0.24f, 0x15FA24C, 8, 1);
                    Goal(cu - hl + 3, cv, 3.66f, 2.44f, -1, g + 0.24f);
                    Goal(cu + hl - 3, cv, 3.66f, 2.44f, 1, g + 0.24f);
                }
                else Flat(cu - hw, cv - hl, cu + hw, cv + hl, g + 0.24f, 0x15FA24C, 8, 1);
            }
            foreach (float u in new[] { 1.5f, _w - 1.5f })
                foreach (float v in new[] { 1.5f, _d - 1.5f })
                    Floodlight(u, v, 13f, cu, cv, 3.0f);
        }

        /// <summary>
        /// Stadio vero e proprio: campo regolamentare con le linee, quattro tribune a gradinata con i sedili colorati,
        /// copertura a sbalzo sulla tribuna principale (sulle altre ai livelli successivi), torri faro che la sera si
        /// accendono, cartelloni a bordo campo, panchine e tabellone.
        /// </summary>
        private void Stadium(CatalogItem item, float lv)
        {
            float H = item.Height;
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.06f, 0x1A9A69E, 10);
            // campo (105×68 m se c'è posto) con 3 m di bordo; il tipo 8 disegna strisce e linee
            float fl = Math.Min(111f, _w - 46f), fw = Math.Min(74f, _d - 44f);
            float fu0 = (_w - fl) / 2, fv0 = (_d - fw) / 2, fu1 = fu0 + fl, fv1 = fv0 + fw;
            Flat(fu0, fv0, fu1, fv1, g + 0.15f, 0x15FA24C, 8);
            Goal(fu0 + 3f, _d / 2, 3.66f, 2.44f, -1, g + 0.15f);
            Goal(fu1 - 3f, _d / 2, 3.66f, 2.44f, 1, g + 0.15f);
            // colori della squadra: sedili delle tribune lunghe e delle curve
            uint[] palette = { 0x2F6DB5, 0xB8433A, 0x3E8E4A, 0xE0A92E, 0x6B3FA0 };
            int pick = (int)(Frac(11) * palette.Length) % palette.Length;
            uint main = palette[pick], ends = palette[(pick + 2) % palette.Length];
            float zMain = Math.Min(H * 0.42f, 14f), zEnds = zMain * 0.7f;
            // tribune: principale a sud (v basse), distinti a nord, curve a ovest e a est
            float gap = 3f;
            SeatingStand(fu0 - 2, 2, fu1 + 2, fv0 - gap, 0, g, g + 1.3f, g + zMain, main);
            SeatingStand(fu0 - 2, fv1 + gap, fu1 + 2, _d - 2, 1, g, g + 1.3f, g + zMain * 0.85f, main);
            SeatingStand(2, fv0, fu0 - gap, fv1, 2, g, g + 1.3f, g + zEnds, ends);
            SeatingStand(fu1 + gap, fv0, _w - 2, fv1, 3, g, g + 1.3f, g + zEnds, ends);
            // coperture a sbalzo: principale sempre, distinti dal livello 2, curve al livello 3
            StandRoof(fu0 - 2, 2, fu1 + 2, fv0 - gap, 0, g, g + zMain + 4.5f);
            if (lv >= 1) StandRoof(fu0 - 2, fv1 + gap, fu1 + 2, _d - 2, 1, g, g + zMain * 0.85f + 4.5f);
            if (lv >= 2)
            {
                StandRoof(2, fv0, fu0 - gap, fv1, 2, g, g + zEnds + 4.5f);
                StandRoof(fu1 + gap, fv0, _w - 2, fv1, 3, g, g + zEnds + 4.5f);
            }
            // cartelloni pubblicitari attorno al campo e panchine davanti alla tribuna principale
            uint[] ads = { 0x1D9534A, 0x1F2F2F2, 0x12F6DB5, 0x1F2C230, 0x13E8E4A };
            for (float u = fu0 + 6; u + 7 < fu1 - 6; u += 8)
            {
                PaintedBox(u, fv0 - 1.4f, u + 7.6f, fv0 - 1.2f, g + 0.15f, g + 1.1f, ads[(int)(u / 8) % ads.Length]);
                PaintedBox(u, fv1 + 1.2f, u + 7.6f, fv1 + 1.4f, g + 0.15f, g + 1.1f, ads[(int)(u / 8 + 2) % ads.Length]);
            }
            for (int k = -1; k <= 1; k += 2)
            {
                float cu = _w / 2 + k * 9f;
                PaintedBox(cu - 4f, fv0 - 2.8f, cu + 4f, fv0 - 1.6f, g + 0.15f, g + 1.9f, 0x12A3A55);
            }
            // tabellone sopra la curva est
            float tu = _w - 2f;
            PaintedBox(tu - 1f, _d / 2 - 7f, tu, _d / 2 + 7f, g + zEnds, g + zEnds + 5.5f, 0x1262A30);
            Poly(new[] { tu - 1.02f, _d / 2 - 6.4f, g + zEnds + 0.6f, tu - 1.02f, _d / 2 + 6.4f, g + zEnds + 0.6f,
                tu - 1.02f, _d / 2 + 6.4f, g + zEnds + 5f, tu - 1.02f, _d / 2 - 6.4f, g + zEnds + 5f }, -1, 0, 0, 0x1203848, BuildingDecorator.KindLight, 0.5f);
            // torri faro agli angoli, fari rivolti verso il centro del campo
            for (int k = 0; k < 4; k++)
            {
                float mu = (k & 1) == 0 ? 4f : _w - 4f, mv = (k & 2) == 0 ? 4f : _d - 4f;
                float mg = Ground(mu, mv);
                float topZ = _z0 + H;
                Cylinder(mu, mv, 0.9f, mg - 0.3f, topZ - 4.5f, 10, 0x1A9AEB4, 1f, 0.55f);
                float du = _w / 2 - mu, dv = _d / 2 - mv;
                float dl = (float)Math.Sqrt(du * du + dv * dv);
                du /= dl;
                dv /= dl;
                float pu = mu + du * 1.2f, pv = mv + dv * 1.2f;
                float su = -dv * 3.2f, sv = du * 3.2f;
                PaintedBox(Math.Min(pu - su, pu + su) - 0.3f, Math.Min(pv - sv, pv + sv) - 0.3f, Math.Max(pu - su, pu + su) + 0.3f, Math.Max(pv - sv, pv + sv) + 0.3f,
                    topZ - 4.6f, topZ - 4.4f, 0x14A4F55);
                Poly(new[] { pu - su, pv - sv, topZ - 4.4f, pu + su, pv + sv, topZ - 4.4f, pu + su, pv + sv, topZ, pu - su, pv - sv, topZ },
                    du, dv, 0, 0x1FFF2D2, BuildingDecorator.KindLight, 1f);
            }
        }

        /// <summary>
        /// Tribuna a gradinata sul rettangolo; <paramref name="front"/> = lato verso il campo (0: v alte, 1: v basse,
        /// 2: u alte, 3: u basse). Sedili dello shader (tipo 18) sul piano inclinato, muri in cemento, scale ogni 18 m.
        /// </summary>
        private void SeatingStand(float u0, float v0, float u1, float v1, int front, float g, float zFront, float zBack, uint seat)
        {
            // angoli in ordine: fronte sinistra, fronte destra, retro destra, retro sinistra
            float[] fu, fv, bu, bv;
            switch (front)
            {
                case 0: fu = new[] { u0, u1 }; fv = new[] { v1, v1 }; bu = new[] { u0, u1 }; bv = new[] { v0, v0 }; break;
                case 1: fu = new[] { u0, u1 }; fv = new[] { v0, v0 }; bu = new[] { u0, u1 }; bv = new[] { v1, v1 }; break;
                case 2: fu = new[] { u1, u1 }; fv = new[] { v0, v1 }; bu = new[] { u0, u0 }; bv = new[] { v0, v1 }; break;
                default: fu = new[] { u0, u0 }; fv = new[] { v0, v1 }; bu = new[] { u1, u1 }; bv = new[] { v0, v1 }; break;
            }
            float zb = g - 0.5f;
            uint concrete = 0x1C6C2BA;
            // gradinata
            Poly(new[] { fu[0], fv[0], zFront, fu[1], fv[1], zFront, bu[1], bv[1], zBack, bu[0], bv[0], zBack }, 0, 0, 1, seat,
                BuildingDecorator.KindSeats, BuildingMesher.SeatRowHeight);
            // retro, parapetto sul fronte, fianchi
            float ou = (bu[0] - fu[0]), ov = (bv[0] - fv[0]);
            Poly(new[] { bu[0], bv[0], zb, bu[1], bv[1], zb, bu[1], bv[1], zBack, bu[0], bv[0], zBack }, ou, ov, 0, concrete, 0, 1, 12);
            Poly(new[] { fu[0], fv[0], zb, fu[1], fv[1], zb, fu[1], fv[1], zFront, fu[0], fv[0], zFront }, -ou, -ov, 0, concrete, 0, 1, 12);
            float lu = fu[0] - fu[1], lv = fv[0] - fv[1];
            Poly(new[] { fu[0], fv[0], zb, bu[0], bv[0], zb, bu[0], bv[0], zBack, fu[0], fv[0], zFront }, lu, lv, 0, concrete, 0, 1, 12);
            Poly(new[] { fu[1], fv[1], zb, bu[1], bv[1], zb, bu[1], bv[1], zBack, fu[1], fv[1], zFront }, -lu, -lv, 0, concrete, 0, 1, 12);
            // scale: strisce chiare sulla gradinata
            float len = (float)Math.Sqrt(lu * lu + lv * lv);
            int aisles = Math.Max(1, (int)(len / 18f));
            for (int k = 1; k <= aisles; k++)
            {
                float t = k / (float)(aisles + 1), w = 0.7f / len;
                float a = t - w, b = t + w;
                float Fu(float x) => fu[0] + (fu[1] - fu[0]) * x;
                float Fv(float x) => fv[0] + (fv[1] - fv[0]) * x;
                float Bu(float x) => bu[0] + (bu[1] - bu[0]) * x;
                float Bv(float x) => bv[0] + (bv[1] - bv[0]) * x;
                Poly(new[] { Fu(a), Fv(a), zFront + 0.05f, Fu(b), Fv(b), zFront + 0.05f, Bu(b), Bv(b), zBack + 0.05f, Bu(a), Bv(a), zBack + 0.05f },
                    0, 0, 1, 0x1D8D4CC, BuildingDecorator.KindProp, 0);
            }
        }

        /// <summary>Copertura a sbalzo sopra una tribuna: lastra leggermente inclinata verso il campo, pilastri sul retro.</summary>
        private void StandRoof(float u0, float v0, float u1, float v1, int front, float g, float z)
        {
            // la copertura parte dal retro e copre il 75% della profondità
            float ru0 = u0, rv0 = v0, ru1 = u1, rv1 = v1;
            switch (front)
            {
                case 0: rv1 = v0 + (v1 - v0) * 0.8f; break;
                case 1: rv0 = v1 - (v1 - v0) * 0.8f; break;
                case 2: ru1 = u0 + (u1 - u0) * 0.8f; break;
                default: ru0 = u1 - (u1 - u0) * 0.8f; break;
            }
            PaintedBox(ru0, rv0, ru1, rv1, z, z + 0.7f, 0x1E9ECEE);
            // bordo frontale colorato
            switch (front)
            {
                case 0: PaintedBox(ru0, rv1 - 0.4f, ru1, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                case 1: PaintedBox(ru0, rv0, ru1, rv0 + 0.4f, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                case 2: PaintedBox(ru1 - 0.4f, rv0, ru1, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
                default: PaintedBox(ru0, rv0, ru0 + 0.4f, rv1, z - 0.6f, z + 0.9f, 0x12F4F7A); break;
            }
            bool alongU = front < 2;
            float len = alongU ? ru1 - ru0 : rv1 - rv0;
            int cols = Math.Max(2, (int)(len / 16f) + 1);
            for (int k = 0; k < cols; k++)
            {
                float t = k / (float)(cols - 1);
                float cu = alongU ? ru0 + 0.8f + (ru1 - ru0 - 1.6f) * t : (front == 2 ? u0 + 0.8f : u1 - 0.8f);
                float cv = alongU ? (front == 0 ? v0 + 0.8f : v1 - 0.8f) : rv0 + 0.8f + (rv1 - rv0 - 1.6f) * t;
                Cylinder(cu, cv, 0.45f, g - 0.3f, z, 8, 0x1D5D9DD);
            }
        }

        // ------------------------------------------------------------------ trasporti

        /// <summary>Parcheggio: asfalto con gli stalli, auto, lampioni, cordoli sul bordo.</summary>
        private void Parking()
        {
            ParkingLot(0.3f, 0.3f, _w - 0.3f, _d - 0.3f, 171, true);
            Curbs(0, 0, _w, _d);
            foreach (float u in new[] { 1.0f, _w - 1.0f }) Tree(PlantKind.Broadleaf, u, _d - 1.0f, (int)u + 3);
        }

        /// <summary>Cordolo in pietra lungo il bordo del rettangolo.</summary>
        private void Curbs(float u0, float v0, float u1, float v1)
        {
            uint c = 0x1C9C4BA;
            foreach (var (a, b, cc, d) in new[] { (u0, v0, u1, v0 + 0.25f), (u0, v1 - 0.25f, u1, v1), (u0, v0 + 0.25f, u0 + 0.25f, v1 - 0.25f), (u1 - 0.25f, v0 + 0.25f, u1, v1 - 0.25f) })
            {
                Range(a, b, cc, d, out var lo, out var hi);
                PaintedBox(a, b, cc, d, lo - 0.1f, hi + 0.15f, c);
            }
        }

        /// <summary>
        /// Parcheggio multipiano: piani aperti con i parapetti in cemento e le auto, pilastri, torre delle scale e
        /// dell'ascensore con l'insegna "P" blu, ultimo piano scoperto con i lampioni.
        /// </summary>
        private void ParkingGarage(CatalogItem item)
        {
            float H = item.Height;
            Asphalt(0, 0, _w, _d);
            float u0 = 1.0f, u1 = _w - 1.0f, v0 = 1.0f, v1 = _d - 1.0f;
            float g = Base(u0, v0, u1, v1);
            const int decks = 4;
            float dh = (H - 1.6f) / decks;
            uint conc = 0x1CFCBC2, parapet = 0x1DCD8D0;
            // pilastri
            for (float u = u0 + 0.5f; u < u1; u += 8f)
                for (float v = v0 + 0.5f; v < v1; v += 7.0f)
                    PaintedBox(u - 0.3f, v - 0.3f, u + 0.3f, v + 0.3f, g - 0.2f, g + decks * dh, conc);
            for (int k = 0; k <= decks; k++)
            {
                float z = g + k * dh;
                if (k > 0)
                {
                    PaintedBox(u0, v0, u1, v1, z - 0.4f, z, conc);
                    Flat(u0 + 0.3f, v0 + 0.3f, u1 - 0.3f, v1 - 0.3f, z + 0.01f, 0x17A7D82, 9);
                }
                // parapetti sui quattro lati
                PaintedBox(u0, v0, u1, v0 + 0.25f, z, z + 1.0f, parapet);
                PaintedBox(u0, v1 - 0.25f, u1, v1, z, z + 1.0f, parapet);
                PaintedBox(u0, v0 + 0.25f, u0 + 0.25f, v1 - 0.25f, z, z + 1.0f, parapet);
                PaintedBox(u1 - 0.25f, v0 + 0.25f, u1, v1 - 0.25f, z, z + 1.0f, parapet);
                // auto nelle due file lungo i parapetti
                for (float u = u0 + 7.5f; u + 1.4f < u1 - 0.6f; u += 2.6f)
                {
                    if (Frac((long)(u * 7) + k * 101) < 0.45f + 0.08f * _level) Car(u, v0 + 3.0f, (float)Math.PI / 2, (long)(u * 13) + k, z + 0.02f);
                    if (Frac((long)(u * 11) + k * 103) < 0.4f + 0.08f * _level) Car(u, v1 - 3.0f, (float)Math.PI / 2, (long)(u * 17) + k, z + 0.02f);
                }
            }
            // torre delle scale e dell'ascensore
            float tu1 = u0 + 6.0f, tv1 = v0 + 6.0f;
            float tg = Block(u0 - 0.6f, v0 - 0.6f, tu1, tv1, H + 1.2f, 0x1BFBBB3, 12, 12, 12, 12, 1, 0x1A9A69E);
            Glazing(u0 + 1.2f, v0 - 0.6f, tu1 - 1.2f, v0 - 0.6f, tg, tg + H - 2.5f, decks);
            // insegna "P" blu accesa
            float sz = tg + H - 2.2f;
            Sign(tu1 + 0.0f, v0 + 0.6f, tu1 + 0.0f, tv1 - 0.6f, sz, sz + 2.8f, 0x11F4E8C, false);
            float pu = tu1 + 0.08f, pv = (v0 + tv1) / 2;
            PaintedBox(pu, pv - 0.9f, pu + 0.06f, pv - 0.5f, sz + 0.5f, sz + 2.3f, 0x1F4F4F2, BuildingDecorator.KindLight, 0.6f);
            PaintedBox(pu, pv - 0.5f, pu + 0.06f, pv + 0.6f, sz + 2.0f, sz + 2.3f, 0x1F4F4F2, BuildingDecorator.KindLight, 0.6f);
            PaintedBox(pu, pv - 0.5f, pu + 0.06f, pv + 0.6f, sz + 1.3f, sz + 1.6f, 0x1F4F4F2, BuildingDecorator.KindLight, 0.6f);
            PaintedBox(pu, pv + 0.5f, pu + 0.06f, pv + 0.8f, sz + 1.3f, sz + 2.3f, 0x1F4F4F2, BuildingDecorator.KindLight, 0.6f);
            // lampioni sull'ultimo piano
            float top = g + decks * dh;
            for (float u = u0 + 12; u < u1 - 4; u += 12)
            {
                Pipe(u, _d / 2, 0.08f, top, top + 4.0f, 0x15A6066, 8);
                PaintedBox(u - 0.3f, _d / 2 - 0.15f, u + 0.3f, _d / 2 + 0.15f, top + 4.0f, top + 4.2f, 0x1FFE6B0, BuildingDecorator.KindLight, 1);
            }
        }

        /// <summary>Pensilina dell'autobus: tetto, vetrate dietro e di lato, panchina, pannello pubblicitario acceso, palina con la tabella.</summary>
        private void BusStop()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.12f, 0x1C9C4BA, 10);
            float z = g + 0.12f, back = _d - 0.35f;
            Wall(0.4f, back, _w - 0.9f, back, z, z + 2.2f, z, 0x1A9B0B6, 10, 1);
            Wall(_w - 0.9f, back + 0.02f, 0.4f, back + 0.02f, z, z + 2.2f, z, 0x1A9B0B6, 10, 1);
            Wall(0.4f, back, 0.4f, 0.8f, z, z + 2.2f, z, 0x1A9B0B6, 10, 1);
            Wall(0.42f, 0.8f, 0.42f, back, z, z + 2.2f, z, 0x1A9B0B6, 10, 1);
            foreach (float u in new[] { 0.35f, _w - 0.95f })
            {
                Beam(u, back, z, u, back, z + 2.4f, 0.05f, 0x1404850);
                Beam(u, 0.6f, z, u, 0.6f, z + 2.4f, 0.05f, 0x1404850);
            }
            PaintedBox(0.1f, 0.15f, _w - 0.7f, _d - 0.1f, z + 2.4f, z + 2.6f, 0x1404850);
            Bench(1.0f, back - 0.6f, _w - 1.6f, back - 0.1f);
            // pannello pubblicitario retroilluminato
            PaintedBox(_w - 0.9f, back - 0.15f, _w - 0.75f, back + 0.15f, z + 0.3f, z + 2.1f, 0x1404850);
            Poly(new[] { _w - 0.74f, back - 0.12f, z + 0.4f, _w - 0.74f, back + 0.12f, z + 0.4f, _w - 0.74f, back + 0.12f, z + 2.0f, _w - 0.74f, back - 0.12f, z + 2.0f },
                1, 0, 0, 0x1F2E8D0, BuildingDecorator.KindLight, 0.6f);
            // palina con la tabella delle linee
            float pu = _w - 0.3f, pv = 0.3f;
            Pipe(pu, pv, 0.04f, z, z + 2.8f, 0x1909498, 6);
            PaintedBox(pu - 0.25f, pv - 0.03f, pu + 0.25f, pv + 0.03f, z + 2.3f, z + 2.75f, 0x11F4E8C);
            PaintedBox(pu - 0.2f, pv - 0.04f, pu + 0.2f, pv + 0.04f, z + 1.5f, z + 2.2f, 0x1F2C230);
        }

        /// <summary>Autostazione: pensilina lunga sulle banchine, autobus agli stalli, terminal vetrato con l'insegna, segnaletica a terra.</summary>
        private void BusStation(CatalogItem item)
        {
            Asphalt(0, 0, _w, _d);
            float g = Base(0, 0, _w, _d);
            float pv0 = _d * 0.38f, pv1 = _d * 0.62f;
            Flat(1.5f, pv0, _w - 1.5f, pv1, g + 0.18f, 0x1C9C4BA, 10);
            float zc = g + Math.Min(item.Height - 1.4f, 5.4f);
            PaintedBox(1.5f, pv0 - 1.0f, _w - 1.5f, pv1 + 1.0f, zc, zc + 0.4f, 0x1F2F2F0);
            Poly(new[] { 1.5f, pv0 - 1.02f, zc - 0.1f, _w - 1.5f, pv0 - 1.02f, zc - 0.1f, _w - 1.5f, pv0 - 1.02f, zc + 0.4f, 1.5f, pv0 - 1.02f, zc + 0.4f }, 0, -1, 0, 0x11F5FA8, BuildingDecorator.KindProp, 0);
            for (float u = 3; u < _w - 2; u += 6) Pipe(u, (pv0 + pv1) / 2, 0.22f, g + 0.18f, zc, 0x1B8BCC0, 10);
            // tabelloni delle partenze sotto la pensilina e panchine
            for (float u = 6; u < _w - 5; u += 12)
            {
                Sign(u - 1.2f, (pv0 + pv1) / 2 - 0.25f, u + 1.2f, (pv0 + pv1) / 2 - 0.25f, zc - 1.2f, zc - 0.4f, 0x1202830);
                Bench(u - 1.0f, pv1 - 1.0f, u + 1.0f, pv1 - 0.5f);
            }
            // autobus agli stalli
            uint[] liveries = { 0x11F5FA8, 0x1E0E3E6, 0x12E7D4F, 0x1B3261E };
            int n = Math.Max(1, (int)((_w - 3) / 13f));
            for (int k = 0; k < n; k++)
            {
                float u = 1.5f + (k + 0.5f) * (_w - 3) / n;
                Bus(u, pv0 - 2.4f, 0, Pick(liveries, k));
                if (_level >= 2 && k % 2 == 0) Bus(u, pv1 + 2.4f, (float)Math.PI, Pick(liveries, k + 7));
            }
            // terminal vetrato
            float tu1 = Math.Min(_w * 0.4f, 16f), tv0 = _d * 0.8f;
            float tg = Block(1.5f, tv0, tu1, _d - 0.8f, 4.6f, 0x1E8E6E0, 10, 12, 12, 12, 1, 0x1A9A69E);
            Sign(2.0f, tv0 - 0.02f, tu1 - 0.5f, tv0 - 0.02f, tg + 3.4f, tg + 4.3f, 0x11F5FA8);
            // strisce degli stalli
            for (int k = 0; k <= n; k++)
            {
                float u = 1.5f + k * (_w - 3) / n;
                Flat(u - 0.08f, pv0 - 4.2f, u + 0.08f, pv0 - 0.4f, g + 0.09f, 0x1F2F2F0, BuildingDecorator.KindProp, 0);
            }
        }

        /// <summary>Vialetto pedonale lastricato con i cordoli.</summary>
        private void Footpath()
        {
            Paving(0, 0, _w, _d, 0x1D5CBB8);
            bool alongU = _w >= _d;
            if (alongU)
            {
                Range(0, 0, _w, 0.15f, out var lo, out var hi);
                PaintedBox(0, 0, _w, 0.15f, lo - 0.1f, hi + 0.14f, 0x1C9C4BA);
                Range(0, _d - 0.15f, _w, _d, out lo, out hi);
                PaintedBox(0, _d - 0.15f, _w, _d, lo - 0.1f, hi + 0.14f, 0x1C9C4BA);
            }
            else
            {
                Range(0, 0, 0.15f, _d, out var lo, out var hi);
                PaintedBox(0, 0, 0.15f, _d, lo - 0.1f, hi + 0.14f, 0x1C9C4BA);
                Range(_w - 0.15f, 0, _w, _d, out lo, out hi);
                PaintedBox(_w - 0.15f, 0, _w, _d, lo - 0.1f, hi + 0.14f, 0x1C9C4BA);
            }
        }

        /// <summary>Piazza lastricata: rosone in pietra al centro, fascia di bordo, panchine, lampioni storici, alberi nelle aiuole.</summary>
        private void Piazza()
        {
            Paving(0, 0, _w, _d, 0x1D5CBB8);
            Paving(0, 0, _w, 0.8f, 0x1B8AE9C, 0.1f);
            Paving(0, _d - 0.8f, _w, _d, 0x1B8AE9C, 0.1f);
            Paving(0, 0.8f, 0.8f, _d - 0.8f, 0x1B8AE9C, 0.1f);
            Paving(_w - 0.8f, 0.8f, _w, _d - 0.8f, 0x1B8AE9C, 0.1f);
            float cu = _w / 2, cv = _d / 2, g = Ground(cu, cv);
            float r = Math.Min(_w, _d) * 0.22f;
            var c = World(cu, cv);
            const int seg = 24;
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                var p0 = c + (_u * (float)Math.Cos(a0) + _v * (float)Math.Sin(a0)) * r;
                var p1 = c + (_u * (float)Math.Cos(a1) + _v * (float)Math.Sin(a1)) * r;
                uint col = MeshBuffer.Rgb(i % 2 == 0 ? 0xB9AF9Eu : 0xE6DED0u);
                QuadW(new Vec3(c.X, c.Y, g + 0.12f), new Vec3(p0.X, p0.Y, g + 0.12f), new Vec3(p1.X, p1.Y, g + 0.12f), new Vec3(p1.X, p1.Y, g + 0.12f),
                    new Vec3(0, 0, 1), col, 10, 1);
            }
            if (_w < 8) return;
            foreach (int su in new[] { -1, 1 })
                foreach (int sv in new[] { -1, 1 })
                {
                    float lu = cu + su * (_w / 2 - 2.2f), lv = cv + sv * (_d / 2 - 2.2f);
                    StreetLamp(lu, lv, 3.8f, true);
                    float tu = cu + su * (_w / 2 - 4.5f), tv = cv + sv * (_d / 2 - 4.5f), tg = Ground(tu, tv);
                    PaintedBox(tu - 0.8f, tv - 0.8f, tu + 0.8f, tv + 0.8f, tg - 0.1f, tg + 0.45f, 0x1C9C4BA);
                    Foliage(tu - 0.7f, tv - 0.7f, tu + 0.7f, tv + 0.7f, tg + 0.45f, tg + 0.55f, 0x1557A35, 0.2f);
                    Tree(PlantKind.Broadleaf, tu, tv, su * 3 + sv + 81);
                    float bu = cu + su * r * 1.25f, bv = cv + sv * r * 1.25f;
                    Bench(bu - 0.8f, bv - 0.25f, bu + 0.8f, bv + 0.25f);
                }
        }

        /// <summary>Eliporto: piazzola in cemento con il cerchio e la "H", luci di bordo, manica a vento.</summary>
        private void Helipad()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.12f, 0x16A6E72, BuildingDecorator.KindProp, 0);
            float r = Math.Min(_w, _d) * 0.4f;
            HelipadMark(_w / 2, _d / 2, r, g + 0.14f);
            for (int k = 0; k < 4; k++)
            {
                float u = (k & 1) == 0 ? 0.6f : _w - 0.6f, v = (k & 2) == 0 ? 0.6f : _d - 0.6f;
                PaintedBox(u - 0.15f, v - 0.15f, u + 0.15f, v + 0.15f, g + 0.12f, g + 0.4f, 0x1F2C230, BuildingDecorator.KindLight, 1);
            }
            // manica a vento a bande bianche e arancioni
            float mu = _w - 1.0f, mv = 1.6f;
            Pipe(mu, mv, 0.05f, g, g + 3.6f, 0x1D8DCDF, 6);
            for (int k = 0; k < 4; k++)
            {
                float a = mu - 0.1f - k * 0.45f, b = a - 0.45f, s = 0.22f - 0.035f * k;
                PaintedBox(b, mv - s, a, mv + s, g + 3.35f - s, g + 3.35f + s, k % 2 == 0 ? 0x1F07020u : 0x1F4F4F2u);
            }
        }

        // ------------------------------------------------------------------ arredo urbano

        /// <summary>Fontana in pietra: vasca con l'acqua, fusto, bacile alto con lo zampillo; quella monumentale è ottagonale con la statua.</summary>
        private void Fountain(float h, bool large)
        {
            float g = Base(0, 0, _w, _d);
            float r = Math.Min(_w, _d) / 2;
            int sides = large ? 8 : 20;
            uint stone = 0x1D9D1C2;
            Cylinder(_w / 2, _d / 2, r, g - 0.2f, g + 0.6f, sides, stone, 1, 1, BuildingDecorator.KindProp, 0);
            Cylinder(_w / 2, _d / 2, r * 0.88f, g + 0.45f, g + 0.62f, sides, 0x157A8C8, 1, 1, BuildingDecorator.KindWater, 0);
            if (large)
            {
                Cylinder(_w / 2, _d / 2, r * 0.22f, g + 0.4f, g + h * 0.45f, 8, stone, 1, 0.85f, BuildingDecorator.KindProp, 0);
                Cylinder(_w / 2, _d / 2, r * 0.42f, g + h * 0.45f, g + h * 0.52f, 12, stone, 0.4f, 1f, BuildingDecorator.KindProp, 0);
                Figure(_w / 2, _d / 2, g + h * 0.52f, h * 0.46f, 0x1E8E2D6, true);
                for (int k = 0; k < 4; k++)
                {
                    double a = Math.PI / 4 + k * Math.PI / 2;
                    float su = _w / 2 + (float)Math.Cos(a) * r * 0.55f, sv = _d / 2 + (float)Math.Sin(a) * r * 0.55f;
                    Sphere(su, sv, g + 0.75f, 0.35f, stone);
                }
                return;
            }
            Cylinder(_w / 2, _d / 2, r * 0.12f, g + 0.4f, g + h * 0.6f, 10, stone, 1, 1, BuildingDecorator.KindProp, 0);
            Cylinder(_w / 2, _d / 2, r * 0.42f, g + h * 0.6f, g + h * 0.68f, 16, stone, 0.35f, 1f, BuildingDecorator.KindProp, 0);
            Cylinder(_w / 2, _d / 2, r * 0.36f, g + h * 0.66f, g + h * 0.685f, 16, 0x157A8C8, 1, 1, BuildingDecorator.KindWater, 0);
            Cylinder(_w / 2, _d / 2, r * 0.06f, g + h * 0.68f, g + h * 0.92f, 8, stone, 1, 0.6f, BuildingDecorator.KindProp, 0);
            Sphere(_w / 2, _d / 2, g + h * 0.95f, r * 0.07f, stone);
        }

        /// <summary>
        /// Statua su piedistallo (figura umana in bronzo); il monumento ai caduti ha la gradinata, la stele con le
        /// lapidi, la Vittoria alata in bronzo e i paracarri con la catena.
        /// </summary>
        private void Statue(float h, bool large)
        {
            float g = Base(0, 0, _w, _d);
            uint stone = 0x1DAD2C3, bronze = 0x1556B4E;
            float cu = _w / 2, cv = _d / 2;
            if (!large)
            {
                PaintedBox(_w * 0.12f, _d * 0.12f, _w * 0.88f, _d * 0.88f, g - 0.2f, g + 0.3f, Darken(stone, 0.05f));
                PaintedBox(_w * 0.22f, _d * 0.22f, _w * 0.78f, _d * 0.78f, g + 0.3f, g + h * 0.4f, stone);
                PaintedBox(_w * 0.18f, _d * 0.18f, _w * 0.82f, _d * 0.82f, g + h * 0.4f, g + h * 0.44f, Lighten(stone, 0.05f));
                Figure(cu, cv, g + h * 0.44f, h * 0.54f, bronze, false);
                return;
            }
            for (int k = 0; k < 3; k++)
            {
                float m = 0.1f + k * 0.28f;
                PaintedBox(m, m, _w - m, _d - m, g - 0.2f, g + 0.18f * (k + 1), Darken(stone, 0.03f * k));
            }
            float sb = g + 0.54f;
            PaintedBox(cu - 0.8f, cv - 0.8f, cu + 0.8f, cv + 0.8f, sb, sb + h * 0.5f, stone);
            PaintedBox(cu - 0.9f, cv - 0.9f, cu + 0.9f, cv + 0.9f, sb + h * 0.5f, sb + h * 0.53f, Lighten(stone, 0.05f));
            Poly(new[] { cu - 0.55f, cv - 0.81f, sb + 0.5f, cu + 0.55f, cv - 0.81f, sb + 0.5f, cu + 0.55f, cv - 0.81f, sb + 1.6f, cu - 0.55f, cv - 0.81f, sb + 1.6f }, 0, -1, 0, 0x16E6A5E, BuildingDecorator.KindProp, 0);
            Figure(cu, cv, sb + h * 0.53f, h * 0.4f, bronze, true);
            // ali della Vittoria
            float fz = sb + h * 0.53f, fh = h * 0.4f;
            foreach (int s in new[] { -1, 1 })
                Poly(new[] { cu + s * 0.08f, cv + 0.1f, fz + fh * 0.75f, cu + s * fh * 0.35f, cv + 0.3f, fz + fh * 1.0f, cu + s * fh * 0.2f, cv + 0.25f, fz + fh * 0.5f },
                    0, 1, 0.2f, bronze, BuildingDecorator.KindProp, 0);
            // paracarri e catena
            foreach (float u in new[] { 0.25f, _w - 0.25f })
                foreach (float v in new[] { 0.25f, _d - 0.25f })
                    Pipe(u, v, 0.1f, Ground(u, v) - 0.1f, Ground(u, v) + 0.7f, 0x1404850, 6);
            Beam(0.25f, 0.25f, g + 0.55f, _w - 0.25f, 0.25f, g + 0.55f, 0.015f, 0x1303438);
            Beam(0.25f, _d - 0.25f, g + 0.55f, _w - 0.25f, _d - 0.25f, g + 0.55f, 0.015f, 0x1303438);
        }

        /// <summary>Figura umana stilizzata (gambe, busto, braccia, testa) alta <paramref name="hf"/>, rivolta verso -V; <paramref name="raised"/> = braccio alzato.</summary>
        private void Figure(float cu, float cv, float z, float hf, uint color, bool raised)
        {
            float leg = hf * 0.06f;
            foreach (int s in new[] { -1, 1 })
                OBox(cu + s * hf * 0.055f, cv, 0, leg, leg * 1.1f, z, z + hf * 0.47f, color);
            OBox(cu, cv, 0, hf * 0.14f, hf * 0.08f, z + hf * 0.45f, z + hf * 0.62f, color);
            OBox(cu, cv, 0, hf * 0.12f, hf * 0.075f, z + hf * 0.6f, z + hf * 0.8f, color);
            OBox(cu, cv, 0, hf * 0.035f, hf * 0.035f, z + hf * 0.8f, z + hf * 0.85f, color);
            Sphere(cu, cv, z + hf * 0.91f, hf * 0.065f, color);
            float sz = z + hf * 0.78f, aw = hf * 0.035f;
            Beam(cu - hf * 0.15f, cv, sz, cu - hf * 0.19f, cv - hf * 0.04f, sz - hf * 0.3f, aw, color);
            if (raised) Beam(cu + hf * 0.15f, cv, sz, cu + hf * 0.24f, cv - hf * 0.08f, sz + hf * 0.32f, aw, color);
            else Beam(cu + hf * 0.15f, cv, sz, cu + hf * 0.19f, cv - hf * 0.06f, sz - hf * 0.28f, aw, color);
        }

        /// <summary>Fioriera in pietra con le piante fiorite.</summary>
        private void Planter()
        {
            float g = Base(0, 0, _w, _d);
            PaintedBox(0, 0, _w, _d, g - 0.1f, g + 0.5f, 0x1C9B9A3);
            PaintedBox(-0.0f, -0.0f, _w, _d, g + 0.5f, g + 0.56f, 0x1D9CBB6);
            Foliage(0.1f, 0.1f, _w - 0.1f, _d - 0.1f, g + 0.5f, g + 0.8f, 0x1557A35, 0.7f);
        }

        /// <summary>Gazebo: pavimento in pietra, colonnine, tetto a piramide in coppi, panca circolare.</summary>
        private void Gazebo(float h)
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.15f, 0x1D5CBB8, 10);
            for (int k = 0; k < 4; k++)
                Cylinder((k & 1) == 0 ? 0.3f : _w - 0.3f, (k & 2) == 0 ? 0.3f : _d - 0.3f, 0.1f, g, g + h - 1, 6, 0x1F2EDE2, 1, 1, BuildingDecorator.KindProp, 0);
            Building(0, 0, _w, _d, 1.2f, RoofShape.Pyramidal, 0x1F2EDE2, 0x13F6B4A, 1, BuildingUse.Roof, false, g + h - 1.2f);
            PaintedBox(0.5f, 0.5f, _w - 0.5f, 0.8f, g + 0.15f, g + 0.6f, 0x19C7350);
            PaintedBox(0.5f, _d - 0.8f, _w - 0.5f, _d - 0.5f, g + 0.15f, g + 0.6f, 0x19C7350);
        }

        /// <summary>Chiosco delle bibite: cassone con il bancone aperto sul fronte, tenda a strisce, tetto sporgente, insegna accesa.</summary>
        private void Kiosk(float h)
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.1f, 0x1C9C4BA, 10);
            uint body = 0x12F5D3A;
            float v0 = 0.45f, zTop = g + h - 0.6f;
            PaintedBox(0.2f, v0, _w - 0.2f, _d - 0.2f, g, zTop, body);
            // apertura del bancone e bancone
            Poly(new[] { 0.5f, v0 - 0.01f, g + 1.05f, _w - 0.5f, v0 - 0.01f, g + 1.05f, _w - 0.5f, v0 - 0.01f, zTop - 0.35f, 0.5f, v0 - 0.01f, zTop - 0.35f }, 0, -1, 0, 0x1302A24, BuildingDecorator.KindProp, 0);
            PaintedBox(0.4f, v0 - 0.3f, _w - 0.4f, v0, g + 0.95f, g + 1.05f, 0x1E8E2D6);
            // bottiglie e frigo dentro (macchie colorate)
            for (int k = 0; k < 6; k++)
                PaintedBox(0.7f + k * (_w - 1.4f) / 6, v0 + 0.05f, 0.85f + k * (_w - 1.4f) / 6, v0 + 0.15f, g + 1.6f, g + 1.85f, Pick(AwningColors, k));
            Poly(new[] { 0.3f, v0, zTop - 0.1f, _w - 0.3f, v0, zTop - 0.1f, _w - 0.3f, 0.02f, zTop - 0.5f, 0.3f, 0.02f, zTop - 0.5f }, 0, -0.4f, 1, 0x1C8372D, BuildingDecorator.KindCanvas, 1);
            PaintedBox(0.05f, 0.05f, _w - 0.05f, _d - 0.05f, zTop, zTop + 0.15f, 0x1E8E2D6);
            Sign(0.4f, v0 - 0.02f, _w - 0.4f, v0 - 0.02f, zTop + 0.15f, g + h - 0.02f, 0x1E8A317);
        }

        /// <summary>Parco giochi: pavimento antitrauma, scivolo, altalena, sabbiera, molle a dondolo, panchina e recinzione bassa.</summary>
        private void Playground()
        {
            Playset(0.5f, 0.5f, _w - 0.5f, _d - 2.0f);
            float g = Base(0, 0, _w, _d);
            Flat(0.5f, _d - 2.0f, _w - 0.5f, _d - 0.5f, g + 0.08f, 0x1B5553F, BuildingDecorator.KindProp, 0);
            uint[] colors = { 0x1E8C030, 0x1D9534A, 0x14F79B5 };
            for (int k = 0; k < 3; k++)
            {
                float u = _w * 0.25f + k * _w * 0.2f, v = _d - 1.25f;
                Pipe(u, v, 0.06f, g, g + 0.45f, 0x1303438, 6);
                OBox(u, v, 0.4f * k, 0.4f, 0.16f, g + 0.45f, g + 0.85f, colors[k]);
            }
            Bench(_w - 2.4f, _d - 1.4f, _w - 0.8f, _d - 0.9f);
            LotFence(1.0f, 1.4f, 0x12E7D4F, false, 0.15f);
        }

        /// <summary>Dehors: pedana, tavolini con le sedie, ombrelloni a strisce, fioriere di bordo.</summary>
        private void Terrace()
        {
            float g = Base(0, 0, _w, _d);
            Flat(0, 0, _w, _d, g + 0.12f, 0x1A8805C, BuildingDecorator.KindProp, 0);
            float z = g + 0.12f;
            for (float u = 1.2f; u + 1 < _w; u += 2.4f)
                for (float v = 1.4f; v + 1.0f < _d - 0.5f; v += 2.2f)
                {
                    Pipe(u, v, 0.35f, z + 0.72f, z + 0.76f, 0x1F2F2F2, 10);
                    Pipe(u, v, 0.04f, z, z + 0.72f, 0x1505458, 6);
                    foreach (float du in new[] { -0.6f, 0.6f })
                    {
                        PaintedBox(u + du - 0.2f, v - 0.2f, u + du + 0.2f, v + 0.2f, z + 0.42f, z + 0.46f, 0x1505458);
                        PaintedBox(u + du + (du > 0 ? 0.16f : -0.2f), v - 0.2f, u + du + (du > 0 ? 0.2f : -0.16f), v + 0.2f, z, z + 0.9f, 0x1505458);
                    }
                    Pipe(u, v, 0.025f, z + 0.76f, z + 2.2f, 0x1E8E8E8, 6);
                    Cone(u, v, 1.05f, z + 1.95f, 0.4f, 0x1C8473B, BuildingDecorator.KindCanvas, 8, 1);
                }
            for (float u = 0.2f; u + 1.2f < _w; u += 1.5f)
            {
                PaintedBox(u, 0.1f, u + 1.2f, 0.5f, z, z + 0.45f, 0x1E6E1D6);
                Foliage(u + 0.05f, 0.15f, u + 1.15f, 0.45f, z + 0.45f, z + 0.85f, 0x1557A35, 0.5f);
            }
        }

        /// <summary>Rastrelliera ad archetti (e qualche bicicletta).</summary>
        private void BikeRack() => BikeRackAt(0, 0, _w);

        private void BikeRackAt(float u0, float v0, float len)
        {
            float g = Ground(u0 + len / 2, v0 + 0.5f);
            int k = 0;
            for (float u = u0 + 0.3f; u < u0 + len - 0.1f; u += 0.7f, k++)
            {
                Beam(u, v0 + 0.25f, g, u, v0 + 0.25f, g + 0.75f, 0.025f, 0x1909498);
                Beam(u, v0 + 0.75f, g, u, v0 + 0.75f, g + 0.75f, 0.025f, 0x1909498);
                Beam(u, v0 + 0.25f, g + 0.75f, u, v0 + 0.75f, g + 0.75f, 0.025f, 0x1909498);
                if (Frac(k * 3 + 7) < 0.25f + 0.1f * _level)
                {
                    // bicicletta: due ruote e il telaio
                    uint c = Pick(AwningColors, k);
                    foreach (float dv in new[] { 0.27f, 0.73f })
                        for (int i = 0; i < 8; i++)
                        {
                            double a0 = 2 * Math.PI * i / 8, a1 = 2 * Math.PI * (i + 1) / 8;
                            Beam(u + 0.12f, v0 + dv + (float)Math.Cos(a0) * 0.24f, g + 0.26f + (float)Math.Sin(a0) * 0.24f,
                                u + 0.12f, v0 + dv + (float)Math.Cos(a1) * 0.24f, g + 0.26f + (float)Math.Sin(a1) * 0.24f, 0.015f, 0x1202020);
                        }
                    Beam(u + 0.12f, v0 + 0.27f, g + 0.26f, u + 0.12f, v0 + 0.5f, g + 0.62f, 0.02f, c);
                    Beam(u + 0.12f, v0 + 0.5f, g + 0.62f, u + 0.12f, v0 + 0.73f, g + 0.26f, 0.02f, c);
                }
            }
        }

        // ------------------------------------------------------------------ natura

        private void Hedge(float h) => HedgeRow(0, 0, _w, _d, h);

        /// <summary>Aiuola: cordolo in pietra, terra e fiori fitti (rose rosse e rosa nel roseto).</summary>
        private void Flowerbed(string color)
        {
            float g = Base(0, 0, _w, _d);
            Curbs(0, 0, _w, _d);
            Flat(0.25f, 0.25f, _w - 0.25f, _d - 0.25f, g + 0.12f, 0x16B4E34, BuildingDecorator.KindProp, 0);
            bool rose = color == "rose";
            for (float v = 0.4f; v + 0.3f < _d - 0.3f; v += rose ? 0.9f : 0.6f)
                Foliage(0.35f, v, _w - 0.35f, v + (rose ? 0.6f : 0.45f), g + 0.1f, g + (rose ? 0.85f : 0.4f), rose ? 0x13E6A30u : 0x1557A35u, rose ? 0.85f : 1.0f);
        }

        /// <summary>Laghetto: acqua con le ninfee, bordo di sassi, canne su un lato.</summary>
        private void Pond()
        {
            Lawn(0, 0, _w, _d);
            float g = Ground(_w / 2, _d / 2);
            var c = World(_w / 2, _d / 2);
            float ru = _w / 2 - 0.6f, rv = _d / 2 - 0.6f;
            const int seg = 20;
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                var p0 = World(_w / 2 + (float)Math.Cos(a0) * ru, _d / 2 + (float)Math.Sin(a0) * rv);
                var p1 = World(_w / 2 + (float)Math.Cos(a1) * ru, _d / 2 + (float)Math.Sin(a1) * rv);
                QuadW(new Vec3(c.X, c.Y, g + 0.06f), new Vec3(p0.X, p0.Y, g + 0.06f), new Vec3(p1.X, p1.Y, g + 0.06f), new Vec3(p1.X, p1.Y, g + 0.06f),
                    new Vec3(0, 0, 1), MeshBuffer.Rgb(0x4F7F8A), BuildingDecorator.KindWater, 1);
                float su = _w / 2 + (float)Math.Cos(a0) * (ru + 0.25f), sv = _d / 2 + (float)Math.Sin(a0) * (rv + 0.25f);
                OBox(su, sv, (float)a0, 0.3f, 0.22f, Ground(su, sv) - 0.1f, Ground(su, sv) + 0.2f + 0.1f * Frac(i), 0x1A39D90);
            }
            for (int k = 0; k < 5; k++)
            {
                float u = _w * (0.3f + 0.1f * k), v = _d * (0.4f + 0.2f * Frac(k + 3));
                Flat(u - 0.3f, v - 0.3f, u + 0.3f, v + 0.3f, g + 0.07f, 0x14E8A3A, BuildingDecorator.KindPlants, 0.4f);
            }
            for (int k = 0; k < 9; k++)
            {
                float u = 0.9f + 0.25f * (k % 3), v = _d * 0.35f + 0.3f * k;
                if (v > _d - 0.5f) break;
                Beam(u, v, g, u + 0.05f, v, g + 1.0f + 0.2f * Frac(k), 0.02f, 0x16E8A3A);
            }
        }
    }
}
