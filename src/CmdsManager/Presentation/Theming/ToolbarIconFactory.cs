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
                        graphics.DrawLine(pen, 4, 5, 12, 5);
                        graphics.DrawLine(pen, 6, 3, 10, 3);
                        graphics.DrawRectangle(pen, 5, 5, 6, 8);
                        graphics.DrawLine(pen, 7, 7, 7, 11);
                        graphics.DrawLine(pen, 9, 7, 9, 11);
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
                        graphics.DrawEllipse(pen, 5.5f, 5.5f, 5, 5);
                        graphics.DrawEllipse(pen, 2.5f, 2.5f, 11, 11);
                        for (var index = 0; index < 8; index++)
                        {
                            var angle = index * Math.PI / 4d;
                            var x1 = 8f + (float)Math.Cos(angle) * 5.3f;
                            var y1 = 8f + (float)Math.Sin(angle) * 5.3f;
                            var x2 = 8f + (float)Math.Cos(angle) * 7f;
                            var y2 = 8f + (float)Math.Sin(angle) * 7f;
                            graphics.DrawLine(pen, x1, y1, x2, y2);
                        }
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
    }
}
