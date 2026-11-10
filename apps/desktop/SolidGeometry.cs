using System.Windows;
using System.Windows.Media;
using EduCast.Classroom;

namespace EduCast.Studio;

internal static class SolidGeometry
{
    internal static Geometry Draw(ShapeKind kind, Rect bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1)
            return Geometry.Empty;
        if (kind is ShapeKind.Sphere or ShapeKind.Cube)
        {
            var size = Math.Min(bounds.Width, bounds.Height);
            bounds = new Rect(bounds.Location, new Size(size, size));
        }
        var width = bounds.Width;
        var height = bounds.Height;
        var x = bounds.Left;
        var y = bounds.Top;
        var middle = x + width / 2;
        var geometry = new GeometryGroup();
        if (kind == ShapeKind.Sphere)
        {
            var center = new Point(middle, y + height / 2);
            geometry.Children.Add(new EllipseGeometry(center, width / 2, height / 2));
            geometry.Children.Add(new EllipseGeometry(center, width / 2, height * .15));
        }
        else if (kind is ShapeKind.Cone or ShapeKind.Cylinder)
        {
            var ry = Math.Min(height * .20, width * .16);
            var bottom = bounds.Bottom - ry;
            geometry.Children.Add(new EllipseGeometry(new Point(middle, bottom), width / 2, ry));
            if (kind == ShapeKind.Cone)
                geometry.Children.Add(BoardSurface.Polyline([new(x, bottom), new(middle, y), new(bounds.Right, bottom)]));
            else
            {
                geometry.Children.Add(new EllipseGeometry(new Point(middle, y + ry), width / 2, ry));
                geometry.Children.Add(new LineGeometry(new Point(x, y + ry), new Point(x, bottom)));
                geometry.Children.Add(new LineGeometry(new Point(bounds.Right, y + ry), new Point(bounds.Right, bottom)));
            }
        }
        else
        {
            var depth = Math.Min(width, height) * .23;
            var face = new Rect(x, y + depth, width - depth, height - depth);
            var offset = new Vector(depth, -depth);
            geometry.Children.Add(new RectangleGeometry(face));
            geometry.Children.Add(BoardSurface.Polyline(
                [face.TopLeft, face.TopLeft + offset, face.TopRight + offset, face.TopRight]));
            geometry.Children.Add(BoardSurface.Polyline(
                [face.TopRight + offset, face.BottomRight + offset, face.BottomRight]));
        }
        return geometry;
    }
}
