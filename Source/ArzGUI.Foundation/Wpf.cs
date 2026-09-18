using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;
using DrawingSize = System.Drawing.Size;

namespace CodectoryCore.UI.Wpf
{
    public interface IDialogService
    {
        void ShowDialogModal(DialogViewModelBase viewModel);
        void ShowDialogModal(DialogViewModelBase viewModel, DrawingSize size);
    }

    public class BaseViewModel : INotifyPropertyChanged
    {
        public IDialogService DialogService { get; set; }
        protected bool ThrowOnInvalidPropertyName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            VerifyPropertyName(propertyName);
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        public void VerifyPropertyName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || GetType().GetProperty(propertyName) != null)
                return;
            string message = "Invalid property name: " + propertyName;
            if (ThrowOnInvalidPropertyName)
                throw new ArgumentException(message);
            Debug.WriteLine(message);
        }
    }

    public class DialogViewModelBase : BaseViewModel
    {
        private string _title;

        public System.Windows.Window Owner { get; set; }
        public string Title
        {
            get { return _title; }
            set { _title = value; OnPropertyChanged(); }
        }

        public void CloseDialog(System.Windows.Window window)
        {
            if (window != null)
                window.Close();
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter == null ? default(T) : (T)parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class MainWindowBase : System.Windows.Window
    {
        public static readonly DependencyProperty DialogServiceProperty = DependencyProperty.Register(
            nameof(DialogService), typeof(IDialogService), typeof(MainWindowBase),
            new PropertyMetadata(null, OnDialogServiceChanged));

        public IDialogService DialogService
        {
            get { return (IDialogService)GetValue(DialogServiceProperty); }
            set { SetValue(DialogServiceProperty, value); }
        }

        public MainWindowBase()
        {
            DataContextChanged += (sender, args) => AssignDialogService(args.NewValue, DialogService);
        }

        private static void OnDialogServiceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            MainWindowBase window = (MainWindowBase)sender;
            AssignDialogService(window.DataContext, args.NewValue as IDialogService);
        }

        private static void AssignDialogService(object dataContext, IDialogService service)
        {
            BaseViewModel viewModel = dataContext as BaseViewModel;
            if (viewModel != null)
                viewModel.DialogService = service;
        }
    }

    public class UserControlBase : UserControl
    {
        public static readonly DependencyProperty DialogServiceProperty = DependencyProperty.Register(
            nameof(DialogService), typeof(IDialogService), typeof(UserControlBase),
            new PropertyMetadata(null, OnDialogServiceChanged));

        public IDialogService DialogService
        {
            get { return (IDialogService)GetValue(DialogServiceProperty); }
            set { SetValue(DialogServiceProperty, value); }
        }

        public UserControlBase()
        {
            DataContextChanged += (sender, args) => AssignDialogService(args.NewValue, DialogService);
        }

        private static void OnDialogServiceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            UserControlBase control = (UserControlBase)sender;
            AssignDialogService(control.DataContext, args.NewValue as IDialogService);
        }

        private static void AssignDialogService(object dataContext, IDialogService service)
        {
            BaseViewModel viewModel = dataContext as BaseViewModel;
            if (viewModel != null)
                viewModel.DialogService = service;
        }
    }

    public class DialogView : System.Windows.Window
    {
        public DialogView()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 420;
            MinHeight = 240;
            // Follow the theme, or a dark-theme dialog is light text on the default white window.
            SetResourceReference(BackgroundProperty, "SurfaceBrush");
            SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        }
    }

    public sealed class DialogService : IDialogService
    {
        public void ShowDialogModal(DialogViewModelBase viewModel)
        {
            Show(viewModel, null);
        }

        public void ShowDialogModal(DialogViewModelBase viewModel, DrawingSize size)
        {
            Show(viewModel, size);
        }

        private static void Show(DialogViewModelBase viewModel, DrawingSize? size)
        {
            if (viewModel == null)
                throw new ArgumentNullException(nameof(viewModel));
            DialogView window = new DialogView
            {
                DataContext = viewModel,
                Content = viewModel,
                Title = viewModel.Title ?? string.Empty,
                Owner = viewModel.Owner ?? (Application.Current == null ? null : Application.Current.MainWindow)
            };
            if (size.HasValue)
            {
                window.Width = size.Value.Width;
                window.Height = size.Value.Height;
            }
            else
            {
                window.SizeToContent = SizeToContent.WidthAndHeight;
            }
            viewModel.Owner = window.Owner;
            window.ShowDialog();
        }
    }

    public sealed class SplashScreen : System.Windows.Window, INotifyPropertyChanged
    {
        private readonly System.Windows.Controls.Image _image;
        private readonly TextBlock _text;

        public SplashScreen()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            StackPanel panel = new StackPanel { Background = Brushes.Transparent };
            // The splash art is 1672x941; at its own pixel size it covers most of a screen.
            _image = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, MaxWidth = 560 };
            _text = new TextBlock { Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(_image);
            panel.Children.Add(_text);
            Content = panel;
        }

        public string Text
        {
            get { return _text.Text; }
            set { _text.Text = value; OnPropertyChanged(); }
        }

        public ImageSource Image
        {
            get { return _image.Source; }
            set { _image.Source = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SetImageFromBitmap(Bitmap bitmap)
        {
            Image = BitmapToBitmapImageConverter.ToImageSource(bitmap);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class UIServices
    {
        public static void SetBusyState()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (Application.Current != null)
                Application.Current.Dispatcher.BeginInvoke(new Action(() => Mouse.OverrideCursor = null));
        }
    }

    public sealed class BitmapToBitmapImageConverter : IValueConverter
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ToImageSource(value as Bitmap);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        internal static ImageSource ToImageSource(Bitmap bitmap)
        {
            if (bitmap == null)
                return null;
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
    }

    public sealed class InvertBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(value, targetType, parameter, culture);
        }
    }

    public sealed class IsNotNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class IsNotZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && System.Convert.ToDouble(value, culture) != 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class VisibilityBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }

    public sealed class SizeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DrawingSize size = value is DrawingSize ? (DrawingSize)value : DrawingSize.Empty;
            return size.IsEmpty ? string.Empty : size.Width + "x" + size.Height;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string[] parts = (value as string ?? string.Empty).ToLowerInvariant().Split('x');
            int width;
            int height;
            return parts.Length == 2 && int.TryParse(parts[0].Trim(), out width) && int.TryParse(parts[1].Trim(), out height)
                ? new DrawingSize(width, height)
                : Binding.DoNothing;
        }
    }

    public sealed class EnumLocaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;
            System.Resources.ResourceManager resources = parameter as System.Resources.ResourceManager;
            string key = value.ToString();
            return resources == null ? key : resources.GetString(key, culture) ?? key;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class StringFormatConcatenator : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string format = parameter as string;
            return string.IsNullOrEmpty(format) ? string.Concat(values) : string.Format(culture, format, values);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class TupleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return value as object[];
        }
    }

    public sealed class ValueConverterCollection : Collection<IValueConverter>
    {
    }

    [ContentProperty(nameof(Converters))]
    public sealed class ConverterChain : IValueConverter
    {
        public ValueConverterCollection Converters { get; private set; } = new ValueConverterCollection();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Converters.Aggregate(value, (current, converter) => converter.Convert(current, targetType, parameter, culture));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Converters.Reverse().Aggregate(value, (current, converter) => converter.ConvertBack(current, targetType, parameter, culture));
        }
    }
}
