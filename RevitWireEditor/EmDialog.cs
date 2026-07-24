#region Namespaces
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
#endregion

namespace RevitWireEditor
{
    /// <summary>Variante visual (cor da barra superior), espelhando o Em Design System.</summary>
    public enum EmVariant { Info, Success, Warning, Error }

    /// <summary>Escolha do usuario ao fechar o dialog.</summary>
    public enum EmResult { Primary, Secondary, Closed }

    /// <summary>
    /// Dialog modal estilizado (card + barra de variante + botao amarelo), construido em codigo
    /// para nao depender de XAML/pack-URIs. Replica o visual do Expert Tools (Em Design System)
    /// dentro deste plugin standalone. Centralizacao DPI-correta no monitor do Revit (L-48).
    /// </summary>
    public sealed class EmDialog : Window
    {
        // ----- Paleta Em -----
        private static readonly Brush Neutral50 = Frozen("#FAFAFA");
        private static readonly Brush Neutral200 = Frozen("#E5E5E5");
        private static readonly Brush Neutral300 = Frozen("#D4D4D4");
        private static readonly Brush Neutral500 = Frozen("#737373");
        private static readonly Brush Neutral700 = Frozen("#404040");
        private static readonly Brush Neutral900 = Frozen("#171717");
        private static readonly Brush BrandPrimary = Frozen("#FFCC00");
        private static readonly Brush BrandPrimaryHover = Frozen("#FFD633");
        private static readonly Brush White = Frozen("#FFFFFF");

        private EmResult _result = EmResult.Closed;
        private IntPtr _ownerHwnd;

        /// <summary>Exibe o dialog modal e retorna a escolha do usuario.</summary>
        public static EmResult Show(
            string title,
            string message,
            string primaryText = "OK",
            string secondaryText = null,
            EmVariant variant = EmVariant.Info,
            IntPtr ownerHandle = default)
        {
            var dlg = new EmDialog(title, message, primaryText, secondaryText, variant, ownerHandle);
            dlg.ShowDialog();
            return dlg._result;
        }

        private EmDialog(string title, string message, string primaryText, string secondaryText,
                         EmVariant variant, IntPtr ownerHandle)
        {
            _ownerHwnd = ownerHandle;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 500;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;

            if (ownerHandle != IntPtr.Zero)
                new WindowInteropHelper(this) { Owner = ownerHandle };

            Content = BuildCard(title, message, primaryText, secondaryText, variant);

            Loaded += OnLoaded;
            MouseLeftButtonDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch { } } };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { _result = EmResult.Closed; Close(); } };
        }

        private FrameworkElement BuildCard(string title, string message, string primaryText,
                                           string secondaryText, EmVariant variant)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = Neutral50,
                BorderBrush = Neutral200,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(24, 16, 24, 32),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 32,
                    Direction = 270,
                    ShadowDepth = 8,
                    Opacity = 0.22,
                    Color = Colors.Black
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new Border { Background = VariantBrush(variant), CornerRadius = new CornerRadius(12, 12, 0, 0) };
            Grid.SetRow(bar, 0);
            grid.Children.Add(bar);

            var body = new StackPanel { Margin = new Thickness(24) };
            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            // Header: icone + titulo + fechar
            var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Image
            {
                Source = LoadIcon(),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            Grid.SetColumn(icon, 0);
            header.Children.Add(icon);

            var titleBlock = new TextBlock
            {
                Text = title ?? string.Empty,
                FontFamily = new FontFamily("Segoe UI Semibold, Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Foreground = Neutral900,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(titleBlock, 1);
            header.Children.Add(titleBlock);

            var close = MakeCloseButton();
            Grid.SetColumn(close, 2);
            header.Children.Add(close);

            body.Children.Add(header);

            // Corpo da mensagem
            var msg = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Foreground = Neutral700,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20
            };
            AppendMessage(msg, message ?? string.Empty);
            body.Children.Add(msg);

            // Botoes
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 24, 0, 0)
            };
            if (!string.IsNullOrEmpty(secondaryText))
            {
                var sec = MakeButton(secondaryText, White, Neutral700, Neutral300, Neutral50);
                sec.Margin = new Thickness(0, 0, 8, 0);
                sec.Click += (s, e) => { _result = EmResult.Secondary; Close(); };
                buttons.Children.Add(sec);
            }
            var prim = MakeButton(primaryText ?? "OK", BrandPrimary, Neutral900, null, BrandPrimaryHover);
            prim.Click += (s, e) => { _result = EmResult.Primary; Close(); };
            buttons.Children.Add(prim);
            body.Children.Add(buttons);

            card.Child = grid;
            return card;
        }

        /// <summary>Quebra a mensagem em linhas, dando enfase (negrito) a linhas "Titulo:".</summary>
        private static void AppendMessage(TextBlock tb, string message)
        {
            var linhas = message.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < linhas.Length; i++)
            {
                if (i > 0) tb.Inlines.Add(new LineBreak());
                tb.Inlines.Add(new Run(linhas[i]));
            }
        }

        private Button MakeCloseButton()
        {
            var b = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 13,
                Foreground = Neutral500,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            b.Template = ButtonTemplate(new CornerRadius(6), Brushes.Transparent, null, Neutral200);
            b.Click += (s, e) => { _result = EmResult.Closed; Close(); };
            return b;
        }

        private static Button MakeButton(string text, Brush bg, Brush fg, Brush border, Brush hoverBg)
        {
            var b = new Button
            {
                Content = text,
                Foreground = fg,
                Background = bg,
                Cursor = Cursors.Hand,
                MinWidth = 112,
                Height = 38,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Semibold, Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(16, 0, 16, 0)
            };
            b.Template = ButtonTemplate(new CornerRadius(8), bg, border, hoverBg);
            return b;
        }

        /// <summary>ControlTemplate de botao (Border+ContentPresenter) com hover.</summary>
        private static ControlTemplate ButtonTemplate(CornerRadius radius, Brush bg, Brush border, Brush hoverBg)
        {
            var border0 = new FrameworkElementFactory(typeof(Border), "bd");
            border0.SetValue(Border.CornerRadiusProperty, radius);
            border0.SetValue(Border.BackgroundProperty, bg ?? Brushes.Transparent);
            if (border != null)
            {
                border0.SetValue(Border.BorderBrushProperty, border);
                border0.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            }
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border0.AppendChild(cp);

            var tmpl = new ControlTemplate(typeof(Button)) { VisualTree = border0 };
            if (hoverBg != null)
            {
                var trg = new Trigger { Property = IsMouseOverProperty, Value = true };
                trg.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "bd"));
                tmpl.Triggers.Add(trg);
            }
            return tmpl;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CentralizarNoMonitorDoRevit();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))));
        }

        /// <summary>
        /// Centraliza no work area do monitor do Revit, em DIPs (L-48: CenterOwner com host
        /// maximizado centraliza no retangulo RESTAURADO — bug WPF; por isso manual).
        /// </summary>
        private void CentralizarNoMonitorDoRevit()
        {
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget == null) return;
                var toDevice = src.CompositionTarget.TransformToDevice;
                double dpiX = toDevice.M11, dpiY = toDevice.M22;

                RECT work;
                if (_ownerHwnd != IntPtr.Zero && GetWindowRect(_ownerHwnd, out RECT owner))
                {
                    var mon = MonitorFromWindow(_ownerHwnd, MONITOR_DEFAULTTONEAREST);
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    work = GetMonitorInfo(mon, ref mi) ? mi.rcWork : owner;
                }
                else
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    var mon = MonitorFromWindow(new WindowInteropHelper(this).Handle, MONITOR_DEFAULTTONEAREST);
                    if (!GetMonitorInfo(mon, ref mi)) return;
                    work = mi.rcWork;
                }

                double winWpx = ActualWidth * dpiX, winHpx = ActualHeight * dpiY;
                double leftPx = work.left + ((work.right - work.left) - winWpx) / 2.0;
                double topPx = work.top + ((work.bottom - work.top) - winHpx) / 2.0;
                Left = leftPx / dpiX;
                Top = topPx / dpiY;
            }
            catch { /* centralizacao e cosmetica; nunca derruba o dialog */ }
        }

        private static BitmapImage LoadIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("RevitWireEditor.Resources.icon-fiacao.png"))
                {
                    if (s == null) return null;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = s;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
            }
            catch { return null; }
        }

        private static Brush VariantBrush(EmVariant v)
        {
            switch (v)
            {
                case EmVariant.Success: return Frozen("#10B981");
                case EmVariant.Warning: return Frozen("#F59E0B");
                case EmVariant.Error: return Frozen("#EF4444");
                default: return Frozen("#3B82F6");
            }
        }

        private static Brush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        // ----- Win32 (centralizacao) -----
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
    }
}
