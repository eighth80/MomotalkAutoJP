namespace MomotalkAutoJP.Core
{
    /// <summary>
    /// 모모톡 자동 처리 흐름
    /// 로비 → 모모톡 → 대화 탭 → (안 읽은 대화 열기 → 답장 → 인연 스토리 입장 → 스킵 → 보상) 반복 → 모모톡 닫기
    /// 마우스 모드: 찾은 버튼을 클릭 / 키보드 모드: 버튼이 보이면 정해 둔 키를 누름 (키가 없으면 클릭)
    /// </summary>
    public sealed class MomotalkBot
    {
        private readonly Vision _v;
        private readonly Settings _s;
        private CancellationToken _ct;

        public int StoriesDone { get; private set; }
        public int ChatsDone { get; private set; }

        // 대화 목록에서 배지를 찾을 영역 (왼쪽 탭 줄 제외)
        private static readonly Region ListArea = new(0.12, 0.10, 0.60, 0.85);
        // 스토리 MENU 버튼은 오른쪽 위
        private static readonly Region TopRight = new(0.65, 0, 0.35, 0.25);

        public MomotalkBot(Vision vision, Settings settings)
        {
            _v = vision;
            _s = settings;
        }

        private static void Log(string key, params object[] args) => Logger.Write(Loc.T(key, args));

        private bool KeyboardFor(string action, out ushort vk)
        {
            vk = 0;
            if (_s.Input != InputMode.Keyboard) return false;
            var code = KeyMap.Code(_s.KeyFor(action));
            if (code == null) return false;
            vk = code.Value;
            return true;
        }

        /// <summary>동작 실행: 키보드 모드 + 키 지정이면 키, 아니면 hit 위치 클릭</summary>
        private void Act(string action, Hit hit, int offsetY = 0)
        {
            if (KeyboardFor(action, out var vk)) _v.PressKey(vk);
            else _v.Click(hit, offsetY);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            _ct = ct;
            Log(_s.Input == InputMode.Keyboard ? "log.startKeyboard" : "log.startMouse");

            await OpenChatListAsync();

            var skipped = new List<int>();
            int lastY = -1, sameCount = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Delay();
                _v.Invalidate();

                var badges = _v.FindAll("unread_badge", _s.BadgeThreshold, ListArea)
                               .Where(b => !skipped.Any(y => Math.Abs(y - b.Y) < b.Height))
                               .ToList();

                if (badges.Count == 0)
                {
                    Log("log.noMoreUnread");
                    break;
                }

                var target = badges[0];

                // 같은 대화를 계속 열어도 배지가 안 사라지면 건너뜀
                if (Math.Abs(target.Y - lastY) < target.Height) sameCount++;
                else sameCount = 0;
                lastY = target.Y;
                if (sameCount >= 2)
                {
                    Log("log.skipStuck");
                    skipped.Add(target.Y);
                    continue;
                }

                Log("log.openChat", badges.Count);
                // 목록 선택은 키가 없으므로 항상 클릭 (배지 왼쪽 = 대화 항목 본문)
                _v.Click(target.X - target.Width * 3, target.Y);
                await Delay(1.5);

                bool story = await ProcessConversationAsync();
                ChatsDone++;

                if (story)
                {
                    await SkipStoryAsync();
                    await ClaimRewardAsync();
                    StoriesDone++;
                    Log("log.storyDone", StoriesDone);
                    await Delay(2);
                    await EnsureChatListAsync();
                }
            }

            await CloseMomotalkAsync();
            Log("log.finished", ChatsDone, StoriesDone);
        }

        #region 단계

        private async Task OpenChatListAsync()
        {
            if (_v.Find("chat_tab") is { } tab)
            {
                Log("log.alreadyOpen");
                _v.Click(tab);
                await Delay();
                return;
            }

            Log("log.openMomotalk");
            var icon = await WaitForAsync("lobby_momotalk");
            Act("OpenMomotalk", icon);
            await Delay(1.5);

            var chat = await WaitForAsync("chat_tab");
            _v.Click(chat);
            await Delay();
        }

        /// <summary>보상 화면 뒤에 대화 목록이 안 보이면 다시 연다</summary>
        private async Task EnsureChatListAsync()
        {
            for (int i = 0; i < 4; i++)
            {
                _v.Invalidate();
                if (_v.Find("chat_tab") != null) return;
                await Delay();
            }
            if (_v.Find("lobby_momotalk") != null) await OpenChatListAsync();
        }

        /// <summary>대화를 끝까지 진행. 인연 스토리에 들어갔으면 true</summary>
        private async Task<bool> ProcessConversationAsync()
        {
            int idle = 0, replies = 0;

            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                _v.Invalidate();

                if (_v.Find("reply_header") is { } reply)
                {
                    if (++replies > 25)
                    {
                        Log("log.replyStuck");
                        return false;
                    }
                    Log("log.reply");
                    Act("Reply", reply, _s.EffectiveReplyOffset);
                    idle = 0;
                    await Delay();
                    continue;
                }

                if (_v.Find("bond_card") is { } card)
                {
                    Log("log.bondFound");
                    Act("OpenStory", card);
                    await Delay();
                    var enter = await WaitForAsync("bond_enter");
                    Act("OpenStory", enter);
                    await Delay(5); // 스토리 로딩
                    return true;
                }

                if (++idle >= _s.IdleChecks)
                {
                    Log("log.chatEnd");
                    return false;
                }
                await Task.Delay(700, _ct);
            }
        }

        private async Task SkipStoryAsync()
        {
            Log("log.skip");
            var menu = await WaitForAsync("story_menu", TopRight);

            if (KeyboardFor("SkipStory", out var vk))
            {
                // PC 클라이언트: 키 하나로 스킵 확인 창이 바로 뜸
                _v.PressKey(vk);
            }
            else
            {
                _v.Click(menu);
                await Delay();
                var skip = await WaitForAsync("story_skip");
                _v.Click(skip);
            }

            await Delay();
            var ok = await WaitForAsync("confirm");
            Act("Confirm", ok);
            await Delay(2);
        }

        private async Task ClaimRewardAsync()
        {
            Log("log.waitReward");
            await WaitForAsync("reward");

            for (int i = 0; i < 6; i++)
            {
                await Delay();
                _v.Invalidate();
                if (_v.Find("reward") == null) return;

                if (KeyboardFor("CloseReward", out var vk)) _v.PressKey(vk);
                else if (_v.Find("confirm") is { } ok) _v.Click(ok);
                else _v.Click(_v.GameW / 2, (int)(_v.GameH * 0.93)); // 아래쪽 빈 곳
            }
            Log("log.rewardStuck");
        }

        private async Task CloseMomotalkAsync()
        {
            _v.Invalidate();
            if (KeyboardFor("CloseMomotalk", out var vk)) _v.PressKey(vk);
            else if (_v.Find("momotalk_close") is { } close) _v.Click(close);
            else _v.PressKey(0x1B); // Esc
            await Delay();
        }

        #endregion

        #region 공통

        private Task Delay(double times = 1) => Task.Delay((int)(_s.StepDelayMs * times), _ct);

        private async Task<Hit> WaitForAsync(string key, Region? region = null)
        {
            var until = DateTime.Now.AddSeconds(_s.WaitTimeoutSec);

            while (DateTime.Now < until)
            {
                _ct.ThrowIfCancellationRequested();
                _v.Invalidate();
                if (_v.Find(key, region: region) is { } hit) return hit;
                await Task.Delay(400, _ct);
            }

            var best = _v.FindBest(key, region);
            throw new BotException(Loc.T("log.timeout",
                TemplateLibrary.Get(key).Name, _s.WaitTimeoutSec, best?.Score ?? 0, _s.Threshold));
        }

        #endregion
    }

    public sealed class BotException : Exception
    {
        public BotException(string message) : base(message) { }
    }
}
