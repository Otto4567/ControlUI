using ControlUI.Support.Services;
using System.IO;
using System.Text.Json;
using Jamesnet.Wpf.Controls;
using System.Windows;
using ControlUI.Forms.UI.Views;
using ControlUI.Motion;

namespace ControlUI
{
    internal class App : JamesApplication
    {
        internal EventLogService EventLog { get; } = new();
        internal bool SimulateMotion { get; }
        internal bool SimulateAlarm { get; }
        internal ProjectWorkspace Workspace { get; }
        internal MotionPreferences MotionConfiguration { get; }
        internal IMotionService MotionService { get; }

        public App(bool simulateMotion, bool simulateAlarm)
        {
            DispatcherUnhandledException += (_, e) => HandleFailure("Dispatcher", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += OnDomainFailure;
            TaskScheduler.UnobservedTaskException += OnTaskFailure;
            MachinePaths.Initialize();
            Workspace = new ProjectWorkspace();
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
            MotionService = SimulateMotion
                ? new SimulatedMotionService(TimeProvider.System, SimulateAlarm, MotionConfiguration)
                : new AjinMotionService(MotionConfiguration);
            AppAppearance.Apply(preferences);
        }
         
        private void OnDomainFailure(object sender, UnhandledExceptionEventArgs e) =>
            HandleFailure("AppDomain", e.ExceptionObject as Exception ?? new Exception("Unhandled runtime error."));

        private void OnTaskFailure(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // GC reports abandoned tasks later, independently of the current motion.
            try { EventLog.RecordFailure("TaskScheduler", e.Exception); }
            catch (Exception logError) { System.Diagnostics.Trace.TraceError("{0}", logError); }
            finally { e.SetObserved(); }
        }

        private void HandleFailure(string source, Exception error)
        {
            try { MotionService?.StopForUnhandledException(); }
            catch (Exception stopError) { error = new AggregateException(error, stopError); }
            try { EventLog.RecordFailure(source, error); }
            catch (Exception logError) { System.Diagnostics.Trace.TraceError("{0}", logError); }
        }

        protected override Window CreateShell()
        {
            var shell = new ShellWindow();
            if (SimulateMotion)
                shell.Title = "ControlUI — Motion Simulation";
            EventLog.RecordLifecycle(SimulateMotion ? "ControlUI started in motion simulation." : "ControlUI started.");
            Exit += (_, args) =>
            {
                try { MotionService?.Dispose(); }
                finally
                {
                    AppDomain.CurrentDomain.UnhandledException -= OnDomainFailure;
                    TaskScheduler.UnobservedTaskException -= OnTaskFailure;
                    Workspace.Save();
                    EventLog.RecordLifecycle($"ControlUI exited. Exit code: {args.ApplicationExitCode}.");
                }
            };
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
