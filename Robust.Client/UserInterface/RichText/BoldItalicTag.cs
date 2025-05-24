using Robust.Client.Graphics; // Required for FontWeight and FontStyle
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface.RichText;

public sealed class BoldItalicTag : IMarkupTag
{
    // No longer needs IResourceCache or IPrototypeManager.
    public string Name => "bolditalic";

    /// <inheritdoc/>
    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        // Set both bold and italic
        context.PushFontStyle(FontWeight.Bold, FontStyle.Italic);
    }

    /// <inheritdoc/>
    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.PopFontStyle();
    }
}
