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

        private async Task LogShutdownAsync(string level, string message)
        {
            try { await EventLog.WriteAsync(level, "Motion", message); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("{0}; log failed: {1}", message, error); }
        }

        protected override Window CreateShell()
        {
            var shell = new ShellWindow();
            if (SimulateMotion)
                shell.Title = "ControlUI — Motion Simulation";
            EventLog.RecordLifecycle(SimulateMotion ? "ControlUI started in motion simulation." : "ControlUI started.");
            bool forceExit = false;
            Exit += (_, args) =>
            {
                try { MotionService?.Dispose(); }
                catch (Exception disposeError) when (forceExit)
                {
                    try { EventLog.RecordFailure("Forced shutdown cleanup", disposeError); }
                    catch (Exception logError) { System.Diagnostics.Trace.TraceError("{0}; log failed: {1}", disposeError, logError); }
                }
                finally
                {
                    AppDomain.CurrentDomain.UnhandledException -= OnDomainFailure;
                    TaskScheduler.UnobservedTaskException -= OnTaskFailure;
                    Workspace.Save();
                    EventLog.RecordLifecycle($"ControlUI exited. Exit code: {args.ApplicationExitCode}.");
                }
            };
            bool closing = false;
            bool exitApproved = false;
            shell.Closing += async (_, args) =>
            {
                if (exitApproved || MotionService is not { } motion)
                    return;

                args.Cancel = true;
                if (closing)
                    return;

                closing = true;
                try
                {
                    await Task.Run(motion.StopForShutdown);
                }
                catch (Exception ex)
                {
                    await LogShutdownAsync("Error", $"Shutdown blocked: {ex}");
                    var dialog = new ShutdownBlockedDialog(shell, ex.Message);
                    if (dialog.ShowDialog() != true)
                    {
                        closing = false;
                        return;
                    }

                    forceExit = true;
                    try
                    {
                        await Task.Run(motion.StopForUnhandledException);
                        await LogShutdownAsync("Warning", "Forced exit requested. E-stop command returned; standstill unconfirmed.");
                    }
                    catch (Exception stopError)
                    {
                        await LogShutdownAsync("Error", $"Forced exit requested. E-stop failed: {stopError}");
                    }
                }

                exitApproved = true;
                // WPF must finish processing the canceled Closing event before a second Close.
                _ = shell.Dispatcher.InvokeAsync(shell.Close);
            };
            return shell;
        }
    }
}
