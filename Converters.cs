using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PrinterTool;

public static class Converters
{
    public static readonly IValueConverter HideIfEmpty = new HideIfEmptyConverter();
    public static readonly IValueConverter ShowIfEmpty = new ShowIfEmptyConverter();
    public static readonly IValueConverter HiddenIfFalse = new HiddenIfFalseConverter();
    public static readonly IValueConverter ShowIfTrue = new ShowIfTrueConverter();
    public static readonly IValueConverter Invert = new InvertConverter();
    public static readonly IValueConverter HideIfTrue = new HideIfTrueConverter();

    private sealed class HiddenIfFalseConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) =>
            v is true ? Visibility.Visible : Visibility.Hidden;   // Hidden keeps the layout slot
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private sealed class ShowIfEmptyConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) =>
            string.IsNullOrEmpty(v as string) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private sealed class HideIfEmptyConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) =>
            string.IsNullOrWhiteSpace(v as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private sealed class ShowIfTrueConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) =>
            v is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private sealed class HideIfTrueConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) =>
            v is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    private sealed class InvertConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c) => v is not true;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is not true;
    }
}
