using System.Windows.Controls;
using System.Windows.Input;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für QuickSettingsView.xaml
    /// </summary>
    public partial class QuickSettingsView : UserControl
    {
        public QuickSettingsView()
        {
            InitializeComponent();
        }

        private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Every control on this page is a single value; the wheel here only ever means scroll.
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 2.0);
            e.Handled = true;
        }
    }
}
