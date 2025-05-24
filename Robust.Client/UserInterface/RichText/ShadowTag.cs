using System.Globalization; // Required for float.Parse
using System.Numerics; // Required for Vector2
using Robust.Client.Graphics; // Required for Color
using Robust.Shared.Maths; // Required for Color
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface.RichText;

/// <summary>
/// Applies a shadow to the text within its opening and closing nodes.
/// Attributes:
/// - offset (string): The shadow offset as "X,Y" (e.g., "1,1" or "1.5,0.5").
/// - color (string): The color of the shadow (e.g., "black", "#000000", "#00000080").
/// </summary>
public sealed class ShadowTag : IMarkupTag
{
    public const string DefaultShadowColor = "black";
    public const string DefaultShadowOffset = "1,1";

    public string Name => "shadow";

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        Vector2 offset = ParseVector2(DefaultShadowOffset); // Default
        if (node.Attributes.TryGetValue("offset", out var offsetAttr) &&
            !string.IsNullOrWhiteSpace(offsetAttr.StringValue))
        {
            offset = ParseVector2(offsetAttr.StringValue) ?? offset;
        }

        Color color = Graphics.Color.Black; // Default
        if (node.Attributes.TryGetValue("color", out var colorAttr))
        {
            var colorString = colorAttr.StringValue;
            if (!string.IsNullOrWhiteSpace(colorString))
            {
                if (Graphics.Color.TryFromHex(colorString, out var hexColor, true)) // Allow alpha
                {
                    color = hexColor;
                }
                else
                {
                    color = Graphics.Color.TryFromName(colorString, out var namedColor) ? namedColor : Graphics.Color.Black;
                }
            }
        }
        else // No color attribute, use a default.
        {
             if (Graphics.Color.TryFromName(DefaultShadowColor, out var namedColor))
                color = namedColor;
        }

        context.PushShadow(offset, color);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.PopShadow();
    }

    private static Vector2? ParseVector2(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(',');
        if (parts.Length != 2)
            return null;

        if (float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
            float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
        {
            return new Vector2(x, y);
        }

        return null;
    }
}
