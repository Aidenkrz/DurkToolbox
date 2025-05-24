using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Collections;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface
{
    /// <summary>
    ///     Used by <see cref="OutputPanel"/> and <see cref="RichTextLabel"/> to handle rich text layout.
    /// </summary>
    internal struct RichTextEntry
    {
        private readonly Color _defaultColor;
        private readonly Type[]? _tagsAllowed;

        public readonly FormattedMessage Message;

        /// <summary>
        ///     The vertical size of this entry, in pixels.
        /// </summary>
        public int Height;

        /// <summary>
        ///     The horizontal size of this entry, in pixels.
        /// </summary>
        public int Width;

        /// <summary>
        ///     The combined text indices in the message's text tags to put line breaks.
        /// </summary>
        public ValueList<int> LineBreaks;

        private readonly Dictionary<int, Control>? _tagControls;

        public RichTextEntry(FormattedMessage message, Control parent, MarkupTagManager tagManager, Type[]? tagsAllowed = null, Color? defaultColor = null)
        {
            Message = message;
            Height = 0;
            Width = 0;
            LineBreaks = default;
            _defaultColor = defaultColor ?? new(200, 200, 200);
            _tagsAllowed = tagsAllowed;
            Dictionary<int, Control>? tagControls = null;

            var nodeIndex = -1;
            foreach (var node in Message)
            {
                nodeIndex++;

                if (node.Name == null)
                    continue;

                if (!tagManager.TryGetMarkupTag(node.Name, _tagsAllowed, out var tag) || !tag.TryGetControl(node, out var control))
                    continue;

                parent.Children.Add(control);
                tagControls ??= new Dictionary<int, Control>();
                tagControls.Add(nodeIndex, control);
            }

            _tagControls = tagControls;
        }

        /// <summary>
        ///     Recalculate line dimensions and where it has line breaks for word wrapping.
        /// </summary>
        /// <param name="defaultFont">The font being used for display.</param>
        /// <param name="maxSizeX">The maximum horizontal size of the container of this entry.</param>
        /// <param name="uiScale"></param>
        /// <param name="lineHeightScale"></param>
        public void Update(MarkupTagManager tagManager, Font defaultFont, float maxSizeX, float uiScale, float lineHeightScale = 1)
        {
            // This method is gonna suck due to complexity.
            // Bear with me here.
            // I am so deeply sorry for the person adding stuff to this in the future.

            Height = defaultFont.GetHeight(uiScale);
            LineBreaks.Clear();

            int? breakLine;
            var wordWrap = new WordWrap(maxSizeX);
            var context = new MarkupDrawingContext();
            // Initialize context with defaults for this entry
            context.CurrentFont = defaultFont;
            context.CurrentColor = _defaultColor;
            // FontStack and ColorStack are used by FontTag and ColorTag if they PUSH/POP.
            // For base values, CurrentFont/CurrentColor are primary.
            // Ensure these are also on the stack if tags expect to Pop to them.
            context.FontStack.Push(defaultFont); // So a Pop without a corresponding Push by a tag reverts to base
            context.ColorStack.Push(_defaultColor);


            // Go over every node.
            // Nodes can change the markup drawing context and return additional text.
            // It's also possible for nodes to return inline controls. They get treated as one large rune.
            var nodeIndex = -1;
            foreach (var node in Message)
            {
                nodeIndex++;
                var text = ProcessNode(tagManager, node, context);

                // Use the font from the context, which might have been changed by a tag
                var font = context.CurrentFont;

                // And go over every character.
                foreach (var rune in text.EnumerateRunes())
                {
                    if (ProcessRune(ref this, rune, out breakLine))
                        continue;

                    // Uh just skip unknown characters I guess.
                    // Pass current font weight, style, and outline thickness from context to GetCharMetrics
                    if (!font.TryGetCharMetrics(rune, uiScale, out var metrics,
                                               context.CurrentFontWeight,
                                               context.CurrentFontStyle,
                                               context.CurrentOutlineThickness))
                        continue;

                    if (ProcessMetric(ref this, metrics, out breakLine))
                        return;
                }

                if (_tagControls == null || !_tagControls.TryGetValue(nodeIndex, out var control))
                    continue;

                control.Measure(new Vector2(Width, Height));

                var desiredSize = control.DesiredPixelSize;
                var controlMetrics = new CharMetrics(
                    0, 0,
                    desiredSize.X,
                    desiredSize.X,
                    desiredSize.Y);

                if (ProcessMetric(ref this, controlMetrics, out breakLine))
                    return;
            }

            Width = wordWrap.FinalizeText(out breakLine);
            CheckLineBreak(ref this, breakLine);

            bool ProcessRune(ref RichTextEntry src, Rune rune, out int? outBreakLine)
            {
                wordWrap.NextRune(rune, out breakLine, out var breakNewLine, out var skip);
                CheckLineBreak(ref src, breakLine);
                CheckLineBreak(ref src, breakNewLine);
                outBreakLine = breakLine;
                return skip;
            }

            bool ProcessMetric(ref RichTextEntry src, CharMetrics metrics, out int? outBreakLine)
            {
                wordWrap.NextMetrics(metrics, out breakLine, out var abort);
                CheckLineBreak(ref src, breakLine);
                outBreakLine = breakLine;
                return abort;
            }

            void CheckLineBreak(ref RichTextEntry src, int? line)
            {
                if (line is { } l)
                {
                    src.LineBreaks.Add(l);
                    // Use the font from the context for line height calculation
                    var fontForLineHeight = context.CurrentFont;
                    src.Height += GetLineHeight(fontForLineHeight, uiScale, lineHeightScale);
                }
            }
        }

        internal readonly void HideControls()
        {
            if (_tagControls == null)
                return;
            foreach (var control in _tagControls.Values)
            {
                control.Visible = false;
            }
        }

        public readonly void Draw(
            MarkupTagManager tagManager,
            DrawingHandleBase handle,
            Font defaultFont, // Base font, context will hold the current font
            UIBox2 drawBox,
            float verticalOffset,
            MarkupDrawingContext context, // This context is now pre-initialized by RichTextLabel
            float uiScale,
            float lineHeightScale = 1)
        {
            // context.Clear() was here, but RichTextLabel now initializes it.
            // We need to ensure the base default color and font are set correctly if not already.
            // RichTextLabel sets context.CurrentFont.
            // Let's ensure CurrentColor is also set if not by RichTextLabel.
            // MarkupDrawingContext defaults CurrentColor to White, _defaultColor from RichTextEntry might be different.
            context.CurrentColor = _defaultColor; // Ensure entry's default color is the starting current color
            context.ColorStack.Clear(); // Clear any previous stack state from other draws if context is reused
            context.FontStack.Clear();  // Clear any previous stack state
            context.ColorStack.Push(_defaultColor); // Set base color on stack
            context.FontStack.Push(context.CurrentFont); // CurrentFont is already set by RichTextLabel, push it as base

            var globalBreakCounter = 0;
            var lineBreakIndex = 0;
            var baseLine = drawBox.TopLeft + new Vector2(0, defaultFont.GetAscent(uiScale) + verticalOffset);
            var controlYAdvance = 0f;

            var nodeIndex = -1;
            foreach (var node in Message)
            {
                nodeIndex++;
                var text = ProcessNode(tagManager, node, context);

                // Use current font, color, and all decoration parameters from the context
                var currentFont = context.CurrentFont;
                var currentColor = context.CurrentColor;
                var currentWeight = context.CurrentFontWeight;
                var currentStyle = context.CurrentFontStyle;
                var currentOutlineThickness = context.CurrentOutlineThickness;
                var currentOutlineColor = context.CurrentOutlineColor;
                var currentShadowOffset = context.CurrentShadowOffset;
                var currentShadowColor = context.CurrentShadowColor;

                foreach (var rune in text.EnumerateRunes())
                {
                    if (lineBreakIndex < LineBreaks.Count &&
                        LineBreaks[lineBreakIndex] == globalBreakCounter)
                    {
                        // Use currentFont for line height calculation at the break point
                        baseLine = new Vector2(drawBox.Left, baseLine.Y + GetLineHeight(currentFont, uiScale, lineHeightScale) + controlYAdvance);
                        controlYAdvance = 0;
                        lineBreakIndex += 1;
                    }

                    var advance = currentFont.DrawChar(handle, rune, baseLine, uiScale, currentColor,
                                                       currentWeight, currentStyle,
                                                       currentOutlineThickness, currentOutlineColor,
                                                       currentShadowOffset, currentShadowColor);
                    baseLine += new Vector2(advance, 0);

                    globalBreakCounter += 1;
                }

                if (_tagControls == null || !_tagControls.TryGetValue(nodeIndex, out var control))
                    continue;

                // Controls may have been previously hidden via HideControls due to being "out-of frame".
                // If this ever gets replaced with RectClipContents / scissor box testing, this can be removed.
                control.Visible = true;

                var invertedScale = 1f / uiScale;
                control.Position = new Vector2(baseLine.X * invertedScale, (baseLine.Y - context.CurrentFont.GetAscent(uiScale)) * invertedScale);
                control.Measure(new Vector2(Width, Height));
                var advanceX = control.DesiredPixelSize.X;
                controlYAdvance = Math.Max(0f, (control.DesiredPixelSize.Y - GetLineHeight(context.CurrentFont, uiScale, lineHeightScale)) * invertedScale);
                baseLine += new Vector2(advanceX, 0);
            }
        }

        private readonly string ProcessNode(MarkupTagManager tagManager, MarkupNode node, MarkupDrawingContext context)
        {
            // If a nodes name is null it's a text node.
            if (node.Name == null)
                return node.Value.StringValue ?? "";

            //Skip the node if there is no markup tag for it.
            if (!tagManager.TryGetMarkupTag(node.Name, _tagsAllowed, out var tag))
                return "";

            if (!node.Closing)
            {
                tag.PushDrawContext(node, context);
                return tag.TextBefore(node);
            }

            tag.PopDrawContext(node, context);
            return tag.TextAfter(node);
        }

        private static int GetLineHeight(Font font, float uiScale, float lineHeightScale)
        {
            var height = font.GetLineHeight(uiScale);
            return (int)(height * lineHeightScale);
        }
    }
}
