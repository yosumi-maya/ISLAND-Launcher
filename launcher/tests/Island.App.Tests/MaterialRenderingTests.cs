using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App;
using Island.Core;

internal static class MaterialRenderingTests
{
    private static readonly SurfaceMaterial[] AddedMaterials = [SurfaceMaterial.Solid, SurfaceMaterial.Gaussian, SurfaceMaterial.LiquidGlass];
    public static void Run(string imagePath, Action<bool, string> check)
    {
        foreach (var hasImage in new[] { false, true })
        {
            var options = new LauncherAppearance(); options.Window.ImagePath = hasImage ? imagePath : "";
            var rendered = new Dictionary<SurfaceMaterial, byte[]>();
            foreach (var material in AddedMaterials)
            {
                options.Instances.Material = material;
                options.Instances.Color = "#243241";
                options.Instances.BlurRadius = 12;
                options.Instances.Opacity = 1;
                AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
                rendered[material] = Render();
                if (!AddedMaterials.Contains(material)) continue;
                var full = rendered[material];
                options.Instances.Opacity = .5; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
                var half = Render();
                options.Instances.Opacity = 0; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
                var clear = Render();
                // Compare the bevel and the interior, then verify the child remains opaque at zero background alpha.
                check(new[] { (40, 40), (80, 1), (2, 60), (120, 100) }.All(point =>
                    Math.Abs(Pixel(half, point.Item1, point.Item2, 3) - Pixel(full, point.Item1, point.Item2, 3) * .5) <= 2
                    && Pixel(clear, point.Item1, point.Item2, 3) == 0)
                    && Pixel(half, 80, 60, 3) == 255 && Pixel(clear, 80, 60, 3) == 255 && Pixel(clear, 80, 60, 0) == 0,
                    $"{material} scales the entire background and highlights independently of its foreground (image={hasImage})");
            }
            check(!rendered[SurfaceMaterial.LiquidGlass].SequenceEqual(rendered[SurfaceMaterial.Gaussian]),
                $"Liquid glass retains distinct edge optics beside Gaussian (image={hasImage})");
            if (hasImage)
            {
                options.Instances.Material = SurfaceMaterial.LiquidGlass; options.Instances.Opacity = 1;
                options.Instances.BlurRadius = 0; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor); var sharp = Render();
                options.Instances.BlurRadius = 32; AppearanceRenderer.Apply(options, AppearanceSettings.DefaultColor);
                check(!sharp.SequenceEqual(Render()), "Liquid glass blur changes the refracted image without changing foreground sharpness");
            }
        }
        var red = RenderNested("#D92554", false); var blue = RenderNested("#2456D9", false);
        check(Pixel(red, 80, 40, 2) > Pixel(blue, 80, 40, 2) + 40 && Pixel(blue, 80, 40, 0) > Pixel(red, 80, 40, 0) + 40,
            $"Glass picks up the actual ancestor surface colors without requiring a wallpaper (red={Pixel(red, 80, 40, 2)}/{Pixel(red, 80, 40, 0)}, blue={Pixel(blue, 80, 40, 2)}/{Pixel(blue, 80, 40, 0)})");
        check(RenderNested("#D92554", true).SequenceEqual(RenderNested("#2456D9", true)),
            "An opaque content surface blocks wallpaper / outer colors from leaking into nested glass");
    }
    private static byte[] RenderNested(string rootColor, bool cover)
    {
        var options = new LauncherAppearance(); options.Window.Color = rootColor; options.Content.Color = "#EEEEEE";
        options.Content.Opacity = cover ? 1 : 0; options.Instances.Material = SurfaceMaterial.LiquidGlass; options.Instances.Color = "#243241";
        var context = new AppearanceRenderContext(); context.Apply(options, AppearanceSettings.DefaultColor);
        var card = new MaterialBorder { Area = SurfaceArea.Instances, CornerRadius = new CornerRadius(12) };
        var content = new MaterialBorder { Area = SurfaceArea.Content, Child = card };
        var root = new MaterialBorder { Area = SurfaceArea.Window, RenderContext = context, IsPreviewRoot = true, Width = 160, Height = 120, Child = content };
        root.Measure(new Size(160, 120)); root.Arrange(new Rect(0, 0, 160, 120)); root.UpdateLayout();
        // OnRender may first run while the parent is still inside ArrangeOverride.
        // Repaint after layout settles, as Loaded / LayoutUpdated do in the real window.
        card.InvalidateVisual(); content.InvalidateVisual(); root.InvalidateVisual(); root.UpdateLayout();
        if (!ReferenceEquals(card.RenderContext, context)) throw new Exception("Nested glass did not inherit its render context");
        var bitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var pixels = new byte[160 * 120 * 4]; bitmap.CopyPixels(pixels, 160 * 4, 0); return pixels;
    }
    private static byte Pixel(byte[] pixels, int x, int y, int channel) => pixels[(y * 160 + x) * 4 + channel];
    private static byte[] Render()
    {
        var card = new MaterialBorder { Area = SurfaceArea.Instances, Width = 160, Height = 120, CornerRadius = new CornerRadius(12), Background = Brushes.White,
            Child = new Border { Width = 12, Height = 12, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        card.Measure(new Size(160, 120)); card.Arrange(new Rect(0, 0, 160, 120)); card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(card);
        var pixels = new byte[160 * 120 * 4]; bitmap.CopyPixels(pixels, 160 * 4, 0); return pixels;
    }
}
