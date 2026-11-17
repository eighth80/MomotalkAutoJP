namespace MomotalkAutoJP.Core
{
    /// <summary>키보드 모드에서 쓰는 동작과 키 목록</summary>
    public static class KeyMap
    {
        /// <summary>키를 정할 수 있는 동작들. 키가 비어 있으면 그 동작은 클릭으로 함</summary>
        public static readonly string[] Actions =
        {
            "OpenMomotalk", // 로비에서 모모톡 열기
            "Reply",        // 답장 선택
            "OpenStory",    // 인연 카드 / 입장 버튼
            "SkipStory",    // 스토리 스킵 창 열기
            "Confirm",      // 확인
            "CloseReward",  // 보상 화면 닫기
            "CloseMomotalk" // 모모톡 닫기
        };

        public static Dictionary<string, string> Defaults() => new()
        {
            ["OpenMomotalk"] = "",
            ["Reply"] = "1",
            ["OpenStory"] = "Space",
            ["SkipStory"] = "Esc",
            ["Confirm"] = "Space",
            ["CloseReward"] = "Space",
            ["CloseMomotalk"] = "Esc",
        };

        /// <summary>선택 가능한 키 이름 → 가상 키 코드</summary>
        public static readonly IReadOnlyDictionary<string, ushort> Codes = Build();

        private static Dictionary<string, ushort> Build()
        {
            var d = new Dictionary<string, ushort>
            {
                ["Space"] = 0x20, ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Tab"] = 0x09, ["Backspace"] = 0x08,
            };
            for (int i = 0; i <= 9; i++) d[i.ToString()] = (ushort)(0x30 + i);
            for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
            for (int i = 1; i <= 12; i++) d["F" + i] = (ushort)(0x6F + i);
            return d;
        }

        public static ushort? Code(string? name) =>
            !string.IsNullOrEmpty(name) && Codes.TryGetValue(name, out var c) ? c : null;
    }
}
