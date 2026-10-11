using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace WindowsSystemGuardian.UI.Controls;

/// <summary>
/// Sparkline de WSG para el monitoreo en vivo: dibuja el histórico de valores con la
/// API de renderizado de WPF (DrawingContext en OnRender, modelo de render de WPF)
/// sin dependencias de charting. Dibuja: área bajo la curva, línea de serie y dos
/// líneas horizontales de umbral (advertencia/crítico) cuando están dentro del rango.
/// Las propiedades son DependencyProperties con AffectsRender: cualquier cambio
/// invalida el render automáticamente.
/// </summary>
public sealed class WsgSparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(WsgSparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(WsgSparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>NaN = auto: el máximo se calcula del mayor valor del histórico (para métricas sin techo, p. ej. throughput).</summary>
    public bool AutoMaximum { get; set; }

    public static readonly DependencyProperty WarningThresholdProperty = DependencyProperty.Register(
        nameof(WarningThreshold), typeof(double), typeof(WsgSparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalThresholdProperty = DependencyProperty.Register(
        nameof(CriticalThreshold), typeof(double), typeof(WsgSparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeriesColorProperty = DependencyProperty.Register(
        nameof(SeriesColor), typeof(Color), typeof(WsgSparkline),
        new FrameworkPropertyMetadata(Color.FromRgb(0x25, 0x63, 0xA8),
            FrameworkPropertyMetadataOptions.AffectsRender));



    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    public double CriticalThreshold
    {
        get => (double)GetValue(CriticalThresholdProperty);
        set => SetValue(CriticalThresholdProperty, value);
    }

    public Color SeriesColor
    {
        get => (Color)GetValue(SeriesColorProperty);
        set => SetValue(SeriesColorProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var values = Values;
        var maximum = AutoMaximum
            ? values is { Count: > 1 } ? Math.Max(1, values.Max()) : 1
            : Maximum > 0 ? Maximum : 100;

        DrawThreshold(drawingContext, width, height, CriticalThreshold, maximum,
            new SolidColorBrush(Color.FromRgb(0xD9, 0x2D, 0x2D)));
        DrawThreshold(drawingContext, width, height, WarningThreshold, maximum,
            new SolidColorBrush(Color.FromRgb(0xE9, 0x9A, 0x00)));

        if (values is not { Count: > 1 })
            return;

        var seriesBrush = new SolidColorBrush(SeriesColor);
        seriesBrush.Freeze();
        var areaBrush = new SolidColorBrush(Color.FromArgb(0x30, SeriesColor.R, SeriesColor.G, SeriesColor.B));
        areaBrush.Freeze();

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(0, Y(values[0], maximum, height)),
                isFilled: true, isClosed: false);

            for (var i = 1; i < values.Count; i++)
            {
                var x = width * i / (values.Count - 1);
                context.LineTo(new Point(x, Y(values[i], maximum, height)), isStroked: true, isSmoothJoin: false);
            }

            // Cierre del área bajo la curva.
            context.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(0, height), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(areaBrush, null, geometry);
        drawingContext.DrawGeometry(null, new Pen(seriesBrush, 1.6), geometry);
    }

    private static void DrawThreshold(
        DrawingContext context,
        double width,
        double height,
        double threshold,
        double maximum,
        Brush brush)
    {
        if (double.IsNaN(threshold) || threshold < 0 || threshold > maximum)
            return;

        var pen = new Pen(brush, 1.0) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var y = Y(threshold, maximum, height);
        context.DrawLine(pen, new Point(0, y), new Point(width, y));
    }

    private static double Y(double value, double maximum, double height)
    {
        var ratio = Math.Clamp(value / maximum, 0, 1);
        return height - ratio * height;
    }
}
