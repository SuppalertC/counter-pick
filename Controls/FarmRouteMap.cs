using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotaComboBoard.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace DotaComboBoard.Controls;

public sealed class FarmRouteMap : FrameworkElement
{
    private const double WorldMin = -9000;
    private const double WorldRange = 18000;
    private ImageSource? _mapImage;
    private string _loadedMapPath = string.Empty;

    public static readonly DependencyProperty RolePlanProperty = DependencyProperty.Register(
        nameof(RolePlan),
        typeof(RoleExecutionPlan),
        typeof(FarmRouteMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public RoleExecutionPlan? RolePlan
    {
        get => (RoleExecutionPlan?)GetValue(RolePlanProperty);
        set => SetValue(RolePlanProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new FrameworkElementAutomationPeer(this);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var side = Math.Min(ActualWidth, ActualHeight);
        var mapRect = new Rect((ActualWidth - side) / 2, 0, side, side);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(10, 14, 18)), null, mapRect, 12, 12);

        var image = LoadMapImage();
        if (image is not null)
        {
            drawingContext.PushClip(new RectangleGeometry(mapRect, 12, 12));
            drawingContext.DrawImage(image, mapRect);
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(72, 5, 8, 12)), null, mapRect);
            drawingContext.Pop();
        }

        DrawSafeZones(drawingContext, mapRect);
        DrawAllCamps(drawingContext, mapRect);
        DrawRoute(drawingContext, mapRect, GetRoute(isRadiant: true), Color.FromRgb(78, 205, 196), false);
        DrawRoute(drawingContext, mapRect, GetRoute(isRadiant: false), Color.FromRgb(244, 166, 76), true);
        DrawMapLabels(drawingContext, mapRect);
        drawingContext.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(58, 69, 82)), 1), mapRect, 12, 12);
    }

    private ImageSource? LoadMapImage()
    {
        var path = RolePlan?.MapImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (_mapImage is not null && path.Equals(_loadedMapPath, StringComparison.OrdinalIgnoreCase))
        {
            return _mapImage;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        _loadedMapPath = path;
        _mapImage = bitmap;
        return _mapImage;
    }

    private static void DrawSafeZones(DrawingContext context, Rect rect)
    {
        var radiantZone = new SolidColorBrush(Color.FromArgb(44, 78, 205, 196));
        var direZone = new SolidColorBrush(Color.FromArgb(42, 244, 166, 76));
        context.DrawEllipse(radiantZone, null, ToPoint(rect, -1800, -4300), rect.Width * .26, rect.Height * .22);
        context.DrawEllipse(direZone, null, ToPoint(rect, -1100, 4600), rect.Width * .26, rect.Height * .22);
    }

    private static void DrawAllCamps(DrawingContext context, Rect rect)
    {
        foreach (var camp in Camps)
        {
            var center = ToPoint(rect, camp.X, camp.Y);
            var fill = camp.Kind switch
            {
                CampKind.Ancient => Color.FromRgb(194, 123, 255),
                CampKind.Hard => Color.FromRgb(236, 95, 86),
                _ => Color.FromRgb(228, 233, 239)
            };
            var radius = camp.Kind == CampKind.Ancient ? 5.8 : 4.6;
            context.DrawEllipse(new SolidColorBrush(fill), new Pen(Brushes.Black, 1.2), center, radius, radius);
        }
    }

    private static void DrawRoute(
        DrawingContext context,
        Rect rect,
        IReadOnlyList<MapNode> route,
        Color routeColor,
        bool dashed)
    {
        if (route.Count == 0)
        {
            return;
        }

        var brush = new SolidColorBrush(routeColor);
        var pen = new Pen(brush, 3.2)
        {
            DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid,
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        for (var index = 1; index < route.Count; index++)
        {
            context.DrawLine(pen, ToPoint(rect, route[index - 1].X, route[index - 1].Y), ToPoint(rect, route[index].X, route[index].Y));
        }

        for (var index = 0; index < route.Count; index++)
        {
            var center = ToPoint(rect, route[index].X, route[index].Y);
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(14, 18, 23)), new Pen(brush, 2.6), center, 11, 11);
            DrawText(context, (index + 1).ToString(CultureInfo.InvariantCulture), center.X - 3.5, center.Y - 8, 12, Brushes.White, FontWeights.Bold);
        }
    }

    private static void DrawMapLabels(DrawingContext context, Rect rect)
    {
        DrawLabel(context, rect.Left + 14, rect.Bottom - 38, "RADIANT", Color.FromRgb(78, 205, 196));
        DrawLabel(context, rect.Right - 72, rect.Top + 14, "DIRE", Color.FromRgb(244, 166, 76));
        DrawLabel(context, rect.Left + 14, rect.Top + 14, "WHITE CAMP  •  RED HARD  •  PURPLE ANCIENT", Color.FromRgb(226, 231, 237), 10);
    }

    private static void DrawLabel(DrawingContext context, double x, double y, string text, Color color, double size = 11)
    {
        var formatted = CreateText(text, size, new SolidColorBrush(color), FontWeights.Bold);
        var background = new Rect(x - 6, y - 3, formatted.Width + 12, formatted.Height + 6);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 12, 16, 21)), null, background, 4, 4);
        context.DrawText(formatted, new Point(x, y));
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double size, Brush brush, FontWeight weight)
    {
        context.DrawText(CreateText(text, size, brush, weight), new Point(x, y));
    }

    private static FormattedText CreateText(string text, double size, Brush brush, FontWeight weight)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);
    }

    private IReadOnlyList<MapNode> GetRoute(bool isRadiant)
    {
        var position = RolePlan?.Position?.Trim();
        return (position, isRadiant) switch
        {
            ("1", true) => [new(5900, -6400), Camp("good_1"), Camp("good_2"), Camp("good_15"), Camp("good_14")],
            ("1", false) => [new(-5700, 6500), Camp("evil_1"), Camp("evil_2"), Camp("evil_15"), Camp("evil_14")],
            ("2", true) => [new(-500, -650), Camp("good_5"), Camp("good_4"), Camp("good_15"), Camp("good_1")],
            ("2", false) => [new(500, 650), Camp("evil_5"), Camp("evil_6"), Camp("evil_15"), Camp("evil_1")],
            ("5", true) => [new(5700, -6500), Camp("good_2"), Camp("good_1"), Camp("good_15"), Camp("good_4")],
            ("5", false) => [new(-5600, 6500), Camp("evil_2"), Camp("evil_1"), Camp("evil_15"), Camp("evil_6")],
            (_, true) => [new(-500, -650), Camp("good_5"), Camp("good_4")],
            _ => [new(500, 650), Camp("evil_5"), Camp("evil_6")]
        };
    }

    private static MapNode Camp(string id)
    {
        var camp = Camps.First(value => value.Id.Equals(id, StringComparison.Ordinal));
        return new MapNode(camp.X, camp.Y);
    }

    private static Point ToPoint(Rect rect, double x, double y)
    {
        return new Point(
            rect.Left + ((x - WorldMin) / WorldRange * rect.Width),
            rect.Top + ((WorldMin + WorldRange - y) / WorldRange * rect.Height));
    }

    private static readonly IReadOnlyList<CampPoint> Camps =
    [
        new("evil_1", -4823.878906, 3914.796143, CampKind.Hard),
        new("evil_11", -2880, 7376, CampKind.Ancient),
        new("evil_12", 2016, 7896, CampKind.Normal),
        new("evil_13", 8429.652344, 1262.828003, CampKind.Normal),
        new("evil_14", 336, 7696, CampKind.Ancient),
        new("evil_15", -2595.518799, 3850.051758, CampKind.Normal),
        new("evil_2", -3910.783691, 4829.345215, CampKind.Hard),
        new("evil_20", -4208, 8336, CampKind.Normal),
        new("evil_4", 1223.999878, 4176, CampKind.Normal),
        new("evil_5", 1064, 2580, CampKind.Hard),
        new("evil_6", -852, 4940.000488, CampKind.Ancient),
        new("evil_7", 7928, -120, CampKind.Hard),
        new("evil_8", 4352, 48, CampKind.Normal),
        new("evil_9", 3392, -1408, CampKind.Normal),
        new("good_1", 3978.452881, -5026.539062, CampKind.Hard),
        new("good_11", -720.000061, -7696, CampKind.Ancient),
        new("good_12", -2415, -8402, CampKind.Normal),
        new("good_13", -8313.316406, -552.892578, CampKind.Hard),
        new("good_14", 2768, -8336, CampKind.Ancient),
        new("good_15", 1921.578247, -3974.81665, CampKind.Normal),
        new("good_16", -8023, -1838, CampKind.Normal),
        new("good_2", 4649.26123, -3698.830566, CampKind.Hard),
        new("good_20", 4416, -8432, CampKind.Normal),
        new("good_4", 186, -5197.15332, CampKind.Ancient),
        new("good_5", -1453.74707, -3356.215088, CampKind.Hard),
        new("good_7", -4012.741699, 991.650146, CampKind.Normal),
        new("good_8", -5014.630371, -96, CampKind.Normal),
        new("good_9", -1983, -4815, CampKind.Normal)
    ];

    private sealed record CampPoint(string Id, double X, double Y, CampKind Kind);
    private sealed record MapNode(double X, double Y);

    private enum CampKind
    {
        Normal,
        Hard,
        Ancient
    }
}
