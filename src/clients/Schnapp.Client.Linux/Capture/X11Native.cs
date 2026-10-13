namespace Schnapp.Client.Linux.Capture;

using System;
using System.Runtime.InteropServices;

using Schnapp.Client.Linux.Services;

/// <summary>
/// Shared in-process X11 / Xss P/Invoke surface and a process-wide display connection.
/// Xlib is not thread-safe — take <see cref="Sync"/> before any native call on <see cref="Display"/>.
/// </summary>
internal static partial class X11Native
{
    public const int ZPixmap = 2;
    public const ulong AllPlanes = 0xFFFFFFFFFFFFFFFF;
    public static readonly IntPtr XA_WINDOW = new(33);

    /// <summary>XScreenSaverInfo.state values (scrnsaver.h).</summary>
    public const int ScreenSaverOff = 0;
    public const int ScreenSaverOn = 1;
    public const int ScreenSaverDisabled = 3;

    private static readonly object Gate = new();
    private static IntPtr _display;
    private static bool _openAttempted;
    private static bool _xssChecked;
    private static bool _xssAvailable;

    /// <summary>Lock held around all X11/Xss calls that use <see cref="Display"/>.</summary>
    public static object Sync => Gate;

    /// <summary>Shared display, or zero if unavailable. Opened lazily under <see cref="Sync"/>.</summary>
    public static IntPtr Display
    {
        get
        {
            lock (Gate)
            {
                EnsureOpenUnlocked();
                return _display;
            }
        }
    }

    /// <summary>True when <see cref="Display"/> is open.</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
            {
                EnsureOpenUnlocked();
                return _display != IntPtr.Zero;
            }
        }
    }

    public static IntPtr DefaultRootWindow(IntPtr display)
    {
        var root = XDefaultRootWindow(display);
        if (root != IntPtr.Zero)
        {

            return root;
        }


        return XRootWindow(display, XDefaultScreen(display));
    }

    /// <summary>
    /// Ensures the X Screen Saver extension has been probed. Caller must hold <see cref="Sync"/>.
    /// </summary>
    private static bool EnsureXssAvailableUnlocked(IntPtr display)
    {
        if (display == IntPtr.Zero)
        {
            return false;
        }

        if (!_xssChecked)
        {
            try
            {
                _xssAvailable = XScreenSaverQueryExtension(display, out var ev, out var err);
                AppLog.Debug($"X11Native: Xss ext query -> available={_xssAvailable} (ev={ev}, err={err})");
            }
            catch (DllNotFoundException)
            {
                AppLog.Debug("X11Native: libXss not found");
                _xssAvailable = false;
            }
            _xssChecked = true;
        }

        return _xssAvailable;
    }

    /// <summary>
    /// Queries <c>XScreenSaverQueryInfo</c>. Caller must hold <see cref="Sync"/>.
    /// </summary>
    /// <returns><c>true</c> when info was filled; <c>false</c> on failure / no Xss.</returns>
    private static bool TryQueryScreenSaverInfoUnlocked(IntPtr display, out XScreenSaverInfo info)
    {
        info = default;
        if (!EnsureXssAvailableUnlocked(display))
        {
            return false;
        }

        try
        {
            var root = DefaultRootWindow(display);
            info = new XScreenSaverInfo();
            if (XScreenSaverQueryInfo(display, root, ref info) == 0)
            {
                AppLog.Debug("X11Native: XScreenSaverQueryInfo returned 0 (fail)");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Debug("X11Native: XScreenSaverQueryInfo threw: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// True when the X Screen Saver extension reports <see cref="ScreenSaverOn"/>.
    /// Caller must hold <see cref="Sync"/> and pass a valid display.
    /// </summary>
    public static bool IsScreensaverActiveUnlocked(IntPtr display)
    {
        if (!TryQueryScreenSaverInfoUnlocked(display, out var info))
        {
            return false;
        }

        var active = info.state == ScreenSaverOn;
        AppLog.Debug($"X11Native: screensaver state={info.state} (On={ScreenSaverOn}) -> active={active}");
        return active;
    }

    /// <summary>
    /// Idle time since last keyboard/mouse input (Xss <c>idle</c> field, milliseconds).
    /// Caller must hold <see cref="Sync"/> and pass a valid display.
    /// </summary>
    /// <returns><c>true</c> when idle was read; <c>false</c> if Xss unavailable or query failed.</returns>
    public static bool TryGetIdleTimeUnlocked(IntPtr display, out TimeSpan idle)
    {
        idle = TimeSpan.Zero;
        if (!TryQueryScreenSaverInfoUnlocked(display, out var info))
        {
            return false;
        }

        // Because idle is milliseconds since last input (XScreenSaverInfo.idle).
        var ms = info.idle.ToUInt64();
        idle = TimeSpan.FromMilliseconds(ms);
        AppLog.Debug($"X11Native: idle ms={ms}");
        return true;
    }

    private static void EnsureOpenUnlocked()
    {
        if (_openAttempted)
        {
            return;
        }


        _openAttempted = true;
        try
        {
            _display = XOpenDisplay(null);
            AppLog.Debug($"X11Native: XOpenDisplay -> 0x{_display:x} (success={_display != IntPtr.Zero})");
        }
        catch (Exception ex)
        {
            AppLog.Debug("X11Native: XOpenDisplay threw: " + ex.Message);
            _display = IntPtr.Zero;
        }
    }

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y;
        public int width, height;
        public int border_width;
        public int depth;
        public IntPtr visual;
        public IntPtr root;
        public int c_class;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed;
        public int map_state;
        public long all_event_masks;
        public long your_event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
        public IntPtr obdata;
        public IntPtr f_create_image;
        public IntPtr f_destroy_image;
        public IntPtr f_get_pixel;
        public IntPtr f_put_pixel;
        public IntPtr f_sub_image;
        public IntPtr f_add_pixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClassHint
    {
        public IntPtr res_name;
        public IntPtr res_class;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XScreenSaverInfo
    {
        public IntPtr window;
        public int state;
        public int kind;
        public UIntPtr til_or_since;
        public UIntPtr idle;
        public UIntPtr eventMask;
    }

    /// <summary>X11/extensions/Xinerama.h</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XineramaScreenInfo
    {
        public int screen_number;
        public short x_org;
        public short y_org;
        public short width;
        public short height;
    }

    /// <summary>X11/extensions/Xrandr.h XRRModeInfo (partial; trailing fields unused).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XRRModeInfo
    {
        public ulong id;          // RRMode
        public uint width;
        public uint height;
        public ulong dotClock;
        public uint hSyncStart;
        public uint hSyncEnd;
        public uint hTotal;
        public uint hSkew;
        public uint vSyncStart;
        public uint vSyncEnd;
        public uint vTotal;
        public IntPtr name;
        public uint nameLength;
        public ulong modeFlags;
    }

    /// <summary>XRRScreenResources header; arrays are IntPtr to RR ids / XRRModeInfo[].</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XRRScreenResources
    {
        public ulong timestamp;
        public ulong configTimestamp;
        public int ncrtc;
        public IntPtr crtcs;
        public int noutput;
        public IntPtr outputs;
        public int nmode;
        public IntPtr modes;
    }

    public const int RR_Rotate_0 = 1;
    public const int RR_Rotate_90 = 2;
    public const int RR_Rotate_180 = 4;
    public const int RR_Rotate_270 = 8;

    /// <summary>One connected RandR output with CRTC vs preferred-mode geometry (for scale detection).</summary>
    public sealed class XrandrOutputInfo
    {
        public string Name { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        /// <summary>CRTC pixel size (what Xinerama / XGetImage see).</summary>
        public int CurrentWidth { get; init; }
        public int CurrentHeight { get; init; }
        /// <summary>Active mode size from XRRModeInfo.</summary>
        public int ModeWidth { get; init; }
        public int ModeHeight { get; init; }
        /// <summary>Preferred mode size before orientation correction.</summary>
        public int PreferredWidth { get; init; }
        public int PreferredHeight { get; init; }
        /// <summary>Physical panel size after applying CRTC rotation (90/270 swap).</summary>
        public int PhysicalWidth { get; init; }
        public int PhysicalHeight { get; init; }
        /// <summary>XRRCrtcInfo.rotation flags.</summary>
        public int Rotation { get; init; }
        /// <summary>
        /// Auto multiply ≤1: physical/CRTC when CRTC is larger (fat FB); else 1 (no upscale).
        /// </summary>
        public double X11Scale { get; init; } = 1.0;
        /// <summary>EDID / panel description (e.g. AOC 31").</summary>
        public string Description { get; init; } = "";
        public int MmWidth { get; init; }
        public int MmHeight { get; init; }
    }

    #endregion

    #region libX11

    [LibraryImport("X11", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XOpenDisplay(string? display);

    [LibraryImport("X11")]
    public static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("X11")]
    public static partial IntPtr XDefaultRootWindow(IntPtr display);

    [LibraryImport("X11")]
    public static partial int XDefaultScreen(IntPtr display);

    [LibraryImport("X11")]
    public static partial IntPtr XRootWindow(IntPtr display, int screen_number);

    [LibraryImport("X11", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XInternAtom(
        IntPtr display,
        string atom_name,
        [MarshalAs(UnmanagedType.Bool)] bool only_if_exists);

    [LibraryImport("X11")]
    public static partial int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        long long_offset,
        long long_length,
        [MarshalAs(UnmanagedType.Bool)] bool delete,
        IntPtr req_type,
        out IntPtr actual_type_return,
        out int actual_format_return,
        out ulong nitems_return,
        out ulong bytes_after_return,
        ref IntPtr prop_return);

    [LibraryImport("X11")]
    public static partial int XFree(IntPtr data);

    [LibraryImport("X11")]
    public static partial int XFetchName(IntPtr display, IntPtr window, ref IntPtr window_name_return);

    [LibraryImport("X11")]
    public static partial int XGetClassHint(IntPtr display, IntPtr window, ref XClassHint class_hints_return);

    [LibraryImport("X11")]
    public static partial int XGetWindowAttributes(IntPtr display, IntPtr window, ref XWindowAttributes attributes);

    [LibraryImport("X11")]
    public static partial IntPtr XGetImage(
        IntPtr display,
        IntPtr drawable,
        int x,
        int y,
        uint width,
        uint height,
        ulong plane_mask,
        int format);

    [LibraryImport("X11")]
    public static partial int XDestroyImage(IntPtr ximage);

    [LibraryImport("X11")]
    public static partial int XQueryPointer(
        IntPtr display,
        IntPtr w,
        out IntPtr rootReturn,
        out IntPtr childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int winXReturn,
        out int winYReturn,
        out int maskReturn);

    [LibraryImport("X11")]
    public static partial int XTranslateCoordinates(
        IntPtr display,
        IntPtr src_w,
        IntPtr dest_w,
        int src_x,
        int src_y,
        out int dest_x_return,
        out int dest_y_return,
        out IntPtr child_return);

    #endregion

    #region libXss

    [LibraryImport("Xss")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool XScreenSaverQueryExtension(
        IntPtr dpy,
        out int event_base_return,
        out int error_base_return);

    [LibraryImport("Xss")]
    public static partial int XScreenSaverQueryInfo(
        IntPtr dpy,
        IntPtr drawable,
        ref XScreenSaverInfo saver_info);

    #endregion

    #region libXinerama

    [LibraryImport("Xinerama")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool XineramaQueryExtension(
        IntPtr dpy,
        out int event_base,
        out int error_base);

    [LibraryImport("Xinerama")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool XineramaIsActive(IntPtr dpy);

    [LibraryImport("Xinerama")]
    public static partial IntPtr XineramaQueryScreens(IntPtr dpy, out int number);

    #endregion

    #region libXrandr

    public const int RR_Connected = 0;

    [LibraryImport("Xrandr")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool XRRQueryExtension(
        IntPtr dpy,
        out int event_base_return,
        out int error_base_return);

    [LibraryImport("Xrandr")]
    public static partial int XRRQueryVersion(
        IntPtr dpy,
        out int major_version_return,
        out int minor_version_return);

    [LibraryImport("Xrandr")]
    public static partial IntPtr XRRGetScreenResourcesCurrent(IntPtr dpy, IntPtr window);

    [LibraryImport("Xrandr")]
    public static partial void XRRFreeScreenResources(IntPtr resources);

    [LibraryImport("Xrandr")]
    public static partial IntPtr XRRGetOutputInfo(IntPtr dpy, IntPtr resources, ulong output);

    [LibraryImport("Xrandr")]
    public static partial void XRRFreeOutputInfo(IntPtr outputInfo);

    [LibraryImport("Xrandr")]
    public static partial IntPtr XRRGetCrtcInfo(IntPtr dpy, IntPtr resources, ulong crtc);

    [LibraryImport("Xrandr")]
    public static partial void XRRFreeCrtcInfo(IntPtr crtcInfo);

    [LibraryImport("Xrandr")]
    public static partial int XRRGetOutputProperty(
        IntPtr dpy,
        ulong output,
        IntPtr property,
        long offset,
        long length,
        [MarshalAs(UnmanagedType.Bool)] bool _delete,
        [MarshalAs(UnmanagedType.Bool)] bool pending,
        IntPtr req_type,
        out IntPtr actual_type,
        out int actual_format,
        out ulong nitems,
        out ulong bytes_after,
        out IntPtr prop);

    #endregion

    /// <summary>
    /// Queries physical monitor rectangles via Xinerama (caller must hold <see cref="Sync"/>).
    /// </summary>
    /// <returns>Screen infos, or empty if Xinerama is unavailable/inactive.</returns>
    public static XineramaScreenInfo[] QueryXineramaScreensUnlocked(IntPtr display)
    {
        if (display == IntPtr.Zero)
        {

            return Array.Empty<XineramaScreenInfo>();
        }


        try
        {
            if (!XineramaQueryExtension(display, out _, out _))
            {

                return Array.Empty<XineramaScreenInfo>();
            }

            if (!XineramaIsActive(display))
            {
                return Array.Empty<XineramaScreenInfo>();
            }


            var ptr = XineramaQueryScreens(display, out var number);
            if (ptr == IntPtr.Zero || number <= 0)
            {

                return Array.Empty<XineramaScreenInfo>();
            }


            try
            {
                var size = Marshal.SizeOf<XineramaScreenInfo>();
                var list = new XineramaScreenInfo[number];
                for (var i = 0; i < number; i++)
                {
                    var item = Marshal.PtrToStructure<XineramaScreenInfo>(
                        IntPtr.Add(ptr, i * size));
                    list[i] = item;
                }
                return list;
            }
            finally
            {
                int v = XFree(ptr);
            }
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Queries connected RandR outputs (caller must hold <see cref="Sync"/>).
    /// Compares CRTC size (capture pixels) to preferred/active mode size (often native).
    /// </summary>
    public static XrandrOutputInfo[] QueryXrandrOutputsUnlocked(IntPtr display)
    {
        if (display == IntPtr.Zero)
        {

            return [];
        }


        try
        {
            if (!XRRQueryExtension(display, out _, out _))
            {
                AppLog.Debug("X11Native: XRRQueryExtension false");
                return [];
            }

            XRRQueryVersion(display, out var maj, out var min);
            AppLog.Debug($"X11Native: XRandR version {maj}.{min}");

            var root = DefaultRootWindow(display);
            var resPtr = XRRGetScreenResourcesCurrent(display, root);
            if (resPtr == IntPtr.Zero)
            {
                AppLog.Debug("X11Native: XRRGetScreenResourcesCurrent null");
                return [];
            }

            try
            {
                var res = Marshal.PtrToStructure<XRRScreenResources>(resPtr);
                if (res.noutput <= 0)
                {

                    return [];
                }

                // Build mode id → size map

                var modeSizes = new System.Collections.Generic.Dictionary<ulong, (uint w, uint h)>();
                if (res.nmode > 0 && res.modes != IntPtr.Zero)
                {
                    var modeSize = Marshal.SizeOf<XRRModeInfo>();
                    for (var m = 0; m < res.nmode; m++)
                    {
                        var mi = Marshal.PtrToStructure<XRRModeInfo>(IntPtr.Add(res.modes, m * modeSize));
                        modeSizes[mi.id] = (mi.width, mi.height);
                    }
                }

                var list = new System.Collections.Generic.List<XrandrOutputInfo>();
                for (var i = 0; i < res.noutput; i++)
                {
                    var outputId = (ulong)Marshal.ReadIntPtr(res.outputs, i * IntPtr.Size).ToInt64();
                    var oiPtr = XRRGetOutputInfo(display, resPtr, outputId);
                    if (oiPtr == IntPtr.Zero)
                    {
                        continue;
                    }


                    try
                    {
                        // Manual field reads (avoid full struct pack issues with name string)
                        var crtcId = (ulong)Marshal.ReadIntPtr(oiPtr, 8).ToInt64(); // crtc @ 8
                        var namePtr = Marshal.ReadIntPtr(oiPtr, 16); // name @ 16
                        // mm_width/mm_height are ulong @ 32 and 40 (not int @ 32/36).
                        var mmWidth = (int)Marshal.ReadInt64(oiPtr, 32);
                        var mmHeight = (int)Marshal.ReadInt64(oiPtr, 40);
                        var connection = Marshal.ReadInt32(oiPtr, 48); // connection @ 48
                        var nmode = Marshal.ReadInt32(oiPtr, 80); // nmode @ 80
                        var npreferred = Marshal.ReadInt32(oiPtr, 84); // npreferred @ 84
                        var modesPtr = Marshal.ReadIntPtr(oiPtr, 88); // modes @ 88

                        if (connection != RR_Connected || crtcId == 0)
                        {
                            continue;
                        }


                        var name = namePtr != IntPtr.Zero
                            ? (Marshal.PtrToStringAnsi(namePtr) ?? $"output-{i}")
                            : $"output-{i}";

                        var description = TryGetOutputDescription(display, outputId, mmWidth, mmHeight);

                        var ciPtr = XRRGetCrtcInfo(display, resPtr, crtcId);
                        if (ciPtr == IntPtr.Zero)
                        {
                            continue;
                        }


                        int cx, cy, cw, ch, rotation;
                        ulong activeModeId;
                        try
                        {
                            cx = Marshal.ReadInt32(ciPtr, 8);
                            cy = Marshal.ReadInt32(ciPtr, 12);
                            cw = Marshal.ReadInt32(ciPtr, 16);
                            ch = Marshal.ReadInt32(ciPtr, 20);
                            activeModeId = (ulong)Marshal.ReadIntPtr(ciPtr, 24).ToInt64();
                            rotation = Marshal.ReadInt32(ciPtr, 32); // rotation @ 32
                        }
                        finally
                        {
                            XRRFreeCrtcInfo(ciPtr);
                        }

                        uint modeW = 0, modeH = 0;
                        if (activeModeId != 0 && modeSizes.TryGetValue(activeModeId, out var ms))
                        {
                            modeW = ms.w;
                            modeH = ms.h;
                        }

                        uint prefW = modeW, prefH = modeH;
                        if (npreferred > 0 && nmode > 0 && modesPtr != IntPtr.Zero)
                        {
                            // First npreferred entries in modes[] are preferred; use first.
                            var prefModeId = (ulong)Marshal.ReadIntPtr(modesPtr, 0).ToInt64();
                            if (modeSizes.TryGetValue(prefModeId, out var ps))
                            {
                                prefW = ps.w;
                                prefH = ps.h;
                            }
                        }

                        // Orient preferred to CRTC rotation (90/270 swap).
                        int physW = (int)prefW, physH = (int)prefH;
                        if ((rotation & (RR_Rotate_90 | RR_Rotate_270)) != 0)
                        {
                            physW = (int)prefH;
                            physH = (int)prefW;
                        }

                        // Auto X11Scale ≤1: shrink fat FB only (no upscale when CRTC < physical).
                        double factor = 1.0;
                        if (cw > 0 && ch > 0 && physW > 0 && physH > 0)
                        {
                            var sx = (double)physW / cw;
                            var sy = (double)physH / ch;
                            double raw;
                            if (Math.Abs(sx - sy) <= 0.02)
                            {
                                raw = (sx + sy) / 2.0;
                            }

                            else
                            {
                                raw = Math.Sqrt(sx * sy);
                                AppLog.Debug(
                                    $"X11Native: {name} scale axis mismatch sx={sx:F3} sy={sy:F3} → geo={raw:F3}");
                            }

                            if (raw < 1.0 - 0.001)
                            {
                                factor = raw;
                                if (factor < 0.25)
                                {
                                    factor = 0.25;
                                }
                            }
                            else
                            {
                                factor = 1.0;
                            }

                        }

                        list.Add(new XrandrOutputInfo
                        {
                            Name = name,
                            X = cx,
                            Y = cy,
                            CurrentWidth = cw,
                            CurrentHeight = ch,
                            ModeWidth = (int)modeW,
                            ModeHeight = (int)modeH,
                            PreferredWidth = (int)prefW,
                            PreferredHeight = (int)prefH,
                            PhysicalWidth = physW,
                            PhysicalHeight = physH,
                            Rotation = rotation,
                            X11Scale = factor,
                            Description = description,
                            MmWidth = mmWidth,
                            MmHeight = mmHeight
                        });
                    }
                    finally
                    {
                        XRRFreeOutputInfo(oiPtr);
                    }
                }

                return [.. list];
            }
            finally
            {
                XRRFreeScreenResources(resPtr);
            }
        }
        catch (DllNotFoundException)
        {
            AppLog.Debug("X11Native: libXrandr not found");
            return [];
        }
        catch (Exception ex)
        {
            AppLog.Debug("X11Native: QueryXrandrOutputs: " + ex.Message);
            return [];
        }
    }


    /// <summary>
    /// Builds a short monitor description from EDID + physical size (e.g. "AOC 31\"").
    /// </summary>
    private static string TryGetOutputDescription(IntPtr display, ulong outputId, int mmWidth, int mmHeight)
    {
        try
        {
            var edidAtom = XInternAtom(display, "EDID", true);
            if (edidAtom == IntPtr.Zero)
            {
                edidAtom = XInternAtom(display, "EDID_DATA", true);
            }

            if (edidAtom == IntPtr.Zero)
            {
                return FormatDescription(null, null, mmWidth, mmHeight);
            }


            var status = XRRGetOutputProperty(
                display,
                outputId,
                edidAtom,
                0,
                256,
                false,
                false,
                IntPtr.Zero,
                out _,
                out var format,
                out var nitems,
                out _,
                out var prop);
            if (status != 0 || prop == IntPtr.Zero || nitems == 0)
            {
                if (prop != IntPtr.Zero)
                {
                    _ = XFree(prop);
                }


                return FormatDescription(null, null, mmWidth, mmHeight);
            }

            try
            {
                int byteCount = format switch
                {
                    8 => (int)nitems,
                    16 => (int)nitems * 2,
                    32 => (int)nitems * 4,
                    _ => (int)nitems
                };
                if (byteCount < 128)
                {

                    return FormatDescription(null, null, mmWidth, mmHeight);
                }


                var edid = new byte[Math.Min(byteCount, 512)];
                Marshal.Copy(prop, edid, 0, edid.Length);
                ParseEdid(edid, out var mfg, out var model);
                // Prefer RandR mm; if missing/zero, EDID max image size (cm → mm).
                var w = mmWidth;
                var h = mmHeight;
                if ((w <= 0 || h <= 0) && edid.Length > 22 && edid[21] > 0 && edid[22] > 0)
                {
                    w = edid[21] * 10;
                    h = edid[22] * 10;
                }
                return FormatDescription(mfg, model, w, h);
            }
            finally
            {
                _ = XFree(prop);
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("X11Native: EDID description: " + ex.Message);
            return FormatDescription(null, null, mmWidth, mmHeight);
        }
    }

    /// <summary>Parse manufacturer id + monitor name (0xFC) from EDID block.</summary>
    internal static void ParseEdid(byte[] edid, out string? manufacturer, out string? modelName)
    {
        manufacturer = null;
        modelName = null;
        if (edid == null || edid.Length < 128)
        {
            return;
        }


        int mid = (edid[8] << 8) | edid[9];
        char c1 = (char)(((mid >> 10) & 0x1f) + 64);
        char c2 = (char)(((mid >> 5) & 0x1f) + 64);
        char c3 = (char)((mid & 0x1f) + 64);
        if (c1 is >= 'A' and <= 'Z' && c2 is >= 'A' and <= 'Z' && c3 is >= 'A' and <= 'Z')
        {
            var pnp = new string(new[] { c1, c2, c3 });
            manufacturer = ExpandPnpManufacturer(pnp);
        }

        // Fallback size from EDID max image size (cm) at bytes 21-22 when mm unknown later.
        // (used by FormatDescription via optional edidCm args — size still from mm when valid)

        foreach (var off in new[] { 54, 72, 90, 108 })
        {
            if (off + 18 > edid.Length)
            {
                break;
            }


            if (edid[off] != 0 || edid[off + 1] != 0 || edid[off + 2] != 0)
            {
                continue;
            }


            if (edid[off + 3] != 0xFC)
            {
                continue;
            }


            var raw = System.Text.Encoding.ASCII.GetString(edid, off + 5, 13);
            var name = raw.Split('\n')[0].Trim().TrimEnd('\0').Trim();
            if (name.Length > 0)
            {
                modelName = name;
                break;
            }
        }
    }

    /// <summary>
    /// Prefer manufacturer + diagonal inches (e.g. "AOC 32\""), else manufacturer + model.
    /// Manufacturer is a display name expanded from EDID PNP id when known.
    /// </summary>
    internal static string FormatDescription(string? mfg, string? model, int mmW, int mmH)
    {
        string? sizeLabel = null;
        if (mmW > 0 && mmH > 0)
        {
            var diagMm = System.Math.Sqrt((double)mmW * mmW + (double)mmH * mmH);
            var inches = diagMm / 25.4;
            if (inches >= 5 && inches <= 120)
            {

                sizeLabel = ((int)System.Math.Round(inches)).ToString() + "\"";
            }

        }

        if (!string.IsNullOrEmpty(mfg) && sizeLabel != null)
        {

            return mfg + " " + sizeLabel;
        }


        if (!string.IsNullOrEmpty(mfg) && !string.IsNullOrEmpty(model))
        {

            return mfg + " " + model;
        }


        if (!string.IsNullOrEmpty(model))
        {
            return model!;
        }


        if (!string.IsNullOrEmpty(mfg))
        {

            return mfg!;
        }


        return sizeLabel ?? "";
    }

    /// <summary>
    /// Used PNP id → vendor name (from hwdata). Misses are also stored so the file is not re-scanned.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> PnpNameCache =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Full table loaded once from pnp.ids (null until first lookup).</summary>
    private static System.Collections.Generic.Dictionary<string, string>? _pnpIdsTable;
    private static readonly Lock PnpIdsGate = new();
    private static bool _pnpIdsLoadAttempted;

    private static readonly string[] PnpIdsPaths =
    [
        "/usr/share/hwdata/pnp.ids",
        "/usr/share/misc/pnp.ids",
        "/usr/share/libmate-desktop/pnp.ids",
    ];

    /// <summary>
    /// Maps EDID 3-letter PNP manufacturer id to a human vendor name via
    /// <c>/usr/share/hwdata/pnp.ids</c> (same source as desktop display settings).
    /// File is read at most once per process; per-id results are cached.
    /// </summary>
    internal static string ExpandPnpManufacturer(string pnpId)
    {
        if (string.IsNullOrEmpty(pnpId))
        {
            return pnpId;
        }


        var key = pnpId.ToUpperInvariant();
        if (PnpNameCache.TryGetValue(key, out var cached))
        {

            return cached;
        }


        EnsurePnpIdsLoaded();
        string name;
        if (_pnpIdsTable != null && _pnpIdsTable.TryGetValue(key, out var fromFile)
            && !string.IsNullOrWhiteSpace(fromFile))
        {
            name = fromFile.Trim();
        }
        else
        {
            name = key; // unknown: keep 3-letter id
        }


        PnpNameCache[key] = name;
        return name;
    }

    /// <summary>Loads pnp.ids into memory once (best-effort).</summary>
    private static void EnsurePnpIdsLoaded()
    {
        if (_pnpIdsLoadAttempted)
        {
            return;
        }


        lock (PnpIdsGate)
        {
            if (_pnpIdsLoadAttempted)
            {
                return;
            }


            _pnpIdsLoadAttempted = true;
            try
            {
                string? path = null;
                foreach (var p in PnpIdsPaths)
                {
                    if (System.IO.File.Exists(p))
                    {
                        path = p;
                        break;
                    }
                }

                if (path == null)
                {
                    AppLog.Debug("X11Native: pnp.ids not found (tried hwdata/misc/mate paths)");
                    _pnpIdsTable = new System.Collections.Generic.Dictionary<string, string>(
                        System.StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var table = new System.Collections.Generic.Dictionary<string, string>(
                    System.StringComparer.OrdinalIgnoreCase);
                foreach (var line in System.IO.File.ReadLines(path))
                {
                    if (line.Length < 5 || line[0] == '#')
                    {
                        continue;
                    }
                    // Format: "AUS\tASUSTek COMPUTER INC" or space-separated
                    var tab = line.IndexOf('\t');
                    int sep = tab >= 0 ? tab : line.IndexOf(' ');
                    if (sep < 3)
                    {
                        continue;
                    }


                    var id = line.AsSpan(0, sep).Trim().ToString();
                    if (id.Length != 3)
                    {
                        continue;
                    }


                    var vendor = line.AsSpan(sep + 1).Trim().ToString();
                    if (vendor.Length == 0)
                    {
                        continue;
                    }
                    // First entry wins if duplicates

                    if (!table.ContainsKey(id))
                    {
                        table[id] = vendor;
                    }

                }

                _pnpIdsTable = table;
                AppLog.Debug($"X11Native: loaded {table.Count} PNP vendors from {path}");
            }
            catch (Exception ex)
            {
                AppLog.Debug("X11Native: pnp.ids load failed: " + ex.Message);
                _pnpIdsTable = new System.Collections.Generic.Dictionary<string, string>(
                    System.StringComparer.OrdinalIgnoreCase);
            }
        }
    }

}
