using AutoActions.Profiles.Actions;
using System.Windows;
using System.Windows.Controls;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für ObsStudioActionView.xaml
    /// </summary>
    public partial class ObsStudioActionView : UserControl
    {
        public ObsStudioActionView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Fill the drop-downs from OBS if it happens to be running. Deserialising an action must
            // not talk to OBS, so this is the view's job and not the constructor's.
            ObsStudioAction action = DataContext as ObsStudioAction;
            if (action != null)
                action.Refresh();
        }
    }
}
