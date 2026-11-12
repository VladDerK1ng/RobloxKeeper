# Making mods for RobloxKeeper

Three things can be made, and none of them needs a programming tool:

1. **Mod packs** - pictures, sounds and fonts that replace Roblox's own.
2. **Shader looks** - a small piece of text that recolours or sharpens the game's picture.
3. **Macros as text** - keys and clicks written one line at a time.

Everything here works from outside the game. Nothing is put inside Roblox while it runs.


## First: why there is no Lua

Roblox games are written in Lua, so it is the first thing people ask for. Running your own Lua
inside a game you are playing means pushing code into the Roblox program while it runs. That is
what a "script executor" does. Roblox's anti-tamper exists to stop exactly that, it gets accounts
banned, and RobloxKeeper will never do it - it does not read or change Roblox's memory at all.

What you get instead:

- a **macro** plays keys and clicks, as you would, and can look at the screen for a picture;
- a **shader** changes the finished picture, after Roblox has drawn it;
- a **mod pack** swaps files on disk before Roblox starts.

None of these knows what is going on inside the game - where a player is, what an item is worth.
They see what you see and press what you press.

(If you want to write Lua that runs in a game, make your own game in Roblox Studio. That is what
Lua is for, and it is free.)


## 1. Mod packs

A mod pack is a folder, or a zip of one, laid out the way Roblox's own folder is. Whatever is
in the pack replaces the file with the same name and place in Roblox.

### Try a ready-made one first

**Looks > Mod packs > Add a pack** lists pointers that come with the app:

| Pack | What it is |
| --- | --- |
| Crosshair pointer | A thin white cross with a gap in the middle |
| Green crosshair pointer | The same in bright green |
| Dot pointer | One small white dot |
| Red dot pointer | The dot in red |
| Ring pointer | A ring with a dot in it |
| Scope pointer | A ring, a cross and a red dot |
| Big bright pointer | A large yellow arrow that is hard to lose |

Pick one, press **Apply packs**, then close and reopen Roblox. Only one pointer can show, so
picking another unticks the one before. A game that sets its own pointer keeps its own.

They are drawn by the app when you add them. After adding one, open
`%LOCALAPPDATA%\RobloxKeeper\mods` to see what a finished pack looks like.

### Make your own

1. Make a folder and give it the pack's name, say `My pointer`.
2. Inside it, make the same folders Roblox has, down to the file you want to replace.
3. Put your file there, with exactly the name Roblox uses.
4. **Add a pack > From a folder...**, choose `My pointer`, then **Apply packs**.

A pack that replaces the mouse pointer looks like this:

```
My pointer
  content
    textures
      Cursors
        KeyboardMouse
          ArrowCursor.png        the pointer            64 x 64
          ArrowFarCursor.png     the pointer, far off   64 x 64
      MouseLockedCursor.png      shown in shift lock    32 x 32
```

For the pointer pictures: PNG with a see-through background, and **the middle of the picture is
the point that clicks**. Draw your crosshair around the middle, not in the top-left corner.

To find what else can be replaced, open Roblox's folder and look - every picture, sound and font
the Roblox program itself uses is a plain file:

```
%LOCALAPPDATA%\Roblox\Versions\version-...\content\textures     pictures
%LOCALAPPDATA%\Roblox\Versions\version-...\content\sounds       sounds
%LOCALAPPDATA%\Roblox\Versions\version-...\content\fonts        fonts
%LOCALAPPDATA%\Roblox\Versions\version-...\content\sky          the default sky
```

Copy the file you want to change somewhere else, edit the copy, keep its name and size, and
put it in your pack at the same place. Never edit the file in Roblox's folder itself - let the
pack do it, so it can be undone.

### The rules a pack has to keep

- Only files inside `content`, `ExtraContent` or `PlatformContent`.
- No programs or scripts: `exe dll bat cmd ps1 vbs js msi scr com lnk` are refused.
- Nothing in `ClientSettings` - that is Roblox's settings, which the Graphics card handles.
- Real files only, no shortcuts or links.

A pack that breaks a rule is not added at all, and the app says which file and why.

### What a pack can and cannot change

It changes what the Roblox program draws and plays by itself: the pointer, built-in sounds,
menu fonts, some built-in textures. It does **not** change things a game brings with it - a
game's own map, characters, sounds and menus come from Roblox's servers, not from these files.

### Undoing it

Before a file is replaced, the original is saved. **Put Roblox back** on the Looks page restores
every original and removes everything a pack added. When Roblox updates itself, the app puts
the packs you last pressed **Apply packs** for into the new version. A pack that is only
ticked waits until you press it.

### Sharing a pack

Zip the folder and send the zip. The other person uses **Add a pack > From a zip file...**.
Packs made for Bloxstrap have the same layout and work as they are.

Only share what is yours to share: your own drawings and sounds, not somebody else's.


## 2. Shader looks

A shader look is run once for every pixel of the game's picture, every frame. It is given the
colour Roblox drew and gives back the colour to show.

### The ones that come with the app

**Looks > Shaders > Look**. Each has three strengths: Gentle, Normal, Strong.

| Look | What it does |
| --- | --- |
| Sharper | Crisper edges and textures |
| Vivid | Richer colours, a touch sharper |
| Sharper and vivid | Both together |
| Warm evening | Warmer light, soft dark corners |
| Cinema | More contrast, calmer colours, dark corners |
| Cool and clean | A cooler, cleaner picture |
| Black and white | No colour at all |
| Crisp HD | As sharp as it goes, deeper darks |
| Soft glow | Bright things spill a soft light |
| Golden hour | Warm, glowing, low sun |
| Dreamy pastel | Soft and pale, nothing quite black |
| Deep shadows | Darker darks, moodier |
| Comic book | Flat colours, dark outlines |
| Old television | Scan lines, grain, dark corners |
| Old film | Brown and grainy |
| Night vision | Green, grainy, bright in the dark |

### Write your own

1. **Looks > Shaders > Make my own...** opens the shaders folder. `example.hlsl` is in it.
2. Copy `example.hlsl` under another name - `mine.hlsl` - and open the copy in Notepad.
3. Change what `main` does, and save.
4. Go to another page and back to Looks. It is in the list as **Your own: mine.hlsl**.

If there is a mistake in it, the Looks page shows the error instead of the look.

The top of the file - everything down to `SrcUv` and `Tap` - has to stay exactly as it is.
That is what the app hands every shader:

| Name | What it is |
| --- | --- |
| `uv` | Where this pixel is: 0,0 is the top-left of the game, 1,1 the bottom-right |
| `pos.xy` | The same, counted in pixels |
| `SrcUv(uv)` | Turns `uv` into the place to read the game's picture from |
| `Tap(t, dx, dy)` | The game's colour at `t`, moved `dx` pixels right and `dy` down |
| `time` | Seconds since the overlay started over this window |
| `outSize` | The game's size in pixels |
| `texel` | One pixel, as a step in the units `SrcUv` gives. `Tap` already uses it: `Tap(t, 1, 0)` is the pixel to the right |

The rest of what is declared up there you can leave alone: `uvOffset` and `uvScale` are what
`SrcUv` uses, and `sharpness`, `saturation` and `vibrance` are numbers the app passes that the
built-in looks do not read.

A colour is three numbers from 0 to 1: red, green, blue. `float3(1, 0, 0)` is pure red.

The smallest shader that does something - it leaves the picture alone:

```hlsl
float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float3 col = Tap(SrcUv(uv), 0, 0);
    return float4(saturate(col), 1.0);
}
```

Put lines between those two to change `col`. Some to copy - each works by itself, and they can
be stacked:

```hlsl
// Brighter. More than 1 is brighter, less is darker.
col *= 1.15;

// More contrast.
col = (col - 0.5) * 1.2 + 0.5;

// Less colour. 0 is black and white, 1 is unchanged, more than 1 is richer.
float grey = dot(col, float3(0.2126, 0.7152, 0.0722));
col = lerp(grey.xxx, col, 0.5);

// Warmer. Swap the + and - for cooler.
col.r += 0.05; col.b -= 0.05;

// A tint: towards purple, a third of the way.
float light = dot(col, float3(0.2126, 0.7152, 0.0722));
col = lerp(col, light * float3(0.8, 0.5, 1.0), 0.33);

// Dark corners.
col *= 1.0 - 0.4 * smoothstep(0.35, 0.85, length(uv - 0.5) * 1.2);

// Upside down, because you can.
col = Tap(SrcUv(float2(uv.x, 1.0 - uv.y)), 0, 0);

// A slow pulse of brightness.
col *= 1.0 + 0.05 * sin(time * 2.0);
```

Keep it light: every `Tap` is the picture read once more, for every pixel, every frame. The
built-in looks use 5 taps, and 13 for the ones that glow. A shader with a loop of a hundred taps
will make the game stutter.

### What a shader cannot do

It only has the finished picture. It does not know how far away anything is, or what moved
since the last frame. So there is **no DLSS, no ray tracing, no depth of field and no ambient
occlusion** this way, and no shader pack that claims them is doing the real thing. Shaders made
for ReShade that need "depth" will not work here for the same reason.

It also only shows while the game is the window in front, and it draws a copy over the game -
on a slow PC it can cost a few frames a second. Switch the look to Off to get them back.

### Sharing a look

Send the `.hlsl` file. The other person puts it in their shaders folder.
An `.hlsl` file is text that draws colours; it cannot touch files or the internet.
It can still be badly made: one that reads the picture hundreds of times a pixel will make the
game stutter, or freeze the screen for a moment. If a look someone sent you does that, set
**Look** to Off.


## 3. Macros as text

Every macro can be written as text: **Automation > Macros > Add macro > Write it as text**.
It is the same macro as the one made with the buttons - write it either way and switch between
them whenever you like.

One step on each line:

```
repeat forever
    click picture "Buy button" wait 3
    press E
    wait 0.5
end
```

### Every line it knows

Places are how far across and how far down the game window, each from 0 to 100 - so `50 50`
is the middle, whatever size the window is. Times are seconds.

| Line | What it does |
| --- | --- |
| `press E` | Press a key and let go |
| `press Space for 0.5` | Hold it for half a second, then let go |
| `hold W` | Hold a key down, and keep holding |
| `release W` | Let go of a key held with `hold` |
| `wait 2` | Do nothing for 2 seconds - 60 at most; for longer, repeat a wait |
| `click 50 50` | Click the middle of the window |
| `right click 20 80` | The same with the right button |
| `click here` | Click wherever the pointer already is |
| `click picture "Buy"` | Look for the picture called Buy and click it where it is |
| `click picture "Buy" wait 3` | Give it up to 3 seconds to appear |
| `click picture "Buy" wait 3 or stop` | ...and stop the macro if it never does |
| `wait for "Loading" gone up to 20` | Wait until the picture has gone, 20 seconds at most |
| `wait for "Shop" up to 10 or stop` | Wait until it shows; stop the macro if it doesn't |
| `move 30 40` | Move the pointer there |
| `drag 40 50 to 60 50 over 0.5` | Hold the left button and drag, taking half a second |
| `right drag 40 50 to 60 50 over 0.5` | The same with the right button - turns the camera |
| `scroll up 3` | Three clicks of the wheel, where the pointer is |
| `scroll down 3 at 50 50` | ...at a place |
| `type "hello"` | Type the letters |
| `say "hello"` | Open the chat, type it, send it |
| `repeat 5` ... `end` | Do the lines in between 5 times |
| `repeat forever` ... `end` | ...until you press the stop key (F10 unless you changed it) |
| `repeat until "Full" shows` ... `end` | ...until a picture appears |
| `repeat until "Full" gone` ... `end` | ...until a picture has gone |

Extras for any line with a picture:

| Extra | What it does |
| --- | --- |
| `alike 90` | How alike it has to be, 50 to 100. Higher is stricter. 82 if you don't say |
| `in "Shop corner"` | Only look inside a box you drew for that game |
| `offset 20 -5` | (click only) Click 20 pixels right of its middle and 5 above |

Keys are written as they are printed on the keyboard: `E`, `5`, `Space`, `Enter`, `Shift`,
`Ctrl`, `Tab`, `Esc`, `F5`, `Left arrow`, `Up arrow`, `[`, `]`, `;`, `,`. The mouse's buttons
are not keys: use `click`.

A line starting with `#` is a note to yourself and is skipped. Capital letters don't matter.
Lines inside a repeat are easier to read moved in, but they don't have to be.

### Also understood

The same things said another way, and one the app writes itself:

| Line | The same as |
| --- | --- |
| `tap E` | `press E` |
| `hold W for 2` | `press W for 2` - held for 2 seconds, then let go |
| `press key 232` | A key that has no name, by its Windows number. The app writes this for such a key |
| `wait until "Shop" shows up to 10` | `wait for "Shop" up to 10` |
| `move to 30 40` | `move 30 40` |
| `repeat 5 times` | `repeat 5` |
| `end repeat` | `end` |
| `// a note` | `# a note` |

A colon after a repeat - `repeat forever:` - is allowed too.

### Pictures

A picture is named in the text, but it is not *in* the text. Cut it first: add a
**Click a picture** step with the buttons, cut the picture from the game, and give it a name.
After that the name works in any macro on this PC.

If you send a macro to a friend, they have to cut their own pictures and give them the same
names. Until they do, the app says which name it couldn't find. The same goes for a box named
with `in`: it is drawn on your PC, for your game, and they have to draw their own.

### When a line is wrong

Nothing is kept until every line is understood. The app says which line and what it expected:

```
Line 3: there is no key called "Spcae". Write it the way it is printed on the keyboard...
```

### Some to start from

Jump every five seconds so a game doesn't think you have left:

```
repeat forever
    press Space
    wait 5
end
```

Click a button whenever it turns up, and wait while it isn't there:

```
repeat forever
    click picture "Collect" wait 30
    wait 1
end
```

Walk forward for three seconds, turn the camera, walk back:

```
hold W
wait 3
release W
right drag 45 50 to 75 50 over 0.4
hold W
wait 3
release W
```

Buy something three times, and give up if the shop never opens:

```
press E
wait for "Shop title" up to 5 or stop
repeat 3
    click picture "Buy button" wait 2
    wait 0.5
end
press Esc
```

Say something in chat every minute:

```
repeat forever
    say "selling pets, come to my plot"
    wait 60
end
```

### What a macro can and cannot do

Roblox only listens to keys and clicks in the window that is in front, so a macro brings its
client to the front while it plays. A macro that repeats forever lets go in between rounds, so
several clients can take turns.

A macro that is not in a repeat has to be under a minute. To go on for longer, put the steps
inside `repeat forever`.

Roblox's Terms of Use do not allow automation, and some games forbid it in their own rules as
well. A macro here only presses keys and clicks from outside the game, but that does not make
it allowed. Use macros at your own risk, as the README says of the whole app.
