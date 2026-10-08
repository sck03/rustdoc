# PDFium 跨平台绑定与验收方案

> 2026-10-08 按当前源码整理。本文维护现用绑定、资源和验收边界；迁移前的手写 FFI 示例、候选库比较与旧静态符号清单保留在 [Git 历史](https://github.com/sck03/rustdoc/blob/b44fbb6/docs/PDFium跨平台绑定与验收方案.md)，不再作为待实施任务。

## 当前实现

| 环节 | 源码与职责 |
| --- | --- |
| 模板测量、分页和 PDF 生成 | [export-doc-report](../crates/export-doc-report/src/lib.rs)，统一 V3 文档，krilla 编码 |
| PDF 读取、页图、文字和合并 | [pdf/native.rs](../crates/export-doc-engine/src/pdf/native.rs)，使用 pdfium-render，保留有界 writer |
| 进程隔离、取消和资源释放 | [PDF worker](../crates/export-doc-engine/src/pdf.rs)及[合并入口](../crates/export-doc-engine/src/pdf/merge.rs) |
| 原生库来源、版本及哈希 | [中央原生资源清单](../eng/native-runtime-packages.json) |
| React 编辑和样例/单据预览 | 同一 Rust 排版结果生成 SVG，由受控 HTML 包装显示；不是 PDFium 页图预览 |

[Engine 清单](../crates/export-doc-engine/Cargo.toml)精确固定 `pdfium-render = 0.9.4`，关闭默认 feature，启用 `pdfium_7881` 与 `image_025`；当前原生载荷为 `152.0.7961`。这分别是绑定版本、API 集和原生构建，不是三个可以互换的版本号。2026-09-20 的“最新版本”查询只属于历史，后续升级重新核验，不按旧候选版本直接更新。

已没有第二套手写 PDFium 绑定，也未采用 pdfium-sys 或 firecrawl-pdfium。封装负责 C ABI 和对象生命周期，本项目继续负责受管路径、输入/输出限制、错误分类和 worker 隔离。PDFium 本体仍是原生 C++ 库。

## 加载和生命周期

- 由 RuntimePaths 提供受管绝对路径，先验证路径及文件，再调用 `Pdfium::bind_to_library`；不使用系统搜索回退、请求时下载或猜测符号名。
- PDFium 初始化、文档、页面、文本及位图对象限于 worker 生命周期。调用方不接触绑定类型，Tauri 窗口与 HTTP 不复制 PDF 业务。
- 合并读取原 PDF 页面并用受限 writer 保存；不把页图重新编码成正式 PDF，不以无限制缓冲替代输出容量控制。
- 缺库、错架构、缺依赖、损坏/密码文件、超时、取消及 worker 异常分别验证；失败不发布半成品。
- 绑定或原生库升级时核查所选 feature 加载的完整符号集。迁移前的 19 个静态符号检查不足以证明当前封装兼容。

## 平台检查

| 目标 | 原生资源与检查重点 |
| --- | --- |
| Windows x64 MSVC / GNU | x64 pdfium.dll、依赖 DLL、实际加载和功能；GNU 结果不替代 MSVC 发布 |
| Windows ARM64 | ARM64 DLL、对应进程与设备 |
| Linux x64 / ARM64 | libpdfium.so、ELF 架构、glibc/依赖库及对应系统调用 |
| macOS ARM64 | libpdfium.dylib、Mach-O arm64、最低系统版本及实际加载 |

现有目标均为 64 位。ABI、符号名称、整数宽度、结构与回调各自核查：Windows 32 位的 stdcall 名称修饰规则不能套到当前 x64/ARM64；macOS 工具显示的前导下划线不能直接变成动态查询名。具体声明由锁定的 pdfium-render 维护，业务代码不另造 FFI。

Windows 本机的报表导出和界面预览记录见[进度](./程序改进重构进度文档.md)。krilla 成功生成 PDF、浏览器显示 SVG、静态导出表检查，都不能单独证明 PDFium 文字/OCR/合并以及其它 OS 已完成验收。

## 验收与剩余工作

1. 使用同一匿名中文/英文、图片、旋转页及多页 PDF，验证 PDFium 打开、页图、文字、OCR 输入和合并后重新打开。
2. 检查坏文件、页数/像素/面积/输入/输出上限、取消、超时、子进程退出和临时文件清理。
3. 在每个目标 OS/架构分别记录 locked build、资源哈希、真实加载、功能和包内许可；不得借用上游 CI 结果。
4. 依赖变更独立完成 Cargo/npm 锁图、notices/SBOM 和治理，要求 unresolved=0、disallowed=0。
5. 如果后续实现“实际输出 PDF 的页图预览”，应增加正式契约、草稿修订绑定、按页加载及权限验证；当前 SVG 预览不能标记为该功能已实现。

模板版式和逐页打印要求见[PDF 验收方案](./Rust统一V3模板与原版PDF验收方案.md)，平台状态见[支持矩阵](./多平台与多架构支持矩阵.md)。

## 上游参考

- [pdfium-render 0.9.4 API](https://docs.rs/pdfium-render/0.9.4/pdfium_render/)
- [PDFium 公共头文件](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/)
- [Rust ABI 说明](https://doc.rust-lang.org/reference/items/external-blocks.html#abi)
- [Apple dlsym](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man3/dlsym.3.html)
