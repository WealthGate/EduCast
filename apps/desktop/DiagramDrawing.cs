using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class DiagramDrawing
{
    public static void Draw(DrawingContext dc, TeachingDiagram diagram, Brush ink)
    {
        var sketch = new Sketch(dc, ink);
        switch (diagram.Diagram)
        {
            case DiagramKind.Atom:
                Atom(sketch, diagram.Element!, diagram.ShowLabels);
                break;
            case DiagramKind.HydrogenMolecule:
                Molecule(sketch, "H", "H", 1);
                break;
            case DiagramKind.OxygenMolecule:
                Molecule(sketch, "O", "O", 2);
                break;
            case DiagramKind.NitrogenMolecule:
                Molecule(sketch, "N", "N", 3);
                break;
            case DiagramKind.Water:
                sketch.Line([new(107, 111), new(157, 70)]);
                sketch.Line([new(233, 111), new(183, 70)]);
                sketch.Label("O", 170, 42, 34);
                sketch.Label("H", 96, 111, 34);
                sketch.Label("H", 244, 111, 34);
                if (diagram.ShowLabels)
                    sketch.Label("Water · bent molecule", 170, 160, 22);
                break;
            case DiagramKind.CarbonDioxide:
                Bond(sketch, 65, 150, 78, 2);
                Bond(sketch, 190, 275, 78, 2);
                sketch.Label("O", 45, 57, 34);
                sketch.Label("C", 170, 57, 34);
                sketch.Label("O", 295, 57, 34);
                if (diagram.ShowLabels)
                    sketch.Label("Carbon dioxide · linear molecule", 170, 120, 22);
                break;
            case DiagramKind.SodiumChloride:
                sketch.Label("Na⁺", 89, 52, 46);
                sketch.Label("Cl⁻", 250, 52, 46);
                sketch.Line([new(140, 82), new(199, 82)], dashed: true);
                if (diagram.ShowLabels)
                    sketch.Label("Oppositely charged ions attract", 170, 126, 22);
                break;
            case DiagramKind.AnimalCell:
            case DiagramKind.PlantCell:
                Cell(sketch, diagram.Diagram == DiagramKind.PlantCell, diagram.ShowLabels);
                break;
            case DiagramKind.PunnettSquare:
                PunnettSquare(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.TransverseWave:
                Wave(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.Refraction:
                Refraction(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.SeriesCircuit:
                Circuit(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.CompassRose:
                Compass(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.ContourMap:
                Contours(sketch, diagram.ShowLabels);
                break;
            case DiagramKind.WaterCycle:
                WaterCycle(sketch, diagram.ShowLabels);
                break;
        }
    }

    private static void Atom(Sketch s, string element, bool labels)
    {
        var shells = Chemistry.Shells(element);
        var radius = 34 + 21 * (shells.Length - 1);
        var center = radius + 14;
        s.Ellipse(center, center, 20, 20);
        s.Label(element, center, center - 16, 25);
        for (var i = 0; i < shells.Length; i++)
        {
            var ring = 34 + 21 * i;
            s.Ellipse(center, center, ring, ring);
            for (var j = 0; j < shells[i]; j++)
            {
                var angle = Math.Tau * j / shells[i] - Math.PI / 2 + i * .19;
                s.Ellipse(center + Math.Cos(angle) * ring, center + Math.Sin(angle) * ring, 4, 4, fill: true);
            }
        }
        if (labels)
        {
            var number = Chemistry.AtomicNumber(element);
            s.Label($"{number} protons · {number} electrons", center, center + radius + 14, 21);
            s.Label($"Shells: {string.Join(", ", shells)}", center, center + radius + 42, 19);
        }
    }

    private static void Bond(Sketch s, double left, double right, double y, int order)
    {
        for (var i = 0; i < order; i++)
        {
            var row = y + (i - (order - 1) / 2d) * 9;
            s.Line([new(left, row), new(right, row)]);
        }
    }

    private static void Molecule(Sketch s, string first, string second, int order)
    {
        s.Label(first, 35, 30, 36);
        Bond(s, 58, 147, 53, order);
        s.Label(second, 170, 30, 36);
    }

    private static void Cell(Sketch s, bool plant, bool labels)
    {
        if (plant)
        {
            s.Box(18, 12, 306, 205, 19);
            s.Box(26, 20, 290, 189, 16);
            s.Box(142, 53, 142, 115, 29);
            foreach (var (x, y) in new[] { (59, 55), (86, 170), (277, 187) })
            {
                s.Ellipse(x, y, 21, 10);
                s.Line([new(x - 10, y), new(x + 10, y)]);
            }
        }
        else
            s.Ellipse(171, 115, 152, 101);

        s.Ellipse(98, 104, 32, 28);
        s.Ellipse(103, 104, 8, 8, fill: true);
        var mitochondrionX = plant ? 120 : 218;
        var mitochondrionY = plant ? 188 : 96;
        s.Ellipse(mitochondrionX, mitochondrionY, 25, 12);
        s.Line([new(mitochondrionX - 16, mitochondrionY), new(mitochondrionX - 9, mitochondrionY - 5),
            new(mitochondrionX, mitochondrionY + 5), new(mitochondrionX + 9, mitochondrionY - 5),
            new(mitochondrionX + 16, mitochondrionY)]);
        if (!labels)
            return;
        s.Label(plant ? "Plant cell" : "Animal cell", 171, 225, 24);
        s.Label("Nucleus", 99, 134, 17);
        if (plant)
        {
            s.Label("Central vacuole", 213, 99, 16);
            s.Label("Chloroplast", 86, 28, 15);
            s.Label("Cell wall", 252, 26, 16);
            s.Label("Mitochondrion", 184, 177, 16);
        }
        else
        {
            s.Label("Mitochondrion", 218, 113, 17);
            s.Label("Cell membrane", 210, 168, 17);
        }
    }

    private static void PunnettSquare(Sketch s, bool labels)
    {
        s.Box(52, 50, 144, 144);
        s.Line([new(124, 50), new(124, 194)]);
        s.Line([new(52, 122), new(196, 122)]);
        if (!labels)
            return;
        s.Label("A", 88, 14);
        s.Label("a", 160, 14);
        s.Label("A", 31, 72);
        s.Label("a", 31, 144);
        s.Label("AA", 88, 72);
        s.Label("Aa", 160, 72);
        s.Label("Aa", 88, 144);
        s.Label("aa", 160, 144);
    }

    private static void Wave(Sketch s, bool labels)
    {
        s.Arrow(new(12, 110), new(358, 110));
        s.Arrow(new(24, 178), new(24, 37));
        s.Line(Enumerable.Range(0, 121).Select(i =>
            new Point(25 + i * 2.6, 110 - 50 * Math.Sin(i * Math.Tau / 60))).ToArray());
        if (labels)
        {
            s.Arrow(new(64, 41), new(220, 41));
            s.Arrow(new(220, 41), new(64, 41));
            s.Label("Wavelength (λ)", 142, 8, 18);
            s.Label("Distance", 308, 119, 17);
            s.Label("Transverse wave", 178, 182, 22);
        }
    }

    private static void Refraction(Sketch s, bool labels)
    {
        s.Line([new(12, 110), new(340, 110)]);
        s.Line([new(170, 19), new(170, 226)], dashed: true);
        s.Arrow(new(62, 24), new(170, 110));
        s.Arrow(new(170, 110), new(250, 220));
        if (labels)
        {
            s.Label("Air", 303, 79);
            s.Label("Water", 303, 122);
            s.Label("Normal", 170, 0, 18);
            s.Label("Incident ray", 61, 67, 16);
            s.Label("Refracted ray", 302, 208, 16);
        }
    }

    private static void Circuit(Sketch s, bool labels)
    {
        s.Line([new(95, 38), new(24, 38), new(24, 160), new(138, 160)]);
        s.Line([new(228, 38), new(304, 38), new(304, 160), new(151, 160)]);
        s.Symbol(ShapeKind.Resistor, new(95, 38), new(228, 38));
        s.Line([new(138, 136), new(138, 184)]);
        s.Line([new(151, 146), new(151, 174)]);
        if (labels)
        {
            s.Label("Resistor", 162, 63);
            s.Label("Cell", 145, 185);
            s.Label("+", 121, 133, 18);
            s.Label("−", 169, 133, 18);
        }
    }

    private static void Compass(Sketch s, bool labels)
    {
        s.Ellipse(110, 110, 62, 62);
        foreach (var point in new Point[] { new(110, 38), new(182, 110), new(110, 182), new(38, 110) })
            s.Arrow(new(110, 110), point);
        if (labels)
        {
            s.Label("N", 110, 8);
            s.Label("E", 198, 95);
            s.Label("S", 110, 184);
            s.Label("W", 22, 95);
        }
    }

    private static void Contours(Sketch s, bool labels)
    {
        for (var i = 0; i < 4; i++)
        {
            var ring = Enumerable.Range(0, 72).Select(j =>
            {
                var angle = j * Math.Tau / 72;
                var bulge = 1 + .16 * Math.Cos(3 * angle + .1);
                return new Point(159 + (118 - i * 23) * Math.Cos(angle) * bulge,
                    106 + (89 - i * 19) * Math.Sin(angle) * bulge);
            }).ToArray();
            s.Line(ring, close: true);
            if (labels)
                s.Label($"{100 + i * 20} m", 293 - i * 27, 85 - i * 15, 15);
        }
        if (labels)
            s.Label("Contour interval · 20 m", 160, 205);
    }

    private static void WaterCycle(Sketch s, bool labels)
    {
        var top = new Point(171, 44);
        var right = new Point(290, 112);
        var bottom = new Point(171, 187);
        var left = new Point(52, 112);
        s.Arrow(top, right);
        s.Arrow(right, bottom);
        s.Arrow(bottom, left);
        s.Arrow(left, top);
        if (!labels)
            return;
        s.Label("Condensation", 171, 12);
        s.Label("Precipitation", 335, 100);
        s.Label("Collection and runoff", 171, 195);
        s.Label("Evaporation", 55, 80);
    }

    private sealed class Sketch(DrawingContext dc, Brush ink)
    {
        private readonly Pen pen = new(ink, 2.8) { LineJoin = PenLineJoin.Round };
        private readonly Pen dashed = new(ink, 1.6) { DashStyle = DashStyles.Dash };

        internal void Line(Point[] points, bool close = false, bool dashed = false) =>
            dc.DrawGeometry(null, dashed ? this.dashed : pen, BoardSurface.Polyline(points, close));

        internal void Symbol(ShapeKind kind, Point from, Point to) => dc.DrawGeometry(null, pen,
            BoardSurface.ShapeGeometry(new Shape(Guid.Empty, Classroom.Visibility.Both, false, 0, pen.Thickness, kind,
                new(from.X, from.Y), new(to.X, to.Y))));

        internal void Arrow(Point from, Point to) => dc.DrawGeometry(null, pen, BoardSurface.ArrowGeometry(from, to, 12));

        internal void Box(double x, double y, double width, double height, double radius = 0) =>
            dc.DrawRoundedRectangle(null, pen, new Rect(x, y, width, height), radius, radius);

        internal void Ellipse(double x, double y, double radiusX, double radiusY, bool fill = false) =>
            dc.DrawEllipse(fill ? ink : null, pen, new Point(x, y), radiusX, radiusY);

        internal void Label(string text, double x, double y, double size = 20)
        {
            var label = BoardSurface.Format(text, size, ink);
            dc.DrawText(label, new Point(x - label.Width / 2, y));
        }
    }
}
