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

The manifest also provisions and packages `NotoSansCJKsc-Medium.otf` (weight
500). As of 2026-11-06, React registers real Sans 400/500/700 faces and Serif
400 on demand. The provisioner converts all four pinned OTF files to WOFF2
using locked `wawoff2@2.0.1`; each generated file has a manifest SHA-256 and
its decompressed font tables are checked by `scripts/test_web_fonts.mjs`.
The browser uses WOFF2; Rust PDF output retains OTF. No glyphs are removed.
The four web resources total 51,958,224 bytes versus 74,506,324 OTF bytes
(30.26% less); this is not a measurement of the complete application package.

Default templates and new V3 drafts use these families. Bold text in default
serif forms uses Sans Bold; there is no bundled Serif Bold face. Reference PDFs
may use other fonts, but migration preserves their layout using this palette.
Report output must not depend on system-font fallbacks. No unlisted font binary
may be copied into a package. `scripts/verify-font-license-policy.mjs` and package
payload checks verify the approved binaries and hashes.

Noto CJK is distributed under the SIL Open Font License 1.1. The complete
license is included as `OFL-Noto-CJK.txt`. It permits commercial use, bundling
with software and embedding in generated PDFs; retain the license notice.
