using System.IO;

namespace MomotalkAutoJP.Core
{
    /// <summary>%AppData%\MomotalkAutoJP\log.txt 에 기록. 문제가 생기면 이 파일을 보면 됨</summary>
    public static class Logger
    {
        public static readonly string LogPath = Path.Combine(AppPaths.Root, "log.txt");
        private static readonly object Gate = new();

        public static event Action<string>? Written;

        public static void Write(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.Root);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000) File.Delete(LogPath);
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
                }
            }
            catch { /* 로그 실패는 무시 */ }

            Written?.Invoke(line);
        }
    }

    public static class AppPaths
    {
        public static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MomotalkAutoJP");
        public static readonly string Templates = Path.Combine(Root, "templates");
        public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    }
}
