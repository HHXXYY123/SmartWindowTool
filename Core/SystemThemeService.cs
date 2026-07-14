using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SmartWindowTool.Core;

internal static class SystemThemeService
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool _isInitialized;

    public static bool IsDarkTheme { get; private set; } = true;

    public static event Action<bool>? ThemeChanged;

    public static void Initialize()
    {
        if (_isInitialized) return;

        _isInitialized = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySystemTheme();
    }

    public static void Shutdown()
    {
        if (!_isInitialized) return;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _isInitialized = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Application? application = Application.Current;
        if (application == null) return;

        application.Dispatcher.BeginInvoke(ApplySystemTheme);
    }

    private static void ApplySystemTheme()
    {
        Application? application = Application.Current;
        if (application == null) return;

        bool isDarkTheme = ReadDarkThemePreference();
        ApplicationTheme applicationTheme = isDarkTheme
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;

        ApplicationThemeManager.Apply(applicationTheme, WindowBackdropType.None, true);
        ReplacePaletteDictionary(application, isDarkTheme);

        IsDarkTheme = isDarkTheme;
        ThemeChanged?.Invoke(isDarkTheme);
    }

    private static bool ReadDarkThemePreference()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0;
            }
        }
        catch
        {
            // Fall back to WPF-UI's system theme detection.
        }

        return ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark;
    }

    private static void ReplacePaletteDictionary(Application application, bool isDarkTheme)
    {
        ResourceDictionary dictionaries = application.Resources;
        ResourceDictionary? currentPalette = dictionaries.MergedDictionaries.FirstOrDefault(
            dictionary => dictionary.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);

        string palettePath = isDarkTheme ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        if (currentPalette?.Source?.OriginalString.EndsWith(palettePath, StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var replacement = new ResourceDictionary
        {
            Source = new Uri(palettePath, UriKind.Relative)
        };

        if (currentPalette == null)
        {
            dictionaries.MergedDictionaries.Add(replacement);
            return;
        }

        int index = dictionaries.MergedDictionaries.IndexOf(currentPalette);
        dictionaries.MergedDictionaries.RemoveAt(index);
        dictionaries.MergedDictionaries.Insert(index, replacement);
    }
}
