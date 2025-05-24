using Robust.Client.Graphics; // Required for FontStyle
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface.RichText;

public sealed class ItalicTag : IMarkupTag
{
    // No longer needs IResourceCache or IPrototypeManager for simple italicizing.

    public string Name => "italic";

    /// <inheritdoc/>
    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        // Preserve current bold style, set style to Italic
        context.PushFontStyle(context.CurrentFontWeight, FontStyle.Italic);
    }

    /// <inheritdoc/>
    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.PopFontStyle();
    }
}
