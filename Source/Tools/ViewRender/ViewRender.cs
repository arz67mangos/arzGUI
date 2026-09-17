// Build from the repository root:
// & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /out:Source\Debug_x64\ViewRender.exe /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll /r:System.Xaml.dll Source\Tools\ViewRender\ViewRender.cs
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ViewRender
{
    [STAThread]
    static void Main(string[] args)
    {
        string outputPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(@"..\..\.impeccable\review");
        string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoActions.exe");
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Type appType = assembly.GetType("AutoActions.App", true);
        Application app = (Application)Activator.CreateInstance(appType);
        appType.GetMethod("InitializeComponent").Invoke(app, null);
        Type globalsType = assembly.GetType("AutoActions.Globals", true);
        object globals = globalsType.GetField("Instance").GetValue(null);
        globalsType.GetMethod("LoadSettings").Invoke(globals, null);
        Type windowType = assembly.GetType("AutoActions.Views.AutoActionsMainView", true);
        Directory.CreateDirectory(outputPath);
        string[] themes = { "Light", "Dark" };
        string[] pages = { "profiles", "applications", "displays", "settings" };
        int[] indices = { 2, 3, 4, 5 };
        foreach (string theme in themes)
        {
            ResourceDictionary colors = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AutoActions;component/Theming/0_" + theme + "Colors.xaml", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(colors);
            for (int i = 0; i < pages.Length; i++)
            {
                Window window = (Window)Activator.CreateInstance(windowType);
                window.Width = 1280;
                window.Height = 800;
                FrameworkElement root = (FrameworkElement)window.Content;
                root.Measure(new Size(1280, 800));
                root.Arrange(new Rect(0, 0, 1280, 800));
                TabControl tabs = Find<TabControl>(root);
                if (tabs == null) throw new InvalidOperationException("TabControl was not created.");
                tabs.SelectedIndex = indices[i];
                root.Measure(new Size(1280, 800));
                root.Arrange(new Rect(0, 0, 1280, 800));
                root.UpdateLayout();
                if (i == 0)
                {
                    ListBox profiles = Find<ListBox>(root);
                    if (profiles != null && profiles.Items.Count > 0) profiles.SelectedIndex = 0;
                    root.UpdateLayout();
                }
                RenderTargetBitmap bitmap = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = File.Create(Path.Combine(outputPath, pages[i] + "-" + theme.ToLowerInvariant() + ".png")))
                    encoder.Save(stream);
                window.DataContext = null;
            }
            app.Resources.MergedDictionaries.Remove(colors);
        }
        Environment.Exit(0);
    }

    static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            T match = child as T;
            if (match != null) return match;
            match = Find<T>(child);
            if (match != null) return match;
        }
        return null;
    }
}
