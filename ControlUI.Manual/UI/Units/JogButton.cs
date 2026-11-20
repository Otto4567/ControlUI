using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ControlUI.Manual.UI.Units;

public sealed class JogButton : Button
{
    public static readonly DependencyProperty ReleaseCommandProperty =
        DependencyProperty.Register(nameof(ReleaseCommand), typeof(ICommand), typeof(JogButton));

    public ICommand? ReleaseCommand
    {
        get => (ICommand?)GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    private bool _held;

    public JogButton()
    {
        Unloaded += (_, _) => ReleaseJog();
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled)
                ReleaseJog();
        };
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsEnabled || _held || Command?.CanExecute(CommandParameter) != true)
            return;
        Focus();
        if (!CaptureMouse())
            return;
        _held = true;
        Command.Execute(CommandParameter);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        ReleaseJog();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ReleaseJog();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ReleaseJog();
    }

    protected override void OnClick()
    {
        // Continuous jog requires a held pointer and a matching release.
    }

    private void ReleaseJog()
    {
        if (!_held)
            return;
        _held = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (ReleaseCommand?.CanExecute(CommandParameter) == true)
            ReleaseCommand.Execute(CommandParameter);
    }
}
