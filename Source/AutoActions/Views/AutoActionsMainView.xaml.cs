using AutoActions.Properties;
using CodectoryCore.UI.Wpf;
using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AutoActions.Views
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class AutoActionsMainView : MainWindowBase
    {
        readonly object _listResizeLock = new object();
        bool _hasAnimatedIn;
        public AutoActionsMainView()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Size size = new Size(Width, Height);
            Globals.Instance.Settings.WindowSize = size;
            this.Hide();
        }

 


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Globals.Instance.SettingsLoaded += Instance_SettingsLoaded;
                if (Globals.Instance.SettingsLoadedOnce)
                {
                    Width = Globals.Instance.Settings.WindowSize.Width;
                    Height = Globals.Instance.Settings.WindowSize.Height;
                }
            }
            catch  { }       

            if (!_hasAnimatedIn && SystemParameters.ClientAreaAnimation)
            {
                _hasAnimatedIn = true;
                MainGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                MainGridTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
        }

        private void Instance_SettingsLoaded(object sender, EventArgs e)
        {
            Width = Globals.Instance.Settings.WindowSize.Width;
            Height = Globals.Instance.Settings.WindowSize.Height;
        }
    }
}
