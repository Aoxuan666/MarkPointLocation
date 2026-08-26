using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Seprays.MarkVision
{
    /// <summary>
    /// Shapes supported by template ROIs and area masks.
    /// </summary>
    public enum RegionShape
    {
        Circle,
        Rectangle,
        Diamond
    }

    internal static class RegionShapeGeometry
    {
        public static RectangleF NormalizeBounds(RectangleF bounds, RegionShape shape)
        {
            if (bounds.Width < 0f)
            {
                bounds = new RectangleF(bounds.Right, bounds.Y, -bounds.Width, bounds.Height);
            }
            if (bounds.Height < 0f)
            {
                bounds = new RectangleF(bounds.X, bounds.Bottom, bounds.Width, -bounds.Height);
            }
            if (shape == RegionShape.Circle)
            {
                float side = Math.Max(bounds.Width, bounds.Height);
                PointF center = new PointF(bounds.Left + bounds.Width * 0.5f, bounds.Top + bounds.Height * 0.5f);
                bounds = new RectangleF(center.X - side * 0.5f, center.Y - side * 0.5f, side, side);
            }
            return bounds;
        }

        public static bool Contains(PointF point, RectangleF bounds, RegionShape shape)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                return false;
            }
            if (shape == RegionShape.Rectangle)
            {
                return bounds.Contains(point);
            }
            float dx = (point.X - bounds.Left) / bounds.Width * 2f - 1f;
            float dy = (point.Y - bounds.Top) / bounds.Height * 2f - 1f;
            if (shape == RegionShape.Circle)
            {
                return dx * dx + dy * dy <= 1f;
            }
            return Math.Abs(dx) + Math.Abs(dy) <= 1f;
        }

        public static PointF[] GetPolygon(RectangleF bounds, RegionShape shape)
        {
            if (shape == RegionShape.Diamond)
            {
                return new[]
                {
                    new PointF(bounds.Left + bounds.Width * 0.5f, bounds.Top),
                    new PointF(bounds.Right, bounds.Top + bounds.Height * 0.5f),
                    new PointF(bounds.Left + bounds.Width * 0.5f, bounds.Bottom),
                    new PointF(bounds.Left, bounds.Top + bounds.Height * 0.5f)
                };
            }
            return new PointF[0];
        }

        public static GraphicsPath CreatePath(RectangleF bounds, RegionShape shape)
        {
            GraphicsPath path = new GraphicsPath();
            bounds = NormalizeBounds(bounds, shape);
            if (shape == RegionShape.Circle)
            {
                path.AddEllipse(bounds);
            }
            else if (shape == RegionShape.Rectangle)
            {
                path.AddRectangle(bounds);
            }
            else
            {
                path.AddPolygon(GetPolygon(bounds, shape));
            }
            return path;
        }
    }
}
