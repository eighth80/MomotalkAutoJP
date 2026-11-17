using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MomotalkAutoJP.Core
{
    /// <summary>
    /// 간단한 BGRA 이미지 (외부 라이브러리 없이 사용).
    /// Px는 B, G, R, A 순서로 한 픽셀당 4바이트.
    /// </summary>
    public sealed class Img
    {
        public int W { get; }
        public int H { get; }
        public byte[] Px { get; }
        public int Stride => W * 4;

        public Img(int w, int h, byte[]? px = null)
        {
            W = w;
            H = h;
            Px = px ?? new byte[w * h * 4];
        }

        /// <summary>영역 잘라내기 (화면 밖은 자동으로 잘림)</summary>
        public Img Crop(int x, int y, int w, int h)
        {
            int x0 = Math.Clamp(x, 0, W), y0 = Math.Clamp(y, 0, H);
            int x1 = Math.Clamp(x + w, 0, W), y1 = Math.Clamp(y + h, 0, H);
            int cw = Math.Max(0, x1 - x0), ch = Math.Max(0, y1 - y0);
            var dst = new Img(cw, ch);
            for (int row = 0; row < ch; row++)
                Buffer.BlockCopy(Px, ((y0 + row) * W + x0) * 4, dst.Px, row * cw * 4, cw * 4);
            return dst;
        }

        /// <summary>크기 변경. 줄일 때는 영역 평균, 키울 때는 양선형 보간</summary>
        public Img Resize(int nw, int nh)
        {
            nw = Math.Max(1, nw);
            nh = Math.Max(1, nh);
            if (nw == W && nh == H) return this;

            var dst = new Img(nw, nh);
            double sx = (double)W / nw, sy = (double)H / nh;

            if (sx >= 1 && sy >= 1)
            {
                // 영역 평균 (축소)
                for (int y = 0; y < nh; y++)
                {
                    int ya = (int)(y * sy), yb = Math.Max(ya + 1, Math.Min(H, (int)((y + 1) * sy)));
                    for (int x = 0; x < nw; x++)
                    {
                        int xa = (int)(x * sx), xb = Math.Max(xa + 1, Math.Min(W, (int)((x + 1) * sx)));
                        int b = 0, g = 0, r = 0, n = 0;
                        for (int yy = ya; yy < yb; yy++)
                        {
                            int i = (yy * W + xa) * 4;
                            for (int xx = xa; xx < xb; xx++, i += 4)
                            {
                                b += Px[i]; g += Px[i + 1]; r += Px[i + 2]; n++;
                            }
                        }
                        int o = (y * nw + x) * 4;
                        dst.Px[o] = (byte)(b / n);
                        dst.Px[o + 1] = (byte)(g / n);
                        dst.Px[o + 2] = (byte)(r / n);
                        dst.Px[o + 3] = 255;
                    }
                }
            }
            else
            {
                // 양선형 보간 (확대)
                for (int y = 0; y < nh; y++)
                {
                    double fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, H - 1);
                    int y0 = (int)fy, y1 = Math.Min(y0 + 1, H - 1);
                    double ty = fy - y0;
                    for (int x = 0; x < nw; x++)
                    {
                        double fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, W - 1);
                        int x0 = (int)fx, x1 = Math.Min(x0 + 1, W - 1);
                        double tx = fx - x0;
                        int o = (y * nw + x) * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            double top = Px[(y0 * W + x0) * 4 + c] * (1 - tx) + Px[(y0 * W + x1) * 4 + c] * tx;
                            double bot = Px[(y1 * W + x0) * 4 + c] * (1 - tx) + Px[(y1 * W + x1) * 4 + c] * tx;
                            dst.Px[o + c] = (byte)Math.Round(top * (1 - ty) + bot * ty);
                        }
                        dst.Px[o + 3] = 255;
                    }
                }
            }
            return dst;
        }

        /// <summary>WPF 화면 표시용</summary>
        public BitmapSource ToBitmapSource()
        {
            var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgr32, null, Px, Stride);
            bmp.Freeze();
            return bmp;
        }

        public void SavePng(string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource()));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            File.WriteAllBytes(path, ms.ToArray());
        }

        public static Img LoadPng(string path)
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource src = decoder.Frames[0];
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            var img = new Img(src.PixelWidth, src.PixelHeight);
            src.CopyPixels(img.Px, img.Stride, 0);
            return img;
        }
    }
}
