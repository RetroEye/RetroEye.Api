// RetroEye.Theming.Wpf: styles for WPF's own controls from RetroEye's contract v1 state.
#nullable enable
using RetroEye.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroEye.Theming.Wpf
{
    /// <summary>
    /// Styles WPF's standard controls (buttons, check and radio boxes, text boxes, combo boxes, lists, scroll bars,
    /// menus, tabs, progress bars...) like the Windows version RetroEye shows: the WPF theme that matches its look
    /// (Classic, Luna in its three colours, Royale or Aero, which ship with .NET itself) with Windows' colour slots
    /// (SystemColors) set to the version's 31 colours, and the version's fonts under the keys in
    /// <see cref="RetroEyeKeys"/>. <see cref="Follow"/> keeps an application's resources in step as RetroEye changes.
    /// </summary>
    public static class RetroEyeTheme
    {
        /// <summary>Marks the dictionary this class made, so it can be found and replaced.</summary>
        private const string Marker = "RetroEye.Theming.Wpf.Dictionary";

        /// <summary>
        /// Makes the resource dictionary for a state: the matching WPF theme merged in, Windows' colour keys set to the
        /// version's colours, and RetroEye's own keys (fonts, metrics, colours by contract name).
        /// </summary>
        public static ResourceDictionary Build(ThemeState state)
        {
            var dictionary = new ResourceDictionary();
            dictionary[Marker] = true;
            if (ThemeDictionary(state.VisualStyle) is ResourceDictionary look)
            {
                dictionary.MergedDictionaries.Add(look);
            }
            foreach (KeyValuePair<string, ApiColor> slot in state.Colors)
            {
                dictionary[RetroEyeKeys.Color(slot.Key)] = Brush(slot.Value);
            }
            void Slot(ResourceKey brushKey, ResourceKey colorKey, string name)
            {
                if (state.Colors.TryGetValue(name, out ApiColor c))
                {
                    dictionary[brushKey] = Brush(c);
                    dictionary[colorKey] = Color.FromRgb(c.R, c.G, c.B);
                }
            }
            Slot(SystemColors.ControlBrushKey, SystemColors.ControlColorKey, "buttonFace");
            Slot(SystemColors.ControlTextBrushKey, SystemColors.ControlTextColorKey, "buttonText");
            Slot(SystemColors.ControlLightBrushKey, SystemColors.ControlLightColorKey, "buttonLight");
            Slot(SystemColors.ControlLightLightBrushKey, SystemColors.ControlLightLightColorKey, "buttonHighlight");
            Slot(SystemColors.ControlDarkBrushKey, SystemColors.ControlDarkColorKey, "buttonShadow");
            Slot(SystemColors.ControlDarkDarkBrushKey, SystemColors.ControlDarkDarkColorKey, "buttonDarkShadow");
            Slot(SystemColors.WindowBrushKey, SystemColors.WindowColorKey, "window");
            Slot(SystemColors.WindowTextBrushKey, SystemColors.WindowTextColorKey, "windowText");
            Slot(SystemColors.WindowFrameBrushKey, SystemColors.WindowFrameColorKey, "windowFrame");
            Slot(SystemColors.HighlightBrushKey, SystemColors.HighlightColorKey, "highlight");
            Slot(SystemColors.HighlightTextBrushKey, SystemColors.HighlightTextColorKey, "highlightText");
            Slot(SystemColors.GrayTextBrushKey, SystemColors.GrayTextColorKey, "grayText");
            Slot(SystemColors.MenuBrushKey, SystemColors.MenuColorKey, "menu");
            Slot(SystemColors.MenuTextBrushKey, SystemColors.MenuTextColorKey, "menuText");
            Slot(SystemColors.MenuBarBrushKey, SystemColors.MenuBarColorKey, "menuBar");
            Slot(SystemColors.MenuHighlightBrushKey, SystemColors.MenuHighlightColorKey, "menuHighlight");
            Slot(SystemColors.InfoBrushKey, SystemColors.InfoColorKey, "infoBackground");
            Slot(SystemColors.InfoTextBrushKey, SystemColors.InfoTextColorKey, "infoText");
            Slot(SystemColors.ScrollBarBrushKey, SystemColors.ScrollBarColorKey, "scrollBar");
            Slot(SystemColors.ActiveCaptionBrushKey, SystemColors.ActiveCaptionColorKey, "activeCaption");
            Slot(SystemColors.InactiveCaptionBrushKey, SystemColors.InactiveCaptionColorKey, "inactiveCaption");
            Slot(SystemColors.ActiveCaptionTextBrushKey, SystemColors.ActiveCaptionTextColorKey, "captionText");
            Slot(SystemColors.InactiveCaptionTextBrushKey, SystemColors.InactiveCaptionTextColorKey, "inactiveCaptionText");
            Slot(SystemColors.ActiveBorderBrushKey, SystemColors.ActiveBorderColorKey, "activeBorder");
            Slot(SystemColors.InactiveBorderBrushKey, SystemColors.InactiveBorderColorKey, "inactiveBorder");
            Slot(SystemColors.AppWorkspaceBrushKey, SystemColors.AppWorkspaceColorKey, "appWorkspace");
            Slot(SystemColors.DesktopBrushKey, SystemColors.DesktopColorKey, "desktop");
            Slot(SystemColors.HotTrackBrushKey, SystemColors.HotTrackColorKey, "hotLight");
            Slot(SystemColors.GradientActiveCaptionBrushKey, SystemColors.GradientActiveCaptionColorKey, "gradientActiveCaption");
            Slot(SystemColors.GradientInactiveCaptionBrushKey, SystemColors.GradientInactiveCaptionColorKey, "gradientInactiveCaption");

            foreach (KeyValuePair<string, ApiFont> font in state.Fonts)
            {
                dictionary[RetroEyeKeys.FontFamily(font.Key)] = new FontFamily(font.Value.DrawnFace);
                dictionary[RetroEyeKeys.FontSize(font.Key)] = font.Value.SizePixels;
                dictionary[RetroEyeKeys.FontWeight(font.Key)] = font.Value.Bold ? FontWeights.Bold : FontWeights.Normal;
            }
            foreach (KeyValuePair<string, double> metric in state.Metrics)
            {
                dictionary[RetroEyeKeys.Metric(metric.Key)] = metric.Value;
            }
            dictionary[RetroEyeKeys.State] = state;
            return dictionary;
        }

        /// <summary>Returns WPF's own theme for a look ("Classic", "Luna.NormalColor"...), which ships with .NET, or null if it can't be loaded.</summary>
        public static ResourceDictionary? ThemeDictionary(string visualStyle)
        {
            string assembly = visualStyle.StartsWith("Luna", StringComparison.Ordinal) ? "Luna"
                : visualStyle.StartsWith("Royale", StringComparison.Ordinal) ? "Royale"
                : visualStyle.StartsWith("Aero", StringComparison.Ordinal) ? "Aero"
                : "Classic";
            string name = assembly == "Classic" ? "Classic" : visualStyle;
            try
            {
                return new ResourceDictionary { Source = new Uri($"/PresentationFramework.{assembly};component/themes/{name}.xaml", UriKind.Relative) };
            }
            catch (Exception ex) when (ex is IOException or System.Windows.Markup.XamlParseException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>Puts the state's dictionary into a resource dictionary (an application's or a window's), replacing the one it had.</summary>
        public static void Apply(ResourceDictionary resources, ThemeState state)
        {
            ResourceDictionary fresh = Build(state);
            for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (resources.MergedDictionaries[i].Contains(Marker))
                {
                    resources.MergedDictionaries.RemoveAt(i);
                }
            }
            resources.MergedDictionaries.Add(fresh);
        }

        /// <summary>
        /// Styles an application from the client's state now and again at every change RetroEye tells of. The client
        /// should be made on the application's thread, so its events come there.
        /// </summary>
        public static void Follow(Application application, RetroEyeClient client)
        {
            Apply(application.Resources, client.State);
            client.StateChanged += (s, e) => application.Dispatcher.BeginInvoke(new Action(() => Apply(application.Resources, client.State)));
            client.Started += (s, e) => application.Dispatcher.BeginInvoke(new Action(() => Apply(application.Resources, client.State)));
        }

        /// <summary>
        /// Returns how many of the picture parts' pixels make one of the version's own pixels: the scale RetroEye drew
        /// them at (scale.factor), or 1 for a state without parts.
        /// </summary>
        public static double PixelsPerUnit(ThemeState? state) => state == null || state.Parts.Count == 0 ? 1 : Math.Max(0.5, state.Scale);

        /// <summary>
        /// Returns how much bigger than WPF's own units a window is drawn to be as big as RetroEye's own windows: RetroEye's
        /// scale (screen pixels for each of the version's pixels, scale.factor) over the scale of the screen the window
        /// is on (its DPI / 96). On a 125% screen where RetroEye draws at 2 that is 1.6, so the window comes out at 2
        /// screen pixels for each of the version's, crisp. 1 for a state without parts (RetroEye never seen).
        /// </summary>
        public static double LayoutScale(ThemeState? state, double screenScale) =>
            state == null || state.Parts.Count == 0 ? 1 : Math.Max(0.5, state.Scale / Math.Max(0.5, screenScale));

        /// <summary>Returns a frozen brush of a colour.</summary>
        internal static SolidColorBrush Brush(ApiColor color)
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        /// <summary>Returns a fill (solid or gradient) as a frozen brush.</summary>
        internal static Brush Fill(ApiFill fill)
        {
            if (fill.Stops.Count == 1 || fill.Kind == "solid")
            {
                return fill.Stops.Count > 0 ? Brush(fill.Stops[0].Color) : Brushes.Gray;
            }
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = fill.Kind == "vertical" ? new Point(0, 1) : new Point(1, 0),
            };
            foreach (ApiGradientStop stop in fill.Stops)
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B), stop.Offset));
            }
            gradient.Freeze();
            return gradient;
        }

        /// <summary>Loads a part's picture without keeping its file open (RetroEye replaces it when the look changes); null if it can't.</summary>
        internal static BitmapSource? Load(ApiPart? part)
        {
            if (part == null || !File.Exists(part.FullPath))
            {
                return null;
            }
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(part.FullPath);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>The resource keys <see cref="RetroEyeTheme"/> sets, for an app's own XAML: {DynamicResource {x:Static ...}}.</summary>
    public static class RetroEyeKeys
    {
        /// <summary>The key of the state itself (a ThemeState).</summary>
        public static readonly string State = "RetroEye.State";

        /// <summary>The key of a colour slot's brush by its contract name, such as "RetroEye.Color.buttonFace".</summary>
        public static string Color(string name) => "RetroEye.Color." + name;

        /// <summary>The key of a font's family by name (caption, smallCaption, menu, status, message, icon).</summary>
        public static string FontFamily(string name) => "RetroEye.Font." + name + ".Family";

        /// <summary>The key of a font's size in WPF's units (pixels at 96 DPI).</summary>
        public static string FontSize(string name) => "RetroEye.Font." + name + ".Size";

        /// <summary>The key of a font's weight.</summary>
        public static string FontWeight(string name) => "RetroEye.Font." + name + ".Weight";

        /// <summary>The key of a metric by name, such as "RetroEye.Metric.scrollBarWidth".</summary>
        public static string Metric(string name) => "RetroEye.Metric." + name;
    }
}
