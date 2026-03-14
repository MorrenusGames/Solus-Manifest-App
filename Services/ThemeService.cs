using SolusManifestApp.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SolusManifestApp.Services
{
    public class ThemeService
    {
        public void ApplyTheme(AppTheme theme, AppSettings? settings = null)
        {
            if (theme == AppTheme.Custom && settings != null)
            {
                ApplyCustomTheme(settings);
                return;
            }

            var themeFile = GetThemeFileName(theme);
            var themeUri = new Uri($"pack://application:,,,/Resources/Themes/{themeFile}", UriKind.Absolute);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var otherDictionaries = Application.Current.Resources.MergedDictionaries
                    .Skip(1)
                    .ToList();

                Application.Current.Resources.MergedDictionaries.Clear();

                var newTheme = new ResourceDictionary { Source = themeUri };
                Application.Current.Resources.MergedDictionaries.Add(newTheme);

                foreach (var dict in otherDictionaries)
                {
                    Application.Current.Resources.MergedDictionaries.Add(dict);
                }
            });
        }

        public void ApplyCustomTheme(AppSettings settings)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dict = new ResourceDictionary();

                var primaryDark = ColorFromHex(settings.CustomPrimaryDark);
                var secondaryDark = ColorFromHex(settings.CustomSecondaryDark);
                var cardBg = ColorFromHex(settings.CustomCardBackground);
                var cardHover = ColorFromHex(settings.CustomCardHover);
                var accent = ColorFromHex(settings.CustomAccent);
                var accentHover = ColorFromHex(settings.CustomAccentHover);
                var textPrimary = ColorFromHex(settings.CustomTextPrimary);
                var textSecondary = ColorFromHex(settings.CustomTextSecondary);
                var borderColor = BlendColor(secondaryDark, accent, 0.3);
                var success = ColorFromHex("#5cb85c");
                var warning = ColorFromHex("#f0ad4e");
                var danger = ColorFromHex("#d9534f");

                dict["PrimaryDark"] = primaryDark;
                dict["SecondaryDark"] = secondaryDark;
                dict["CardBackground"] = cardBg;
                dict["CardHover"] = cardHover;
                dict["Accent"] = accent;
                dict["AccentHover"] = accentHover;
                dict["TextPrimary"] = textPrimary;
                dict["TextSecondary"] = textSecondary;
                dict["BorderColor"] = borderColor;
                dict["Success"] = success;
                dict["Warning"] = warning;
                dict["Danger"] = danger;

                dict["PrimaryDarkBrush"] = new SolidColorBrush(primaryDark);
                dict["SecondaryDarkBrush"] = new SolidColorBrush(secondaryDark);
                dict["CardBackgroundBrush"] = new SolidColorBrush(cardBg);
                dict["CardHoverBrush"] = new SolidColorBrush(cardHover);
                dict["AccentBrush"] = new SolidColorBrush(accent);
                dict["AccentHoverBrush"] = new SolidColorBrush(accentHover);
                dict["TextPrimaryBrush"] = new SolidColorBrush(textPrimary);
                dict["TextSecondaryBrush"] = new SolidColorBrush(textSecondary);
                dict["SuccessBrush"] = new SolidColorBrush(success);
                dict["WarningBrush"] = new SolidColorBrush(warning);
                dict["DangerBrush"] = new SolidColorBrush(danger);
                dict["BorderBrush"] = new SolidColorBrush(borderColor);

                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                gradient.GradientStops.Add(new GradientStop(accent, 0));
                gradient.GradientStops.Add(new GradientStop(accentHover, 1));
                dict["AccentGradientBrush"] = gradient;

                var otherDictionaries = Application.Current.Resources.MergedDictionaries
                    .Skip(1)
                    .ToList();

                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);

                foreach (var d in otherDictionaries)
                {
                    Application.Current.Resources.MergedDictionaries.Add(d);
                }
            });
        }

        private static Color ColorFromHex(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return Colors.Gray;
            }
        }

        private static Color BlendColor(Color a, Color b, double ratio)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * ratio),
                (byte)(a.G + (b.G - a.G) * ratio),
                (byte)(a.B + (b.B - a.B) * ratio));
        }

        private string GetThemeFileName(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Default => "DefaultTheme.xaml",
                AppTheme.Dark => "DarkTheme.xaml",
                AppTheme.Light => "LightTheme.xaml",
                AppTheme.Cherry => "CherryTheme.xaml",
                AppTheme.Sunset => "SunsetTheme.xaml",
                AppTheme.Forest => "ForestTheme.xaml",
                AppTheme.Grape => "GrapeTheme.xaml",
                AppTheme.Cyberpunk => "CyberpunkTheme.xaml",
                AppTheme.Pink => "PinkTheme.xaml",
                AppTheme.Pastel => "PastelTheme.xaml",
                AppTheme.Rainbow => "RainbowTheme.xaml",
                _ => "DefaultTheme.xaml"
            };
        }
    }
}
