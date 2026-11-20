using Jamesnet.Wpf.Controls;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ControlUI.Support.UI.Units
{
    public class DarkWindow : JamesWindow
    {
        private const int CaptionColor = 35;
        private const int CaptionTextColor = 36;
        private const int CaptionButtonBounds = 5;

        public DarkWindow()
        {
            Loaded += (_, _) => UpdateCaptionLayout();
            SizeChanged += (_, _) => UpdateCaptionLayout();
            StateChanged += (_, _) => Dispatcher.InvokeAsync(UpdateCaptionLayout, DispatcherPriority.Loaded);
            DpiChanged += (_, _) => Dispatcher.InvokeAsync(UpdateCaptionLayout, DispatcherPriority.Loaded);
        }

        private static readonly DependencyPropertyKey CaptionButtonReservePropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CaptionButtonReserve), typeof(GridLength), typeof(DarkWindow),
                new PropertyMetadata(new GridLength(160)));
        public static readonly DependencyProperty CaptionButtonReserveProperty = CaptionButtonReservePropertyKey.DependencyProperty;
        public GridLength CaptionButtonReserve => (GridLength)GetValue(CaptionButtonReserveProperty);

        private static readonly DependencyPropertyKey CaptionButtonSizePropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CaptionButtonSize), typeof(Size), typeof(DarkWindow),
                new PropertyMetadata(new Size(46, 30)));
        public static readonly DependencyProperty CaptionButtonSizeProperty = CaptionButtonSizePropertyKey.DependencyProperty;
        public Size CaptionButtonSize => (Size)GetValue(CaptionButtonSizeProperty);

        private static readonly DependencyPropertyKey CaptionButtonOffsetPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(CaptionButtonOffset), typeof(Thickness), typeof(DarkWindow),
                new PropertyMetadata(new Thickness()));
        public static readonly DependencyProperty CaptionButtonOffsetProperty = CaptionButtonOffsetPropertyKey.DependencyProperty;
        public Thickness CaptionButtonOffset => (Thickness)GetValue(CaptionButtonOffsetProperty);

        private void UpdateCaptionLayout()
        {
            if (!IsVisible || WindowState == WindowState.Minimized || PresentationSource.FromVisual(this) is null)
                return;
            nint handle = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(handle, out var window) ||
                DwmGetWindowAttribute(handle, CaptionButtonBounds, out var buttons, Marshal.SizeOf<NativeRect>()) != 0 ||
                buttons.Right <= buttons.Left || buttons.Bottom <= buttons.Top)
                return;

            var start = PointFromScreen(new Point(window.Left + buttons.Left, window.Top + buttons.Top));
            var end = PointFromScreen(new Point(window.Left + buttons.Right, window.Top + buttons.Bottom));
            int count = ResizeMode == ResizeMode.NoResize ? 1 : 3;
            SetValue(CaptionButtonReservePropertyKey, new GridLength(Math.Max(0, ActualWidth - start.X)));
            SetValue(CaptionButtonSizePropertyKey, new Size((end.X - start.X) / count, end.Y - start.Y));
            SetValue(CaptionButtonOffsetPropertyKey, new Thickness(0, Math.Max(0, start.Y), 0, 0));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyCaptionColors();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == BackgroundProperty || e.Property == ForegroundProperty)
                ApplyCaptionColors();
        }

        private void ApplyCaptionColors()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == 0)
                return;
            SetCaptionColor(handle, CaptionColor, Background);
            SetCaptionColor(handle, CaptionTextColor, Foreground);
        }

        private static void SetCaptionColor(nint handle, int attribute, Brush? brush)
        {
            uint value = brush is SolidColorBrush solid
                ? (uint)(solid.Color.R | solid.Color.G << 8 | solid.Color.B << 16)
                : uint.MaxValue;
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(uint));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint window, out NativeRect bounds);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        public static readonly DependencyProperty LocationTemplateProperty =
            DependencyProperty.Register("LocationTemplate", typeof(DataTemplate),
            typeof(DarkWindow), new UIPropertyMetadata(null));
        public DataTemplate LocationTemplate
        {
            get { return (DataTemplate)GetValue(LocationTemplateProperty); }
            set { SetValue(LocationTemplateProperty, value); }
        }
        public static readonly DependencyProperty LocationProperty =
            DependencyProperty.Register("Location", typeof(object),
            typeof(DarkWindow), new UIPropertyMetadata(null));
        public object Location
        {
            get { return (object)GetValue(LocationProperty); }
            set { SetValue(LocationProperty, value); }
        }
        static DarkWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DarkWindow),
                new FrameworkPropertyMetadata(typeof(DarkWindow)));
        }
    }
}
