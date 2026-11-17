using System.IO;
using System.Text.Json;

namespace MomotalkAutoJP.Core
{
    /// <summary>찾아야 할 버튼(템플릿) 하나의 정의. 이름/설명은 언어 파일(Loc)에서 가져옴</summary>
    public sealed class TemplateInfo
    {
        public required string Key { get; init; }
        public bool Optional { get; init; }
        /// <summary>키보드 모드에서는 필요 없는 항목</summary>
        public bool MouseOnly { get; init; }

        public string Name => Loc.T("tpl." + Key);
        public string Hint => Loc.T("tpl." + Key + ".hint");
    }

    /// <summary>
    /// 사용자가 직접 캡처한 템플릿 이미지 관리.
    /// 저장 위치: %AppData%\MomotalkAutoJP\templates\{key}.png
    /// 캡처할 때의 게임 해상도를 함께 저장해서, 나중에 해상도를 바꿔도 크기를 맞춰 씀.
    /// </summary>
    public sealed class TemplateLibrary
    {
        public static readonly IReadOnlyList<TemplateInfo> All = new List<TemplateInfo>
        {
            new() { Key = "lobby_momotalk" },
            new() { Key = "chat_tab" },
            new() { Key = "unread_badge" },
            new() { Key = "reply_header" },
            new() { Key = "bond_card" },
            new() { Key = "bond_enter" },
            new() { Key = "story_menu" },
            new() { Key = "story_skip", MouseOnly = true },
            new() { Key = "confirm" },
            new() { Key = "reward" },
            new() { Key = "momotalk_close", Optional = true },
        };

        public static TemplateInfo Get(string key) => All.First(t => t.Key == key);

        private sealed class Meta
        {
            public Dictionary<string, int[]> CapturedAt { get; set; } = new();
        }

        private static readonly string MetaPath = Path.Combine(AppPaths.Templates, "meta.json");
        private readonly Dictionary<string, Matcher.Template> _cache = new();
        private readonly object _gate = new();
        private readonly Meta _meta;

        public TemplateLibrary()
        {
            _meta = LoadMeta();
        }

        public static string PathOf(string key) => Path.Combine(AppPaths.Templates, key + ".png");
        public bool Exists(string key) => File.Exists(PathOf(key));

        private static bool Needed(TemplateInfo t, InputMode mode) =>
            !t.Optional && !(t.MouseOnly && mode == InputMode.Keyboard);

        public IEnumerable<TemplateInfo> Missing(InputMode mode) => All.Where(t => Needed(t, mode) && !Exists(t.Key));
        public int ReadyCount(InputMode mode) => All.Count(t => Needed(t, mode) && Exists(t.Key));
        public int RequiredCount(InputMode mode) => All.Count(t => Needed(t, mode));

        public (int W, int H)? CapturedResolution(string key)
            => _meta.CapturedAt.TryGetValue(key, out var r) && r.Length == 2 ? (r[0], r[1]) : null;

        /// <summary>게임 해상도 (gameW x gameH) 기준으로 잘라낸 이미지를 저장</summary>
        public void Save(string key, Img crop, int gameW, int gameH)
        {
            Directory.CreateDirectory(AppPaths.Templates);
            crop.SavePng(PathOf(key));
            _meta.CapturedAt[key] = new[] { gameW, gameH };
            SaveMeta();
            ClearCache();
        }

        public void Delete(string key)
        {
            if (Exists(key)) File.Delete(PathOf(key));
            _meta.CapturedAt.Remove(key);
            SaveMeta();
            ClearCache();
        }

        /// <summary>현재 게임 해상도에 맞게 크기를 조정하고 매칭 준비까지 끝낸 템플릿. 없으면 null</summary>
        public Matcher.Template? Load(string key, int gameW, int gameH)
        {
            string cacheKey = $"{key}@{gameW}x{gameH}";
            lock (_gate)
            {
                if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
                if (!Exists(key)) return null;

                var img = Img.LoadPng(PathOf(key));
                if (CapturedResolution(key) is { } r && (r.W != gameW || r.H != gameH))
                {
                    int nw = Math.Max(4, (int)Math.Round(img.W * (double)gameW / r.W));
                    int nh = Math.Max(4, (int)Math.Round(img.H * (double)gameH / r.H));
                    img = img.Resize(nw, nh);
                }

                var prepared = Matcher.Prepare(img);
                _cache[cacheKey] = prepared;
                return prepared;
            }
        }

        public void ClearCache()
        {
            lock (_gate) _cache.Clear();
        }

        private static Meta LoadMeta()
        {
            try
            {
                if (File.Exists(MetaPath))
                    return JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaPath)) ?? new Meta();
            }
            catch { }
            return new Meta();
        }

        private void SaveMeta()
        {
            Directory.CreateDirectory(AppPaths.Templates);
            File.WriteAllText(MetaPath, JsonSerializer.Serialize(_meta, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
