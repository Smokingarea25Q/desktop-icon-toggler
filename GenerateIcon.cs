using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

internal static class GenerateIcon
{
    private static readonly int[] Sizes = new[] { 16, 24, 32, 48, 64 };

    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "DesktopToggle.ico";
        List<byte[]> images = new List<byte[]>();

        foreach (int size in Sizes)
        {
            using (Bitmap bitmap = RenderIcon(size))
            {
                images.Add(ToIconImage(bitmap));
            }
        }

        WriteIcon(output, images);
    }

    private static Bitmap RenderIcon(int size)
    {
        Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            float s = size / 64f;
            RectangleF outer = new RectangleF(4f * s, 4f * s, 56f * s, 56f * s);
            using (GraphicsPath outerPath = RoundedRectangle(outer, 16f * s))
            using (LinearGradientBrush background = new LinearGradientBrush(
                outer,
                Color.FromArgb(255, 20, 33, 47),
                Color.FromArgb(255, 12, 132, 145),
                LinearGradientMode.ForwardDiagonal))
            {
                graphics.FillPath(background, outerPath);
            }

            using (Pen glowPen = new Pen(Color.FromArgb(105, 122, 240, 255), Math.Max(1f, 2.3f * s)))
            {
                graphics.DrawArc(glowPen, 11f * s, 9f * s, 42f * s, 42f * s, 205, 145);
            }

            RectangleF screen = new RectangleF(16f * s, 20f * s, 32f * s, 22f * s);
            using (GraphicsPath screenPath = RoundedRectangle(screen, 4f * s))
            using (SolidBrush screenBrush = new SolidBrush(Color.FromArgb(235, 235, 250, 255)))
            using (Pen screenPen = new Pen(Color.FromArgb(255, 226, 252, 255), Math.Max(1f, 2.2f * s)))
            {
                graphics.FillPath(screenBrush, screenPath);
                graphics.DrawPath(screenPen, screenPath);
            }

            using (SolidBrush hideBrush = new SolidBrush(Color.FromArgb(255, 18, 58, 73)))
            {
                graphics.FillRectangle(hideBrush, 22f * s, 26f * s, 20f * s, Math.Max(1f, 2.8f * s));
                graphics.FillRectangle(hideBrush, 22f * s, 32f * s, 14f * s, Math.Max(1f, 2.8f * s));
            }

            using (Pen standPen = new Pen(Color.FromArgb(255, 226, 252, 255), Math.Max(1f, 3.1f * s)))
            {
                graphics.DrawLine(standPen, 32f * s, 42f * s, 32f * s, 49f * s);
                graphics.DrawLine(standPen, 23f * s, 50f * s, 41f * s, 50f * s);
            }

            using (SolidBrush sparkle = new SolidBrush(Color.FromArgb(255, 162, 255, 232)))
            {
                graphics.FillEllipse(sparkle, 43f * s, 14f * s, Math.Max(2f, 7f * s), Math.Max(2f, 7f * s));
            }
        }

        return bitmap;
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static byte[] ToIconImage(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int xorStride = width * 4;
        int andStride = ((width + 31) / 32) * 4;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40);
            writer.Write(width);
            writer.Write(height * 2);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(xorStride * height);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
                }
            }

            byte[] emptyMaskRow = new byte[andStride];
            for (int y = 0; y < height; y++)
            {
                writer.Write(emptyMaskRow);
            }

            return stream.ToArray();
        }
    }

    private static void WriteIcon(string path, List<byte[]> images)
    {
        using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)images.Count);

            int offset = 6 + images.Count * 16;
            for (int i = 0; i < images.Count; i++)
            {
                int size = Sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            foreach (byte[] image in images)
            {
                writer.Write(image);
            }
        }
    }
}
