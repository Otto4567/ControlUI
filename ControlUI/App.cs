using ControlUI.Support.Services;
using System.IO;
using System.Text.Json;
using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Forms.UI.Views;

namespace ControlUI
{
    internal class App : JamesApplication
    {
        internal EventLogService EventLog { get; } = new();
        internal bool SimulateMotion { get; }
        internal bool SimulateAlarm { get; }
        internal MotionPreferences MotionConfiguration { get; }
        internal IMotionService? MotionService { get; set; }

        public App(bool simulateMotion, bool simulateAlarm)
        {
            SimulateMotion = simulateMotion;
            SimulateAlarm = simulateMotion && simulateAlarm;
            AppPreferences preferences;
            try
            {
                preferences = AppPreferences.Load();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                preferences = AppPreferences.Default;
            }

            MotionConfiguration = preferences.Motion ?? new MotionPreferences();
            AppAppearance.Apply(preferences);
        }
         
        protected override Window CreateShell()
        {
            var shell = new ShellWindow();
            if (SimulateMotion)
                shell.Title = "ControlUI — Motion Simulation";
            EventLog.RecordLifecycle(SimulateMotion ? "ControlUI started in motion simulation." : "ControlUI started.");
            Exit += (_, args) =>
                EventLog.RecordLifecycle($"ControlUI exited. Exit code: {args.ApplicationExitCode}.");
            bool closing = false;
            bool stopped = false;
            shell.Closing += async (_, args) =>
            {
                if (stopped || MotionService is not { } motion)
                    return;

                args.Cancel = true;
                if (closing)
                    return;

                closing = true;
                try
                {
                    await Task.Run(motion.StopForShutdown);
                    stopped = true;
                    // Queue the second Close after WPF processes the canceled Closing event.
                    _ = shell.Dispatcher.InvokeAsync(shell.Close);
                }
                catch (Exception ex)
                {
                    await EventLog.WriteAsync("Error", "Motion", $"Shutdown blocked: {ex.Message}");
                    MessageBox.Show(shell, ex.Message, "Motion shutdown blocked",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    closing = false;
                }
            };
            return shell;
        }
    }
}
