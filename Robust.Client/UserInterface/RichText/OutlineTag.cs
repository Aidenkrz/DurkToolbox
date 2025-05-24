using System.Globalization; // Required for float.Parse
using Robust.Client.Graphics; // Required for Color
using Robust.Shared.Maths; // Required for Color
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface.RichText;

/// <summary>
/// Applies an outline to the text within its opening and closing nodes.
/// Attributes:
/// - thickness (float): The thickness of the outline.
/// - color (string): The color of the outline (e.g., "red", "#FF0000", "#FF0000AA").
/// </summary>
public sealed class OutlineTag : IMarkupTag
{
    public const string DefaultOutlineColor = "transparent"; // Default to transparent, effectively no outline
    public const float DefaultOutlineThickness = 1f;

    public string Name => "outline";

    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        float thickness = DefaultOutlineThickness;
        if (node.Attributes.TryGetValue("thickness", out var thicknessAttr) &&
            float.TryParse(thicknessAttr.StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedThickness))
        {
            thickness = parsedThickness;
        }

        Color color = Graphics.Color.Transparent;
        if (node.Attributes.TryGetValue("color", out var colorAttr))
        {
            // TryGetColor should handle hex and named colors.
            // We need a MarkupNodeValue to use TryGetColor. Let's assume colorAttr.StringValue is the raw string.
            // For simplicity, let's try Color.TryFromHex first, then Color.FromName if that fails.
            // Or, if FormattedMessage.MarkupNodeValue has a helper, that would be better.
            // Assuming colorAttr.StringValue is what we get.
            var colorString = colorAttr.StringValue;
            if (!string.IsNullOrWhiteSpace(colorString))
            {
                if (Graphics.Color.TryFromHex(colorString, out var hexColor, true)) // Allow alpha
                {
                    color = hexColor;
                }
                else
                {
                    // Fallback for named colors or other formats if supported by a future central parser
                    // For now, this is a simple named color check. Robust's Color might not have FromName directly.
                    // Let's assume a limited set or rely on hex for now.
                    // If node.Value.TryGetColor exists and can be used on attribute values, that's preferred.
                    // For now, we'll keep it simple: Hex or common names if Color had a FromName.
                    // Robust's Color.TryFromName exists.
                    color = Graphics.Color.TryFromName(colorString, out var namedColor) ? namedColor : Graphics.Color.Transparent;
                }
            }
        }
        else // No color attribute, use a default or transparent.
        {
            color = Graphics.Color.Transparent; // Default to transparent if color attribute is missing
        }

        context.PushOutline(thickness, color);
    }

    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.PopOutline();
    }
}
