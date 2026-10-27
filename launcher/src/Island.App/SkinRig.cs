using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Island.App;

public enum SkinPose { Standing, Walking, Running, Waving }

public sealed class SkinRig
{
    private sealed class Bone
    {
        public Model3DGroup Model { get; } = new();
        public Model3DGroup Outer { get; } = new();
        public AxisAngleRotation3D Pitch { get; } = new(new Vector3D(1, 0, 0), 0);
        public AxisAngleRotation3D Yaw { get; } = new(new Vector3D(0, 1, 0), 0);
        public AxisAngleRotation3D Roll { get; } = new(new Vector3D(0, 0, 1), 0);
        public Bone(Point3D pivot)
        {
            var rotations = new Transform3DGroup();
            rotations.Children.Add(new RotateTransform3D(Pitch, pivot)); rotations.Children.Add(new RotateTransform3D(Roll, pivot)); rotations.Children.Add(new RotateTransform3D(Yaw, pivot));
            Model.Transform = rotations;
            Outer.Transform = rotations;
        }
    }
    private readonly List<Bone> bones = [];
    private readonly Bone head, rightArm, leftArm, rightLeg, leftLeg;
    private readonly TranslateTransform3D bounce = new();
    private readonly Model3DGroup opaqueParts = new(), overlays = new();
    private readonly BitmapSource atlas;
    private readonly byte[] pixels;
    private readonly int textureHeight;
    private bool outerVisible = true;
    public Model3DGroup Model { get; } = new();
    public int ArmWidth { get; }
    public bool IsLegacy => textureHeight == 32;
    public int OuterFaceCount => bones.Sum(bone => bone.Outer.Children.Count);
    public double HeadYaw => head.Yaw.Angle;
    public double HeadPitch => head.Pitch.Angle;
    public double RightArmPitch => rightArm.Pitch.Angle;
    public bool OuterVisible
    {
        get => outerVisible;
        set
        {
            if (outerVisible == value) return;
            outerVisible = value;
            if (value) Model.Children.Add(overlays); else Model.Children.Remove(overlays);
        }
    }
    public SkinRig(BitmapSource texture, string variant)
    {
        textureHeight = texture.PixelHeight; ArmWidth = variant == "slim" && !IsLegacy ? 3 : 4;
        var rgba = new FormatConvertedBitmap(texture, PixelFormats.Bgra32, null, 0);
        pixels = new byte[64 * textureHeight * 4]; rgba.CopyPixels(pixels, 256, 0);
        // Old skins used the unused half of the atlas as an opaque background.
        if (IsLegacy && !HasTransparency(32, 0, 32, 32))
            for (int y = 0; y < 32; y++) for (int x = 32; x < 64; x++) pixels[(y * 64 + x) * 4 + 3] = 0;
        Opaque(new Int32Rect(0, 0, 32, 16)); Opaque(new Int32Rect(0, 16, 64, 16));
        if (!IsLegacy) Opaque(new Int32Rect(16, 48, 32, 16));
        atlas = Enlarge();
        Model.Transform = bounce;
        // Transparent texels still participate in WPF's depth buffer. Finish every solid
        // body part before drawing any clothing / hat layer, including the adjacent leg.
        Model.Children.Add(opaqueParts); Model.Children.Add(overlays);
        rightLeg = Part(new Point3D(-2, 12, 0), -4, 0, -2, 4, 12, 4, 0, 16, 0, 32);
        leftLeg = IsLegacy ? Part(new Point3D(2, 12, 0), 0, 0, -2, 4, 12, 4, 0, 16, null, null, true)
            : Part(new Point3D(2, 12, 0), 0, 0, -2, 4, 12, 4, 16, 48, 0, 48);
        Part(new Point3D(), -4, 12, -2, 8, 12, 4, 16, 16, 16, 32);
        rightArm = Part(new Point3D(-5, 22, 0), -4 - ArmWidth, 12, -2, ArmWidth, 12, 4, 40, 16, 40, 32);
        leftArm = IsLegacy ? Part(new Point3D(5, 22, 0), 4, 12, -2, ArmWidth, 12, 4, 40, 16, null, null, true)
            : Part(new Point3D(5, 22, 0), 4, 12, -2, ArmWidth, 12, 4, 32, 48, 48, 48);
        head = Part(new Point3D(0, 24, 0), -4, 24, -4, 8, 8, 8, 0, 0, 32, 0);
        SetPose(SkinPose.Standing, 0, 0, 0);
    }
    public void SetPose(SkinPose pose, double seconds, double headYaw, double headPitch)
    {
        head.Yaw.Angle = Math.Clamp(headYaw, -50, 50); head.Pitch.Angle = Math.Clamp(headPitch, -28, 28);
        var swing = Math.Sin(seconds * (pose == SkinPose.Running ? 10 : 6));
        double rightPitch = 0, leftPitch = 0, rightRoll = 0, leftRoll = 0, rightStride = 0, leftStride = 0, height = 0;
        switch (pose)
        {
            case SkinPose.Walking:
            case SkinPose.Running:
                var amplitude = pose == SkinPose.Running ? 58 : 30;
                rightPitch = swing * amplitude; leftPitch = -rightPitch;
                rightStride = -rightPitch; leftStride = rightPitch;
                height = Math.Abs(Math.Cos(seconds * (pose == SkinPose.Running ? 10 : 6))) * (pose == SkinPose.Running ? 1.1 : .3);
                break;
            case SkinPose.Waving:
                rightRoll = -155 + Math.Sin(seconds * 7) * 18; rightPitch = -12; leftRoll = -3;
                break;
            default:
                rightRoll = 3 + Math.Sin(seconds * 1.8) * 1.2; leftRoll = -rightRoll;
                rightPitch = Math.Sin(seconds * 1.4) * 1.5; leftPitch = -rightPitch;
                break;
        }
        rightArm.Pitch.Angle = rightPitch; leftArm.Pitch.Angle = leftPitch;
        rightArm.Roll.Angle = rightRoll; leftArm.Roll.Angle = leftRoll;
        rightLeg.Pitch.Angle = rightStride; leftLeg.Pitch.Angle = leftStride; bounce.OffsetY = height;
    }
    private Bone Part(Point3D pivot, double x, double y, double z, int width, int height, int depth, int u, int v, int? outerU, int? outerV, bool mirror = false)
    {
        var bone = new Bone(pivot); bones.Add(bone); opaqueParts.Children.Add(bone.Model); overlays.Children.Add(bone.Outer);
        Box(bone.Model, x, y, z, width, height, depth, u, v, 0, false, mirror);
        if (outerU.HasValue && outerV.HasValue && (!IsLegacy || height == 8))
            Box(bone.Outer, x, y, z, width, height, depth, outerU.Value, outerV.Value, height == 8 ? .5 : .25, true, mirror);
        return bone;
    }
    private void Box(Model3DGroup parent, double x, double y, double z, int w, int h, int d, int u, int v, double inflate, bool outer, bool mirror)
    {
        var x0 = x - inflate; var x1 = x + w + inflate; var y0 = y - inflate; var y1 = y + h + inflate; var z0 = z - inflate; var z1 = z + d + inflate;
        var right = new Int32Rect(u, v + d, d, h); var left = new Int32Rect(u + d + w, v + d, d, h);
        Face(new Int32Rect(u + d, v + d, w, h), new(x0, y1, z1), new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1));
        Face(new Int32Rect(u + d * 2 + w, v + d, w, h), new(x1, y1, z0), new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0));
        Face(mirror ? left : right, new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1));
        Face(mirror ? right : left, new(x1, y1, z1), new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0));
        Face(new Int32Rect(u + d, v, w, d), new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0));
        Face(new Int32Rect(u + d + w, v, w, d), new(x0, y0, z1), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1));
        void Face(Int32Rect uv, Point3D a, Point3D b, Point3D c, Point3D e)
        {
            if (outer && !HasVisiblePixel(uv)) return;
            // Isolate the face before filtering. Sampling the shared atlas can pull transparent
            // pixels from its neighbours into a face's edges, especially at small sidebar sizes.
            var faceTexture = new CroppedBitmap(atlas, new Int32Rect(uv.X * 16, uv.Y * 16, uv.Width * 16, uv.Height * 16)); faceTexture.Freeze();
            var insetX = .5 / faceTexture.PixelWidth; var insetY = .5 / faceTexture.PixelHeight;
            var leftU = mirror ? 1 - insetX : insetX; var rightU = mirror ? insetX : 1 - insetX;
            var mesh = new MeshGeometry3D { Positions = new Point3DCollection { a, b, c, e }, TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
                TextureCoordinates = new PointCollection { new(leftU, insetY), new(leftU, 1 - insetY), new(rightU, 1 - insetY), new(rightU, insetY) } };
            mesh.Freeze();
            var brush = new ImageBrush(faceTexture) { Stretch = Stretch.Fill, TileMode = TileMode.None };
            brush.Freeze(); var material = new DiffuseMaterial(brush); material.Freeze();
            var face = new GeometryModel3D(mesh, material); face.Freeze(); parent.Children.Add(face);
        }
    }
    private bool HasVisiblePixel(Int32Rect rect)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++) for (int x = rect.X; x < rect.X + rect.Width; x++)
            if (pixels[(y * 64 + x) * 4 + 3] > 0) return true;
        return false;
    }
    private bool HasTransparency(int left, int top, int width, int height)
    {
        for (int y = top; y < top + height; y++) for (int x = left; x < left + width; x++) if (pixels[(y * 64 + x) * 4 + 3] < 128) return true;
        return false;
    }
    private void Opaque(Int32Rect rect)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++) for (int x = rect.X; x < rect.X + rect.Width; x++) pixels[(y * 64 + x) * 4 + 3] = 255;
    }
    private BitmapSource Enlarge()
    {
        // Replicate pixels before WPF's 3D texture sampling to retain sharp skin texels.
        const int scale = 16; var stride = 64 * scale * 4; var expanded = new byte[stride * textureHeight * scale];
        for (int y = 0; y < textureHeight * scale; y++) for (int x = 0; x < 64 * scale; x++)
        {
            var source = ((y / scale) * 64 + x / scale) * 4; var target = y * stride + x * 4;
            expanded[target] = pixels[source]; expanded[target + 1] = pixels[source + 1]; expanded[target + 2] = pixels[source + 2]; expanded[target + 3] = pixels[source + 3];
        }
        var image = BitmapSource.Create(64 * scale, textureHeight * scale, 96, 96, PixelFormats.Bgra32, null, expanded, stride); image.Freeze(); return image;
    }
}
