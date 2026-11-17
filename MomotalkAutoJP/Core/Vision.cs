namespace MomotalkAutoJP.Core
{
    /// <summary>찾은 위치. X, Y는 게임 해상도 기준 중심 좌표</summary>
    public readonly record struct Hit(int X, int Y, int Width, int Height, double Score);

    /// <summary>화면 영역 (0~1 비율). 해상도와 상관없이 같은 위치를 가리킴</summary>
    public readonly record struct Region(double X, double Y, double W, double H)
    {
        public static readonly Region Full = new(0, 0, 1, 1);
    }

    /// <summary>
    /// 게임 화면을 캡처해서 설정된 게임 해상도로 맞춘 뒤 템플릿을 찾는다.
    /// 클릭 좌표는 실제 창 크기로 다시 변환한다.
    /// </summary>
    public sealed class Vision
    {
        private readonly GameWindow _window;
        private readonly Settings _settings;
        private readonly TemplateLibrary _templates;

        private Matcher.FrameData? _frame;
        private DateTime _frameTime;
        private double _toClientX = 1, _toClientY = 1;
        private bool _sizeWarned;

        public Vision(GameWindow window, Settings settings, TemplateLibrary templates)
        {
            _window = window;
            _settings = settings;
            _templates = templates;
        }

        public GameWindow Window => _window;
        public int GameW => _settings.GameWidth;
        public int GameH => _settings.GameHeight;

        /// <summary>현재 화면 (게임 해상도 크기). 150ms 안에 다시 부르면 같은 프레임 재사용</summary>
        public Img? Frame() => GetFrame()?.Frame;

        private Matcher.FrameData? GetFrame()
        {
            if (_frame != null && (DateTime.Now - _frameTime).TotalMilliseconds < 150) return _frame;

            var raw = _window.Capture(_settings.Capture);
            if (raw == null) return null;

            _toClientX = (double)raw.W / GameW;
            _toClientY = (double)raw.H / GameH;

            if (raw.W != GameW || raw.H != GameH)
            {
                if (!_sizeWarned)
                {
                    Logger.Write(Loc.T("log.sizeMismatch", raw.W, raw.H, GameW, GameH));
                    _sizeWarned = true;
                }
                raw = raw.Resize(GameW, GameH);
            }

            _frame = new Matcher.FrameData(raw);
            _frameTime = DateTime.Now;
            return _frame;
        }

        /// <summary>다음 검색 때 화면을 새로 찍도록</summary>
        public void Invalidate() => _frame = null;

        private (int x, int y, int w, int h) ToRect(Region r) =>
            ((int)(r.X * GameW), (int)(r.Y * GameH), (int)Math.Ceiling(r.W * GameW), (int)Math.Ceiling(r.H * GameH));

        private List<Hit> Search(string key, double threshold, Region? region, int max)
        {
            var fd = GetFrame();
            var tpl = _templates.Load(key, GameW, GameH);
            if (fd == null || tpl == null) return new List<Hit>();

            var (x, y, w, h) = ToRect(region ?? Region.Full);
            return Matcher.Search(fd, tpl, x, y, w, h, threshold, max)
                .Select(f => new Hit(f.X + tpl.W / 2, f.Y + tpl.H / 2, tpl.W, tpl.H, f.Score))
                .ToList();
        }

        /// <summary>가장 잘 맞는 위치 하나 (기준 미달이면 null)</summary>
        public Hit? Find(string key, double? threshold = null, Region? region = null)
        {
            var list = Search(key, threshold ?? _settings.Threshold, region, 1);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>점수와 상관없이 가장 잘 맞는 위치 (테스트용)</summary>
        public Hit? FindBest(string key, Region? region = null)
        {
            var list = Search(key, -1, region, 1);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>기준 이상인 위치 전부, 위에서 아래 순</summary>
        public List<Hit> FindAll(string key, double threshold, Region? region = null, int max = 15)
            => Search(key, threshold, region, max).OrderBy(h => h.Y).ToList();

        /// <summary>게임 해상도 좌표를 클릭</summary>
        public void Click(int gameX, int gameY)
        {
            _window.Click((int)Math.Round(gameX * _toClientX), (int)Math.Round(gameY * _toClientY));
            Invalidate();
        }

        public void Click(Hit h, int offsetY = 0) => Click(h.X, h.Y + offsetY);

        public void PressKey(ushort vk)
        {
            _window.PressKey(vk);
            Invalidate();
        }
    }
}
