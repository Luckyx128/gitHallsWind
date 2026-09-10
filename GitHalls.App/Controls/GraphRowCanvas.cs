using GitHalls.Core.Graph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;

namespace GitHalls.App.Controls;

public sealed class GraphRowCanvas : Canvas
{
    private const double TrackWidth = 16.0;
    private const double RowHeight = 32.0;
    private const double DotRadius = 4.0;
    private const double LineThickness = 2.0;

    public static readonly DependencyProperty RowDataProperty =
        DependencyProperty.Register("RowData", typeof(GraphRow), typeof(GraphRowCanvas), new PropertyMetadata(null, OnRowDataChanged));

    public GraphRow? RowData
    {
        get => (GraphRow?)GetValue(RowDataProperty);
        set => SetValue(RowDataProperty, value);
    }

    private double _lastDrawnHeight;

    public GraphRowCanvas()
    {
        SizeChanged += (s, e) => 
        {
            if (Math.Abs(_lastDrawnHeight - ActualHeight) > 0.5)
            {
                _lastDrawnHeight = ActualHeight;
                Redraw();
            }
        };
    }

    private static void OnRowDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GraphRowCanvas canvas)
        {
            if (canvas.RowData != null)
            {
                canvas.Width = canvas.RowData.TrackCount * TrackWidth;
            }
            canvas.Redraw();
        }
    }

    private SolidColorBrush GetBrush(int colorIndex)
    {
        string[] hexColors = { "#005FB8", "#6B2FBF", "#0A7C74", "#0A6B0A", "#BC2819", "#8E1F14", "#5D6169" };
        var hex = hexColors[colorIndex % hexColors.Length];
        return new SolidColorBrush(ColorHelper.FromArgb(255, 
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16)));
    }

    private void Redraw()
    {
        Children.Clear();
        var row = RowData;
        if (row == null) return;

        double currentHeight = ActualHeight > 0 ? ActualHeight : RowHeight;

        // Draw top lines
        foreach (var track in row.TopTracks)
        {
            var line = new Line
            {
                X1 = track.Index * TrackWidth + (TrackWidth / 2),
                Y1 = 0,
                X2 = track.Index * TrackWidth + (TrackWidth / 2),
                Y2 = track.Index == row.TrackIndex ? currentHeight / 2 : currentHeight,
                Stroke = GetBrush(track.ColorIndex),
                StrokeThickness = LineThickness
            };
            Children.Add(line);
        }

        // Draw bottom edges
        foreach (var edge in row.BottomEdges)
        {
            var startX = edge.StartTrack * TrackWidth + (TrackWidth / 2);
            var endX = edge.EndTrack * TrackWidth + (TrackWidth / 2);
            var startY = currentHeight / 2;
            var endY = currentHeight;

            if (edge.StartTrack == edge.EndTrack)
            {
                if (edge.StartTrack == row.TrackIndex)
                {
                    var line = new Line
                    {
                        X1 = startX, Y1 = startY, X2 = endX, Y2 = endY,
                        Stroke = GetBrush(edge.ColorIndex), StrokeThickness = LineThickness
                    };
                    Children.Add(line);
                }
            }
            else
            {
                // Bezier curve to connect different tracks
                var path = new Microsoft.UI.Xaml.Shapes.Path
                {
                    Stroke = GetBrush(edge.ColorIndex),
                    StrokeThickness = LineThickness
                };

                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(startX, startY), IsClosed = false };
                var bezier = new BezierSegment
                {
                    Point1 = new Windows.Foundation.Point(startX, startY + (currentHeight / 4)),
                    Point2 = new Windows.Foundation.Point(endX, endY - (currentHeight / 4)),
                    Point3 = new Windows.Foundation.Point(endX, endY)
                };
                figure.Segments.Add(bezier);
                geometry.Figures.Add(figure);
                path.Data = geometry;

                Children.Add(path);
            }
        }

        // Draw commit dot
        var dot = new Ellipse
        {
            Width = DotRadius * 2,
            Height = DotRadius * 2,
            Fill = GetBrush(row.ColorIndex),
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.White),
            StrokeThickness = 1
        };
        Canvas.SetLeft(dot, row.TrackIndex * TrackWidth + (TrackWidth / 2) - DotRadius);
        Canvas.SetTop(dot, (currentHeight / 2) - DotRadius);
        Children.Add(dot);
    }
}
