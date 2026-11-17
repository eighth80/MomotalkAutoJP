using System.Windows;
using System.Windows.Controls;
using MomotalkAutoJP.Core;

namespace MomotalkAutoJP.Views
{
    public partial class SettingsWindow : Window
    {
        private static readonly (int W, int H)[] Presets =
        {
            (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160)
        };

        private readonly double _originalFont;
        private readonly string _originalLang;
        private readonly Dictionary<string, ComboBox> _keyBoxes = new();
        private bool _loading;
        private bool _saved;

        public SettingsWindow()
        {
            InitializeComponent();
            _originalFont = App.Settings.FontSize;
            _originalLang = App.Settings.Language;

            BuildKeyRows();
            BuildResolutionItems();
            Fill(App.Settings);
            LoadWindowList();
            Loc.Changed += OnLanguageChanged;
            Closed += (_, _) =>
            {
                Loc.Changed -= OnLanguageChanged;
                // 저장하지 않고 닫으면 미리 적용했던 언어/글자 크기를 되돌림
                if (!_saved)
                {
                    App.ApplyFontSize(_originalFont);
                    if (Loc.Lang != _originalLang) Loc.Apply(_originalLang);
                }
            };
        }

        #region 화면 구성

        private void BuildResolutionItems()
        {
            int sel = ResolutionCombo.SelectedIndex;
            ResolutionCombo.Items.Clear();
            foreach (var p in Presets) ResolutionCombo.Items.Add($"{p.W} x {p.H}");
            ResolutionCombo.Items.Add(Loc.T("ui.s.custom"));
            if (sel >= 0) ResolutionCombo.SelectedIndex = sel;
        }

        private void BuildKeyRows()
        {
            int row = 0;
            foreach (var action in KeyMap.Actions)
            {
                KeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                label.SetResourceReference(TextBlock.TextProperty, "ui.s.key." + action);
                Grid.SetRow(label, row);
                KeysGrid.Children.Add(label);

                var box = new ComboBox { Margin = new Thickness(0, 3, 0, 3) };
                FillKeyItems(box);
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                KeysGrid.Children.Add(box);

                _keyBoxes[action] = box;
                row++;
            }
        }

        private static void FillKeyItems(ComboBox box)
        {
            int sel = box.SelectedIndex;
            box.Items.Clear();
            box.Items.Add(Loc.T("key.none"));
            foreach (var name in KeyMap.Codes.Keys) box.Items.Add(name);
            if (sel >= 0) box.SelectedIndex = sel;
        }

        private void OnLanguageChanged()
        {
            BuildResolutionItems();
            foreach (var box in _keyBoxes.Values) FillKeyItems(box);
            UpdateReplyDesc();
        }

        private void Fill(Settings s)
        {
            _loading = true;

            LangKo.IsChecked = s.Language != "ja";
            LangJa.IsChecked = s.Language == "ja";

            FontNormal.IsChecked = s.FontSize < 15;
            FontLarge.IsChecked = s.FontSize >= 15 && s.FontSize < 17.5;
            FontXLarge.IsChecked = s.FontSize >= 17.5;

            int idx = Array.FindIndex(Presets, p => p.W == s.GameWidth && p.H == s.GameHeight);
            ResolutionCombo.SelectedIndex = idx >= 0 ? idx : Presets.Length;
            WidthBox.Text = s.GameWidth.ToString();
            HeightBox.Text = s.GameHeight.ToString();
            UpdateCustomPanel();

            InputMouse.IsChecked = s.Input == InputMode.Mouse;
            InputKeyboard.IsChecked = s.Input == InputMode.Keyboard;
            foreach (var (action, box) in _keyBoxes)
            {
                string key = s.KeyFor(action);
                int i = string.IsNullOrEmpty(key) ? 0 : box.Items.IndexOf(key);
                box.SelectedIndex = i < 0 ? 0 : i;
            }
            UpdateKeysArea();

            TitleBox.Text = s.WindowTitle;
            ThresholdSlider.Value = s.Threshold;
            BadgeSlider.Value = s.BadgeThreshold;
            CapturePrint.IsChecked = s.Capture == CaptureMode.PrintWindow;
            CaptureScreen.IsChecked = s.Capture == CaptureMode.Screen;

            DelayBox.Text = s.StepDelayMs.ToString();
            TimeoutBox.Text = s.WaitTimeoutSec.ToString();
            IdleBox.Text = s.IdleChecks.ToString();
            ReplyBox.Text = s.ReplyOffsetPx.ToString();

            UpdateSliderTexts();
            UpdateReplyDesc();
            _loading = false;
        }

        private void UpdateCustomPanel()
        {
            bool custom = ResolutionCombo.SelectedIndex == Presets.Length;
            CustomPanel.IsEnabled = custom;
            CustomPanel.Opacity = custom ? 1 : 0.55;
        }

        private void UpdateKeysArea()
        {
            KeysArea.Visibility = InputKeyboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateReplyDesc()
        {
            int h = int.TryParse(HeightBox.Text, out var v) && v > 0 ? v : App.Settings.GameHeight;
            ReplyDesc.Text = Loc.T("s.replyDesc", (int)Math.Round(h * 0.07));
        }

        private void UpdateSliderTexts()
        {
            ThresholdText.Text = ThresholdSlider.Value.ToString("0.00");
            BadgeText.Text = BadgeSlider.Value.ToString("0.00");
        }

        private void LoadWindowList()
        {
            WindowCombo.Items.Clear();
            foreach (var w in GameWindow.ListWindows().OrderBy(w => w.Title)) WindowCombo.Items.Add(w);
        }

        private double SelectedFont() =>
            FontNormal.IsChecked == true ? 14 : FontXLarge.IsChecked == true ? 18 : 16;

        private string SelectedLang() => LangJa.IsChecked == true ? "ja" : "ko";

        #endregion

        #region 이벤트

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Loc.Apply(SelectedLang());
        }

        private void Font_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            App.ApplyFontSize(SelectedFont());
        }

        private void Input_Checked(object sender, RoutedEventArgs e)
        {
            if (KeysArea != null) UpdateKeysArea();
        }

        private void ResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = ResolutionCombo.SelectedIndex;
            if (i >= 0 && i < Presets.Length && WidthBox != null)
            {
                WidthBox.Text = Presets[i].W.ToString();
                HeightBox.Text = Presets[i].H.ToString();
            }
            if (CustomPanel != null) UpdateCustomPanel();
        }

        private void Size_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ReplyDesc != null && HeightBox != null) UpdateReplyDesc();
        }

        private void FillFromWindow_Click(object sender, RoutedEventArgs e)
        {
            var win = GameWindow.Find(TitleBox.Text);
            if (win == null)
            {
                MessageBox.Show(this, Loc.T("msg.windowNotFound"), Loc.T("ui.s.title"));
                return;
            }
            var (w, h) = win.ClientSize;
            int idx = Array.FindIndex(Presets, p => p.W == w && p.H == h);
            ResolutionCombo.SelectedIndex = idx >= 0 ? idx : Presets.Length;
            WidthBox.Text = w.ToString();
            HeightBox.Text = h.ToString();
        }

        private void RefreshWindows_Click(object sender, RoutedEventArgs e) => LoadWindowList();

        private void WindowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WindowCombo.SelectedItem is GameWindow.WindowEntry w) TitleBox.Text = w.Title;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ThresholdText != null && BadgeText != null) UpdateSliderTexts();
        }

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            var d = new Settings { Language = SelectedLang() };
            Fill(d);
            App.ApplyFontSize(SelectedFont());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(WidthBox.Text, out int w) || !int.TryParse(HeightBox.Text, out int h) ||
                w < 640 || h < 360 || w > 7680 || h > 4320) { Warn("warn.res"); return; }
            if (!int.TryParse(DelayBox.Text, out int delay) || delay < 200 || delay > 10000) { Warn("warn.delay"); return; }
            if (!int.TryParse(TimeoutBox.Text, out int timeout) || timeout < 5 || timeout > 600) { Warn("warn.timeout"); return; }
            if (!int.TryParse(IdleBox.Text, out int idle) || idle < 1 || idle > 50) { Warn("warn.idle"); return; }
            if (!int.TryParse(ReplyBox.Text, out int reply) || reply < 0 || reply > 1000) { Warn("warn.reply"); return; }

            var s = App.Settings;
            bool resChanged = s.GameWidth != w || s.GameHeight != h;

            s.GameWidth = w;
            s.GameHeight = h;
            s.WindowTitle = TitleBox.Text.Trim();
            s.Threshold = Math.Round(ThresholdSlider.Value, 2);
            s.BadgeThreshold = Math.Round(BadgeSlider.Value, 2);
            s.Capture = CaptureScreen.IsChecked == true ? CaptureMode.Screen : CaptureMode.PrintWindow;
            s.StepDelayMs = delay;
            s.WaitTimeoutSec = timeout;
            s.IdleChecks = idle;
            s.ReplyOffsetPx = reply;
            s.FontSize = SelectedFont();
            s.Language = SelectedLang();
            s.Input = InputKeyboard.IsChecked == true ? InputMode.Keyboard : InputMode.Mouse;
            s.Keys = _keyBoxes.ToDictionary(kv => kv.Key,
                kv => kv.Value.SelectedIndex <= 0 ? "" : kv.Value.SelectedItem as string ?? "");

            try
            {
                s.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.T("warn.save", ex.Message), Loc.T("warn.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _saved = true;
            App.Templates.ClearCache();
            Logger.Write(Loc.T("log.settingsSaved", w, h));
            if (resChanged && App.Templates.ReadyCount(s.Input) > 0) Logger.Write(Loc.T("log.resChanged"));
            Close();
        }

        private void Warn(string key) =>
            MessageBox.Show(this, Loc.T(key), Loc.T("warn.title"), MessageBoxButton.OK, MessageBoxImage.Warning);

        #endregion
    }
}
