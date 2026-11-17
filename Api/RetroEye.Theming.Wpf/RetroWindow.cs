// RetroEye.Theming.Wpf: a window framed like the Windows version RetroEye shows.
#nullable enable
using RetroEye.Api;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace RetroEye.Theming.Wpf
{
    /// <summary>
    /// A window with the title bar and frame of the Windows version RetroEye shows: RetroEye's own pictures of them
    /// (contract v1's parts, stretched as 9-slices) with the title in the version's caption font and colours, and its
    /// caption buttons in each state, or, with no pictures (RetroEye not running), the same drawn from the published
    /// colours. Windows still moves, sizes, maximizes and snaps it: the frame answers WM_NCHITTEST with the caption, the
    /// edges and the corners. Active and inactive look as the version's did. Give it a <see cref="Client"/> (or a
    /// <see cref="State"/>) and it follows RetroEye's changes live.
    /// </summary>
    public class RetroWindow : Window
    {
        /// <summary>The state the frame is drawn from.</summary>
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ThemeState), typeof(RetroWindow),
            new FrameworkPropertyMetadata(null, (d, e) => ((RetroWindow)d).OnThemeStateChanged()));

        /// <summary>Whether the window has "?" and close only, as a dialog's caption did.</summary>
        public static readonly DependencyProperty IsDialogProperty = DependencyProperty.Register(nameof(IsDialog), typeof(bool), typeof(RetroWindow),
            new FrameworkPropertyMetadata(false, (d, e) => ((RetroWindow)d)._chrome?.Rebuild()));

        private RetroChrome? _chrome;
        private RetroEyeClient? _client;
        private bool _previewed;
        private double _scale = 1;
        private double? _screenScale;

        /// <summary>Makes the window: no Windows frame of its own, sizable, framed by RetroEye's look.</summary>
        public RetroWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            var template = new ControlTemplate(typeof(Window));
            var chrome = new FrameworkElementFactory(typeof(RetroChrome), "PART_Chrome");
            var decorator = new FrameworkElementFactory(typeof(AdornerDecorator));
            decorator.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            chrome.AppendChild(decorator);
            template.VisualTree = chrome;
            Template = template;
            State = ThemeState.Default;
            Activated += (s, e) => _chrome?.SetActive(true);
            Deactivated += (s, e) => _chrome?.SetActive(false);
            StateChanged += (s, e) => _chrome?.Rebuild();
            AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(ScalePopup));
            AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler((s, e) => ScalePopup(s, e)));
        }

        /// <summary>The state the frame is drawn from (Windows 98's defaults until one is given).</summary>
        public ThemeState State
        {
            get => (ThemeState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        /// <summary>Whether the caption has "?" and close only, as dialogs had.</summary>
        public bool IsDialog
        {
            get => (bool)GetValue(IsDialogProperty);
            set => SetValue(IsDialogProperty, value);
        }

        /// <summary>Raised when the "?" button is clicked.</summary>
        public event EventHandler? HelpRequested;

        /// <summary>
        /// A client whose state the window follows, frame and controls (its own resources are styled by
        /// <see cref="RetroEyeTheme"/> too); make it on the window's thread.
        /// </summary>
        public RetroEyeClient? Client
        {
            get => _client;
            set
            {
                _client = value;
                if (value != null)
                {
                    State = value.State;
                    value.StateChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => State = value.State));
                    value.Started += (s, e) => Dispatcher.BeginInvoke(new Action(() => State = value.State));
                }
            }
        }

        /// <summary>
        /// Draws the window off screen, never showing it, at its Width and Height, active or inactive, at the DPI given
        /// (96 for one pixel a unit): for previews and pictures of the window.
        /// </summary>
        public BitmapSource RenderPreview(bool active, double dpi = 96)
        {
            if (!_previewed)
            {
                // The first drawing of a never-shown window leaves some controls' states unpainted (Aero's ticks and
                // radio dots): it is drawn once and thrown away
                _previewed = true;
                RenderPreview(active, dpi);
            }
            ApplyTemplate();
            // Drawn as it would be on a screen of that DPI
            ApplyScale(dpi / 96);
            _chrome?.Rebuild();
            _chrome?.SetActive(active);
            var size = new Size(double.IsNaN(Width) ? 400 : Width, double.IsNaN(Height) ? 300 : Height);
            if (_chrome == null)
            {
                throw new InvalidOperationException("The window has no frame.");
            }
            // The frame and its content, laid out on their own in the version's pixels (the window's scale is put back as
            // it's drawn: a root element's own LayoutTransform isn't applied when it's laid out alone)
            Transform kept = _chrome.LayoutTransform;
            _chrome.LayoutTransform = Transform.Identity;
            var inside = new Size(size.Width / _scale, size.Height / _scale);
            _chrome.Measure(inside);
            _chrome.Arrange(new Rect(inside));
            _chrome.UpdateLayout();
            // Let the controls' own state animations (Aero's ticks and dots fade in) run to their end before drawing: they
            // start when first drawn, so the window is drawn once to start them, then given time
            for (int warm = 0; warm < 2; warm++)
            {
                new RenderTargetBitmap((int)inside.Width, (int)inside.Height, 96, 96, PixelFormats.Pbgra32).Render(_chrome);
                var until = DateTime.UtcNow.AddMilliseconds(500);
                while (DateTime.UtcNow < until)
                {
                    Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    System.Threading.Thread.Sleep(15);
                }
            }
            _chrome.UpdateLayout();
            if (Environment.GetEnvironmentVariable("RETROEYE_PREVIEW_DEBUG") == "1")
            {
                Console.WriteLine(_chrome.Describe());
            }
            var picture = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi / 96), (int)Math.Ceiling(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var canvas = new DrawingVisual();
            using (DrawingContext drawing = canvas.RenderOpen())
            {
                drawing.DrawRectangle(Background, null, new Rect(size));
                // The brush draws the frame in its own units (the version's pixels), so its scale is put back here
                drawing.DrawRectangle(new VisualBrush(_chrome) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(inside) }, null, new Rect(size));
            }
            picture.Render(canvas);
            picture.Freeze();
            _chrome.LayoutTransform = kept;
            return picture;
        }

        /// <summary>Takes the frame part from the template.</summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _chrome = GetTemplateChild("PART_Chrome") as RetroChrome;
            if (_chrome != null)
            {
                _chrome.Window = this;
                _chrome.Rebuild();
            }
        }

        /// <summary>Restyles the window's controls and redraws the frame when the state changes.</summary>
        private void OnThemeStateChanged()
        {
            if (State == null)
            {
                return;
            }
            RetroEyeTheme.Apply(Resources, State);
            ApiFont? message = State.Font("message");
            if (message != null)
            {
                FontFamily = new FontFamily(message.DrawnFace);
                FontSize = message.SizePixels;
                ApplyTextRule(this, message);
            }
            Background = RetroEyeTheme.Brush(State.Color("buttonFace", new ApiColor(192, 192, 192)));
            ApplyScale(_screenScale ?? VisualTreeHelper.GetDpi(this).DpiScaleX);
            _chrome?.Rebuild();
            SetCorners();
        }

        /// <summary>How much bigger than WPF's units the window is drawn now (see <see cref="RetroEyeTheme.LayoutScale"/>).</summary>
        public double LayoutScale => _scale;

        /// <summary>
        /// Draws the window at RetroEye's scale for a screen of the scale given (its DPI / 96), as RetroEye blows its own
        /// windows up: the whole look (frame, caption and content) under one LayoutTransform, and the window's own size
        /// made that much bigger, so it keeps its size in the version's pixels.
        /// </summary>
        private void ApplyScale(double screenScale)
        {
            _screenScale = screenScale;
            double scale = RetroEyeTheme.LayoutScale(State, screenScale);
            if (Math.Abs(scale - _scale) < 0.0001)
            {
                return;
            }
            double ratio = scale / _scale;
            _scale = scale;
            if (State?.Font("message") is ApiFont message)
            {
                ApplyTextRule(this, message);
            }
            if (_chrome != null)
            {
                _chrome.LayoutTransform = Math.Abs(scale - 1) < 0.0001 ? Transform.Identity : new ScaleTransform(scale, scale);
            }
            foreach (DependencyProperty size in new[] { WidthProperty, HeightProperty, MinWidthProperty, MinHeightProperty })
            {
                double value = (double)GetValue(size);
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
                {
                    SetValue(size, value * ratio);
                }
            }
        }

        /// <summary>
        /// Gives text the look RetroEye gives its own: a version's pixel font as pixels (aliased) only where the total scale
        /// (this window's screen scale times its layout scale) is a whole number, as RetroEye's own windows do
        /// (<see cref="PixelScale.IsWhole"/>); otherwise the same face smoothed. Display text formatting only when the window
        /// isn't scaled: under a scale it blurs, so Ideal is used.
        /// </summary>
        internal void ApplyTextRule(DependencyObject target, ApiFont font)
        {
            double total = _scale * (_screenScale ?? VisualTreeHelper.GetDpi(this).DpiScaleX);
            bool unscaled = Math.Abs(_scale - 1) < 0.0001;
            TextOptions.SetTextFormattingMode(target, unscaled ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(target, font.Bitmap && PixelScale.IsWhole(total) ? TextRenderingMode.Aliased : TextRenderingMode.Auto);
        }
        /// <summary>Draws the window again at its new screen's scale when it moves to a screen of another DPI.</summary>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyScale(newDpi.DpiScaleX);
            // The caption's title follows the pixel-text rule for its new total scale
            _chrome?.Rebuild();
        }

        /// <summary>Scales a menu's drop-down as the window is, as RetroEye's own menus are (they live outside the window).</summary>
        private void ScalePopup(object sender, RoutedEventArgs e)
        {
            Transform scale = Math.Abs(_scale - 1) < 0.0001 ? Transform.Identity : new ScaleTransform(_scale, _scale);
            if (e.OriginalSource is MenuItem item && item.Template?.FindName("PART_Popup", item) is System.Windows.Controls.Primitives.Popup popup && popup.Child is FrameworkElement child)
            {
                child.LayoutTransform = scale;
            }
            else if (e.OriginalSource is FrameworkElement element && element.ContextMenu is ContextMenu menu)
            {
                menu.LayoutTransform = scale;
            }
        }

        /// <summary>Asks Windows 11 for square corners on the versions that had them, small round ones otherwise.</summary>
        private void SetCorners()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || State == null)
            {
                return;
            }
            int preference = State.CornerShape == "rounded" ? 3 /* DWMWCP_ROUNDSMALL */ : 1 /* DWMWCP_DONOTROUND */;
            try
            {
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
        }

        /// <summary>Hooks the window's messages, to take Windows' frame off and say where the caption and edges are.</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(Hook);
            }
            SetCorners();
        }

        /// <summary>Carries out a caption button's click.</summary>
        internal void Press(string kind)
        {
            switch (kind)
            {
                case "minimize": WindowState = WindowState.Minimized; break;
                case "maximize": WindowState = WindowState.Maximized; break;
                case "restore": WindowState = WindowState.Normal; break;
                case "close": Close(); break;
                case "help": HelpRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }

        private const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCACTIVATE = 0x0086, WM_GETMINMAXINFO = 0x0024;
        private const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        /// <summary>The window's message hook: no Windows frame, the hit-test of the drawn frame, and maximizing to the work area.</summary>
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (message)
            {
                case WM_NCCALCSIZE when wParam != IntPtr.Zero:
                    if (IsZoomed(hwnd))
                    {
                        // Maximized, Windows puts the frame off the screen: keep the window to the screen's work area
                        var rect = Marshal.PtrToStructure<RECT>(lParam);
                        IntPtr monitor = MonitorFromWindow(hwnd, 2);
                        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(monitor, ref info))
                        {
                            rect = info.rcWork;
                            Marshal.StructureToPtr(rect, lParam, false);
                        }
                    }
                    handled = true;
                    return IntPtr.Zero;
                case WM_NCACTIVATE:
                    // No frame of Windows' to paint; the drawn one follows Activated and Deactivated
                    handled = true;
                    return new IntPtr(1);
                case WM_NCHITTEST:
                    handled = true;
                    return new IntPtr(HitTest(hwnd, lParam));
            }
            return IntPtr.Zero;
        }

        /// <summary>Returns what is under a point: an edge or corner to size by, the caption to move by, or the client area.</summary>
        private int HitTest(IntPtr hwnd, IntPtr lParam)
        {
            int x = unchecked((short)(long)lParam), y = unchecked((short)((long)lParam >> 16));
            if (!GetWindowRect(hwnd, out RECT window) || _chrome == null)
            {
                return HTCLIENT;
            }
            double perDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var at = new Point((x - window.Left) / perDip, (y - window.Top) / perDip);
            double width = (window.Right - window.Left) / perDip, height = (window.Bottom - window.Top) / perDip;
            if (WindowState == WindowState.Normal && ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                double edge = Math.Max(4, (State?.Metric("frameWidth", 4) ?? 4) * _scale);
                bool left = at.X < edge, right = at.X >= width - edge, top = at.Y < edge, bottom = at.Y >= height - edge;
                double corner = edge + 12;
                if (top && at.X < corner || left && at.Y < corner) return left || at.X < corner ? (top || at.Y < corner ? HTTOPLEFT : HTLEFT) : HTTOP;
                if (top && at.X >= width - corner || right && at.Y < corner) return HTTOPRIGHT;
                if (bottom && at.X < corner || left && at.Y >= height - corner) return HTBOTTOMLEFT;
                if (bottom && at.X >= width - corner || right && at.Y >= height - corner) return HTBOTTOMRIGHT;
                if (left) return HTLEFT;
                if (right) return HTRIGHT;
                if (top) return HTTOP;
                if (bottom) return HTBOTTOM;
            }
            // The frame's own units are the version's pixels, under the window's scale
            return _chrome.IsCaption(TranslatePoint(at, _chrome)) ? HTCAPTION : HTCLIENT;
        }

        #region Interop

        /// <summary>A rectangle in screen pixels.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            /// <summary>The left edge.</summary>
            public int Left;
            /// <summary>The top edge.</summary>
            public int Top;
            /// <summary>The right edge.</summary>
            public int Right;
            /// <summary>The bottom edge.</summary>
            public int Bottom;
        }

        /// <summary>A monitor's rectangles.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            /// <summary>The structure's size.</summary>
            public int cbSize;
            /// <summary>The whole monitor.</summary>
            public RECT rcMonitor;
            /// <summary>The part outside the taskbars.</summary>
            public RECT rcWork;
            /// <summary>Flags.</summary>
            public int dwFlags;
        }

        /// <summary>Gets a window's rectangle.</summary>
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        /// <summary>Whether a window is maximized.</summary>
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        /// <summary>Gets the monitor a window is on.</summary>
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        /// <summary>Gets a monitor's rectangles.</summary>
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        /// <summary>Sets one of a window's DWM attributes.</summary>
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        #endregion
    }

    /// <summary>
    /// The frame RetroWindow's template draws round its content: the frame picture (or drawn colours), the caption's
    /// icon, title and buttons, and the content inside the frame's content insets.
    /// </summary>
    public sealed class RetroChrome : Decorator
    {
        private readonly NineSlice _frame = new();
        private readonly Border _drawnFrame = new();
        private readonly Border _drawnCaption = new();
        private readonly Image _icon = new() { Width = 16, Height = 16 };
        private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
        private readonly List<UIElement> _extra = new();
        private bool _active;
        private BitmapSource? _frameActive, _frameInactive;
        private Thickness _content, _slice;
        private Rect _caption;

        /// <summary>Makes the frame's parts.</summary>
        public RetroChrome()
        {
            foreach (UIElement part in new UIElement[] { _drawnFrame, _drawnCaption, _frame, _icon, _title, _buttons })
            {
                _extra.Add(part);
                AddVisualChild(part);
                AddLogicalChild(part);
            }
            RenderOptions.SetBitmapScalingMode(_frame, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.NearestNeighbor);
        }

        /// <summary>The window the frame is round.</summary>
        internal RetroWindow? Window { get; set; }

        /// <summary>Whether a point (in the window's units) is on the caption, away from its buttons.</summary>
        internal bool IsCaption(Point at)
        {
            if (!_caption.Contains(at))
            {
                return false;
            }
            Point inButtons = TranslatePoint(at, _buttons);
            return !(inButtons.X >= 0 && inButtons.Y >= 0 && inButtons.X < _buttons.ActualWidth && inButtons.Y < _buttons.ActualHeight);
        }

        /// <summary>Describes where the frame's parts were laid out, for checking previews.</summary>
        internal string Describe()
        {
            string At(FrameworkElement? e) => e == null ? "-" : $"{e.TranslatePoint(new Point(0, 0), this)} {e.ActualWidth}x{e.ActualHeight}";
            return $"content {_content} caption {_caption} title {At(_title)} buttons {At(_buttons)} child {At(Child as FrameworkElement)} size {ActualWidth}x{ActualHeight} offset {VisualTreeHelper.GetOffset(this)}";
        }

        /// <summary>Shows the frame active or inactive.</summary>
        internal void SetActive(bool active)
        {
            _active = active;
            Paint();
        }

        /// <summary>Builds the frame again for the window's state.</summary>
        internal void Rebuild()
        {
            RetroWindow? window = Window;
            if (window?.State is not ThemeState state)
            {
                return;
            }
            _active = window.IsActive;
            _frameActive = RetroEyeTheme.Load(state.Part("frameActive"));
            _frameInactive = RetroEyeTheme.Load(state.Part("frameInactive"));
            double perDip = PixelsPerDip;
            if (_frameActive != null && state.Part("frameActive") is ApiPart part)
            {
                ApiJson content = part.Raw["content"];
                _content = new Thickness(content["left"].AsNumber() / perDip, content["top"].AsNumber() / perDip, content["right"].AsNumber() / perDip, content["bottom"].AsNumber() / perDip);
                _slice = new Thickness(part.SliceLeft, part.SliceTop, part.SliceRight, part.SliceBottom);
            }
            else
            {
                // No pictures: the frame's thickness and the caption's height from the metrics
                double frame = state.Metric("frameWidth", 4), caption = state.Metric("captionHeight", 19);
                _content = new Thickness(frame, frame + caption, frame, frame);
            }
            _buttons.Children.Clear();
            IEnumerable<string> kinds = window.IsDialog ? new[] { "help", "close" }
                : window.ResizeMode == ResizeMode.NoResize ? new[] { "close" }
                : (IEnumerable<string>)state.CaptionButtonOrder;
            double gap = state.Raw["chrome"]["buttons"]["gap"].AsNumber(), gapBeforeClose = state.Raw["chrome"]["buttons"]["gapBeforeClose"].AsNumber();
            foreach (string kind in kinds)
            {
                string shown = kind == "maximize" && window.WindowState == WindowState.Maximized ? "restore" : kind;
                var button = new CaptionButton(window, state, shown);
                button.Margin = new Thickness(kind == "close" ? gapBeforeClose - gap : 0, 0, gap, 0);
                _buttons.Children.Add(button);
            }
            ApiFont? font = state.Font("caption");
            if (font != null)
            {
                _title.FontFamily = new FontFamily(font.DrawnFace);
                _title.FontSize = font.SizePixels;
                _title.FontWeight = font.Bold ? FontWeights.Bold : FontWeights.Normal;
                window.ApplyTextRule(_title, font);
            }
            _title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(System.Windows.Window.Title)) { Source = window });
            _icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(System.Windows.Window.Icon)) { Source = window });
            Paint();
            InvalidateMeasure();
        }

        /// <summary>Paints the frame, caption and title in the active or inactive look.</summary>
        private void Paint()
        {
            if (Window?.State is not ThemeState state)
            {
                return;
            }
            ApiCaptionState caption = _active ? state.CaptionActive : state.CaptionInactive;
            BitmapSource? picture = _active ? _frameActive : _frameInactive ?? _frameActive;
            _frame.Source = picture;
            _frame.PixelsPerUnit = PixelsPerDip;
            _frame.Slice = _slice;
            _frame.Visibility = picture != null ? Visibility.Visible : Visibility.Collapsed;
            bool drawn = picture == null;
            _drawnFrame.Visibility = _drawnCaption.Visibility = drawn ? Visibility.Visible : Visibility.Collapsed;
            if (drawn)
            {
                string frameColour = state.Raw["chrome"]["frame"][_active ? "active" : "inactive"].AsString("#C0C0C0");
                _drawnFrame.Background = RetroEyeTheme.Brush(ApiColor.Parse(frameColour, new ApiColor(192, 192, 192)));
                _drawnFrame.BorderBrush = RetroEyeTheme.Brush(state.Color("buttonDarkShadow", new ApiColor(0, 0, 0)));
                _drawnFrame.BorderThickness = new Thickness(1);
                _drawnCaption.Background = RetroEyeTheme.Fill(caption.Fill);
            }
            _title.Foreground = RetroEyeTheme.Brush(caption.Text);
            _title.Effect = caption.TextShadow is ApiColor shadow
                ? new DropShadowEffect { ShadowDepth = 1, Direction = 315, BlurRadius = 0, Opacity = 0.7, Color = Color.FromRgb(shadow.R, shadow.G, shadow.B) }
                : caption.TextGlow ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 12, Color = Colors.White, Opacity = 1 } : null;
            foreach (UIElement child in _buttons.Children)
            {
                (child as CaptionButton)?.SetActive(_active);
            }
        }

        /// <summary>The screen pixels to one of WPF's units here.</summary>
        /// <summary>The parts' pixels to one of WPF's units: Windows' scale on the screen RetroEye drew them for (so the frame is as big as RetroEye's own windows there), whatever screen this window is on.</summary>
        private double PixelsPerDip => RetroEyeTheme.PixelsPerUnit(Window?.State);

        /// <inheritdoc />
        protected override int VisualChildrenCount => _extra.Count + (Child != null ? 1 : 0);

        /// <inheritdoc />
        protected override Visual GetVisualChild(int index) => index < _extra.Count ? _extra[index] : Child!;

        /// <inheritdoc />
        protected override Size MeasureOverride(Size available)
        {
            foreach (UIElement part in _extra)
            {
                part.Measure(available);
            }
            Size inside = new(Math.Max(0, available.Width - _content.Left - _content.Right), Math.Max(0, available.Height - _content.Top - _content.Bottom));
            Child?.Measure(inside);
            Size child = Child?.DesiredSize ?? default;
            return new Size(child.Width + _content.Left + _content.Right, child.Height + _content.Top + _content.Bottom);
        }

        /// <inheritdoc />
        protected override Size ArrangeOverride(Size size)
        {
            ThemeState? state = Window?.State;
            var whole = new Rect(size);
            _frame.Arrange(whole);
            _drawnFrame.Arrange(whole);
            // The caption: the band above the content, inside the side frames
            double frameWidth = state?.Metric("frameWidth", 4) ?? 4;
            double captionHeight = state?.Raw["chrome"]["caption"]["height"].AsNumber(18) ?? 18;
            bool raised = state?.FrameKind == "raised3d";
            double bottom = _content.Top - (raised ? 1 : 0);
            double top = Math.Max(raised ? frameWidth : 0, bottom - captionHeight);
            double left = raised ? _content.Left : Math.Max(_content.Left, frameWidth);
            _caption = new Rect(left, top, Math.Max(0, size.Width - left - _content.Right), Math.Max(0, bottom - top));
            _drawnCaption.Arrange(_caption);
            double inset = state?.Metric("captionTextInset", 2) ?? 2;
            Size buttons = _buttons.DesiredSize;
            bool hanging = state?.CaptionButtonStyle == "aero";
            double buttonsTop = hanging ? 1 : _caption.Top + (_caption.Height - buttons.Height) / 2;
            double buttonsRight = size.Width - _content.Right - (hanging ? 0 : 2);
            _buttons.Arrange(new Rect(buttonsRight - buttons.Width, buttonsTop, buttons.Width, buttons.Height));
            double x = _caption.Left + inset;
            if (_icon.Source != null)
            {
                _icon.Arrange(new Rect(x, _caption.Top + (_caption.Height - 16) / 2, 16, 16));
                x += 16 + 4;
            }
            else
            {
                _icon.Arrange(new Rect(0, 0, 0, 0));
            }
            _title.Arrange(new Rect(x, _caption.Top, Math.Max(0, buttonsRight - buttons.Width - x - 4), _caption.Height));
            Child?.Arrange(new Rect(_content.Left, _content.Top, Math.Max(0, size.Width - _content.Left - _content.Right), Math.Max(0, size.Height - _content.Top - _content.Bottom)));
            return size;
        }
    }

    /// <summary>
    /// A caption button (minimize, maximize, restore, close or "?"): RetroEye's picture of it in its state (normal, hot,
    /// pressed, disabled, or as on an inactive caption), or a drawn stand-in without pictures.
    /// </summary>
    internal sealed class CaptionButton : FrameworkElement
    {
        private readonly RetroWindow _window;
        private readonly ThemeState _state;
        private readonly string _kind;
        private readonly Dictionary<string, BitmapSource?> _pictures = new();
        private bool _active = true, _pressed;

        /// <summary>Makes a caption button of a kind for a window.</summary>
        public CaptionButton(RetroWindow window, ThemeState state, string kind)
        {
            _window = window;
            _state = state;
            _kind = kind;
            foreach (string look in new[] { "normal", "hot", "pressed", "disabled", "inactive" })
            {
                _pictures[look] = RetroEyeTheme.Load(state.Part($"button.{kind}.{look}"));
            }
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            ToolTip = kind switch { "minimize" => "Minimize", "maximize" => "Maximize", "restore" => "Restore Down", "close" => "Close", _ => "Help" };
        }

        /// <summary>Shows the button as on an active caption or an inactive one.</summary>
        public void SetActive(bool active)
        {
            _active = active;
            InvalidateVisual();
        }

        /// <summary>The look to draw now.</summary>
        private string Look => !IsEnabled ? "disabled" : _pressed && IsMouseOver ? "pressed" : IsMouseOver ? "hot" : _active ? "normal" : "inactive";

        /// <inheritdoc />
        protected override Size MeasureOverride(Size available)
        {
            if (_pictures["normal"] is BitmapSource picture)
            {
                double perDip = RetroEyeTheme.PixelsPerUnit(_state);
                return new Size(picture.PixelWidth / perDip, picture.PixelHeight / perDip);
            }
            ApiJson buttons = _state.Raw["chrome"]["buttons"];
            return new Size(_kind == "close" ? buttons["closeWidth"].AsNumber(16) : buttons["width"].AsNumber(16), buttons["height"].AsNumber(14));
        }

        /// <inheritdoc />
        protected override void OnRender(DrawingContext drawing)
        {
            var place = new Rect(RenderSize);
            BitmapSource? picture = _pictures[Look] ?? _pictures["normal"];
            if (picture != null)
            {
                drawing.DrawImage(picture, place);
                return;
            }
            // Without RetroEye's pictures: a raised face with the glyph
            Brush face = RetroEyeTheme.Brush(_state.Color("buttonFace", new ApiColor(192, 192, 192)));
            Pen light = new(RetroEyeTheme.Brush(_state.Color("buttonHighlight", new ApiColor(255, 255, 255))), 1);
            Pen dark = new(RetroEyeTheme.Brush(_state.Color("buttonDarkShadow", new ApiColor(0, 0, 0))), 1);
            drawing.DrawRectangle(face, null, place);
            bool down = Look == "pressed";
            drawing.DrawLine(down ? dark : light, new Point(0.5, 0.5), new Point(place.Width - 0.5, 0.5));
            drawing.DrawLine(down ? dark : light, new Point(0.5, 0.5), new Point(0.5, place.Height - 0.5));
            drawing.DrawLine(down ? light : dark, new Point(place.Width - 0.5, 0.5), new Point(place.Width - 0.5, place.Height - 0.5));
            drawing.DrawLine(down ? light : dark, new Point(0.5, place.Height - 0.5), new Point(place.Width - 0.5, place.Height - 0.5));
            // The glyph, drawn here: a bar, a box, two boxes, a cross or a question mark
            Brush ink = RetroEyeTheme.Brush(_state.Color("buttonText", new ApiColor(0, 0, 0)));
            var pen = new Pen(ink, 1.5);
            double cx = place.Width / 2 + (down ? 1 : 0), cy = place.Height / 2 + (down ? 1 : 0), s = Math.Max(3, Math.Min(place.Width, place.Height) / 2 - 3);
            switch (_kind)
            {
                case "minimize":
                    drawing.DrawLine(new Pen(ink, 2), new Point(cx - s, cy + s - 1), new Point(cx + s - 2, cy + s - 1));
                    break;
                case "maximize":
                    drawing.DrawRectangle(null, pen, new Rect(cx - s, cy - s, s * 2, s * 2));
                    drawing.DrawLine(new Pen(ink, 2), new Point(cx - s, cy - s + 1), new Point(cx + s, cy - s + 1));
                    break;
                case "restore":
                    drawing.DrawRectangle(null, pen, new Rect(cx - s + 2, cy - s, s * 2 - 2, s * 2 - 2));
                    drawing.DrawRectangle(face, pen, new Rect(cx - s, cy - s + 2, s * 2 - 2, s * 2 - 2));
                    break;
                case "close":
                    drawing.DrawLine(pen, new Point(cx - s, cy - s), new Point(cx + s, cy + s));
                    drawing.DrawLine(pen, new Point(cx + s, cy - s), new Point(cx - s, cy + s));
                    break;
                default:
                    var text = new FormattedText("?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Tahoma"), Math.Max(6, place.Height - 4), ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    drawing.DrawText(text, new Point(cx - text.Width / 2, cy - text.Height / 2));
                    break;
            }
        }

        /// <inheritdoc />
        protected override void OnMouseEnter(MouseEventArgs e) => InvalidateVisual();

        /// <inheritdoc />
        protected override void OnMouseLeave(MouseEventArgs e) => InvalidateVisual();

        /// <inheritdoc />
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _pressed = true;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }

        /// <inheritdoc />
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            bool click = _pressed && IsMouseOver;
            _pressed = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            if (click)
            {
                _window.Press(_kind);
            }
        }
    }

    /// <summary>A picture stretched as a 9-slice: its corners kept, its edges stretched one way, its middle both ways.</summary>
    public sealed class NineSlice : FrameworkElement
    {
        private BitmapSource? _source;
        private Thickness _slice;

        /// <summary>The picture; setting it redraws.</summary>
        public BitmapSource? Source
        {
            get => _source;
            set
            {
                _source = value;
                InvalidateVisual();
            }
        }

        /// <summary>How many of the picture's pixels make one of WPF's units (1 at 96 DPI).</summary>
        public double PixelsPerUnit { get; set; } = 1;

        /// <summary>The insets kept as they are, in the picture's pixels; setting them redraws.</summary>
        public Thickness Slice
        {
            get => _slice;
            set
            {
                _slice = value;
                InvalidateVisual();
            }
        }

        /// <inheritdoc />
        protected override void OnRender(DrawingContext drawing)
        {
            if (Source is not BitmapSource picture)
            {
                return;
            }
            double perDip = PixelsPerUnit;
            int pw = picture.PixelWidth, ph = picture.PixelHeight;
            int l = (int)Slice.Left, t = (int)Slice.Top, r = (int)Slice.Right, b = (int)Slice.Bottom;
            double[] xs = { 0, l / perDip, RenderSize.Width - r / perDip, RenderSize.Width };
            double[] ys = { 0, t / perDip, RenderSize.Height - b / perDip, RenderSize.Height };
            int[] px = { 0, l, pw - r, pw };
            int[] py = { 0, t, ph - b, ph };
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    int sw = px[column + 1] - px[column], sh = py[row + 1] - py[row];
                    double dw = xs[column + 1] - xs[column], dh = ys[row + 1] - ys[row];
                    if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0)
                    {
                        continue;
                    }
                    var piece = new CroppedBitmap(picture, new Int32Rect(px[column], py[row], sw, sh));
                    drawing.DrawImage(piece, new Rect(xs[column], ys[row], dw, dh));
                }
            }
        }
    }
}
