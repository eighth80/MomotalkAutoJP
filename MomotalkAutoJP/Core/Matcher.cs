namespace MomotalkAutoJP.Core
{
    /// <summary>
    /// 템플릿 매칭 (정규화 상관계수, OpenCV TM_CCOEFF_NORMED와 같은 계산).
    /// 빠르게 하려고 2단계로 찾는다:
    ///  1) 화면과 템플릿을 F배 줄인 흑백 이미지로 전체를 훑어서 후보 위치를 고르고
    ///  2) 후보 주변만 원래 크기 컬러로 정밀하게 다시 계산
    /// </summary>
    public static class Matcher
    {
        /// <summary>매칭 준비가 끝난 템플릿</summary>
        public sealed class Template
        {
            public int W, H, N;
            public float[] Zc = Array.Empty<float>();   // 컬러 (B,G,R 순서), 채널별 평균을 뺀 값
            public double ZcNormSq;

            public int F;                                // 축소 배율
            public int CW, CH, CN;
            public float[] Zg = Array.Empty<float>();   // 축소 흑백, 평균을 뺀 값
            public double ZgNormSq;
        }

        public static Template Prepare(Img t)
        {
            var tp = new Template { W = t.W, H = t.H, N = t.W * t.H };

            // 컬러
            double mb = 0, mg = 0, mr = 0;
            for (int i = 0; i < tp.N; i++)
            {
                mb += t.Px[i * 4]; mg += t.Px[i * 4 + 1]; mr += t.Px[i * 4 + 2];
            }
            mb /= tp.N; mg /= tp.N; mr /= tp.N;

            tp.Zc = new float[tp.N * 3];
            double norm = 0;
            for (int i = 0; i < tp.N; i++)
            {
                float b = (float)(t.Px[i * 4] - mb), g = (float)(t.Px[i * 4 + 1] - mg), r = (float)(t.Px[i * 4 + 2] - mr);
                tp.Zc[i * 3] = b; tp.Zc[i * 3 + 1] = g; tp.Zc[i * 3 + 2] = r;
                norm += b * b + g * g + r * r;
            }
            tp.ZcNormSq = norm;

            // 축소 흑백 (템플릿 짧은 변이 10px 이상 남도록, 최대 4배)
            tp.F = Math.Clamp(Math.Min(t.W, t.H) / 10, 1, 4);
            var g2 = DownGray(t, tp.F, out tp.CW, out tp.CH);
            tp.CN = tp.CW * tp.CH;
            double mean = 0;
            foreach (var v in g2) mean += v;
            mean /= Math.Max(1, tp.CN);
            tp.Zg = new float[tp.CN];
            double gn = 0;
            for (int i = 0; i < tp.CN; i++)
            {
                float z = (float)(g2[i] - mean);
                tp.Zg[i] = z;
                gn += z * z;
            }
            tp.ZgNormSq = gn;
            return tp;
        }

        /// <summary>한 프레임에 대해 축소 흑백 이미지/적분 이미지를 재사용하기 위한 캐시</summary>
        public sealed class FrameData
        {
            public Img Frame { get; }
            private readonly Dictionary<int, Coarse> _levels = new();

            public FrameData(Img frame) { Frame = frame; }

            public Coarse Level(int f)
            {
                if (!_levels.TryGetValue(f, out var c))
                {
                    c = new Coarse(Frame, f);
                    _levels[f] = c;
                }
                return c;
            }
        }

        public sealed class Coarse
        {
            public int W, H;
            public float[] Gray;
            public double[] Sum, Sq; // (W+1)*(H+1) 적분 이미지

            public Coarse(Img frame, int f)
            {
                Gray = DownGray(frame, f, out W, out H);
                Sum = new double[(W + 1) * (H + 1)];
                Sq = new double[(W + 1) * (H + 1)];
                int s = W + 1;
                for (int y = 0; y < H; y++)
                {
                    double rs = 0, rq = 0;
                    for (int x = 0; x < W; x++)
                    {
                        double v = Gray[y * W + x];
                        rs += v; rq += v * v;
                        Sum[(y + 1) * s + x + 1] = Sum[y * s + x + 1] + rs;
                        Sq[(y + 1) * s + x + 1] = Sq[y * s + x + 1] + rq;
                    }
                }
            }

            public void Window(int x, int y, int w, int h, out double sum, out double sq)
            {
                int s = W + 1;
                int a = y * s + x, b = y * s + x + w, c = (y + h) * s + x, d = (y + h) * s + x + w;
                sum = Sum[d] - Sum[b] - Sum[c] + Sum[a];
                sq = Sq[d] - Sq[b] - Sq[c] + Sq[a];
            }
        }

        /// <summary>찾은 위치 (왼쪽 위 좌표, 원래 크기 기준)</summary>
        public readonly record struct Found(int X, int Y, double Score);

        /// <summary>
        /// roi(원래 크기 좌표) 안에서 템플릿 찾기.
        /// threshold 이상인 결과를 점수 높은 순으로 최대 max개. threshold &lt; 0 이면 최고 1개를 무조건 반환
        /// </summary>
        public static List<Found> Search(FrameData fd, Template t, int rx, int ry, int rw, int rh, double threshold, int max)
        {
            var frame = fd.Frame;
            var results = new List<Found>();

            // ROI를 화면 안으로
            int x0 = Math.Max(0, rx), y0 = Math.Max(0, ry);
            int x1 = Math.Min(frame.W, rx + rw), y1 = Math.Min(frame.H, ry + rh);
            if (x1 - x0 < t.W || y1 - y0 < t.H || t.CN == 0) return results;

            int f = t.F;
            var c = fd.Level(f);

            // 1단계: 축소 흑백에서 전체 점수 계산
            int cx0 = (x0 + f - 1) / f, cy0 = (y0 + f - 1) / f;
            int cx1 = x1 / f - t.CW, cy1 = y1 / f - t.CH; // 포함
            if (cx1 < cx0 || cy1 < cy0) return results;

            int mw = cx1 - cx0 + 1, mh = cy1 - cy0 + 1;
            var map = new float[mw * mh];
            for (int y = 0; y < mh; y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    int px = cx0 + x, py = cy0 + y;
                    c.Window(px, py, t.CW, t.CH, out double s, out double q);
                    double varI = q - s * s / t.CN;
                    if (varI <= 1e-6 || t.ZgNormSq <= 1e-6) { map[y * mw + x] = 0; continue; }

                    double num = 0;
                    int k = 0;
                    for (int j = 0; j < t.CH; j++)
                    {
                        int row = (py + j) * c.W + px;
                        for (int i = 0; i < t.CW; i++, k++) num += c.Gray[row + i] * t.Zg[k];
                    }
                    map[y * mw + x] = (float)(num / Math.Sqrt(varI * t.ZgNormSq));
                }
            }

            // 후보 고르기: 1단계 점수는 축소 때문에 조금 낮게 나오므로 여유를 둠
            double coarseTh = threshold < 0 ? double.MinValue : threshold - 0.25;
            int candidateLimit = threshold < 0 ? 3 : Math.Max(3, max * 2);
            var order = new List<int>();
            for (int i = 0; i < map.Length; i++) if (map[i] >= coarseTh) order.Add(i);
            order.Sort((a, b) => map[b].CompareTo(map[a]));

            var picked = new List<(int x, int y)>();
            foreach (int idx in order)
            {
                int px = idx % mw, py = idx / mw;
                bool near = picked.Any(p => Math.Abs(p.x - px) < t.CW && Math.Abs(p.y - py) < t.CH);
                if (near) continue;
                picked.Add((px, py));
                if (picked.Count >= candidateLimit) break;
            }

            // 2단계: 후보 주변을 원래 크기 컬러로 정밀 계산
            foreach (var (px, py) in picked)
            {
                int gx = (cx0 + px) * f, gy = (cy0 + py) * f;
                Found best = new(gx, gy, double.MinValue);
                for (int dy = -f; dy <= f; dy++)
                {
                    for (int dx = -f; dx <= f; dx++)
                    {
                        int ax = gx + dx, ay = gy + dy;
                        if (ax < x0 || ay < y0 || ax + t.W > x1 || ay + t.H > y1) continue;
                        double sc = ColorScore(frame, t, ax, ay);
                        if (sc > best.Score) best = new Found(ax, ay, sc);
                    }
                }
                if (best.Score > double.MinValue) results.Add(best);
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            // 정밀 계산 후 겹치는 결과 제거
            var final = new List<Found>();
            foreach (var r in results)
            {
                if (threshold >= 0 && r.Score < threshold) continue;
                if (final.Any(o => Math.Abs(o.X - r.X) < t.W && Math.Abs(o.Y - r.Y) < t.H)) continue;
                final.Add(r);
                if (final.Count >= (threshold < 0 ? 1 : max)) break;
            }
            return final;
        }

        private static double ColorScore(Img frame, Template t, int ax, int ay)
        {
            double sb = 0, sg = 0, sr = 0, qb = 0, qg = 0, qr = 0, num = 0;
            int k = 0;
            byte[] px = frame.Px;
            for (int j = 0; j < t.H; j++)
            {
                int i = ((ay + j) * frame.W + ax) * 4;
                for (int x = 0; x < t.W; x++, i += 4, k += 3)
                {
                    double b = px[i], g = px[i + 1], r = px[i + 2];
                    sb += b; sg += g; sr += r;
                    qb += b * b; qg += g * g; qr += r * r;
                    num += b * t.Zc[k] + g * t.Zc[k + 1] + r * t.Zc[k + 2];
                }
            }
            double n = t.N;
            double varI = (qb - sb * sb / n) + (qg - sg * sg / n) + (qr - sr * sr / n);
            if (varI <= 1e-6 || t.ZcNormSq <= 1e-6) return 0;
            return num / Math.Sqrt(varI * t.ZcNormSq);
        }

        /// <summary>f배 축소한 흑백 이미지 (영역 평균)</summary>
        private static float[] DownGray(Img img, int f, out int w, out int h)
        {
            w = img.W / f;
            h = img.H / f;
            var g = new float[Math.Max(0, w * h)];
            float inv = 1f / (f * f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float acc = 0;
                    for (int yy = 0; yy < f; yy++)
                    {
                        int i = ((y * f + yy) * img.W + x * f) * 4;
                        for (int xx = 0; xx < f; xx++, i += 4)
                            acc += 0.114f * img.Px[i] + 0.587f * img.Px[i + 1] + 0.299f * img.Px[i + 2];
                    }
                    g[y * w + x] = acc * inv;
                }
            }
            return g;
        }
    }
}
