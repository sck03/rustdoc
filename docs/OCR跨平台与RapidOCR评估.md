# OCR 跨平台与 RapidOCR 评估

> 2026-10-15。源码、随包资源、真实测试分别核对；目标平台不等于已经在每种设备上验收。

## 选择结论

继续使用 **Rust + ONNX Runtime + PP-OCRv6 Small** 的隔离进程，重构识别流水线。RapidOCR 的算法、模型选择和硬件后端更丰富，但不自动使本项目更轻。本日官方最新稳定版为 [v3.10.0](https://github.com/RapidAI/RapidOCR/releases/tag/v3.10.0)，2026-10-08 发布，Apache-2.0；主实现为 Python。[依赖清单](https://github.com/RapidAI/RapidOCR/blob/v3.10.0/python/requirements.txt)包含 OpenCV headless、NumPy、Shapely、pyclipper 等，另需推理后端。直接包装还需携带 Python 环境。

| 对比项 | 原有 Rust 实现 | RapidOCR 3.10.0 | 本次 Rust 重构 |
| --- | --- | --- | --- |
| 运行依赖 | worker、ONNX、两份 Small 模型 | Python、图像/几何库、推理后端、模型 | 保留原依赖与模型 |
| 检测框 | 轴对齐矩形，直接合并邻近行 | DB 后处理、旋转框与裁剪 | 有界连通区域、凸包最小旋转框、扩框及直接矫正缩放 |
| 识别 | 每行固定多次推理，优先更长文字 | 批量识别、置信度过滤 | 通常一次推理，不确定时尝试半周旋转，按置信度选择 |
| 解码与内存 | 复制完整输出张量，单引号/全角空格处理错误 | CTC 与模型字典 | 借用输出张量，正确处理 YAML 字符、CTC 空白和重复字符 |
| 后端 | CPU ONNX | ONNX、OpenVINO、Paddle、PyTorch、TensorRT、MNN 等 | CPU ONNX，未来通过 worker 边界扩展 |

参考同类 DB/CTC 流程，自行实现 Rust 几何和图像处理，没有复制 RapidOCR 运行时，也没有把其 Python 包加入交付依赖。受控子进程继续保障取消、超时及原生内存释放；浏览器不下载模型。模型与 ONNX 版本本次未升级。

## 实测方法及边界

Windows x64、ONNX Runtime 1.29.0、同一份 PP-OCRv6 Small 模型及字典、四个 CPU 推理线程。RapidOCR 关闭另加模型的方向分类器，检测输入上限和归一化使用随包模型配置。分别启动新进程，计入启动、导入、模型加载和识别，每样本三次取中位数；内存监测包括 Python 启动器的子进程。这组单次进程数据不代表常驻批处理、GPU 或所有单据性能。

匿名样本包含中英发票、7° 倾斜、90° 旋转、低对比度、16 行密集文字和空白页，见 [OCR 夹具](../tests/OcrFixtures/README.md)。原实现在倾斜图上严重漏字，且将单引号变成两个单引号；本次修复这些问题。最终耗时、内存与通过范围见本日[实施进度](./程序改进重构进度文档.md)。合成样本不能当作真实业务识别率保证；手写、严重透视、复杂竖排和混合方向仍须独立验收。

## 产品与平台

| 入口/目标 | 执行位置与要求 | 证据边界 |
| --- | --- | --- |
| Windows x64 Document、Full SQLite 测试版 | 本地 worker、x64 ONNX、x64 CRT | 本机实际识别与 Tauri；GNU 不替代 MSVC 发布 |
| Windows ARM64 桌面 | ARM64 worker、ONNX、三个 ARM64 CRT | 已补齐打包与初始化；签名/摘要不等于设备运行 |
| Linux x64/ARM64 桌面 | 对应 GNU worker、libonnxruntime.so | 对应 runner/设备，另需 WebKitGTK |
| macOS ARM64 桌面 | ARM64 worker、libonnxruntime.dylib | 对应 runner/设备，使用 WKWebView |
| 网页/移动浏览器 | 上传到 Rust HTTP 服务，由服务器识别 | 客户端不加载 ONNX；文件选择可用于非 HTTPS 局域网，粘贴权限依浏览器而定 |
| Docker amd64/arm64 | 容器内对应 Linux worker 和 ONNX | 与 PostgreSQL 分工不变，需对应架构真实镜像验收 |
| Sales 或显式 WithoutOcr 包 | 不附 OCR | 明确显示不可用 |

运行库从 AppRoot 绝对路径加载，不依赖工作目录或系统 ONNX。Windows 两种架构从同一已核验微软归档提取目标架构所需的 CRT 文件，缓存隔离，许可和哈希随包，见 [CRT 说明](../VisualCppRuntime/README.md)。

网页增加文件选择，桌面保留原生对话框。PNG/JPEG/BMP/WebP/TIFF 共用后端；部分浏览器不能预览 TIFF，但可以上传识别。手机照片按 EXIF 纠正方向，与浏览器预览一致，返回坐标以显示方向为准；新增对应 JPEG 回归。换图立即取消旧预览和识别，迟到结果不覆盖当前图；Blob URL 在替换或离开后释放。图片和结果不自动写入业务库，信用证应用保留原确认与权限边界。

## 持续验证

OCR 是独立 Cargo 工程，根 `cargo test --workspace` 不包含它。本次增加独立 fmt/test、真实模型回归和打包后的 `scripts/test_native_ocr.mjs`。`rust-native-validation.yml` 在各目标 runner 执行，并增加 OCR/模型/CRT 变更触发路径。只有实际工作流成功才计入该平台证据；检查或打包成功不代表所有 OS、架构和浏览器均已实测。
