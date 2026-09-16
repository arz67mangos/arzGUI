using System.Windows.Controls;
using System.Windows.Input;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für UserAppSettings.xaml
    /// </summary>
    public partial class ActionShortcutManagerView : UserControl
    {
        public ActionShortcutManagerView()
        {
            InitializeComponent();
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        /// <summary>
        /// Records the pressed combination onto the shortcut. Everything is handled, so Tab, Space and
        /// the arrows are captured like any other key instead of moving focus or scrolling the list.
        /// </summary>
        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox box = sender as TextBox;
            ProfileActionShortcut shortcut = box != null ? box.DataContext as ProfileActionShortcut : null;
            if (shortcut == null)
                return;
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Back || key == Key.Delete || key == Key.Escape)
            {
                shortcut.Hotkey = string.Empty;
                return;
            }

            // Null while only modifiers are down, which is every keystroke on the way to a real one.
            string hotkey = HotkeyManager.Format(Keyboard.Modifiers, key);
            if (hotkey != null)
                shortcut.Hotkey = hotkey;
        }


    }
}
