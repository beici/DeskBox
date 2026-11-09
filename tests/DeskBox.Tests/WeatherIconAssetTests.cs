using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DeskBox.Tests;

/// <summary>
/// Guards the shipped weather icon assets. The weather widget is wired up correctly even when the
/// art itself is unreadable, so nothing else in the suite can notice an asset that renders as a
/// featureless near-white tile at the 18-22px sizes the widget actually uses. These tests read the
/// SVG payloads directly instead of rendering them, which keeps them dependency-free and fast.
/// </summary>
public class WeatherIconAssetTests
{
    /// <summary>
    /// Minimum contrast between the darkest and the lightest paint colour inside one icon. A filled
    /// illustration below roughly 2:1 collapses into a single blob once it is scaled down.
    /// </summary>
    private const double MinimumFluentLuminanceSpan = 2.0;

    /// <summary>
    /// Maximum share of the canvas a single opaque filled shape may cover on either axis. An icon
    /// that paints most of its own box loses its silhouette against the wallpaper. The threshold
    /// sits between the widest legitimate shape in the shipped sets (the fluent clear-day glow at
    /// 80.6% x 81.9%) and the opaque plate that broke fluent/fog.svg (87.5% x 87.5%).
    /// </summary>
    private const double MaximumOpaqueCanvasCoverage = 0.85;

    /// <summary>
    /// Known P1 debt: these fluent assets still sit at an internal luminance span of 1.54-1.69 and
    /// read as a near-white block on a light wallpaper. They are listed so the suite stays green
    /// while the redraw is scheduled; remove an entry in the same change that redraws the asset.
    /// </summary>
    private static readonly string[] KnownFluentContrastDebt =
    [
        "src/DeskBox/Assets/WeatherIcons/fluent/partly-cloudy-night.svg",
        "src/DeskBox/Assets/WeatherIcons/fluent/snow.svg",
    ];

    private static readonly string[] PaintAttributes = ["fill", "stroke", "stop-color"];

    /// <summary>Containers whose children are never painted on their own.</summary>
    private static readonly string[] NonRenderedContainers = ["defs", "mask", "clipPath", "pattern", "symbol"];

    private static readonly Regex PathTokenRegex = new(
        @"[MmLlHhVvCcSsQqTtAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex HexColorRegex = new(
        @"^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
        RegexOptions.CultureInvariant);

    private static readonly Regex GradientReferenceRegex = new(
        @"^url\(#(?<id>[A-Za-z0-9_:.\-]+)\)$",
        RegexOptions.CultureInvariant);

    private static readonly Dictionary<char, int> PathArity = new()
    {
        ['M'] = 2,
        ['L'] = 2,
        ['T'] = 2,
        ['H'] = 1,
        ['V'] = 1,
        ['C'] = 6,
        ['S'] = 4,
        ['Q'] = 4,
        ['A'] = 7,
        ['Z'] = 0,
    };

    [Fact]
    public void FluentWeatherIcons_KeepAnInternalLuminanceSpanOfTwo()
    {
        string root = FindRepositoryRoot();
        string fluentDirectory = Path.Combine(root, "src/DeskBox/Assets/WeatherIcons/fluent");
        Assert.True(Directory.Exists(fluentDirectory), $"Missing fluent icon directory: {fluentDirectory}");

        // Only the fluent set is checked here. flat/ and line/ are single-stroke Meteocons line art
        // and the root set is a single-hue silhouette set; for both of those the icon reads through
        // stroke weight and background separation, so a fill-colour range means nothing.
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fluentDirectory, "*.svg").OrderBy(p => p, StringComparer.Ordinal))
        {
            XDocument document = XDocument.Load(file);
            List<string> colors = CollectPaintColors(document);
            if (colors.Count < 2)
            {
                offenders.Add(RelativeTo(root, file));
                continue;
            }

            double darkest = colors.Min(RelativeLuminance);
            double lightest = colors.Max(RelativeLuminance);
            double span = (lightest + 0.05) / (darkest + 0.05);
            if (span < MinimumFluentLuminanceSpan)
            {
                offenders.Add(RelativeTo(root, file));
            }
        }

        Assert.Equal(KnownFluentContrastDebt, offenders);
    }

    [Fact]
    public void WeatherIcons_DoNotDrawAnOpaqueBackdropAcrossTheCanvas()
    {
        string root = FindRepositoryRoot();
        string iconsDirectory = Path.Combine(root, "src/DeskBox/Assets/WeatherIcons");
        Assert.True(Directory.Exists(iconsDirectory), $"Missing weather icon directory: {iconsDirectory}");

        var offenders = new List<string>();
        foreach (string file in Directory
                     .EnumerateFiles(iconsDirectory, "*.svg", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            XDocument document = XDocument.Load(file);
            (double canvasWidth, double canvasHeight) = ReadCanvasSize(document);
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                continue;
            }

            foreach (XElement element in document.Descendants())
            {
                if (!IsPaintedShape(element) || !IsOpaqueFill(document, element))
                {
                    continue;
                }

                (double shapeWidth, double shapeHeight)? extent = ReadShapeExtent(element);
                if (extent is null)
                {
                    continue;
                }

                if (extent.Value.shapeWidth < canvasWidth * MaximumOpaqueCanvasCoverage
                    || extent.Value.shapeHeight < canvasHeight * MaximumOpaqueCanvasCoverage)
                {
                    continue;
                }

                offenders.Add(
                    $"{RelativeTo(root, file)}: <{element.Name.LocalName}> covers "
                    + $"{extent.Value.shapeWidth:0.#}x{extent.Value.shapeHeight:0.#} "
                    + $"of a {canvasWidth:0.#}x{canvasHeight:0.#} canvas");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void WeatherIcons_WithinAStyleFolder_AreAllDistinct()
    {
        string root = FindRepositoryRoot();
        string iconsDirectory = Path.Combine(root, "src/DeskBox/Assets/WeatherIcons");

        var duplicates = new List<string>();
        IEnumerable<IGrouping<string, string>> folders = Directory
            .EnumerateFiles(iconsDirectory, "*.svg", SearchOption.AllDirectories)
            .GroupBy(file => Path.GetDirectoryName(file) ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, string> folder in folders)
        {
            var byHash = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in folder.OrderBy(p => p, StringComparer.Ordinal))
            {
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
                if (byHash.TryGetValue(hash, out string? existing))
                {
                    duplicates.Add($"{RelativeTo(root, existing)} == {RelativeTo(root, file)}");
                }
                else
                {
                    byHash[hash] = file;
                }
            }
        }

        Assert.Empty(duplicates);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "DeskBox")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string RelativeTo(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    private static List<string> CollectPaintColors(XDocument document)
    {
        var colors = new List<string>();
        foreach (XElement element in document.Descendants())
        {
            foreach (string attributeName in PaintAttributes)
            {
                string? value = (string?)element.Attribute(attributeName);
                if (value is null)
                {
                    continue;
                }

                string trimmed = value.Trim();
                if (!HexColorRegex.IsMatch(trimmed))
                {
                    continue;
                }

                // A fully transparent gradient stop contributes no visible paint.
                if (element.Name.LocalName == "stop"
                    && ParseNumber((string?)element.Attribute("stop-opacity"), 1.0) <= 0)
                {
                    continue;
                }

                colors.Add(trimmed.ToUpperInvariant());
            }
        }

        return colors;
    }

    private static double RelativeLuminance(string color)
    {
        string value = color.TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        double red = LinearChannel(value[..2]);
        double green = LinearChannel(value.Substring(2, 2));
        double blue = LinearChannel(value.Substring(4, 2));
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double LinearChannel(string hexPair)
    {
        int channel = int.Parse(hexPair, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static bool IsPaintedShape(XElement element)
    {
        if (element.Name.LocalName is not ("path" or "rect"))
        {
            return false;
        }

        return !element.Ancestors().Any(ancestor => NonRenderedContainers.Contains(ancestor.Name.LocalName));
    }

    private static bool IsOpaqueFill(XDocument document, XElement element)
    {
        if (ParseNumber((string?)element.Attribute("fill-opacity"), 1.0) < 1.0
            || ParseNumber((string?)element.Attribute("opacity"), 1.0) < 1.0)
        {
            return false;
        }

        string? fill = ResolveFill(element);
        if (fill is null)
        {
            return false;
        }

        if (fill.Equals("none", StringComparison.OrdinalIgnoreCase)
            || fill.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Match reference = GradientReferenceRegex.Match(fill);
        if (reference.Success)
        {
            return IsOpaqueGradient(document, reference.Groups["id"].Value);
        }

        return HexColorRegex.IsMatch(fill);
    }

    private static string? ResolveFill(XElement element)
    {
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            string? value = (string?)current.Attribute("fill");
            if (value is not null)
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsOpaqueGradient(XDocument document, string id)
    {
        XElement? gradient = document.Descendants().FirstOrDefault(candidate =>
            (candidate.Name.LocalName is "linearGradient" or "radialGradient")
            && string.Equals((string?)candidate.Attribute("id"), id, StringComparison.Ordinal));
        if (gradient is null)
        {
            return false;
        }

        List<XElement> stops = gradient.Elements().Where(candidate => candidate.Name.LocalName == "stop").ToList();
        if (stops.Count == 0)
        {
            return false;
        }

        foreach (XElement stop in stops)
        {
            if (ParseNumber((string?)stop.Attribute("stop-opacity"), 1.0) < 1.0)
            {
                return false;
            }

            string color = ((string?)stop.Attribute("stop-color") ?? string.Empty).Trim();
            if (!HexColorRegex.IsMatch(color))
            {
                return false;
            }
        }

        return true;
    }

    private static (double Width, double Height) ReadCanvasSize(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null)
        {
            return (0, 0);
        }

        string? viewBox = (string?)root.Attribute("viewBox");
        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            string[] parts = viewBox.Split(
                new[] { ' ', ',', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                return (width, height);
            }
        }

        return (ParseNumber((string?)root.Attribute("width")), ParseNumber((string?)root.Attribute("height")));
    }

    private static (double shapeWidth, double shapeHeight)? ReadShapeExtent(XElement element)
    {
        if (element.Name.LocalName == "rect")
        {
            double width = ParseNumber((string?)element.Attribute("width"));
            double height = ParseNumber((string?)element.Attribute("height"));
            return width > 0 && height > 0 ? (width, height) : null;
        }

        string? data = (string?)element.Attribute("d");
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        (double MinX, double MinY, double MaxX, double MaxY)? bounds = MeasurePathEndpoints(data);
        return bounds is null
            ? null
            : (bounds.Value.MaxX - bounds.Value.MinX, bounds.Value.MaxY - bounds.Value.MinY);
    }

    /// <summary>
    /// Approximate bounding box of a path from the coordinates its commands land on. Control points
    /// and arc sweeps are ignored on purpose: every shape this guard cares about (a backdrop plate,
    /// a rounded rectangle) puts its extremes on real vertices, and counting control points would
    /// inflate soft clouds until they looked like backdrops.
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? MeasurePathEndpoints(string data)
    {
        MatchCollection tokens = PathTokenRegex.Matches(data);
        double currentX = 0;
        double currentY = 0;
        double startX = 0;
        double startY = 0;
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        bool measured = false;
        char? command = null;
        int index = 0;

        while (index < tokens.Count)
        {
            string token = tokens[index].Value;
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                command = token[0];
                index++;
                if (command is 'Z' or 'z')
                {
                    currentX = startX;
                    currentY = startY;
                    continue;
                }
            }

            if (command is null)
            {
                break;
            }

            char upper = char.ToUpperInvariant(command.Value);
            if (!PathArity.TryGetValue(upper, out int arity) || arity == 0 || index + arity > tokens.Count)
            {
                break;
            }

            var values = new double[arity];
            bool parsed = true;
            for (int offset = 0; offset < arity; offset++)
            {
                if (!double.TryParse(
                        tokens[index + offset].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out values[offset]))
                {
                    parsed = false;
                    break;
                }
            }

            if (!parsed)
            {
                break;
            }

            index += arity;
            bool relative = char.IsLower(command.Value);

            switch (upper)
            {
                case 'M':
                    currentX = relative ? currentX + values[0] : values[0];
                    currentY = relative ? currentY + values[1] : values[1];
                    startX = currentX;
                    startY = currentY;
                    // Extra coordinate pairs after a moveto are implicit linetos.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    currentX = relative ? currentX + values[0] : values[0];
                    currentY = relative ? currentY + values[1] : values[1];
                    break;
                case 'H':
                    currentX = relative ? currentX + values[0] : values[0];
                    break;
                case 'V':
                    currentY = relative ? currentY + values[0] : values[0];
                    break;
                case 'C':
                case 'S':
                case 'Q':
                case 'T':
                    currentX = relative ? currentX + values[arity - 2] : values[arity - 2];
                    currentY = relative ? currentY + values[arity - 1] : values[arity - 1];
                    break;
                case 'A':
                    currentX = relative ? currentX + values[5] : values[5];
                    currentY = relative ? currentY + values[6] : values[6];
                    break;
            }

            minX = Math.Min(minX, currentX);
            minY = Math.Min(minY, currentY);
            maxX = Math.Max(maxX, currentX);
            maxY = Math.Max(maxY, currentY);
            measured = true;
        }

        return measured ? (minX, minY, maxX, maxY) : null;
    }

    private static double ParseNumber(string? text, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
    }
}
