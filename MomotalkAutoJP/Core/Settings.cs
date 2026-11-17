using System.IO;
using System.Text.Json;

namespace MomotalkAutoJP.Core
{
    public enum InputMode { Mouse = 0, Keyboard = 1 }

    public sealed class Settings
    {
        /// <summary>게임 해상도 (게임 설정에서 고른 값). 인식은 이 크기 기준으로 함</summary>
        public int GameWidth { get; set; } = 1920;
        public int GameHeight { get; set; } = 1080;

        /// <summary>비우면 자동으로 게임 창을 찾음</summary>
        public string WindowTitle { get; set; } = "";

        public CaptureMode Capture { get; set; } = CaptureMode.PrintWindow;

        /// <summary>버튼 인식 정확도 (0.70 ~ 0.99)</summary>
        public double Threshold { get; set; } = 0.88;

        /// <summary>안 읽은 메시지 배지 인식 정확도. 숫자가 바뀌므로 조금 낮게</summary>
        public double BadgeThreshold { get; set; } = 0.80;

        /// <summary>동작 사이 대기 (ms)</summary>
        public int StepDelayMs { get; set; } = 900;

        /// <summary>버튼이 이 시간 동안 안 보이면 멈춤 (초)</summary>
        public int WaitTimeoutSec { get; set; } = 40;

        /// <summary>대화창에서 답장/인연 카드가 더 안 나오면 끝났다고 보는 확인 횟수</summary>
        public int IdleChecks { get; set; } = 6;

        /// <summary>답장 제목줄 중심 → 첫 번째 선택지까지 거리(px, 게임 해상도 기준). 0이면 자동</summary>
        public int ReplyOffsetPx { get; set; } = 0;

        /// <summary>프로그램 글자 크기</summary>
        public double FontSize { get; set; } = 16;

        /// <summary>프로그램 언어: ko / ja</summary>
        public string Language { get; set; } = "ko";

        /// <summary>입력 방식: 마우스 클릭 / 키보드 단축키</summary>
        public InputMode Input { get; set; } = InputMode.Mouse;

        /// <summary>키보드 모드에서 동작별 키 (비우면 그 동작은 클릭)</summary>
        public Dictionary<string, string> Keys { get; set; } = KeyMap.Defaults();

        public string KeyFor(string action) => Keys.TryGetValue(action, out var k) ? k : "";

        public int EffectiveReplyOffset => ReplyOffsetPx > 0 ? ReplyOffsetPx : (int)Math.Round(GameHeight * 0.07);

        public static Settings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
            }
            catch (Exception ex)
            {
                Logger.Write("설정 파일을 읽지 못해 기본값 사용: " + ex.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;
    }
}
