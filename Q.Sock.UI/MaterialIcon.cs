using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Q.Sock.UI;

public sealed class MaterialIcon : Control
{
    private static readonly Lazy<Dictionary<string, MaterialIconNode[]>> Sources = new(() =>
    {
        using var stream = IconResources.Open("material");
        return JsonSerializer.Deserialize(
                stream,
                MaterialIconsJsonContext.Default.DictionaryStringMaterialIconNodeArray
            ) ?? throw new InvalidDataException("Каталог Material Icons не загружен.");
    });
    private static readonly Lazy<Dictionary<string, string>> Lookup = new(() =>
        Sources.Value.Keys.ToDictionary(Normalize, name => name, StringComparer.Ordinal)
    );
    private static readonly Dictionary<string, DrawingNode[]> Drawings = [];
    private readonly DrawingNode[] drawing;

    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<
        MaterialIcon,
        IBrush?
    >(nameof(Foreground));

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public string IconName { get; }

    public static IReadOnlyCollection<string> Names => Sources.Value.Keys;

    static MaterialIcon() => AffectsRender<MaterialIcon>(ForegroundProperty);

    public MaterialIcon(string name, double size = 20)
    {
        IconName =
            ResolveName(name)
            ?? throw new ArgumentException("Неизвестная Material-иконка.", nameof(name));
        Width = size;
        Height = size;
        this.Bind(ForegroundProperty, new DynamicResourceExtension("text"));
        if (!Drawings.TryGetValue(IconName, out drawing!))
        {
            drawing = Sources.Value[IconName].Select(CreateDrawing).ToArray();
            Drawings.Add(IconName, drawing);
        }
        AutomationProperties.SetName(this, IconName);
    }

    public static string? ResolveName(string name) =>
        Lookup.Value.GetValueOrDefault(Normalize(name));

    public override void Render(DrawingContext context)
    {
        using var options = context.PushRenderOptions(
            new RenderOptions { RequiresFullOpacityHandling = true }
        );
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24;
        var offset = new Vector((Bounds.Width - scale * 24) / 2, (Bounds.Height - scale * 24) / 2);
        using var transform = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)
        );
        Draw(context, drawing);
    }

    private void Draw(DrawingContext context, IEnumerable<DrawingNode> nodes)
    {
        foreach (var node in nodes)
        {
            using var opacity = context.PushOpacity(node.Opacity);
            using var transform = context.PushTransform(node.Transform);
            if (node.Geometry is { } geometry)
                context.DrawGeometry(Foreground, null, geometry);
            else
                Draw(context, node.Children);
        }
    }

    private static DrawingNode CreateDrawing(MaterialIconNode node)
    {
        var geometry = node.Type switch
        {
            "path" => Geometry.Parse((node.FillRule == "evenodd" ? "F0 " : "F1 ") + node.Data),
            "circle" => new EllipseGeometry(
                new Rect(node.Cx - node.R, node.Cy - node.R, node.R * 2, node.R * 2)
            ),
            "ellipse" => new EllipseGeometry(
                new Rect(node.Cx - node.Rx, node.Cy - node.Ry, node.Rx * 2, node.Ry * 2)
            ),
            "g" => null,
            _ => throw new InvalidDataException($"Неизвестная фигура Material Icons: {node.Type}."),
        };
        var transform = node.Scale is { Length: 2 } scale
            ? Matrix.CreateScale(scale[0], scale[1])
            : Matrix.Identity;
        return new DrawingNode(
            geometry,
            node.Opacity * node.FillOpacity,
            transform,
            node.Children.Select(CreateDrawing).ToArray()
        );
    }

    private static string Normalize(string name) =>
        name.Replace("_", "").Replace("-", "").ToLowerInvariant();

    private sealed record DrawingNode(
        Geometry? Geometry,
        double Opacity,
        Matrix Transform,
        DrawingNode[] Children
    );
}

internal sealed class MaterialIconNode
{
    public string Type { get; set; } = "";
    public string? Data { get; set; }
    public double Opacity { get; set; } = 1;
    public double FillOpacity { get; set; } = 1;
    public string? FillRule { get; set; }
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double R { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }
    public double[]? Scale { get; set; }
    public MaterialIconNode[] Children { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, MaterialIconNode[]>))]
internal partial class MaterialIconsJsonContext : JsonSerializerContext;
