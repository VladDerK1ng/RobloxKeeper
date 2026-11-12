using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

// The shader overlay: a see-through window laid exactly over one Roblox
// window, showing a live copy of it drawn through a pixel shader.
//
// Nothing is loaded into Roblox and its memory is never opened. The copy
// comes from Windows.Graphics.Capture - the same thing a screen recorder uses
// - and is drawn on this app's own Direct3D device into this app's own
// window. That window never takes clicks, keys or the foreground: they all go
// straight through to the game underneath, which stays the window in front,
// so macros and the anti-AFK nudge work as they did.
//
// It runs as a separate copy of this exe (Program starts it with --overlay),
// one for each window it covers. A fault in graphics code then ends that
// copy and not the app, and stopping the effect is ending a process.
//
// The Direct3D calls are made through each object's table of functions by
// number, because this project takes on no packages. The numbers were read
// out of the Windows SDK headers by a script, and the whole path was measured
// before any of it was kept: on a 180Hz laptop it presented every frame it
// was given, 0.4ms after each arrived, for about a tenth of one processor
// core and 60MB.
namespace RobloxKeeper.Shader
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr hwnd, ref Guid iid, out IntPtr result);
        [PreserveSig] int CreateForMonitor(IntPtr hmon, ref Guid iid, out IntPtr result);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(ref Guid iid, out IntPtr p);
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; public uint priv; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public int cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width, Height; public int Format; public int Stereo; public uint SampleCount, SampleQuality;
        public uint BufferUsage, BufferCount; public int Scaling, SwapEffect, AlphaMode; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct D3D11_VIEWPORT { public float X, Y, W, H, MinD, MaxD; }
    [StructLayout(LayoutKind.Sequential)] struct D3D11_BUFFER_DESC { public uint ByteWidth; public int Usage; public uint BindFlags, CpuAccess, Misc, Stride; }
    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_SAMPLER_DESC
    {
        public int Filter, AddressU, AddressV, AddressW; public float MipLodBias; public uint MaxAnisotropy; public int ComparisonFunc;
        public float B0, B1, B2, B3, MinLod, MaxLod;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize; public int Format; public uint SampleCount, SampleQuality;
        public int Usage; public uint BindFlags, CpuAccess, Misc;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision; public IntPtr VideoMem, SysMem, SharedMem; public uint LuidLow; public int LuidHigh;
    }

    delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    // Vtable slot numbers were extracted by script from d3d11.h / dxgi.h / dxgi1_2.h / d3dcommon.h (SDK 10.0.26100).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DevCreateBuffer(IntPtr self, ref D3D11_BUFFER_DESC d, IntPtr init, out IntPtr buf);          // ID3D11Device 3
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DevCreateView(IntPtr self, IntPtr res, IntPtr desc, out IntPtr view);                    // 7 SRV, 9 RTV
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DevCreateShader(IntPtr self, IntPtr code, IntPtr len, IntPtr linkage, out IntPtr sh);    // 12 VS, 15 PS
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DevCreateSampler(IntPtr self, ref D3D11_SAMPLER_DESC d, out IntPtr s);                   // 23
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxSetSlots(IntPtr self, uint start, uint n, IntPtr[] items);                           // ID3D11DeviceContext 8, 10, 16
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxSetShader(IntPtr self, IntPtr sh, IntPtr inst, uint n);                              // 9 PS, 11 VS
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxDraw(IntPtr self, uint count, uint start);                                           // 13
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxSetInt(IntPtr self, int v);                                                          // 24
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxSetTargets(IntPtr self, uint n, IntPtr[] rtvs, IntPtr dsv);                          // 33
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxSetViewports(IntPtr self, uint n, ref D3D11_VIEWPORT vp);                            // 44
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxUpdateSubresource(IntPtr self, IntPtr res, uint sub, IntPtr box, IntPtr data, uint rowPitch, uint depthPitch); // 48
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CtxVoid(IntPtr self);                                                                   // 110 ClearState, 111 Flush
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void TexGetDesc(IntPtr self, out D3D11_TEXTURE2D_DESC d);                                    // ID3D11Texture2D 10
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScPresent(IntPtr self, uint sync, uint flags);                                           // IDXGISwapChain 8
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScGetBuffer(IntPtr self, uint i, ref Guid iid, out IntPtr surf);                         // 9
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScResizeBuffers(IntPtr self, uint count, uint w, uint h, int fmt, uint flags);           // 13
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FacCreateSwapChainForHwnd(IntPtr self, IntPtr dev, IntPtr hwnd, ref DXGI_SWAP_CHAIN_DESC1 d, IntPtr fs, IntPtr output, out IntPtr sc); // IDXGIFactory2 15
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FacMakeWindowAssociation(IntPtr self, IntPtr hwnd, uint flags);                          // 8
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DxgiGetAdapter(IntPtr self, out IntPtr adapter);                                         // IDXGIDevice 7
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DxgiGetParent(IntPtr self, ref Guid iid, out IntPtr parent);                             // IDXGIAdapter 6
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AdapterGetDesc(IntPtr self, out DXGI_ADAPTER_DESC d);                                    // IDXGIAdapter 8
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumIndexed(IntPtr self, uint index, out IntPtr item);                                    // IDXGIFactory 7 EnumAdapters, IDXGIAdapter 7 EnumOutputs
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FacEnumByPreference(IntPtr self, uint index, int preference, ref Guid iid, out IntPtr adapter); // IDXGIFactory6 29
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ScSetMaxLatency(IntPtr self, uint frames);                                               // IDXGISwapChain2 31
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr ScGetWaitable(IntPtr self);                                                           // IDXGISwapChain2 33
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BlobGet(IntPtr self);                                                                 // ID3DBlob 3 pointer, 4 size

    static class N
    {
        public static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T));
        }
        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new Exception(what + " failed, HRESULT 0x" + hr.ToString("X8"));
        }

        [DllImport("d3d11.dll")] public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint nLevels, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3d11.dll")] public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
        [DllImport("d3dcompiler_47.dll")] public static extern int D3DCompile(byte[] src, IntPtr len, [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr defines, IntPtr include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
        [DllImport("dxgi.dll")] public static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG m);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc cb, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandleW(IntPtr name);
    }

    static class Overlay
    {
        const string ClassName = "RobloxKeeperShaderOverlay";
        const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                   WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_TIMER = 0x113;

        // What it was started with.
        static string shaderPath, problemPath;
        static double minIntervalMs = 1;
        static float sharpness = 0.5f, saturation = 1f, vibrance;

        // shared state (UI thread writes geometry, render thread reads it)
        static IntPtr target, hwnd;
        static uint targetPid;
        static volatile bool running = true, wantRender;
        static volatile int cw, ch, cropX, cropY, shownW = -1, shownH = -1;
        static int lastX = int.MinValue, lastY, lastW, lastH;
        static bool shown;
        static long presented;
        static readonly AutoResetEvent frameEvent = new AutoResetEvent(false);
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static WndProc wndProc;            // keep delegates alive
        static WinEventProc winEventProc;
        static string fatal;
        static IntPtr waitable;             // signalled when the swap chain can take another frame

        // Why it could not go on, left where the app that started it will look.
        static void Problem(string why)
        {
            if (problemPath == null) return;
            try { File.WriteAllText(problemPath, why); } catch { }
        }

        static float Number(string s, float fallback)
        {
            float f;
            return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : fallback;
        }

        // 0 when it ran until its window went away; anything else, and the
        // problem file says why.
        public static int Run(string[] args)
        {
            int parent = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                bool more = i + 1 < args.Length;
                if (a == "--hwnd" && more) { long h; if (long.TryParse(args[++i], out h)) target = new IntPtr(h); }
                else if (a == "--shader" && more) shaderPath = args[++i];
                else if (a == "--parent" && more) int.TryParse(args[++i], out parent);
                else if (a == "--problem" && more) problemPath = args[++i];
                else if (a == "--sharpness" && more) sharpness = Number(args[++i], sharpness);
                else if (a == "--saturation" && more) saturation = Number(args[++i], saturation);
                else if (a == "--vibrance" && more) vibrance = Number(args[++i], vibrance);
            }
            if (target == IntPtr.Zero || !N.IsWindow(target)) { Problem("the Roblox window has gone"); return 3; }
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath)) { Problem("the shader file is missing"); return 3; }
            N.GetWindowThreadProcessId(target, out targetPid);

            try
            {
                N.SetProcessDpiAwarenessContext((IntPtr)(-4)); // real pixels, so the overlay sits exactly on the game
                if (!GraphicsCaptureSession.IsSupported()) { Problem("it needs Windows 10 version 1903 or newer"); return 4; }

                CreateOverlayWindow();
                Thread render = new Thread(RenderThread);
                render.IsBackground = true;
                render.Start();

                // The app that started this one closing, or crashing, takes the overlay with it.
                if (parent > 0)
                {
                    Thread watch = new Thread(delegate()
                    {
                        try { using (Process p = Process.GetProcessById(parent)) p.WaitForExit(); } catch { }
                        running = false;
                        IntPtr h = hwnd;
                        if (h != IntPtr.Zero) N.PostMessageW(h, WM_TIMER, (IntPtr)1, IntPtr.Zero);
                    });
                    watch.IsBackground = true;
                    watch.Start();
                }

                winEventProc = OnWinEvent;
                IntPtr h1 = N.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, winEventProc, 0, 0, 0x0002);          // EVENT_SYSTEM_FOREGROUND, any process, out-of-context
                IntPtr h2 = N.SetWinEventHook(0x0016, 0x0017, IntPtr.Zero, winEventProc, targetPid, 0, 0x0002);  // MINIMIZESTART/END of the target
                IntPtr h3 = N.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, winEventProc, targetPid, 0, 0x0002);  // LOCATIONCHANGE of the target
                N.SetTimer(hwnd, (IntPtr)1, 15, IntPtr.Zero);

                MSG m;
                while (N.GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) N.DispatchMessageW(ref m);

                running = false;
                frameEvent.Set();
                render.Join(3000);
                N.UnhookWinEvent(h1); N.UnhookWinEvent(h2); N.UnhookWinEvent(h3);
            }
            catch (Exception e) { fatal = e.Message; }
            if (fatal != null) { Problem(fatal); return 5; }
            return 0;
        }

        // ---------------------------------------------------------------- overlay window + tracking (UI thread)

        static void CreateOverlayWindow()
        {
            wndProc = WindowProc;
            WNDCLASSEX wc = new WNDCLASSEX();
            wc.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc);
            wc.hInstance = N.GetModuleHandleW(IntPtr.Zero);
            wc.lpszClassName = ClassName;
            if (N.RegisterClassExW(ref wc) == 0) throw new Exception("Windows couldn't create the shader window (" + Marshal.GetLastWin32Error() + ")");
            // Layered and transparent to the mouse, never activated, kept out of
            // Alt+Tab and the taskbar, on top of the game it covers.
            uint ex = WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            hwnd = N.CreateWindowExW(ex, ClassName, "RobloxKeeper shader", WS_POPUP, 0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new Exception("Windows couldn't create the shader window (" + Marshal.GetLastWin32Error() + ")");
            N.SetLayeredWindowAttributes(hwnd, 0, 255, 2); // LWA_ALPHA, opaque: the layered style is only there for click-through
        }

        static IntPtr WindowProc(IntPtr h, uint m, IntPtr w, IntPtr l)
        {
            switch (m)
            {
                case WM_NCHITTEST: return (IntPtr)(-1);                               // HTTRANSPARENT
                case WM_MOUSEACTIVATE: return (IntPtr)3;                               // MA_NOACTIVATE
                case WM_TIMER: Track(); return IntPtr.Zero;
                case WM_DESTROY: N.PostQuitMessage(0); return IntPtr.Zero;
            }
            return N.DefWindowProcW(h, m, w, l);
        }

        static void OnWinEvent(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
        {
            if (ev == 0x800B && (h != target || idObject != 0)) return;
            Track();
        }

        static void Track()
        {
            if (!running || fatal != null || !N.IsWindow(target))
            {
                if (hwnd != IntPtr.Zero) { IntPtr h = hwnd; hwnd = IntPtr.Zero; wantRender = false; N.DestroyWindow(h); }
                return;
            }
            RECT cr; POINT p = new POINT();
            N.GetClientRect(target, out cr);
            N.ClientToScreen(target, ref p);
            int w = cr.R - cr.L, hgt = cr.B - cr.T;
            // Only over the game while the game is the window in front: behind
            // another window there is nothing of it to draw over.
            bool want = N.IsWindowVisible(target) && !N.IsIconic(target) && w > 0 && hgt > 0 && N.GetForegroundWindow() == target;
            if (want)
            {
                RECT fb;
                if (N.DwmGetWindowAttribute(target, 9, out fb, 16) == 0) { cropX = p.X - fb.L; cropY = p.Y - fb.T; }  // DWMWA_EXTENDED_FRAME_BOUNDS
                cw = w; ch = hgt;
                if (!shown && (p.X != lastX || p.Y != lastY || w != lastW || hgt != lastH))
                {
                    N.SetWindowPos(hwnd, (IntPtr)(-1), p.X, p.Y, w, hgt, 0x0010); // place it while still hidden
                    lastX = p.X; lastY = p.Y; lastW = w; lastH = hgt;
                }
            }
            wantRender = want;
            // show only once a frame of the current size has been presented, so the user never sees a stale/blank overlay
            bool ready = want && shownW == w && shownH == hgt;
            if (ready)
            {
                if (!shown || p.X != lastX || p.Y != lastY || w != lastW || hgt != lastH)
                {
                    N.SetWindowPos(hwnd, (IntPtr)(-1), p.X, p.Y, w, hgt, 0x0010 | 0x0040); // HWND_TOPMOST, SWP_NOACTIVATE | SWP_SHOWWINDOW
                    lastX = p.X; lastY = p.Y; lastW = w; lastH = hgt; shown = true;
                }
            }
            else if (shown)
            {
                if (!want) { N.ShowWindow(hwnd, 0); shown = false; shownW = shownH = -1; }
                else if (p.X != lastX || p.Y != lastY || w != lastW || hgt != lastH)
                {
                    // resizing: keep covering the client area, the swap chain stretches until the next frame lands
                    N.SetWindowPos(hwnd, (IntPtr)(-1), p.X, p.Y, w, hgt, 0x0010);
                    lastX = p.X; lastY = p.Y; lastW = w; lastH = hgt;
                }
            }
        }

        // ---------------------------------------------------------------- D3D + capture (render thread)

        static IntPtr device, context, swapChain, rtv, vs, ps, sampler, cbuffer;
        static int scW, scH;
        static DevCreateView createSrv, createRtv;
        static CtxSetSlots psSetSrv, psSetSamplers, psSetCb;
        static CtxSetShader psSet, vsSet;
        static CtxDraw draw;
        static CtxSetInt iaTopology;
        static CtxSetTargets omSetTargets;
        static CtxSetViewports rsViewports;
        static CtxUpdateSubresource updateSubresource;
        static CtxVoid clearState;
        static ScPresent present;
        static ScGetBuffer getBuffer;
        static ScResizeBuffers resizeBuffers;
        static Guid IID_Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        // Whether a pixel shader's text compiles: null when it does, and
        // the compiler's own words when it doesn't. Uses nothing but the
        // compiler that ships with Windows - no graphics device - so the
        // built-in looks can be checked without a screen.
        public static string CompileProblem(string source)
        {
            try
            {
                IntPtr code = Compile(source, "shader", "ps_5_0");
                Marshal.Release(code);
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        static IntPtr Compile(string source, string name, string profile)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(source);
            IntPtr code, err;
            int hr = N.D3DCompile(bytes, (IntPtr)bytes.Length, name, IntPtr.Zero, IntPtr.Zero, "main", profile, 1u << 15, 0, out code, out err);
            if (hr < 0)
            {
                string msg = "";
                if (err != IntPtr.Zero) msg = Marshal.PtrToStringAnsi(N.Fn<BlobGet>(err, 3)(err), (int)N.Fn<BlobGet>(err, 4)(err));
                throw new Exception("D3DCompile(" + name + ") HRESULT 0x" + hr.ToString("X8") + ": " + msg);
            }
            if (err != IntPtr.Zero) Marshal.Release(err);
            return code;
        }

        const string VertexShader =
            "struct VO { float4 pos : SV_Position; float2 uv : TEXCOORD0; };\n" +
            "VO main(uint id : SV_VertexID) { VO o; o.uv = float2((id << 1) & 2, id & 2);\n" +
            "  o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1); return o; }\n";

        static void InitD3D()
        {
            int level;
            IntPtr enumFactory, chosen = IntPtr.Zero;
            Guid iidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            N.Check(N.CreateDXGIFactory1(ref iidFactory1, out enumFactory), "CreateDXGIFactory1");
            {
                // Default: the power-saving GPU. On hybrid laptops that is the one DWM composes on, so presenting from it
                // needs no cross-adapter copy. (EnumOutputs is no guide: DXGI lists the panel under the "preferred" GPU.)
                IntPtr f6; Guid iidF6 = new Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17"), iidAdapter = new Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
                if (Marshal.QueryInterface(enumFactory, ref iidF6, out f6) >= 0) // IDXGIFactory6: Windows 10 1803+
                {
                    if (N.Fn<FacEnumByPreference>(f6, 29)(f6, 0, 1, ref iidAdapter, out chosen) < 0) chosen = IntPtr.Zero; // DXGI_GPU_PREFERENCE_MINIMUM_POWER
                    Marshal.Release(f6);
                }
            }
            Marshal.Release(enumFactory);
            // driver type UNKNOWN (0) is required with an explicit adapter, HARDWARE (1) with none; 0x20 = BGRA_SUPPORT
            N.Check(N.D3D11CreateDevice(chosen, chosen != IntPtr.Zero ? 0 : 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context), "D3D11CreateDevice");
            if (chosen != IntPtr.Zero) Marshal.Release(chosen);
            createSrv = N.Fn<DevCreateView>(device, 7); createRtv = N.Fn<DevCreateView>(device, 9);
            psSetSrv = N.Fn<CtxSetSlots>(context, 8); psSet = N.Fn<CtxSetShader>(context, 9); psSetSamplers = N.Fn<CtxSetSlots>(context, 10);
            vsSet = N.Fn<CtxSetShader>(context, 11); draw = N.Fn<CtxDraw>(context, 13); psSetCb = N.Fn<CtxSetSlots>(context, 16);
            iaTopology = N.Fn<CtxSetInt>(context, 24); omSetTargets = N.Fn<CtxSetTargets>(context, 33); rsViewports = N.Fn<CtxSetViewports>(context, 44);
            updateSubresource = N.Fn<CtxUpdateSubresource>(context, 48); clearState = N.Fn<CtxVoid>(context, 110);

            IntPtr code = Compile(VertexShader, "builtin-vs", "vs_5_0");
            N.Check(N.Fn<DevCreateShader>(device, 12)(device, N.Fn<BlobGet>(code, 3)(code), N.Fn<BlobGet>(code, 4)(code), IntPtr.Zero, out vs), "CreateVertexShader");
            Marshal.Release(code);
            code = Compile(File.ReadAllText(shaderPath), Path.GetFileName(shaderPath), "ps_5_0");
            N.Check(N.Fn<DevCreateShader>(device, 15)(device, N.Fn<BlobGet>(code, 3)(code), N.Fn<BlobGet>(code, 4)(code), IntPtr.Zero, out ps), "CreatePixelShader");
            Marshal.Release(code);

            D3D11_SAMPLER_DESC sd = new D3D11_SAMPLER_DESC();
            sd.Filter = 0x14; sd.AddressU = sd.AddressV = sd.AddressW = 3; sd.ComparisonFunc = 1; sd.MaxAnisotropy = 1; sd.MaxLod = float.MaxValue; // MIN_MAG_LINEAR_MIP_POINT, CLAMP
            N.Check(N.Fn<DevCreateSampler>(device, 23)(device, ref sd, out sampler), "CreateSamplerState");
            D3D11_BUFFER_DESC bd = new D3D11_BUFFER_DESC();
            bd.ByteWidth = 48; bd.BindFlags = 4; // CONSTANT_BUFFER
            N.Check(N.Fn<DevCreateBuffer>(device, 3)(device, ref bd, IntPtr.Zero, out cbuffer), "CreateBuffer");
        }

        static void CreateSwapChain(IntPtr dxgiDevice, int w, int h)
        {
            IntPtr adapter, factory;
            N.Check(N.Fn<DxgiGetAdapter>(dxgiDevice, 7)(dxgiDevice, out adapter), "IDXGIDevice.GetAdapter");
            Guid iidFactory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
            N.Check(N.Fn<DxgiGetParent>(adapter, 6)(adapter, ref iidFactory2, out factory), "IDXGIAdapter.GetParent(IDXGIFactory2)");
            DXGI_SWAP_CHAIN_DESC1 d = new DXGI_SWAP_CHAIN_DESC1();
            d.Width = (uint)w; d.Height = (uint)Align(h); d.Format = 87; // B8G8R8A8_UNORM; height padded, see Align
            d.SampleCount = 1; d.BufferUsage = 0x20; d.BufferCount = 2; // RENDER_TARGET_OUTPUT
            d.Scaling = 1; d.SwapEffect = 4; d.AlphaMode = 3;  // STRETCH(0)/NONE(1), FLIP_DISCARD, IGNORE
            d.Flags = SwapFlags();
            N.Check(N.Fn<FacCreateSwapChainForHwnd>(factory, 15)(factory, device, hwnd, ref d, IntPtr.Zero, IntPtr.Zero, out swapChain), "CreateSwapChainForHwnd(flip-discard on layered window)");
            N.Fn<FacMakeWindowAssociation>(factory, 8)(factory, hwnd, 3); // no Alt+Enter, no window-change monitoring
            Marshal.Release(factory); Marshal.Release(adapter);
            present = N.Fn<ScPresent>(swapChain, 8); getBuffer = N.Fn<ScGetBuffer>(swapChain, 9); resizeBuffers = N.Fn<ScResizeBuffers>(swapChain, 13);
            scW = w; scH = h;
            {
                IntPtr sc2; Guid iidSc2 = new Guid("a8be2ac4-199f-4946-b331-79599fb98de7");
                N.Check(Marshal.QueryInterface(swapChain, ref iidSc2, out sc2), "QI IDXGISwapChain2");
                N.Check(N.Fn<ScSetMaxLatency>(sc2, 31)(sc2, 1), "SetMaximumFrameLatency");
                waitable = N.Fn<ScGetWaitable>(sc2, 33)(sc2);
                Marshal.Release(sc2);
            }
            MakeRtv();
        }
        static uint SwapFlags() { return 0x40u; } // DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT

        // Measured on the test machine (RTX 5060 laptop, hybrid graphics): a flip-model back buffer whose height is not a
        // multiple of 4 reaches the screen resampled from the next multiple of 4 (rows dropped at 1/4 and 3/4 of the height).
        // Padding the buffer and presenting unscaled (DXGI_SCALING_NONE) keeps the mapping 1:1; the pad rows are clipped.
        static int Align(int h) { return (h + 3) & ~3; }

        static void MakeRtv()
        {
            IntPtr back;
            N.Check(getBuffer(swapChain, 0, ref IID_Texture2D, out back), "IDXGISwapChain.GetBuffer");
            N.Check(createRtv(device, back, IntPtr.Zero, out rtv), "CreateRenderTargetView");
            Marshal.Release(back);
        }

        static void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            frameEvent.Set();
        }

        // Newer session properties live in their own methods: on an older Windows the JIT would otherwise
        // fail the whole caller with MissingMethodException.
        [MethodImpl(MethodImplOptions.NoInlining)] static void DisableCursor(GraphicsCaptureSession s) { s.IsCursorCaptureEnabled = false; }
        [MethodImpl(MethodImplOptions.NoInlining)] static void DisableBorder(GraphicsCaptureSession s) { s.IsBorderRequired = false; }
        [MethodImpl(MethodImplOptions.NoInlining)] static void SetMinInterval(GraphicsCaptureSession s, double ms) { s.MinUpdateInterval = TimeSpan.FromMilliseconds(ms); }

        static void RenderThread()
        {
            try { RenderLoop(); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message + " (HRESULT 0x" + Marshal.GetHRForException(e).ToString("X8") + ")"; }
            running = false;
            IntPtr h = hwnd;
            if (h != IntPtr.Zero) N.PostMessageW(h, WM_TIMER, (IntPtr)1, IntPtr.Zero); // let the UI thread tear down
        }

        static void RenderLoop()
        {
            InitD3D();

            IntPtr dxgiDevice, inspectable;
            Guid iidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            N.Check(Marshal.QueryInterface(device, ref iidDxgiDevice, out dxgiDevice), "QI IDXGIDevice");
            N.Check(N.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
            IDirect3DDevice winrtDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
            Marshal.Release(inspectable);

            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
            Guid iidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            IntPtr itemPtr;
            N.Check(interop.CreateForWindow(target, ref iidItem, out itemPtr), "IGraphicsCaptureItemInterop.CreateForWindow");
            GraphicsCaptureItem item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr);
            Marshal.Release(itemPtr);
            item.Closed += delegate { running = false; };

            // Begun when the game is first the window in front, not before.
            SizeInt32 poolSize = item.Size;
            Direct3D11CaptureFramePool pool = null;
            GraphicsCaptureSession session = null;
            double idleSince = -1;

            float[] cb = new float[12];
            GCHandle cbPin = GCHandle.Alloc(cb, GCHandleType.Pinned);
            IntPtr[] one = new IntPtr[1];
            bool mustWait = false;

            while (running)
            {
                frameEvent.WaitOne(50);
                double now = clock.Elapsed.TotalSeconds;

                // Capturing costs graphics time whether or not anything is drawn with it. While the game is not the
                // window in front there is nothing to draw over, so after a moment the capture is ended - and begun
                // again when the game comes back. An overlay over a client parked behind others costs nothing.
                if (!wantRender)
                {
                    if (idleSince < 0) idleSince = now;
                    if (session != null && now - idleSince >= PAUSE_AFTER_S)
                    {
                        session.Dispose(); pool.Dispose();
                        session = null; pool = null;
                    }
                    if (session == null) continue;
                }
                else
                {
                    idleSince = -1;
                    if (session == null)
                    {
                        poolSize = item.Size;
                        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
                        pool.FrameArrived += OnFrameArrived;
                        session = Begin(pool, item);
                    }
                }

                // Wait until the swap chain can take another frame *before* choosing which frame to draw:
                // Present then never blocks and the frame drawn is always the newest one.
                if (mustWait && waitable != IntPtr.Zero) { if (N.WaitForSingleObject(waitable, 100) != 0) continue; mustWait = false; }

                // drain to the newest frame
                Direct3D11CaptureFrame frame = null, next;
                while ((next = pool.TryGetNextFrame()) != null)
                {
                    if (frame != null) frame.Dispose();
                    frame = next;
                }
                if (frame == null) continue;
                SizeInt32 content = frame.ContentSize;
                int w = cw, h = ch;
                if (wantRender && w > 0 && h > 0)
                {
                    if (swapChain == IntPtr.Zero) CreateSwapChain(dxgiDevice, w, h);
                    else if (w != scW || h != scH)
                    {
                        clearState(context);
                        Marshal.Release(rtv); rtv = IntPtr.Zero;
                        N.Check(resizeBuffers(swapChain, 0, (uint)w, (uint)Align(h), 0, SwapFlags()), "ResizeBuffers");
                        scW = w; scH = h;
                        MakeRtv();
                    }

                    IDirect3DSurface surface = frame.Surface;
                    IntPtr tex, srv;
                    N.Check(((IDirect3DDxgiInterfaceAccess)(object)surface).GetInterface(ref IID_Texture2D, out tex), "IDirect3DDxgiInterfaceAccess.GetInterface");
                    D3D11_TEXTURE2D_DESC td;
                    N.Fn<TexGetDesc>(tex, 10)(tex, out td);
                    N.Check(createSrv(device, tex, IntPtr.Zero, out srv), "CreateShaderResourceView");

                    cb[0] = (float)cropX / td.Width; cb[1] = (float)cropY / td.Height;
                    cb[2] = (float)w / td.Width; cb[3] = (float)h / td.Height;
                    cb[4] = 1f / td.Width; cb[5] = 1f / td.Height; cb[6] = sharpness; cb[7] = saturation;
                    cb[8] = vibrance; cb[9] = (float)now; cb[10] = w; cb[11] = h;
                    updateSubresource(context, cbuffer, 0, IntPtr.Zero, cbPin.AddrOfPinnedObject(), 0, 0);

                    D3D11_VIEWPORT vp = new D3D11_VIEWPORT(); vp.W = w; vp.H = h; vp.MaxD = 1;
                    one[0] = rtv; omSetTargets(context, 1, one, IntPtr.Zero);
                    rsViewports(context, 1, ref vp);
                    iaTopology(context, 4); // TRIANGLELIST
                    vsSet(context, vs, IntPtr.Zero, 0);
                    psSet(context, ps, IntPtr.Zero, 0);
                    one[0] = srv; psSetSrv(context, 0, 1, one);
                    one[0] = sampler; psSetSamplers(context, 0, 1, one);
                    one[0] = cbuffer; psSetCb(context, 0, 1, one);
                    draw(context, 3, 0);
                    N.Check(present(swapChain, 0, 0), "Present");
                    mustWait = true;
                    one[0] = IntPtr.Zero; psSetSrv(context, 0, 1, one);
                    Marshal.Release(srv); Marshal.Release(tex);
                    ((IDisposable)surface).Dispose();

                    Interlocked.Increment(ref presented);
                    shownW = w; shownH = h;
                }
                frame.Dispose();
                if (content.Width != poolSize.Width || content.Height != poolSize.Height)
                {
                    poolSize = content;
                    pool.Recreate(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
                }
            }

            if (session != null) { session.Dispose(); pool.Dispose(); }
            cbPin.Free();
        }

        // Long enough that flicking to another window and back does not
        // stop and start it; short enough that a parked client costs nothing.
        const double PAUSE_AFTER_S = 2.0;

        static GraphicsCaptureSession Begin(Direct3D11CaptureFramePool pool, GraphicsCaptureItem item)
        {
            GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            bool hasCursorProp = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled");
            bool hasBorderProp = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired");
            if (hasCursorProp) DisableCursor(session);
            if (hasBorderProp) DisableBorder(session);
            bool hasIntervalProp = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval");
            // Windows hands frames over at most every 16ms unless asked for more; the game runs faster than that.
            if (hasIntervalProp && minIntervalMs >= 0) SetMinInterval(session, minIntervalMs);
            session.StartCapture();
            return session;
        }
    }
}
