using System;
using System.Windows;
using System.Windows.Media;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Client.Services
{
    public static class ThemeManager
    {
        public static string CurrentTheme { get; private set; } = "Dark";

        public static void ApplyTheme(string theme)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                bool isLight = theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
                CurrentTheme = isLight ? "Light" : "Dark";

                if (isLight)
                {
                    app.Resources["PrimaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    app.Resources["SecondaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                    app.Resources["CardBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                    app.Resources["TextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    app.Resources["TextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                    app.Resources["BorderColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                }
                else
                {
                    app.Resources["PrimaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    app.Resources["SecondaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                    app.Resources["CardBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                    app.Resources["TextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                    app.Resources["TextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    app.Resources["BorderColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                }

                AppLogger.LogInfo("ThemeManager", $"Tema aplicado: {CurrentTheme}");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ThemeManager", "Erro ao aplicar tema", ex);
            }
        }
    }
}
