# Shared assets

Place source artwork, design tokens, screenshots, and documentation diagrams here.
Runtime package images remain in `src/SeanShell.App/Assets`.

Regenerate every package and executable image from the reviewed SeanShell mark:

```powershell
.\tools\generate-brand-assets.ps1
```

The generated rounded terminal prompt is self-contained and remains legible on
light, dark, and transparent Windows surfaces down to the 16-pixel ICO frame.
Keep its package dimensions and filenames stable because the MSIX manifest
resolves scaled assets by convention.

Do not commit third-party assets without recording their source and license.
