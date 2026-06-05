# OopsType — Custom Icon Brief (for the SVG / icon agent)

This document specifies the **bespoke brand art** OopsType needs that can't be pulled from a
standard icon set — primarily the **brand mark**, the app **tile**, and the raster/`.ico` files
derived from them. Every *functional* icon in the UI (checkmark, add, settings, etc.) is already
handled in-app by the **WPF‑UI / Fluent System Icons** font (`ui:SymbolIcon`); those are **not**
part of this request.

Deliver **SVG masters plus the noted PNG/ICO exports**. WPF cannot render SVG directly — the
**PNG/ICO are what actually ship and get loaded at runtime** (via `pack://` URIs); the SVG is the
editable master. After you hand them back, the files are dropped into `Assets\` (replacing the
four placeholder files listed in §5) and the app picks them up with **zero code changes**.

---

## 1. What OopsType is (so the mark fits the product)

OopsType is a lightweight **Windows tray utility** with one job: stop you typing in the wrong
keyboard layout. It gives you an **unmissable, glanceable indication of your active layout**
(`EN`, `עב`, `РУ`, `ΕΛ`, `日本`) exactly where your eyes already are — a tiny chip next to your
text **caret**, a chip that follows your **mouse**, and a colored **strip along the taskbar**.
Tagline: **"Stop typing in the wrong language."**

It is a **tray-first app** — there is no main window; it lives in the notification area. That makes
the **tray / taskbar icon the single most-seen brand surface**, so the mark **must be crisp and
unmistakable at 16 px**. The vibe: **friendly, precise, modern, multilingual** — a calm helper that
catches your mistakes, not a loud or techy logo. The name "OopsType" is playful ("oops" = a caught
typo), so a subtle hint of friendliness is welcome, but the silhouette comes first.

## 2. Visual language (match the app)

OopsType is a **flat, modern, LIGHT‑themed** WPF app built on the **WPF‑UI (Fluent / Mica)** design
system — white cards on a near-white canvas, a single blue accent. The brand should sit naturally in
that light UI **and** pop in the Windows tray/taskbar. The cleanest way is a **self-contained tile**
(mark on its own blue gradient) used everywhere; the same tile reads on the light sidebar, the white
About card, and the system tray.

Use these exact tokens (pulled from the app's palette):

| Token               | Hex        | Use                                                     |
|---------------------|------------|---------------------------------------------------------|
| Brand blue (accent) | `#2563EB`  | primary brand color / gradient mid (the app's accent)   |
| Brand blue deep     | `#1D4ED8`  | gradient end / shading                                  |
| Brand blue light    | `#3B82F6`  | gradient start / highlight                              |
| Indigo (optional)   | `#4F46E5`  | optional cooler accent in the gradient                  |
| Foreground / glyph  | `#FFFFFF`  | the letter/caret on the tile — white for max contrast   |
| Text dark           | `#1F2937`  | wordmark text on light bg                               |
| Subtle text         | `#6B7280`  | secondary text                                          |
| Light canvas        | `#F8FAFC`  | app background (light theme)                            |
| Surface             | `#FFFFFF`  | cards                                                   |
| Border              | `#E5E7EB`  | hairline borders                                        |
| Caret / accent pop  | `#F59E0B`  | optional warm caret-blink accent (amber) for energy     |

Recommended tile background: a diagonal **`#3B82F6` → `#1D4ED8`** gradient (top-left → bottom-right),
with `#2563EB` as the canonical solid-color fallback so it matches the running UI accent exactly.

Style rules:
- Flat, geometric, **generous rounded corners**. No skeuomorphism, no glassy/3D bevels, no heavy
  outlines. (The current placeholder is a glossy glass tile — deliberately move **away** from that.)
- Must stay legible at **16 px** — this is the hard constraint (it's a tray app). Keep interior
  detail minimal; **the silhouette is the brand**. Test every concept mentally at 16 px first.
- Friendly and precise: clean letterforms, one clear focal glyph, optional single accent. No clutter.

## 3. Concept direction (primary + alternates)

The heart of the product is the **little layout chip** it paints next to your caret. Reusing that as
the brand mark gives perfect product↔brand cohesion (the logo literally *is* the thing the app draws).

**Primary — "Caret chip":** a friendly **rounded-square chip** (the same chip metaphor the app shows
by your caret) on the blue gradient, containing a single **bold white letterform** with a **blinking
text‑caret bar** beside it. The caret (a vertical I‑beam `|`) is the signature element — it ties the
mark to the caret-label feature and instantly reads as "where you type / your cursor." Keep the
letter neutral and ownable (a clean bold **"A"**, or an abstract type-block) so it isn't tied to one
language. Optionally tint the caret bar amber (`#F59E0B`) so it "blinks" with a touch of warmth.

Please also sketch **two alternates** so we can choose:
- **B — "Script swap" (most on-message):** two letterforms from **different scripts** facing each
  other — e.g. Latin **"A"** and Hebrew **"א"** (or Cyrillic **"Я"**) — joined by a small circular
  **swap / switch** motif (two short curved arrows, or one glyph morphing into the other). This most
  directly says *"switch language"* — the literal product. Risk: busier; simplify hard for 16 px.
- **C — "Keycap + caret" (most timeless, best at 16 px):** a single **keyboard keycap** (rounded
  square with a subtle top face) bearing a **caret/I‑beam** or a minimal letter. Reads instantly as
  "keyboard," survives 16 px best, and is the safest scalable fallback. Keep this even if we pick A
  or B — it's the candidate for the tiny tray frames if the others get muddy.

Lean **minimal** in all cases, all on the blue gradient with a white glyph.

---

## 4. Deliverables

> File naming below uses the heading `id` as the base filename. Backgrounds transparent unless it's a
> tile. The four **bold-named files in §4.4** are the exact filenames the app loads — please deliver
> those names so the swap is drop-in.

### 4.1 `oopstype-tile` — mark on a rounded-square app tile  ⭐ the in-app badge & icon source
This is the master brand surface — the source for **every** raster below.
- Format: **SVG**, square `viewBox="0 0 512 512"`.
- A **rounded-square tile** (corner radius ≈ **22 %** of the side) filled with the diagonal brand
  gradient `#3B82F6`→`#1D4ED8`, mark centred in **white** (plus optional amber caret) so it pops.
- Generous internal padding (~14 % inset) so it survives a further OS rounded-corner mask.
- Must look right both on the **light** app canvas (`#F8FAFC`, white cards) and on the Windows
  tray/taskbar (light or dark). White-on-blue achieves this on its own — don't rely on page color.

### 4.2 `oopstype-logo` — full-color master mark (bare, transparent)  ⭐ most important master
The "hero" mark for README / About / flexible placement, without the tile background.
- Format: **SVG**, square `viewBox="0 0 512 512"`, **transparent** background.
- Full color allowed: the brand gradient, multiple paths, one soft inner shade.
- Must read on its own on a light background (give the mark self-contained contrast; don't depend on
  the page). This is the editable source the tile is built from.

### 4.3 `oopstype-glyph` — single-path monochrome mark (optional but nice)
A tintable one-color version, for any monochrome context (a future tray-mono mode, an About line, a
favicon).
- Format: **SVG**, `viewBox="0 0 24 24"`, **exactly one `<path>`**, solid fill, `fill-rule="evenodd"`
  for any cut-outs (the caret/letter counters). No `<g>`, gradients, strokes, or filters — it must
  convert cleanly to a WPF geometry. Centred, ~1 px padding inside the 24-grid. Should read re-tinted
  to accent blue or white.

### 4.4 Raster exports the app actually loads  ⭐ ship these exact filenames
Built from **`oopstype-tile`**. These four filenames replace the current placeholders in `Assets\`
(see §5) so the swap needs **no code change**:

- **`logo.ico`** — multi-resolution `.ico` containing **16, 24, 32, 48, 64, 128, 256** px. This is
  the workhorse: the **.exe icon**, the **tray icon**, the **settings-window icon**, and the
  **installer icon** all load it. **Hand-tune the 16 / 24 px frames** — drop fine detail so the mark
  stays crisp (if the chosen concept gets muddy at 16 px, fall back to concept **C** / a single
  caret-or-letter for those small frames).
- **`logo-64.ico`** — 64 px icon used for the settings-window **title-bar** icon (keep it sharp at 64).
- **`logo-128.ico`** — 128 px icon (high-DPI / spare).
- **`logo.png`** — square **transparent** PNG of the tile (suggest **512 px**, min 256). Shown in the
  app at 40×40 on the light sidebar header and in the About card — so it must look clean small on
  white.

### 4.5 Extra PNG exports (recommended, for README / store / high-DPI)
From `oopstype-tile` (transparent): `oopstype-256.png`, `oopstype-128.png`, `oopstype-64.png`,
`oopstype-48.png`, `oopstype-32.png`.

### 4.6 `oopstype-wordmark` — logo + "OopsType" lockup (optional)
For the README hero / About box: the mark to the left of the **"OopsType"** wordmark. Format: **SVG**,
transparent; text in `#1F2937` for light backgrounds.

---

## 5. How each asset gets wired in (FYI — kept here so the names match reality)

> **Note:** WPF doesn't render SVG natively, so the **PNG/ICO are the files actually consumed**; the
> SVGs are the editable masters. All four raster files live in the existing **`Assets\`** folder and
> are registered as `<Resource>` in [`OopsType.csproj`](../OopsType.csproj), so `pack://` URIs work at
> runtime. Replacing the files and rebuilding is the entire integration.

- **`logo.ico`** →
  - `<ApplicationIcon>Assets\logo.ico</ApplicationIcon>` in [`OopsType.csproj`](../OopsType.csproj) — the .exe shell icon (taskbar, Alt-Tab, Explorer).
  - `Icon="pack://application:,,,/Assets/logo.ico"` on the settings [`SettingsWindow.xaml`](../Views/SettingsWindow.xaml).
  - The **tray** `NotifyIcon` loads it via `pack://...,/Assets/logo.ico` in [`TrayPresenter.cs`](../Services/TrayPresenter.cs) — **the most-seen surface; this is why 16 px matters most.**
  - `SetupIconFile=...\Assets\logo.ico` in the installer [`OopsType.iss`](../Installer/OopsType.iss).
- **`logo-64.ico`** → the `ui:TitleBar` icon in [`SettingsWindow.xaml`](../Views/SettingsWindow.xaml).
- **`logo-128.ico`** → registered as a `<Resource>` for high-DPI / future use.
- **`logo.png`** → the sidebar header badge (40×40) and the About card image (40×40) in [`SettingsWindow.xaml`](../Views/SettingsWindow.xaml).
- **`oopstype-logo` / `oopstype-wordmark`** → README header and future marketing.

## 6. Output checklist

- [ ] `oopstype-tile.svg` (gradient rounded-square tile, white glyph) — **master surface**
- [ ] `oopstype-logo.svg` (bare color mark, 512, transparent) — **master**
- [ ] `logo.ico` (16/24/32/48/64/128/256, from the tile, 16/24 hand-tuned) — **app loads this**
- [ ] `logo-64.ico` · `logo-128.ico` — **app loads these**
- [ ] `logo.png` (≥256, ideally 512, transparent) — **app loads this**
- [ ] `oopstype-256/128/64/48/32.png` (transparent) — README / high-DPI
- [ ] (optional) `oopstype-glyph.svg` (1 path, 24×24, evenodd, monochrome)
- [ ] (optional) `oopstype-wordmark.svg`
- [ ] Three concept sketches (A caret-chip / B script-swap / C keycap) shown at 16 px before finalizing
</content>
</invoke>
