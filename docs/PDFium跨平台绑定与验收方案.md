# PDFium 跨平台绑定与验收方案

> 2026-09-20：对照当前 Rust 源码、PDFium `chromium/7961` / `chromium/8057` 公共头文件、`pdfium-render 0.9.4` 发布归档及本地 Windows/Linux x64 原生资源。本文修正绑定选型与 ABI 说明，不表示已替换依赖或完成目标平台运行验收。

## 1. 当前事实与推荐方向

**报表继续采用 krilla + PDFium。建议长期使用 `pdfium-render` 替换手写 PDFium 绑定；完成兼容性和功能验收前，现有手写绑定继续运行。** 这是维护方式的建议，不改变 krilla 的排版/PDF 生成职责，也不增加第二套 PDF 实现。

| 事项 | 当前事实 | 修正方向 |
| --- | --- | --- |
| 报表生成 | `export-doc-report` 使用 krilla | 继续补齐原版报表模型、模板语义与原生布局 |
| PDF 处理 | engine 的 `pdf/native.rs` 使用 `libloading` 直接加载 PDFium 动态库，手写函数指针 | 将实现限制在 PDF 适配模块/worker；优先迁移到经过验证的 `pdfium-render` |
| 运行隔离 | PDF 预览、提取、OCR 页图、合并通过受控子进程 | 无论使用何种绑定都保留隔离、超时、取消与容量限制 |
| 封装版本 | 未引入 `pdfium-render`；本次查询最新稳定为 `0.9.4`，`MIT OR Apache-2.0` | 固定 crate、API feature、原生包和 RID，作为独立依赖变更 |
| 原生版本 | 清单固定 `152.0.7961`；本次发布源最新非预发布包为 `155.0.8057` | 独立验证升级；封装版本与原生版本分别记录 |

使用 `pdfium-render` 后，底层仍是 C++ PDFium 动态库；封装不会将它变成纯 Rust，也不会自带与所有 feature 匹配的本地库。

## 2. 调用约定与符号名必须分开核对

**调用约定决定参数/返回值/栈如何传递，导出符号名决定动态加载器查找哪个入口。** 修改 Rust `extern` 字符串不会自动改变 `Library::get()` 查找的名称；找到符号也不能证明其函数指针类型正确。ABI 错误可在调用时导致崩溃或内存损坏，不能统称为“找不到符号”。

### 2.1 PDFium 头文件的实际条件

本次核对两个修订的 `public/fpdfview.h` 均为：

```c
#if defined(WIN32) && defined(FPDFSDK_EXPORTS)
#define FPDF_CALLCONV __stdcall
#else
#define FPDF_CALLCONV
#endif
```

公共 API 以 `extern "C"` linkage 导出，函数声明带 `FPDF_CALLCONV`；Windows 是否启用该宏还与原生构建定义有关。不能仅根据“.dll”后缀推断所有构建的 ABI，更不能把 Linux/macOS 都写成 x86 专用的 `extern "cdecl"`。

Rust 官方说明：`extern "system"` 除 Windows 32 位 x86 非可变参数函数外，等价于 `extern "C"`；Windows x86_32 才映射为 `stdcall`。因此当前源码对公共函数使用 `unsafe extern "system" fn`，**本身并不构成 Linux/macOS ABI 错误**。

### 2.2 本项目平台矩阵

| 目标 | 调用 ABI 要求 | 动态库/符号查找 | 当前证据边界 |
| --- | --- | --- | --- |
| Windows x64 MSVC | 平台 x64 ABI；`system` 与 `C` 在此相同，不能套用 x86 `_name@N` 规则 | `pdfium.dll`；查实际导出的 `FPDF_*` 名 | 本地 x64 DLL 静态导出检查；MSVC 完整运行仍待验 |
| Windows x64 GNU | 同样核对 x64 C ABI；不能因 GNU/MSVC 编译器名字不同就加符号后缀 | 同上；同时检查依赖 DLL 和机器类型 | 当前本地联调工具链；本轮未重跑 PDF 功能 |
| Windows ARM64 | Windows ARM64 ABI；不使用 x86 `stdcall` 名称修饰 | ARM64 `pdfium.dll`，不能装入 x64 DLL | 待目标 runner/设备 |
| Linux x64 / ARM64 | 对应目标的 C ABI，使用 `extern "C"` 或等价的 `system` | `libpdfium.so`；精确 `FPDF_*` 名；核对 ELF 机器类型、依赖及最低运行库要求 | 本地 x64 ELF 静态导出检查；不等于在 Linux 调用成功，ARM64 待验 |
| macOS ARM64 | Darwin ARM64 C ABI，使用 `extern "C"` 或等价的 `system` | `libpdfium.dylib`；通过 `dlsym`/`libloading` 查 **不带前导下划线** 的 `FPDF_*` | 待 macOS runner/设备；核对 Mach-O arm64 slice、依赖与最低系统版本 |

macOS 的 `nm` 输出可显示 `_FPDF_*`，这是 Mach-O 工具展示的符号形式；Apple `dlsym` 文档明确要求查询名不要加前导下划线。不能把 `nm` 输出原样当成 `libloading` 查询字符串。

Windows 32 位 x86 不在当前支持矩阵内。若以后正式增加，需同时核对原生构建宏、函数/回调 ABI 与实际导出表；`_FPDF_Name@N` 是否存在由该构建决定。不能为尚未支持的 x86 添加猜测式多名称重试。

### 2.3 回调、整数宽度和结构布局

`FPDF_SaveAsCopy` 是带 `FPDF_CALLCONV` 的 API；`fpdf_save.h` 中的 `FPDF_FILEWRITE::WriteBlock` 却声明为普通 C 函数指针，没有 `FPDF_CALLCONV`。**该回调须使用 `extern "C"`，不能跟随公共 API 一律改为 `system/stdcall`。** 当前源码在这一点上已分开处理。其它回调逐个检查相应头文件，不从 WriteBlock 推断全部回调。

- `FPDF_DWORD` 在这两个修订中为 `unsigned long`，回调 `size` 也是 `unsigned long`。Rust 使用 `std::ffi::c_ulong`：Windows 64 位为 32 位，Linux/macOS 本项目 64 位目标为 64 位，不能统一替换成 `u32` 或 `u64`。
- `size_t` 对应 `usize`；C `int`/`char` 使用 `c_int`/`c_char`，句柄和缓冲区使用准确的指针类型；不要把所有整数机械转换为 `i64`。
- C 结构使用 `#[repr(C)]`，逐字段核对顺序、偏移、对齐与函数指针类型。传入缓冲区、writer、回调状态及动态库必须活到原生调用/文档/页面释放结束。
- Rust panic 不得穿越 C 边界；回调的容量拒绝、错误返回及资源回收应符合原头文件约定。`Library::get::<T>` 不会验证这些合同。

## 3. 继续手写期间的集中声明示例

当前 `symbol!` 宏只做符号查找，调用方重复传入完整函数签名。若在迁移前整理手写绑定，应把公共 API 的类型声明集中到一个模块；下面示例覆盖本项目 Windows/Linux/macOS，**只作为文档方案，本轮未写入生产代码**。现有统一 `extern "system"` 在当前目标上已等价，此宏主要用于显式表达边界、避免签名分散。

```rust
use std::ffi::{c_char, c_int, c_ulong, c_void};

type FpdfDocument = *mut c_void;

macro_rules! pdfium_api_type {
    ($name:ident = fn($($arg:ty),* $(,)?) -> $ret:ty) => {
        #[cfg(target_os = "windows")]
        type $name = unsafe extern "system" fn($($arg),*) -> $ret;

        #[cfg(any(target_os = "linux", target_os = "macos"))]
        type $name = unsafe extern "C" fn($($arg),*) -> $ret;
    };
}

#[repr(C)]
struct FpdfFileWrite {
    version: c_int,
    // 此回调无 FPDF_CALLCONV，不能套用公共 API 宏。
    write_block: unsafe extern "C" fn(
        *mut FpdfFileWrite,
        *const c_void,
        c_ulong,
    ) -> c_int,
}

pdfium_api_type!(InitLibrary = fn() -> ());
pdfium_api_type!(LoadMemDocument64 = fn(
    *const c_void, usize, *const c_char
) -> FpdfDocument);
pdfium_api_type!(SaveAsCopy = fn(
    FpdfDocument, *mut FpdfFileWrite, c_ulong
) -> c_int);

const INIT_LIBRARY_SYMBOL: &[u8] = b"FPDF_InitLibrary\0";
// 已通过路径和原生包验证的 library：
// library.get::<InitLibrary>(INIT_LIBRARY_SYMBOL)
```

类型宏不拼 `_` 或 `@N`，不更改导出名；缺符号时报告明确库路径、目标架构、固定版本、缺失名称及加载器原因，并 fail-closed。需要的函数表在 worker 初始化时一次解析完成，开始业务前发现缺失；不能仅在合并最后保存时才发现缺少入口。

本项目现有目标均为 64 位，上述 Windows 分支与 C ABI 等价；此示例不是对未来任意 Win32 PDFium 构建的支持承诺。若继续手写，应从与实际二进制对应的头文件审查/生成声明，而不是维护未经核对的手工签名集合。

## 4. 为什么更推荐 `pdfium-render`

| 对比项 | 手写 `libloading` + C ABI | `pdfium-render` |
| --- | --- | --- |
| 维护范围 | 自行维护函数签名、结构、回调、对象释放与版本变化 | 复用专门维护的类型和较高层接口，减少本项目 unsafe 面积 |
| 符号需求 | 当前适配器只使用 19 个不同入口 | 动态绑定器初始化时加载更大的 API 集；必须检查全部启用入口，不能仅复用 19 项检查 |
| 业务边界 | 可精确实现现有上限和 worker 协议 | 保留同一适配接口、受限 writer 和 worker，不能因换封装取消限制 |
| 版本风险 | 自行审查 ABI/符号，使用面较小 | crate/API feature/原生库仍需匹配，封装不能修复打包错架构或缺失符号 |
| 并发/生命周期 | 项目负责 PDFium 全局初始化和文档/页面/文本/位图的释放顺序 | 可复用生命周期接口和 `thread_safe` 包装；仍须遵守 PDFium 的调用串行化与进程边界 |

本次直接读取 crates.io 发布的 `pdfium-render-0.9.4.crate`，确认：

1. 默认 feature 是 `pdfium_latest`、`image_latest`、`thread_safe`；其中 **`pdfium_latest = ["pdfium_7881"]`**，`image_latest = ["image_025"]`。不能从名称推断它已提供 7961/8057 专属绑定，也不能编造 `pdfium_8057` feature。
2. 动态绑定器声明公共函数为 `unsafe extern "C" fn`，按 `FPDF_*` 原名查找。对本项目现有 64 位平台，这与现有 `system` ABI 一致；不能宣称该库自动解决所有 Win32 `stdcall` 构建问题。
3. `Pdfium::bind_to_library(path)` 支持指定库路径。`Pdfium::default()` 会先尝试当前目录，再尝试系统库并可能 panic，不符合本项目受管路径要求；迁移时必须显式传入 `RuntimePaths` 派生、已核验的绝对路径，禁止系统回退。
4. `Pdfium` 使用进程级 bindings 初始化状态；worker 内统一初始化和持有 PDFium，再创建/关闭文档，不在每个文档中重复绑定/初始化/销毁全局库。
5. 提供页面渲染、文本提取、页面复制/追加及 `save_to_writer` 等接口，可以承接现有预览/OCR/合并职责。迁移仍需验证输出上限、错误传播与资源生命周期，不能直接用无限制 `save_to_bytes` 取代受限 writer。

**实施建议：** 将 `pdfium-render = 0.9.4` 作为优先候选，关闭默认 feature 后显式选择经过验证的 API/图像/线程策略；`pdfium_7881` 是待验证候选，尚未证明与 `152.0.7961` 或 `155.0.8057` 所需的全部符号兼容。先做独立兼容试验，再锁定最终组合。不要为了“最新”启用 `pdfium_future` 或同时启用多个版本 feature。迁移通过后移除被替代的手写绑定，不长期保留双实现；不需要为过渡阶段先扩建一整套自定义 FFI 框架。

## 5. 模块化联动与验收

```text
export-doc-report：数据投影 / 模板 / 布局 → krilla → PDF 字节
engine：任务 / 权限 / 路径 / 超时取消 → 受控 PDF worker
worker：单一 PDF 适配器 → pdfium-render（拟迁移）→ 随包 PDFium
                                               → 预览 / 文字 / OCR 页图 / 合并
```

迁移只改变 PDF 适配器内部；React、Tauri command、HTTP、报表布局和 OCR 用例不得直接依赖绑定类型。原生库由组合根选择并验证，各模式复用同一 worker 实现；Tauri WebView 不承担服务端 PDFium 加载。

| 验收层 | 必须记录的结果 |
| --- | --- |
| 原生资源 | 固定来源、版本、RID、归档哈希/许可；动态库机器类型与运行进程一致；依赖库可解析 |
| 符号/ABI | 当前手写需全部 19 项；迁移后需封装在选定 feature 下加载的全部符号；回调/结构/整数宽度与头文件一致 |
| 加载失败 | 缺库、错架构、缺依赖、缺符号分别可诊断；无系统库回退，无改名/换 ABI 试调用 |
| 功能 | krilla 生成的中文/英文/图片/多页 PDF 经 PDFium 打开，预览、文字提取、OCR 页图、合并保存后重开均正确 |
| 边界 | 原页数/像素/输入/输出/内存限额，坏文件/密码文件、超时/取消、worker 异常退出及资源回收；受限 writer 拒绝超限输出 |
| 平台 | Windows x64 MSVC、Windows ARM64、Linux x64/ARM64、macOS ARM64 分别运行；Windows GNU 本机结果只作其自身证据 |
| 依赖交付 | `cargo fmt --all --check`、`cargo test --locked --workspace`、`cargo check --locked --workspace --all-features`、平台 locked build、notices/SBOM、治理 `unresolved=0 / disallowed=0`；不混入 krilla 替换或无关依赖升级 |

### 本次实际核查

- 从 `pdf/native.rs` 提取到 19 个不同 `FPDF_*` / `FPDFText_*` / `FPDFBitmap_*` 查询名。
- 对本地 Windows x64 `pdfium.dll` 用 `objdump -p` 检查，19/19 名称存在，未修饰；机器类型为 `pei-x86-64`。
- 对本地 Linux x64 `libpdfium.so` 用 `objdump -T` 检查，19/19 名称存在，未修饰；机器类型为 `elf64-x86-64`。
- 文档中的 Rust 类型宏已用本机 `rustc 1.98.1`、`x86_64-pc-windows-gnu` 做 `--emit=metadata` 编译检查通过；只证明本机分支语法/类型可编译，不证明动态加载或其它目标 ABI 已实跑。两份方案文档共 67 个本地链接和 `git diff --check` 通过。
- 本次没有在 Linux 调用 PDFium，没有检查 macOS/ARM64 二进制，也没有实跑 `pdfium-render`。静态导出检查不能写成三平台运行通过；没有依据把当前代码定性为已发生 stdcall/cdecl 符号故障。
- 当前源码与依赖保持原实现。本次只修正文档，并将下载的封装源归档保存在忽略目录 `.codex-runtime/pdfium-abi-audit-20260920/` 用于复核。

## 6. 证据与链接

- 项目：[手写绑定](../crates/export-doc-engine/src/pdf/native.rs)、[worker 入口](../crates/export-doc-engine/src/pdf.rs)、[合并边界](../crates/export-doc-engine/src/pdf/merge.rs)、[原生清单](../eng/native-runtime-packages.json)。
- PDFium：[7961 fpdfview.h](https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/7961/public/fpdfview.h)、[7961 fpdf_save.h](https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/7961/public/fpdf_save.h)、[8057 fpdfview.h](https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/8057/public/fpdfview.h)、[8057 fpdf_save.h](https://pdfium.googlesource.com/pdfium/+/refs/heads/chromium/8057/public/fpdf_save.h)。
- ABI：[Rust Reference](https://doc.rust-lang.org/reference/items/external-blocks.html#abi)、[Microsoft __stdcall](https://learn.microsoft.com/en-us/cpp/cpp/stdcall?view=msvc-170)、[Apple dlsym](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/dlsym.3.html)。
- 封装：[0.9.4 发布归档](https://static.crates.io/crates/pdfium-render/pdfium-render-0.9.4.crate)、[版本元数据](https://crates.io/api/v1/crates/pdfium-render/0.9.4)、[0.9.4 API 文档](https://docs.rs/pdfium-render/0.9.4/pdfium_render/)。所读归档 SHA-256：`8948a803616a9e936b15a6637af2cd48c5fb8ae0fcdeb3c32eac3a540e255a19`；核查文件为 `Cargo.toml.orig`、`src/bindings/dynamic_bindings.rs`、`src/bindgen/pdfium_7881.rs`、`src/pdfium.rs`。
- 关联：[后端差距及修正方案](./Rust与CSharp后端功能差距及修正方案.md)、[Rust 桌面平台适配与验收](./Rust桌面平台适配与验收.md)、[当前架构事实](./当前架构事实.md)。

## 7. `pdfium-sys + krilla` 是否更适合可视化设计器

### 7.1 本次核实的是哪个 `pdfium-sys`

这里明确区分 crates.io 上的 **`pdfium-sys` 包**与“自行生成一个 PDFium sys 层”的架构做法。2026-09-20 查询前者，最新未撤回、无预发布后缀的发布版本为 **0.1.1，发布于 2021-03-31**，许可证 `MIT OR Apache-2.0`；不是 PDFium 官方维护的 Rust SDK。仓库地址现重定向至 `halaaro/pdfium-sys`；不能仅凭名称推断它比 `pdfium-render` 更完整或跨平台适配更好。

直接核查 `pdfium-sys-0.1.1.crate` 得到：

| 证据 | 对本项目的影响 |
| --- | --- |
| README 明确写 `Only tested on Windows` | Linux/macOS/ARM64 的支持仍需由本项目承担验证；不能当作已完成三平台适配 |
| 默认 feature 启用 `bindgen 0.54`，要求 Clang 与外部 PDFium 头文件 | 引入生成工具及头文件环境依赖；不能把构建时任意找到的头文件当作当前随包库的准确 ABI |
| `wrapper.h` 只 include `fpdfview.h`，并在 `_WIN32` 定义时将其 undef | 不是完整的文本/保存/编辑头文件集合，也没有自动提供本项目所需的完整 ABI 策略；需按实际目标和构建宏重新核对 |
| `build.rs` 声明 `rustc-link-lib=dylib=pdfium.dll`（Windows）或 `dylib=pdfium`（其它平台） | 属于链接阶段建立动态库依赖，不是当前 `libloading` 按受管绝对路径延迟加载；要重新设计链接搜索、DLL/so/dylib 布局及加载失败行为 |
| bindgen 输出写入 `src/bindings.rs` | 未使用构建输出目录隔离生成物；直接采用会增加可复现构建/缓存管理的审查工作 |
| 随包 `bindings.rs` 只声明当前所需入口的 **12/19** | 缺少 `FPDFText_LoadPage`、`FPDFText_ClosePage`、`FPDFText_CountChars`、`FPDFText_GetText`、`FPDF_CreateNewDocument`、`FPDF_ImportPages`、`FPDF_SaveAsCopy`，也没有 `FPDF_FILEWRITE`；不能直接承接现有文字提取与合并保存 |

最后一行是对发布包所带声明的静态检查，不表示本次运行了它。启用 bindgen 可以根据提供的头文件重新生成部分声明，但 `wrapper.h` 本身未包含上述其它公共头文件，不能把默认生成流程当成这些缺口已自动消失。

**因此不建议直接引入这个已发布的 `pdfium-sys 0.1.1` 替换当前适配器。** 它不会改变 PDFium 的实际光栅化质量，也不会替项目管理句柄、回调、线程、受限 writer、取消或模板布局；这些工作仍需在上层实现。链接式动态依赖还可能把“使用 PDF 功能时诊断缺库”变成进程启动加载阶段失败，必须单独验证，不能破坏当前可选能力边界。

### 7.2 设计器真正需要的分工

源码现状是：

- `ReportDesignerV3Workspace` / `ReportDesignerV3Canvas` 负责 React 元素编辑、选中、拖动、缩放、撤销和表格单元格操作，编辑对象来自 V3 模板结构。
- `useReportTemplatePreviewMutations` 调用现有 HTML 预览接口，`ReportTemplatePreviewWorkspace` 用 iframe 的 `srcDoc` 显示结果；Rust `Document::html()` 将分页 SVG 包装为 HTML。
- krilla 将 report 模块生成的页面内容编码为 PDF。PDFium worker 已有 PDF 渲染能力，但**设计器上述 HTML 预览并未因存在这个 worker 就自动变成最终 PDF 的像素预览**。

krilla 是 PDF 生成库，完整 Grid/Conditional/复杂明细分组、字体测量、分页和高级 HTML 求值仍属于本项目报表模型与布局器。`pdfium-sys` / `pdfium-render` 都位于生成 PDF 之后，换绑定不会补齐这些缺口。

建议采用下面的分工，继续保留原版 React 编辑体验：

```mermaid
flowchart LR
  Editor[React 可视化设计器 / V3 草稿] --> Layout[Rust 校验 / 数据绑定 / 测量 / 分页]
  Layout --> Writer[krilla 生成 PDF]
  Writer --> Output[原 PDF 下载 / 打印 / 归档]
  Writer --> Worker[隔离 PDFium worker]
  Worker --> Preview[最终输出预览]
```

1. **编辑反馈**：拖动、文本输入、对齐、选区、缩放继续在 React 中即时反馈。不要每次鼠标移动都重新生成 PDF 或启动 PDFium，也不要用不可编辑的整页位图替换模板结构。
2. **排版模型**：后端作为数据、字体测量与分页的事实源，内部保留页面尺寸、元素定位、表格及分页结果。前端只做编辑反馈和布局结果展示，不再发展一套独立的最终分页业务规则。
3. **最终输出预览**：显式预览或编辑暂停后，按草稿修订生成 PDF；PDFium 渲染这份 PDF 的当前页/缩略图。请求带修订标识，取消旧请求并拒绝旧结果覆盖新草稿；按页加载，限制 DPI、像素与内存。预览不写入正式业务数据。
4. **输出一致性**：下载/打印使用与预览同一模板、数据、字体和资源快照产生的原 PDF。不能把 PDFium 的位图重新编码成正式 PDF；若界面要保留文字选择，需要文字层/坐标映射，不能声称纯图片预览具备原有全部交互。
5. **API 与权限**：沿用受控任务、缓存和授权边界；如需增加草稿 PDF 预览/页图契约，按正式 OpenAPI 流程更新，不维护另一套私有接口。缓存不得跨用户/公司泄露，撤权后重新访问仍校验权限。

以上是待实施方案；没有将当前 HTML 预览或打印链路标记为已改造。是否需要从即时画布切换到 PDF 输出预览属于页面流程实现，和选用哪个 FFI crate 是两个层次。

### 7.3 三种选择的项目适配结论

| 选择 | 适配本项目的结论 |
| --- | --- |
| crates.io `pdfium-sys 0.1.1` + krilla | **不作为优先方案**：发布较旧、仅声明 Windows 测试、接口集合不满足现有用途，加载方式也需调整；收益不足以抵消迁移工作 |
| 自建最小 sys 层 + krilla | **可行的备选**：从锁定原生库的头文件和目标宏生成所需声明，集中动态加载，只暴露安全业务接口；可缩小符号面，但生命周期、回调、ABI 和全部平台验收仍由项目长期承担 |
| `pdfium-render` + krilla | **优先验证方案**：高层接口覆盖预览、文字、复制/合并与保存，减少项目内重复 FFI 工作；保留独立适配模块、固定 feature、受管加载及 worker，上游封装不保证原生库任意版本兼容 |

自建 sys 层应是同一 PDF 适配器内部实现，不能与高层封装并存成第二套业务链路。只有在独立验证确认 `pdfium-render` 存在本项目所需能力无法满足、固定原生库无法匹配等具体阻碍时，才优先考虑这个备选；不是因为 sys 名称更接近底层就默认选择。

本次没有性能基准证明高层绑定是瓶颈，不能宣称 `pdfium-sys` 会让设计器明显更快。后续应分别测量布局、PDF 编码、worker 启动、PDFium 渲染及页图传输的耗时，再优化实际瓶颈。**当前优先工作仍是补完整报表模型，并让最终预览使用实际输出 PDF。**

核查来源：[pdfium-sys 注册表](https://crates.io/api/v1/crates/pdfium-sys)、[0.1.1 发布归档](https://static.crates.io/crates/pdfium-sys/pdfium-sys-0.1.1.crate)、[当前仓库](https://github.com/halaaro/pdfium-sys)、[pdfium-render 注册表](https://crates.io/api/v1/crates/pdfium-render)。`pdfium-sys` 归档 SHA-256：`4f48ada0387b3b05c0490de19b51f015c9a90db7de8cef2e5dad7bf2eb1af1c4`；检查了 `README.md`、`Cargo.toml.orig`、`build.rs`、`wrapper.h`、`src/bindings.rs` 和 `src/lib.rs`。

项目源码：[V3 工作区](../apps/export-doc-web/src/features/report-designer/ReportDesignerV3Workspace.tsx)、[V3 画布](../apps/export-doc-web/src/features/report-designer/ReportDesignerV3Canvas.tsx)、[预览请求](../apps/export-doc-web/src/features/reports/useReportTemplatePreviewMutations.ts)、[预览显示](../apps/export-doc-web/src/features/reports/ReportTemplatePreviewWorkspace.tsx)、[HTML/SVG 与 PDF 输出](../crates/export-doc-report/src/document.rs)。本次未引入依赖或运行该 sys 包，不宣称任何跨平台加载/性能测试已通过。

## 8. `firecrawl/pdfium-rs` 补充评估

### 8.1 项目身份与真实能力

2026-09-20 核对 [firecrawl/pdfium-rs](https://github.com/firecrawl/pdfium-rs)；它发布的 crate 名为 **`firecrawl-pdfium`**，不是 crates.io 的 `pdfium-sys` 或另一个名为 `pdfium` 的包。最新未撤回、无预发布后缀版本为 **0.1.0，2026-08-11 发布**；Cargo 和许可文件明确为 **`MIT OR Apache-2.0`**。它仍处于早期 0.x，发布历史较短；GitHub 页面自动识别单一许可证不能代替包清单和许可证原文。

直接核查发布归档的 `Cargo.toml.orig`、`src/sys/bindings.rs`、`src/library.rs`、`src/render.rs` 等源码，结论如下：

| 项目 | 已查明事实 | 本项目适配判断 |
| --- | --- | --- |
| 加载 | 仅依赖 `libloading 0.8`，没有构建脚本/构建期 PDFium 链接；支持 `load_from_path` 指定动态库 | 比旧 `pdfium-sys` 更吻合当前受管加载和 worker 方案；仅使用经过 RuntimePaths/哈希验证的绝对路径 |
| 默认发现 | `Pdfium::load()` 可查环境变量、可执行文件目录、开发目录及系统库 | 不使用此自动发现入口，不引入另一个默认资源目录或自动下载流程 |
| 绑定 | 内部有人工转录的 `sys` 函数表，49 个入口一次解析，统一 `extern "C"`、原始符号名 | 减少项目自行维护的声明，但不意味着完全没有手写 FFI；符合本项目当前 64 位 ABI 前提，仍需头文件/结构/实际调用验证 |
| 预览 | 输出自有像素缓冲区，支持尺寸/DPI/旋转/颜色格式等控制 | 适合 PDF 输出预览和缩略图；不把页图重新编码成正式 PDF |
| 文字/OCR | 按字符提取文字和位置，提供 PDF 页面坐标与像素坐标转换 | 适合 OCR 框回映、文字高亮；不是模板组件身份/分组/条件信息，不能从 PDF 自动重建 V3 设计器 |
| 并发 | 安全 API 调用经进程级互斥锁串行化，返回的像素/文字结果由调用方持有 | 契合 worker 内部串行调用；`Send + Sync` 不代表 PDFium 可以并行渲染，吞吐仍靠有上限的进程调度 |
| 生命周期 | 每进程初始化一次，库一直保留到进程退出；再次显式加载不同路径报 AlreadyLoaded | 在隔离 worker 中易于管理；不在应用进程混用独立绑定的初始化/销毁和锁 |
| 默认资源限制 | 渲染默认像素缓冲上限 1 GiB，文字默认每页上限 100 万字符 | 必须覆盖为项目原有更严格的页数、总文字、像素和容量上限，保留超时/取消/异常退出隔离；默认值不能直接沿用 |
| 合并/保存 | 明确排除 PDF 编辑/保存；其函数表没有 `FPDF_CreateNewDocument`、`FPDF_ImportPages`、`FPDF_SaveAsCopy`、`FPDF_SaveWithVersion` | 无法直接替换已有 PDF 合并和单据包合并链路；公共 `sys` 只暴露已绑定函数，不能据此声称缺失 API 已可调用 |

krilla 可以把本程序多个报表页面组织成 PDF，但这不能替代当前对**用户上传的任意已有 PDF**进行合并的需求；不得为了使用这个封装而删除该功能、将输入 PDF 光栅化或静默改变文档内容。

### 8.2 版本与跨平台证据

上游 `pdfium.lock.json` 固定测试原生版本 **`chromium/7988` / `153.0.7988.0`**，与本项目当前 `152.0.7961` 以及本次查询的最新非预发布包 `155.0.8057` 不同。无需直接采用它的原生分发渠道，优先保留本项目中央原生资源清单；更换渠道/版本另做来源、许可、哈希和打包验收。

上游 README 声明 CI-tested 的平台为 Windows x64、Linux glibc x64/ARM64、macOS x64/ARM64，当前 CI 文件也包含这些 runner；**Windows ARM64 仅声明预期可用，未列入其 CI 测试矩阵**。本次只核查声明与工作流源码，没有把上游 CI 历史或本项目目标平台功能运行重新验收。

对 `firecrawl-pdfium 0.1.0` 发布包提取全部 **49 个绑定入口**，在本地已有 Windows x64 DLL 和 Linux x64 SO 的静态导出表中均为 **49/49 存在**。这比只检查本项目当前 19 项更有参考价值，但仍不能证明 ABI、回调、内存布局与功能完全兼容；没有在 Linux/macOS/ARM64 运行，也没有运行此封装。上游“符号齐全即可兼容”的描述在本项目只作为必要条件，不代替原生来源和真实调用检查。

### 8.3 对本项目的建议

**它应纳入预览/OCR 的有力候选，比旧 `pdfium-sys 0.1.1` 更贴近需求；若要用一个封装接管现有 PDF 全部职责，当前仍优先验证 `pdfium-render`。**

| 评价范围 | 建议 |
| --- | --- |
| 仅考虑设计器最终输出预览、字符位置和 OCR 框映射 | 优先纳入 `firecrawl-pdfium` 对照试验：接口集中、依赖少、受管动态加载方便；未实测前不宣称更快或更稳定 |
| 同时需要当前预览、提取、OCR 页图、合并保存 | `pdfium-render` 的能力覆盖更完整；Firecrawl 0.1.0 的合并/保存缺口是明确阻碍 |
| 希望采用 Firecrawl 并补合并 | 先解决同一适配层/绑定和生命周期内的扩展方案及维护责任，再做选择；当前不存在完整迁移条件 |

不建议直接在同一进程引入 Firecrawl 与另一套 PDFium 封装分别“负责预览/合并”。两套绑定各自的全局锁和初始化/销毁并不互相协调，可能破坏同一 PDFium 实例的串行化与生命周期；即便放入不同进程，也增加双依赖和双维护面。按本项目模块化原则，最终选择一种内部绑定实现，外部继续只依赖统一 PDF 适配接口。

选型试验只需比较与本项目有关的场景：同一 krilla 生成 PDF 的中文字体/印章/多页预览，像素↔页面坐标、旋转页、文字位置、OCR 输入上限、坏文件、超时/取消、worker 生命周期；同时验证合并保存完整性。测试前固定版本与数据，分别记录静态证据、实际功能、性能和未完成平台。选用任何候选都不改变 React 画布、Rust 模板模型/分页与 krilla 生成的既定分工。

来源：[crate 注册表](https://crates.io/api/v1/crates/firecrawl-pdfium)、[0.1.0 发布归档](https://static.crates.io/crates/firecrawl-pdfium/firecrawl-pdfium-0.1.0.crate)、[发布说明](https://github.com/firecrawl/pdfium-rs/releases/tag/v0.1.0)、[上游版本策略](https://github.com/firecrawl/pdfium-rs/blob/main/docs/VERSIONING.md)、[CI 矩阵](https://github.com/firecrawl/pdfium-rs/blob/main/.github/workflows/ci.yml)、[原生版本清单](https://github.com/firecrawl/pdfium-rs/blob/main/pdfium.lock.json)。本次所读发布归档 SHA-256：`30ae09ff39bd357efb94cacf8096ec527adb05a62022691ff2d68f09336e1612`。本轮仅更新评估，未引入 crate、替换原生资源或实施封装迁移。
