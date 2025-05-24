using System.Numerics;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.UnitTesting.Client.Stubs; // For FontManagerStub if needed, or similar stubs

namespace Robust.UnitTesting.Client.UserInterface.Controls
{
    [TestFixture]
    [TestOf(typeof(Label))]
    public sealed class LabelTest : RobustUnitTest
    {
        public override UnitTestProject Project => UnitTestProject.Client;
        private IUserInterfaceManager _uiManager = default!;

        [SetUp]
        public void Setup()
        {
            IoCManager.Resolve<IUserInterfaceManagerInternal>().InitializeTesting();
            _uiManager = IoCManager.Resolve<IUserInterfaceManager>();

            // Register the FontManagerStub
            // Ensure it's registered for all relevant interfaces if FontManager implements multiple.
            // Based on FontManager.cs, it implements IFontManagerInternal, which extends IFontManager.
            IoCManager.Register<IFontManager, FontManagerStub>(overwrite: true);
            IoCManager.Register<IFontManagerInternal, FontManagerStub>(overwrite: true);

            // Initialize the FontManagerStub (calling SetFontDpi, which is part of IFontManagerInternal)
            // This mimics what the actual FontManager setup might do.
            var fontManagerInternal = IoCManager.Resolve<IFontManagerInternal>();
            fontManagerInternal.SetFontDpi(96); // Standard DPI, stub's SetFontDpi is no-op but call for completeness.
        }

        [TearDown]
        public void Teardown()
        {
            _uiManager.RootControl.Children.Clear();
            _uiManager.RootControl.Dispose();
            // Any other cleanup specific to UI testing
        }

        private static void MeasureLabel(Label label)
        {
            // To get DesiredSize, a measure pass is needed.
            // In tests, this often needs to be manually stimulated.
            label.InvalidateMeasure(); // Mark as needing remeasure
            // Available size for measure, can be large for unconstrained
            var availableSize = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            label.Measure(availableSize);
        }

        [Test]
        public void TestLabelDecorationImpactOnSize()
        {
            var label = new Label { Text = "Hello World" };
            _uiManager.RootControl.AddChild(label); // Add to tree for styles/theme to apply if necessary

            MeasureLabel(label);
            var initialSize = label.DesiredSize;
            Assert.That(initialSize.X, Is.GreaterThan(0), "Initial width should be > 0");
            Assert.That(initialSize.Y, Is.GreaterThan(0), "Initial height should be > 0");

            // Test Bold
            label.FontWeightOverride = FontWeight.Bold;
            MeasureLabel(label);
            var boldSize = label.DesiredSize;
            Assert.That(boldSize, Is.Not.EqualTo(initialSize), "Size should change with Bold.");
            // It's hard to assert X > initial.X because bold might only change weight, not necessarily advance width for all fonts/characters.
            // But it *should* be different due to metrics potentially changing or different glyphs being cached.

            // Reset and Test Italic
            label.FontWeightOverride = null;
            label.FontStyleOverride = FontStyle.Italic;
            MeasureLabel(label);
            var italicSize = label.DesiredSize;
            Assert.That(italicSize, Is.Not.EqualTo(initialSize), "Size should change with Italic.");

            // Reset and Test OutlineThickness
            label.FontStyleOverride = null;
            label.OutlineThicknessOverride = 1.0f;
            // OutlineColor doesn't affect size, only appearance
            MeasureLabel(label);
            var outlineSize = label.DesiredSize;
            Assert.That(outlineSize, Is.Not.EqualTo(initialSize), "Size should change with OutlineThickness.");
            // For outline, width and height might increase.
            Assert.That(outlineSize.X, Is.GreaterThanOrEqualTo(initialSize.X), "Outline width should be >= initial.");
            Assert.That(outlineSize.Y, Is.GreaterThanOrEqualTo(initialSize.Y), "Outline height should be >= initial.");


            // Test combined - e.g., Bold + Outline
            label.FontWeightOverride = FontWeight.Bold; // OutlineThickness is still 1.0f
            MeasureLabel(label);
            var boldOutlineSize = label.DesiredSize;
            Assert.That(boldOutlineSize, Is.Not.EqualTo(initialSize), "Size should change with Bold + Outline.");
            Assert.That(boldOutlineSize, Is.Not.EqualTo(boldSize), "Size for Bold+Outline should be different from just Bold.");
            Assert.That(boldOutlineSize, Is.Not.EqualTo(outlineSize), "Size for Bold+Outline should be different from just Outline.");
        }

        [Test]
        public void TestLabelShadowDoesNotImpactSize()
        {
            // Shadow is a visual effect drawn outside the glyph's original metrics/bounds
            // and should not affect the label's measured size.
            var label = new Label { Text = "Shadow Test" };
            _uiManager.RootControl.AddChild(label);

            MeasureLabel(label);
            var initialSize = label.DesiredSize;

            label.ShadowOffsetOverride = new Vector2(2, 2);
            label.ShadowColorOverride = Color.Black;
            MeasureLabel(label);
            var shadowSize = label.DesiredSize;

            Assert.That(shadowSize, Is.EqualTo(initialSize), "Shadow properties should not change the label's DesiredSize.");
        }
    }
}
