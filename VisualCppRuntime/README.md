# Microsoft Visual C++ 运行组件

Windows Document／Full 的 ONNX Runtime 随包携带对应架构的 Microsoft Visual C++ v14 DLL。共同需要 `vcruntime140.dll`、`msvcp140.dll`、`msvcp140_1.dll`；x64 另需 `vcruntime140_1.dll`。x64 四个文件合计 908008 字节，ARM64 三个文件合计 1884704 字节，与 `onnxruntime.dll` 同放在 `sidecar/ocr/`。OCR 依清单顺序预加载，Sales 不携带 OCR；Administration 已无桌面交付。

`visual-cpp-runtime.json` 固定微软官方下载地址、版本、体积、SHA-256、CAB 坐标及各架构 DLL 摘要。已核验的 `VC_redist.x64.exe` 同时包含两种架构 payload，**只用于构建缓存**：验证微软签名后，通过 Windows CAB 工具提取目标文件，不执行安装器，不把 EXE、MSI 或另一架构的 DLL 放进客户包。版本为 `14.51.36247.0`；2026-10-15 逐项验证签名、元数据、摘要、PE Machine 和 ONNX 导入依赖。归档中的 `vcruntime140_1.dll_arm64` 实际为 x64，ARM64 ONNX 也未引用它，因此排除。实际加载及识别仍由各架构 runner／设备验证。

这采用微软支持的应用本地部署（app-local）。客户无需安装整套 Visual C++ 运行库，不改动系统 DLL，也不会产生相应 UAC 或重启步骤。系统 UCRT 由项目最低支持的 Windows 10 1809／Server 2019 提供。微软 CRT 按 Visual Studio 可再分发代码条款提供，不标作 MIT／Apache 开源依赖；本文件与精确资产清单同时进入客户包，程序更新负责维护随包 DLL。

- [微软支持的运行组件与分发说明](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)
- [微软 Visual Studio 可再分发代码说明](https://learn.microsoft.com/visualstudio/releases/2022/redistribution)

升级此平台资产时，必须重新核验官方来源、微软签名及 ONNX 的实际导入依赖，再一次性更新固定清单；不得从第三方下载 DLL，也不得将开发机系统 DLL 复制给客户。业务数据仍只使用项目 DataRoot。
