# ExportDocManager third-party dependency inventory

Generated from committed npm/Cargo lock files, Cargo metadata and signed native-resource archive manifests. Managed .NET projects are retired; verify-dependency-policy.mjs enforces the Rust-only source and delivery boundary. The exact application build version is recorded in the accompanying machine-readable SBOM files.

Runtime image boundary: PostgreSQL client tools and their native library closure ship in web/server packages under Tools/PostgreSQL with license texts. The database server remains a separate OS/container input. Rust containers do not include .NET or a browser rendering service.
The rust-workspace scope covers the complete locked desktop/server/build/test graph across supported platforms. It is not a desktop runtime inventory; the desktop feature closure is checked separately by verify-native-desktop.mjs.

## npm (131)

| Package | Version | Declared license | Used by |
|---|---:|---|---|
| @dimforge/rapier3d-compat | 0.12.0 | Apache-2.0 | web |
| @esbuild/aix-ppc64 | 0.28.2 | MIT | web |
| @esbuild/android-arm | 0.28.2 | MIT | web |
| @esbuild/android-arm64 | 0.28.2 | MIT | web |
| @esbuild/android-x64 | 0.28.2 | MIT | web |
| @esbuild/darwin-arm64 | 0.28.2 | MIT | web |
| @esbuild/darwin-x64 | 0.28.2 | MIT | web |
| @esbuild/freebsd-arm64 | 0.28.2 | MIT | web |
| @esbuild/freebsd-x64 | 0.28.2 | MIT | web |
| @esbuild/linux-arm | 0.28.2 | MIT | web |
| @esbuild/linux-arm64 | 0.28.2 | MIT | web |
| @esbuild/linux-ia32 | 0.28.2 | MIT | web |
| @esbuild/linux-loong64 | 0.28.2 | MIT | web |
| @esbuild/linux-mips64el | 0.28.2 | MIT | web |
| @esbuild/linux-ppc64 | 0.28.2 | MIT | web |
| @esbuild/linux-riscv64 | 0.28.2 | MIT | web |
| @esbuild/linux-s390x | 0.28.2 | MIT | web |
| @esbuild/linux-x64 | 0.28.2 | MIT | web |
| @esbuild/netbsd-arm64 | 0.28.2 | MIT | web |
| @esbuild/netbsd-x64 | 0.28.2 | MIT | web |
| @esbuild/openbsd-arm64 | 0.28.2 | MIT | web |
| @esbuild/openbsd-x64 | 0.28.2 | MIT | web |
| @esbuild/openharmony-arm64 | 0.28.2 | MIT | web |
| @esbuild/sunos-x64 | 0.28.2 | MIT | web |
| @esbuild/win32-arm64 | 0.28.2 | MIT | web |
| @esbuild/win32-ia32 | 0.28.2 | MIT | web |
| @esbuild/win32-x64 | 0.28.2 | MIT | web |
| @oxc-project/types | 0.155.0 | MIT | web |
| @rolldown/binding-android-arm-eabi | 1.2.15 | MIT | web |
| @rolldown/binding-android-arm64 | 1.2.15 | MIT | web |
| @rolldown/binding-darwin-arm64 | 1.2.15 | MIT | web |
| @rolldown/binding-darwin-x64 | 1.2.15 | MIT | web |
| @rolldown/binding-freebsd-x64 | 1.2.15 | MIT | web |
| @rolldown/binding-linux-arm-gnueabihf | 1.2.15 | MIT | web |
| @rolldown/binding-linux-arm64-gnu | 1.2.15 | MIT | web |
| @rolldown/binding-linux-arm64-musl | 1.2.15 | MIT | web |
| @rolldown/binding-linux-ppc64-gnu | 1.2.15 | MIT | web |
| @rolldown/binding-linux-s390x-gnu | 1.2.15 | MIT | web |
| @rolldown/binding-linux-x64-gnu | 1.2.15 | MIT | web |
| @rolldown/binding-linux-x64-musl | 1.2.15 | MIT | web |
| @rolldown/binding-openharmony-arm64 | 1.2.15 | MIT | web |
| @rolldown/binding-win32-arm64-msvc | 1.2.15 | MIT | web |
| @rolldown/binding-win32-x64-msvc | 1.2.15 | MIT | web |
| @rolldown/pluginutils | 1.0.1 | MIT | web |
| @tanstack/query-core | 5.105.0 | MIT | web |
| @tanstack/react-query | 5.105.0 | MIT | web |
| @tauri-apps/api | 2.12.4 | Apache-2.0 OR MIT | web |
| @tauri-apps/cli | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-darwin-arm64 | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-darwin-x64 | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-arm-gnueabihf | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-arm64-gnu | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-arm64-musl | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-riscv64-gnu | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-x64-gnu | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-linux-x64-musl | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-win32-arm64-msvc | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-win32-ia32-msvc | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tauri-apps/cli-win32-x64-msvc | 2.12.4 | Apache-2.0 OR MIT | tauri-build |
| @tweenjs/tween.js | 23.1.3 | MIT | web |
| @types/node | 26.6.6 | MIT | web |
| @types/pngjs | 6.0.5 | MIT | web |
| @types/react | 19.3.0 | MIT | web |
| @types/react-dom | 19.3.0 | MIT | web |
| @types/stats.js | 0.17.4 | MIT | web |
| @types/three | 0.186.0 | MIT | web |
| @types/webxr | 0.5.24 | MIT | web |
| @typescript/typescript-aix-ppc64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-darwin-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-darwin-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-freebsd-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-freebsd-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-arm | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-loong64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-mips64el | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-ppc64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-riscv64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-s390x | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-linux-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-netbsd-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-netbsd-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-openbsd-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-openbsd-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-sunos-x64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-win32-arm64 | 7.0.2 | Apache-2.0 | web |
| @typescript/typescript-win32-x64 | 7.0.2 | Apache-2.0 | web |
| @vitejs/plugin-react | 6.1.2 | MIT | web |
| axe-core | 4.14.0 | MPL-2.0 | web |
| cookie | 1.1.1 | MIT | web |
| csstype | 3.2.3 | MIT | web |
| detect-libc | 2.1.2 | Apache-2.0 | web |
| esbuild | 0.28.2 | MIT | web |
| fdir | 6.5.0 | MIT | web |
| fflate | 0.8.3 | MIT | web |
| fsevents | 2.3.3 | MIT | web |
| lightningcss | 1.33.0 | MPL-2.0 | web |
| lightningcss-android-arm64 | 1.33.0 | MPL-2.0 | web |
| lightningcss-darwin-arm64 | 1.33.0 | MPL-2.0 | web |
| lightningcss-darwin-x64 | 1.33.0 | MPL-2.0 | web |
| lightningcss-freebsd-x64 | 1.33.0 | MPL-2.0 | web |
| lightningcss-linux-arm-gnueabihf | 1.33.0 | MPL-2.0 | web |
| lightningcss-linux-arm64-gnu | 1.33.0 | MPL-2.0 | web |
| lightningcss-linux-arm64-musl | 1.33.0 | MPL-2.0 | web |
| lightningcss-linux-x64-gnu | 1.33.0 | MPL-2.0 | web |
| lightningcss-linux-x64-musl | 1.33.0 | MPL-2.0 | web |
| lightningcss-win32-arm64-msvc | 1.33.0 | MPL-2.0 | web |
| lightningcss-win32-x64-msvc | 1.33.0 | MPL-2.0 | web |
| lucide-react | 1.64.0 | ISC | web |
| meshoptimizer | 1.1.1 | MIT | web |
| nanoid | 3.3.20 | MIT | web |
| picocolors | 1.1.1 | ISC | web |
| picomatch | 4.0.7 | MIT | web |
| pixelmatch | 8.0.0 | ISC | web |
| playwright | 1.64.1 | Apache-2.0 | web |
| playwright-core | 1.64.1 | Apache-2.0 | web |
| pngjs | 7.0.0 | MIT | web |
| postcss | 8.5.31 | MIT | web |
| react | 19.3.1 | MIT | web |
| react-dom | 19.3.1 | MIT | web |
| react-router | 7.18.5 | MIT | web |
| react-router-dom | 7.18.5 | MIT | web |
| rolldown | 1.2.15 | MIT | web |
| scheduler | 0.28.0 | MIT | web |
| set-cookie-parser | 2.7.3 | MIT | web |
| source-map-js | 1.2.2 | BSD-3-Clause | web |
| three | 0.186.1 | MIT | web |
| tinyglobby | 0.2.17 | MIT | web |
| typescript | 7.0.2 | Apache-2.0 | web |
| undici-types | 8.9.0 | MIT | web |
| vite | 8.3.5 | MIT | web |

## cargo (699)

| Package | Version | Declared license | Used by |
|---|---:|---|---|
| adler2 | 2.0.1 | 0BSD OR MIT OR Apache-2.0 | ocr, rust-workspace |
| aead | 0.6.1 | MIT OR Apache-2.0 | rust-workspace |
| aes | 0.9.3 | MIT OR Apache-2.0 | rust-workspace |
| aes-gcm | 0.11.1 | Apache-2.0 OR MIT | rust-workspace |
| aho-corasick | 1.1.5 | Unlicense OR MIT | rust-workspace |
| alloc-no-stdlib | 3.0.0 | BSD-3-Clause | rust-workspace |
| alloc-stdlib | 0.3.0 | BSD-3-Clause | rust-workspace |
| android_system_properties | 0.1.6 | MIT OR Apache-2.0 | rust-workspace |
| anyhow | 1.0.104 | MIT OR Apache-2.0 | ocr, rust-workspace |
| arrayref | 0.3.9 | BSD-2-Clause | rust-workspace |
| arrayvec | 0.7.8 | MIT OR Apache-2.0 | rust-workspace |
| async-broadcast | 0.7.2 | MIT OR Apache-2.0 | rust-workspace |
| async-channel | 2.5.0 | Apache-2.0 OR MIT | rust-workspace |
| async-executor | 1.14.0 | Apache-2.0 OR MIT | rust-workspace |
| async-io | 2.6.0 | Apache-2.0 OR MIT | rust-workspace |
| async-lock | 3.4.2 | Apache-2.0 OR MIT | rust-workspace |
| async-process | 2.5.0 | Apache-2.0 OR MIT | rust-workspace |
| async-recursion | 1.2.0 | MIT OR Apache-2.0 | rust-workspace |
| async-signal | 0.2.14 | Apache-2.0 OR MIT | rust-workspace |
| async-task | 4.7.1 | Apache-2.0 OR MIT | rust-workspace |
| async-trait | 0.1.92 | MIT OR Apache-2.0 | rust-workspace |
| atk | 0.18.2 | MIT | rust-workspace |
| atk-sys | 0.18.2 | MIT | rust-workspace |
| atoi_simd | 0.18.1 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| atomic-waker | 1.1.2 | Apache-2.0 OR MIT | rust-workspace |
| autocfg | 1.5.1 | Apache-2.0 OR MIT | ocr, rust-workspace |
| aws-credential-types | 1.3.1 | Apache-2.0 | rust-workspace |
| aws-sigv4 | 1.6.3 | Apache-2.0 | rust-workspace |
| aws-smithy-async | 1.3.1 | Apache-2.0 | rust-workspace |
| aws-smithy-http | 0.64.2 | Apache-2.0 | rust-workspace |
| aws-smithy-runtime-api | 1.19.1 | Apache-2.0 | rust-workspace |
| aws-smithy-runtime-api-macros | 1.1.0 | Apache-2.0 | rust-workspace |
| aws-smithy-types | 1.9.0 | Apache-2.0 | rust-workspace |
| axum | 0.8.9 | MIT | rust-workspace |
| axum-core | 0.5.6 | MIT | rust-workspace |
| base64 | 0.21.7 | MIT OR Apache-2.0 | rust-workspace |
| base64 | 0.22.1 | MIT OR Apache-2.0 | rust-workspace |
| base64 | 0.23.1 | MIT OR Apache-2.0 | rust-workspace |
| base64-simd | 0.8.0 | MIT | rust-workspace |
| base64ct | 1.8.3 | Apache-2.0 OR MIT | rust-workspace |
| bit-set | 0.8.0 | Apache-2.0 OR MIT | rust-workspace |
| bit-vec | 0.8.0 | Apache-2.0 OR MIT | rust-workspace |
| bitflags | 1.3.2 | MIT OR Apache-2.0 | rust-workspace |
| bitflags | 2.13.2 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| block-buffer | 0.10.4 | MIT OR Apache-2.0 | rust-workspace |
| block-buffer | 0.12.1 | MIT OR Apache-2.0 | rust-workspace |
| block2 | 0.6.2 | MIT | rust-workspace |
| blocking | 1.7.0 | Apache-2.0 OR MIT | rust-workspace |
| borsh | 1.8.1 | MIT OR Apache-2.0 | rust-workspace |
| borsh-derive | 1.8.1 | Apache-2.0 | rust-workspace |
| brotli | 9.0.0 | BSD-3-Clause AND MIT | rust-workspace |
| brotli-decompressor | 6.0.1 | BSD-3-Clause OR MIT | rust-workspace |
| bs58 | 0.5.1 | MIT OR Apache-2.0 | rust-workspace |
| bumpalo | 3.20.3 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| bytemuck | 1.25.2 | Zlib OR Apache-2.0 OR MIT | ocr, rust-workspace |
| bytemuck_derive | 1.12.1 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| byteorder | 1.5.0 | Unlicense OR MIT | excel-analyzer, rust-workspace |
| byteorder-lite | 0.1.0 | Unlicense OR MIT | ocr, rust-workspace |
| bytes | 1.12.1 | MIT | rust-workspace |
| bytes-utils | 0.1.4 | Apache-2.0 OR MIT | rust-workspace |
| cairo-rs | 0.18.5 | MIT | rust-workspace |
| cairo-sys-rs | 0.18.2 | MIT | rust-workspace |
| calamine | 0.37.0 | MIT | excel-analyzer, rust-workspace |
| camino | 1.2.6 | MIT OR Apache-2.0 | rust-workspace |
| cargo_metadata | 0.19.2 | MIT | rust-workspace |
| cargo_toml | 1.0.1 | Apache-2.0 OR MIT | rust-workspace |
| cargo-platform | 0.1.9 | MIT OR Apache-2.0 | rust-workspace |
| cc | 1.9.0 | MIT OR Apache-2.0 | rust-workspace |
| cesu8 | 1.1.0 | Apache-2.0 OR MIT | rust-workspace |
| cfb | 0.14.0 | MIT | rust-workspace |
| cfb | 0.15.0 | MIT | rust-workspace |
| cfg_aliases | 0.2.2 | MIT | excel-analyzer, rust-workspace |
| cfg-expr | 0.15.8 | MIT OR Apache-2.0 | rust-workspace |
| cfg-if | 1.0.5 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| chacha20 | 0.10.2 | MIT OR Apache-2.0 | rust-workspace |
| chrono | 0.4.45 | MIT OR Apache-2.0 | rust-workspace |
| chrono-tz | 0.10.4 | MIT OR Apache-2.0 | rust-workspace |
| cipher | 0.5.2 | MIT OR Apache-2.0 | rust-workspace |
| cmov | 0.5.4 | Apache-2.0 OR MIT | rust-workspace |
| codepage | 0.1.3 | Apache-2.0 OR MIT | excel-analyzer, rust-workspace |
| color_quant | 1.1.0 | MIT | rust-workspace |
| combine | 4.6.9 | MIT | rust-workspace |
| concurrent-queue | 2.5.0 | Apache-2.0 OR MIT | rust-workspace |
| console_error_panic_hook | 0.1.7 | Apache-2.0 OR MIT | rust-workspace |
| console_log | 1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| const-oid | 0.10.2 | Apache-2.0 OR MIT | rust-workspace |
| cookie | 0.18.2 | MIT OR Apache-2.0 | rust-workspace |
| cookie_store | 0.22.1 | MIT OR Apache-2.0 | rust-workspace |
| core_detect | 1.0.0 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| core_maths | 0.1.1 | MIT | rust-workspace |
| core-foundation | 0.10.1 | MIT OR Apache-2.0 | rust-workspace |
| core-foundation | 0.9.4 | MIT OR Apache-2.0 | rust-workspace |
| core-foundation-sys | 0.8.7 | MIT OR Apache-2.0 | rust-workspace |
| core-graphics | 0.25.0 | MIT OR Apache-2.0 | rust-workspace |
| core-graphics-types | 0.2.0 | MIT OR Apache-2.0 | rust-workspace |
| cpubits | 0.1.1 | MIT OR Apache-2.0 | rust-workspace |
| cpufeatures | 0.2.17 | MIT OR Apache-2.0 | rust-workspace |
| cpufeatures | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| crc32fast | 1.5.3 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| crossbeam-channel | 0.5.17 | MIT OR Apache-2.0 | rust-workspace |
| crossbeam-utils | 0.8.23 | MIT OR Apache-2.0 | rust-workspace |
| crunchy | 0.2.4 | MIT | ocr |
| crypto-common | 0.1.7 | MIT OR Apache-2.0 | rust-workspace |
| crypto-common | 0.2.2 | MIT OR Apache-2.0 | rust-workspace |
| cssparser | 0.37.0 | MPL-2.0 | rust-workspace |
| cssparser | 0.38.0 | MPL-2.0 | rust-workspace |
| cssparser-macros | 0.7.1 | MPL-2.0 | rust-workspace |
| ctor | 1.0.13 | Apache-2.0 OR MIT | rust-workspace |
| ctr | 0.10.1 | MIT OR Apache-2.0 | rust-workspace |
| ctutils | 0.4.4 | Apache-2.0 OR MIT | rust-workspace |
| darling | 0.24.1 | MIT | rust-workspace |
| darling_core | 0.24.1 | MIT | rust-workspace |
| darling_macro | 0.24.1 | MIT | rust-workspace |
| data-url | 0.3.2 | MIT OR Apache-2.0 | rust-workspace |
| dbus | 0.9.12 | Apache-2.0 OR MIT | rust-workspace |
| debug_unsafe | 0.1.4 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| defmt | 1.2.0 | MIT OR Apache-2.0 | rust-workspace |
| defmt-macros | 1.2.0 | MIT OR Apache-2.0 | rust-workspace |
| defmt-parser | 1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| der | 0.8.2 | Apache-2.0 OR MIT | rust-workspace |
| deranged | 0.5.9 | MIT OR Apache-2.0 | rust-workspace |
| derive_more | 2.1.1 | MIT | rust-workspace |
| derive_more-impl | 2.1.1 | MIT | rust-workspace |
| digest | 0.10.7 | MIT OR Apache-2.0 | rust-workspace |
| digest | 0.11.3 | MIT OR Apache-2.0 | rust-workspace |
| dirs | 7.0.0 | MIT OR Apache-2.0 | rust-workspace |
| dirs-sys | 0.5.0 | MIT OR Apache-2.0 | rust-workspace |
| dispatch2 | 0.3.1 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| displaydoc | 0.2.7 | MIT OR Apache-2.0 | rust-workspace |
| dlopen2 | 0.8.2 | MIT | rust-workspace |
| dlopen2_derive | 0.4.3 | MIT | rust-workspace |
| document-features | 0.2.12 | MIT OR Apache-2.0 | rust-workspace |
| dom_query | 0.28.0 | MIT | rust-workspace |
| dpi | 0.1.2 | Apache-2.0 AND MIT | rust-workspace |
| dtoa | 1.0.11 | MIT OR Apache-2.0 | rust-workspace |
| dtoa-short | 0.3.5 | MPL-2.0 | rust-workspace |
| dunce | 1.0.5 | CC0-1.0 OR MIT-0 OR Apache-2.0 | rust-workspace |
| dyn-clone | 1.0.20 | MIT OR Apache-2.0 | rust-workspace |
| ego-tree | 0.11.1 | ISC | rust-workspace |
| either | 1.19.0 | MIT OR Apache-2.0 | rust-workspace |
| email_address | 0.2.9 | MIT | rust-workspace |
| email-encoding | 0.4.2 | MIT OR Apache-2.0 | rust-workspace |
| embed_plist | 1.2.2 | MIT OR Apache-2.0 | rust-workspace |
| embed-resource | 3.0.12 | MIT | rust-workspace |
| encoding_rs | 0.8.42 | (Apache-2.0 OR MIT) AND BSD-3-Clause | excel-analyzer, rust-workspace |
| endi | 1.1.1 | MIT | rust-workspace |
| enumflags2 | 0.7.12 | MIT OR Apache-2.0 | rust-workspace |
| enumflags2_derive | 0.7.12 | MIT OR Apache-2.0 | rust-workspace |
| equivalent | 1.0.3 | Apache-2.0 OR MIT | excel-analyzer, rust-workspace |
| erased-serde | 0.4.10 | MIT OR Apache-2.0 | rust-workspace |
| errno | 0.3.14 | MIT OR Apache-2.0 | rust-workspace |
| euclid | 0.22.14 | MIT OR Apache-2.0 | rust-workspace |
| event-listener | 5.4.2 | Apache-2.0 OR MIT | rust-workspace |
| event-listener-strategy | 0.5.4 | Apache-2.0 OR MIT | rust-workspace |
| fallible-iterator | 0.2.0 | MIT OR Apache-2.0 | rust-workspace |
| fallible-iterator | 0.3.0 | MIT OR Apache-2.0 | rust-workspace |
| fallible-streaming-iterator | 0.1.9 | MIT OR Apache-2.0 | rust-workspace |
| fast-float2 | 0.2.4 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| fastrand | 2.5.0 | Apache-2.0 OR MIT | rust-workspace |
| fax | 0.2.7 | MIT | ocr |
| fdeflate | 0.3.7 | MIT OR Apache-2.0 | ocr, rust-workspace |
| field-offset | 0.3.6 | MIT OR Apache-2.0 | rust-workspace |
| filetime | 0.2.29 | MIT OR Apache-2.0 | rust-workspace |
| find-msvc-tools | 0.1.15 | MIT OR Apache-2.0 | rust-workspace |
| flate2 | 1.1.10 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| float-cmp | 0.9.0 | MIT | rust-workspace |
| fnv | 1.0.7 | Apache-2.0 OR MIT | rust-workspace |
| foldhash | 0.2.0 | Zlib | rust-workspace |
| font-types | 0.11.3 | MIT OR Apache-2.0 | rust-workspace |
| fontconfig-parser | 0.5.8 | MIT | rust-workspace |
| fontdb | 0.23.0 | MIT | rust-workspace |
| foreign-types | 0.3.2 | MIT OR Apache-2.0 | rust-workspace |
| foreign-types | 0.5.0 | MIT OR Apache-2.0 | rust-workspace |
| foreign-types-macros | 0.2.4 | MIT OR Apache-2.0 | rust-workspace |
| foreign-types-shared | 0.1.1 | MIT OR Apache-2.0 | rust-workspace |
| foreign-types-shared | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| form_urlencoded | 1.2.2 | MIT OR Apache-2.0 | rust-workspace |
| fs2 | 0.4.3 | MIT OR Apache-2.0 | rust-workspace |
| futures-channel | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-core | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-executor | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-io | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-lite | 2.6.1 | Apache-2.0 OR MIT | rust-workspace |
| futures-macro | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-sink | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-task | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| futures-util | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| gdk | 0.18.2 | MIT | rust-workspace |
| gdk-pixbuf | 0.18.5 | MIT | rust-workspace |
| gdk-pixbuf-sys | 0.18.0 | MIT | rust-workspace |
| gdk-sys | 0.18.2 | MIT | rust-workspace |
| gdkwayland-sys | 0.18.2 | MIT | rust-workspace |
| gdkx11 | 0.18.2 | MIT | rust-workspace |
| gdkx11-sys | 0.18.2 | MIT | rust-workspace |
| generic-array | 0.14.7 | MIT | rust-workspace |
| getrandom | 0.2.17 | MIT OR Apache-2.0 | rust-workspace |
| getrandom | 0.3.4 | MIT OR Apache-2.0 | rust-workspace |
| getrandom | 0.4.3 | MIT OR Apache-2.0 | rust-workspace |
| ghash | 0.6.0 | Apache-2.0 OR MIT | rust-workspace |
| gif | 0.14.2 | MIT OR Apache-2.0 | rust-workspace |
| gio | 0.18.4 | MIT | rust-workspace |
| gio-sys | 0.18.1 | MIT | rust-workspace |
| glib | 0.18.5 | MIT | rust-workspace |
| glib-macros | 0.18.5 | MIT | rust-workspace |
| glib-sys | 0.18.1 | MIT | rust-workspace |
| glob | 0.3.4 | MIT OR Apache-2.0 | rust-workspace |
| gobject-sys | 0.18.0 | MIT | rust-workspace |
| gtk | 0.18.2 | MIT | rust-workspace |
| gtk-sys | 0.18.2 | MIT | rust-workspace |
| gtk3-macros | 0.18.2 | MIT | rust-workspace |
| half | 2.7.1 | MIT OR Apache-2.0 | ocr |
| hashbrown | 0.12.3 | MIT OR Apache-2.0 | rust-workspace |
| hashbrown | 0.17.1 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| hashlink | 0.12.2 | MIT OR Apache-2.0 | rust-workspace |
| heck | 0.4.1 | MIT OR Apache-2.0 | rust-workspace |
| heck | 0.5.0 | MIT OR Apache-2.0 | rust-workspace |
| hermit-abi | 0.5.3 | MIT OR Apache-2.0 | rust-workspace |
| hex | 0.4.3 | MIT OR Apache-2.0 | rust-workspace |
| hmac | 0.13.0 | MIT OR Apache-2.0 | rust-workspace |
| html5ever | 0.39.0 | MIT OR Apache-2.0 | rust-workspace |
| html5ever | 0.40.1 | MIT OR Apache-2.0 | rust-workspace |
| http | 1.5.0 | MIT OR Apache-2.0 | rust-workspace |
| http-body | 1.1.0 | MIT | rust-workspace |
| http-body-util | 0.1.5 | MIT | rust-workspace |
| http-range-header | 0.4.2 | MIT | rust-workspace |
| httparse | 1.10.1 | MIT OR Apache-2.0 | rust-workspace |
| httpdate | 1.0.3 | MIT OR Apache-2.0 | rust-workspace |
| hybrid-array | 0.4.15 | MIT OR Apache-2.0 | rust-workspace |
| hyper | 1.12.0 | MIT | rust-workspace |
| hyper-rustls | 0.27.10 | Apache-2.0 OR ISC OR MIT | rust-workspace |
| hyper-util | 0.1.21 | MIT | rust-workspace |
| iana-time-zone | 0.1.65 | MIT OR Apache-2.0 | rust-workspace |
| iana-time-zone-haiku | 0.1.2 | MIT OR Apache-2.0 | rust-workspace |
| ico | 0.6.0 | MIT | rust-workspace |
| icu_collections | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_locale_core | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_normalizer | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_normalizer_data | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_properties | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_properties_data | 2.4.0 | Unicode-3.0 | rust-workspace |
| icu_provider | 2.4.0 | Unicode-3.0 | rust-workspace |
| ident_case | 1.0.1 | MIT OR Apache-2.0 | rust-workspace |
| idna | 1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| idna_adapter | 1.2.2 | Apache-2.0 OR MIT | rust-workspace |
| image | 0.25.10 | MIT OR Apache-2.0 | ocr, rust-workspace |
| image-webp | 0.2.4 | MIT OR Apache-2.0 | ocr, rust-workspace |
| imagesize | 0.14.0 | MIT | rust-workspace |
| indexmap | 1.9.3 | Apache-2.0 OR MIT | rust-workspace |
| indexmap | 2.14.2 | Apache-2.0 OR MIT | excel-analyzer, rust-workspace |
| infer | 0.22.0 | MIT | rust-workspace |
| inout | 0.2.5 | MIT OR Apache-2.0 | rust-workspace |
| ipnet | 2.12.2 | MIT OR Apache-2.0 | rust-workspace |
| itertools | 0.15.0 | MIT OR Apache-2.0 | rust-workspace |
| itoa | 1.0.18 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| javascriptcore-rs | 1.1.2 | MIT | rust-workspace |
| javascriptcore-rs-sys | 1.1.1 | MIT | rust-workspace |
| jiff | 0.2.38 | Unlicense OR MIT | rust-workspace |
| jiff-core | 0.1.1 | Unlicense OR MIT | rust-workspace |
| jiff-static | 0.2.38 | Unlicense OR MIT | rust-workspace |
| jiff-tzdb | 0.1.9 | Unlicense OR MIT | rust-workspace |
| jiff-tzdb-platform | 0.1.3 | Unlicense OR MIT | rust-workspace |
| jni | 0.21.1 | MIT OR Apache-2.0 | rust-workspace |
| jni | 0.22.4 | MIT OR Apache-2.0 | rust-workspace |
| jni-macros | 0.22.4 | MIT OR Apache-2.0 | rust-workspace |
| jni-sys | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| jni-sys | 0.4.1 | MIT OR Apache-2.0 | rust-workspace |
| jni-sys-macros | 0.4.1 | MIT OR Apache-2.0 | rust-workspace |
| js-sys | 0.3.106 | MIT OR Apache-2.0 | rust-workspace |
| json-patch | 4.2.0 | MIT OR Apache-2.0 | rust-workspace |
| jsonptr | 0.7.1 | MIT OR Apache-2.0 | rust-workspace |
| keyboard-types | 0.8.3 | MIT OR Apache-2.0 | rust-workspace |
| krilla | 0.8.2 | MIT OR Apache-2.0 | rust-workspace |
| krilla-svg | 0.8.1 | MIT OR Apache-2.0 | rust-workspace |
| kurbo | 0.13.1 | Apache-2.0 OR MIT | rust-workspace |
| lettre | 0.11.23 | MIT | rust-workspace |
| libappindicator | 0.9.0 | Apache-2.0 OR MIT | rust-workspace |
| libappindicator-sys | 0.9.0 | Apache-2.0 OR MIT | rust-workspace |
| libc | 0.2.191 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| libdbus-sys | 0.2.7 | Apache-2.0 OR MIT | rust-workspace |
| libloading | 0.7.4 | ISC | rust-workspace |
| libloading | 0.9.0 | ISC | ocr, rust-workspace |
| libm | 0.2.16 | MIT | rust-workspace |
| libredox | 0.1.25 | MIT | rust-workspace |
| libsqlite3-sys | 0.38.2 | MIT | rust-workspace |
| linux-raw-sys | 0.12.1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| litemap | 0.8.3 | Unicode-3.0 | rust-workspace |
| litrs | 1.0.1 | MIT OR Apache-2.0 | rust-workspace |
| lock_api | 0.4.14 | MIT OR Apache-2.0 | rust-workspace |
| log | 0.4.34 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| markup5ever | 0.39.0 | MIT OR Apache-2.0 | rust-workspace |
| markup5ever | 0.40.0 | MIT OR Apache-2.0 | rust-workspace |
| matchit | 0.8.4 | MIT AND BSD-3-Clause | rust-workspace |
| matrixmultiply | 0.3.11 | MIT OR Apache-2.0 | ocr |
| maybe-owned | 0.3.4 | MIT OR Apache-2.0 | rust-workspace |
| md-5 | 0.11.0 | MIT OR Apache-2.0 | rust-workspace |
| memchr | 2.8.3 | Unlicense OR MIT | excel-analyzer, ocr, rust-workspace |
| memmap2 | 0.9.12 | MIT OR Apache-2.0 | rust-workspace |
| memoffset | 0.9.1 | MIT | rust-workspace |
| mime | 0.3.17 | MIT OR Apache-2.0 | rust-workspace |
| mime_guess | 2.0.5 | MIT | rust-workspace |
| minisign-verify | 0.3.0 | MIT | rust-workspace |
| miniz_oxide | 0.8.9 | MIT OR Zlib OR Apache-2.0 | ocr, rust-workspace |
| miniz_oxide | 0.9.1 | MIT OR Zlib OR Apache-2.0 | ocr, rust-workspace |
| mio | 1.2.4 | MIT | rust-workspace |
| moxcms | 0.8.1 | BSD-3-Clause OR Apache-2.0 | ocr, rust-workspace |
| muda | 0.20.0 | Apache-2.0 OR MIT | rust-workspace |
| multer | 3.1.0 | MIT | rust-workspace |
| multiversion_no_op | 1.0.0 | Apache-2.0 OR MIT | excel-analyzer, rust-workspace |
| native-tls | 0.2.18 | MIT OR Apache-2.0 | rust-workspace |
| ndarray | 0.17.2 | MIT OR Apache-2.0 | ocr |
| ndk | 0.9.0 | MIT OR Apache-2.0 | rust-workspace |
| ndk-context | 0.1.1 | MIT OR Apache-2.0 | rust-workspace |
| ndk-sys | 0.6.0+11769913 | MIT OR Apache-2.0 | rust-workspace |
| new_debug_unreachable | 1.0.6 | MIT | rust-workspace |
| nix | 0.31.3 | MIT | excel-analyzer, rust-workspace |
| nom | 8.0.0 | MIT | rust-workspace |
| num_enum | 0.7.6 | BSD-3-Clause OR MIT OR Apache-2.0 | rust-workspace |
| num_enum_derive | 0.7.6 | BSD-3-Clause OR MIT OR Apache-2.0 | rust-workspace |
| num-complex | 0.4.6 | MIT OR Apache-2.0 | ocr |
| num-conv | 0.2.2 | MIT OR Apache-2.0 | rust-workspace |
| num-integer | 0.1.47 | MIT OR Apache-2.0 | ocr, rust-workspace |
| num-traits | 0.2.19 | MIT OR Apache-2.0 | ocr, rust-workspace |
| objc2 | 0.6.5 | MIT | rust-workspace |
| objc2-app-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-cloud-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-data | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-foundation | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-graphics | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-image | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-location | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-core-text | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-encode | 4.1.0 | MIT | rust-workspace |
| objc2-exception-helper | 0.1.1 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-foundation | 0.3.2 | MIT | rust-workspace |
| objc2-io-surface | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-osa-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-quartz-core | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-system-configuration | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-ui-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-user-notifications | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| objc2-web-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| once_cell | 1.21.4 | MIT OR Apache-2.0 | ocr, rust-workspace |
| openssl | 0.10.81 | Apache-2.0 | rust-workspace |
| openssl-macros | 0.1.1 | MIT OR Apache-2.0 | rust-workspace |
| openssl-probe | 0.2.1 | MIT OR Apache-2.0 | rust-workspace |
| openssl-sys | 0.9.117 | MIT | rust-workspace |
| option-ext | 0.2.0 | MPL-2.0 | rust-workspace |
| ordered-stream | 0.2.0 | MIT OR Apache-2.0 | rust-workspace |
| ort | 2.0.0-rc.12 | MIT OR Apache-2.0 | ocr |
| ort-sys | 2.0.0-rc.12 | MIT OR Apache-2.0 | ocr |
| osakit | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| outref | 0.5.2 | MIT | rust-workspace |
| pango | 0.18.3 | MIT | rust-workspace |
| pango-sys | 0.18.0 | MIT | rust-workspace |
| parking | 2.2.1 | Apache-2.0 OR MIT | rust-workspace |
| parking_lot | 0.12.5 | MIT OR Apache-2.0 | rust-workspace |
| parking_lot_core | 0.9.12 | MIT OR Apache-2.0 | rust-workspace |
| pbkdf2 | 0.13.0 | MIT OR Apache-2.0 | rust-workspace |
| pdf-writer | 0.15.0 | MIT OR Apache-2.0 | rust-workspace |
| pdfium-render | 0.9.4 | MIT OR Apache-2.0 | rust-workspace |
| pem-rfc7468 | 1.0.0 | Apache-2.0 OR MIT | rust-workspace |
| percent-encoding | 2.3.2 | MIT OR Apache-2.0 | rust-workspace |
| phf | 0.12.1 | MIT | rust-workspace |
| phf | 0.13.1 | MIT | rust-workspace |
| phf | 0.14.0 | MIT | rust-workspace |
| phf_codegen | 0.13.1 | MIT | rust-workspace |
| phf_codegen | 0.14.0 | MIT | rust-workspace |
| phf_generator | 0.13.1 | MIT | rust-workspace |
| phf_generator | 0.14.0 | MIT | rust-workspace |
| phf_macros | 0.13.1 | MIT | rust-workspace |
| phf_macros | 0.14.0 | MIT | rust-workspace |
| phf_shared | 0.12.1 | MIT | rust-workspace |
| phf_shared | 0.13.1 | MIT | rust-workspace |
| phf_shared | 0.14.0 | MIT | rust-workspace |
| pico-args | 0.5.0 | MIT | rust-workspace |
| pin-project-lite | 0.2.17 | Apache-2.0 OR MIT | ocr, rust-workspace |
| piper | 0.2.5 | MIT OR Apache-2.0 | rust-workspace |
| piston-float | 1.0.1 | MIT | rust-workspace |
| pkg-config | 0.3.34 | MIT OR Apache-2.0 | rust-workspace |
| plist | 1.10.3 | MIT | rust-workspace |
| png | 0.18.1 | MIT OR Apache-2.0 | ocr, rust-workspace |
| polling | 3.11.0 | Apache-2.0 OR MIT | rust-workspace |
| pollster | 0.4.0 | Apache-2.0 OR MIT | rust-workspace |
| polycool | 0.4.0 | MIT OR Apache-2.0 | rust-workspace |
| polyval | 0.7.3 | Apache-2.0 OR MIT | rust-workspace |
| portable-atomic | 1.15.0 | Apache-2.0 OR MIT | ocr, rust-workspace |
| portable-atomic-util | 0.2.8 | Apache-2.0 OR MIT | ocr, rust-workspace |
| postgres | 0.19.14 | MIT OR Apache-2.0 | rust-workspace |
| postgres-native-tls | 0.5.3 | MIT OR Apache-2.0 | rust-workspace |
| postgres-protocol | 0.6.12 | MIT OR Apache-2.0 | rust-workspace |
| postgres-types | 0.2.14 | MIT OR Apache-2.0 | rust-workspace |
| potential_utf | 0.1.6 | Unicode-3.0 | rust-workspace |
| powerfmt | 0.2.1 | MIT OR Apache-2.0 | rust-workspace |
| ppv-lite86 | 0.2.21 | MIT OR Apache-2.0 | rust-workspace |
| precomputed-hash | 0.1.1 | MIT | rust-workspace |
| proc-macro-crate | 1.3.1 | MIT OR Apache-2.0 | rust-workspace |
| proc-macro-crate | 2.0.2 | MIT OR Apache-2.0 | rust-workspace |
| proc-macro-crate | 3.5.0 | MIT OR Apache-2.0 | rust-workspace |
| proc-macro-error | 1.0.4 | MIT OR Apache-2.0 | rust-workspace |
| proc-macro-error-attr | 1.0.4 | MIT OR Apache-2.0 | rust-workspace |
| proc-macro2 | 1.0.107 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| pxfm | 0.1.30 | BSD-3-Clause OR Apache-2.0 | ocr, rust-workspace |
| quick-error | 2.0.1 | MIT OR Apache-2.0 | ocr, rust-workspace |
| quick-xml | 0.42.0 | MIT | excel-analyzer, rust-workspace |
| quote | 1.0.47 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| quoted_printable | 0.5.2 | 0BSD | rust-workspace |
| r-efi | 5.3.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later | rust-workspace |
| r-efi | 6.0.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later | rust-workspace |
| rand | 0.10.3 | MIT OR Apache-2.0 | rust-workspace |
| rand | 0.8.8 | MIT OR Apache-2.0 | rust-workspace |
| rand | 0.9.5 | MIT OR Apache-2.0 | rust-workspace |
| rand_chacha | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| rand_chacha | 0.9.0 | MIT OR Apache-2.0 | rust-workspace |
| rand_core | 0.10.1 | MIT OR Apache-2.0 | rust-workspace |
| rand_core | 0.6.4 | MIT OR Apache-2.0 | rust-workspace |
| rand_core | 0.9.5 | MIT OR Apache-2.0 | rust-workspace |
| raw-window-handle | 0.6.2 | MIT OR Apache-2.0 OR Zlib | rust-workspace |
| rawpointer | 0.2.1 | MIT OR Apache-2.0 | ocr |
| read-fonts | 0.39.2 | MIT OR Apache-2.0 | rust-workspace |
| redox_syscall | 0.5.18 | MIT | rust-workspace |
| redox_users | 0.5.3 | MIT | rust-workspace |
| ref-cast | 1.0.27 | MIT OR Apache-2.0 | rust-workspace |
| ref-cast-impl | 1.0.27 | MIT OR Apache-2.0 | rust-workspace |
| regex | 1.13.1 | MIT OR Apache-2.0 | rust-workspace |
| regex-automata | 0.4.18 | MIT OR Apache-2.0 | rust-workspace |
| regex-syntax | 0.8.11 | MIT OR Apache-2.0 | rust-workspace |
| reqwest | 0.13.5 | MIT OR Apache-2.0 | rust-workspace |
| resvg | 0.47.0 | Apache-2.0 OR MIT | rust-workspace |
| rfd | 0.17.2 | MIT | rust-workspace |
| rgb | 0.8.53 | MIT | rust-workspace |
| ring | 0.17.14 | Apache-2.0 AND ISC | rust-workspace |
| roxmltree | 0.20.0 | MIT OR Apache-2.0 | rust-workspace |
| roxmltree | 0.21.1 | MIT OR Apache-2.0 | rust-workspace |
| rusqlite | 0.40.2 | MIT | rust-workspace |
| rust_decimal | 1.43.0 | MIT | rust-workspace |
| rustc_version | 0.4.1 | MIT OR Apache-2.0 | rust-workspace |
| rustc-hash | 2.1.3 | Apache-2.0 OR MIT | rust-workspace |
| rustix | 1.1.5 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| rustls | 0.23.46 | Apache-2.0 OR ISC OR MIT | rust-workspace |
| rustls-native-certs | 0.8.5 | Apache-2.0 OR ISC OR MIT | rust-workspace |
| rustls-pki-types | 1.15.1 | MIT OR Apache-2.0 | rust-workspace |
| rustls-platform-verifier | 0.7.1 | MIT OR Apache-2.0 | rust-workspace |
| rustls-platform-verifier-android | 0.2.0 | MIT OR Apache-2.0 | rust-workspace |
| rustls-webpki | 0.103.16 | ISC | rust-workspace |
| rustversion | 1.0.23 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| rustybuzz | 0.20.1 | MIT | rust-workspace |
| ryu | 1.0.23 | Apache-2.0 OR BSL-1.0 | rust-workspace |
| same-file | 1.0.6 | Unlicense OR MIT | rust-workspace |
| schannel | 0.1.30 | MIT | rust-workspace |
| schemars | 0.8.22 | MIT | rust-workspace |
| schemars | 0.9.0 | MIT | rust-workspace |
| schemars | 1.2.3 | MIT | rust-workspace |
| schemars_derive | 0.8.22 | MIT | rust-workspace |
| scopeguard | 1.2.0 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| scraper | 0.29.0 | ISC | rust-workspace |
| security-framework | 3.7.0 | MIT OR Apache-2.0 | rust-workspace |
| security-framework-sys | 2.17.0 | MIT OR Apache-2.0 | rust-workspace |
| selectors | 0.38.0 | MPL-2.0 | rust-workspace |
| selectors | 0.41.0 | MPL-2.0 | rust-workspace |
| semver | 1.0.28 | MIT OR Apache-2.0 | rust-workspace |
| serde | 1.0.229 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| serde_core | 1.0.229 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| serde_derive | 1.0.229 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| serde_derive_internals | 0.29.1 | MIT OR Apache-2.0 | rust-workspace |
| serde_json | 1.0.154 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| serde_path_to_error | 0.1.20 | MIT OR Apache-2.0 | rust-workspace |
| serde_repr | 0.1.21 | MIT OR Apache-2.0 | rust-workspace |
| serde_spanned | 0.6.9 | MIT OR Apache-2.0 | rust-workspace |
| serde_spanned | 1.1.2 | MIT OR Apache-2.0 | rust-workspace |
| serde_with | 3.24.0 | MIT OR Apache-2.0 | rust-workspace |
| serde_with_macros | 3.24.0 | MIT OR Apache-2.0 | rust-workspace |
| serde-untagged | 0.1.9 | MIT OR Apache-2.0 | rust-workspace |
| serialize-to-javascript | 0.1.2 | MIT OR Apache-2.0 | rust-workspace |
| serialize-to-javascript-impl | 0.1.2 | MIT OR Apache-2.0 | rust-workspace |
| servo_arc | 0.4.3 | MIT OR Apache-2.0 | rust-workspace |
| servo_arc | 0.5.0 | MIT OR Apache-2.0 | rust-workspace |
| sha2 | 0.10.9 | MIT OR Apache-2.0 | rust-workspace |
| sha2 | 0.11.1 | MIT OR Apache-2.0 | rust-workspace |
| shlex | 2.0.1 | MIT OR Apache-2.0 | rust-workspace |
| signal-hook-registry | 1.4.8 | MIT OR Apache-2.0 | rust-workspace |
| simd_cesu8 | 1.2.0 | Apache-2.0 OR MIT | rust-workspace |
| simd-adler32 | 0.3.10 | MIT | excel-analyzer, ocr, rust-workspace |
| simdutf8 | 0.1.5 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| simplecss | 0.2.2 | Apache-2.0 OR MIT | rust-workspace |
| siphasher | 1.0.4 | MIT OR Apache-2.0 | rust-workspace |
| skrifa | 0.42.1 | MIT OR Apache-2.0 | rust-workspace |
| slab | 0.4.12 | MIT | rust-workspace |
| slotmap | 1.1.1 | Zlib | rust-workspace |
| smallvec | 1.16.3 | MIT OR Apache-2.0 | ocr, rust-workspace |
| socket2 | 0.6.5 | MIT OR Apache-2.0 | rust-workspace |
| softbuffer | 0.4.8 | MIT OR Apache-2.0 | rust-workspace |
| soup3 | 0.5.0 | MIT | rust-workspace |
| soup3-sys | 0.5.0 | MIT | rust-workspace |
| spin | 0.9.9 | MIT | rust-workspace |
| stable_deref_trait | 1.2.1 | MIT OR Apache-2.0 | rust-workspace |
| static_vcruntime | 3.0.0 | MIT OR Apache-2.0 OR Zlib | rust-workspace |
| strict-num | 0.1.1 | MIT | rust-workspace |
| string_cache | 0.11.0 | MIT OR Apache-2.0 | rust-workspace |
| string_cache | 0.9.0 | MIT OR Apache-2.0 | rust-workspace |
| string_cache_codegen | 0.11.2 | MIT OR Apache-2.0 | rust-workspace |
| string_cache_codegen | 0.6.1 | MIT OR Apache-2.0 | rust-workspace |
| stringprep | 0.1.5 | MIT OR Apache-2.0 | rust-workspace |
| strsim | 0.11.1 | MIT | rust-workspace |
| subsetter | 0.2.6 | MIT OR Apache-2.0 | rust-workspace |
| subtle | 2.6.1 | BSD-3-Clause | rust-workspace |
| svgtypes | 0.16.1 | Apache-2.0 OR MIT | rust-workspace |
| swift-rs | 1.0.9 | MIT OR Apache-2.0 | rust-workspace |
| syn | 1.0.109 | MIT OR Apache-2.0 | rust-workspace |
| syn | 2.0.119 | MIT OR Apache-2.0 | ocr, rust-workspace |
| syn | 3.0.9 | MIT OR Apache-2.0 | excel-analyzer, ocr, rust-workspace |
| sync_wrapper | 1.0.2 | Apache-2.0 | rust-workspace |
| synstructure | 0.14.0 | MIT | rust-workspace |
| system-configuration | 0.7.0 | MIT OR Apache-2.0 | rust-workspace |
| system-configuration-sys | 0.6.0 | MIT OR Apache-2.0 | rust-workspace |
| system-deps | 6.2.2 | MIT OR Apache-2.0 | rust-workspace |
| tao | 0.37.1 | Apache-2.0 | rust-workspace |
| tao-macros | 0.1.4 | MIT OR Apache-2.0 | rust-workspace |
| tar | 0.4.46 | MIT OR Apache-2.0 | rust-workspace |
| target-lexicon | 0.12.16 | Apache-2.0 WITH LLVM-exception | rust-workspace |
| tauri | 2.12.5 | Apache-2.0 OR MIT | rust-workspace |
| tauri-build | 2.7.4 | Apache-2.0 OR MIT | rust-workspace |
| tauri-codegen | 2.7.4 | Apache-2.0 OR MIT | rust-workspace |
| tauri-macros | 2.7.4 | Apache-2.0 OR MIT | rust-workspace |
| tauri-plugin | 2.7.2 | Apache-2.0 OR MIT | rust-workspace |
| tauri-plugin-single-instance | 2.5.4 | Apache-2.0 OR MIT | rust-workspace |
| tauri-plugin-updater | 2.14.0 | Apache-2.0 OR MIT | rust-workspace |
| tauri-runtime | 2.12.2 | Apache-2.0 OR MIT | rust-workspace |
| tauri-runtime-wry | 2.12.3 | Apache-2.0 OR MIT | rust-workspace |
| tauri-utils | 2.10.2 | Apache-2.0 OR MIT | rust-workspace |
| tauri-winres | 0.3.7 | MIT | rust-workspace |
| tempfile | 3.27.0 | MIT OR Apache-2.0 | rust-workspace |
| tendril | 0.5.1 | MIT OR Apache-2.0 | rust-workspace |
| thiserror | 1.0.69 | MIT OR Apache-2.0 | rust-workspace |
| thiserror | 2.0.21 | MIT OR Apache-2.0 | rust-workspace |
| thiserror-impl | 1.0.69 | MIT OR Apache-2.0 | rust-workspace |
| thiserror-impl | 2.0.21 | MIT OR Apache-2.0 | rust-workspace |
| tiff | 0.11.3 | MIT | ocr |
| time | 0.3.55 | MIT OR Apache-2.0 | rust-workspace |
| time-core | 0.1.9 | MIT OR Apache-2.0 | rust-workspace |
| time-macros | 0.2.32 | MIT OR Apache-2.0 | rust-workspace |
| tiny-skia | 0.12.0 | BSD-3-Clause | rust-workspace |
| tiny-skia-path | 0.12.0 | BSD-3-Clause | rust-workspace |
| tinystr | 0.8.5 | Unicode-3.0 | rust-workspace |
| tinyvec | 1.13.3 | Zlib OR Apache-2.0 OR MIT | rust-workspace |
| tokio | 1.54.1 | MIT | rust-workspace |
| tokio-macros | 2.7.2 | MIT | rust-workspace |
| tokio-native-tls | 0.3.1 | MIT | rust-workspace |
| tokio-postgres | 0.7.18 | MIT OR Apache-2.0 | rust-workspace |
| tokio-rustls | 0.26.6 | MIT OR Apache-2.0 | rust-workspace |
| tokio-util | 0.7.20 | MIT | rust-workspace |
| toml | 0.8.2 | MIT OR Apache-2.0 | rust-workspace |
| toml | 1.1.8+spec-1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| toml_datetime | 0.6.3 | MIT OR Apache-2.0 | rust-workspace |
| toml_datetime | 1.1.2+spec-1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| toml_edit | 0.19.15 | MIT OR Apache-2.0 | rust-workspace |
| toml_edit | 0.20.2 | MIT OR Apache-2.0 | rust-workspace |
| toml_edit | 0.25.17+spec-1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| toml_parser | 1.1.5+spec-1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| toml_writer | 1.1.3+spec-1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| tower | 0.5.3 | MIT | rust-workspace |
| tower-http | 0.6.11 | MIT | rust-workspace |
| tower-http | 0.7.1 | MIT | rust-workspace |
| tower-layer | 0.3.3 | MIT | rust-workspace |
| tower-service | 0.3.3 | MIT | rust-workspace |
| tracing | 0.1.44 | MIT | ocr, rust-workspace |
| tracing-attributes | 0.1.31 | MIT | rust-workspace |
| tracing-core | 0.1.36 | MIT | ocr, rust-workspace |
| tray-icon | 0.25.1 | MIT OR Apache-2.0 | rust-workspace |
| try-lock | 0.2.5 | MIT | rust-workspace |
| ttf-parser | 0.25.1 | MIT OR Apache-2.0 | rust-workspace |
| typed-path | 0.12.4 | MIT OR Apache-2.0 | excel-analyzer, rust-workspace |
| typeid | 1.0.3 | MIT OR Apache-2.0 | rust-workspace |
| typenum | 1.20.1 | MIT OR Apache-2.0 | rust-workspace |
| uds_windows | 1.2.1 | MIT | rust-workspace |
| unicase | 2.10.0 | MIT OR Apache-2.0 | rust-workspace |
| unicode-bidi | 0.3.18 | MIT OR Apache-2.0 | rust-workspace |
| unicode-bidi-mirroring | 0.4.0 | MIT OR Apache-2.0 | rust-workspace |
| unicode-ccc | 0.4.0 | MIT OR Apache-2.0 | rust-workspace |
| unicode-ident | 1.0.27 | (MIT OR Apache-2.0) AND Unicode-3.0 | excel-analyzer, ocr, rust-workspace |
| unicode-normalization | 0.1.26 | (MIT OR Apache-2.0) AND Unicode-3.0 | rust-workspace |
| unicode-properties | 0.1.4 | MIT OR Apache-2.0 | rust-workspace |
| unicode-script | 0.5.8 | MIT OR Apache-2.0 | rust-workspace |
| unicode-segmentation | 1.13.3 | MIT OR Apache-2.0 | rust-workspace |
| unicode-vo | 0.1.0 | MIT OR Apache-2.0 | rust-workspace |
| universal-hash | 0.6.1 | MIT OR Apache-2.0 | rust-workspace |
| untrusted | 0.9.0 | ISC | rust-workspace |
| ureq | 3.5.0 | MIT OR Apache-2.0 | rust-workspace |
| ureq-proto | 0.6.5 | MIT OR Apache-2.0 | rust-workspace |
| url | 2.5.8 | MIT OR Apache-2.0 | rust-workspace |
| urlpattern | 0.6.0 | MIT | rust-workspace |
| usvg | 0.47.0 | Apache-2.0 OR MIT | rust-workspace |
| utf16string | 0.2.0 | MIT OR Apache-2.0 | rust-workspace |
| utf8_iter | 1.0.4 | Apache-2.0 OR MIT | rust-workspace |
| utf8-zero | 0.8.1 | MIT OR Apache-2.0 | rust-workspace |
| uuid | 1.28.0 | Apache-2.0 OR MIT | rust-workspace |
| vcpkg | 0.2.15 | MIT OR Apache-2.0 | rust-workspace |
| vecmath | 1.0.0 | MIT | rust-workspace |
| version_check | 0.9.5 | MIT OR Apache-2.0 | rust-workspace |
| version-compare | 0.2.1 | MIT | rust-workspace |
| vsimd | 0.8.0 | MIT | rust-workspace |
| vswhom | 0.1.0 | MIT | rust-workspace |
| vswhom-sys | 0.1.3 | MIT | rust-workspace |
| walkdir | 2.5.0 | Unlicense OR MIT | rust-workspace |
| want | 0.3.2 | MIT | rust-workspace |
| wasi | 0.11.1+wasi-snapshot-preview1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| wasi | 0.14.7+wasi-0.2.4 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| wasip2 | 1.0.4+wasi-0.2.12 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| wasite | 1.0.2 | Apache-2.0 OR BSL-1.0 OR MIT | rust-workspace |
| wasm-bindgen | 0.2.129 | MIT OR Apache-2.0 | rust-workspace |
| wasm-bindgen-futures | 0.4.79 | MIT OR Apache-2.0 | rust-workspace |
| wasm-bindgen-macro | 0.2.129 | MIT OR Apache-2.0 | rust-workspace |
| wasm-bindgen-macro-support | 0.2.129 | MIT OR Apache-2.0 | rust-workspace |
| wasm-bindgen-shared | 0.2.129 | MIT OR Apache-2.0 | rust-workspace |
| wasm-streams | 0.5.0 | MIT OR Apache-2.0 | rust-workspace |
| web_atoms | 0.2.6 | MIT OR Apache-2.0 | rust-workspace |
| web_atoms | 0.3.0 | MIT OR Apache-2.0 | rust-workspace |
| web-sys | 0.3.106 | MIT OR Apache-2.0 | rust-workspace |
| web-time | 1.1.0 | MIT OR Apache-2.0 | rust-workspace |
| webkit2gtk | 2.0.2 | MIT | rust-workspace |
| webkit2gtk-sys | 2.0.2 | MIT | rust-workspace |
| webpki-root-certs | 1.0.10 | CDLA-Permissive-2.0 | rust-workspace |
| webview2-com | 0.39.1 | MIT | rust-workspace |
| webview2-com-macros | 0.8.1 | MIT | rust-workspace |
| webview2-com-sys | 0.39.1 | MIT | rust-workspace |
| weezl | 0.1.12 | MIT OR Apache-2.0 | ocr, rust-workspace |
| whoami | 2.1.3 | Apache-2.0 OR BSL-1.0 OR MIT | rust-workspace |
| winapi | 0.3.9 | MIT OR Apache-2.0 | rust-workspace |
| winapi-i686-pc-windows-gnu | 0.4.0 | MIT OR Apache-2.0 | rust-workspace |
| winapi-util | 0.1.11 | Unlicense OR MIT | rust-workspace |
| winapi-x86_64-pc-windows-gnu | 0.4.0 | MIT OR Apache-2.0 | rust-workspace |
| window-vibrancy | 0.8.1 | Apache-2.0 OR MIT | rust-workspace |
| windows | 0.62.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_aarch64_gnullvm | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_aarch64_gnullvm | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_aarch64_msvc | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_aarch64_msvc | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_i686_gnu | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_i686_gnu | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_i686_gnullvm | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_i686_msvc | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_i686_msvc | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_gnu | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_gnu | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_gnullvm | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_gnullvm | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_msvc | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows_x86_64_msvc | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows-collections | 0.3.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-core | 0.62.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-future | 0.3.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-implement | 0.60.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-interface | 0.59.3 | MIT OR Apache-2.0 | rust-workspace |
| windows-link | 0.2.1 | MIT OR Apache-2.0 | ocr, rust-workspace |
| windows-numerics | 0.3.1 | MIT OR Apache-2.0 | rust-workspace |
| windows-registry | 0.6.1 | MIT OR Apache-2.0 | rust-workspace |
| windows-result | 0.4.1 | MIT OR Apache-2.0 | rust-workspace |
| windows-strings | 0.5.1 | MIT OR Apache-2.0 | rust-workspace |
| windows-sys | 0.45.0 | MIT OR Apache-2.0 | rust-workspace |
| windows-sys | 0.52.0 | MIT OR Apache-2.0 | rust-workspace |
| windows-sys | 0.61.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-targets | 0.42.2 | MIT OR Apache-2.0 | rust-workspace |
| windows-targets | 0.52.6 | MIT OR Apache-2.0 | rust-workspace |
| windows-threading | 0.2.1 | MIT OR Apache-2.0 | rust-workspace |
| windows-version | 0.1.7 | MIT OR Apache-2.0 | rust-workspace |
| winnow | 0.5.40 | MIT | rust-workspace |
| winnow | 1.0.4 | MIT | rust-workspace |
| winreg | 0.56.0 | MIT | rust-workspace |
| wit-bindgen | 0.57.1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | rust-workspace |
| write-fonts | 0.48.1 | MIT OR Apache-2.0 | rust-workspace |
| writeable | 0.6.4 | Unicode-3.0 | rust-workspace |
| wry | 0.57.0 | Apache-2.0 OR MIT | rust-workspace |
| x11 | 2.21.0 | MIT | rust-workspace |
| x11-dl | 2.21.0 | MIT | rust-workspace |
| xattr | 1.6.1 | MIT OR Apache-2.0 | rust-workspace |
| xmlwriter | 0.1.0 | MIT | rust-workspace |
| xmp-writer | 0.3.3 | MIT OR Apache-2.0 | rust-workspace |
| yoke | 0.8.4 | Unicode-3.0 | rust-workspace |
| yoke-derive | 0.8.4 | Unicode-3.0 | rust-workspace |
| zbus | 5.19.0 | MIT | rust-workspace |
| zbus_macros | 5.19.0 | MIT | rust-workspace |
| zbus_names | 4.3.5 | MIT | rust-workspace |
| zcheapstr | 1.1.0 | MIT | rust-workspace |
| zerocopy | 0.8.63 | BSD-2-Clause OR Apache-2.0 OR MIT | ocr, rust-workspace |
| zerocopy-derive | 0.8.63 | BSD-2-Clause OR Apache-2.0 OR MIT | ocr, rust-workspace |
| zerofrom | 0.1.8 | Unicode-3.0 | rust-workspace |
| zerofrom-derive | 0.1.8 | Unicode-3.0 | rust-workspace |
| zeroize | 1.9.1 | Apache-2.0 OR MIT | rust-workspace |
| zerotrie | 0.2.5 | Unicode-3.0 | rust-workspace |
| zerovec | 0.11.9 | Unicode-3.0 | rust-workspace |
| zerovec-derive | 0.11.7 | Unicode-3.0 | rust-workspace |
| zip | 9.0.5 | MIT | excel-analyzer, rust-workspace |
| zlib-rs | 0.6.8 | Zlib | excel-analyzer, ocr, rust-workspace |
| zmij | 1.0.23 | MIT | excel-analyzer, ocr, rust-workspace |
| zopfli | 0.8.4 | Apache-2.0 | excel-analyzer, rust-workspace |
| zune-core | 0.5.3 | MIT OR Apache-2.0 OR Zlib | ocr, rust-workspace |
| zune-jpeg | 0.5.15 | MIT OR Apache-2.0 OR Zlib | ocr, rust-workspace |
| zvariant | 5.15.1 | MIT | rust-workspace |
| zvariant_derive | 5.15.1 | MIT | rust-workspace |
| zvariant_utils | 4.2.0 | MIT | rust-workspace |

## native-resource (5)

| Package | Version | Declared license | Used by |
|---|---:|---|---|
| bblanchon.pdfium.linux | 158.0.8111 | Apache-2.0 | Rust native library only; managed assemblies excluded |
| bblanchon.pdfium.macos | 158.0.8111 | Apache-2.0 | Rust native library only; managed assemblies excluded |
| bblanchon.pdfium.win32 | 158.0.8111 | Apache-2.0 | Rust native library only; managed assemblies excluded |
| microsoft.ml.onnxruntime | 1.31.0 | MIT | Rust native library only; managed assemblies excluded |
| postgresql-client | 18.6 | PostgreSQL | Rust web/server backup and migration clients; excluded from desktop |

## Windows OCR app-local CRT assets

Microsoft Visual C++ 14.51.36247.0; Microsoft redistribution terms apply. Full installer excluded from customer packages.

| Architecture | File | Bytes | SHA-256 |
|---|---|---:|---|
| x64 | vcruntime140.dll | 178616 | d1f4225df2cd877dbf130d5668a021dce3f94118455ff5ec952061c30afc9ce7 |
| x64 | vcruntime140_1.dll | 50112 | a7146c08f89fe5b04541ab507cdb59ff7b44534d4ba3c668a426c6450a03434e |
| x64 | msvcp140.dll | 643512 | 7c26614e1d733892c2deac7e245ce115504b1d80592dd0a01b08e3e5a55f89ca |
| x64 | msvcp140_1.dll | 35768 | 206c931bf90fdad8816de3b5e2ef80b2bcaa9406c89ecc05fe6fddffe251e982 |
| arm64 | vcruntime140.dll | 246112 | 3c56f4167e2b3d8e6338497731e6aae8cd7ec46bd6789f9423a8d9cf9a630310 |
| arm64 | msvcp140.dll | 1588064 | 167ceac85c2d726c4cd9e39b8881fafdc6de1520c0e67c4f6b271235f9d2a6c5 |
| arm64 | msvcp140_1.dll | 50528 | c7713c2061b29d24de4874923522ada0815cfca80d09265c181e81d16c7c42d6 |

Source and redistribution notice: `VisualCppRuntime/visual-cpp-runtime.json` and `VisualCppRuntime/README.md`.
