using System.Windows;

namespace AutoActions.Theming
{
    /// <summary>
    /// Attached properties for the sidebar navigation style (SidebarTabControl / SidebarTabItem in
    /// Controls/2_DefaultControls.xaml): the block above the nav items, the block below them, and
    /// the icon glyph per item. Presentation only; no behaviour.
    /// </summary>
    public static class Sidebar
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.RegisterAttached("Header", typeof(object), typeof(Sidebar), new PropertyMetadata(null));
        public static object GetHeader(DependencyObject obj) => obj.GetValue(HeaderProperty);
        public static void SetHeader(DependencyObject obj, object value) => obj.SetValue(HeaderProperty, value);

        public static readonly DependencyProperty FooterProperty =
            DependencyProperty.RegisterAttached("Footer", typeof(object), typeof(Sidebar), new PropertyMetadata(null));
        public static object GetFooter(DependencyObject obj) => obj.GetValue(FooterProperty);
        public static void SetFooter(DependencyObject obj, object value) => obj.SetValue(FooterProperty, value);

        /// <summary>A Segoe MDL2 Assets glyph, e.g. "&#xE80F;".</summary>
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Sidebar), new PropertyMetadata(string.Empty));
        public static string GetIcon(DependencyObject obj) => (string)obj.GetValue(IconProperty);
        public static void SetIcon(DependencyObject obj, string value) => obj.SetValue(IconProperty, value);
    }
}
