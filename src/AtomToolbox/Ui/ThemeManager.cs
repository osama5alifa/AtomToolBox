using System.Windows;
using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Desktop.Ui;

/// <summary>Swaps the palette dictionary at runtime; every brush is a DynamicResource.</summary>
public static class ThemeManager
{
    private static readonly Dictionary<AppTheme, Uri> Sources = new()
    {
        [AppTheme.Dark] = new Uri("pack://application:,,,/Ui/Design/Theme.Dark.xaml", UriKind.Absolute),
        [AppTheme.Oled] = new Uri("pack://application:,,,/Ui/Design/Theme.Oled.xaml", UriKind.Absolute),
        [AppTheme.Light] = new Uri("pack://application:,,,/Ui/Design/Theme.Light.xaml", UriKind.Absolute)
    };

    private static ResourceDictionary? _current;

    public static void Initialize(AppTheme theme)
    {
        var application = System.Windows.Application.Current;
        application.Resources.MergedDictionaries.Clear();

        _current = new ResourceDictionary { Source = Sources[theme] };
        application.Resources.MergedDictionaries.Add(_current);
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Ui/Design/Styles.xaml", UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Ui/Design/Templates.xaml", UriKind.Absolute)
        });
    }

    public static void Apply(AppTheme theme)
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = Sources[theme] };
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            var index = dictionaries.IndexOf(_current);
            AppLog.Info($"theme: apply {theme}, index={index}, count={dictionaries.Count}");
            if (index >= 0)
            {
                dictionaries[index] = replacement;
                _current = replacement;
                AppLog.Info($"theme: replaced at {index}");
                return;
            }
        }

        dictionaries.Insert(0, replacement);
        _current = replacement;
        AppLog.Info($"theme: inserted at 0 (count={dictionaries.Count})");
    }
}
