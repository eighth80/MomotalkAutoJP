using System.Windows;
using MomotalkAutoJP.Core;

namespace MomotalkAutoJP
{
    public partial class App : Application
    {
        public static Settings Settings { get; private set; } = new();
        public static TemplateLibrary Templates { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Write("==== start ====");

            // 예상 못한 오류가 나도 꺼지지 않고 내용을 보여줌
            DispatcherUnhandledException += (_, args) =>
            {
                Logger.Write("UI error: " + args.Exception);
                MessageBox.Show(Loc.T("log.errorDialog", args.Exception.Message, Logger.LogPath),
                    Loc.T("ui.appTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Logger.Write("fatal: " + args.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, args) => { Logger.Write("task: " + args.Exception); args.SetObserved(); };

            Settings = Settings.Load();
            ApplyFontSize(Settings.FontSize);
            Loc.Apply(Settings.Language);

            base.OnStartup(e);
        }

        public static void ApplyFontSize(double size)
        {
            Current.Resources["BaseFontSize"] = Math.Clamp(size, 12, 22);
        }
    }
}
