# ExportDocManager report font policy

Formal PDF and print output uses the open-source Noto CJK fonts declared in
`font-manifest.json`. Run `node scripts/provision-report-fonts.mjs` before a
desktop or browser-server release build. The provisioner downloads the pinned
upstream files into this program-root directory and verifies every SHA-256.

The `.otf` files are deliberately not committed to Git. They are release
dependencies staged beside the program under `Resources/Fonts/OpenSource`;
they are never installed into Windows, macOS or Linux system font folders.

Segoe UI, Microsoft YaHei, SimSun, SimHei, Arial, Times New Roman, SF Pro,
PingFang and other operating-system fonts may appear only as CSS fallback
names. Their binary files must not be copied into this directory or any
installer. `scripts/verify-font-license-policy.mjs` and package payload checks
enforce this rule.

Noto CJK is distributed under the SIL Open Font License 1.1. The complete
license is included as `OFL-Noto-CJK.txt`.
