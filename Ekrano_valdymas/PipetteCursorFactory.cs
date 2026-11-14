using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas
{
    internal static class PipetteCursorFactory
    {
        private const string Shape = "M 12,8 L 15,5 L 17,6 L 20,3 C 24,-1 29,4 25,8 L 22,11 L 23,13 L 20,16 Z M 13,10 L 4,19 L 3,23 L 1,25 C 0,27 2,29 4,27 L 6,25 L 10,24 L 19,15";
        internal static System.Windows.Shapes.Path Icon()=>new()
        {
            Data=Geometry.Parse(Shape), Stroke=Brushes.White, StrokeThickness=1.8,
            StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round,
            StrokeLineJoin=PenLineJoin.Round, Stretch=Stretch.Uniform
        };
        public static Cursor Create()
        {
            const int size = 32;
            const int pixelBytes = size * size * 4;
            const int maskBytes = size * 4;
            const int imageOffset = 22;
            byte[] data = new byte[imageOffset + 40 + pixelBytes + maskBytes];

            Write16(data, 2, 2); // CUR
            Write16(data, 4, 1);
            data[6] = size;
            data[7] = size;
            Write16(data, 10, 3);  // hotspot: pipette tip
            Write16(data, 12, 28);
            Write32(data, 14, 40 + pixelBytes + maskBytes);
            Write32(data, 18, imageOffset);

            int header = imageOffset;
            Write32(data, header, 40);
            Write32(data, header + 4, size);
            Write32(data, header + 8, size * 2);
            Write16(data, header + 12, 1);
            Write16(data, header + 14, 32);
            Write32(data, header + 20, pixelBytes);

            DrawingVisual visual = new();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                Geometry body = Geometry.Parse(Shape);
                drawing.PushTransform(new TranslateTransform(2,1));
                drawing.DrawGeometry(null,new Pen(Brushes.Black,4){LineJoin=PenLineJoin.Round},body);
                drawing.DrawGeometry(null,new Pen(Brushes.White,2){LineJoin=PenLineJoin.Round,StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},body);
                drawing.Pop();
            }

            RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            byte[] topDown = new byte[pixelBytes];
            bitmap.CopyPixels(topDown, size * 4, 0);
            int pixels = header + 40;
            for (int y = 0; y < size; y++)
                Buffer.BlockCopy(topDown, y * size * 4, data,
                    pixels + (size - 1 - y) * size * 4, size * 4);

            using MemoryStream stream = new(data, writable: false);
            return new Cursor(stream);
        }

        private static void Write16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        private static void Write32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }
    }
}
