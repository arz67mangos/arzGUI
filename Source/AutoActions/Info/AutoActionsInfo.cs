using AutoActions.ProjectResources;
using CodectoryCore;
using CodectoryCore.UI.Wpf;
using System;
using System.Diagnostics;
using System.Windows;

namespace AutoActions.Info
{
    /// <summary>
    /// The About dialog: product, version, and the credit to the upstream project. The online
    /// section (newest release, changelog, download) went with the upstream updater.
    /// </summary>
    public class AutoActionsInfo : DialogViewModelBase
    {
        public string ProductName => ProjectLocales.AutoActions;

        public Version Version { get; private set; }

        public RelayCommand OpenUpstreamCommand { get; private set; }
        public RelayCommand OpenForkCommand { get; private set; }

        public AutoActionsInfo()
        {
            Version = VersionExtension.ApplicationVersion(System.Reflection.Assembly.GetExecutingAssembly());
            Title = ProjectLocales.Info;
            OpenUpstreamCommand = new RelayCommand(() => OpenLink("UpstreamRepoLink"));
            OpenForkCommand = new RelayCommand(() => OpenLink("ForkRepoLink"));
        }

        private static void OpenLink(string resourceKey)
        {
            try
            {
                Process.Start(new ProcessStartInfo((string)Application.Current.Resources[resourceKey]) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Globals.Logs.AddException(ex);
            }
        }
    }
}
