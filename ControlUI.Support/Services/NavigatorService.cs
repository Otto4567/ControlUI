using System;
using System.Collections.Generic;

namespace ControlUI.Support.Services
{
    public class NavigationService
    {
        private readonly Stack<string> backStack = new();
        private readonly Stack<string> forwardStack = new();

        public string? CurrentKey { get; private set; }

        public bool CanGoBack => backStack.Count > 0;
        public bool CanGoForward => forwardStack.Count > 0;

        public event EventHandler? NavigationChanged;

        public void Navigate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (string.Equals(CurrentKey, key, StringComparison.Ordinal))
                return;

            if (CurrentKey is not null)
                backStack.Push(CurrentKey);

            CurrentKey = key;
            forwardStack.Clear();

            OnNavigationChanged();
        }

        public void GoBack()
        {
            if (!CanGoBack)
                return;

            RestoreState(backStack, forwardStack);
        }

        public void GoForward()
        {
            if (!CanGoForward)
                return;

            RestoreState(forwardStack, backStack);
        }

        private void RestoreState(
            Stack<string> source,
            Stack<string> destination)
        {
            if (CurrentKey is not null)
                destination.Push(CurrentKey);

            CurrentKey = source.Pop();

            OnNavigationChanged();
        }

        private void OnNavigationChanged()
        {
            NavigationChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}