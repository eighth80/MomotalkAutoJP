using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MomotalkAutoJP.Core;

namespace MomotalkAutoJP.Views
{
    public partial class TemplateWindow : Window
    {
        private Img? _shot;            // 게임 해상도로 맞춘 스크린샷
        private Point _dragStart;
        private bool _dragging;
        private Int32Rect _sel = Int32Rect.Empty;

        private static Settings S => App.Settings;
        private static TemplateLibrary Lib => App.Templates;

        public TemplateWindow()
        {
            InitializeComponent();
            ShotGrid.Width = S.GameWidth;
            ShotGrid.Height = S.GameHeight;

            BuildList();
            var first = TemplateLibrary.All.FirstOrDefault(t => !t.Optional && !Lib.Exists(t.Key)) ?? TemplateLibrary.All[0];
            Select(first.Key);

            Loc.Changed += OnLanguageChanged;
            Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
            Loaded += async (_, _) => await TakeShotAsync();
        }

        private string? CurrentKey => (ItemList.SelectedItem as ListBoxItem)?.Tag as string;

        private void OnLanguageChanged()
        {
            string? key = CurrentKey;
            BuildList();
            if (key != null) Select(key);
        }

        #region 목록

        private void BuildList()
        {
            string? key = CurrentKey;
            ItemList.Items.Clear();
            foreach (var t in TemplateLibrary.All)
            {
                string mark = Lib.Exists(t.Key) ? "✓" : (t.Optional ? "－" : "✗");
                string suffix = t.Optional ? Loc.T("t.optional") : t.MouseOnly ? Loc.T("t.mouseOnly") : "";
                var item = new ListBoxItem
                {
                    Content = new TextBlock { Text = $"{mark} {t.Name}{suffix}", TextWrapping = TextWrapping.Wrap },
                    Tag = t.Key,
                    Padding = new Thickness(6, 5, 6, 5),
                    Foreground = (System.Windows.Media.Brush)FindResource(Lib.Exists(t.Key) ? "Ok" : (t.Optional ? "SubText" : "Danger"))
                };
                ItemList.Items.Add(item);
            }
            if (key != null) Select(key);
        }

        private void Select(string key)
        {
            foreach (ListBoxItem item in ItemList.Items)
            {
                if ((string)item.Tag == key)
                {
                    ItemList.SelectedItem = item;
                    item.BringIntoView();
                    break;
                }
            }
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HitRect.Visibility = Visibility.Collapsed;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            var key = CurrentKey;
            if (key == null) return;
            var t = TemplateLibrary.Get(key);

            NameText.Text = t.Name;
            HintText.Text = t.Hint;

            if (Lib.Exists(key))
            {
                try { PreviewImage.Source = Img.LoadPng(TemplateLibrary.PathOf(key)).ToBitmapSource(); }
                catch { PreviewImage.Source = null; }
                var res = Lib.CapturedResolution(key);
                StatusText.Text = res is { } r ? Loc.T("t.state", r.W, r.H) : "";
            }
            else
            {
                PreviewImage.Source = null;
                StatusText.Text = Loc.T("t.noImage");
            }
        }

        #endregion

        #region 캡처

        private async Task TakeShotAsync()
        {
            var win = GameWindow.Find(S.WindowTitle);
            if (win == null)
            {
                StatusText.Text = Loc.T("t.noWindow");
                return;
            }

            // '화면 그대로' 방식은 보이는 대로 찍으므로 이 창을 잠깐 화면 밖으로 치움
            double oldLeft = Left;
            bool moveAway = S.Capture == Core.CaptureMode.Screen;
            if (moveAway)
            {
                Left = -32000;
                await Task.Delay(300);
            }

            Img? raw = null;
            try { raw = win.Capture(S.Capture); }
            finally { if (moveAway) Left = oldLeft; }

            if (raw == null)
            {
                StatusText.Text = Loc.T("t.captureFail");
                return;
            }

            _shot = raw.W == S.GameWidth && raw.H == S.GameHeight ? raw : raw.Resize(S.GameWidth, S.GameHeight);
            ShotGrid.Width = _shot.W;
            ShotGrid.Height = _shot.H;
            ShotImage.Source = _shot.ToBitmapSource();

            SelRect.Visibility = Visibility.Collapsed;
            HitRect.Visibility = Visibility.Collapsed;
            _sel = Int32Rect.Empty;
            StatusText.Text = Loc.T("t.captured", raw.W, raw.H);

            if (raw.W != S.GameWidth || raw.H != S.GameHeight)
                Logger.Write(Loc.T("log.resMismatchCheck", S.GameWidth, S.GameHeight, raw.W, raw.H));
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await TakeShotAsync();

        #endregion

        #region 드래그 선택

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_shot == null) return;
            _dragStart = e.GetPosition(Overlay);
            _dragging = true;
            Overlay.CaptureMouse();
            HitRect.Visibility = Visibility.Collapsed;
            SelRect.Visibility = Visibility.Visible;
            UpdateSelection(_dragStart);
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging) UpdateSelection(e.GetPosition(Overlay));
        }

        private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Overlay.ReleaseMouseCapture();
            UpdateSelection(e.GetPosition(Overlay));
        }

        private void UpdateSelection(Point p)
        {
            double maxW = ShotGrid.Width, maxH = ShotGrid.Height;
            double x1 = Math.Clamp(Math.Min(_dragStart.X, p.X), 0, maxW);
            double y1 = Math.Clamp(Math.Min(_dragStart.Y, p.Y), 0, maxH);
            double x2 = Math.Clamp(Math.Max(_dragStart.X, p.X), 0, maxW);
            double y2 = Math.Clamp(Math.Max(_dragStart.Y, p.Y), 0, maxH);

            Canvas.SetLeft(SelRect, x1);
            Canvas.SetTop(SelRect, y1);
            SelRect.Width = x2 - x1;
            SelRect.Height = y2 - y1;
            _sel = new Int32Rect((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1));
        }

        #endregion

        #region 저장 / 테스트 / 삭제

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var key = CurrentKey;
            if (key == null || _shot == null) return;

            if (_sel.IsEmpty || _sel.Width < 8 || _sel.Height < 8)
            {
                StatusText.Text = Loc.T("t.dragFirst");
                return;
            }

            var crop = _shot.Crop(_sel.X, _sel.Y, _sel.Width, _sel.Height);
            Lib.Save(key, crop, S.GameWidth, S.GameHeight);

            SelRect.Visibility = Visibility.Collapsed;
            _sel = Int32Rect.Empty;
            BuildList();
            ShowCurrent();
            StatusText.Text = Loc.T("t.saved", TemplateLibrary.Get(key).Name, crop.W, crop.H);
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            var key = CurrentKey;
            if (key == null) return;
            if (!Lib.Exists(key))
            {
                StatusText.Text = Loc.T("t.noImage");
                return;
            }

            var win = GameWindow.Find(S.WindowTitle);
            if (win == null)
            {
                StatusText.Text = Loc.T("t.noWindow");
                return;
            }

            var vision = new Vision(win, S, Lib);
            var best = vision.FindBest(key);

            // 테스트한 그 순간의 화면으로 바꿔서 보여줌
            if (vision.Frame() is { } frame)
            {
                _shot = frame;
                ShotImage.Source = frame.ToBitmapSource();
            }

            double th = key == "unread_badge" ? S.BadgeThreshold : S.Threshold;
            if (best is not { } hit)
            {
                StatusText.Text = Loc.T("t.testResult", 0.0, th, Loc.T("t.testFail"));
                HitRect.Visibility = Visibility.Collapsed;
                return;
            }

            Canvas.SetLeft(HitRect, hit.X - hit.Width / 2.0);
            Canvas.SetTop(HitRect, hit.Y - hit.Height / 2.0);
            HitRect.Width = hit.Width;
            HitRect.Height = hit.Height;
            HitRect.Visibility = Visibility.Visible;
            SelRect.Visibility = Visibility.Collapsed;

            StatusText.Text = Loc.T("t.testResult", hit.Score, th, Loc.T(hit.Score >= th ? "t.testOk" : "t.testFail"));
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var key = CurrentKey;
            if (key == null) return;
            Lib.Delete(key);
            BuildList();
            ShowCurrent();
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AppPaths.Templates);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Templates}\"") { UseShellExecute = true });
        }

        #endregion
    }
}
