# PulsR TMS brand design

This is the authoritative visual identity guide for PulsR TMS. Follow it for new
product UI and changes to existing branded surfaces. It complements the required
[UI controls](../ui-controls.md) and
[style architecture](../architecture/styles.md); it does not replace their
component, accessibility or token contracts.

## Name and selected direction

Use **PulsR TMS** as the full display name and **PulsR** as its short form.
Preserve the capital P and final R. The canonical technical slug is `pulsartms`,
not a replacement spelling for the wordmark. Repository, configuration and
compatibility rules belong in
[product naming](../development/product-branding.md).

The selected direction is **Pulse**: five rounded vertical bars, tallest in the
center, preceding a bold geometric PulsR wordmark. A smaller TMS descriptor sits
beside the name on the same baseline. The selected red variant blends red into coral. The
reversed version keeps that pulse and uses white lettering on navy. The square
icon contains the same pulse on white, with no letter. These are one identity,
not interchangeable logo proposals. This replaces the earlier Signature R.

The production SVG is a vector adaptation of the selected reference, not a claim
of exact pixel reconstruction or a particular commercial typeface. Its letter
shapes, spacing and descriptor placement are artwork. Do not recreate the logo
with text, a similar font, emoji, a generic P or an unrelated R icon.

## Shared assets and ownership

- [Wordmark artwork](../../Client/wwwroot/brand/pulsr.svg) owns the reusable SVG
  symbols and letter geometry.
- [BrandLogo](../../Client/Shared/Brand/BrandLogo/BrandLogo.razor) is the shared
  application component. Its Razor/code-behind pair stays in
  `Client/Shared/Brand/BrandLogo/`. Login, navigation and other product surfaces
  reuse it rather than copying paths or building their own text lockup.
- [Favicon](../../Client/wwwroot/favicon.svg) owns the square pulse icon used by
  the browser. Its `pulse` path, stroke and gradient match the wordmark asset;
  only placement and the solid white backing differ. Regression checks preserve
  that shared geometry.
- [Visual brand guide](../../Client/wwwroot/brand/index.html), served at
  `/brand/index.html`, presents the same assets with the compiled UI tokens.
  It is a reference surface, not another theme or a source of page-specific CSS.

Use the external SVG symbols through the shared component. Do not introduce
another font, CDN, image-generation dependency or duplicated inline wordmark.
Page and layout styles place the component; reusable brand styling belongs in
the components layer and consumes the public `base` token API.

## Light, reversed and compact use

Use the ink wordmark and colored pulse on white or quiet light surfaces. Use
white lettering and the same colored pulse on dark navigation and fixed dark
brand panels. Theme-aware sign-in resolves all lettering through the existing
`text` role, becoming light in dark mode. The pulse retains its static SVG
gradient. Do not put fixed dark-ink artwork on dark surfaces or color the final
R independently of the other letters.
Use a quiet solid backing when a photograph or map would otherwise interfere;
do not add branding over operational map data merely to decorate it.

Keep at least half the main wordmark's capital-letter height clear around the
complete lockup, including TMS. Preserve its aspect ratio and internal spacing.
Allow that clear space within the existing layout padding instead of increasing
spacing throughout the application.

A compact full wordmark should normally be at least about 112 CSS pixels wide at
the default scale. Scale it uniformly and check the TMS descriptor at the actual
rendered size. If the space is too small for a legible lockup, use the supplied
pulse icon instead of compressing the lettering. The icon is intended for small
16–32 pixel uses, with its built-in padding preserved. Larger identity
placements may scale the artwork; small artwork does not reduce a control's
touch target.

Give a meaningful logo the accessible name `PulsR TMS`. A decorative duplicate
beside an already named identity can be hidden from assistive technology. Do not
make a static logo focusable; if it is a navigation link, preserve the link's
accessible name and visible keyboard focus.

Never stretch, skew, rotate, outline, redraw or animate the letter shapes. Do
not move TMS below the wordmark, change its tracking, add a second descriptor or
surround every instance with a separate badge. Avoid glow, bevels and decorative
shadows on the mark.

## Palette and operational meaning

The identity adds two artwork colors while preserving the existing UI palette.

| Role | Reference value | Application use |
| --- | --- | --- |
| Pulse red | `#ec354b` | Pulse artwork and `pulse, 500` reference swatch |
| Pulse coral | `#ff6371` | Pulse gradient and `pulse, 400` reference swatch |
| Ink | `#172438` | Light-theme `text`; dark wordmark and primary information |
| Navigation navy | `#111e33` | Existing `navigation` background |
| White | `#ffffff` | Light `surface` and reversed lettering |
| Quiet canvas | `#f5f7fb` | Light `canvas` and `surface-soft` backgrounds |
| Primary action | `#2850d9` | Existing accessible filled `action` role |

Keep the darker primary action color and its existing foreground/hover roles.
Do not substitute the red/coral artwork colors for action, body-text or link roles
merely to make everything match. Dark mode continues to resolve through its
existing semantic theme map; the reference values above are not hardcoded dark
theme overrides.

Palette literals belong in `base/_colors.scss` and deliberate static SVG art,
not page, layout or component SCSS. Consume `ui.theme(brand)`, `ui.theme(text)`,
`ui.theme(surface)`, `ui.theme(canvas)` and the appropriate existing semantic
role.
Do not change shared role values as a side effect of adding a logo.

Fuel prices, favorable/unfavorable changes, pickup/delivery, routes, truck
telemetry, HOS and warnings retain their established colors and meanings. The
brand must not recolor those signals. A red pulse does not indicate an error.

## Product presentation

Keep the interface compact, readable and calm: clear grouping, normal
whitespace, quiet dividers and existing card elevation. Accents may appear
subtly in navigation and sign-in composition. This direction does not call for an
all-dark application, neon theme, large gradients, glow behind data or oversized
marketing panels inside Dispatch and Fleet Map.

Retain the current UI font stack, named typography, spacing, radii and control
sizes. The logo's geometric lettering is not a new body font. Do not load a
remote font or increase density by shrinking body text, fields or touch targets.
Responsive layouts and increased text size must retain visible information and
keyboard access; the artwork scales independently of data typography.

Product branding is separate from account and carrier information. Preserve the
authenticated user's name and role, AMF Carrier's business identity, truck and
trailer labels, load prefixes and imported records. Do not replace an account
avatar, carrier logo or operational marker with the pulse unless that surface
actually identifies PulsR TMS.

For future identity changes, update this guide and the shared assets/component
together. Select the affected checks using [test selection](../testing.md), and
keep dated verification evidence in the archive rather than in the brand rules.
