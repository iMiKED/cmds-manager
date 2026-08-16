using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CmdsManager.Presentation.Theming
{
    internal enum ToolbarIcon
    {
        Add,
        Edit,
        Delete,
        Start,
        Stop,
        StartAll,
        StopAll,
        Reload,
        Settings,
        About,
        Exit
    }

    internal static class ToolbarIconFactory
    {
        internal static Bitmap Create(ToolbarIcon icon, Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                switch (icon)
                {
                    case ToolbarIcon.Add:
                        graphics.DrawLine(pen, 8, 3, 8, 13);
                        graphics.DrawLine(pen, 3, 8, 13, 8);
                        break;
                    case ToolbarIcon.Edit:
                        graphics.DrawLine(pen, 3, 12.5f, 4.5f, 8.5f);
                        graphics.DrawLine(pen, 4.5f, 8.5f, 10.8f, 2.2f);
                        graphics.DrawLine(pen, 10.8f, 2.2f, 13.7f, 5.1f);
                        graphics.DrawLine(pen, 13.7f, 5.1f, 7.4f, 11.4f);
                        graphics.DrawLine(pen, 7.4f, 11.4f, 3, 12.5f);
                        break;
                    case ToolbarIcon.Delete:
                        DrawTrash(graphics, pen);
                        break;
                    case ToolbarIcon.Start:
                        DrawPlay(graphics, brush, 4, 3, 9, 10);
                        break;
                    case ToolbarIcon.Stop:
                        graphics.FillRectangle(brush, 4, 4, 8, 8);
                        break;
                    case ToolbarIcon.StartAll:
                        DrawPlay(graphics, brush, 2, 4, 6, 8);
                        DrawPlay(graphics, brush, 8, 4, 6, 8);
                        break;
                    case ToolbarIcon.StopAll:
                        graphics.FillRectangle(brush, 2, 5, 5, 6);
                        graphics.FillRectangle(brush, 9, 5, 5, 6);
                        break;
                    case ToolbarIcon.Reload:
                        graphics.DrawArc(pen, 3, 3, 10, 10, 35, 270);
                        graphics.DrawLine(pen, 11.5f, 2.5f, 13.5f, 4.5f);
                        graphics.DrawLine(pen, 13.5f, 4.5f, 10.5f, 5f);
                        break;
                    case ToolbarIcon.Settings:
                        graphics.DrawPolygon(pen, GearPoints());
                        graphics.DrawEllipse(pen, 5.8f, 5.8f, 4.4f, 4.4f);
                        break;
                    case ToolbarIcon.About:
                        graphics.DrawEllipse(pen, 2.5f, 2.5f, 11, 11);
                        graphics.FillEllipse(brush, 7.2f, 4.3f, 1.6f, 1.6f);
                        graphics.DrawLine(pen, 8, 7.5f, 8, 11);
                        break;
                    case ToolbarIcon.Exit:
                        graphics.DrawLine(pen, 3, 3, 8, 3);
                        graphics.DrawLine(pen, 3, 3, 3, 13);
                        graphics.DrawLine(pen, 3, 13, 8, 13);
                        graphics.DrawLine(pen, 7, 8, 14, 8);
                        graphics.DrawLine(pen, 11, 5, 14, 8);
                        graphics.DrawLine(pen, 14, 8, 11, 11);
                        break;
                }
            }
            return bitmap;
        }

        private static void DrawPlay(Graphics graphics, Brush brush, float x, float y, float width, float height)
        {
            graphics.FillPolygon(brush, new[]
            {
                new PointF(x, y),
                new PointF(x, y + height),
                new PointF(x + width, y + height / 2f)
            });
        }

        private static void DrawTrash(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 2.3f, 4.3f, 13.7f, 4.3f);

            using (var handle = new GraphicsPath())
            {
                handle.StartFigure();
                handle.AddLine(5.5f, 4.2f, 5.5f, 2.9f);
                handle.AddBezier(5.5f, 2.9f, 5.5f, 2.1f, 6.1f, 1.5f, 6.9f, 1.5f);
                handle.AddLine(6.9f, 1.5f, 9.1f, 1.5f);
                handle.AddBezier(9.1f, 1.5f, 9.9f, 1.5f, 10.5f, 2.1f, 10.5f, 2.9f);
                handle.AddLine(10.5f, 2.9f, 10.5f, 4.2f);
                graphics.DrawPath(pen, handle);
            }

            using (var body = new GraphicsPath())
            {
                body.StartFigure();
                body.AddLine(3.5f, 4.5f, 3.5f, 12.7f);
                body.AddBezier(3.5f, 12.7f, 3.5f, 13.8f, 4.2f, 14.5f, 5.3f, 14.5f);
                body.AddLine(5.3f, 14.5f, 10.7f, 14.5f);
                body.AddBezier(10.7f, 14.5f, 11.8f, 14.5f, 12.5f, 13.8f, 12.5f, 12.7f);
                body.AddLine(12.5f, 12.7f, 12.5f, 4.5f);
                graphics.DrawPath(pen, body);
            }

            graphics.DrawLine(pen, 6.5f, 7.2f, 6.5f, 11.4f);
            graphics.DrawLine(pen, 9.5f, 7.2f, 9.5f, 11.4f);
        }

        private static PointF[] GearPoints()
        {
            const int teeth = 8;
            const float center = 8f;
            const float rootRadius = 5.15f;
            const float toothRadius = 6.55f;
            var points = new PointF[teeth * 4];
            for (var tooth = 0; tooth < teeth; tooth++)
            {
                var centerAngle = -Math.PI / 2d + tooth * Math.PI * 2d / teeth;
                SetPolarPoint(points, tooth * 4, centerAngle - Math.PI / 8d, rootRadius, center);
                SetPolarPoint(points, tooth * 4 + 1, centerAngle - Math.PI / 18d, toothRadius, center);
                SetPolarPoint(points, tooth * 4 + 2, centerAngle + Math.PI / 18d, toothRadius, center);
                SetPolarPoint(points, tooth * 4 + 3, centerAngle + Math.PI / 8d, rootRadius, center);
            }
            return points;
        }

        private static void SetPolarPoint(PointF[] points, int index, double angle, float radius, float center)
        {
            points[index] = new PointF(center + (float)Math.Cos(angle) * radius,
                center + (float)Math.Sin(angle) * radius);
        }
    }
}
