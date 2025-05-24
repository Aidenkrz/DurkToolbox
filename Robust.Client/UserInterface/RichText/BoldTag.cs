using Robust.Client.Graphics; // Required for FontWeight
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface.RichText;

public sealed class BoldTag : IMarkupTag
{
    // No longer needs IResourceCache or IPrototypeManager for simple bolding.
    // If it needed to load a specific "Bold Font" for some reason, those would be kept.

    public string Name => "bold";

    /// <inheritdoc/>
    public void PushDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        // Preserve current italic style, set weight to Bold
        context.PushFontStyle(FontWeight.Bold, context.CurrentFontStyle);
    }

    /// <inheritdoc/>
    public void PopDrawContext(MarkupNode node, MarkupDrawingContext context)
    {
        context.PopFontStyle();
    }
}
