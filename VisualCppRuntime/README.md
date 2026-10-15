# Microsoft Visual C++ 运行组件

Windows Document／Full 的 ONNX Runtime 随包携带对应架构的四个 Microsoft Visual C++ v14 DLL：`vcruntime140.dll`、`vcruntime140_1.dll`、`msvcp140.dll`、`msvcp140_1.dll`。x64 合计 908008 字节，ARM64 合计 1938304 字节。它们与 `onnxruntime.dll` 同放在 `sidecar/ocr/`，OCR 使用 ort 的公开 API 按依赖顺序预加载。Sales 不携带 OCR；Administration 已无桌面交付。

`visual-cpp-runtime.json` 固定微软官方下载地址、版本、体积、SHA-256、CAB 坐标及两种架构各自的 DLL 摘要。已核验的 `VC_redist.x64.exe` 同时包含 x64 和 ARM64 原生 payload，**只用于构建缓存**：验证微软签名后，通过 Windows CAB 工具提取目标架构的四个文件，不执行安装器，不把 EXE、MSI 或另一架构的 DLL 放进客户包。审核版本为 `14.51.36247.0`；2026-10-15 对 ARM64 四个 DLL 另行验证微软签名、文件元数据和摘要。实际加载及识别仍须由各架构 runner／设备验证。

这采用微软支持的应用本地部署（app-local）。客户无需安装整套 Visual C++ 运行库，不改动系统 DLL，也不会产生相应 UAC 或重启步骤。系统 UCRT 由项目最低支持的 Windows 10 1809／Server 2019 提供。微软 CRT 按 Visual Studio 可再分发代码条款提供，不标作 MIT／Apache 开源依赖；本文件与精确资产清单同时进入客户包，程序更新负责维护随包 DLL。

- [微软支持的运行组件与分发说明](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)
- [微软 Visual Studio 可再分发代码说明](https://learn.microsoft.com/visualstudio/releases/2022/redistribution)

升级此平台资产时，必须重新核验官方来源、微软签名及 ONNX 的实际导入依赖，再一次性更新固定清单；不得从第三方下载 DLL，也不得将开发机系统 DLL 复制给客户。业务数据仍只使用项目 DataRoot。
