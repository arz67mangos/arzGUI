using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für UserAppSettings.xaml
    /// </summary>
    public partial class StatusView : UserControl
    {
        public StatusView()
        {
            InitializeComponent();
            IsVisibleChanged += StatusView_IsVisibleChanged;
        }

        // The mic monitoring card must show what the hardware says right now, not what it said when
        // the app started, so re-read it every time the view is brought back into view.
        private void StatusView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool visible) || !visible)
                return;
            AutoActionsDaemon daemon = DataContext as AutoActionsDaemon;
            if (daemon != null && daemon.MicMonitoringStatus != null)
                daemon.MicMonitoringStatus.Refresh();
        }
    }
}
