using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using MomotalkAutoJP.Core;

namespace MomotalkAutoJP.Views
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<string> _logs = new();
        private CancellationTokenSource? _cts;
        private GameWindow? _lastWindow;

        public MainWindow()
        {
            InitializeComponent();
            LogList.ItemsSource = _logs;
            Logger.Written += OnLog;
            Loc.Changed += OnLanguageChanged;
            Closed += (_, _) =>
            {
                Logger.Written -= OnLog;
                Loc.Changed -= OnLanguageChanged;
                _cts?.Cancel();
            };

            Loaded += (_, _) =>
            {
                RefreshStatus();
                Logger.Write(Loc.T("log.hello"));
                if (App.Templates.ReadyCount(App.Settings.Input) < App.Templates.RequiredCount(App.Settings.Input))
                    Logger.Write(Loc.T("log.firstRun"));
            };
        }

        private void OnLog(string line)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _logs.Add(line);
                while (_logs.Count > 300) _logs.RemoveAt(0);
                LogList.ScrollIntoView(_logs[^1]);
            });
        }

        private void OnLanguageChanged() => Dispatcher.InvokeAsync(() => RefreshStatus());

        private void RefreshStatus()
        {
            var s = App.Settings;
            if (_lastWindow != null && _lastWindow.IsAlive)
            {
                var (w, h) = _lastWindow.ClientSize;
                WindowStatus.Text = Loc.T("st.windowFound", _lastWindow.Title, w, h);
            }
            else
            {
                WindowStatus.Text = Loc.T(_lastWindow == null ? "st.windowUnknown" : "st.windowNone");
            }

            ResolutionStatus.Text = Loc.T("st.res", s.GameWidth, s.GameHeight);
            ModeStatus.Text = Loc.T("st.mode", Loc.T(s.Input == InputMode.Keyboard ? "mode.keyboard" : "mode.mouse"));

            int ready = App.Templates.ReadyCount(s.Input), need = App.Templates.RequiredCount(s.Input);
            TemplateStatus.Text = Loc.T("st.tpl", ready, need);
            TemplateStatus.Foreground = (Brush)FindResource(ready == need ? "Ok" : "Danger");
        }

        private void SetRunning(bool running)
        {
            StartButton.IsEnabled = !running;
            StopButton.IsEnabled = running;
            CheckButton.IsEnabled = !running;
            TemplateButton.IsEnabled = !running;
            SettingsButton.IsEnabled = !running;
        }

        private GameWindow? FindWindow()
        {
            var win = GameWindow.Find(App.Settings.WindowTitle);
            _lastWindow = win;
            if (win == null) WindowStatus.Text = Loc.T("st.windowNone");
            RefreshStatus();
            return win;
        }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            var win = FindWindow();
            if (win == null)
            {
                Logger.Write(Loc.T("log.windowNotFound"));
                return;
            }

            var (w, h) = win.ClientSize;
            Logger.Write(Loc.T("log.windowFound", win.Title, w, h));
            if (w != App.Settings.GameWidth || h != App.Settings.GameHeight)
                Logger.Write(Loc.T("log.resMismatchCheck", App.Settings.GameWidth, App.Settings.GameHeight, w, h));
        }

        private void Template_Click(object sender, RoutedEventArgs e)
        {
            new TemplateWindow { Owner = this }.ShowDialog();
            RefreshStatus();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow { Owner = this }.ShowDialog();
            RefreshStatus();
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            var missing = App.Templates.Missing(App.Settings.Input).ToList();
            if (missing.Count > 0)
            {
                Logger.Write(Loc.T("log.missingTpl", string.Join(", ", missing.Select(m => m.Name))));
                Logger.Write(Loc.T("log.captureFirst"));
                return;
            }

            var win = FindWindow();
            if (win == null)
            {
                Logger.Write(Loc.T("log.windowNotFound"));
                return;
            }

            SetRunning(true);
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var settings = App.Settings.Clone();

            try
            {
                await Task.Run(async () =>
                {
                    var vision = new Vision(win, settings, App.Templates);
                    var bot = new MomotalkBot(vision, settings);
                    await bot.RunAsync(token);
                });
            }
            catch (OperationCanceledException)
            {
                Logger.Write(Loc.T("log.stopped"));
            }
            catch (BotException ex)
            {
                Logger.Write(Loc.T("log.halted", ex.Message));
            }
            catch (Exception ex)
            {
                Logger.Write(Loc.T("log.error", ex.Message));
                Logger.Write(ex.ToString());
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetRunning(false);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Logger.Write(Loc.T("log.stopping"));
        }
    }
}
