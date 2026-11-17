// RetroEye app API sample: a WPF window framed and styled like the Windows version RetroEye shows, switching live.
#nullable enable
using RetroEye.Api;
using RetroEye.Theming.Wpf;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroEye.Samples.Wpf
{
    /// <summary>
    /// Starts the sample. Arguments (all optional): --folder &lt;api\v1 folder&gt; and --channel &lt;name&gt; to find a
    /// RetroEye other than the real one; --left/--top to place the window; --shot &lt;file.png&gt; to save a picture of the
    /// window once drawn and close.
    /// </summary>
    public static class Program
    {
        /// <summary>The sample's entry point.</summary>
        [STAThread]
        public static void Main(string[] args)
        {
            string? Arg(string name)
            {
                int at = Array.IndexOf(args, name);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var client = new RetroEyeClient("RetroEye WPF sample", new RetroEyeClientOptions { Folder = Arg("--folder"), Channel = Arg("--channel") });
            RetroEyeTheme.Follow(app, client);
            client.Start();
            var window = new SampleWindow { Client = client };
            if (Arg("--left") is string left && Arg("--top") is string top)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = double.Parse(left, System.Globalization.CultureInfo.InvariantCulture);
                window.Top = double.Parse(top, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (Arg("--shot") is string shot)
            {
                // For pictures of the sample: drawn off screen, active and inactive one above the other, never shown
                double dpi = Arg("--dpi") is string d ? double.Parse(d, System.Globalization.CultureInfo.InvariantCulture) : 96;
                Save(window.RenderPreview(true, dpi), window.RenderPreview(false, dpi), shot);
                window.Close();
                client.Dispose();
                return;
            }
            if (Environment.GetEnvironmentVariable("RETROEYE_SAMPLE_OFFSCREEN") == "1")
            {
                // A picture-taking run that lost its --shot: never show a window
                Console.Error.WriteLine("RETROEYE_SAMPLE_OFFSCREEN is set and there is no --shot: not showing the window.");
                client.Dispose();
                return;
            }
            app.Run(window);
            client.Dispose();
        }

        /// <summary>Saves two pictures one above the other as a PNG.</summary>
        private static void Save(BitmapSource top, BitmapSource bottom, string file)
        {
            var both = new DrawingVisual();
            using (DrawingContext drawing = both.RenderOpen())
            {
                drawing.DrawImage(top, new Rect(0, 0, top.PixelWidth, top.PixelHeight));
                drawing.DrawImage(bottom, new Rect(0, top.PixelHeight + 8, bottom.PixelWidth, bottom.PixelHeight));
            }
            var picture = new RenderTargetBitmap(Math.Max(top.PixelWidth, bottom.PixelWidth), top.PixelHeight + bottom.PixelHeight + 8, 96, 96, PixelFormats.Pbgra32);
            picture.Render(both);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
    }

    /// <summary>The sample window: a RetroWindow with a menu, tabs and one of each standard control.</summary>
    public sealed class SampleWindow : RetroWindow
    {
        private readonly TextBlock _status = new() { Margin = new Thickness(4, 2, 4, 2) };

        /// <summary>Lays the window out.</summary>
        public SampleWindow()
        {
            Width = 440;
            Height = 330;
            var menu = new Menu();
            var file = new MenuItem { Header = "_File" };
            file.Items.Add(new MenuItem { Header = "_New" });
            file.Items.Add(new MenuItem { Header = "_Open..." });
            file.Items.Add(new Separator());
            var exit = new MenuItem { Header = "E_xit" };
            exit.Click += (s, e) => Close();
            file.Items.Add(exit);
            menu.Items.Add(file);
            menu.Items.Add(new MenuItem { Header = "_Edit" });
            menu.Items.Add(new MenuItem { Header = "_View" });
            menu.Items.Add(new MenuItem { Header = "_Help" });

            var general = new StackPanel { Margin = new Thickness(8) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new Button { Content = "OK", Width = 75, Height = 23, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new Button { Content = "Cancel", Width = 75, Height = 23, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new Button { Content = "Apply", Width = 75, Height = 23, IsEnabled = false });
            general.Children.Add(row);
            general.Children.Add(new CheckBox { Content = "Show window contents while dragging", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) });
            general.Children.Add(new RadioButton { Content = "Large icons", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) });
            general.Children.Add(new RadioButton { Content = "Small icons", Margin = new Thickness(0, 2, 0, 2) });
            general.Children.Add(new TextBox { Text = "C:\\My Documents", Margin = new Thickness(0, 4, 0, 4) });
            var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 4) };
            foreach (string item in new[] { "Windows Standard", "Rose", "Slate", "Storm" })
            {
                combo.Items.Add(item);
            }
            combo.SelectedIndex = 0;
            general.Children.Add(combo);
            general.Children.Add(new ProgressBar { Height = 16, Value = 60, Margin = new Thickness(0, 2, 0, 2) });

            var list = new ListBox { Height = 120, Margin = new Thickness(8) };
            foreach (string item in new[] { "My Computer", "My Documents", "Network Neighborhood", "Recycle Bin", "Internet Explorer", "Online Services", "Briefcase", "Control Panel" })
            {
                list.Items.Add(item);
            }
            list.SelectedIndex = 1;

            var tabs = new TabControl { Margin = new Thickness(6) };
            tabs.Items.Add(new TabItem { Header = "General", Content = general });
            tabs.Items.Add(new TabItem { Header = "Items", Content = list });

            var status = new StatusBar();
            status.Items.Add(_status);

            var layout = new DockPanel();
            DockPanel.SetDock(menu, Dock.Top);
            DockPanel.SetDock(status, Dock.Bottom);
            layout.Children.Add(menu);
            layout.Children.Add(status);
            layout.Children.Add(tabs);
            Content = layout;
            HelpRequested += (s, e) => MessageBox.Show(this, "Help would open here.", Title);
        }

        /// <summary>Keeps the title and status saying what RetroEye shows.</summary>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == StateProperty && State != null)
            {
                Title = $"Sample - {State.WindowsName}";
                _status.Text = $"{State.WindowsName}, {State.LookName}{(State.Running ? "" : " (RetroEye isn't running)")}";
            }
        }
    }
}
