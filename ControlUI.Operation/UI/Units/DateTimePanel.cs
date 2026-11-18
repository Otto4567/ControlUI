using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ControlUI.Operation.UI.Units
{
    public class DateTimePanel : Control
    {
        private readonly DispatcherTimer _timer;

        public static readonly DependencyProperty DateTextProperty =
            DependencyProperty.Register(nameof(DateText), typeof(string),
                typeof(DateTimePanel), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TimeTextProperty =
            DependencyProperty.Register(nameof(TimeText), typeof(string),
                typeof(DateTimePanel), new PropertyMetadata(string.Empty));

        public string DateText
        {
            get => (string)GetValue(DateTextProperty);
            private set => SetValue(DateTextProperty, value);
        }

        public string TimeText
        {
            get => (string)GetValue(TimeTextProperty);
            private set => SetValue(TimeTextProperty, value);
        }

        static DateTimePanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DateTimePanel),
                new FrameworkPropertyMetadata(typeof(DateTimePanel)));
        }

        public DateTimePanel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateTime();
            Loaded += (_, _) => { UpdateTime(); _timer.Start(); };
            Unloaded += (_, _) => _timer.Stop();
        }

        private void UpdateTime()
        {
            DateTime now = DateTime.Now;
            DateText = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
