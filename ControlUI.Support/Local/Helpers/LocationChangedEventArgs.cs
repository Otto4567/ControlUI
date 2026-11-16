using System;
using ControlUI.Support.Local.Models;

namespace ControlUI.Support.Local.Helpers
{
    public class LocationChangedEventArgs : EventArgs
    {
        public FileInfoBase Current { get; }
        public LocationChangedEventArgs(FileInfoBase current)
        {
            Current = current;
        }
    }
}