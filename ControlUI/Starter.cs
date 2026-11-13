using System;
using ControlUI.Properties;

namespace ControlUI
{
    internal class Starter
    {
        [STAThread]
        private static void Main(string[] args)
        {
            _ = new App()
                .AddInversionModule<HelperModules>()
                .AddWireDataContext<WireDataContext>()
                .Run();
        }
    }
}