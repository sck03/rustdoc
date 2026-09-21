# ExportDocManager report font policy

Formal PDF and print output uses the open-source Noto CJK fonts declared in
`font-manifest.json`. Run `node scripts/provision-report-fonts.mjs` before a
desktop or browser-server release build. The provisioner downloads the pinned
upstream files into this program-root directory and verifies every SHA-256.

The `.otf` files are deliberately not committed to Git. They are release
dependencies staged beside the program under `Resources/Fonts/OpenSource`;
they are never installed into Windows, macOS or Linux system font folders.

The report palette consists of three files, representing two font families:

| File | Use |
| --- | --- |
| `NotoSansCJKsc-Regular.otf` | Sans body text and tables, weight 400 |
| `NotoSansCJKsc-Bold.otf` | Bold headings, table headers and emphasized amounts, weight 700 |
| `NotoSerifCJKsc-Regular.otf` | Serif body text in customs, payment and expense forms, weight 400 |

Default templates and new V3 drafts use these families. Bold text in default
serif forms uses Sans Bold; there is no bundled Serif Bold face. Reference PDFs
may use other fonts, but migration preserves their layout using this palette.
Report output must not depend on system-font fallbacks. No unlisted font binary
may be copied into a package. `scripts/verify-font-license-policy.mjs` and package
payload checks verify the approved binaries and hashes.

Noto CJK is distributed under the SIL Open Font License 1.1. The complete
license is included as `OFL-Noto-CJK.txt`. It permits commercial use, bundling
with software and embedding in generated PDFs; retain the license notice.
