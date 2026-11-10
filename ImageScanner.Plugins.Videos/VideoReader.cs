using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ImageScanner.Core.Library;

namespace ImageScanner.Plugins.Videos;

/// <summary>
/// Reads a video's frame size, running time and thumbnail through the Windows shell: the same data and picture
/// File Explorer shows, using whatever video codecs Windows has, without bundling a decoder.
/// </summary>
internal sealed class VideoReader(IReadOnlyCollection<string> extensions) : IMediaReader {
    // System.Video.FrameWidth / FrameHeight and System.Media.Duration (in 100 ns units)
    private static readonly Guid VideoProperties = new("64440491-4C8B-11D1-8B70-080036B11A03");
    private static readonly Guid MediaProperties = new("64440490-4C8B-11D1-8B70-080036B11A03");
    private static readonly PropertyKey FrameWidth = new(VideoProperties, 3);
    private static readonly PropertyKey FrameHeight = new(VideoProperties, 4);
    private static readonly PropertyKey Duration = new(MediaProperties, 3);

    private static readonly Guid ShellItem2Id = typeof(IShellItem2).GUID;

    public IReadOnlyCollection<string> Extensions { get; } = extensions;

    public bool IsVideo => true;

    public MediaInfo Read(string path, int thumbnailSize) {
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, in ShellItem2Id, out IShellItem2 item));
        try {
            int width = (item.GetUInt32(in FrameWidth, out uint frameWidth) == 0) ? (int)frameWidth : 0;
            int height = (item.GetUInt32(in FrameHeight, out uint frameHeight) == 0) ? (int)frameHeight : 0;
            TimeSpan? duration = ((item.GetUInt64(in Duration, out ulong ticks) == 0) && (ticks > 0)) ? TimeSpan.FromTicks((long)ticks) : null;
            return new MediaInfo(width, height, ReadThumbnail((IShellItemImageFactory)item, thumbnailSize), duration);
        }
        finally {
            Marshal.ReleaseComObject(item);
        }
    }

    private static byte[] ReadThumbnail(IShellItemImageFactory factory, int size) {
        NativeSize requested = new(size, size);
        // A frame from the video when Windows can make one; otherwise the file type's icon, so the video is still listed
        int result = factory.GetImage(requested, ImageFlags.ThumbnailOnly | ImageFlags.BiggerSizeOk, out IntPtr bitmap);
        if (result != 0) {
            Marshal.ThrowExceptionForHR(factory.GetImage(requested, ImageFlags.ResizeToFit, out bitmap));
        }

        try {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            double scale = Math.Min(1.0, (double)size / Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale < 1.0) {
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }

            // JPEG has no alpha channel
            JpegBitmapEncoder encoder = new() { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0)));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally {
            DeleteObject(bitmap);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItem2 item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId) {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height) {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [Flags]
    private enum ImageFlags {
        ResizeToFit = 0x0,
        BiggerSizeOk = 0x1,
        ThumbnailOnly = 0x8
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory {
        [PreserveSig]
        int GetImage(NativeSize size, ImageFlags flags, out IntPtr bitmap);
    }

    // Only the three getters are called; the others are declared to keep the methods in their COM order
    [ComImport]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2 {
        void BindToHandler();
        void GetParent();
        void GetDisplayName();
        void GetAttributes();
        void Compare();
        void GetPropertyStore();
        void GetPropertyStoreWithCreateObject();
        void GetPropertyStoreForKeys();
        void GetPropertyDescriptionList();
        void Update();
        void GetProperty();
        void GetCLSID();
        void GetFileTime();
        void GetInt32();
        void GetString();

        [PreserveSig]
        int GetUInt32(in PropertyKey key, out uint value);

        [PreserveSig]
        int GetUInt64(in PropertyKey key, out ulong value);

        void GetBool();
    }
}