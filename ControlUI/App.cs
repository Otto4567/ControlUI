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
            return shell;
        }
    }
}
