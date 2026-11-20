using Jamesnet.Wpf.Controls;
using System.Windows;
using Prism.Ioc;

namespace ControlUI.Main.UI.Views
{
    public class MainContent : JamesContent
    {
        private readonly IContainerProvider _container;

        public MainContent(IContainerProvider container) => _container = container;

        public static readonly DependencyProperty CurrentPageKeyProperty =
            DependencyProperty.Register(nameof(CurrentPageKey), typeof(string), typeof(MainContent),
                new PropertyMetadata(null, OnCurrentPageChanged));

        private static readonly DependencyPropertyKey PageContentPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(PageContent), typeof(object), typeof(MainContent),
                new PropertyMetadata(null));

        public static readonly DependencyProperty PageContentProperty = PageContentPropertyKey.DependencyProperty;

        public string? CurrentPageKey
        {
            get => (string?)GetValue(CurrentPageKeyProperty);
            set => SetValue(CurrentPageKeyProperty, value);
        }

        public object? PageContent => GetValue(PageContentProperty);

        private static void OnCurrentPageChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var view = (MainContent)sender;
            string? registration = args.NewValue switch
            {
                "operation" => "OperationContentView",
                "manual" => "ManualContentView",
                "sequence" => "SequenceContentView",
                "settings" => "SettingsContentView",
                _ => null
            };
            view.SetValue(PageContentPropertyKey, registration is null ? null : view._container.Resolve<IViewable>(registration));
        }

        static MainContent()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MainContent),
                new FrameworkPropertyMetadata(typeof(MainContent)));
        }
    }
}
