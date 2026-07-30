using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MicFXApp.Controls
{
    /// <summary>
    /// A VST-style rotary knob. Drag vertically to change value (hold Shift for fine
    /// adjustment), scroll the mouse wheel to nudge, double-click to reset to DefaultValue.
    /// Sweeps -135deg to +135deg (270deg total, gap at the bottom), matching the
    /// convention used by most hardware-style plugin UIs.
    /// </summary>
    public partial class KnobControl : UserControl
    {
        private const double StartAngle = -135;
        private const double EndAngle = 135;
        private const double SweepDegrees = EndAngle - StartAngle;
        private const double Radius = 25; // sits just outside the 40px (radius 20) knob body, inside the 60px cell
        private static readonly Point Center = new Point(30, 30); // true center of the 60x60 dial grid

        private bool _dragging;
        private Point _dragStart;
        private double _dragStartValue;

        public KnobControl()
        {
            InitializeComponent();
            Loaded += (s, e) => { BuildTrackArc(); UpdateVisual(); };
            SizeChanged += (s, e) => { BuildTrackArc(); UpdateVisual(); };
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(KnobControl),
                new PropertyMetadata(0.0, (d, e) => ((KnobControl)d).UpdateVisual()));
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(KnobControl),
                new PropertyMetadata(1.0, (d, e) => ((KnobControl)d).UpdateVisual()));
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

        public static readonly DependencyProperty DefaultValueProperty =
            DependencyProperty.Register(nameof(DefaultValue), typeof(double), typeof(KnobControl), new PropertyMetadata(0.0));
        public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) => ((KnobControl)d).UpdateVisual()));
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

        public static readonly DependencyProperty AccentBrushProperty =
            DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(KnobControl),
                new PropertyMetadata(Brushes.DeepSkyBlue, (d, e) => ((KnobControl)d).UpdateVisual()));
        public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(KnobControl), new PropertyMetadata(""));
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(KnobControl),
                new PropertyMetadata("", (d, e) => ((KnobControl)d).UpdateVisual()));
        public string Suffix { get => (string)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(KnobControl),
                new PropertyMetadata(1, (d, e) => ((KnobControl)d).UpdateVisual()));
        public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

        private double Fraction
        {
            get
            {
                double range = Maximum - Minimum;
                if (range <= 0) return 0;
                return Math.Clamp((Value - Minimum) / range, 0.0, 1.0);
            }
        }

        private static Point PointOnCircle(double angleDeg, double radius, Point center)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Sin(rad), center.Y - radius * Math.Cos(rad));
        }

        private void BuildTrackArc()
        {
            TrackArc.Data = BuildArcGeometry(StartAngle, EndAngle);
        }

        private void UpdateVisual()
        {
            if (ValueArc == null) return; // not yet loaded

            double angle = StartAngle + Fraction * SweepDegrees;
            ValueArc.Data = Fraction > 0.002 ? BuildArcGeometry(StartAngle, angle) : null;
            ValueArc.Stroke = AccentBrush;
            NeedleRotate.Angle = angle;

            ValueText.Text = Value.ToString("F" + DecimalPlaces) + Suffix;
        }

        private static Geometry BuildArcGeometry(double fromDeg, double toDeg)
        {
            var start = PointOnCircle(fromDeg, Radius, Center);
            var end = PointOnCircle(toDeg, Radius, Center);
            bool isLargeArc = Math.Abs(toDeg - fromDeg) > 180;

            var figure = new System.Windows.Media.PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new System.Windows.Media.ArcSegment(
                end, new Size(Radius, Radius), 0, isLargeArc,
                System.Windows.Media.SweepDirection.Clockwise, true));

            var geometry = new System.Windows.Media.PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        // ---------------- interaction ----------------

        private void DialGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Value = Math.Clamp(DefaultValue, Minimum, Maximum);
                return;
            }

            _dragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartValue = Value;
            DialGrid.CaptureMouse();
        }

        private void DialGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            DialGrid.ReleaseMouseCapture();
        }

        private void DialGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(this);
            double deltaY = _dragStart.Y - pos.Y; // dragging up increases value

            double sensitivity = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 600.0 : 150.0;
            double range = Maximum - Minimum;
            double delta = (deltaY / sensitivity) * range;

            Value = Math.Clamp(_dragStartValue + delta, Minimum, Maximum);
        }

        private void DialGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double range = Maximum - Minimum;
            double step = range / 100.0 * (e.Delta > 0 ? 1 : -1);
            Value = Math.Clamp(Value + step, Minimum, Maximum);
            e.Handled = true;
        }
    }
}
