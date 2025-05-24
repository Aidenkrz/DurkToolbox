using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Robust.UnitTesting.Client.Graphics.Stubs
{
    public class FontManagerStub : IFontManagerInternal
    {
        public void ClearFontCache() { }

        public IFontFaceHandle Load(Stream stream)
        {
            return new FontFaceHandleStub();
        }

        public IFontInstanceHandle MakeInstance(IFontFaceHandle handle, int size)
        {
            return new FontInstanceHandleStub(size);
        }

        public void SetFontDpi(uint fontDpi) { }

        private class FontFaceHandleStub : IFontFaceHandle { }

        private class FontInstanceHandleStub : IFontInstanceHandle
        {
            private readonly int _size;

            public FontInstanceHandleStub(int size)
            {
                _size = size; // Not used for metrics yet, but good to have.
            }

            public Texture? GetCharTexture(Rune codePoint, float scale, FontWeight fontWeight = FontWeight.Normal, FontStyle fontStyle = FontStyle.Normal, float outlineThickness = 0f)
            {
                return null; // Not needed for metrics tests
            }

            public CharMetrics? GetCharMetrics(Rune codePoint, float scale, FontWeight fontWeight = FontWeight.Normal, FontStyle fontStyle = FontStyle.Normal, float outlineThickness = 0f)
            {
                // Base metrics - chosen to be simple
                int baseAdvance = 10;
                int baseWidth = 10;
                int baseHeight = 10;
                int baseBearingX = 0;
                int baseBearingY = 10; // Ascent is typically positive

                // Apply variations based on decorations
                if (fontWeight == FontWeight.Bold)
                {
                    baseAdvance += 2; // Bold makes characters wider
                    baseWidth += 1;   // And perhaps a bit fatter
                }

                if (fontStyle == FontStyle.Italic)
                {
                    // Italic might slightly change advance or width due to slant, or nothing if purely algorithmic shear
                    // For testing, let's make it have a distinct effect.
                    baseAdvance += 1;
                }

                if (outlineThickness > 0)
                {
                    // Outline adds to all dimensions
                    var outlineEffect = (int)(outlineThickness * 2); // Example: outline adds on both sides
                    baseAdvance += outlineEffect;
                    baseWidth += outlineEffect;
                    baseHeight += outlineEffect;
                    // Bearing might also shift if outline is asymmetric, but for simple stub, keep it basic.
                }

                // Ignore 'scale' for simplicity in this stub, assuming UIScale=1 for tests.
                // If scale was important, multiply dimensions by scale.
                return new CharMetrics(baseBearingX, baseBearingY, baseAdvance, baseWidth, baseHeight);
            }

            public int GetAscent(float scale) => 10; // Consistent with baseBearingY
            public int GetDescent(float scale) => 2; // Arbitrary small descent
            public int GetHeight(float scale) => 12; // Ascent + Descent
            public int GetLineHeight(float scale) => 14; // Slightly more than height for spacing
        }
    }
}
