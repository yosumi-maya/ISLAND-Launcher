using System.Text.Json;
using Island.Core;

internal static class AppearanceTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var legacy = JsonSerializer.Deserialize<LauncherState>("{}")!;
        check(!legacy.Appearance.HideSidebarDoll && legacy.Appearance.WindowOpacity == 1 && Enum.GetValues<SurfaceArea>().All(area => legacy.Appearance.For(area).Opacity == 1),
            "Older settings keep the paper doll visible and retain opaque default backgrounds");
        var oldMaterials = JsonSerializer.Deserialize<LauncherAppearance>("""{"Window":{"Material":0},"Hero":{"Material":1},"Instances":{"Material":2},"Panels":{"Material":3}}""")!.Normalize();
        check(oldMaterials.Window.Material == SurfaceMaterial.Solid && oldMaterials.Hero.Material == SurfaceMaterial.Gaussian
            && oldMaterials.Instances.Material == SurfaceMaterial.Gaussian && oldMaterials.Panels.Material == SurfaceMaterial.Gaussian && oldMaterials.Panels.BlurRadius == 64,
            "Removed acrylic / mica values migrate to Gaussian without renumbering supported materials");
        check(legacy.Appearance.LaunchBarMode == LaunchBarMode.Always && legacy.Appearance.CardAnimation == CardAnimationMode.FloatTilt,
            "Old settings default to a permanent launch bar and enabled card hover motion");
        var oldOff = JsonSerializer.Deserialize<LauncherAppearance>("""{"CardHoverEffects":false}""")!.Normalize();
        var oldOn = JsonSerializer.Deserialize<LauncherAppearance>("""{"CardHoverEffects":true}""")!.Normalize();
        check(oldOff.CardAnimation == CardAnimationMode.Off && oldOn.CardAnimation == CardAnimationMode.FloatTilt, "Old card motion switches migrate to Off / the original floating-follow animation");
        foreach (var json in new[] { """{"CardHoverEffects":false,"CardAnimation":5}""", """{"CardAnimation":5,"CardHoverEffects":false}""" })
            check(JsonSerializer.Deserialize<LauncherAppearance>(json)!.Normalize().CardAnimation == CardAnimationMode.Magnetic, "An explicit animation choice takes priority over the legacy switch in either JSON property order");
        check(JsonSerializer.Deserialize<LauncherAppearance>("""{"CardAnimation":999}""")!.Normalize().CardAnimation == CardAnimationMode.FloatTilt, "Unknown card animation values recover to the existing effect");
        check(oldMaterials.Content.Material == SurfaceMaterial.Solid && oldMaterials.Content.Opacity == 1
            && (int)SurfaceArea.RecentMap == 6 && (int)SurfaceArea.Content == 7,
            "Older appearance settings gain a default content module without renumbering existing regions");
        var options = new LauncherAppearance { WindowOpacity = .64, HideSidebarDoll = true, LaunchBarMode = LaunchBarMode.OnApproach, CardAnimation = CardAnimationMode.Off };
        options.Window.ImagePath = @"F:\Images\背景.jpg"; options.Hero.ImagePath = @"F:\Images\另一张.png";
        options.RecentMap.ImagePath = @"F:\Images\地图卡片.png"; options.RecentMap.ForegroundColor = "#ffeedd";
        foreach (var area in Enum.GetValues<SurfaceArea>())
        {
            var surface = options.For(area); surface.Material = new[] { SurfaceMaterial.Solid, SurfaceMaterial.Gaussian, SurfaceMaterial.LiquidGlass }[(int)area % 3];
            surface.Opacity = .3 + (int)area * .07; surface.BlurRadius = 8 + (int)area; surface.Color = "#123abc";
        }
        var store = new LauncherStore(Path.Combine(root, "appearance-settings")); var state = store.Load();
        state.Appearance = options; store.Save(state);
        var restored = store.Load().Appearance;
        check(restored.RecentMap.ForegroundColor == "#FFEEDD" && restored.RecentMap.ImagePath == options.RecentMap.ImagePath
            && restored.Hero.ForegroundColor == "", "Map-card text color and image persist independently of the recent-play hero");
        check(restored.LaunchBarMode == LaunchBarMode.OnApproach && restored.CardAnimation == CardAnimationMode.Off, "Launch bar behavior and card motion preferences persist independently");
        foreach (var mode in Enum.GetValues<CardAnimationMode>())
        {
            state.Appearance.CardAnimation = mode; store.Save(state);
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(store.DirectoryPath, "settings.json")));
            check(store.Load().Appearance.CardAnimation == mode && saved.RootElement.GetProperty("Appearance").GetProperty("CardHoverEffects").GetBoolean() == (mode != CardAnimationMode.Off),
                "Animation choice survives a settings roundtrip and keeps the older switch consistent: " + mode);
        }
        check(restored.HideSidebarDoll && restored.WindowOpacity == .64 && restored.Window.ImagePath != restored.Hero.ImagePath && restored.Hero.ImagePath == options.Hero.ImagePath,
            "Paper doll visibility, window opacity and independent background image paths persist");
        check(Enum.GetValues<SurfaceArea>().All(area => restored.For(area).Material == options.For(area).Material && restored.For(area).Opacity == options.For(area).Opacity
            && restored.For(area).Color == "#123ABC"), "Every surface preserves its material, color and background opacity");
        foreach (var material in new[] { SurfaceMaterial.Solid, SurfaceMaterial.Gaussian, SurfaceMaterial.LiquidGlass })
        {
            foreach (var area in Enum.GetValues<SurfaceArea>()) state.Appearance.Set(area, new SurfaceAppearance { Material = material, Opacity = (int)area / 7d });
            store.Save(state); var materialCopy = store.Load().Appearance;
            check(Enum.GetValues<SurfaceArea>().All(area => materialCopy.For(area).Material == material && materialCopy.For(area).Opacity == (int)area / 7d),
                "New material and independent opacity survive reload in every region: " + material);
        }
        var invalid = new LauncherAppearance { WindowOpacity = 0 };
        invalid.Instances.Color = "#bad"; invalid.Instances.Material = (SurfaceMaterial)999; invalid.Instances.Opacity = 20; invalid.RecentMap.ForegroundColor = "#bad";
        invalid.Hero.BlurRadius = -2; invalid.Hero.ImageTint = 5;
        var normalized = invalid.Normalize();
        check(normalized.RecentMap.ForegroundColor == "" && legacy.Appearance.RecentMap.ForegroundColor == "", "Invalid and legacy map foreground values use automatic theme colors");
        check(normalized.WindowOpacity == .4 && normalized.Instances.Opacity == 1 && normalized.Hero.BlurRadius == 0 && normalized.Hero.ImageTint == 1,
            "Clamp background controls and keep an invisible window from being saved");
        check(normalized.Instances.Material == SurfaceMaterial.Solid && normalized.Instances.Color == "", "Invalid materials and colors fall back without losing other settings");
        state.Appearance.Hero.DefaultIllustration = false; store.Save(state);
        check(!store.Load().Appearance.Hero.DefaultIllustration, "A plain recent-play background stays plain after settings reload");
        state.Appearance.HideSidebarDoll = false; store.Save(state);
        check(!store.Load().Appearance.HideSidebarDoll, "The paper doll can be restored after a hidden preference was saved");
        normalized.Hero.Color = "#FFFFFF";
        check(invalid.Hero.Color == "", "Appearance normalization creates independent surface copies");
        var nullable = JsonSerializer.Deserialize<LauncherAppearance>("""{"Window":null,"Hero":null}""")!.Normalize();
        check(nullable.Window.Material == SurfaceMaterial.Solid && nullable.Hero.Opacity == 1, "Missing appearance sections recover independently");
    }
}
