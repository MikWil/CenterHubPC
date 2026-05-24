using Avalonia;
using System;

namespace CenterHubNew
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("Dispatcher shut down"))
            {
                // Expected race during app shutdown — not a crash
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
