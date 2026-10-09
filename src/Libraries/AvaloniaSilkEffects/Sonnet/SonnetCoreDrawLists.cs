namespace AvaloniaSilkEffects.Sonnet;

/// <summary>
/// Folia's shot geometry variants 0–13 (sonnetShotMg.ts), 18–23 (sonnetAdditionalShotMg.ts) and
/// 36–47 (sonnetOpenFrameShotMg.ts), recorded with the same AnimatedGraphics semantics: every
/// fill or stroke consumes the current path.
/// </summary>
internal static class SonnetCoreDrawLists
{
    private const double Tau = Math.PI * 2;

    internal static bool Handles(int variant) => variant is >= 0 and <= 13 or >= 18 and <= 23 or >= 36 and <= 47;

    internal static SonnetDrawList Build(int variant, double width, double height, uint seed, uint primary, uint secondary)
    {
        var g = new SonnetDrawList();
        var radius = Math.Min(width, height);
        var c = new Context(g, radius, width, height, seed, primary, secondary);
        switch (variant)
        {
            case 0: SunburstFrame(c); break;
            case 1: NestedDiamonds(c); break;
            case 2: HexagonGrid(c); break;
            case 3: Molecules(c); break;
            case 4: Orbitals(c); break;
            case 5: RingedPlanet(c); break;
            case 6: WireframeMountains(c); break;
            case 7: Radar(c); break;
            case 8: HudFrame(c); break;
            case 9: IsometricCubes(c); break;
            case 10: Constellation(c); break;
            case 11: LunarPhases(c); break;
            case 12: Lotus(c); break;
            case 13: SeedOfLife(c); break;
            case 18: ContourAtlas(c); break;
            case 19: RadialWave(c); break;
            case 20: TransitBlueprint(c); break;
            case 21: Chronograph(c); break;
            case 22: FoldedRibbons(c); break;
            case 23: HalftonePoster(c); break;
            case 36: OpenArcBrackets(c); break;
            case 37: DashedOrbits(c); break;
            case 38: OpenFragments(c); break;
            case 39: HorizonBundles(c); break;
            case 40: SemiWreath(c); break;
            case 41: SideRulers(c); break;
            case 42: DiagonalStream(c); break;
            case 43: CornerPetalSpray(c); break;
            case 44: DottedWindows(c); break;
            case 45: OpenRadar(c); break;
            case 46: BrushStrokes(c); break;
            case 47: StitchCorners(c); break;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
        return g;
    }

    private sealed record Context(SonnetDrawList G, double Radius, double Width, double Height, uint Seed, uint Primary, uint Secondary)
    {
        public (double X, double Y) Bleed => (Math.Max(Radius * 0.92, Width * 0.64), Math.Max(Radius * 0.92, Height * 0.64));
    }

    // ---- sonnetShotMg.ts ----------------------------------------------------------------

    private static void SunburstFrame(Context c)
    {
        var (g, r, p) = (c.G, c.Radius, c.Primary);
        g.Circle(0, 0, r * 0.6).Stroke(p, 6, 0.8);
        g.Circle(0, 0, r * 0.58).Stroke(p, 2, 0.4);
        for (var i = 0; i < 32; i++)
        {
            var angle = i / 32d * Tau;
            var r1 = r * (0.3 + i % 3 * 0.05);
            var r2 = r * 0.55;
            g.MoveTo(Math.Cos(angle) * r1, Math.Sin(angle) * r1).LineTo(Math.Cos(angle) * r2, Math.Sin(angle) * r2)
                .Stroke(p, 1, 0.2 + i % 2 * 0.1);
        }
    }

    private static void NestedDiamonds(Context c)
    {
        var (g, p) = (c.G, c.Primary);
        var r = c.Radius * 0.7;
        void Diamond(double s, double width, double alpha) =>
            g.MoveTo(0, -r * s).LineTo(r * s, 0).LineTo(0, r * s).LineTo(-r * s, 0).LineTo(0, -r * s).Stroke(p, width, alpha);
        Diamond(1, 6, 0.8);
        Diamond(0.96, 2, 0.4);
        Diamond(0.4, 1, 0.6);
        g.MoveTo(-r, 0).LineTo(r, 0).Stroke(p, 1, 0.3);
        g.MoveTo(0, -r).LineTo(0, r).Stroke(p, 1, 0.3);
    }

    private static void HexagonGrid(Context c)
    {
        var (g, r, p) = (c.G, c.Radius, c.Primary);
        void Hex(double hr, double width, double alpha)
        {
            g.MoveTo(hr * Math.Sin(0), -hr * Math.Cos(0));
            for (var j = 1; j <= 6; j++) g.LineTo(hr * Math.Sin(j * Math.PI / 3), -hr * Math.Cos(j * Math.PI / 3));
            g.Stroke(p, width, alpha);
        }
        Hex(r * 0.6, 6, 0.8);
        Hex(r * 0.57, 2, 0.4);
        Hex(r * 0.25, 1, 0.5);
        for (var j = 0; j < 6; j++)
        {
            var angle = j * Math.PI / 3 - Math.PI / 6;
            g.MoveTo(Math.Cos(angle) * r * 0.25, Math.Sin(angle) * r * 0.25)
                .LineTo(Math.Cos(angle) * r * 0.57, Math.Sin(angle) * r * 0.57).Stroke(p, 2, 0.4);
        }
    }

    private static void Molecules(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var molecule = SonnetVariantResolver.Molecule((int)c.Seed);
        if (molecule == 0)
        {
            var hexR = radius * 0.22;
            void Benzene(double cx, double cy, double scale, double rotation = 0)
            {
                var r = hexR * scale;
                g.MoveTo(cx + r * Math.Sin(rotation), cy - r * Math.Cos(rotation));
                for (var j = 1; j <= 6; j++)
                    g.LineTo(cx + r * Math.Sin(j * Math.PI / 3 + rotation), cy - r * Math.Cos(j * Math.PI / 3 + rotation));
                g.Stroke(p, 3, 0.8);
                for (var j = 0; j < 6; j += 2)
                {
                    var inner = r * 0.82;
                    g.MoveTo(cx + inner * Math.Sin(j * Math.PI / 3 + rotation), cy - inner * Math.Cos(j * Math.PI / 3 + rotation))
                        .LineTo(cx + inner * Math.Sin((j + 1) * Math.PI / 3 + rotation), cy - inner * Math.Cos((j + 1) * Math.PI / 3 + rotation))
                        .Stroke(p, 2, 0.5);
                }
            }
            var main = hexR * 1.2;
            Benzene(0, 0, 1.2);
            Benzene(Math.Sin(Math.PI / 3) * main * 2, 0, 1.2);
            var branch = Math.Sin(Math.PI / 3) * main * 2;
            Benzene(-Math.Sin(Math.PI / 6) * branch, -Math.Cos(Math.PI / 6) * branch, 1.2);
            g.MoveTo(0, main).LineTo(0, main + radius * 0.2).LineTo(radius * 0.15, main + radius * 0.35).Stroke(p, 2, 0.6);
        }
        else if (molecule == 1)
        {
            var hexR = radius * 0.22;
            g.MoveTo(0, -hexR);
            for (var j = 1; j <= 6; j++) g.LineTo(hexR * Math.Sin(j * Math.PI / 3), -hexR * Math.Cos(j * Math.PI / 3));
            g.Stroke(p, 3, 0.8);
            g.MoveTo(hexR * 0.8 * Math.Sin(Math.PI / 3), -hexR * 0.8 * Math.Cos(Math.PI / 3))
                .LineTo(hexR * 0.8 * Math.Sin(2 * Math.PI / 3), -hexR * 0.8 * Math.Cos(2 * Math.PI / 3)).Stroke(p, 2, 0.5);
            g.MoveTo(hexR * 0.8 * Math.Sin(4 * Math.PI / 3), -hexR * 0.8 * Math.Cos(4 * Math.PI / 3))
                .LineTo(hexR * 0.8 * Math.Sin(5 * Math.PI / 3), -hexR * 0.8 * Math.Cos(5 * Math.PI / 3)).Stroke(p, 2, 0.5);
            var px1 = hexR * Math.Sqrt(3) / 2;
            var py1 = -hexR / 2;
            var px2 = hexR * Math.Sqrt(3) / 2;
            var py2 = hexR / 2;
            var midX = px1 + hexR * 1.2;
            g.MoveTo(px1, py1).LineTo(px1 + hexR * 0.8, py1 - hexR * 0.1).LineTo(midX, 0)
                .LineTo(px2 + hexR * 0.8, py2 + hexR * 0.1).LineTo(px2, py2).Stroke(p, 3, 0.8);
            void Branch(double sx, double sy, double angle, double length, bool node)
            {
                var ex = sx + Math.Cos(angle) * length;
                var ey = sy + Math.Sin(angle) * length;
                g.MoveTo(sx, sy).LineTo(ex, ey).Stroke(p, 2, 0.6);
                if (node) g.Circle(ex, ey, 6).Stroke(p, 2, 0.8);
            }
            Branch(0, -hexR, -Math.PI / 2, radius * 0.15, true);
            Branch(-hexR * Math.Sqrt(3) / 2, hexR / 2, Math.PI * 0.8, radius * 0.2, true);
            Branch(-hexR * Math.Sqrt(3) / 2, -hexR / 2, -Math.PI * 0.8, radius * 0.15, false);
            Branch(midX, 0, 0, radius * 0.18, false);
            Branch(midX + radius * 0.18, 0, Math.PI / 4, radius * 0.1, true);
        }
        else
        {
            var segment = radius * 0.18;
            const int steps = 7;
            var x = -segment * (steps / 2d) * Math.Cos(Math.PI / 6);
            var points = new List<(double X, double Y)> { (x, 0) };
            for (var i = 0; i < steps; i++)
            {
                x += segment * Math.Cos(Math.PI / 6);
                points.Add((x, (i % 2 == 0 ? 1 : -1) * segment * Math.Sin(Math.PI / 6)));
            }
            g.MoveTo(points[0].X, points[0].Y);
            for (var i = 1; i <= steps; i++) g.LineTo(points[i].X, points[i].Y);
            g.Stroke(p, 3, 0.8);
            var nx = -Math.Sin(Math.PI / 6) * 6;
            var ny = Math.Cos(Math.PI / 6) * 6;
            g.MoveTo(points[1].X + nx, points[1].Y + ny).LineTo(points[2].X + nx, points[2].Y + ny).Stroke(p, 2, 0.5);
            for (var i = 1; i < steps; i++)
            {
                var angle = i % 2 == 0 ? Math.PI / 2 : -Math.PI / 2;
                var bx = points[i].X;
                var by = points[i].Y + Math.Sin(angle) * segment * 0.6;
                g.MoveTo(points[i].X, points[i].Y).LineTo(bx, by).Stroke(p, 2, 0.5);
                if (i % 2 != 0) g.Circle(bx, by, 5).Stroke(p, 2, 0.8);
                else g.MoveTo(bx, by).LineTo(bx + segment * 0.5, by - segment * 0.3).Stroke(p, 2, 0.5);
            }
        }
    }

    private static void Orbitals(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var r = radius * 0.7;
        for (var i = 0; i < 3; i++)
        {
            var angle = i * Math.PI / 3;
            for (var j = 0; j <= 60; j++)
            {
                var t = j * Tau / 60;
                var ex = Math.Cos(t) * r;
                var ey = Math.Sin(t) * r * 0.18;
                var rx = ex * Math.Cos(angle) - ey * Math.Sin(angle);
                var ry = ex * Math.Sin(angle) + ey * Math.Cos(angle);
                if (j == 0) g.MoveTo(rx, ry); else g.LineTo(rx, ry);
            }
            g.Stroke(p, 1, 0.3);
        }
        g.Circle(0, 0, radius * 0.05).Fill(p, 0.8);
    }

    private static void RingedPlanet(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var planet = radius * 0.25;
        // AnimatedGraphics consumes the path on fill, so the chained outline never draws.
        g.Circle(0, 0, planet).Fill(p, 0.15);
        g.MoveTo(-planet * 0.7, -planet * 0.5).QuadraticCurveTo(0, -planet * 0.2, planet * 0.7, -planet * 0.5).Stroke(p, 1, 0.4);
        g.MoveTo(-planet * 0.9, 0).QuadraticCurveTo(0, planet * 0.3, planet * 0.9, 0).Stroke(p, 1, 0.4);
        const double tilt = Math.PI / 6;
        void TiltedEllipse(double rx, double ry, double width, double alpha)
        {
            for (var j = 0; j <= 60; j++)
            {
                var t = j * Tau / 60;
                var ex = Math.Cos(t) * rx;
                var ey = Math.Sin(t) * ry;
                var x = ex * Math.Cos(tilt) - ey * Math.Sin(tilt);
                var y = ex * Math.Sin(tilt) + ey * Math.Cos(tilt);
                if (j == 0) g.MoveTo(x, y); else g.LineTo(x, y);
            }
            g.Stroke(p, width, alpha);
        }
        var ringX = radius * 0.6;
        var ringY = radius * 0.15;
        TiltedEllipse(ringX, ringY, 4, 0.5);
        TiltedEllipse(ringX * 1.1, ringY * 1.15, 1, 0.3);
        TiltedEllipse(ringX * 1.25, ringY * 1.3, 2, 0.2);
        g.Circle(0, 0, radius * 0.7).Stroke(p, 1, 0.2);
        g.Circle(Math.Cos(Math.PI / 4) * radius * 0.7, Math.Sin(Math.PI / 4) * radius * 0.7, 8).Fill(p, 0.6);
    }

    private static void WireframeMountains(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var bleed = c.Bleed;
        var w = bleed.X * 1.08;
        var h = radius * 0.8;
        var baseY = radius * 0.2;
        g.Circle(0, baseY - h * 0.6, radius * 0.3).Stroke(p, 2, 0.4);
        for (var i = 0; i < 5; i++)
            g.MoveTo(-bleed.X, baseY - h * 0.6 + i * 15).LineTo(bleed.X, baseY - h * 0.6 + i * 15).Stroke(p, 1, 0.3);
        const int peaks = 7;
        for (var layer = 0; layer < 3; layer++)
        {
            var layerW = w * (1 + layer * 0.2);
            var layerH = h * (0.5 + layer * 0.25);
            g.MoveTo(-layerW / 2, baseY);
            for (var i = 1; i < peaks; i++)
                g.LineTo(-layerW / 2 + layerW / peaks * i,
                    baseY - layerH * (0.3 + 0.7 * Math.Abs(Math.Sin(c.Seed + layer * 11 + i * 7))));
            g.LineTo(layerW / 2, baseY);
            g.Stroke(p, 3 - layer, 0.6 - layer * 0.15);
        }
        g.MoveTo(-w, baseY).LineTo(w, baseY).Stroke(p, 4, 0.8);
        for (var i = 0; i < 5; i++)
        {
            var y = baseY + Math.Pow(i, 1.5) * 12;
            g.MoveTo(-w, y).LineTo(w, y).Stroke(p, 1, 0.4 - i * 0.08);
        }
        for (var i = -4; i <= 4; i++)
            g.MoveTo(i * radius * 0.2, baseY).LineTo(i * bleed.X * 0.32, bleed.Y).Stroke(p, 1, 0.3);
    }

    private static void Radar(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        for (var i = 1; i <= 6; i++)
            g.Circle(0, 0, radius * 0.15 * i).Stroke(p, i % 2 == 0 ? 2 : 1, 0.2 + i % 3 * 0.1);
        g.MoveTo(-radius * 0.9, 0).LineTo(radius * 0.9, 0).Stroke(p, 1, 0.4);
        g.MoveTo(0, -radius * 0.9).LineTo(0, radius * 0.9).Stroke(p, 1, 0.4);
        g.MoveTo(0, 0).Arc(0, 0, radius * 0.75, 0, Math.PI / 4).LineTo(0, 0).Fill(p, 0.1);
        var outer = radius * 0.8;
        for (var i = 0; i < 72; i++)
        {
            var angle = i / 72d * Tau;
            var length = i % 18 == 0 ? 20 : i % 6 == 0 ? 10 : 5;
            g.MoveTo(Math.Cos(angle) * outer, Math.Sin(angle) * outer)
                .LineTo(Math.Cos(angle) * (outer + length), Math.Sin(angle) * (outer + length)).Stroke(p, 1, 0.4);
        }
        var lockAngle = c.Seed % 360 * Math.PI / 180;
        var lx = Math.Cos(lockAngle) * radius * 0.45;
        var ly = Math.Sin(lockAngle) * radius * 0.45;
        g.Rectangle(lx - 15, ly - 15, 30, 30).Stroke(p, 2, 0.8);
        g.MoveTo(lx, ly - 20).LineTo(lx, ly + 20).Stroke(p, 1, 0.6);
        g.MoveTo(lx - 20, ly).LineTo(lx + 20, ly).Stroke(p, 1, 0.6);
    }

    private static void HudFrame(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var fw = radius * 0.85;
        var fh = radius * 0.65;
        var bracket = radius * 0.15;
        void Bracket(double cx, double cy, double sx, double sy)
        {
            g.MoveTo(cx - sx * bracket, cy).LineTo(cx, cy).LineTo(cx, cy - sy * bracket).Stroke(p, 3, 0.7);
            g.MoveTo(cx - sx * bracket * 0.8, cy - sy * 8).LineTo(cx - sx * 8, cy - sy * 8)
                .LineTo(cx - sx * 8, cy - sy * bracket * 0.8).Stroke(p, 1, 0.4);
        }
        Bracket(-fw, -fh, -1, -1);
        Bracket(fw, -fh, 1, -1);
        Bracket(-fw, fh, -1, 1);
        Bracket(fw, fh, 1, 1);
        for (var i = -fw + 20; i < fw - 20; i += 20)
        {
            // JS `%` keeps the sign and works on the fractional value, like C#'s double remainder.
            var tall = i % 60 == 0;
            g.MoveTo(i, -fh).LineTo(i, -fh - (tall ? 12 : 6)).Stroke(p, 1, 0.5);
            g.MoveTo(i, fh).LineTo(i, fh + (tall ? 12 : 6)).Stroke(p, 1, 0.5);
        }
        g.Circle(0, 0, radius * 0.1).Stroke(p, 2, 0.4);
        g.MoveTo(-radius * 0.15, 0).LineTo(radius * 0.15, 0).Stroke(p, 1, 0.4);
        g.MoveTo(0, -radius * 0.15).LineTo(0, radius * 0.15).Stroke(p, 1, 0.4);
        g.Rectangle(-fw, -fh + 20, 10, 40).Fill(p, 0.5);
        g.Rectangle(-fw, -fh + 65, 10, 15).Fill(p, 0.3);
        g.Rectangle(fw - 10, fh - 60, 10, 40).Fill(p, 0.5);
    }

    private static void IsometricCubes(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        void Cube(double cx, double cy, double size, double alpha)
        {
            var dy = size * 0.5;
            var dx = size * 0.866;
            g.MoveTo(cx, cy - size).LineTo(cx + dx, cy - dy).LineTo(cx, cy).LineTo(cx - dx, cy - dy).LineTo(cx, cy - size)
                .Fill(p, alpha * 0.15);
            g.MoveTo(cx, cy).LineTo(cx + dx, cy - dy).LineTo(cx + dx, cy + size - dy).LineTo(cx, cy + size).LineTo(cx, cy)
                .Fill(p, alpha * 0.3);
            g.MoveTo(cx, cy).LineTo(cx - dx, cy - dy).LineTo(cx - dx, cy + size - dy).LineTo(cx, cy + size).LineTo(cx, cy)
                .Fill(p, alpha * 0.05);
        }
        Cube(0, 0, radius * 0.35, 0.8);
        Cube(radius * 0.4, -radius * 0.15, radius * 0.2, 0.5);
        Cube(-radius * 0.45, radius * 0.25, radius * 0.25, 0.6);
        Cube(0, radius * 0.45, radius * 0.15, 0.4);
        g.MoveTo(0, 0).LineTo(radius * 0.4, -radius * 0.15).Stroke(p, 1, 0.3);
        g.MoveTo(0, 0).LineTo(-radius * 0.45, radius * 0.25).Stroke(p, 1, 0.3);
    }

    private static void Constellation(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        g.Circle(0, 0, radius * 0.75).Stroke(p, 1, 0.2);
        g.Circle(0, 0, radius * 0.73).Stroke(p, 2, 0.1);
        var seed = (double)c.Seed;
        var nodes = new (double X, double Y)[18];
        for (var i = 0; i < nodes.Length; i++)
        {
            var r = radius * (0.1 + (seed * 17 + i * 23) % 65 / 100);
            var angle = (seed * 11 + i * 37) % 360 * Math.PI / 180;
            nodes[i] = (Math.Cos(angle) * r, Math.Sin(angle) * r);
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            g.Circle(nodes[i].X, nodes[i].Y, 3).Fill(p, 0.7);
            g.Circle(nodes[i].X, nodes[i].Y, 6).Stroke(p, 1, 0.3);
            for (var j = i + 1; j < nodes.Length; j++)
            {
                var distance = Math.Sqrt(Math.Pow(nodes[i].X - nodes[j].X, 2) + Math.Pow(nodes[i].Y - nodes[j].Y, 2));
                if (distance < radius * 0.45)
                    g.MoveTo(nodes[i].X, nodes[i].Y).LineTo(nodes[j].X, nodes[j].Y)
                        .Stroke(p, 1, 0.4 * (1 - distance / (radius * 0.45)));
            }
        }
    }

    private static void LunarPhases(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var moon = radius * 0.4;
        g.MoveTo(0, -moon).Arc(0, 0, moon, -Math.PI / 2, Math.PI / 2).QuadraticCurveTo(-moon * 0.4, 0, 0, -moon).Fill(p, 0.8);
        g.Circle(0, 0, moon).Stroke(p, 1, 0.3);
        var orbit = radius * 0.65;
        g.Circle(0, 0, orbit).Stroke(p, 1, 0.2);
        for (var i = 0; i < 8; i++)
        {
            var angle = i / 8d * Tau - Math.PI / 2;
            var mx = Math.Cos(angle) * orbit;
            var my = Math.Sin(angle) * orbit;
            g.Circle(mx, my, 8).Stroke(p, 1, 0.5);
            if (i == 4) g.Circle(mx, my, 6).Fill(p, 0.8);
            else if (i != 0)
                g.MoveTo(mx, my - 6).Arc(mx, my, 6, -Math.PI / 2, Math.PI / 2, i > 4).LineTo(mx, my - 6).Fill(p, 0.5);
        }
        void Star(double sx, double sy, double sr) =>
            g.MoveTo(sx, sy - sr).LineTo(sx + sr * 0.2, sy - sr * 0.2).LineTo(sx + sr, sy).LineTo(sx + sr * 0.2, sy + sr * 0.2)
                .LineTo(sx, sy + sr).LineTo(sx - sr * 0.2, sy + sr * 0.2).LineTo(sx - sr, sy).LineTo(sx - sr * 0.2, sy - sr * 0.2)
                .Fill(p, 0.7);
        Star(-radius * 0.4, -radius * 0.5, 12);
        Star(radius * 0.5, -radius * 0.3, 8);
        Star(-radius * 0.2, radius * 0.5, 15);
    }

    private static void Lotus(Context c)
    {
        var (g, radius, p) = (c.G, c.Radius, c.Primary);
        var petalLength = radius * 0.5;
        void Petal(double angle, double length, double spread, double alpha)
        {
            var control = length * 0.5;
            g.MoveTo(0, 0)
                .QuadraticCurveTo(Math.Cos(angle - spread) * control, Math.Sin(angle - spread) * control,
                    Math.Cos(angle) * length, Math.Sin(angle) * length)
                .QuadraticCurveTo(Math.Cos(angle + spread) * control, Math.Sin(angle + spread) * control, 0, 0)
                .Stroke(p, 1, alpha);
            // The stroke consumed the path, so the follow-up fill has nothing to paint.
        }
        for (var layer = 0; layer < 4; layer++)
        {
            var petals = 8 + layer * 4;
            for (var i = 0; i < petals; i++)
                Petal(i / (double)petals * Tau + layer * (Math.PI / petals), petalLength * (1 - layer * 0.2),
                    0.3 - layer * 0.05, 0.8 - layer * 0.15);
        }
        g.Circle(0, 0, radius * 0.08).Stroke(p, 2, 0.8);
        g.Circle(0, 0, radius * 0.03).Fill(p, 0.9);
        var stem = radius * 0.65;
        for (var i = 0; i < 3; i++)
        {
            var start = i / 3d * Tau;
            g.MoveTo(Math.Cos(start) * stem, Math.Sin(start) * stem)
                .BezierCurveTo(Math.Cos(start + 1) * stem * 1.2, Math.Sin(start + 1) * stem * 1.2,
                    Math.Cos(start + 2) * stem * 0.8, Math.Sin(start + 2) * stem * 0.8,
                    Math.Cos(start + 3) * stem, Math.Sin(start + 3) * stem)
                .Stroke(p, 1, 0.4);
            g.Circle(Math.Cos(start + 1.5) * stem * 1.05, Math.Sin(start + 1.5) * stem * 1.05, 4).Fill(p, 0.6);
        }
    }

    private static void SeedOfLife(Context c)
    {
        var (g, p, s) = (c.G, c.Primary, c.Secondary);
        var r = c.Radius * 0.25;
        g.Circle(0, 0, r).Stroke(p, 1.5, 0.35);
        for (var i = 0; i < 6; i++)
        {
            var angle = i / 6d * Tau;
            g.Circle(Math.Cos(angle) * r, Math.Sin(angle) * r, r).Stroke(p, 1.5, 0.2);
        }
        for (var i = 0; i < 12; i++)
        {
            var angle = i / 12d * Tau;
            var distance = i % 2 == 0 ? r * 2 : r * Math.Sqrt(3);
            g.Circle(Math.Cos(angle) * distance, Math.Sin(angle) * distance, r).Stroke(s, 1, 0.1);
        }
        g.Circle(0, 0, r * 3).Stroke(p, 1.5, 0.2);
        g.Circle(0, 0, r * 3.1).Stroke(s, 1, 0.08);
        for (var i = 0; i < 12; i++)
        {
            var angle = i / 12d * Tau;
            g.MoveTo(Math.Cos(angle) * r * 0.5, Math.Sin(angle) * r * 0.5)
                .LineTo(Math.Cos(angle) * r * 3, Math.Sin(angle) * r * 3).Stroke(s, 1, 0.05);
        }
    }

    // ---- sonnetAdditionalShotMg.ts ------------------------------------------------------

    private static void ContourAtlas(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, (double)c.Seed);
        for (var ring = 0; ring < 8; ring++)
        {
            var baseRadius = radius * (0.16 + ring * 0.075);
            for (var step = 0; step <= 48; step++)
            {
                var angle = step / 48d * Tau;
                var ripple = Math.Sin(angle * 3 + seed * 0.07 + ring) * radius * 0.018
                    + Math.Cos(angle * 5 - seed * 0.03 + ring * 0.7) * radius * 0.012;
                var x = Math.Cos(angle) * (baseRadius + ripple) + Math.Sin(ring * 1.7) * radius * 0.055;
                var y = Math.Sin(angle) * (baseRadius + ripple) * 0.72 + Math.Cos(ring * 1.3) * radius * 0.035;
                if (step == 0) g.MoveTo(x, y); else g.LineTo(x, y);
            }
            g.Stroke(ring % 3 == 0 ? s : p, ring % 3 == 0 ? 2 : 1, 0.2 + ring * 0.045);
        }
        g.MoveTo(-radius * 0.78, radius * 0.52).LineTo(-radius * 0.58, radius * 0.52)
            .LineTo(-radius * 0.58, radius * 0.46).LineTo(-radius * 0.38, radius * 0.46).Stroke(p, 3, 0.64);
    }

    private static void RadialWave(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, (double)c.Seed);
        for (var index = 0; index < 64; index++)
        {
            var angle = index / 64d * Tau;
            var signal = 0.5 + 0.5 * Math.Sin(index * 1.83 + seed * 0.11);
            var inner = radius * (0.29 + signal * 0.035);
            var outer = radius * (0.43 + signal * 0.21);
            g.MoveTo(Math.Cos(angle) * inner, Math.Sin(angle) * inner).LineTo(Math.Cos(angle) * outer, Math.Sin(angle) * outer)
                .Stroke(index % 8 == 0 ? s : p, index % 8 == 0 ? 3 : 1, index % 2 == 0 ? 0.58 : 0.32);
        }
        g.Circle(0, 0, radius * 0.24).Stroke(p, 5, 0.7);
        g.Circle(0, 0, radius * 0.68).Stroke(s, 1, 0.18);
        g.Circle(0, 0, radius * 0.72).Stroke(p, 2, 0.12);
    }

    private static void TransitBlueprint(Context c)
    {
        var (g, radius, p, s) = (c.G, c.Radius, c.Primary, c.Secondary);
        var direction = c.Seed % 2 == 0 ? 1 : -1;
        var bleed = c.Bleed;
        double[][][] routes =
        [
            [[-0.72, -0.38], [-0.38, -0.38], [-0.38, 0.08], [0.08, 0.08], [0.08, 0.52], [0.68, 0.52]],
            [[-0.62, 0.58], [-0.62, 0.24], [-0.14, 0.24], [-0.14, -0.52], [0.5, -0.52], [0.5, -0.2], [0.74, -0.2]],
            [[-0.78, -0.06], [-0.5, -0.06], [-0.5, -0.62], [0.24, -0.62], [0.24, 0.3], [0.72, 0.3]],
        ];
        for (var routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            var route = routes[routeIndex];
            g.MoveTo(-bleed.X * direction, route[0][1] * radius);
            foreach (var point in route) g.LineTo(point[0] * radius * direction, point[1] * radius);
            g.LineTo(bleed.X * direction, route[^1][1] * radius);
            g.Stroke(routeIndex == 1 ? s : p, routeIndex == 0 ? 5 : 2, 0.34 + routeIndex * 0.12);
            for (var pointIndex = 0; pointIndex < route.Length; pointIndex++)
            {
                if (pointIndex != 0 && pointIndex != route.Length - 1 && (pointIndex + routeIndex) % 2 != 0) continue;
                var px = route[pointIndex][0] * radius * direction;
                var py = route[pointIndex][1] * radius;
                g.Circle(px, py, pointIndex == 0 ? 10 : 6).Fill(pointIndex % 2 == 0 ? p : s, 0.72);
                g.Circle(px, py, pointIndex == 0 ? 16 : 11).Stroke(p, 1, 0.36);
            }
        }
    }

    private static void Chronograph(Context c)
    {
        var (g, radius, p, s) = (c.G, c.Radius, c.Primary, c.Secondary);
        double[] rings = [0.2, 0.38, 0.62];
        for (var index = 0; index < rings.Length; index++)
            g.Circle(0, 0, radius * rings[index]).Stroke(index == 1 ? s : p, index == 2 ? 4 : 2, 0.3 + index * 0.13);
        for (var tick = 0; tick < 48; tick++)
        {
            var angle = tick / 48d * Tau;
            var outer = radius * 0.72;
            var inner = outer - radius * (tick % 4 == 0 ? 0.11 : 0.045);
            g.MoveTo(Math.Cos(angle) * inner, Math.Sin(angle) * inner).LineTo(Math.Cos(angle) * outer, Math.Sin(angle) * outer)
                .Stroke(tick % 4 == 0 ? s : p, tick % 4 == 0 ? 3 : 1, 0.5);
        }
        var hand = c.Seed % 60 / 60d * Tau - Math.PI / 2;
        var second = (ulong)c.Seed * 7 % 60 / 60d * Tau - Math.PI / 2;
        g.MoveTo(-Math.Cos(hand) * radius * 0.12, -Math.Sin(hand) * radius * 0.12)
            .LineTo(Math.Cos(hand) * radius * 0.55, Math.Sin(hand) * radius * 0.55).Stroke(p, 6, 0.74);
        g.MoveTo(0, 0).LineTo(Math.Cos(second) * radius * 0.65, Math.Sin(second) * radius * 0.65).Stroke(s, 2, 0.72);
        g.Circle(0, 0, radius * 0.055).Fill(p, 0.85);
    }

    private static void FoldedRibbons(Context c)
    {
        var (g, radius, p, s) = (c.G, c.Radius, c.Primary, c.Secondary);
        var direction = c.Seed % 2 == 0 ? 1 : -1;
        var bleed = c.Bleed;
        for (var band = 0; band < 5; band++)
        {
            var y = (-0.5 + band * 0.25) * radius;
            var offset = (band % 2 == 0 ? 1 : -1) * direction;
            g.MoveTo(-bleed.X, y)
                .BezierCurveTo(-radius * 0.38, y - radius * 0.28 * offset, radius * 0.18, y + radius * 0.28 * offset, bleed.X, y)
                .Stroke(band % 2 == 0 ? p : s, band == 2 ? 12 : 5, 0.24 + band * 0.08);
            var shift = radius * 0.055;
            g.MoveTo(-bleed.X, y + shift)
                .BezierCurveTo(-radius * 0.38, y - radius * 0.28 * offset + shift, radius * 0.18, y + radius * 0.28 * offset + shift,
                    bleed.X, y + shift)
                .Stroke(p, 1, 0.3);
        }
        g.MoveTo(-radius * 0.34, -bleed.Y).LineTo(-radius * 0.34, -radius * 0.18).Stroke(p, 2, 0.18);
        g.MoveTo(radius * 0.34, radius * 0.18).LineTo(radius * 0.34, bleed.Y).Stroke(p, 2, 0.18);
    }

    private static void HalftonePoster(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, (double)c.Seed);
        var spacing = radius * 0.17;
        var bleed = c.Bleed;
        var columns = (int)Math.Ceiling(bleed.X * 2 / spacing) + 2;
        var rows = (int)Math.Ceiling(bleed.Y * 2 / spacing) + 2;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = (column - (columns - 1) / 2d) * spacing;
                var y = (row - (rows - 1) / 2d) * spacing;
                var distance = Math.Sqrt(x * x + y * y) / radius;
                var pulse = 0.5 + 0.5 * Math.Sin(column * 0.9 + row * 1.4 + seed * 0.08);
                var dot = radius * (0.009 + Math.Max(0, 0.72 - distance) * 0.034 + pulse * 0.012);
                g.Circle(x, y, dot).Fill((row + column) % 5 == 0 ? s : p, 0.24 + pulse * 0.5);
            }
        }
        g.MoveTo(-bleed.X, -bleed.Y * 0.72).LineTo(bleed.X, -bleed.Y * 0.72).Stroke(s, 2, 0.28);
        g.MoveTo(-bleed.X * 0.58, -bleed.Y).LineTo(-bleed.X * 0.58, bleed.Y).Stroke(p, 1, 0.2);
    }

    // ---- sonnetOpenFrameShotMg.ts -------------------------------------------------------

    private static (double X, double Y) Rotate(double x, double y, double angle) =>
        (x * Math.Cos(angle) - y * Math.Sin(angle), x * Math.Sin(angle) + y * Math.Cos(angle));

    private static void OpenArcBrackets(Context c)
    {
        var (g, radius, p, s) = (c.G, c.Radius, c.Primary, c.Secondary);
        var bx = c.Width * 0.34;
        var by = c.Height * 0.34;
        var arc = radius * 0.17;
        (double X, double Y, double Start, double End)[] corners =
        [
            (-bx, -by, Math.PI, Math.PI * 1.5), (bx, -by, -Math.PI / 2, 0),
            (bx, by, 0, Math.PI / 2), (-bx, by, Math.PI / 2, Math.PI),
        ];
        for (var index = 0; index < corners.Length; index++)
        {
            var corner = corners[index];
            g.Arc(corner.X, corner.Y, arc, corner.Start, corner.End).Stroke(index % 2 == 1 ? s : p, 3, 0.6);
            g.Arc(corner.X, corner.Y, arc * 0.72, corner.Start, corner.End).Stroke(p, 1, 0.3);
            g.MoveTo(corner.X - 5, corner.Y).LineTo(corner.X + 5, corner.Y).Stroke(p, 1, 0.5);
            g.MoveTo(corner.X, corner.Y - 5).LineTo(corner.X, corner.Y + 5).Stroke(p, 1, 0.5);
        }
        var dot = c.Seed % 8 * Tau / 8;
        g.Circle(Math.Cos(dot) * radius * 0.5, Math.Sin(dot) * radius * 0.5, radius * 0.02).Fill(s, 0.7);
    }

    private static void DashedOrbits(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, (double)c.Seed);
        for (var ring = 0; ring < 3; ring++)
        {
            var ringRadius = radius * (0.32 + ring * 0.18);
            var dashes = 12 + ring * 4;
            var offset = seed * 0.13 + ring * 0.7;
            var span = Tau / dashes * 0.55;
            for (var dash = 0; dash < dashes; dash++)
            {
                var start = offset + dash / (double)dashes * Tau;
                g.Arc(0, 0, ringRadius, start, start + span).Stroke(ring == 1 ? s : p, ring == 1 ? 1 : 2, 0.28 + ring * 0.06);
            }
        }
        var marker = seed * 0.31;
        g.Circle(Math.Cos(marker) * radius * 0.68, Math.Sin(marker) * radius * 0.68, radius * 0.022).Fill(p, 0.75);
        g.Circle(Math.Cos(marker + Math.PI) * radius * 0.5, Math.Sin(marker + Math.PI) * radius * 0.5, radius * 0.016).Fill(s, 0.6);
        g.Circle(0, 0, radius * 0.05).Stroke(p, 1.5, 0.5);
    }

    private static void OpenFragments(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, c.Seed);
        for (var index = 0; index < 5; index++)
        {
            var angle = index / 5d * Tau + seed * 0.05;
            var distance = radius * (0.42 + (seed + index * 7) % 30 / 100d);
            var cx = Math.Cos(angle) * distance;
            var cy = Math.Sin(angle) * distance * 0.8;
            var size = radius * (0.07 + (seed + index * 13) % 20 / 200d);
            var rotation = seed * 0.11 + index * 0.9;
            (double X, double Y)[] points =
                [Rotate(-size, -size, rotation), Rotate(size, -size, rotation), Rotate(size, size, rotation), Rotate(-size, size, rotation)];
            g.MoveTo(cx + points[0].X, cy + points[0].Y);
            for (var point = 1; point < points.Length; point++) g.LineTo(cx + points[point].X, cy + points[point].Y);
            g.Stroke(index % 2 == 1 ? s : p, 2, 0.5);
            if (index == seed % 5) g.Circle(cx, cy, size * 0.28).Fill(p, 0.35);
        }
    }

    private static void HorizonBundles(Context c)
    {
        var (g, width, height, p, s, seed) = (c.G, c.Width, c.Height, c.Primary, c.Secondary, c.Seed);
        (double BaseY, int Drift)[] bundles = [(-height * 0.28, 1), (height * 0.3, -1)];
        for (var bundleIndex = 0; bundleIndex < bundles.Length; bundleIndex++)
        {
            var bundle = bundles[bundleIndex];
            for (var line = 0; line < 3; line++)
            {
                var y = bundle.BaseY + line * 10 * bundle.Drift;
                var breakAt = width * (((seed + line * 17 + bundleIndex * 31) % 40 + 30) / 100d - 0.5);
                var gap = width * 0.045;
                var color = line == 1 ? s : p;
                g.MoveTo(-width * 0.4, y).LineTo(breakAt - gap, y).Stroke(color, line == 1 ? 2 : 1, 0.42 - line * 0.08);
                g.MoveTo(breakAt + gap, y).LineTo(width * 0.4, y).Stroke(color, line == 1 ? 2 : 1, 0.42 - line * 0.08);
            }
            g.MoveTo(-width * 0.42, bundle.BaseY).LineTo(-width * 0.42 + 6, bundle.BaseY).Stroke(p, 1, 0.4);
        }
        g.Rectangle(width * 0.36, -height * 0.28 - 3, width * 0.04, 6).Fill(s, 0.5);
    }

    private static void SemiWreath(Context c)
    {
        var (g, radius, p, s) = (c.G, c.Radius, c.Primary, c.Secondary);
        var opening = c.Seed % 4 * (Math.PI / 2) + Math.PI / 8;
        var span = Tau * 0.75;
        var outer = radius * 0.58;
        var inner = radius * 0.5;
        g.Arc(0, 0, outer, opening, opening + span).Stroke(p, 2.5, 0.55);
        g.Arc(0, 0, inner, opening, opening + span).Stroke(s, 1, 0.3);
        for (var index = 0; index < 18; index++)
        {
            var angle = opening + index / 18d * span;
            g.MoveTo(Math.Cos(angle) * outer, Math.Sin(angle) * outer)
                .LineTo(Math.Cos(angle) * (outer + radius * 0.05), Math.Sin(angle) * (outer + radius * 0.05)).Stroke(p, 1, 0.4);
            if (index % 6 == 0) g.Circle(Math.Cos(angle) * inner, Math.Sin(angle) * inner, radius * 0.016).Fill(s, 0.6);
        }
    }

    private static void SideRulers(Context c)
    {
        var (g, width, height, p, s, seed) = (c.G, c.Width, c.Height, c.Primary, c.Secondary, c.Seed);
        foreach (var side in new[] { -1, 1 })
        {
            var x = side * width * 0.36;
            g.MoveTo(x, -height * 0.3).LineTo(x, height * 0.3).Stroke(p, 1, 0.35);
            for (var tick = 0; tick < 12; tick++)
            {
                var y = -height * 0.3 + tick / 11d * height * 0.6;
                g.MoveTo(x, y).LineTo(x - side * (tick % 4 == 0 ? 18 : 9), y).Stroke(tick % 4 == 0 ? s : p, 1, 0.45);
            }
            var accentY = -height * 0.3 + (seed + (side > 0 ? 5 : 0)) % 11 / 11d * height * 0.6;
            g.Rectangle(side > 0 ? x : x - 4, accentY - 4, 4, 8).Fill(s, 0.6);
        }
    }

    private static void DiagonalStream(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, c.Seed);
        var angle = Math.PI / 4 + seed % 3 * (Math.PI / 12);
        var dirX = Math.Cos(angle);
        var dirY = Math.Sin(angle);
        var normalX = -dirY;
        var normalY = dirX;
        for (var line = 0; line < 7; line++)
        {
            var offset = (line - 3) * radius * 0.16;
            var length = radius * (0.55 + (seed + line * 29) % 40 / 100d);
            var cx = normalX * offset;
            var cy = normalY * offset;
            g.MoveTo(cx - dirX * length, cy - dirY * length).LineTo(cx + dirX * length * 0.6, cy + dirY * length * 0.6)
                .Stroke(line == 3 ? s : p, line == 3 ? 2 : 1, 0.22 + line * 0.03);
        }
        double[] offsets = [-radius * 0.16, radius * 0.16];
        for (var index = 0; index < offsets.Length; index++)
        {
            var cx = normalX * offsets[index] + dirX * radius * 0.1;
            var cy = normalY * offsets[index] + dirY * radius * 0.1;
            var size = radius * 0.07;
            (double X, double Y)[] points =
                [Rotate(0, -size, angle), Rotate(size, 0, angle), Rotate(0, size, angle), Rotate(-size, 0, angle)];
            g.MoveTo(cx + points[0].X, cy + points[0].Y);
            for (var point = 1; point < points.Length; point++) g.LineTo(cx + points[point].X, cy + points[point].Y);
            g.Stroke(p, 1.5, 0.55);
            if (index == 0) g.Circle(cx, cy, size * 0.22).Fill(s, 0.55);
        }
    }

    private static void CornerPetalSpray(Context c)
    {
        var (g, width, height, radius, p, s) = (c.G, c.Width, c.Height, c.Radius, c.Primary, c.Secondary);
        var sign = c.Seed % 2 == 0 ? 1 : -1;
        (double X, double Y, double Base)[] corners =
        [
            (-sign * width * 0.28, -height * 0.26, Math.Atan2(1, sign)),
            (sign * width * 0.28, height * 0.26, Math.Atan2(-1, -sign)),
        ];
        for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
        {
            var corner = corners[cornerIndex];
            for (var petal = 0; petal < 5; petal++)
            {
                var angle = corner.Base + (petal - 2) * 0.3;
                var length = radius * (0.3 - Math.Abs(petal - 2) * 0.045);
                g.MoveTo(corner.X, corner.Y)
                    .QuadraticCurveTo(corner.X + Math.Cos(angle + 0.35) * length * 0.55, corner.Y + Math.Sin(angle + 0.35) * length * 0.55,
                        corner.X + Math.Cos(angle) * length, corner.Y + Math.Sin(angle) * length)
                    .Stroke(petal == 2 ? s : p, petal == 2 ? 2 : 1, 0.4);
            }
            if (cornerIndex == 0) g.Circle(corner.X, corner.Y, radius * 0.02).Fill(p, 0.6);
        }
    }

    private static void DottedWindows(Context c)
    {
        var (g, width, height, radius, p, s, seed) = (c.G, c.Width, c.Height, c.Radius, c.Primary, c.Secondary, c.Seed);
        const int columns = 7;
        const int rows = 5;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if ((row * columns + column + seed) % 5 == 0) continue;
                g.Circle((column - (columns - 1) / 2d) * width * 0.11, (row - (rows - 1) / 2d) * height * 0.14, 1.5).Fill(p, 0.35);
            }
        }
        var arm = radius * 0.1;
        for (var window = 0; window < 3; window++)
        {
            var cellX = ((seed + window * 2) % columns - (columns - 1) / 2d) * width * 0.11;
            var cellY = ((seed + window * 3 + 1) % rows - (rows - 1) / 2d) * height * 0.14;
            var flipX = (seed + window) % 2 == 0 ? 1 : -1;
            var flipY = (seed + window * 2) % 2 == 0 ? 1 : -1;
            g.MoveTo(cellX + flipX * arm, cellY).LineTo(cellX, cellY).LineTo(cellX, cellY + flipY * arm).Stroke(s, 2, 0.6);
        }
    }

    private static void OpenRadar(Context c)
    {
        var (g, radius, p, s, seed) = (c.G, c.Radius, c.Primary, c.Secondary, (double)c.Seed);
        var span = Tau * 0.56;
        for (var ring = 0; ring < 3; ring++)
        {
            var start = seed * 0.1 + ring * 0.9;
            g.Arc(0, 0, radius * (0.3 + ring * 0.18), start, start + span)
                .Stroke(ring == 1 ? s : p, ring == 1 ? 2 : 1, 0.35 + ring * 0.06);
        }
        var sweep = seed * 0.07;
        g.MoveTo(0, 0).LineTo(Math.Cos(sweep) * radius * 0.66, Math.Sin(sweep) * radius * 0.66).Stroke(p, 1.5, 0.5);
        var lockX = Math.Cos(sweep + 0.8) * radius * 0.48;
        var lockY = Math.Sin(sweep + 0.8) * radius * 0.48;
        var tick = radius * 0.04;
        g.MoveTo(lockX - tick * 2, lockY - tick).LineTo(lockX - tick * 2, lockY - tick * 2).LineTo(lockX - tick, lockY - tick * 2)
            .Stroke(s, 1.5, 0.7);
        g.MoveTo(lockX + tick * 2, lockY + tick).LineTo(lockX + tick * 2, lockY + tick * 2).LineTo(lockX + tick, lockY + tick * 2)
            .Stroke(s, 1.5, 0.7);
        g.Circle(0, 0, radius * 0.018).Fill(p, 0.8);
        g.Circle(lockX, lockY, radius * 0.014).Fill(s, 0.7);
    }

    private static void BrushStrokes(Context c)
    {
        var (g, width, height, radius, p, s, seed) = (c.G, c.Width, c.Height, c.Radius, c.Primary, c.Secondary, c.Seed);
        for (var stroke = 0; stroke < 4; stroke++)
        {
            var horizontal = stroke % 2 == 0;
            var along = ((seed + stroke * 23) % 50 / 100d - 0.25) * (horizontal ? width : height);
            var across = ((seed + stroke * 41) % 60 / 100d - 0.3) * (horizontal ? height : width);
            var length = radius * (0.3 + (seed + stroke * 7) % 25 / 100d);
            var x1 = horizontal ? along - length : across;
            var y1 = horizontal ? across : along - length;
            var x2 = horizontal ? along + length : across;
            var y2 = horizontal ? across : along + length;
            g.MoveTo(x1, y1).LineTo(x2, y2).Stroke(stroke == 1 ? s : p, radius * 0.04, 0.22);
            g.MoveTo(x1, y1 + (horizontal ? radius * 0.035 : 0))
                .LineTo(horizontal ? x2 * 0.6 : x2, horizontal ? y2 + radius * 0.035 : y2 * 0.6)
                .Stroke(p, radius * 0.012, 0.45);
        }
        var size = radius * 0.05;
        var anchorX = width * 0.3;
        var anchorY = -height * 0.3;
        g.MoveTo(anchorX - size, anchorY - size).LineTo(anchorX + size, anchorY - size).LineTo(anchorX + size, anchorY + size)
            .Stroke(s, 1.5, 0.6);
        g.Rectangle(-anchorX - size / 2, -anchorY - size / 2, size, size).Fill(p, 0.4);
    }

    private static void StitchCorners(Context c)
    {
        var (g, width, height, radius, p, s) = (c.G, c.Width, c.Height, c.Radius, c.Primary, c.Secondary);
        var arm = radius * 0.18;
        var dash = radius * 0.03;
        (double X, double Y, int Sx, int Sy)[] corners =
        [
            (-width * 0.39, -height * 0.36, 1, 1), (width * 0.39, -height * 0.36, -1, 1),
            (width * 0.39, height * 0.36, -1, -1), (-width * 0.39, height * 0.36, 1, -1),
        ];
        for (var index = 0; index < corners.Length; index++)
        {
            var corner = corners[index];
            var color = index % 2 == 1 ? s : p;
            for (var offset = 0d; offset + dash <= arm; offset += dash * 2)
            {
                g.MoveTo(corner.X + corner.Sx * offset, corner.Y).LineTo(corner.X + corner.Sx * (offset + dash), corner.Y).Stroke(color, 2, 0.5);
                g.MoveTo(corner.X, corner.Y + corner.Sy * offset).LineTo(corner.X, corner.Y + corner.Sy * (offset + dash)).Stroke(color, 2, 0.5);
            }
        }
        var crossArm = radius * 0.09;
        for (var offset = -crossArm; offset + dash * 0.6 <= crossArm; offset += dash * 1.2)
        {
            g.MoveTo(offset, 0).LineTo(offset + dash * 0.6, 0).Stroke(p, 1, 0.4);
            g.MoveTo(0, offset).LineTo(0, offset + dash * 0.6).Stroke(p, 1, 0.4);
        }
        g.Circle(0, 0, radius * 0.012).Fill(s, 0.7);
        var accent = corners[c.Seed % corners.Length];
        g.Circle(accent.X + accent.Sx * arm, accent.Y, radius * 0.014).Fill(p, 0.6);
    }
}
