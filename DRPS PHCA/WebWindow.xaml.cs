using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DRPS_PHCA
{
    public sealed partial class WebWindow : Window
    {
        public WebWindow(string title, string url)
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;

            // Custom title bar — no icon, no title text
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragArea);

            // Remove Minimize and Maximize buttons
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            // Size and center
            AppWindow.Resize(new SizeInt32(900, 650));
            App.CenterWindow(this);

            // Navigate — loading overlay is visible by default
            ContentWebView.NavigationCompleted += OnNavigationCompleted;
            ContentWebView.CoreWebView2Initialized += OnCoreWebView2Initialized;
            ContentWebView.Source = new System.Uri(url);
        }

        private void OnCoreWebView2Initialized(WebView2 sender, object _)
        {
            sender.CoreWebView2.Settings.AreDevToolsEnabled = false;
        }

        private void OnNavigationCompleted(WebView2 sender, object _)
        {
            DispatcherQueue.TryEnqueue(() => LoadingOverlay.Visibility = Visibility.Collapsed);
        }
    }
}
