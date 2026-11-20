using System.IO;
using System.Windows;
using ControlUI.Support.Services;
using ControlUI.Properties;

namespace ControlUI
{
    internal class Starter
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool simulateMotion = Array.Exists(args, arg => arg.Equals("--simulate-motion", StringComparison.OrdinalIgnoreCase));
            bool simulateAlarm = Array.Exists(args, arg => arg.Equals("--simulate-alarm", StringComparison.OrdinalIgnoreCase));
            bool running = false;
            App? app = null;
            try
            {
                app = new App(simulateMotion, simulateAlarm);
                var application = app.AddInversionModule<HelperModules>()
                    .AddInversionModule<ViewModules>()
                    .AddWireDataContext<WireDataContext>();
                running = true;
                return application.Run();
            }
            catch (Exception error)
            {
                try { app?.MotionService.StopForUnhandledException(); }
                catch (Exception stopError) { error = new AggregateException(error, stopError); }

                string phase = running ? "runtime" : "startup";
                string config = Path.Combine(AppContext.BaseDirectory, "machine-paths.json");
                string details = $"Configuration file: {config}";
                try { details += $"\nData directory: {MachinePaths.Root}"; }
                catch { /* Resolving the data directory may be the original failure. */ }

                string message = $"{error.GetBaseException().Message}\n\n{details}";
                try
                {
                    string path = Path.Combine(Path.GetTempPath(), $"ControlUI-{phase}-{Guid.NewGuid():N}.log");
                    File.WriteAllText(path, $"{DateTimeOffset.Now:O}\n{details}\n{error}");
                    message += $"\nError log: {path}";
                }
                catch (Exception logError) { message += $"\nCould not write the temporary log: {logError.Message}"; }
                MessageBox.Show(message, $"ControlUI {phase} error", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
