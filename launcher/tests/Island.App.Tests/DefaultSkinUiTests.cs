using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Island.App;
internal static class DefaultSkinUiTests
{
    public static void Run(MainWindow window,string root,Action<bool,string> check,Action<Window,string> capture)
    {
        using var stream=typeof(SkinImages).Assembly.GetManifestResourceStream("Island.App.Steve.png")!;
        check(Convert.ToHexString(SHA256.HashData(stream)).Equals("4C7B0468044BFECACC43D00A3A69335A834B73937688292C20D3988CAE58248D"),"Default skin embeds the unchanged official Steve template");
        var texture=SkinImages.TextureOrDefault(null);
        check(texture.PixelWidth==64 && texture.PixelHeight==64 && texture.IsFrozen,"Default Steve texture is a cached immutable skin atlas");
        check(ReferenceEquals(texture,SkinImages.TextureOrDefault(null)) && ReferenceEquals(texture,SkinImages.TextureOrDefault(new byte[]{1,2,3})),"Missing or malformed skins use the same offline Steve fallback");
        var avatar=SkinImages.Avatar(null);
        check(avatar is BitmapSource {PixelWidth:8,PixelHeight:8},"Default account avatar comes from Steve's face");
        var preview=(SkinPreview3D)window.FindName("SidebarDoll");preview.SetSkin(null,"slim");
        check(preview.Rig.ArmWidth==4 && !preview.Rig.IsLegacy,"Steve fallback always uses the classic wide-arm model");
        var custom=File.ReadAllBytes(SkinUiTests.MakeSkin(root));preview.SetSkin(custom,"slim");
        check(preview.Rig.ArmWidth==3,"Imported slim skins retain their selected model");
        preview.SetSkin(null,"classic");window.Width=1020;window.Height=700;window.UpdateLayout();capture(window,"Default-Steve-Sidebar");
    }
}
