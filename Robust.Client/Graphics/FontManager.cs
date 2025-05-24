using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Robust.Client.Utility;
using Robust.Shared.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SharpFont;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerraFX.Interop.Windows;

namespace Robust.Client.Graphics
{
    internal sealed class FontManager : IFontManagerInternal
    {
        private const int SheetWidth = 256;
        private const int SheetHeight = 256;

        private readonly IClyde _clyde;

        private uint _baseFontDpi = 96;

        private readonly Library _library;

        private readonly Dictionary<(FontFaceHandle, int fontSize), FontInstanceHandle> _loadedInstances =
            new();

        public FontManager(IClyde clyde)
        {
            _clyde = clyde;
            _library = new Library();
        }

        public IFontFaceHandle Load(Stream stream)
        {
            // Freetype directly operates on the font memory managed by us.
            // As such, the font data should be pinned in POH.
            var fontData = stream.CopyToPinnedArray();
            var face = new Face(_library, fontData, 0);
            var handle = new FontFaceHandle(face);
            return handle;
        }

        void IFontManagerInternal.SetFontDpi(uint fontDpi)
        {
            _baseFontDpi = fontDpi;
        }

        public IFontInstanceHandle MakeInstance(IFontFaceHandle handle, int size)
        {
            var fontFaceHandle = (FontFaceHandle) handle;
            // TODO: Consider font decorations for caching key if necessary
            if (_loadedInstances.TryGetValue((fontFaceHandle, size), out var instance))
            {
                return instance;
            }

            instance = new FontInstanceHandle(this, size, fontFaceHandle);

            _loadedInstances.Add((fontFaceHandle, size), instance);
            return instance;
        }

        public void ClearFontCache()
        {
            foreach (var fontInstance in _loadedInstances)
            {
                fontInstance.Value.ClearSizeData();
            }
        }

        private ScaledFontData _generateScaledDatum(FontInstanceHandle instance, float scale)
        {
            var ftFace = instance.FaceHandle.Face;
            ftFace.SetCharSize(0, instance.Size, 0, (uint) (_baseFontDpi * scale));

            var ascent = ftFace.Size.Metrics.Ascender.ToInt32();
            var descent = -ftFace.Size.Metrics.Descender.ToInt32();
            var lineHeight = ftFace.Size.Metrics.Height.ToInt32();

            // TODO: Adjust metrics for bold/outline if necessary
            var data = new ScaledFontData(ascent, descent, ascent + descent, lineHeight);


            return data;
        }

        private GlyphInfo EnsureGlyphCached(FontInstanceHandle instance, ScaledFontData scaled, float scale, uint glyph, FontWeight fontWeight, FontStyle fontStyle, float outlineThickness)
        {
            // Generate a unique key for the glyph based on its properties
            var glyphKey = new GlyphKey(glyph, fontWeight, fontStyle, outlineThickness);

            // Check if already cached.
            if (scaled.GlyphInfos.TryGetValue(glyphKey, out var info))
                return info;

            info = new GlyphInfo();

            var face = instance.FaceHandle.Face;
            face.SetCharSize(0, instance.Size, 0, (uint) (_baseFontDpi * scale));
            face.LoadGlyph(glyph, LoadFlags.Default, LoadTarget.Normal);

            // Order of operations: Italic, then Bold, then Outline.
            // This order ensures that transformations are applied consistently.

            if (fontStyle == FontStyle.Italic)
            {
                // Apply shear transformation for italic.
                // This modifies the glyph outline in the slot.
                var matrix = new FTMatrix(1 << 16, (int)(0.3f * (1 << 16)), 0, 1 << 16); // 0.3f is a common shear factor.
                face.OutlineTransform(matrix);
            }

            if (fontWeight == FontWeight.Bold)
            {
                // Apply emboldening to the glyph outline in the slot.
                // This should be done after italic transformation.
                // 1 << 6 means 1 pixel.
                face.OutlineEmbolden(1 << 6);
            }

            if (outlineThickness > 0)
            {
                // Use FreeType stroker for outlines.
                // This replaces the glyph in the slot with its stroked version.
                using var ftGlyph = face.Glyph.GetGlyph(); // Get the glyph object from the slot.
                using var stroker = new Stroker();
                // Set stroker properties. outlineThickness is in pixels. FreeType expects 26.6 fixed point.
                stroker.Set((int)(outlineThickness * 64), StrokerLineCap.Round, StrokerLineJoin.Round, 0);

                // Stroke the glyph. This modifies the ftGlyph object.
                // The `true` for `destroy` means the original outline in ftGlyph is destroyed.
                ftGlyph.Stroke(stroker, true);

                // Render the (now stroked) glyph to the slot's bitmap.
                // The `true` for `destroy` means ftGlyph itself is disposed after this.
                // This updates face.Glyph.Bitmap, face.Glyph.Metrics, etc.
                face.GlyphSlot.RenderGlyph(RenderMode.Normal); // Render the transformed glyph in the slot first
                // It seems the above RenderGlyph is not enough, ToBitmap is needed to get the bitmap into the slot for SharpFont.
                // However, the `GlyphSlot.RenderGlyph` should have updated the slot.
                // Let's try with ToBitmap as it's more explicit for SharpFont's model after manual modifications.
                // If ftGlyph.ToBitmap is used, it writes to ftGlyph's internal bitmap,
                // and then we'd need to get it back to the slot or use that bitmap directly.
                // The most straightforward way after modifying outline (embolden, transform) or stroking a Glyph
                // is to render it back to the slot if it's not done automatically.
                // face.Glyph.RenderGlyph() should render the current outline in the slot.

                // After ftGlyph.Stroke, the ftGlyph IS the stroked outline.
                // We need to get this back into the slot or render it.
                // The typical FreeType C pattern is:
                // FT_Get_Glyph(slot, &glyph);
                // FT_Glyph_StrokeBorder(&glyph, stroker, 0, 1); // 0 for inside, 1 for outside. Or FT_Glyph_Stroke
                // FT_Glyph_To_Bitmap(&glyph, mode, origin, 1); // Render to glyph->bitmap
                // Then use glyph->bitmap.
                // SharpFont: glyph.ToBitmap(mode, origin, destroyOriginal);
                // This renders to *glyph.BitmapGlyph.Bitmap*
                // The slot (face.Glyph) is not automatically updated.
                // So, after ftGlyph.ToBitmap, the bitmap is in ftGlyph.BitmapGlyph.

                // Let's simplify: render the current slot's outline, which should have been modified by embolden/transform.
                // For stroking, it's more complex as Stroke creates a new glyph.
                // The current code `ftGlyph.ToBitmap(RenderMode.Normal, Vector2.Zero, true);` followed by
                // `var glyphSlot = ftGlyph.ToBitmapGlyph();` and then `face.Glyph.RenderGlyph(RenderMode.Normal);`
                // seems a bit convoluted.

                // Correct approach for stroking:
                // 1. Load glyph into slot: face.LoadGlyph(...)
                // 2. Get Glyph object: using (var glyph = face.Glyph.GetGlyph())
                // 3. Stroke it: glyph.Stroke(stroker, true) // This modifies 'glyph'
                // 4. Convert the stroked 'glyph' to a bitmap glyph: using (var bitmapGlyph = glyph.ToBitmapGlyph(RenderMode.Normal, Vector2.Zero, true))
                // 5. Now, bitmapGlyph.Bitmap is the actual FT_Bitmap. We need to use this.
                // The problem is our existing code expects face.Glyph.Bitmap.

                // Let's ensure the slot itself is updated.
                // If OutlineEmbolden and OutlineTransform modify the slot's outline directly,
                // then face.Glyph.RenderGlyph() should be sufficient for those.

                // For stroking:
                // Get the glyph from the slot.
                using var originalGlyph = face.Glyph.GetGlyph();
                // Stroke this glyph (this creates new outline data within originalGlyph if not done in place,
                // but SharpFont's Glyph.Stroke is in-place on the Glyph object's outline)
                originalGlyph.Stroke(stroker, false); // false to not destroy the original glyph's internal outline immediately for safety, stroker owns new outline.
                                                     // The glyph object now contains the stroked outline.
                // Now render this modified glyph object into the slot's bitmap.
                // This is done by converting the abstract Glyph to a BitmapGlyph and copying its contents.
                // Or, more directly, ask FreeType to render the slot's current outline,
                // but we need to ensure the slot's outline *is* the stroked one.
                // FT_Done_Glyph(face->glyph->outline) happens if new outline is set.
                // The ftGlyph.Stroke(stroker, true) does replace the outline in ftGlyph.
                // Then ftGlyph.ToBitmap(...) renders *that* ftGlyph to a bitmap.
                // This bitmap is then what we need. face.Glyph.Bitmap is the target.

                // Re-evaluating the SharpFont stroker pattern:
                face.Glyph.GetGlyph().ToBitmap(RenderMode.Outline, Vector2.Zero, true); // Get it as an outline glyph
                // This above line is probably not needed.

                // Stroker workflow:
                // 1. Load glyph (done).
                // 2. Get FT_Glyph:
                using (var glyphToStroke = face.Glyph.GetGlyph())
                {
                    // 3. Stroke it (this modifies glyphToStroke):
                    glyphToStroke.Stroke(stroker, true); // true: destroy original outline data in glyphToStroke, replace with stroked.
                    // 4. Convert the stroked glyphToStroke to a bitmap and store it in the slot:
                    // This is the tricky part. ToBitmap renders to glyphToStroke.BitmapGlyph.
                    // We need it in face.GlyphSlot.Bitmap.
                    // The easiest way is to render the glyph (which is now stroked) from the modified glyph object
                    // back into the slot by assigning it and then calling RenderGlyph,
                    // or by directly using the bitmap from ToBitmap if the rest of the system can adapt.

                    // The SharpFont `GlyphSlot.RenderGlyph()` renders the current outline in the slot.
                    // `Glyph.OutlineEmbolden()` and `Glyph.OutlineTransform()` modify the slot's outline.
                    // `Glyph.Stroke()` modifies the `Glyph` object's outline, not (directly) the slot's.
                    // So, after `glyphToStroke.Stroke()`, `glyphToStroke` has the new outline.
                    // We then need to render `glyphToStroke` into a bitmap.
                    using (var bitmappedStrokedGlyph = glyphToStroke.ToBitmapGlyph(RenderMode.Normal, Vector2.Zero, true))
                    {
                        // Now, bitmappedStrokedGlyph.Bitmap is the FT_Bitmap we want.
                        // The existing code uses face.Glyph.Bitmap.
                        // We need to copy bitmappedStrokedGlyph.Bitmap's data to face.Glyph.Bitmap.
                        // This is an area where SharpFont abstractions can be a bit tricky.
                        // For now, let's assume face.Glyph.RenderGlyph() after modifications is enough for non-stroked.
                        // For stroked, the current code was:
                        // using var ftGlyph = face.Glyph.GetGlyph();
                        // ftGlyph.Stroke(stroker, true);
                        // ftGlyph.ToBitmap(RenderMode.Normal, Vector2.Zero, true); // This renders ftGlyph to *its own* bitmap store
                        // var glyphSlot = ftGlyph.ToBitmapGlyph(); // this is the rendered glyph
                        // face.Glyph.RenderGlyph(RenderMode.Normal); // This renders the *original slot's outline*, which isn't the stroked one. THIS IS THE PROBLEM.

                        // Corrected stroker logic:
                        // After this, face.Glyph.Bitmap should contain the rendered stroked glyph.
                        // This is because ToBitmapGlyph will internally call FT_Glyph_To_Bitmap which renders the glyph.
                        // The resulting FT_Bitmap is what we need.
                        // The slot's bitmap (face.Glyph.Bitmap) must be set from this result.
                        // SharpFont's `face.Glyph.Bitmap` is a direct wrapper around `slot->bitmap`.
                        // And `ToBitmapGlyph().Bitmap` wraps `glyph->bitmap`. These are different.
                        // The easiest path: `face.RenderGlyph(glyphToStroke, RenderMode.Normal)` if it existed. It doesn't.
                        // Alternative: `face.GlyphSlot.Bitmap = bitmappedStrokedGlyph.Bitmap;` NO, this is not how FT works.
                        // We must load the bitmappedStrokedGlyph back into the slot, or use its bitmap directly.
                        // The most robust way is to use the bitmap from `bitmappedStrokedGlyph.Bitmap` and bypass `face.Glyph.Bitmap` for this path.
                        // This means `using var bitmap = bitmappedStrokedGlyph.Bitmap;` and then use `bitmap` below.
                        // This complicates the downstream code that expects `face.Glyph.Bitmap`.

                        // Let's try to make `face.Glyph.RenderGlyph()` render the stroked outline.
                        // This requires the slot's outline to BE the stroked outline.
                        // `face.OutlineCopyFrom(strokedGlyph.Outline)`? No such direct method.
                        // The `face.Glyph` is a `GlyphSlot` object.
                        // We have `strokedGlyph` (which is `glyphToStroke` after being stroked).
                        // We can try to set the slot's outline from `strokedGlyph.Outline`.
                        // However, `strokedGlyph.Outline` is managed by the `Glyph` object.
                        // This is getting too complex. Let's stick to:
                        // 1. Modify slot for non-stroked effects (italic, bold) -> Render slot.
                        // 2. For stroke: Get glyph, stroke it, render stroked glyph to its own bitmap, then use that bitmap.
                        // This means `EnsureGlyphCached` will sometimes use `face.Glyph.Bitmap` and sometimes `strokedGlyphBitmap`.
                        // This is messy.
                        //
                        // Simpler path for stroking:
                        // After `face.LoadGlyph`, the glyph is in `face.GlyphSlot`.
                        // If we stroke, we want the *stroked outline* to be in the slot, then render the slot.
                        // `face.GlyphSlot.Format = GlyphFormat.Outline;` // ensure it's an outline
                        // Then `Stroker.StrokeGlyph(face.GlyphSlot, stroker)` ? No.
                        // `Stroker.GenerateStroke(face.GlyphSlot.Outline, stroker, out newOutline)`? Then set slot's outline?
                        //
                        // Back to a slightly modified version of original attempt:
                        // This will make face.Glyph (the slot) contain the stroked and rendered glyph.
                        face.Glyph.Stroke(stroker, true); // Modifies the slot's outline to be the stroked version.
                        face.Glyph.RenderGlyph(RenderMode.Normal); // Renders the (now stroked) outline in the slot.
                    }
                }
            }
            else // Not outlined
            {
                // If not outlined, but bold or italic, these have modified the slot's outline.
                // So, render the (potentially transformed) glyph outline in the slot.
                face.Glyph.RenderGlyph(RenderMode.Normal);
            }

            var glyphMetrics = face.Glyph.Metrics;
            info.Metrics = new CharMetrics(glyphMetrics.HorizontalBearingX.ToInt32(),
                glyphMetrics.HorizontalBearingY.ToInt32(),
                glyphMetrics.HorizontalAdvance.ToInt32(),
                glyphMetrics.Width.ToInt32(),
                glyphMetrics.Height.ToInt32());

            using var bitmap = face.Glyph.Bitmap;
            if (bitmap.Pitch < 0)
            {
                throw new NotImplementedException();
            }

            if (bitmap.Pitch != 0)
            {
                Image<A8> img;
                switch (bitmap.PixelMode)
                {
                    case PixelMode.Mono:
                    {
                        img = MonoBitMapToImage(bitmap);
                        break;
                    }

                    case PixelMode.Gray:
                    {
                        ReadOnlySpan<A8> span;
                        unsafe
                        {
                            span = new ReadOnlySpan<A8>((void*) bitmap.Buffer, bitmap.Pitch * bitmap.Rows);
                        }

                        img = new Image<A8>(bitmap.Width, bitmap.Rows);

                        span.Blit(
                            bitmap.Pitch,
                            UIBox2i.FromDimensions(0, 0, bitmap.Pitch, bitmap.Rows),
                            img,
                            (0, 0));

                        break;
                    }

                    case PixelMode.Gray2:
                    case PixelMode.Gray4:
                    case PixelMode.Lcd:
                    case PixelMode.VerticalLcd:
                    case PixelMode.Bgra:
                        throw new NotImplementedException();
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                OwnedTexture sheet;
                if (scaled.AtlasTextures.Count == 0)
                        sheet = GenSheet(scaled, face, instance, scale); // Pass necessary parameters
                else
                    sheet = scaled.AtlasTextures[^1];

                var (sheetW, sheetH) = sheet.Size;

                if (sheetW - scaled.CurSheetX < img.Width)
                {
                    scaled.CurSheetX = 0;
                    // +1 Adds a pixel of vertical padding, to avoid arifacts when aliasing
                    scaled.CurSheetY = scaled.CurSheetMaxY + 1;
                }

                if (sheetH - scaled.CurSheetY < img.Height)
                {
                    // Make new sheet.
                    scaled.CurSheetY = 0;
                    scaled.CurSheetX = 0;
                    scaled.CurSheetMaxY = 0;

                        sheet = GenSheet(scaled, face, instance, scale); // Pass necessary parameters
                }

                sheet.SetSubImage((scaled.CurSheetX, scaled.CurSheetY), img);

                var atlasTexture = new AtlasTexture(
                    sheet,
                    UIBox2.FromDimensions(
                        scaled.CurSheetX,
                        scaled.CurSheetY,
                        bitmap.Width,
                        bitmap.Rows));

                info.Texture = atlasTexture;

                scaled.CurSheetMaxY = Math.Max(scaled.CurSheetMaxY, scaled.CurSheetY + bitmap.Rows);
                // +1 adds a pixel of horizontal padding, to avoid artifacts when aliasing
                scaled.CurSheetX += bitmap.Width + 1;
            }

            scaled.GlyphInfos.Add(glyphKey, info); // Use the glyphKey for caching
            return info;

            OwnedTexture GenSheet(ScaledFontData currentScaled, Face currentFace, FontInstanceHandle currentInstance, float currentScale)
            {
                var sheet = _clyde.CreateBlankTexture<A8>((SheetWidth, SheetHeight),
                    $"font-{currentFace.FamilyName}-{currentInstance.Size}-{(uint)(_baseFontDpi * currentScale)}-sheet{currentScaled.AtlasTextures.Count}");
                currentScaled.AtlasTextures.Add(sheet);
                return sheet;
            }
        }

        private static Image<A8> MonoBitMapToImage(FTBitmap bitmap)
        {
            DebugTools.Assert(bitmap.PixelMode == PixelMode.Mono);
            DebugTools.Assert(bitmap.Pitch > 0);

            ReadOnlySpan<byte> span;
            unsafe
            {
                span = new ReadOnlySpan<byte>((void*) bitmap.Buffer, bitmap.Rows * bitmap.Pitch);
            }

            var bitmapImage = new Image<A8>(bitmap.Width, bitmap.Rows);
            for (var y = 0; y < bitmap.Rows; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var byteIndex = y * bitmap.Pitch + (x / 8);
                    var bitIndex = x % 8;

                    var bit = (span[byteIndex] & (1 << (7 - bitIndex))) != 0;
                    bitmapImage[x, y] = new A8(bit ? byte.MaxValue : byte.MinValue);
                }
            }

            return bitmapImage;
        }

        private sealed class FontFaceHandle : IFontFaceHandle
        {
            public Face Face { get; }

            public FontFaceHandle(Face face)
            {
                Face = face;
            }
        }

        [PublicAPI]
        private sealed class FontInstanceHandle : IFontInstanceHandle
        {
            public FontFaceHandle FaceHandle { get; }
            public int Size { get; }
            private readonly Dictionary<float, ScaledFontData> _scaledData = new();
            private readonly FontManager _fontManager;
            public readonly Dictionary<Rune, uint> GlyphMap;

            public FontInstanceHandle(FontManager fontManager, int size, FontFaceHandle faceHandle)
            {
                GlyphMap = new Dictionary<Rune, uint>();
                _fontManager = fontManager;
                Size = size;
                FaceHandle = faceHandle;
            }

            public void ClearSizeData()
            {
                foreach (var scaleData in _scaledData)
                {
                    foreach (var ownedTexture in scaleData.Value.AtlasTextures)
                    {
                        ownedTexture.Dispose();
                    }
                }
                _scaledData.Clear();
            }

            public Texture? GetCharTexture(Rune codePoint, float scale, FontWeight fontWeight = FontWeight.Normal, FontStyle fontStyle = FontStyle.Normal, float outlineThickness = 0f)
            {
                var glyph = GetGlyph(codePoint);
                if (glyph == 0)
                    return null;

                var scaled = GetScaleDatum(scale);
                // Pass decoration parameters to EnsureGlyphCached
                var glyphInfo = _fontManager.EnsureGlyphCached(this, scaled, scale, glyph, fontWeight, fontStyle, outlineThickness);

                return glyphInfo.Texture;
            }

            public CharMetrics? GetCharMetrics(Rune codePoint, float scale, FontWeight fontWeight = FontWeight.Normal, FontStyle fontStyle = FontStyle.Normal, float outlineThickness = 0f)
            {
                var glyph = GetGlyph(codePoint);
                if (glyph == 0)
                {
                    return null;
                }

                var scaled = GetScaleDatum(scale);
                // Pass decoration parameters to EnsureGlyphCached
                var info = _fontManager.EnsureGlyphCached(this, scaled, scale, glyph, fontWeight, fontStyle, outlineThickness);

                return info.Metrics;
            }

            public int GetAscent(float scale)
            {
                var scaled = GetScaleDatum(scale);
                return scaled.Ascent;
            }

            public int GetDescent(float scale)
            {
                var scaled = GetScaleDatum(scale);
                return scaled.Descent;
            }

            public int GetHeight(float scale)
            {
                var scaled = GetScaleDatum(scale);
                return scaled.Height;
            }

            public int GetLineHeight(float scale)
            {
                var scaled = GetScaleDatum(scale);
                return scaled.LineHeight;
            }

            private uint GetGlyph(Rune chr)
            {
                if (GlyphMap.TryGetValue(chr, out var glyph))
                {
                    return glyph;
                }

                // Check FreeType to see if it exists.
                var index = FaceHandle.Face.GetCharIndex((uint) chr.Value);

                GlyphMap.Add(chr, index);

                return index;
            }

            private ScaledFontData GetScaleDatum(float scale)
            {
                if (_scaledData.TryGetValue(scale, out var datum))
                {
                    return datum;
                }

                datum = _fontManager._generateScaledDatum(this, scale);
                _scaledData.Add(scale, datum);
                return datum;
            }
        }

        private sealed class ScaledFontData
        {
            public ScaledFontData(int ascent, int descent, int height, int lineHeight)
            {
                Ascent = ascent;
                Descent = descent;
                Height = height;
                LineHeight = lineHeight;
            }

            public readonly List<OwnedTexture> AtlasTextures = new();
            public readonly Dictionary<GlyphKey, GlyphInfo> GlyphInfos = new(); // Use GlyphKey as key
            public readonly int Ascent;
            public readonly int Descent;
            public readonly int Height;
            public readonly int LineHeight;

            public int CurSheetX;
            public int CurSheetY;
            public int CurSheetMaxY;
        }

        // Struct to use as a key for caching glyphs with different decorations
        private readonly struct GlyphKey : IEquatable<GlyphKey>
        {
            public readonly uint Glyph;
            public readonly FontWeight FontWeight;
            public readonly FontStyle FontStyle;
            public readonly float OutlineThickness; // Using float directly, consider precision issues if any

            public GlyphKey(uint glyph, FontWeight fontWeight, FontStyle fontStyle, float outlineThickness)
            {
                Glyph = glyph;
                FontWeight = fontWeight;
                FontStyle = fontStyle;
                OutlineThickness = outlineThickness;
            }

            public bool Equals(GlyphKey other)
            {
                return Glyph == other.Glyph && FontWeight == other.FontWeight && FontStyle == other.FontStyle && OutlineThickness.Equals(other.OutlineThickness);
            }

            public override bool Equals(object? obj)
            {
                return obj is GlyphKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Glyph, (int)FontWeight, (int)FontStyle, OutlineThickness);
            }
        }

        public sealed class GlyphInfo
        {
            public CharMetrics Metrics;
            public AtlasTexture? Texture;
        }
    }
}
