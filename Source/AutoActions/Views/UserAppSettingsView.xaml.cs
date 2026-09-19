using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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

        /// <summary>
        /// A PasswordBox cannot be bound, so the OBS password is copied in and out by hand. It is
        /// masked rather than a text box because this page ends up in screenshots.
        /// </summary>
        private void ObsPasswordBox_Loaded(object sender, RoutedEventArgs e)
        {
            UserAppSettings settings = DataContext as UserAppSettings;
            if (settings == null)
                return;
            if (ObsPasswordBox.Password != settings.ObsPassword)
                ObsPasswordBox.Password = settings.ObsPassword;
            // Where OBS is and whether it is running: read when the card appears, not at startup.
            settings.RefreshObsInstall();
            settings.RefreshLogonTask();
        }

        private void ObsPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UserAppSettings settings = DataContext as UserAppSettings;
            if (settings != null && settings.ObsPassword != ObsPasswordBox.Password)
                settings.ObsPassword = ObsPasswordBox.Password;
        }

        /// <summary>
        /// The wheel scrolls this page from anywhere on it. Taken at the page root rather than on the
        /// scroll viewer: half the page is the shortcut column, the gaps between cards have nothing
        /// in them to receive the wheel, and combo boxes and text boxes swallow it where they are.
        /// A list that can genuinely scroll keeps it.
        /// </summary>
        private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
                return;
            for (DependencyObject element = e.OriginalSource as DependencyObject; element != null; element = Parent(element))
            {
                if (ReferenceEquals(element, PageScroll))
                    break;
                ScrollViewer viewer = element as ScrollViewer;
                if (viewer != null && CanScroll(viewer, e.Delta))
                    return;
            }
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 2.0);
            e.Handled = true;
        }

        /// <summary>Whether this viewer still has room to move the way the wheel is turning.</summary>
        static bool CanScroll(ScrollViewer viewer, int delta)
        {
            if (viewer.ScrollableHeight <= 0)
                return false;
            return delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight;
        }

        static DependencyObject Parent(DependencyObject element)
        {
            // OriginalSource can be a text run, which is not in the visual tree.
            return element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }


    }
}
