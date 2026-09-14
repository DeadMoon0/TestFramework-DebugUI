# Theming

The window's appearance is data, not code. A theme names its colours and what to paint behind them, and
the controls read those names rather than spelling out a colour anywhere. That is what makes a
hand-written theme a first-class one: it goes through the same resolver the built-ins do.

## Picking a theme

Settings lists every theme it can find - the built-ins, and any file you have added - as chips, with
**Reload themes** next to them so a file being edited can be re-read without restarting. The choice is
remembered per user in `%USERPROFILE%\.testframework\DebugUI\settings.json`, and an unreadable or
missing setting falls back to `slate-dark`.

Twelve built-ins ship, as six light/dark pairs:

| Family | Ids | What it is |
| --- | --- | --- |
| Slate | `slate-dark`, `slate-light` | The default. Neutral greys, layered ridges behind. |
| Ember | `ember-dark`, `ember-light` | Warm, orange-accented. |
| Tide | `tide-dark`, `tide-light` | Cool blues and greens. |
| Glass | `glass-dark`, `glass-light` | See-through: the desktop shows through a tint. See below. |
| Contrast | `contrast-dark`, `contrast-light` | Maximum separation, minimum decoration. |
| Origin | `origin-dark`, `origin-light` | What the tool looked like before it had themes. |

## Writing your own

A theme file is a built-in with some things changed - not a full palette. **Open themes folder** in
Settings writes a commented example (once, if it is not already there) and opens the folder on it; that
example is the format's documentation, kept next to the thing it documents rather than only here.

Themes live in:

```
%USERPROFILE%\.testframework\DebugUI\themes\*.json
```

The file name is the theme's id, so `my-theme.json` is `my-theme`. A minimal one:

```jsonc
{
  "name": "My theme",
  "inherits": "slate-dark",

  "colours": {
    "Accent": "#FFE07A3F",
    "StateTimeout": "#FFE8C547"
  },

  "backdrop": {
    "recipe": "Orbits",
    "blur": 14
  }
}
```

Things worth knowing about the format:

- **It is sparse by design.** Anything you leave out is whatever `inherits` says now - including colours
  added by a later version of the tool. A file that listed every colour would start missing a key the day
  one more was added.
- **`colours` and `colors` are both accepted.** The alternative is a file that looks right, parses, loads
  and changes nothing, with no way to tell which spelling was wanted.
- **Colours are `#AARRGGBB` or `#RRGGBB`.**
- **`recipe` is one of a closed set** (below), matched without regard to case. A theme cannot hand the
  tool a drawing to execute - it chooses geometry, and the drawing stays compiled in.
- **Comments are allowed.** The reader accepts them, and the example ships with them.
- **`family`** is recorded and inherited, but nothing in the picker groups by it yet - setting it changes
  no behaviour today.

Your themes are marked as yours: a custom chip's tooltip names the file it came from.

A file that cannot be read does not take the tool down and does not vanish silently: it is reported, with
the reason, and the themes that did load are still offered.

### Backdrops

Every recipe is drawn from the same four palette entries - base, near, far and one glow - so a recipe
moved onto another theme comes out in that theme's colours rather than looking borrowed.

`Clear`, `Flat`, `Hexfield`, `Orbits`, `Scatter`, `Lattice`, `Arcs`, `Ridges`, `Dunes`, `Origin`.

`Clear` paints nothing at all: what shows through is the blur behind the window, tinted by `WindowTint`,
and whatever is on the desktop.

## See-through themes and when Windows refuses

A theme shows what is behind it when **both** `WindowTint` and `BackdropBase` are translucent - it is
derived from the palette rather than declared, so a theme cannot be wrong about itself. The Glass pair
qualifies, and so will any file you write that way.

Such a theme depends on the compositor blurring what is behind the window. Windows will sometimes not do
that, and **nothing fails when it doesn't** - the call succeeds, the window composites, and the blur is
quietly replaced by a flat fill of the tint. A see-through theme in that state is not a slightly flatter
version of itself; it is a window with the desktop showing through it sharply.

So the tool asks first. A theme that cannot be drawn is left in the picker but dimmed and unclickable,
with the reason in its tooltip - a theme that disappears reads as a theme that was removed:

| Reason | What to do |
| --- | --- |
| Energy saver is on | It switches off every compositor effect. Plug in, or turn it off. |
| Transparency effects are off in Windows | Settings → Personalisation → Colours → Transparency effects. |
| Windows will not say | Nothing; the theme is offered anyway. |

Both the answer and the reason fail open: if the answer cannot be got, the theme is offered. A theme that
looks flatter than intended is a smaller problem than a theme nobody can choose because a version of
Windows did not implement a flag.

If a glass theme has gone flat and the picker has not disabled it, energy saver is the first thing to
check - it is the one that takes effects away without appearing to change any setting.

## For contributors

- `TestFramework.DebugUI.State/Theming` holds the data: `ThemePalette` (the colour keys), `ThemeFile`
  (what a person writes), `ThemeResolver` (file plus base to a definition, or the reasons it could not),
  `ThemeStore` (the folder), `BuiltInThemes` (the twelve).
- `TestFramework.DebugUI/Theme` holds the application of it: `ThemeApplier`, `ThemeService`,
  `ThemeFollow`, and `ThemeKeys` - the names, so no lookup spells a resource string by hand.
- `BlurSupport` answers whether the compositor will blur, and separately why not.
- Adding a colour means adding it to `ThemePalette` and giving every built-in a value for it. Existing
  hand-written themes need no edit, which is the whole point of inheritance.
