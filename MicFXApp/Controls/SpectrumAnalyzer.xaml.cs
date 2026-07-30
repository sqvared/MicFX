using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MicFXApp.Controls
{
    /// <summary>
    /// Real-time frequency-spectrum display, similar in spirit to FL Studio's
    /// Parametric EQ 2 analyzer overlay: a filled, log-frequency curve instead
    /// of a flat level bar, so you can actually see what's in the signal (and
    /// what your EQ/noise-suppression settings are doing to it) rather than
    /// just how loud it is.
    /// </summary>
    public partial class SpectrumAnalyzer : UserControl
    {
        public SpectrumAnalyzer()
        {
            InitializeComponent();
            SizeChanged += (s, e) => { DrawGrid(); Redraw(); };
            Loaded += (s, e) => { ApplyBrushes(); DrawGrid(); Redraw(); };
        }

        public static readonly DependencyProperty AccentBrushProperty =
            DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(SpectrumAnalyzer),
                new PropertyMetadata(Brushes.DeepSkyBlue, (d, e) => ((SpectrumAnalyzer)d).ApplyBrushes()));
        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        private float[] _lastMagnitudes = Array.Empty<float>();

        /// <summary>Feed a new set of 0-1 normalized magnitudes (log-frequency spaced) to display.</summary>
        public void UpdateSpectrum(float[] magnitudes)
        {
            _lastMagnitudes = magnitudes;
            Redraw();
        }

        private void ApplyBrushes()
        {
            StrokePath.Stroke = AccentBrush;
            if (AccentBrush is SolidColorBrush scb)
            {
                var c = scb.Color;
                var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(120, c.R, c.G, c.B), 0));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(8, c.R, c.G, c.B), 1));
                FillPath.Fill = fill;
            }
        }

        private void DrawGrid()
        {
            GridCanvas.Children.Clear();
            double w = RootGrid.ActualWidth, h = RootGrid.ActualHeight;
            if (w <= 0 || h <= 0) return;

            for (int i = 1; i < 4; i++)
            {
                double y = h * i / 4.0;
                GridCanvas.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = w, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x2E)),
                    StrokeThickness = 1,
                });
            }
        }

        private void Redraw()
        {
            double w = RootGrid.ActualWidth, h = RootGrid.ActualHeight;
            int n = _lastMagnitudes.Length;
            if (w <= 0 || h <= 0 || n < 2) return;

            var points = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double x = w * i / (n - 1);
                double mag = Math.Clamp(_lastMagnitudes[i], 0f, 1f);
                double y = h - mag * h;
                points[i] = new Point(x, y);
            }

            var strokeFigure = new PathFigure { StartPoint = points[0], IsClosed = false };
            for (int i = 1; i < n; i++) strokeFigure.Segments.Add(new LineSegment(points[i], true));
            var strokeGeom = new PathGeometry();
            strokeGeom.Figures.Add(strokeFigure);
            StrokePath.Data = strokeGeom;

            var fillFigure = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
            fillFigure.Segments.Add(new LineSegment(points[0], true));
            for (int i = 1; i < n; i++) fillFigure.Segments.Add(new LineSegment(points[i], true));
            fillFigure.Segments.Add(new LineSegment(new Point(w, h), true));
            var fillGeom = new PathGeometry();
            fillGeom.Figures.Add(fillFigure);
            FillPath.Data = fillGeom;
        }
    }
}
