using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Island.App;
internal static class CapeTextureTests
{
    public static void Run(Window owner, Action<bool,string> check, Action<Window,string> capture)
    {
        var pixels = new byte[64 * 32 * 4];
        for (int y=0;y<32;y++) for(int x=0;x<64;x++)
        {
            var index=(y*64+x)*4; bool light=(x+y)%2==0;
            pixels[index]=light ? (byte)255 : (byte)0; pixels[index+2]=light ? (byte)180 : (byte)32; pixels[index+3]=255;
        }
        var source=BitmapSource.Create(64,32,96,96,PixelFormats.Bgra32,null,pixels,256);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream=new MemoryStream(); encoder.Save(stream);
        var doll=new SkinPreview3D { Width=280, Height=400, FollowPointer=false };
        doll.SetCape(stream.ToArray());
        var scene=(Model3DGroup)((ModelVisual3D)doll.Children.OfType<Viewport3D>().Single().Children.Last()).Content;
        var cape=(Model3DGroup)scene.Children.Last();
        var face=(GeometryModel3D)cape.Children[0]; var texture=(BitmapSource)((ImageBrush)((DiffuseMaterial)face.Material).Brush).ImageSource;
        check(texture.PixelWidth==160 && texture.PixelHeight==256,"Cape face uses enlarged texels for WPF 3D filtering");
        var expanded=new byte[texture.PixelWidth*texture.PixelHeight*4]; texture.CopyPixels(expanded,texture.PixelWidth*4,0);
        check(expanded[0]==expanded[15*4] && expanded[0]!=expanded[16*4],"Cape texels preserve exact colors with a sharp boundary");
        var uv=((MeshGeometry3D)face.Geometry).TextureCoordinates;
        check(uv.All(p=>p.X>0 && p.X<1 && p.Y>0 && p.Y<1),"Cape sampling stays inside each face texture");
        ((AxisAngleRotation3D)((RotateTransform3D)scene.Transform).Rotation).Angle=180;
        var dialog=new Window { Owner=owner, Content=doll, Width=320,Height=450,Background=Brushes.White,Left=-16000,Top=0,ShowInTaskbar=false };
        dialog.Show(); dialog.UpdateLayout(); capture(dialog,"Cape-Pixel-Edges"); dialog.Close();
        doll.SetSkin(null,"slim"); check(scene.Children.Contains(cape),"Changing skin retains the cape");
        doll.SetCape(null); check(!scene.Children.Contains(cape),"Unequipping removes the cape from the preview");
    }
}
