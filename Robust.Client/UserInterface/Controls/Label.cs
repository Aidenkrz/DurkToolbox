using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A label is a GUI control that displays simple text.
    /// </summary>
    [Virtual]
    public class Label : Control
    {
        public const string StylePropertyFontColor = "font-color";
        public const string StylePropertyFont = "font";
        public const string StylePropertyAlignMode = "alignMode";
        public const string StylePropertyFontWeight = "font-weight";
        public const string StylePropertyFontStyle = "font-style";
        public const string StylePropertyOutlineThickness = "outline-thickness";
        public const string StylePropertyOutlineColor = "outline-color";
        public const string StylePropertyShadowOffset = "shadow-offset";
        public const string StylePropertyShadowColor = "shadow-color";

        private int _cachedTextHeight;
        private readonly List<int> _cachedTextWidths = new();
        private bool _textDimensionCacheValid;
        private string? _text;
        private ReadOnlyMemory<char> _textMemory;
        private bool _clipText;
        private AlignMode _align;

        public Label()
        {
            VerticalAlignment = VAlignment.Center;
        }

        /// <summary>
        ///     The text to display.
        /// </summary>
        /// <remarks>
        /// Replaces <see cref="TextMemory"/> when set.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="TextMemory"/> was set directly and there is no backing string instance to fetch.
        /// </exception>
        [ViewVariables]
        public string? Text
        {
            get => _text ?? (_textMemory.Length > 0 ? throw new InvalidOperationException("Label uses TextMemory, cannot fetch string text.") : null);
            set
            {
                _text = value;
                _textMemory = value.AsMemory();
                _textDimensionCacheValid = false;
                InvalidateMeasure();
            }
        }

        /// <summary>
        /// The text to display, set as a read-only memory.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Note that updating the backing memory while the control is using it can result in incorrect display due to caching of measure information and similar.
        /// If you modify the backing storage, re-assign the property to invalidate these.
        /// </para>
        /// <para>
        /// Sets <see cref="Text"/> to throw an exception if read, as there is no backing string to retrieve.
        /// </para>
        /// </remarks>
        public ReadOnlyMemory<char> TextMemory
        {
            get => _textMemory;
            set
            {
                _text = null;
                _textMemory = value;
                _textDimensionCacheValid = false;
                InvalidateMeasure();
            }
        }

        [ViewVariables]
        public bool ClipText
        {
            get => _clipText;
            set
            {
                _clipText = value;
                RectClipContent = value;
                InvalidateMeasure();
            }
        }

        [ViewVariables] public AlignMode Align {
            get
            {
                if (TryGetStyleProperty<AlignMode>(StylePropertyAlignMode, out var alignMode))
                {
                    return alignMode;
                }

                return _align;
            }
            set => _align = value;
        }

        [ViewVariables] public VAlignMode VAlign { get; set; }

        public Font? FontOverride { get; set; }

        private Font ActualFont
        {
            get
            {
                if (FontOverride != null)
                {
                    return FontOverride;
                }

                if (TryGetStyleProperty<Font>(StylePropertyFont, out var font))
                {
                    return font;
                }

                return UserInterfaceManager.ThemeDefaults.LabelFont;
            }
        }

        public Color? FontColorShadowOverride { get; set; }

        private Color ActualFontColor
        {
            get
            {
                if (FontColorOverride.HasValue)
                {
                    return FontColorOverride.Value;
                }

                if (TryGetStyleProperty<Color>(StylePropertyFontColor, out var color))
                {
                    return color;
                }

                return Color.White;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Color? FontColorOverride { get; set; }

        public int? ShadowOffsetXOverride { get; set; }

        public int? ShadowOffsetYOverride { get; set; }

        // New decoration properties
        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public FontWeight? FontWeightOverride { get; set; }

        private FontWeight ActualFontWeight => FontWeightOverride ??
                                             (TryGetStyleProperty<FontWeight>(StylePropertyFontWeight, out var value) ? value : Graphics.FontWeight.Normal);

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public FontStyle? FontStyleOverride { get; set; }

        private FontStyle ActualFontStyle => FontStyleOverride ??
                                           (TryGetStyleProperty<FontStyle>(StylePropertyFontStyle, out var value) ? value : Graphics.FontStyle.Normal);

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public float? OutlineThicknessOverride { get; set; }

        private float ActualOutlineThickness => OutlineThicknessOverride ??
                                                (TryGetStyleProperty<float>(StylePropertyOutlineThickness, out var value) ? value : 0f);

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Color? OutlineColorOverride { get; set; }

        private Color ActualOutlineColor => OutlineColorOverride ??
                                            (TryGetStyleProperty<Color>(StylePropertyOutlineColor, out var value) ? value : Color.Transparent);
        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Vector2? ShadowOffsetOverride { get; set; }

        // Note: ShadowOffsetXOverride and ShadowOffsetYOverride are legacy?
        // New Vector2 ShadowOffset is more complete. For now, let's assume new property takes precedence if set.
        private Vector2 ActualShadowOffset
        {
            get
            {
                if (ShadowOffsetOverride.HasValue) return ShadowOffsetOverride.Value;
                if (TryGetStyleProperty<Vector2>(StylePropertyShadowOffset, out var value)) return value;
                // Legacy fallback - consider if this is desired.
                // For simplicity, I'm prioritizing the new Vector2 property.
                // If ShadowOffsetXOverride or ShadowOffsetYOverride are set, they won't be used if StylePropertyShadowOffset or ShadowOffsetOverride is set.
                if (ShadowOffsetXOverride.HasValue || ShadowOffsetYOverride.HasValue)
                    return new Vector2(ShadowOffsetXOverride ?? 0, ShadowOffsetYOverride ?? 0);
                return Vector2.Zero;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Color? ShadowColorOverride { get; set; } // Renamed from FontColorShadowOverride for consistency

        private Color ActualShadowColor => ShadowColorOverride ?? // Property formerly FontColorShadowOverride
                                           (TryGetStyleProperty<Color>(StylePropertyShadowColor, out var value) ? value : Color.Transparent);


        protected internal override void Draw(DrawingHandleScreen handle)
        {
            if (_textMemory.Length == 0)
            {
                return;
            }

            if (!_textDimensionCacheValid)
            {
                _calculateTextDimension();
                DebugTools.Assert(_textDimensionCacheValid);
            }

            int vOffset;
            switch (VAlign)
            {
                case VAlignMode.Top:
                    vOffset = 0;
                    break;
                case VAlignMode.Fill:
                case VAlignMode.Center:
                    vOffset = (PixelSize.Y - _cachedTextHeight) / 2;
                    break;
                case VAlignMode.Bottom:
                    vOffset = PixelSize.Y - _cachedTextHeight;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var newlines = 0;
            var font = ActualFont;
            var actualFontColor = ActualFontColor;

            // Get actual decoration values
            var fontWeight = ActualFontWeight;
            var fontStyle = ActualFontStyle;
            var outlineThickness = ActualOutlineThickness;
            var outlineColor = ActualOutlineColor;
            var shadowOffset = ActualShadowOffset;
            var shadowColor = ActualShadowColor;

            Vector2 CalcBaseline()
            {
                DebugTools.Assert(_textDimensionCacheValid);

                int hOffset;
                switch (Align)
                {
                    case AlignMode.Left:
                        hOffset = 0;
                        break;
                    case AlignMode.Center:
                    case AlignMode.Fill:
                        hOffset = (PixelSize.X - _cachedTextWidths[newlines]) / 2;
                        break;
                    case AlignMode.Right:
                        hOffset = PixelSize.X - _cachedTextWidths[newlines];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return new Vector2(hOffset, font.GetAscent(UIScale) + font.GetLineHeight(UIScale) * newlines + vOffset);
            }

            var baseLine = CalcBaseline();

            foreach (var rune in _textMemory.Span.EnumerateRunes())
            {
                if (rune == new Rune('\n'))
                {
                    newlines += 1;
                    baseLine = CalcBaseline();
                }

                var advance = font.DrawChar(handle, rune, baseLine, UIScale, actualFontColor,
                                            fontWeight, fontStyle, outlineThickness, outlineColor, shadowOffset, shadowColor);
                baseLine += new Vector2(advance, 0);
            }
        }

        public enum AlignMode : byte
        {
            Left = 0,
            Center = 1,
            Right = 2,
            Fill = 3
        }

        public enum VAlignMode : byte
        {
            Top = 0,
            Center = 1,
            Bottom = 2,
            Fill = 3
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (!_textDimensionCacheValid)
            {
                _calculateTextDimension();
                DebugTools.Assert(_textDimensionCacheValid);
            }

            if (ClipText)
            {
                return new Vector2(0, _cachedTextHeight / UIScale);
            }

            var totalWidth = 0;
            foreach (var width in _cachedTextWidths)
            {
                totalWidth = Math.Max(totalWidth, width);
            }

            return new Vector2(totalWidth / UIScale, _cachedTextHeight / UIScale);
        }

        protected internal override void UIScaleChanged()
        {
            _textDimensionCacheValid = false;

            base.UIScaleChanged();
        }

        private void _calculateTextDimension()
        {
            _cachedTextWidths.Clear();
            _cachedTextWidths.Add(0);

            if (_textMemory.Length == 0)
            {
                _cachedTextHeight = 0;
                _textDimensionCacheValid = true;
                return;
            }

            var font = ActualFont;

            // Get actual decoration values relevant for metrics
            var fontWeight = ActualFontWeight;
            var fontStyle = ActualFontStyle;
            var outlineThickness = ActualOutlineThickness;

            var height = font.GetHeight(UIScale); // Base height, decorations might increase it effectively but GetHeight is per font.
                                                 // Outline, for example, expands glyphs but GetHeight is usually about line spacing.
                                                 // The per-glyph metrics will account for individual size changes.

            foreach (var rune in _textMemory.Span.EnumerateRunes())
            {
                if (rune == new Rune('\n'))
                {
                    _cachedTextWidths.Add(0);
                    height += font.GetLineHeight(UIScale); // Line height shouldn't change with style per se
                }
                else
                {
                    // Pass decoration parameters to GetCharMetrics
                    var metrics = font.GetCharMetrics(rune, UIScale, fontWeight, fontStyle, outlineThickness);
                    if (metrics == null)
                    {
                        continue;
                    }

                    _cachedTextWidths[^1] += metrics.Value.Advance;
                }
            }

            // TODO: _cachedTextHeight might need to be adjusted if outlines/bold significantly increase perceived line height.
            // For now, using font.GetLineHeight() and font.GetHeight() which are generally style-agnostic at the Font level.
            // The individual glyphs are measured correctly. Total height for layout might need more thought if styles make lines taller.
            _cachedTextHeight = height;
            _textDimensionCacheValid = true;
        }

        protected override void StylePropertiesChanged()
        {
            _textDimensionCacheValid = false;

            base.StylePropertiesChanged();
        }
    }
}
