using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace RobloxKeeper
{
    // One ready-made look: how much of each thing the built-in shader does.
    class ShaderPreset
    {
        public string Name, Says;
        public double Sharpen;          // 0 none .. 1 strongest
        public double Saturation = 1;   // 1 unchanged, 0 black and white
        public double Vibrance;         // lifts dull colours more than bright ones
        public double Contrast = 1;     // 1 unchanged
        public double Warmth;           // towards orange when positive, blue when negative
        public double Vignette;         // darker corners

        // The ones only some looks use. Each adds its own lines to the
        // shader, and a look that leaves one at nothing does not pay for it.
        public double Glow;             // bright things spill a soft light
        public double Fade;             // blacks lifted towards grey, as in a faded photograph
        public double Depth;            // darks darker and lights lighter along a curve, without clipping
        public double Scanlines;        // the dark lines of an old television
        public double Grain;            // film grain
        public double Outline;          // a dark line where the picture changes sharply
        public int Levels;              // this many steps of each colour, as in a comic; 0 for all of them
        public double Tint;             // how far towards one colour: 1 is all the way
        public double TintR = 1, TintG = 1, TintB = 1;

        public ShaderPreset(string name, string says, double sharpen, double saturation, double vibrance,
                            double contrast, double warmth, double vignette)
        {
            Name = name; Says = says; Sharpen = sharpen; Saturation = saturation; Vibrance = vibrance;
            Contrast = contrast; Warmth = warmth; Vignette = vignette;
        }

        // Written after the constructor, so a look reads as a sentence:
        // new ShaderPreset(...).WithGlow(0.5).WithGrain(0.03)
        public ShaderPreset WithGlow(double v) { Glow = v; return this; }
        public ShaderPreset WithFade(double v) { Fade = v; return this; }
        public ShaderPreset WithDepth(double v) { Depth = v; return this; }
        public ShaderPreset WithScanlines(double v) { Scanlines = v; return this; }
        public ShaderPreset WithGrain(double v) { Grain = v; return this; }
        public ShaderPreset WithOutline(double v) { Outline = v; return this; }
        public ShaderPreset WithLevels(int n) { Levels = n; return this; }
        public ShaderPreset WithTint(double amount, double r, double g, double b)
        {
            Tint = amount; TintR = r; TintG = g; TintB = b;
            return this;
        }

        // How many times the picture is read for one pixel. What a look costs.
        public int Taps { get { return 5 + (Glow > 0 ? ShaderLooks.GLOW_TAPS : 0); } }
    }

    // The looks a shader overlay can give the game, and the shader text for
    // each. Kept apart from anything that draws, so it can be tested.
    //
    // What a shader over a copy of the picture can do is change the picture:
    // sharpen it, colour it, shade it. What it cannot do is anything that
    // needs to know what the picture is of - how far away a pixel is, where
    // it was a frame ago. That is what DLSS, ray tracing and depth of field
    // need, and only the game has it.
    static class ShaderLooks
    {
        public const string OFF = "";
        public const string PRESET = "preset:";     // settings keep "preset:Vivid"
        public const string FILE = "file:";         // or "file:mine.hlsl", from the shaders folder

        public static readonly string[] StrengthNames = { "Gentle", "Normal", "Strong" };
        static readonly double[] StrengthBy = { 0.5, 1.0, 1.5 };

        public static readonly ShaderPreset[] Presets =
        {
            new ShaderPreset("Sharper", "Crisper edges and textures.", 0.8, 1, 0, 1, 0, 0),
            new ShaderPreset("Vivid", "Richer colours, a touch sharper.", 0.3, 1.12, 0.35, 1.03, 0, 0),
            new ShaderPreset("Sharper and vivid", "Both together.", 0.8, 1.1, 0.3, 1.04, 0, 0),
            new ShaderPreset("Warm evening", "Warmer light, soft dark corners.", 0.3, 1.05, 0.2, 1.05, 0.05, 0.25),
            new ShaderPreset("Cinema", "More contrast, calmer colours, dark corners.", 0.4, 0.94, 0.1, 1.12, 0.01, 0.35),
            new ShaderPreset("Cool and clean", "A cooler, cleaner picture.", 0.6, 1.02, 0.15, 1.04, -0.04, 0),
            new ShaderPreset("Black and white", "No colour at all.", 0.4, 0, 0, 1.08, 0, 0.2),

            // Saved by name, so new ones can go anywhere - but they go on the
            // end, where someone who knew the list finds the ones they knew.
            new ShaderPreset("Crisp HD", "As sharp as it goes, deeper darks.", 1.0, 1.04, 0.15, 1.02, 0, 0).WithDepth(0.35),
            new ShaderPreset("Soft glow", "Bright things spill a soft light.", 0.2, 1.05, 0.2, 1.02, 0, 0).WithGlow(0.55),
            new ShaderPreset("Golden hour", "Warm, glowing, low sun.", 0.3, 1.1, 0.25, 1.04, 0.07, 0.2).WithGlow(0.3),
            new ShaderPreset("Dreamy pastel", "Soft and pale, nothing quite black.", 0.1, 0.9, 0.3, 0.96, 0.015, 0).WithGlow(0.4).WithFade(0.14),
            new ShaderPreset("Deep shadows", "Darker darks, moodier.", 0.4, 1.03, 0.1, 1.04, 0, 0.15).WithDepth(0.7),
            new ShaderPreset("Comic book", "Flat colours, dark outlines.", 0.5, 1.15, 0.2, 1.05, 0, 0).WithLevels(6).WithOutline(0.7),
            new ShaderPreset("Old television", "Scan lines, grain, dark corners.", 0.2, 1.08, 0.1, 1.06, 0, 0.4).WithScanlines(0.3).WithGrain(0.03),
            new ShaderPreset("Old film", "Brown and grainy.", 0.3, 0.6, 0, 1.08, 0, 0.45).WithTint(0.75, 1.07, 0.95, 0.80).WithGrain(0.05).WithFade(0.06),
            new ShaderPreset("Night vision", "Green, grainy, bright in the dark.", 0.6, 1, 0, 1.15, 0, 0.5).WithTint(1, 0.35, 1.25, 0.40).WithGrain(0.06).WithScanlines(0.15)
        };

        // Eight more reads of the picture, in two rings. Enough for a soft
        // light; few enough to cost a fraction of what the game itself does.
        public const int GLOW_TAPS = 8;

        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RobloxKeeper", "shaders");
            }
        }

        public static ShaderPreset Find(string name)
        {
            foreach (ShaderPreset p in Presets)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // A preset at a strength: how far each thing is from "unchanged" is
        // scaled, so Gentle is half of what Normal does.
        public static ShaderPreset At(ShaderPreset p, int strength)
        {
            double k = StrengthBy[strength < 0 ? 0 : strength >= StrengthBy.Length ? StrengthBy.Length - 1 : strength];
            ShaderPreset at = new ShaderPreset(p.Name, p.Says,
                Clamp(p.Sharpen * k, 0, 1),
                // Black and white stays black and white however strong.
                p.Saturation == 0 ? 0 : Clamp(1 + (p.Saturation - 1) * k, 0, 2),
                Clamp(p.Vibrance * k, 0, 1),
                Clamp(1 + (p.Contrast - 1) * k, 0.5, 1.5),
                Clamp(p.Warmth * k, -0.15, 0.15),
                Clamp(p.Vignette * k, 0, 0.6));
            at.Glow = Clamp(p.Glow * k, 0, 1);
            at.Fade = Clamp(p.Fade * k, 0, 0.3);
            at.Depth = Clamp(p.Depth * k, 0, 1);
            at.Scanlines = Clamp(p.Scanlines * k, 0, 0.6);
            at.Grain = Clamp(p.Grain * k, 0, 0.12);
            at.Outline = Clamp(p.Outline * k, 0, 1);
            // Fewer steps of colour is the stronger look.
            at.Levels = p.Levels <= 0 ? 0 : Math.Max(3, (int)Math.Round(p.Levels / k));
            at.Tint = Clamp(p.Tint * k, 0, 1);
            at.TintR = p.TintR; at.TintG = p.TintG; at.TintB = p.TintB;
            return at;
        }

        static double Clamp(double v, double low, double high)
        {
            return v < low ? low : v > high ? high : v;
        }

        static string F(double v)
        {
            return v.ToString("0.0###", CultureInfo.InvariantCulture);
        }

        // What every shader is handed, and has to declare exactly as it is
        // here: the picture, how to find the game's part of it, and the size
        // of a pixel.
        public const string Header =
            "Texture2D src : register(t0);\n" +
            "SamplerState smp : register(s0);\n" +
            "cbuffer Params : register(b0)\n" +
            "{\n" +
            "    float2 uvOffset;   // where the game's picture starts inside the captured window\n" +
            "    float2 uvScale;    // how much of the captured window it is\n" +
            "    float2 texel;      // one pixel of the captured window\n" +
            "    float  sharpness;  // not used by the built-in looks\n" +
            "    float  saturation;\n" +
            "    float  vibrance;\n" +
            "    float  time;       // seconds since the overlay started\n" +
            "    float2 outSize;    // the game's size in pixels\n" +
            "};\n" +
            "float2 SrcUv(float2 uv) { return uvOffset + uv * uvScale; }\n" +
            "float3 Tap(float2 uv, float dx, float dy) { return src.SampleLevel(smp, uv + float2(dx, dy) * texel, 0).rgb; }\n";

        // The built-in shader, with one look's numbers written into it.
        public static string Source(ShaderPreset p)
        {
            StringBuilder s = new StringBuilder();
            s.Append("// RobloxKeeper built-in look: ").Append(p.Name).Append("\n");
            s.Append(Header);
            s.Append("static const float SHARPEN = ").Append(F(p.Sharpen)).Append(";\n");
            s.Append("static const float SATURATION = ").Append(F(p.Saturation)).Append(";\n");
            s.Append("static const float VIBRANCE = ").Append(F(p.Vibrance)).Append(";\n");
            s.Append("static const float CONTRAST = ").Append(F(p.Contrast)).Append(";\n");
            s.Append("static const float WARMTH = ").Append(F(p.Warmth)).Append(";\n");
            s.Append("static const float VIGNETTE = ").Append(F(p.Vignette)).Append(";\n");
            s.Append(
                "float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target\n" +
                "{\n" +
                "    float2 t = SrcUv(uv);\n" +
                "    // Sharpening that eases off where there is already contrast, so edges\n" +
                "    // get crisper without glowing: five taps, a cross.\n" +
                "    float3 a = Tap(t, 0, -1), b = Tap(t, -1, 0), c = Tap(t, 0, 0), d = Tap(t, 1, 0), e = Tap(t, 0, 1);\n" +
                "    float3 mn = min(c, min(min(a, b), min(d, e)));\n" +
                "    float3 mx = max(c, max(max(a, b), max(d, e)));\n" +
                "    float3 amp = sqrt(saturate(min(mn, 1.0 - mx) / max(mx, 1e-4)));\n" +
                "    float3 w = amp * (-1.0 / lerp(8.0, 5.0, saturate(SHARPEN))) * step(0.001, SHARPEN);\n" +
                "    float3 col = saturate(((a + b + d + e) * w + c) / (1.0 + 4.0 * w));\n");
            if (p.Outline > 0) s.Append(
                "    // A dark line along an edge, from the same five taps - drawn on the darker\n" +
                "    // side of it only, so thin bright things, like writing, are left to be read.\n" +
                "    float darker = dot((a + b + d + e) * 0.25 - c, float3(0.2126, 0.7152, 0.0722));\n" +
                "    col *= 1.0 - " + F(p.Outline) + " * saturate(darker * 5.0 - 0.08);\n");
            if (p.Glow > 0) s.Append(
                "    // Glow: the picture blurred from eight taps in two rings, and what is\n" +
                "    // bright in the blur added back - so lights spill, and dark things do not.\n" +
                "    float3 blur = (Tap(t, 2.5, 2.5) + Tap(t, -2.5, 2.5) + Tap(t, 2.5, -2.5) + Tap(t, -2.5, -2.5)\n" +
                "                 + Tap(t, 6, 0) + Tap(t, -6, 0) + Tap(t, 0, 6) + Tap(t, 0, -6)) / 8.0;\n" +
                "    float bright = max(blur.r, max(blur.g, blur.b));\n" +
                "    col += blur * smoothstep(0.55, 1.0, bright) * " + F(p.Glow * 0.6) + ";\n" +
                "    col = lerp(col, max(col, blur), " + F(p.Glow * 0.35) + ");\n");
            s.Append(
                "    // Colour: all of it, then the dull colours a little more.\n" +
                "    float luma = dot(col, float3(0.2126, 0.7152, 0.0722));\n" +
                "    col = lerp(luma.xxx, col, SATURATION);\n" +
                "    float sat = max(col.r, max(col.g, col.b)) - min(col.r, min(col.g, col.b));\n" +
                "    col = lerp(luma.xxx, col, 1.0 + VIBRANCE * (1.0 - sat) * step(0.001, SATURATION));\n" +
                "    col = (col - 0.5) * CONTRAST + 0.5;\n" +
                "    col.r += WARMTH; col.b -= WARMTH;\n");
            if (p.Depth > 0) s.Append(
                "    // An S-shaped curve: darks down, lights up, black and white left where they are.\n" +
                "    col = saturate(col);\n" +
                "    col = lerp(col, col * col * (3.0 - 2.0 * col), " + F(p.Depth) + ");\n");
            if (p.Tint > 0) s.Append(
                "    // Towards one colour, keeping how light each pixel is.\n" +
                "    col = lerp(col, dot(saturate(col), float3(0.2126, 0.7152, 0.0722)) * float3("
                    + F(p.TintR) + ", " + F(p.TintG) + ", " + F(p.TintB) + "), " + F(p.Tint) + ");\n");
            if (p.Levels > 0) s.Append(
                "    // So many steps of each colour and no more.\n" +
                "    col = floor(saturate(col) * " + F(p.Levels) + " + 0.5) / " + F(p.Levels) + ";\n");
            if (p.Fade > 0) s.Append(
                "    // Nothing quite black.\n" +
                "    col = col * " + F(1 - p.Fade) + " + " + F(p.Fade) + ";\n");
            if (p.Scanlines > 0) s.Append(
                "    // Every third row of the screen darker.\n" +
                "    col *= 1.0 - " + F(p.Scanlines) + " * (0.5 + 0.5 * cos(pos.y * 2.0944));\n");
            if (p.Grain > 0) s.Append(
                "    // Grain: a different speck for every pixel, changing a few times a second.\n" +
                "    // The count of frames is kept small: left to grow for hours it gets too big for\n" +
                "    // one pixel to be told from the next, and the specks turn into streaks.\n" +
                "    float speck = frac(sin(dot(pos.xy + fmod(floor(time * 24.0), 997.0) * 7.31, float2(12.9898, 78.233))) * 43758.5453);\n" +
                "    col += (speck - 0.5) * " + F(p.Grain * 2) + ";\n");
            s.Append(
                "    float2 fromMiddle = uv - 0.5;\n" +
                "    col *= 1.0 - VIGNETTE * smoothstep(0.35, 0.85, length(fromMiddle) * 1.2);\n" +
                "    return float4(saturate(col), 1.0);\n" +
                "}\n");
            return s.ToString();
        }

        // A starting point for someone writing their own, put in the shaders
        // folder the first time it is opened.
        public static string Example()
        {
            return "// Your own look for RobloxKeeper's shader overlay.\n"
                 + "// Save a copy under another name in this folder and it appears in the Looks page's list.\n"
                 + "// Keep everything down to SrcUv and Tap exactly as it is - that is what the overlay hands every shader -\n"
                 + "// and change what main does. It is run once for every pixel of the game, every frame.\n"
                 + "// A shader can only change the picture: nothing here knows how far away a pixel is, so there is no\n"
                 + "// depth of field, ambient occlusion or ray tracing to be had this way.\n\n"
                 + Header
                 + "float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target\n"
                 + "{\n"
                 + "    float3 col = Tap(SrcUv(uv), 0, 0);      // the game's own colour here\n"
                 + "    // A sepia tone, as an example. Replace these lines with your own.\n"
                 + "    float grey = dot(col, float3(0.299, 0.587, 0.114));\n"
                 + "    col = lerp(col, grey * float3(1.07, 0.95, 0.80), 0.6);\n"
                 + "    return float4(saturate(col), 1.0);\n"
                 + "}\n";
        }

        // The .hlsl files someone has put in the shaders folder, by name.
        // Ones this app writes for itself start with an underscore and are
        // not anyone's own.
        public static List<string> UserFiles(string folder)
        {
            List<string> names = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return names;
                foreach (string f in Directory.GetFiles(folder, "*.hlsl"))
                {
                    string name = Path.GetFileName(f);
                    if (!name.StartsWith("_") && !string.Equals(name, "example.hlsl", StringComparison.OrdinalIgnoreCase)) names.Add(name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return names;
        }

        public static bool IsOn(string chosen) { return !string.IsNullOrEmpty(chosen); }

        public static string Describe(string chosen)
        {
            if (!IsOn(chosen)) return "Off";
            if (chosen.StartsWith(PRESET)) return chosen.Substring(PRESET.Length);
            if (chosen.StartsWith(FILE)) return "Your own: " + chosen.Substring(FILE.Length);
            return "Off";
        }

        // The file the overlay is to be started with, written if it is a
        // built-in look. Null and why when there isn't one.
        public static string FileFor(string chosen, int strength, string folder, out string problem)
        {
            problem = null;
            try
            {
                if (chosen.StartsWith(PRESET))
                {
                    ShaderPreset p = Find(chosen.Substring(PRESET.Length));
                    if (p == null) { problem = "that look isn't available any more"; return null; }
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "_builtin.hlsl");
                    string text = Source(At(p, strength));
                    // Only written when it changes: the file's time is how
                    // a running overlay is known to be out of date.
                    if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
                    return path;
                }
                if (chosen.StartsWith(FILE))
                {
                    // A name, never a path: nothing outside the shaders folder is read.
                    string name = Path.GetFileName(chosen.Substring(FILE.Length));
                    string path = Path.Combine(folder, name);
                    if (name.Length == 0 || !File.Exists(path)) { problem = name + " is no longer in the shaders folder"; return null; }
                    return path;
                }
            }
            catch (Exception ex) { problem = ex.Message; return null; }
            problem = "no look is chosen";
            return null;
        }

        // What starts an overlay over one window.
        public static string Arguments(IntPtr hwnd, string shaderFile, int parentPid, string problemFile)
        {
            return "--overlay --hwnd " + hwnd.ToInt64().ToString(CultureInfo.InvariantCulture)
                 + " --parent " + parentPid.ToString(CultureInfo.InvariantCulture)
                 + " --shader \"" + shaderFile + "\" --problem \"" + problemFile + "\"";
        }
    }

    // Keeps one overlay going over each Roblox window while a look is on, and
    // none when it is off.
    //
    // Each overlay is its own process (see ShaderOverlay), so starting one is
    // starting a program and stopping one is ending it. Asked once a second
    // what should be running; it starts what is missing and ends what is not
    // wanted.
    class ShaderRunner
    {
        class Running
        {
            public Process Process;
            public IntPtr Hwnd;
            public string Shader;
            public DateTime ShaderTime;
            public string ProblemFile;
        }

        readonly Dictionary<int, Running> running = new Dictionary<int, Running>();
        readonly Dictionary<int, DateTime> failedAt = new Dictionary<int, DateTime>();

        // Starting an overlay, and whether one is still going: the real ones
        // unless a test puts its own in.
        public Func<string, Process> Start = StartReal;
        public Func<Process, bool> Alive = AliveReal;
        public Action<Process> Stop = StopReal;
        public string ProblemFolder = Path.GetTempPath();

        public int Count { get { return running.Count; } }

        // The last thing an overlay said went wrong, once.
        public string Problem;

        static Process StartReal(string arguments)
        {
            ProcessStartInfo si = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, arguments);
            si.UseShellExecute = false;
            si.CreateNoWindow = true;
            return Process.Start(si);
        }

        static void StopReal(Process p)
        {
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
            try { if (p != null) p.Dispose(); } catch { }
        }

        static bool AliveReal(Process p)
        {
            try { return p != null && !p.HasExited; }
            catch { return false; }
        }

        // windows: each Roblox client's game window by its process id.
        // shaderFile: what to draw them through, or null for nothing.
        public void Sync(IDictionary<int, IntPtr> windows, string shaderFile, DateTime now)
        {
            DateTime shaderTime = DateTime.MinValue;
            if (shaderFile != null)
                try { shaderTime = File.GetLastWriteTimeUtc(shaderFile); } catch { }

            // Ended: the client closed, its window changed, the look changed
            // or was edited, or the overlay stopped by itself.
            foreach (int pid in new List<int>(running.Keys))
            {
                Running r = running[pid];
                IntPtr hwnd;
                bool wanted = shaderFile != null && windows.TryGetValue(pid, out hwnd) && hwnd == r.Hwnd
                           && r.Shader == shaderFile && r.ShaderTime == shaderTime;
                bool alive = Alive(r.Process);
                if (wanted && alive) continue;
                // It ended while it was still wanted. Whether or not it
                // said why - a fault in graphics code says nothing - it is
                // not started again straight away, or one that cannot run
                // on this PC would be started and lost every second for ever.
                if (!alive && wanted)
                {
                    Problem = ReadProblem(r.ProblemFile) ?? "the overlay stopped by itself";
                    failedAt[pid] = now;
                }
                End(r);
                running.Remove(pid);
            }
            if (shaderFile == null) return;

            foreach (KeyValuePair<int, IntPtr> w in windows)
            {
                if (w.Value == IntPtr.Zero || running.ContainsKey(w.Key)) continue;
                // One that just failed is not started again every second:
                // a shader with a mistake in it would otherwise start and
                // fail for ever.
                DateTime failed;
                if (failedAt.TryGetValue(w.Key, out failed) && now - failed < TimeSpan.FromSeconds(30)) continue;

                Running r = new Running();
                r.Hwnd = w.Value;
                r.Shader = shaderFile;
                r.ShaderTime = shaderTime;
                r.ProblemFile = Path.Combine(ProblemFolder, "rk-shader-" + w.Key + ".txt");
                try { if (File.Exists(r.ProblemFile)) File.Delete(r.ProblemFile); } catch { }
                try
                {
                    r.Process = Start(ShaderLooks.Arguments(w.Value, shaderFile, Process.GetCurrentProcess().Id, r.ProblemFile));
                    if (r.Process != null) running[w.Key] = r;
                }
                catch (Exception ex) { Problem = "couldn't start it: " + ex.Message; failedAt[w.Key] = now; }
            }

            foreach (int pid in new List<int>(failedAt.Keys))
                if (!windows.ContainsKey(pid)) failedAt.Remove(pid);
        }

        // A look changed by hand - a file edited and saved - is tried again
        // at once rather than after the wait.
        public void TryAgain() { failedAt.Clear(); }

        public void StopAll()
        {
            foreach (Running r in running.Values) End(r);
            running.Clear();
            failedAt.Clear();
        }

        static string ReadProblem(string file)
        {
            try
            {
                if (file == null || !File.Exists(file)) return null;
                string why = File.ReadAllText(file).Trim();
                File.Delete(file);
                return why.Length == 0 ? null : why;
            }
            catch { return null; }
        }

        void End(Running r)
        {
            try { Stop(r.Process); } catch { }
            try { if (r.ProblemFile != null && File.Exists(r.ProblemFile)) File.Delete(r.ProblemFile); } catch { }
        }
    }
}
