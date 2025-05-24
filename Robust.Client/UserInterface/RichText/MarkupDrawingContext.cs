using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Robust.Client.UserInterface.RichText;

public sealed class MarkupDrawingContext
{
    // Existing stacks
    public readonly Stack<Color> ColorStack; // Renamed for clarity
    public readonly Stack<Font> FontStack;   // Renamed for clarity
    public readonly List<IMarkupTag> Tags; // This seems to be a list of active tags, not a stack for properties

    // Current values - these will be set by tags
    // Tags should handle storing/restoring previous values if they nest in complex ways,
    // or the RichTextLabel needs to recompute the context when tags are popped.
    // For now, tags will directly set these.
    public Color CurrentColor { get; set; } = Graphics.Color.White; // Default color
    public Font CurrentFont { get; set; } = default!; // Should be initialized with a default font by RichTextLabel
    public FontWeight CurrentFontWeight { get; set; } = FontWeight.Normal;
    public FontStyle CurrentFontStyle { get; set; } = FontStyle.Normal;
    public float CurrentOutlineThickness { get; set; } = 0f;
    public Color CurrentOutlineColor { get; set; } = Graphics.Color.Transparent;
    public Vector2 CurrentShadowOffset { get; set; } = Vector2.Zero;
    public Color CurrentShadowColor { get; set; } = Graphics.Color.Transparent;

    // Store previous states for tags to restore
    // This is a simple way for individual tags to manage their changes.
    private readonly Stack<(FontWeight, FontStyle)> _fontStyleStack = new();
    private readonly Stack<(float, Color)> _outlineStack = new();
    private readonly Stack<(Vector2, Color)> _shadowStack = new();
    // Font and Color already have stacks (FontStack, ColorStack)

    public MarkupDrawingContext()
    {
        ColorStack = new Stack<Color>();
        FontStack = new Stack<Font>();
        Tags = new List<IMarkupTag>();
    }

    public MarkupDrawingContext(int capacity)
    {
        ColorStack = new Stack<Color>(capacity);
        FontStack = new Stack<Font>(capacity);
        Tags = new List<IMarkupTag>();
    }

    public void Clear()
    {
        ColorStack.Clear();
        FontStack.Clear();
        Tags.Clear();

        // Reset current values to defaults
        CurrentColor = Graphics.Color.White;
        CurrentFont = default!; // Needs proper default
        CurrentFontWeight = FontWeight.Normal;
        CurrentFontStyle = FontStyle.Normal;
        CurrentOutlineThickness = 0f;
        CurrentOutlineColor = Graphics.Color.Transparent;
        CurrentShadowOffset = Vector2.Zero;
        CurrentShadowColor = Graphics.Color.Transparent;

        _fontStyleStack.Clear();
        _outlineStack.Clear();
        _shadowStack.Clear();
    }

    // Methods for tags to push/pop their specific states
    public void PushFontStyle(FontWeight weight, FontStyle style)
    {
        _fontStyleStack.Push((CurrentFontWeight, CurrentFontStyle));
        CurrentFontWeight = weight;
        CurrentFontStyle = style;
    }

    public void PopFontStyle()
    {
        if (_fontStyleStack.TryPop(out var previous))
        {
            (CurrentFontWeight, CurrentFontStyle) = previous;
        }
        else
        {
            CurrentFontWeight = FontWeight.Normal;
            CurrentFontStyle = FontStyle.Normal;
        }
    }

    public void PushOutline(float thickness, Color color)
    {
        _outlineStack.Push((CurrentOutlineThickness, CurrentOutlineColor));
        CurrentOutlineThickness = thickness;
        CurrentOutlineColor = color;
    }

    public void PopOutline()
    {
        if (_outlineStack.TryPop(out var previous))
        {
            (CurrentOutlineThickness, CurrentOutlineColor) = previous;
        }
        else
        {
            CurrentOutlineThickness = 0f;
            CurrentOutlineColor = Graphics.Color.Transparent;
        }
    }

    public void PushShadow(Vector2 offset, Color color)
    {
        _shadowStack.Push((CurrentShadowOffset, CurrentShadowColor));
        CurrentShadowOffset = offset;
        CurrentShadowColor = color;
    }

    public void PopShadow()
    {
        if (_shadowStack.TryPop(out var previous))
        {
            (CurrentShadowOffset, CurrentShadowColor) = previous;
        }
        else
        {
            CurrentShadowOffset = Vector2.Zero;
            CurrentShadowColor = Graphics.Color.Transparent;
        }
    }

    // Existing Font and Color stack interactions (simplified example, actual ColorTag/FontTag will use these)
    public void PushFont(Font font)
    {
        FontStack.Push(CurrentFont); // Store current before changing
        CurrentFont = font;
    }

    public void PopFont()
    {
        if (FontStack.TryPop(out var previousFont))
        {
            CurrentFont = previousFont;
        }
        // Else: What to do if stack is empty? Keep current or error? Depends on RichTextLabel init.
    }

    public void PushColor(Color color)
    {
        ColorStack.Push(CurrentColor); // Store current before changing
        CurrentColor = color;
    }

    public void PopColor()
    {
        if (ColorStack.TryPop(out var previousColor))
        {
            CurrentColor = previousColor;
        }
    }
}
