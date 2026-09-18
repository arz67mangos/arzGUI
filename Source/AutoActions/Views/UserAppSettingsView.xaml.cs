using System.Windows.Controls;
using System.Windows.Input;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für UserAppSettings.xaml
    /// </summary>
    public partial class UserAppSettingsView : UserControl
    {
        public UserAppSettingsView()
        {
            InitializeComponent();
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Without this the wheel does nothing over the gaps between the cards - there is no
            // element there to receive it, so Windows sends it to whatever has keyboard focus - and
            // combo boxes and text boxes swallow it where there is one.
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 2.0);
            e.Handled = true;
        }


    }
}
