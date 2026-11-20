using System;
using ControlUI.Properties;

namespace ControlUI
{
    internal class Starter
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool simulateMotion = Array.Exists(args, arg => arg.Equals("--simulate-motion", StringComparison.OrdinalIgnoreCase));
            bool simulateAlarm = Array.Exists(args, arg => arg.Equals("--simulate-alarm", StringComparison.OrdinalIgnoreCase));
            _ = new App(simulateMotion, simulateAlarm)
                .AddInversionModule<HelperModules>()
                .AddInversionModule<ViewModules>()
                .AddWireDataContext<WireDataContext>()
                .Run();
        }
    }
}
