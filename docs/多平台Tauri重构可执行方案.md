# 多平台 Tauri 重构可执行方案

生成日期: 2026-06-22

更新日期: 2026-07-03

本文基于当前仓库源码、`设计规划.md`、`程序改进文档.md`、`模块级审查清单.md`、`单一窗口对接代码级设计.md`、`单一窗口字段映射清单初稿.md` 的审查结果编写。目标是把原 Windows WinForms 单体程序，重构为同时支持桌面多平台、Docker 服务端和网页版的高性能单证系统。当前主线已经进入 `Tauri/Web + ASP.NET Core API sidecar + Domain/Application/Infrastructure` 执行阶段，旧 WinForms 主项目、旧 WinForms 测试项目、旧 WinForms 注册机和旧 WinForms 发布脚本已经清理。

## 1. 总体结论

当前项目不建议直接重写为 Go 或 Rust 后端。已采用且继续推荐的路线是:

```text
第一阶段:
React/Vite 前端 + Tauri 桌面壳 + ASP.NET Core API sidecar + C# Domain/Application/Infrastructure 业务内核

第二阶段:
在 API 边界稳定后，再按性能或部署需求，把少数模块替换为 Rust/Go/独立服务
```

理由:

- 现有核心业务资产集中在 C#/.NET: EF Core 数据模型、SQLite/PostgreSQL 双 provider、单一窗口映射/XML/回执、HTML+Scriban 模板、Excel/PDF/OCR、权限和测试。
- 当前真正阻碍多平台的不是 C# 语言，而是 `WinForms / WebView2 WinForms / Windows 打印对话框 / System.Drawing / Registry/WMI / cmd.exe` 这些宿主与基础设施绑定。
- ASP.NET Core 足以支撑本项目的业务负载。主要性能瓶颈在数据库、OCR、PDF/Excel、模板渲染和批量文件处理，不在 API 框架。
- Go/Rust 重写主后端可能减少几十到一百多 MB 体积，但无法解决 WebView2、OCR 模型、PDF/图片 native 库这些真正的大头，且会显著增加业务回归风险。
- 软件更新主线采用 Tauri updater，不再保留 API 更新检查、API 更新包暂存、桌面 staged 包 handoff 或 Windows `.cmd` 自替换兼容流程。更新中心检查到新版本后显示 Tauri updater endpoint JSON 的 `notes` 作为“更新日志”，下载、签名校验、安装和重启由 Tauri updater / 平台安装器接管；业务数据库和运行数据继续留运行目录 `App_Data`，授权文件保留运行目录镜像，同时由机器级授权锚点防止重装后重置试用。

最终目标形态:

```text
桌面离线版:
Tauri + React
  -> 本机 ASP.NET Core sidecar
  -> 运行目录中的 SQLite/SQLCipher、模板、日志、OCR 模型、浏览器/PDF renderer、授权镜像和用户显式导出文件

局域网团队版:
Tauri 或浏览器
  -> Docker 中 ASP.NET Core API
  -> PostgreSQL

网页版:
React Web
  -> ASP.NET Core API
  -> PostgreSQL
```

## 2. 当前项目审查摘要

### 2.1 工程状态

当前解决方案包含:

- `src/ExportDocManager.Domain`: 领域实体、值对象和纯领域规则
- `src/ExportDocManager.Application`: 应用端口、DTO、分页模型和跨平台业务规则
- `src/ExportDocManager.Infrastructure`: EF Core、SQLite/PostgreSQL、运行路径、OCR、报表、单一窗口、授权验签和文件系统实现
- `src/ExportDocManager.Api`: ASP.NET Core 本地 API sidecar、OpenAPI、鉴权和后台任务
- `apps/export-doc-web`: React/Vite/TypeScript 前端
- `apps/export-doc-tauri`: Tauri v2 桌面壳
- `apps/license-keygen-tauri`: Tauri/Rust 离线授权注册机
- `Shared`: 授权编码/验签共享模型
- `tests/*`: Domain/Application/Infrastructure/API 测试
- `tools/ExportDocManager.ApiClientGenerator`: OpenAPI client 生成工具

旧 `ExportDocManager` WinForms 主项目、旧 `ExportDocManager.Tests`、旧 `KeyGen` 和旧 `scripts/publish-local-win-x64.ps1` 已下线。当前后端项目目标框架为 `net8.0`，不再被 `net8.0-windows + UseWindowsForms` 绑定；桌面能力由 Tauri/Rust 进程承接。

### 2.2 代码规模

原 WinForms 审查时统计如下，仅保留为迁移规模参考:

| 区域 | 文件数 | 行数 |
|---|---:|---:|
| 主项目 C# | 589 | 100994 |
| 测试 C# | 96 | 36499 |
| Forms | 148 | 32899 |
| Services | 186 | 29992 |
| ViewModels | 106 | 22212 |
| Utils | 64 | 9363 |
| Models | 59 | 4276 |
| DataAccess | 8 | 1010 |

判断:

- 这不是简单 UI 程序，已经有大量业务逻辑和测试资产。
- `Services` 和 `Models/DataAccess` 值得保留并拆出跨平台项目。
- `Forms` 和一部分 UI 型 `ViewModels/Utils` 已作为旧 Windows 前端迁移来源逐步退场，不再作为新增能力中心。

### 2.3 已稳定的业务资产

可以优先保留并 API 化的资产:

- `Services/Core`: 发票、商品、付款、托单/单据包
- `Services/MasterData`: 客户、出口商、产品、HS 编码、辅助资料
- `Services/SingleWindow`: COO/ACD 映射、XML、回执、交接包、操作中心、协同基础
- `Services/Reporting`: HTML 模板、Scriban 渲染、模板包、批量导出路径规则、PDF 合并
- `Services/Data`: Excel 导入、汇率
- `DataAccess`: EF Core、SQLite/SQLCipher、PostgreSQL、事务执行策略
- `Models`: 实体、DTO、分页模型、单一窗口读模型
- `Utils`: 原子写文件、ZIP、分页 Excel 导出、文本清理、搜索等非 UI helper

### 2.4 主要平台绑定点

需要拆除或隔离的绑定:

| 绑定点 | 当前位置 | 影响 | 处理方式 |
|---|---|---|---|
| WinForms 全局引用 | 旧 `GlobalUsings.cs` | 污染所有项目编译边界 | 已通过 Domain/Application/Infrastructure/API 拆分隔离，生产主线禁止重新引用 Forms/WinForms |
| `ServiceConfigurator` 标记 Windows | 旧 `ServiceConfigurator.cs` | DI 装配无法复用到 API | 已拆成 API/Infrastructure/Tauri 组合根 |
| UI 对话框服务 | `IAppDialogService`/`AppDialogService` | 大量 `IWin32Window/Form` | API 中删除，前端路由/Modal 承接 |
| 文件对话框 | `FileDialogService` | WinForms only | Tauri 插件/浏览器上传/服务端路径策略 |
| 通知与确认框 | `UserNotificationService` | WinForms `MessageBox` | API 返回错误码，前端 toast/dialog |
| WebView2 PDF/预览 | `PrintService`/`WebView2PdfGenerator` | Windows WebView2 | 抽 `IHtmlToPdfService`，换跨平台渲染器 |
| 打印对话框 | `PrintPreviewForm`/`PrintService` | Windows 打印 | 桌面用 Tauri/系统打印，Web 用浏览器/服务端 PDF |
| `System.Drawing` | 章图、装柜绘图、图片工具 | Linux 容器风险高 | 改 ImageSharp/SkiaSharp 或前端 Canvas |
| Registry/WMI 机器码 | 旧 `LicenseManager` | Windows only | 已升级为 EDM2 签名许可证 + 跨平台设备指纹/本机密封绑定 |
| 固定 AES Key/IV | 旧授权/加密 helper | 安全和多部署模式不足 | 主授权入口已改为客户端公钥验签，注册机私钥签发；后续可继续接离线介质/HSM |
| 静态当前用户 | `SessionManager.CurrentUser` | 不适合 Web 请求并发 | 改 `ICurrentUserContext` |
| Windows 运行命令 | 旧 `UpdateService` / `.cmd` 自替换 | Docker/macOS/Linux 不可用且有命令注入面 | 已移除旧 API 更新服务和 `.cmd` 自替换，桌面更新统一走 Tauri updater |
| Windows 版 OpenCV runtime | `OpenCvSharp4.runtime.win` | OCR 非跨平台 | 换多 RID runtime 或替代图像库 |

## 3. 目标架构

### 3.1 推荐解决方案结构

当前采用以下项目结构:

```text
ExportDocManager.sln
  src/
    ExportDocManager.Domain/
      实体、枚举、纯业务值对象、领域规则

    ExportDocManager.Application/
      用例服务、业务接口、DTO、校验、单一窗口、模板渲染入口

    ExportDocManager.Infrastructure/
      EF Core、文件存储、模板目录、ZIP、Excel、OCR、邮件、WebDAV

    ExportDocManager.Api/
      ASP.NET Core API、认证、OpenAPI、后台任务、SSE/SignalR

  apps/
    export-doc-web/
      React + Vite + TypeScript 前端

    export-doc-tauri/
      Tauri v2 桌面壳，复用 React 前端

    license-keygen-tauri/
      Tauri/Rust 离线授权注册机

  tests/
    ExportDocManager.Application.Tests/
    ExportDocManager.Infrastructure.Tests/
    ExportDocManager.Api.Tests/
    ExportDocManager.E2E.Tests/
```

第一阶段“保留旧 WinForms 作为过渡”的任务已经结束。后续新增桌面能力必须进入 `apps/export-doc-tauri`、`apps/export-doc-web`、`src/ExportDocManager.Api`、`Application` 或 `Infrastructure`，不再恢复旧壳。

### 3.2 依赖方向

必须保持单向依赖:

```text
Domain
  <- Application
      <- Infrastructure
      <- Api
      <- Tauri sidecar launcher / Web frontend through HTTP/OpenAPI

React/Tauri 前端只通过 HTTP/OpenAPI 调用 Api
```

禁止:

- `Domain/Application` 引用 `System.Windows.Forms`
- `Domain/Application` 引用 `Microsoft.Web.WebView2`
- `Application` 直接读取前端组件或桌面窗口状态
- `Api` 直接调用系统对话框、MessageBox、FileDialog 或 Tauri command

### 3.3 便携优先与扩展边界

新增跨平台架构时，必须把“运行目录优先”作为默认设计，而不是后期补丁。这里的重点是默认不落到系统 C 盘，而不是强制所有文件都放进 `App_Data`。现有便携目录中已经随程序发布的 `Templates/`、`OcrModels/`、`logs/` 可以继续保留在程序根目录；数据库、缓存、单一窗口交接包、授权镜像、导出文件等业务可写数据优先放在程序运行目录下的 `App_Data`，或安装时由用户选择的业务数据目录中。授权防重置锚点是小型机器级安全锚点，只保存试用起点、机器绑定随机量和已验证注册码，不保存业务数据。系统 C 盘的 AppData/ProgramData 只能作为用户明确选择或管理员统一策略，不能作为默认落盘位置。

推荐运行目录:

```text
artifacts/windows-desktop-run/ 或用户选择的非系统盘运行根
  ExportDocManager/            客户发布目录，可直接分发给普通用户
    ExportDocManager.exe
    sidecar/
      ExportDocManager.Api.exe
    Templates/
    Resources/
    OcrModels/
    Browsers/
    logs/
    Tools/
      exportdoc-excel-analyzer.exe
    App_Data/
      Database/
        exportdoc.db
      Files/
      Exports/
      SingleWindow/
        Packages/
        Receipts/
        Client/
      Cache/
      Security/
      Config/
  KEY/                         内部授权工具目录，不随普通客户包分发
    ExportDocLicenseKeyGen.exe
    WebView2Loader.dll
```

安装版建议:

- 安装器默认建议安装到用户选择的业务目录，例如 `D:\ExportDocManager`、`E:\ExportDocManager`。
- 数据目录默认跟随安装目录，例如 `D:\ExportDocManager\App_Data`；随程序资源继续可使用 `D:\ExportDocManager\Templates`、`D:\ExportDocManager\OcrModels`、`D:\ExportDocManager\logs`。
- 如果用户把程序安装到 `C:\Program Files` 且目录不可写，安装器或首次启动必须要求用户选择数据目录，推荐非系统盘，例如 `D:\ExportDocManagerData`。
- 不允许静默把 SQLite 数据库、授权镜像、OCR 模型、日志和导出文件写入 `C:\Users\<User>\AppData` 或 `C:\ProgramData`，否则长期使用会把系统盘撑大；授权防重置锚点仅使用注册表/Keychain/Secret Service 存储小型签名数据，不承载业务文件。

路径访问必须通过统一接口，不允许业务服务散落使用 `AppContext.BaseDirectory`、`Environment.SpecialFolder.ApplicationData`、硬编码 `C:\` 或任意绝对路径。

建议新增:

```csharp
public interface IAppPathProvider
{
    string AppRoot { get; }
    string DataRoot { get; }
    string DatabaseRoot { get; }
    string TemplateRoot { get; }
    string FileRoot { get; }
    string ExportRoot { get; }
    string SingleWindowRoot { get; }
    string OcrModelRoot { get; }
    string LogRoot { get; }
    string CacheRoot { get; }
    string ConfigRoot { get; }
    string SecurityRoot { get; }
}
```

扩展性原则:

- `Application` 只依赖接口，例如 `IFileStorage`、`ITemplateStorage`、`IHtmlToPdfService`、`IOcrService`、`ISingleWindowPackageStore`、`IAppPathProvider`。
- `Infrastructure` 提供本地运行目录实现、Docker volume 实现、当前 WebDAV 云备份实现和未来对象存储实现。
- `Api` 只编排请求和权限，不直接拼路径、不直接读写模板文件。
- `Tauri` 只做桌面能力适配，不承载业务规则。
- 新增 OCR、PDF、打印、模板设计器、外部接口时，以 adapter/provider 形式接入，避免再次形成 UI 与业务互相依赖的单体结构。

## 4. 技术选型

### 4.1 后端

推荐:

- `.NET 8 或 .NET 9`
- `ASP.NET Core Minimal API` 或 MVC Controller
- `EF Core`
- `SQLite/SQLCipher + PostgreSQL`
- `OpenAPI/Swagger`
- `BackgroundService + Channel` 处理长任务
- `SSE` 或 `SignalR` 推送长任务进度

说明:

- 主后端继续 C#，不是为了偷懒，而是为了最大化复用现有业务资产。
- API 层很薄，主要做认证、DTO、请求上下文、任务调度和文件下载。
- 长任务不能卡 HTTP 请求，应进入任务队列。

### 4.2 前端

推荐:

- `React`
- `TypeScript`
- `Vite`
- `TanStack Query`
- `React Router`
- 表格组件优先选能虚拟滚动的开源方案，例如 `TanStack Table` + 虚拟滚动
- 表单可用 `React Hook Form` + `Zod`
- API 类型从 OpenAPI 生成

原因:

- 业务界面是高密度单据/管理工具，不适合做营销站式前端。
- React 生态适合复杂表单、表格、模板设计器嵌入、长任务进度和多端复用。

### 4.3 桌面壳

推荐:

- `Tauri v2`
- 前端复用 `apps/export-doc-web`
- 桌面启动本机 ASP.NET Core sidecar
- Tauri 负责窗口、文件选择、系统通知、打开文件、打印适配、更新器

桌面版安全要求:

- API sidecar 只监听 `127.0.0.1`
- 每次启动生成随机访问令牌
- 前端请求必须带 `Authorization`
- 端口随机分配，Tauri 启动后注入 API base URL
- 启动时由 Tauri/sidecar 确定 `App_Data` 根目录，并通过配置传给 API
- 默认不把数据库、日志、模板、OCR 模型写入系统 C 盘用户目录

### 4.4 Docker/服务端

推荐:

- `ExportDocManager.Api` 提供 Linux container
- PostgreSQL 作为主数据库
- 模板、OCR 模型、日志、上传文件、交接包、导出产物按运行目录语义挂载到 volume
- Docker 中仍保持与桌面版一致的目录语义：业务可写数据使用 `/app/App_Data`，模板、OCR 模型和日志保留 `/app/Templates`、`/app/OcrModels`、`/app/logs`
- OCR/PDF 渲染可做可选 profile

示例目标:

```yaml
services:
  api:
    image: export-doc-manager-api
    environment:
      Database__Provider: PostgreSQL
      Database__Host: postgres
      Database__Database: export_doc
      Database__Username: export_doc
      Database__Password: ${POSTGRES_PASSWORD}
    volumes:
      - ./data/database:/app/App_Data/Database
      - ./data/templates:/app/Templates
      - ./data/files:/app/App_Data/Files
      - ./data/exports:/app/App_Data/Exports
      - ./data/single-window:/app/App_Data/SingleWindow
      - ./data/ocr-models:/app/OcrModels
      - ./data/logs:/app/logs
      - ./data/backups:/app/App_Data/Backups
  postgres:
    image: postgres:18-bookworm
    volumes:
      - ./data/postgres:/var/lib/postgresql
```

## 5. 需要先做的代码分层

### 5.1 建立跨平台 Core 项目

第一批迁移目标:

| 当前目录/文件 | 目标项目 | 备注 |
|---|---|---|
| `Models/Entities` | `Domain` | 实体类先保持 EF 兼容 |
| `Models/DTOs` | `Application` 或 `Contracts` | API 读写 DTO 需要重新整理命名 |
| `Models/PagedResult.cs` | `Application` | 保留 |
| `DataAccess/AppDbContext.cs` | `Infrastructure` | EF provider 配置拆出 |
| `DataAccess/AppDbContextExecution.cs` | `Infrastructure` | 保留 |
| `Services/Core` | `Application` | 去掉 UI 依赖 |
| `Services/MasterData` | `Application` | 去掉 `SupportedOSPlatform` |
| `Services/SingleWindow` | `Application` | 保留业务，文件路径适配 |
| `Services/Reporting/ReportTemplateRenderer.cs` | `Application` | 可跨平台 |
| `Services/Reporting/ReportTemplateCatalog.cs` | `Application/Infrastructure` | 路径策略需抽象 |
| `Utils/AtomicFileHelper.cs` | `Infrastructure` | 保留 |
| `Utils/ZipArchiveHelper.cs` | `Infrastructure` | 保留 |
| `Utils/PagedExcelExportHelper.cs` | `Infrastructure` | 保留 |

第一批禁止迁移:

- `Forms/**`
- `Utils/DataGridView*`
- `Utils/*Layout*`
- `Utils/*Ui*`
- `Services/Infrastructure/AppDialogService.cs`
- `Services/Reporting/PrintService.cs`
- `Services/Reporting/WebView2PdfGenerator.cs`
- `Services/Infrastructure/FileDialogService.cs`
- `Services/Infrastructure/UserNotificationService.cs`
- `Services/Infrastructure/WebView2EnvironmentService.cs`

### 5.2 拆掉 `GlobalUsings.cs` 污染

当前 `GlobalUsings.cs` 全局引入了 Forms 和 `System.Windows.Forms`。重构时:

- 新项目不要复用这个文件。
- Domain/Application 使用显式 using。
- 旧 WinForms 过渡项目已清理；新代码不得恢复全局 WinForms using。

验收标准:

- `ExportDocManager.Domain` 可用 `net8.0` 编译。
- `ExportDocManager.Application` 可用 `net8.0` 编译。
- 这两个项目不引用 `System.Windows.Forms`、`Microsoft.Web.WebView2`、`System.Drawing.Common`。

### 5.3 拆 DI 装配

目标:

```csharp
services.AddExportDocDomain();
services.AddExportDocApplication();
services.AddExportDocInfrastructure(configuration);
services.AddExportDocApi();
services.AddExportDocTauriDesktopAdapters(); // 仅桌面 bridge/sidecar 场景
```

旧 `ServiceConfigurator.Configure()` 曾同时注册数据、业务、基础设施、Forms、ViewModels。当前已经拆成:

- `ApplicationServiceCollectionExtensions`
- `InfrastructureServiceCollectionExtensions`
- `ApiServiceCollectionExtensions`
- Tauri `runtime_paths / sidecar / desktop_commands / window` 模块

验收标准:

- API 项目能启动并注册业务服务，但不注册任何 Form。
- 旧 WinForms 能继续使用同一批业务服务。

## 6. API 化方案

### 6.1 认证与用户上下文

现状:

- `SessionManager.CurrentUser` 是静态全局状态。
- `BusinessDataAccessPolicy` 默认读取静态用户。
- 这在 Web/API 并发请求下不安全。

改造:

```csharp
public interface ICurrentUserContext
{
    int UserId { get; }
    string Username { get; }
    string Role { get; }
    string DepartmentId { get; }
    string CompanyScope { get; }
    bool IsAuthenticated { get; }
}
```

处理:

- 桌面 sidecar: 登录后发本地短期 token。
- Web/Docker: JWT 或 Cookie session。
- `BusinessDataAccessPolicy` 改为显式传 `ICurrentUserContext` 或 `UserAccessContext`。
- `UserService` 中的管理员判断不要再读静态 `SessionManager`。

验收标准:

- 两个用户同时请求 API 时，权限过滤互不串扰。
- 单元测试覆盖普通用户只能看本人数据、Admin 可看全部。

### 6.2 API 模块划分

建议第一批 API:

```text
POST   /api/auth/login
POST   /api/auth/logout
GET    /api/auth/me

GET    /api/invoices
GET    /api/invoices/{id}
POST   /api/invoices
PUT    /api/invoices/{id}
DELETE /api/invoices/{id}
POST   /api/invoices/{id}/clone

GET    /api/invoices/{id}/items
PUT    /api/invoices/{id}/items

GET    /api/payments
GET    /api/payments/{id}
POST   /api/payments
PUT    /api/payments/{id}
DELETE /api/payments/{id}

GET    /api/master/customers
POST   /api/master/customers
PUT    /api/master/customers/{id}
DELETE /api/master/customers/{id}

GET    /api/master/exporters
GET    /api/master/products
GET    /api/master/hscodes
GET    /api/master/payees
GET    /api/master/ports
GET    /api/master/units

GET    /api/reports/templates
POST   /api/reports/templates
POST   /api/reports/render
POST   /api/reports/pdf-jobs
GET    /api/reports/pdf-jobs/{jobId}
GET    /api/reports/pdf-jobs/{jobId}/download

GET    /api/single-window/operation-center
GET    /api/single-window/invoices/{invoiceId}/coo
PUT    /api/single-window/invoices/{invoiceId}/coo
GET    /api/single-window/invoices/{invoiceId}/acd
PUT    /api/single-window/invoices/{invoiceId}/acd
POST   /api/single-window/packages/submit
POST   /api/single-window/packages/import
POST   /api/single-window/receipts/import

GET    /api/settings
PUT    /api/settings

POST   /api/tools/ocr
POST   /api/tools/lc/import
POST   /api/tools/container-loading/analyze

GET    /api/jobs/{jobId}
GET    /api/jobs/{jobId}/events
POST   /api/jobs/{jobId}/cancel
```

### 6.3 长任务模型

以下操作必须做成长任务:

- 批量导出 PDF/ZIP
- OCR
- 扫描 PDF 信用证导入
- 大 Excel 导入/导出
- 单一窗口批量交接包
- PDF 合并

建议模型:

```text
POST /api/export-jobs
  -> 返回 jobId

GET /api/jobs/{jobId}
  -> 状态、进度、错误、输出文件

GET /api/jobs/{jobId}/events
  -> SSE 实时进度

POST /api/jobs/{jobId}/cancel
  -> 取消任务
```

后端实现:

- `Channel<BackgroundJobRequest>`
- `BackgroundService`
- `IJobStore` 记录状态
- 桌面版可用 SQLite/内存，服务端用数据库表

验收标准:

- 前端刷新页面后仍能看到任务进度或最终结果。
- 用户取消任务后，PDF/ZIP/临时文件能清理。
- 任务失败时返回可读错误，不留下半成品覆盖正式文件。

## 7. 前端与 Tauri 迁移方案

### 7.1 前端信息架构

第一屏不做营销页，直接进入业务工作区:

```text
登录
  -> 仪表盘
  -> 发票
  -> 商品/付款
  -> 基础资料
  -> 报表/模板
  -> 单一窗口
  -> 工具
  -> 系统设置
```

设计原则:

- 高密度、可扫描、少装饰。
- 表格默认分页/虚拟滚动。
- 编辑页保存/取消/复制/导出动作固定位置。
- 单一窗口编辑器按业务块分组，字段提示不侵占主内容。
- 长任务统一进度抽屉或任务中心。

### 7.2 Tauri 桌面版

启动流程:

```text
Tauri main
  -> 找到 sidecar 可执行文件
  -> 确定运行模式: portable 或 installed
  -> 创建/校验运行目录结构和 App_Data 业务数据目录
  -> 分配 localhost 随机端口
  -> 生成启动 token
  -> 把 App_Data 路径、端口、token 作为参数/环境变量传给 ASP.NET Core API sidecar
  -> 等待 /healthz 就绪
  -> 打开 React 窗口
  -> 关闭窗口时优雅停止 sidecar
```

桌面专用能力:

- 文件选择: Tauri dialog 插件
- 打开导出目录: Tauri opener 插件
- 系统通知: Tauri notification 插件
- 自动更新: Tauri updater 或保留服务端清单但重做桌面安装器
- 直接打印: Tauri/Rust 适配或先使用浏览器打印/PDF 下载

安全要求:

- sidecar 不监听局域网地址。
- token 不写入磁盘。
- API 拒绝无 token 请求。
- 上传/下载文件路径由 API 生成，不允许前端提交任意绝对路径直接覆盖。
- API 只接收逻辑文件标识或受控相对路径，最终路径由 `IAppPathProvider` 与存储服务解析。
- portable 和 installed 模式下，数据库、缓存、导出、交接包等业务可写数据优先落在应用目录的 `App_Data` 或用户选择的数据目录；`Templates/`、`OcrModels/`、`logs/` 继续保持运行目录根下的固定目录。
- 系统 app data、ProgramData、注册表不作为默认业务数据位置，只能在用户明确选择或企业管理员策略下启用；授权防重置机器级锚点例外，且只存试用/机器绑定/已验证注册码元数据。

### 7.3 网页版

网页版直接部署 React 静态资源:

```text
Nginx/Caddy
  -> /        React
  -> /api     ASP.NET Core API
```

注意:

- 网页版不能访问用户本地单一窗口客户端目录。
- 网页版文件选择只能上传/下载，不能直接读写本机任意路径。
- 直接打印依赖浏览器能力；批量正式 PDF 应由服务端生成。

## 8. 报表、模板和打印重构

### 8.1 保留 HTML + Scriban

当前 `HTML + Scriban` 适合继续保留:

- 模板文件已经存在。
- 设计器资源已经是 HTML/JS。
- `ReportTemplateRenderer` 已经独立承接预处理和渲染。

要做的是把职责拆清:

```text
IReportTemplateCatalog
IReportTemplateRenderer
IReportDataAssembler
IHtmlPreviewService       // 前端职责
IHtmlToPdfService         // 后端职责
IPrintAdapter             // 桌面职责
```

### 8.2 PDF 生成方案

当前 Windows 方案:

- WebView2 导航临时 HTML
- WebView2 `PrintToPdfAsync`

跨平台建议:

第一阶段:

- 桌面预览由 Tauri 前端 WebView 直接渲染 HTML，不再依赖 WinForms WebView2 窗体。
- PDF 导出统一走 API sidecar 的 `IReportPdfRenderService`，当前实现内部仍复用 `IHtmlToPdfService`，优先使用程序根 `Browsers/` 下的 `chrome-headless-shell` / Chromium / Chrome for Testing，渲染临时目录进入运行数据根 `Cache/ReportPdf`。
- 浏览器核心作为“正式 PDF 高保真渲染器”随程序根可选打包或由显式环境变量指定，不作为模板设计器的必需依赖。

第二阶段:

- 保持 `IReportPdfRenderService` / `IHtmlToPdfService` 可插拔，在不破坏 HTML 模板兼容性的前提下评估轻量 renderer。
- Windows/macOS/Linux/Docker 都走同一 PDF 生成接口；不同 renderer 由配置或运行 profile 选择。
- 对正式单据建立 PDF/HTML 快照对比测试。

候选实现:

| 方案 | 优点 | 风险 |
|---|---|---|
| Chrome Headless Shell/Chromium | HTML/CSS 兼容好，和当前 HTML+Scriban 模板最匹配，跨平台一致性最好 | 包体积增加，需要随程序根 `Browsers/` 管理 |
| Tauri/WebView 打印 | 预览和手工打印轻便，复用系统 WebView | 不适合作为批量正式 PDF 的唯一依据，三平台一致性较难 |
| Typst | Rust 生态成熟度较高，排版确定性强，输出稳定，适合固定版式发票/报告单 | 需要把 HTML+Scriban 模板体系重建为 Typst 模板；缺少成熟 WYSIWYG 单据设计器 |
| printpdf/genpdf 等 Rust PDF 库 | 依赖轻、可直接生成 PDF | 偏底层绘图/文档 API，不适合承接现有可视化模板设计器 |
| WeasyPrint/Vivliostyle/Paged.js | CSS Paged Media 方案成熟，可参考分页模型 | 不是纯 Rust；仍需要额外运行时或浏览器/JS 环境 |
| wkhtmltopdf | 传统简单 | QtWebKit 陈旧，CSS 兼容落后，不建议作为新主线 |

推荐当前主线继续保留 HTML+Scriban，并把 Chrome Headless Shell/Chromium 作为正式 PDF 高保真 renderer；Typst 适合后续新增“轻量固定版式模板”实验，不建议第一阶段替换旧模板体系。这样可以先完成 WinForms 报表/模板功能在 Tauri 的闭环，再逐步压缩或可选化浏览器渲染依赖。

可参考但不直接作为第一阶段替换的开源/免费路线:

- JasperReports Community / Jaspersoft Studio: 成熟的报表设计与打印思路，适合参考字段、数据源、分页和模板包管理，但 Java 生态和现有 HTML 模板体系差异较大。
- ReportServer Community: 可参考报表服务器、权限和模板发布模型，不适合作为本地离线桌面的直接依赖。
- LibreOffice/ODF 模板 + PDF 导出: 适合固定版式发票、报告单和可编辑模板参考，跨平台但运行时也不轻，自动化和样式一致性需要额外验证。
- Paged.js / Vivliostyle / Gotenberg: 可参考 HTML/CSS 分页和服务端 PDF 渲染模式，但仍离不开浏览器或额外运行时。
- Typst: 后续可在 `IHtmlToPdfService` 之外新增 `ITemplateRenderer` 实验分支，用于不要求兼容现有 HTML/Scriban 模板的固定版式模板。

### 8.3 模板设计器

当前模板设计器已从独立离线静态资源改为 `apps/export-doc-web/src/features/report-designer/` 下的 React 结构化设计器，作为 `#/reports/templates` 报表模板页的“新版设计器”模式运行；源码模式仍保留给高级维护。

改造点:

- 模板保存/载入、新建、重命名、删除走既有 API。
- 模板包导入导出和浏览器上传下载走 API；Tauri 桌面通过文件对话框选择 `.edtpl` / `.zip` 路径，Web/浏览器模式通过下载 `.edtpl` 和上传 `.edtpl` / `.zip` 导入，不要求用户输入服务端路径。
- 设计器字段清单由 API 提供，避免前端硬编码，并按出口单据、付款/报销两类报表域隔离。
- 新版设计器以结构化 schema、React 画布、字段候选、Row 多列行、明细表、条件块、图片/印章块、HTML/Scriban 导出和 Chromium 预览为主线；旧独立可视化静态目录、iframe 入口和离线设计器文件已清理，不再作为运行依赖。
- 模板文件仍保留在程序根 `Templates/`，用户自定义或覆盖策略需单独备案，不静默写系统 C 盘。

当前进展 2026-07-02:

- 报表模板页已切换为新版设计器/源码双模式：新版设计器直接作为 React 模块运行，生成 HTML/Scriban 后复用 `/api/reports/templates/content` 管理员原子保存和 `/api/reports/templates/preview` 样例预览。
- 字段目录直接读取 `/api/reports/templates/fields`，出口单据模板显示 `Invoice/Customer/Exporter/item` 字段，付款/报销模板显示 `Payment.*` 与金额换算字段；schema 校验阻断两个业务数据库字段混用。
- `Row` 多列行支持自由列数、列宽百分比、字段/固定文本、单元格样式和四边边框，可覆盖发票同行左右信息、付款单票据格和费用报销横向费用项目表。
- 明细表继续限定出口单据 `Invoice.Items` 循环，付款/报销模板不能插入出口明细表；付款/报销真实预览只按 `paymentId` 读取付款域，不通过 `Payment.InvoiceNo` 反查发票/报关域。
- 旧 HTML 无新版 schema 时只进入结构化草稿并在应用或保存前要求确认覆盖；客户旧 HTML fixture 继续作为视觉、打印和 PDF 回归样本，验证现存模板渲染不空白、不明显塌版。
- 旧可视化 iframe、`/designer/*` 静态托管、Vite 代理、旧宿主状态模块、旧专项脚本和独立离线设计器资源已清理；Tauri 报表 smoke 现在检查 `.new-report-designer`、“字段目录”和新版模板页闭环。
- 发票编辑页报表面板已从 `App.tsx` 拆成 `apps/export-doc-web/src/features/invoices/InvoiceReportPreviewPanel.tsx`：发票表单继续负责业务字段、明细和保存，报表组件独立负责模板选择、带章开关、HTML 预览、预览后手工打印、PDF 输出路径、Tauri 保存对话框和任务中心缓存失效，降低主应用入口对报表细节的耦合；打印动作只使用当前预览 HTML 的前端内存 iframe 和浏览器/WebView 打印对话框，不新增后端文件路径。
- 发票编辑页报表面板已继续接入旧单据管理的单发票多模板单据包能力：React 在同一面板内新增“单据包”模板复选、逐模板带章开关、`生成 ZIP` 开关、`合并 PDF` 勾选框、ZIP/文件夹输出路径和任务创建按钮；API 新增 `POST /api/reports/invoices/{invoiceId}/document-package`，复用程序根 `Templates/`、报表 PDF renderer、PDF 合并和 ZIP/helper。该任务只用当前发票 `invoiceId` 读取发票/报关数据域，临时 PDF 写运行数据根 `Cache/ReportDocumentPackages/{jobId}`，每个模板 PDF 文件名复用旧 `BatchExportSettings.OutputFileNamePattern` 与 `BatchExportPathHelper` 规则，`生成 ZIP` 初始值复用旧 `BatchExportSettings.ZipAfterExport`，`合并 PDF` 初始值复用旧 `BatchExportSettings.MergePdf`；勾选 ZIP 时最终 ZIP 只写用户显式 `.zip` 路径或 Tauri 保存对话框结果，取消 ZIP 时只写用户显式选择目录下按旧 `BatchExportSettings.OutputFolderPattern` 创建的批次文件夹，不恢复默认导出目录。最新真实 Tauri/Web smoke 已连续跑通两种生产路径：ZIP 模式填写 smoke profile 下的显式 `.zip` 输出路径、点击 `生成 ZIP`、等待 `ReportDocumentPackage` 后台任务 `Succeeded`，确认 ZIP 文件存在、文件头为 `PK`、大小约 927KB、退出前清理成功；文件夹模式取消 `生成 ZIP` 后填写 smoke profile 下的显式临时目录、点击 `导出文件夹`，确认任务返回旧规则批次目录，目录中生成合并 PDF 与四个模板 PDF 共 5 个文件，PDF 头均为 `%PDF`，退出前清理成功。
- 任务中心批量报表 ZIP 已从 API/面板接入推进到真实 Tauri/Web 生产闭环：`#/jobs` 的“批量报表 ZIP”面板可填写多张发票 ID、模板和用户显式 `.zip` 输出路径，调用 `/api/reports/invoices/pdf-zip` 创建 `ReportPdfZip` 后台任务；任务逐张按 `invoice.id` 生成临时 PDF 到运行缓存根 `Cache/ReportBatchZip/{jobId}`，再写入用户选择的 ZIP，任务结束清理临时目录。最新 `--job-center-check` smoke 已创建两张临时发票，真实填写面板并点击“开始”，等待任务 `Succeeded`，校验 ZIP 文件头 `PK`、大小大于 0、`Cache/ReportBatchZip/{jobId}` 已删除，并在退出前删除 ZIP 与临时发票。该闭环不按 `InvoiceNo` 合并同号 `实际数据` / `报关数据`，不读取付款/报销单据，`Payment.InvoiceNo` 不作为发票查询键；不创建默认导出目录或系统 C 盘/AppData/ProgramData 落点。
- 任务中心 PDF 合并已从 API/面板接入推进到真实 Tauri/Web 生产闭环：`#/jobs` 的“PDF 合并任务”面板可填写多个用户显式源 PDF 和用户显式输出 PDF，调用 `/api/tools/pdf/merge` 创建 `PdfMerge` 后台任务；服务只读取请求中的源 PDF，使用 `PdfSharp` 合并后通过原子写入保存到请求中的输出路径。最新 `--job-center-check` smoke 已在运行数据根 smoke profile 下生成两份临时源 PDF，真实填写面板并点击“开始”，等待任务 `Succeeded`，校验输出 PDF 文件头 `%PDF`、大小大于 0，并在退出前删除源文件和输出文件。该闭环不访问发票/报关、付款/报销、主数据或单一窗口业务表，不按 `InvoiceNo` 或 `Payment.InvoiceNo` 取数；不创建默认导出目录或系统 C 盘/AppData/ProgramData 落点。
- 旧 WinForms 单据管理 `PreviewEnabledItemsAsync` 的“启用模板一起预览”已补入 Tauri/Web/API：API 新增 `POST /api/reports/invoices/{invoiceId}/document-package/html-preview`，复用单据包模板项校验并按前端当前勾选顺序逐个渲染 `ExportDocument` HTML；React 发票报表面板新增“预览单据包”，可显示多个模板 iframe，打印按钮会打印当前单模板预览或整组单据包预览。该预览只使用当前发票记录 ID，不按发票号合并另一类型的“实际数据/报关数据”，不读取付款/报销单据；HTML 仅随响应返回前端内存，不生成 PDF/ZIP，不写运行缓存、数据库、默认导出目录、AppData/ProgramData 或系统 C 盘默认路径。
- 旧 WinForms 发票列表 `.edpkg` 导出/导入单据包已迁入 Tauri/Web/API 并纳入真实桌面闭环：API 新增 `POST /api/invoices/{id}/transfer-package/export`、`POST /api/invoices/transfer-package/preview` 和 `POST /api/invoices/transfer-package/import`，React 发票列表提供“导入单据包”和每行“导出单据包”入口，Tauri 桌面桥提供 `.edpkg` 打开/保存对话框；列表路径选择命令失败时会回到页面业务提示。本轮已移除发票列表文件流的浏览器 `window.prompt` fallback，Tauri 桌面继续用文件/保存对话框，非桌面导入 `.edpkg` 只打开页面内受控路径输入，导出类动作明确提示需要桌面保存对话框。导出包只写用户显式 `.edpkg` 路径，临时 JSON 暂存走运行数据根 `Cache/InvoiceTransfer` 并清理；导入前先展示校验和、发票号、类型、明细数、客户/出口商匹配和同号同类型冲突，支持跳过、覆盖、另存新发票号和追加明细。最新 `invoiceListDesktopWorkflowCheck` 已从 `#/invoices` 列表真实点击导出、选择、预览和导入，校验 `.edpkg` 头为 `PK` 且 Tauri 打开/保存命令命中。该 `.edpkg` 业务转移包与报表 PDF/ZIP 单据包是两个独立流程：前者搬运发票业务数据，后者生成打印/交付文件；二者都不按付款参考号读取付款/报销域。同一发票号下“实际数据/报关数据”继续以 `InvoiceNo + Type` 判断冲突，不因发票号相同互相覆盖。
- 旧 WinForms 发票编辑页删除入口已迁入 Tauri/Web/API 发票编辑闭环：React 对已保存发票显示“删除”按钮，确认后调用既有 `DELETE /api/invoices/{id}`，成功后清理当前详情缓存、刷新发票列表、查询、仪表盘、任务中心和单一窗口相关缓存，并返回发票列表提示“发票已删除”。`scripts/smoke-web-runtime-diagnostics.mjs` 已新增 `--invoice-delete-check`，会创建临时发票并真实点击编辑页删除按钮，随后用详情接口确认 `404`；`scripts/smoke-tauri-desktop.ps1` 的报表 smoke 参数已接入该检查。该删除只按当前发票 ID 操作，不按同一发票号删除另一条 `实际数据` / `报关数据`，不读取或写入付款/报销单据，也不新增文件、缓存目录、默认导出目录或系统 C 盘默认落点。
- 旧 WinForms 单一窗口 COO/ACD 编辑器高频工具、字段锁定查看/解锁和按分组/类别清理已迁入 Tauri/Web/API 闭环：`取默认` 保留可撤销快照，`回填空白项`、`清空覆盖`、`撤销` 继续只修改页面内存草稿；新增 `locked-fields` 查询与 `unlock-fields` 解锁 API，后端复用 Application 层 `SingleWindowDraftStateHelper` 计算字段、当前值和建议值，解锁时只恢复当前已知锁定键。React 页面新增“字段锁定”弹窗，COO/ACD 共用 `SingleWindowLockedFieldsDialog.tsx`；本轮继续新增 `SingleWindowScopedClearControls.tsx`，把旧端 `清当前分组` / `清当前类字段` 按 COO `证书基础/申报与对象/运输与贸易/补充与特殊项/明细项目/附件`、ACD `基础标识/申报要素/单证与费用` 的字段清单接入页面，并保留 ACD `回执回写信息` 只提示不可清理的旧行为。范围清理通过现有 defaults 构建接口取当前发票建议值，只在浏览器内存草稿中恢复对应字段并生成撤销快照，保存后才写当前 `SourceInvoiceId` 草稿。该闭环继续以当前 `invoiceId` / `SourceInvoiceId` 关联源发票，不按发票号合并同号 `实际数据` / `报关数据`，也不读取付款/报销域；`scripts/smoke-web-runtime-diagnostics.mjs --single-window-editor-tools-check` 已扩展到字段锁定弹窗和分区清理控件，Web typecheck/build、脚本语法检查和真实 Tauri 报表 smoke 通过，最新 `web-report-templates-smoke.json` 中 COO/ACD 的 `scopedClearCheck.found=true`、`lockDialogCheck.found=true`、`cleanupDeleted=true`。
- 旧 WinForms `SingleWindowExportReviewForm` 导出前检查对话框已继续迁入 Tauri/Web COO/ACD 编辑页：新增共享 `SingleWindowExportReviewPanel`，恢复按分组查看预检结果、源资料变化详情、当前分组问题列表、可修复分组勾选和“修复所选分组”。修复复用既有 `/api/single-window/export-review/{businessType}/{invoiceId}/repair`，有未保存草稿时先确认并保存当前 COO/ACD 草稿，再执行修复、重读当前草稿并刷新预检结果；不新增后端端点、数据库表、缓存目录或默认导出目录，继续按当前 `SourceInvoiceId` 隔离同号 `实际数据` / `报关数据`，付款/报销域不参与。
- 旧 WinForms 单一窗口 COO 生产企业资料库选择/维护已迁入 Tauri/Web/API 闭环：API 新增 `GET/POST/PUT/DELETE /api/single-window/coo/producer-profiles*`，React COO 明细行可打开生产企业资料弹窗，完成搜索/刷新、套用到当前行、从当前行保存资料、编辑资料和删除资料；COO 明细列已补入旧端 CIQ/海关注册号、生产企业名称、联系人、电话、生产者电话、传真、邮箱和类型标志等字段。本轮继续补齐旧选择窗体编辑区防丢保护：关闭、新增、切换/重载资料、删除、直接套用列表资料和未保存套用当前编辑区前均会确认，并接入全局离页/刷新守卫。生产企业资料只写运行数据根数据库表 `CustomsCooProducerProfiles`，套用资料先进入当前页面草稿，保存 COO 草稿后才写当前 `SourceInvoiceId`；该闭环不读取付款/报销域，不按发票号合并同号 `实际数据` / `报关数据`，不新增默认导出目录、缓存目录、AppData/ProgramData 或系统 C 盘默认落点。
- 旧 WinForms 单一窗口 COO 明细行 `生成货物描述` 与 `复制原产标准/生产企业到后续项` 已迁入 Tauri/Web 草稿编辑页：React 操作列新增生成和复制图标按钮，前者写当前行 `goodsDesc`，后者把当前行非空且不同的原产标准/生产企业字段复制到后续行。该闭环不新增后端端点，不创建文件或缓存目录，只修改浏览器内存草稿与撤销快照，保存后才写当前 `SourceInvoiceId` 草稿；同号 `实际数据` / `报关数据` 仍以当前发票 ID 隔离，付款/报销域不参与。本轮验证：`npm --prefix apps\export-doc-web run typecheck:api`、`npm --prefix apps\export-doc-web run build`、`npm --prefix apps\export-doc-tauri run tauri:check:local` 通过，新增源码路径扫描未发现 AppData/ProgramData/SpecialFolder 或硬编码系统 C 盘默认落点。
- 旧 WinForms 单一窗口 COO 附件选择与维护已迁入 Tauri/Web 草稿编辑页：Tauri 主进程新增 `select_customs_coo_attachment_files` 文件选择命令，React COO 附件表格可从桌面选择 PDF/图片/Word/任意附件，编辑附件分类、路径和备注，打开已有路径并删除附件行。真实 `--single-window-editor-tools-check` smoke 已覆盖选择附件、编辑说明、保存、重载持久化、`open_path` 打开、删除、再次保存和重载后消失；最新结果为 `addedRowCount=1`、`reloadedRowCount=1`、`openPathCheck=true`、`removedInEditor=true`、`deletedAfterSave=true`、`deletedAfterReload=true`。附件数据继续作为当前 COO 草稿 `attachments` 随既有保存链路写入当前 `SourceInvoiceId` 单一窗口草稿；本阶段只记录用户显式选择或输入的路径，不复制附件实体文件、不创建默认附件目录，也不写系统用户目录。该闭环不读取付款/报销域，不按发票号合并同号 `实际数据` / `报关数据`；脚本语法检查和真实 Tauri 报表 smoke 已通过。
- 旧 WinForms `EditableComboBox` 自定义候选项已迁入 Tauri/Web/API：API 新增 `/api/custom-options/{optionType}`，内置候选和用户候选统一返回，用户输入的新候选只写运行数据根数据库 `CustomOptions` 表；React 发票编辑、付款/报销编辑和基础资料收款对象分类共用 `EditableComboField`。当前覆盖币制、监管方式、付款条款、起运港、目的港、运输方式、付款方式和收款对象分类；其中发票基础信息区已恢复旧 `SupervisionMode` 监管方式可编辑下拉框，默认值保持 `一般贸易`。`Type` 只读返回固定的 `实际数据` / `报关数据`，不允许保存自定义值，避免同号双类型发票口径漂移。该能力不写设置文件，不读取付款/报销表来补发票候选，也不读取发票/报关表来补付款候选。
- 旧 WinForms 唛头图片设计器已迁入 Tauri/Web/API 并补强到真实桌面闭环：API 通过 `/api/invoices/shipping-marks/image` 保存 PNG 到运行数据根 `Marks/`，通过 `/api/invoices/shipping-marks/image/preview` 只读取该目录内图片；React 发票编辑页提供“文本/图片”唛头切换、受控预览和独立画布设计器，文字工具已从浏览器 `window.prompt` 改为对话框内受控输入，支持选择、文字编辑、线条、矩形、菱形、三角形、圆形、拖动、删除、清空和保存。最新 `tauri:smoke:reports:local` 已从发票页真实点击图片模式、打开编辑器、输入文字、绘制矩形、保存预览、提交发票并用 API 读回 `ShippingMarksType=Image`；图片文件位于运行数据根 `Marks/`，发票记录只保存图片路径和类型，不保存 base64；同号 `实际数据` / `报关数据` 仍按当前记录隔离，付款/报销域不参与。
- 旧 WinForms 发票明细英文单位联想中文单位已迁入 Tauri/Web：React 发票明细编辑器读取既有 `/api/master-data/units` 单位主数据，录入 `UnitEN` 或 `CtnUnitEN` 后按英文单位精确匹配中文候选；唯一候选自动回填，多候选显示行内选择面板。本轮同时补齐旧明细表的 `CtnUnitCN` 包装中文单位列，使商品库回填/保存、发票保存和单一窗口默认映射保留包装中英文单位。该闭环只读取单位主数据和当前发票草稿，不新增 API 端点、数据表、文件落点或系统 C 盘默认目录，也不读取付款/报销域。
- 旧 WinForms `ItemForm` 的 `Ctrl+D` / “向下填充”选择区域语义已迁入 Tauri/Web 发票明细编辑器：多单元格选区跨至少两行时，前端按字段/列分组，以选区顶部单元格为来源覆盖下方选中行；没有有效多行选区时保留聚焦单元格从上一行填充的快捷行为。该动作只修改当前发票草稿并复用既有撤销/重做快照，保存后仍按当前 `invoice.id` 写当前发票与明细，不按发票号合并同号 `实际数据` / `报关数据`，不读取付款/报销域，也不新增 API、数据库表、缓存目录或文件落点。本轮验证：Web build、Tauri check 和新增源码路径扫描通过。
- 旧 WinForms `ProductListForm` 的商品资料选择模式已迁入 Tauri/Web 发票明细编辑器：原 quick dropdown 保留，新增“打开商品库选择”表格弹窗，支持关键字过滤、刷新、单击选中、双击套用和确认套用；套用时把完整 `ApiProductDto` 回传给发票编辑页并按旧端商品字段映射新增当前发票明细，避免仅通过商品 ID 在异步刷新时丢失选择。该能力只读取既有 `/api/master-data/products`，套用结果保存前只进入当前发票草稿；“保存当前明细到商品库”仍只写商品主数据表。该闭环不新增 API、数据库表、缓存目录或文件落点，不按发票号合并同号 `实际数据` / `报关数据`，不读取付款/报销域。本轮验证：Web build、Tauri check、真实 Tauri 报表 smoke 和路径扫描通过，`invoiceItemProductLibraryCheck` 已覆盖 quick dropdown、保存商品、商品库弹窗搜索、双击套用、撤销恢复和临时商品清理。
- 旧 WinForms `PaginationControl` 的列表分页操作已迁入 Tauri/Web 发票列表与付款/报销列表：两张列表现在支持首页、上一页、下一页、末页、每页 `20/50/100/200`、跳转页和搜索重置，并用浏览器 `localStorage` 恢复搜索关键字与每页条数，默认每页回到旧端 `50` 条口径。前端新增可复用 `ListPaginationControls.tsx` 与 `listViewState.ts`，查询缓存 key 带 `pageSize`，避免不同页大小串用缓存。该闭环只影响前端视图状态和既有分页请求，不新增 API、数据库表、文件落点、缓存目录或系统 C 盘默认目录；发票列表不读取付款/报销域，付款列表不按 `Payment.InvoiceNo` 反查发票，同号 `实际数据` / `报关数据` 仍由当前发票记录隔离。本轮验证：Web `typecheck:api`、Web build、Tauri check 和源码路径扫描通过。
- 旧 WinForms `PaginationControl` 的剩余高频列表操作已继续迁入 Tauri/Web：主数据列表、任务中心、单一窗口操作中心、单一窗口协同看板、审计日志和单据查询页均复用 `ListPaginationControls.tsx`，支持首页/上一页/下一页/末页、每页 `20/50/100/200` 和跳转页；主数据按分类保存搜索关键字和每页条数，任务中心保存关键字和每页条数，单一窗口操作中心/协同看板保存业务类型、状态、关键字等视图状态，审计日志按旧端恢复筛选、时间范围、保留天数和每页条数。相关 query key 均加入 `pageSize`，避免不同页大小串用缓存；所有视图状态只写浏览器 `localStorage`，不新增 API、数据库表、文件目录、缓存目录、默认导出目录或系统 C 盘/AppData/ProgramData 落点。单据查询继续按当前发票记录与 `Type` 过滤，不按同号发票合并 `实际数据` / `报关数据`，付款/报销单据域不参与。本轮验证：Web `typecheck:api`、Web build、Tauri check 和源码路径扫描通过。
- 旧 WinForms `ItemForm` 的纵向录入节奏、预留空白行和 `Ctrl+S` 保存快捷键已迁入 Tauri/Web 发票编辑页：发票编辑页读取既有 `/api/settings` 的 `system.itemEntryBlankRowCount`，按 1-500 范围和默认 20 规范化后，在明细表业务行后渲染占位空白行；用户在占位行输入时才扩展当前发票草稿，保存前继续由 `normalizeInvoiceForSave` 过滤纯空行。明细输入框内 `Enter` / `Shift+Enter`、`Tab` / `Shift+Tab` 按旧端逻辑在同一字段上下移动，页面级 `Ctrl+S` / `Meta+S` 复用原发票保存流程，不绕过编辑状态、审核锁定或当前 API 校验。该闭环仍只处理当前发票草稿和当前 `invoice.id`，不按发票号合并同号 `实际数据` / `报关数据`，不读取付款/报销域，不新增 API、数据库表、缓存目录、默认导出目录或系统 C 盘落点。本轮真实 Tauri 报表 smoke 已新增 `invoiceItemKeyboardNavigationCheck` 并通过，确认占位行输入转草稿行和 `Ctrl+S` 保存均跑通。
- 旧 WinForms 商品资料编辑输入辅助已迁入 Tauri/Web：React 主数据商品编辑页读取既有商品资料和单位资料，商品编码、英文品名、中文品名、HS 编码、材质、品牌、原产地提供历史候选，HS 编码输入保持旧端大写口径，必填口径改回旧端保存校验的“英文品名”；`unitEN -> unitCN`、`packageUnitEN -> packageUnitCN` 按英文单位规范化匹配中文候选，英文单位输入保持大写，失焦、回车和保存前会回填唯一候选，多候选留给操作者选择，且不会覆盖已经手工编辑的中文单位。该闭环只处理商品/单位主数据，不读取发票/报关草稿、不按同号 `实际数据` / `报关数据` 互查，也不读取付款/报销域；不新增 API、数据表、文件落点或系统 C 盘默认目录。
- 旧 WinForms 商品资料编辑中的 HS 编码库衔接已补入 Tauri/Web：商品编辑页 HS 编码字段候选现在合并历史商品资料与本地 HS 编码库；录入或选择 HS 编码后按规范化编码匹配本地库，并回填空白的描述、申报要素、监管条件、检验检疫类别、中文单位和退税率。该能力只读取运行数据根数据库主数据，不联网、不自动保存远端 HS 结果，保存商品前不写数据库或文件。
- 旧 WinForms 主数据列表层常用操作已迁入 Tauri/Web：React 主数据通用列表页支持输入暂停后自动搜索、回车立即搜索、行点击/Enter/Space 打开编辑、行内编辑按钮、行内删除按钮，以及行聚焦时 `Delete` 删除当前行。删除继续复用既有 `/api/master-data/*/{id}`，客户/出口商删除后同步失效发票贸易方档案缓存；该闭环只作用于当前主数据 ID，不读取发票/报关、付款/报销、单一窗口草稿或报表模板，不新增 API、数据表、文件落点或系统 C 盘默认目录。
- 旧 WinForms 主数据编辑页输入流和保存快捷键已迁入 Tauri/Web：通用 `MasterDataEditorPage` 现在支持页面级 `Ctrl+S` / `Meta+S` 保存当前主数据草稿，普通输入、数字和可输入下拉字段支持 `Enter` / `Shift+Enter` 按旧 `InputFlowHelper.RegisterEnterAsTab(...)` 前后流转，多行地址和备注保留换行。前端新增 `formKeyboard.ts` 共用焦点流工具，付款编辑器和主数据编辑器共用同一套行为，后续表单迁移可继续复用。保存仍走既有 `/api/master-data/*` 创建/更新，只写当前客户、出口商、收款对象、商品、港口、单位或 HS 编码记录；不读取发票/报关、付款/报销、单一窗口草稿或报表模板，不新增 API、数据表、文件落点或系统 C 盘默认目录。本轮验证：`node --check scripts\smoke-web-runtime-diagnostics.mjs`、Web build、Tauri check、真实 Tauri 报表 smoke 和源码路径扫描通过，smoke 确认 `masterDataDeleteCheck.keyboardFlowCheck` 的 Enter 流转、`Ctrl+S` 保存提示、联系人字段持久化和临时客户删除 `404`。
- 旧 WinForms HS 编码 Excel 导入、联网查询、详情补全、详情查看和保存本地已迁入 Tauri/Web/API：桌面模式可选择 `.xlsx/.xlsm` 导入，浏览器模式可上传 Excel 导入；远端查询结果可展开查看旧 `HsCodeDetailForm` 同口径字段，详情补全只替换页面内存中的远端结果，操作者确认后才保存到本地 HS 编码主数据。上传导入暂存运行数据根 `Cache/HsCodeImports` 并清理，导入结果写当前运行数据根数据库；该闭环不读取发票/报关、付款/报销或单一窗口草稿，也不创建系统 C 盘默认路径。
- 旧 WinForms `HsCodeSelectionForm` / `HsCodeManagementForm` 的“删除选中”和“清空所有数据”已迁入 Tauri/Web/API：API 新增 `POST /api/master-data/hs-codes/delete-batch` 和 `POST /api/master-data/hs-codes/clear-all`，分别复用 `IHsCodeService.DeleteAsync(IEnumerable<int>)` 与 `IHsCodeService.ClearAllLocalAsync()`；Web 主数据 HS 编码列表新增当前页勾选列、全选、`Ctrl+A` 选中当前页和“删除选中”按钮，工具区新增“清空本地库”危险操作。批量删除只删除选中 HS 编码 ID，清空全部要求管理员和确认文本 `CLEAR`；本轮已把“清空本地库”从浏览器 `window.prompt` 改为页面内危险确认表单，只有输入精确 `CLEAR` 后提交按钮才可用，成功后关闭确认区并刷新列表。真实 Tauri smoke 新增 `hsCodeClearAllCheck`，仅在运行数据根包含 `App_Data/Smoke` 时创建临时 HS 编码并执行清空，随后确认详情接口返回 `404`，非 smoke 数据根自动跳过。该闭环只删除 HS 编码主数据，不删除导入源 Excel、不创建缓存/导出目录，也不读取发票/报关、付款/报销或单一窗口草稿。
- 旧 WinForms 出口商单据章/报关章图片路径浏览已迁入 Tauri/Web：Tauri 新增 `select_exporter_seal_image_file` 桌面命令并纳入 `allow-desktop-bridge` 权限，React 出口商主数据编辑页对 `docSealPath` / `customsSealPath` 显示图片选择按钮；选择结果只回填用户显式图片路径，保存仍写出口商主数据字段，不复制图片、不创建默认印章目录，也不把印章图片写入程序根、运行数据根或系统 C 盘默认目录。
- 旧 WinForms 单一窗口参考词典维护已迁入 Tauri/Web/API 闭环，并继续补齐旧端导入/导出入口：`#/single-window/reference-catalog` 可维护 COO 国家/地区、ACD 国别地区、币制、ACD 贸易方式、运输方式和港口，支持新增、删除、别名编辑、当前页去重、保存覆盖词典、恢复内置词典、导出当前草稿 JSON、上传 JSON 导入并直接保存覆盖词典，以及上传 `.xlsx/.xlsm` 后按当前分类、工作表、表头行、数据起始行和列号映射预览，再追加/替换到当前页草稿。API 新增 `POST /api/single-window/reference-catalog/import-json` 和 `POST /api/single-window/reference-catalog/excel/preview`，前者要求管理员并复用词典校验后写运行数据根覆盖文件，后者要求管理员、只解析上传请求体并返回当前分类预览，不接收服务器任意本地路径、不写临时文件；Excel 预览恢复旧 WinForms 可独立选择表头行和数据起始行的行为，自动列匹配读取指定表头行，未传表头行时兼容原 Web 默认。该闭环只维护参考词典配置，不读取发票/报关草稿、付款/报销单据或业务数据库记录；覆盖文件仍为运行数据根 `SingleWindow/singlewindow_reference_catalogs.override.json`，内置资源仍为程序根 `Resources/SingleWindow/singlewindow_reference_catalogs.json`，不新增默认导出目录、缓存目录、AppData/ProgramData 或系统 C 盘默认落点。
- 旧 WinForms 单一窗口参考词典表格维护快捷操作已迁入 Tauri/Web：`SingleWindowReferenceCatalogPage.tsx` 支持在当前单元格 `Ctrl+V` / 粘贴 Excel 制表符文本并自动补行，工具栏和右键菜单均可批量粘贴，`Ctrl+D` 执行当前页去重，别名列 `F4` 或 `Enter` 打开多行编辑弹窗，右键菜单提供新增一行、删除当前行、批量粘贴、批量去重和编辑别名。该能力只改页面草稿，保存仍走既有参考词典 API 写运行数据根覆盖文件；不新增 API、数据表、缓存目录或默认导出目录，不读取发票/报关、付款/报销或单一窗口草稿业务记录。最新 `tauri:smoke:reports:local` 已在 `singleWindowEditorToolsCheck.referenceCatalog` 中真实验证表格粘贴、F4 别名弹窗、右键菜单和 Ctrl+D 去重。
- 旧 WinForms 单一窗口操作中心目录交接动作已迁入 Tauri/Web/API 闭环：批次详情页新增当前业务目录根保存、`OutBox/SentBox/InBox/FailBox` 派生路径展示和桌面打开动作，保存复用既有客户端档案端点并按当前 `BusinessType` 写业务覆盖目录；“发送到导入目录”复用既有客户端派发端点，把当前批次提交报文复制到显式保存的业务根 `OutBox`。回执包面板新增“收集回执”和“默认目录打包”，从已保存业务回执根自动收集当前批次匹配的 `.xml/.acd` 回执，并在 Tauri 桌面模式下通过显式保存对话框选择 `.swpkg` 后导出回执包。该闭环不新增 API 端点、不新增数据库表、不恢复旧固定客户端路径；客户端目录档案仍写运行数据根数据库，内置交换根仍为运行数据根 `SingleWindow/Client/{Cooimp|Acd|Others}`，外部客户端特殊目录只按用户显式保存处理；提交包/回执包继续只绑定当前单一窗口批次和源 `invoiceId`，不按发票号混合同号 `实际数据` / `报关数据`，也不读取付款/报销域。
- 旧 WinForms 单一窗口操作中心列表级批次动作已继续迁入 Tauri/Web：操作中心列表页现在可选中当前批次，并在列表上方直接执行查看详情、业务目录根读取/保存、派生目录打开、派发到客户端 `OutBox`、自动收集默认回执目录、显式保存 `.swpkg` 回执包导出，以及新增“打包并导入”把回执包立即写回操作中心。详情页仍保留完整的手动提交包导入、回执包导入/导出和客户端桥接工具；列表页补回旧端“选中批次后直接处理”的操作节奏。本轮真实 `--single-window-operation-center-check` smoke 已扩展到 COO + ACD 双业务：分别创建临时发票和提交包，在列表页保存 smoke 客户端根、派发到 `OutBox`、从 `InBox` 收集匹配回执、通过 Tauri 保存路径导出 `.swpkg`，随后调用既有回执包导入 API 写入回执日志并刷新批次状态；最新结果为 `allBusinessesSucceeded=true`，COO `detailStatus=Approved`、`detailReceiptRecordCount=1`、`detailReceiptMessages=["Smoke approved"]`，ACD `detailStatus=Accepted`、`detailReceiptRecordCount=1`、`detailReceiptMessages=["Smoke ACD accepted"]`、`receiptPackageHeader=PK`、`receiptPackageSize=967`。该能力复用既有 API 和客户端档案，不新增数据库结构或默认系统盘目录；路径只来自用户显式保存、运行数据根默认客户端交换根或保存对话框返回路径，导入工作目录默认走运行数据根短生命周期目录。
- 旧 WinForms 单据管理 `ExportEnabledItemsAsync` 导出完成后询问打开输出目录的体验，已映射为 Tauri/Web 任务中心“输出”列的桌面打开动作：后台任务完成后只把任务快照中已经存在的 `OutputPath` 展示为“打开任务输出”图标按钮，调用 Tauri `open_path`。该入口不恢复旧默认导出目录、不推导新目录、不写数据库，也不按发票号、付款参考号或业务号跨域查询；发票/报关类任务继续只使用当前发票记录 ID，付款/报销任务继续只使用付款记录 ID。
- 旧 WinForms 导出/打印动作结束后用户能立刻追踪结果的闭环，已继续映射为 Tauri/Web 任务深链：任务中心支持 `#/jobs?jobId=...`，自动按任务号筛选并高亮目标行；发票报表 PDF、发票托单、单据包、单据邮件、付款/报销 PDF、Excel 工具类导出、列表托单导出、PDF 合并、批量报表 ZIP 和任务重试都会在创建任务后提供“查看任务”或直接聚焦新任务。该能力只改变前端路由查询参数、列表筛选和行高亮，不新增 API、不写数据库、不创建默认输出目录；任务输出仍由各后台任务原有路径策略控制，发票/报关与付款/报销数据域继续按当前记录 ID 隔离。
- 旧单据管理 `BatchExport.Items` 已继续接入发票报表面板的“单据包/邮件附件”模板列表：Tauri/Web 会读取 `/api/settings` 中每个旧导出项的模板路径、名称、启用状态、带章状态和顺序，作为新单据包默认模板顺序、显示名、默认勾选和默认带章。匹配不到旧路径的现有模板仍显示在列表中，报表模板设计器也继续枚举程序根 `Templates/` 下全部模板，避免旧批量导出配置限制模板维护能力。本轮补强顺序验证：API 设置测试覆盖 `BatchExport.Items` 顺序写入程序根 `appsettings.json` 后读回保持不变。
- Tauri/Web 设置页已补齐旧单据管理 `BatchExport` 配置维护入口：`#/settings` 的“单据包设置”可编辑文件命名规则、文件夹命名规则、默认合并 PDF、默认生成 ZIP，并维护导出项的名称、启用、带章、模板路径和顺序；模板路径可从当前 `ExportDocument` 模板中选择，也保留手工路径输入兼容旧配置。本轮继续补入 Tauri 桌面模板文件选择：主进程新增 `select_report_template_file`，Web 设置页在桌面模式下可用文件按钮选择 `.html/.htm/.scriban/.txt` 模板文件，选择结果只写入设置草稿，不复制模板文件、不创建默认目录。本轮继续将“单据包设置”从巨型 `SettingsPage.tsx` 抽入 `DocumentTemplateSettingsPanels.tsx`，页面层只负责设置读取/保存和区块编排，面板模块独立维护 `batchExport.items` 草稿、模板选择、顺序调整和桌面模板文件选择；设置键、ARIA 标签、按钮文案和保存 payload 保持不变。最新 Tauri 报表 smoke 通过 `batchExportOrderInteractionCheck` 在真实设置页临时命名前两项并点击“下移”，确认顺序从 Alpha/Beta 变为 Beta/Alpha。保存仍走 `/api/settings` 写程序根 `appsettings.json`，不新增数据库表、默认导出目录或系统 C 盘用户目录落点。
- 发票报表/单据包面板已补入直达旧批量单证配置入口：`InvoiceReportPreviewPanel.tsx` 提供“管理单证模板”按钮，跳转 `#/settings?section=documentTemplates`；旧 `#/settings?section=batchExport` 仍兼容进入“单证模板中心”，并定位到 `[aria-label="单证模板设置"]`。该入口只改变前端路由和焦点定位，不新增 API、不写数据库、不创建运行目录；最新 Tauri 报表 smoke 已通过 `batchExportSettingsButtonCheck` 与 `batchExportSettingsDeepLinkCheck` 验证按钮存在、可用，并能从发票报表页深链到单证模板设置区。
- 发票报表/单据包面板已继续补入旧 WinForms `DocumentManagerForm` 页内配置闭环：`InvoiceReportPreviewPanel.tsx` 的“单据包配置”可折叠区支持在当前发票上下文编辑文件名规则、文件夹规则、默认合并 PDF、默认生成 ZIP，以及导出项启用、名称、模板路径、带章和顺序；保存复用既有 `/api/settings`，不新增端点。Tauri 桌面可从该面板显式选择模板文件，历史配置中未进入模板枚举的旧/外部模板路径也会保留为可用单据项，继续参与单据包预览、ZIP/文件夹导出和邮件附件生成。该闭环不复制模板、不写数据库、不创建默认导出目录，发票/报关单据 `batchExport.items` 与付款/报销 `paymentTemplates` 继续独立。
- 设置中心继续按业务心智拆分：原“单据与模板”分类已拆为“单证模板中心 / Excel 导入 / 汇率与币制”。单证模板中心只放 `单证模板设置` 和 `付款/报销模板设置`；Excel 导入只放 `excelImport` / `excelImportSchemes` 方案与字段映射；汇率与币制只放 `exchangeRate` 源地址、缓存分钟和常用币种。既有 `section=batchExport`、`section=documentTemplates`、`section=paymentTemplates`、`section=excelImport`、`section=exchangeRate` 深链继续兼容，旧 `section=documents` 映射到单证模板中心。该拆分只改变 React 分类导航和 smoke 检查，不改变 `/api/settings`、配置 JSON 结构、模板路径策略、Excel 方案保存策略、汇率保存策略或运行目录策略。
- 发票报表页“邮件附件”区域也补入邮件设置直达入口：`InvoiceReportPreviewPanel.tsx` 新增“邮件设置”按钮，跳转 `#/settings?section=email`；`SettingsPage.tsx` 读取 `section=email` 后会定位到 `[aria-label="邮件与备份"]`，方便从单据邮件操作流直接修改 SMTP 与单据邮件默认主题/正文。该入口只改变前端路由和焦点定位，不新增 API、不写数据库、不创建运行目录；最新 Tauri 报表 smoke 已通过 `documentEmailSettingsButtonCheck` 与 `documentEmailSettingsDeepLinkCheck` 验证按钮存在、可用并能从单据邮件区域深链到邮件与备份设置区。
- 旧单据管理 `ZipAfterExport` 已继续接入发票报表面板：Tauri/Web 的“生成 ZIP”开关默认读取 `/api/settings` 中的 `batchExport.zipAfterExport`。保持勾选时 `document-package` 任务仍写用户显式 `.zip`；取消勾选时前端改用目录选择，API 仍先在运行数据根缓存生成 PDF，然后在用户显式目录下按旧 `BatchExportSettings.OutputFolderPattern` 创建批次文件夹并复制 PDF/可选合并 PDF。该路径不创建默认导出目录，也不把文件写到系统 C 盘用户目录。
- 发票编辑页报表面板已继续接入旧单据管理的“生成单据附件后发送邮件”能力：React 在同一组单据包模板选择下新增“邮件附件”收件人、主题、正文和发送任务按钮；API 新增 `POST /api/reports/invoices/{invoiceId}/document-email`，复用发票单据 PDF 生成 helper 和 `IEmailService`。临时 PDF 写运行数据根 `Cache/ReportDocumentEmails/{jobId}`，附件 PDF 文件名同样复用旧 `BatchExportSettings.OutputFileNamePattern` 与 `BatchExportPathHelper` 规则，发送完成或失败后清理；任务只读取当前发票/报关数据域、客户邮箱和程序根 `appsettings.json` SMTP 配置，不读取付款/报销表，不创建默认附件目录。本轮继续将单据邮件默认主题/正文从前后端硬编码收拢到 `EmailConfig.DocumentEmailSubjectTemplate` 与 `EmailConfig.DocumentEmailBodyTemplate`，设置页“邮件与备份”可维护这两项，发票页按 `{InvoiceNo}`、`{Customer}`、`{Date}` 预填，API 后台任务在请求为空时用同一配置兜底，用户手工输入仍优先。最新真实 Tauri 报表 smoke 已把该能力从控件/API 接入推进到 SMTP 发送闭环：脚本启动本机回环 SMTP，临时写入并恢复程序根 SMTP 设置，在发票页真实点击“发送邮件”，等待 `ReportDocumentEmail` 成功，并验证收到 1 封邮件、5 个 PDF 附件、运行数据根 `Cache/ReportDocumentEmails/{jobId}` 已清理、设置已恢复、SMTP 已关闭；该验证仍不读取付款/报销表，不按 `Payment.InvoiceNo` 或同号发票号跨域合并。
- 付款/报销单据已补齐 Tauri/Web/API 报表闭环：新增 `/api/reports/payments/{paymentId}/html-preview` 和 `/api/reports/payments/{paymentId}/pdf`，付款编辑页可选择 `PaymentVoucher` 模板预览、预览后手工打印并创建 PDF 后台任务；输出路径仍由用户显式输入或 Tauri 保存对话框选择，最终 PDF 不写默认系统盘目录。
- 付款/报销草稿 HTML 预览已与发票草稿预览对齐：`PaymentEditorPage.tsx` 把当前 `normalizePaymentForSave` 草稿传给 `PaymentReportPreviewPanel.tsx`，调用 `/api/reports/payments/draft/html-preview` 渲染当前未保存字段并显示存储策略；该策略明确只使用请求体付款/报销草稿、程序根 `Templates/Internal` 模板和收款对象/出口商主数据，不按 `Payment.InvoiceNo` 反查发票/报关单据，不写数据库、缓存、默认导出目录或系统 C 盘。正式 PDF 仍只基于已保存 `paymentId`，当前付款页存在未保存修改时禁用“生成 PDF”并提示先保存，避免草稿预览和正式 PDF 输出来源不一致。
- 付款/报销模板闭环继续从单一付款凭证扩展到费用报销明细：`PaymentReportPreviewPanel.tsx` 暴露当前选择模板与当前预览模板的无视觉 `data-*` 状态，`scripts/smoke-web-runtime-diagnostics.mjs` 在真实 Tauri/Web 付款编辑页中启用 `payment_voucher_template.html` 和 `expense_reimbursement_template.html`，先完成付款凭证保存/草稿预览，再切换到费用报销模板并校验 iframe 返回的模板路径、付款方、业务科别、备注、差旅费和 CNY 小计。该链路仍只调用付款/报销报表 API，不通过 `Payment.InvoiceNo` 读取发票/报关数据；模板读取仍来自程序根 `Templates/Internal`，HTML 预览只留在前端内存，PDF 输出仍要求已保存 `paymentId` 和用户显式路径。最新 `tauri:smoke:reports:local` 通过，`paymentReportCheck.reimbursementTemplatePreviewCheck.skipped=false` 且 `frameCheck.found=true`。
- 付款/报销编辑页已继续补齐旧 WinForms 支付对象资料套用闭环：React 付款编辑页读取既有 `/api/master-data/payees` 和 `/api/master-data/exporters`，可从收款对象资料库选择支付对象，并按人民币/美金账号切换回填收款方、银行、账号和 `PayeeId`；付款方输入复用出口商中文名候选，费用拆分字段变更会按旧端规则重新汇总 CNY 金额。该闭环只读收款对象、出口商主数据和当前付款草稿，保存仍只写当前付款/报销记录；不按 `Payment.InvoiceNo` 反查发票，不读取或修改发票/报关、单一窗口草稿或单据模板，不新增 API、数据库表、默认导出目录、缓存目录、AppData/ProgramData 或系统 C 盘默认落点。
- 付款/报销编辑页已继续补齐旧 WinForms `PaymentForm` 键盘流：页面级 `Ctrl+S` / `Meta+S` 复用既有保存 mutation 保存当前付款草稿，普通输入、日期、数字和下拉字段内 `Enter` / `Shift+Enter` 按旧 `InputFlowHelper.RegisterEnterAsTab(gbEdit)` 口径前后移动，多行备注保留换行。该能力只写当前付款/报销记录，`Payment.InvoiceNo` 仍是业务参考文本，不按编号读取或修改发票/报关数据域，不新增 API、数据库表、缓存目录或默认导出目录。最新 `tauri:smoke:reports:local` 已在 `paymentReportCheck.keyboardFlowCheck` 中真实验证 Enter 流转、Ctrl+S 成功提示和项目字段持久化。
- 旧付款/报销模板管理 `PaymentTemplates` 已接入 Tauri/Web 主线：`#/settings` 的“付款/报销模板设置”可维护模板名称、模板路径、启用、带章和顺序，保存仍写程序根 `appsettings.json` 的独立 `paymentTemplates` 配置域；付款/报销单据预览和 PDF 面板读取该配置后按旧顺序、显示名、启用状态和带章默认值展示 `PaymentVoucher` 模板，没有配置时回退到程序根 `Templates/Internal` 模板枚举。本轮同样补入 Tauri 桌面模板文件选择按钮，付款/报销模板路径可通过 `select_report_template_file` 显式选择 `.html/.htm/.scriban/.txt` 模板文件；浏览器模式继续保留下拉和手工路径输入。本轮继续将“付款/报销模板设置”与“单据包设置”一起收敛到 `DocumentTemplateSettingsPanels.tsx`，两个配置域仍分别写 `paymentTemplates` 与 `batchExport.items`，不共享草稿数组、不按业务号互查对方数据。本轮补齐旧配置别名兼容：设置页和付款报表面板现在同时接受 `PaymentVoucher`、`PaymentDocument` 和 `Internal`，避免历史 `PaymentDocument` 配置能保存却不显示。API 设置测试已覆盖 `PaymentTemplates` 顺序保存/读回；最新 Tauri 报表 smoke 通过 `paymentTemplateOrderInteractionCheck` 在真实设置页临时命名前两项并点击“下移”，确认顺序从 Alpha/Beta 变为 Beta/Alpha，并通过 `configuredTemplateSettingsCheck` 临时写入 `PaymentDocument` 配置，验证付款报表页只显示启用模板、隐藏禁用模板路径、应用 `ShowSeal=false` 默认值，然后恢复原设置。该配置不与出口单据 `BatchExport.Items` 混用，不新增数据库表、默认导出目录或模板复制目录。
- 付款/报销报表面板已补入直达模板维护入口：`PaymentReportPreviewPanel.tsx` 新增“模板设置”按钮，跳转 `#/settings?section=paymentTemplates`；`SettingsPage.tsx` 读取 `section=paymentTemplates` 后自动滚动定位到 `[aria-label="付款/报销模板设置"]`。该入口只改变前端路由和焦点定位，不新增后端端点、不写数据库、不创建运行目录；最新 Tauri 报表 smoke 已通过 `templateSettingsButtonCheck` 与 `templateSettingsDeepLinkCheck` 验证按钮存在、可用，并能从付款报表页深链到付款/报销模板设置区。
- 付款编辑页报表面板已从 `PaymentPages.tsx` 拆成 `PaymentReportPreviewPanel.tsx`：付款表单继续负责业务字段和保存，报表组件独立负责模板选择、带章开关、HTML 预览、预览后手工打印、PDF 输出路径、Tauri 保存对话框和任务中心缓存失效，避免后续报销单模板、固定版式 renderer 或更多输出格式继续堆到付款编辑页；打印动作只使用当前预览 HTML 的前端内存 iframe 和浏览器/WebView 打印对话框，不新增后端文件路径。
- 付款/报销编辑页基础信息、业务信息、金额和费用已从 `PaymentPages.tsx` 拆成 `apps/export-doc-web/src/features/payments/PaymentFormPanels.tsx`：付款页面继续负责列表、查询、保存、路由和报表面板编排，表单面板负责字段显示与局部回调。该拆分保持 `/api/payments*`、付款/报销报表、`Payment.InvoiceNo` 业务参考文本和运行数据根数据库行为不变，不与发票/报关数据域合并。
- 付款/报销列表、编辑器和保存模型已从 `PaymentPages.tsx` 继续拆成 `PaymentListPage.tsx`、`PaymentEditorPage.tsx` 和 `paymentModel.ts`，原 `PaymentPages.tsx` 只做路由兼容导出。列表页负责分页搜索和表格打开，编辑页负责详情读取、保存 mutation、路由消息和报表面板编排，保存模型负责默认值和字段归一化。本轮继续补齐旧 WinForms 付款/报销删除入口：编辑页对已保存付款/报销记录显示“删除”按钮，确认后调用既有 `DELETE /api/payments/{id}`，成功后移除当前详情缓存、刷新付款列表与任务缓存并返回列表提示结果；该删除只作用于当前付款/报销记录，不按 `Payment.InvoiceNo` 反查发票/报关数据，不读取或写入发票、明细、报关草稿或模板文件。`scripts/smoke-web-runtime-diagnostics.mjs` 已新增 `--payment-delete-check`，会创建临时付款并真实点击编辑页删除按钮，`scripts/smoke-tauri-desktop.ps1` 的 `-VerifyWebReports` 参数已接入该检查；本轮已完成脚本语法与 PowerShell AST 验证，并复跑 `npm --prefix apps\export-doc-tauri run tauri:smoke:reports:local` 通过，最新 smoke 中 `paymentDeleteCheck` 命中删除按钮、确认框、列表成功提示和详情接口 `404`。该拆分、删除闭环与 smoke 接入不改变 `/api/payments*`、报表预览/PDF、SQLite 运行数据根或 `Payment.InvoiceNo` 业务参考文本语义。
- 发票编辑页信用证导入面板已从 `App.tsx` 拆成 `apps/export-doc-web/src/features/invoices/InvoiceLetterOfCreditPanel.tsx`：发票页面继续负责草稿、保存、主数据和明细，信用证组件独立负责来源路径、Tauri 文件选择、打开路径、导入 API 调用、导入提示和 busy 状态回传，避免后续 OCR 细化或多来源解析继续堆到主应用入口。
- 旧 WinForms 发票信用证 `AI合规审查` 已迁入 Tauri/Web/API 闭环：应用层 `ILetterOfCreditComplianceReviewService` 从当前发票草稿和信用证文本构建审查上下文，API `POST /api/tools/letter-of-credit/review` 读取程序根 `appsettings.json` 中的 AI 配置并调用兼容 OpenAI 的 `IAIService`，React 信用证面板新增 `AI 审查` 按钮和只读报告区。该入口支持未保存草稿即时审查，锁定发票仍可只读审查；报告只返回前端内存，不写数据库、不生成文件、不创建默认目录。同一发票号的 `实际数据` / `报关数据` 只以当前请求草稿口径参与审查，不按发票号合并另一口径，也不读取付款/报销单据。
- Tauri/Web 设置页已补齐旧 WinForms `AI 审查配置` 中的系统提示词维护入口：`SettingsPage.tsx` 在“AI 与单一窗口”区提供多行 `AI 系统提示词`，复用 `/api/settings` 保存 `AI.SystemPrompt` 到程序根 `appsettings.json`；`AI.ApiKey` 仍按“更新敏感字段”控制，系统提示词作为非密钥字段可单独保存。该入口不新增 API、数据库表、缓存目录或默认导出目录，也不读取发票/报关、付款/报销业务域。
- Tauri/Web 设置页已补齐旧 WinForms `单一窗口默认申报资料` 入口：申报员姓名、申报员身份证号、申报员电话、签证机构代码、领证机构代码和申请地址按旧字段口径显示；签证机构候选通过只读 `GET /api/single-window/coo/issuing-authorities` 获取，sidecar 使用与旧 WinForms 一致的程序根 `Resources/SingleWindow` 内置/地址覆盖 JSON 与运行数据根 `SingleWindow` 覆盖 JSON loader。选择签证机构后自动回填 4 位代码，并在领证机构/申请地址未手工改动时联动；保存仍复用 `/api/settings` 写程序根 `appsettings.json`，不新增数据库表、缓存目录、默认导出目录，也不读取发票/报关、付款/报销业务域。
- 发票编辑页明细编辑器已从 `App.tsx` 拆成 `apps/export-doc-web/src/features/invoices/InvoiceItemsEditor.tsx`：发票页面继续负责发票级状态、主数据、保存和跨面板编排，明细模块独立负责行编辑、数量/箱数/体积/重量/金额联动、合计、保存归一化和有效行判断。旧 WinForms `ItemForm` 的常用行级操作已继续迁入 Tauri/Web：复制新增行、上下移动、删除、`Insert` 新增、`Ctrl+Shift+D` 复制行、`Alt+Up/Down` 移动行、当前单元格向下填充、剪贴板/Excel 表格粘贴自动扩行、多选单元格复制/清空、显示/隐藏列，以及明细局部 `Ctrl+Z` 撤销和 `Ctrl+Y` / `Ctrl+Shift+Z` 重做；商品库能力已通过既有 `/api/master-data/products` 接入明细工具条，支持搜索/刷新商品库、quick dropdown 套用、旧 `ProductListForm` 式商品库弹窗表格选择、双击套用、从商品库新增明细、将当前聚焦明细保存或更新到商品库，商品到明细和明细到商品字段映射集中在 `invoiceProductLibrary.ts`。当前显示列状态只驻留前端内存，隐藏列后多选复制和 Excel 表格粘贴按当前可见列顺序处理，不向隐藏字段悄悄写入数据；粘贴内容、选区、历史栈、列显示状态和商品库套用后的未保存明细只进入前端当前发票状态，仍需用户保存发票才写运行数据根数据库；保存到商品库只写商品主数据表。该拆分是 Tauri/Web 主线模块化，不回退 WinForms，不新增运行时存储路径，不读取付款/报销域，也不按发票号合并实际/报关数据。
- 发票编辑页基础信息、客户/出口商、运输与条款、唛头入口和明细容器已继续从 `App.tsx` 拆成 `apps/export-doc-web/src/features/invoices/InvoiceFormPanels.tsx`：表单面板负责字段显示、客户/出口商档案套用和局部回调，页面层保留查询、保存、路由和跨面板编排。该拆分保持发票保存、客户/出口商快照回填、COO/ACD 跳转和报表/信用证入口行为不变，不新增运行时存储路径。
- 发票列表、发票表格和发票编辑页已整体从 `App.tsx` 拆成 `apps/export-doc-web/src/features/invoices/InvoicePages.tsx`：`App.tsx` 继续收敛为登录、会话、导航和路由壳，发票 feature 模块承接分页查询、草稿入口、保存 mutation、客户/出口商档案查询、明细协调、信用证和报表面板编排。该拆分保持 `/api/invoices*`、发票报表、信用证导入和 COO/ACD 入口行为不变，不新增运行时存储路径。
- 发票列表、编辑器和保存模型已从 `InvoicePages.tsx` 继续拆成 `InvoiceListPage.tsx`、`InvoiceEditorPage.tsx` 和 `invoiceModel.ts`，原 `InvoicePages.tsx` 只做路由兼容导出。列表页负责分页搜索和表格打开，编辑页负责详情读取、客户/出口商档案、保存 mutation、明细协调、信用证和报表面板编排，保存模型负责默认值、导入草稿和字段/明细归一化；该拆分不改变 `/api/invoices*`、信用证导入、报表预览/PDF、COO/ACD 入口、SQLite 运行数据根或发票/付款数据域边界。
- 任务中心装箱分析已从 `JobCenterPage.tsx` 拆成 `apps/export-doc-web/src/features/tools/container-packing/ContainerPackingPanel.tsx`：任务中心页继续负责任务列表、取消/重试、PDF 合并、批量报表 ZIP 和 Excel 工具；装箱组件负责柜型、货物、托盘/重心规则、颜色 ARGB 转换、分析请求构造、结果展示、前端 2D SVG 平面可视化、按需加载的 Three.js 三维可视化、装柜方案保存/加载/删除和柜型保存/删除。旧 WinForms 装柜工具栏的“添加货物、清空列表、立即刷新、显示: 仅外轮廓/完整分格”已补入该组件：清空允许货物表回到空状态，立即刷新复用内存分析请求，完整分格只在 2D SVG 上按当前分析响应绘制受限网格线。旧 `ContainerLoadingViewModel` 的 180ms 防抖自动重算、后台刷新中状态和“仅保留最后一次结果”语义已迁入同一组件：页面默认开启自动刷新，输入变化后用请求签名和序号忽略过期响应，状态栏同步展示货物、已装/未装、托盘、体积/重量、柜数、重心偏差和刷新状态；手动“立即刷新/分析”仍保留。旧 `ContainerProjectSelectionForm` 删除方案前的确认也已恢复，删除时提示 `确定删除方案 '<name>' 吗?`，避免误删当前装柜方案。装箱分析保持只处理内存输入/输出，俯视图、侧视图、柜门视图、重心标记、颜色图例和三维装柜场景都直接使用分析响应与当前柜型尺寸在页面内渲染；`ContainerPackingScene3d.tsx` 作为独立异步 chunk 加载 `three`，并已补齐三维自动旋转暂停/恢复、等轴/俯视/柜门固定视角切换、拖拽旋转和重置视角控件；固定视角切换会进入手动视角，避免检查柜门或俯视布局时被自动旋转带走。该交互不新增后端接口、文件缓存、数据库字段、默认目录或系统 C 盘落点。装柜方案与柜型管理通过 `/api/tools/container-packing/projects*` 和 `/api/tools/container-packing/container-types*` 写运行数据根数据库表 `ContainerProjects`、`ContainerProjectItems`、`ContainerTypeDefinitions`，不读写发票、报关、付款或报销业务表。
- 旧 WinForms 独立“装柜模拟器”入口已继续迁入 Tauri/Web 工作区闭环：装箱组件和 Three.js 三维场景从 `features/jobs` 归档到 `apps/export-doc-web/src/features/tools/container-packing/`，新增 `ContainerPackingPage.tsx`、左侧主导航“装箱”和 `#/tools/container-packing` 路由；任务中心仍复用同一组件，避免两套装箱逻辑分叉。该入口不新增 API、不新增数据库表或文件路径，分析仍为内存输入/输出；方案和柜型保存继续只走既有 `/api/tools/container-packing/projects*` 与 `/api/tools/container-packing/container-types*`，写当前运行数据根数据库，不读取发票/报关、付款/报销业务域，也不新增 AppData/ProgramData 或系统 C 盘默认落点。
- 旧 WinForms 底部“导入 Excel / 导出空白完整导入模板 / 导出空白托单模板 / 导入模板转换为托单”已继续迁成 Tauri/Web 独立工具页闭环：Excel 导入与托单组件从任务中心目录迁到 `apps/export-doc-web/src/features/tools/excel/ExcelToolsPanel.tsx`，新增 `ExcelToolsPage.tsx`、左侧主导航“Excel”和 `#/tools/excel` 路由；任务中心仍复用同一组件并继续负责任务列表、取消/重试、PDF 合并和批量报表 ZIP。该工具页覆盖导入预览、打开发票草稿、模板导出、空白托单、模板转托单和发票托单导出。源 Excel 仍为用户显式路径，模板仍从程序根 `Resources/ExcelTemplates/` 读取，`.xlsx` 输出仍为用户显式路径或 Tauri 保存对话框结果；不新增 API、数据库写入、默认目录、缓存目录、AppData/ProgramData 或系统 C 盘落点。最新 `--job-center-check` smoke 已在任务中心真实填写“模板输出”“空白托单”“转换源/转换输出”和“发票 ID/发票托单”，点击“导出模板”“导出空白托单”“转为托单”“导出发票托单”，等待 `ExcelTemplateExport`、`BlankBookingSheetExport`、`BookingSheetConvert` 和 `InvoiceBookingSheetExport` 成功，校验四个输出均为 ZIP 结构 `PK` 文件头并在退出前删除；转换源临时 Excel 和发票托单临时发票也被清理。
- 旧 WinForms Excel 导入后的客户/出口商承接已继续补入 Tauri/Web Excel 工具页：导入预览返回的客户和出口商会在工具页显示，操作者可显式保存到主数据；前端复用既有 `/api/master-data/customers` 与 `/api/master-data/exporters`，按英文名称查找同名记录，存在则更新同名主数据，不存在则新增，且更新时保留 Excel 未提供的既有字段。该闭环不新增后端端点、数据库表、文件缓存或默认目录；预览仍只读用户显式 Excel 路径，发票草稿仍进入新建发票流程，同号 `实际数据` / `报关数据` 不按发票号互相覆盖，付款/报销单据域不参与。
- 旧 WinForms `InvoiceImportConflictForm` 已继续迁入 Tauri/Web Excel 工具页：导入预览后按 `InvoiceNo + Type` 在发票域内查找同号同类型记录，冲突时提供覆盖现有、追加明细和另存新号。覆盖/追加只把 Excel 结果合并到现有发票编辑页草稿，仍需用户核对并点击保存才写运行数据根数据库；另存新号进入新建发票草稿。该闭环不新增 API 或文件落点，不把同号 `实际数据` / `报关数据` 混写，也不读取付款/报销域。
- 旧 WinForms 当前发票导出订舱托单已进一步补入发票编辑页闭环：`InvoiceReportPreviewPanel.tsx` 在已保存发票上下文中提供“托单 Excel”路径、Tauri Excel 保存对话框和“导出托单”按钮，直接调用既有 `startInvoiceBookingSheetExportJob`，不再要求操作者跳到任务中心手填发票 ID。该入口只使用当前 `invoiceId`，不按发票号跨 `实际数据` / `报关数据` 合并查询，不读取付款/报销数据域；输出 `.xlsx` 仍是用户显式路径，模板仍来自程序根 `Resources/ExcelTemplates/`，不新增 API、数据库表、默认目录、缓存目录、AppData/ProgramData 或系统 C 盘落点。
- 旧 WinForms 发票列表右键中的“导出货代订舱托单”和“单一窗口办理”也已补入并验证为 Tauri/Web 发票列表桌面闭环：`InvoiceListPage.tsx` 的行操作新增托单 Excel 与单一窗口办理入口；单一窗口办理面板可针对当前行发票直达编辑 COO/ACD、导出 COO/ACD `.swpkg` 提交包、导入单一窗口回执包、创建托单 Excel 后台任务并跳转操作中心。COO/ACD 提交包导出前已补回旧 WinForms 导出审查护栏，会按当前发票和当前业务类型展示错误、警告、来源差异、锁定字段和可自动修复项，并可先修复可修复分组再继续导出。本轮 `--invoice-list-desktop-workflow-check` 已接入 `tauri:smoke:reports:local`：smoke 从发票列表真实点击托单导出、单一窗口办理、导出 COO 包、导出 ACD 包和导入回执包，验证托单任务 `Succeeded`、`.xlsx/.swpkg` 文件头为 `PK`、COO 写回 `Approved`、ACD 写回 `Accepted`、Tauri 保存/打开文件命令全部命中并清理临时发票和运行目录。该入口复用既有单一窗口提交包、回执包导入、导出审查/修复和托单后台任务 API，不新增后端；所有业务动作只传当前 `invoice.id`，不按发票号跨 `实际数据` / `报关数据` 合并，也不读取付款/报销域。提交包和托单输出路径仍由用户显式输入或 Tauri 保存对话框返回；回执包导入源仍由用户显式选择或页面内受控输入，不新增浏览器 prompt、默认目录或系统盘落点。
- Tauri/Web 设置页已补齐旧 WinForms Excel 导入方案管理：`#/settings` 的“Excel 导入方案”可维护当前方案名、已有方案、出口商/客户/发票/明细字段映射、明细起始行和列号，并支持加载方案、保存当前映射为方案、删除方案和恢复默认映射。该能力复用 `/api/settings`，保留 `excelImport` 与 `excelImportSchemes` 配置域并写程序根 `appsettings.json`，不新增数据库表、不创建默认导入/导出目录，不改变源 Excel 用户显式路径、输出 `.xlsx` 用户显式路径或程序根 `Resources/ExcelTemplates/` 模板读取策略，也不写 AppData/ProgramData 或系统 C 盘。
- 审计日志管理已从旧 WinForms 迁成 Tauri/Web/API 闭环：`apps/export-doc-web/src/features/audit-logs/AuditLogPage.tsx` 保留主导航“审计”和 `/audit-logs` 路由，复用 `/api/audit-logs` 查询，并新增管理员管理区；sidecar 新增 `/api/audit-logs/export`、`/api/audit-logs/delete` 和 `/api/audit-logs/cleanup`。管理员可按筛选条件导出 Excel、输入 `DELETE` 删除当前筛选结果、按保留天数清理历史审计日志；本轮多人权限复核后，审计日志列表也已收紧为仅管理员可查看。Excel 导出只写用户显式 `.xlsx` 路径或 Tauri 保存对话框返回路径，不提供默认导出目录；删除/清理只操作运行数据根数据库 `AuditLogs` 表，不读取发票/报关或付款/报销业务表，不按编号互查两个数据域。
- 智能 OCR 已补齐旧 WinForms 的主要桌面交互到 Tauri/Web/API 主线：API sidecar 保留 `/api/tools/ocr/recognize-image` 路径识别，并新增 `/api/tools/ocr/recognize-image-content` 用于剪贴板等无路径内存图片；两者均进入 OpenAPI/生成 client。Tauri 桌面桥提供 `select_ocr_image_file` 显式图片选择和 `read_ocr_image_file_as_data_url` 只读预览；Web `SmartOcrPage.tsx` 接入主导航“OCR”和 `/tools/ocr`，支持显式路径、剪贴板图片粘贴/Ctrl+V、图片预览、缩放、鼠标拖拽平移、OCR 行框叠加、行框表格和识别文本复制。路径图片只读用户显式路径；内存图片只通过请求体传递，不落地为临时文件、不复制源图、不写数据库或默认输出目录；OCR 模型仍随程序根 `OcrModels/` 发布。
- 软件更新主线已从旧 WinForms 更新窗口和旧 API staged 包方案收敛为 Tauri updater-only。源码已移除 `GET/POST /api/system/update*`、`IUpdateService` / `UpdateService`、旧 API 更新 DTO、`system.updateUrl` / `System.UpdateSignaturePublicKey` 设置项、旧 staged 包和 `handoff_update_package` 交接命令。Web `features/system/UpdateCenterPage.tsx` 只通过 Tauri IPC 调用 `check_tauri_update` / `install_tauri_update`：检查动作展示当前版本、最新版本、目标平台、下载地址、发布时间和“更新日志”，其中“更新日志”来自 Tauri updater endpoint JSON 的 `notes`；安装动作交给 Tauri updater 下载、验签、安装并请求重启。该能力不访问发票/报关或付款/报销业务表，不新增系统盘默认用户目录。
- 发布侧更新服务器只需提供 Tauri updater 标准 JSON 和安装包，例如 `version`、`notes`、`pub_date`、`platforms.{target}.url`、`platforms.{target}.signature`。不同平台 target 键随 bundle 类型变化，例如 Windows NSIS 通常使用 `windows-x86_64-nsis`；真实验收需要覆盖签名、公钥、安装器重启和失败回滚。业务数据库和运行数据继续留运行目录 `App_Data`，授权镜像保留在 `Security/license.dat`，试用/注册防重置锚点由平台机器级安全存储承接，更新下载缓存和安装事务由 Tauri updater / 平台安装器管理。
- 产品版本统一从根目录 `version.json` 维护，当前为 `0.1.1`；`scripts/sync-version.mjs` 同步 .NET `Directory.Build.props`、Tauri 配置、Cargo 包、npm package 和 lock 文件。`run-tauri-local.ps1`、`build-windows-desktop-run.ps1` 与 `prepare-tauri-bundle.mjs` 会在构建前执行同步，API `/healthz` 暴露编译后的 `productVersion` / `informationalVersion`，发布侧 updater JSON 的 `version` 必须与该版本一致。
- 旧启动静默检查和强制更新门禁已从前端主流程移除，不再依赖 API 更新清单阻断业务界面。需要强制升级策略时，应由发布侧 endpoint、签名策略和安装器分发规则实现；前端更新中心保持为显式检查和安装入口，避免业务工作区背后隐藏旧更新 API 状态。
- 注册/授权已从旧 WinForms 注册窗口迁成 Tauri/Web/API 基础闭环：API sidecar 新增 `GET /api/system/license` 和 `POST /api/system/license/register`，应用层以 `ILicenseService` 抽象授权状态和注册动作，Infrastructure `RuntimeLicenseService` 复用共享授权码编解码并把授权镜像写到运行数据根 `Security/license.dat`、机器码 seed 镜像写到 `Security/machine-id.seed`。本轮新增机器级授权锚点，Windows 使用 `HKLM/HKCU\SOFTWARE\ExportDocManager\RuntimeLicense\MachineTrialAnchor` + DPAPI LocalMachine，macOS 使用 Keychain，Linux 使用 Secret Service；锚点保存试用起点、最后运行时间、机器 seed、本机密封随机量和已验证注册码/到期日，删除程序目录或 `App_Data` 后不会恢复 7 天试用，也不会丢失已注册状态。项目仍处开发阶段，本轮不兼容旧授权数据，旧无签名授权文件不导入。Web 新增 `features/system/LicensePage.tsx`、主导航“授权”和 `/system/license` 路由，可查看授权状态、试用/剩余天数、到期日期、机器码和授权文件路径，并支持刷新、复制机器码和注册。
- 邮件发送基础闭环已从旧 WinForms 工具迁入 Tauri/Web/API 主线：API sidecar 新增 `GET /api/tools/email/status` 和 `POST /api/tools/email/send`，复用 `IEmailService` / `SmtpEmailService` 与程序根 `appsettings.json` SMTP 配置；Web 新增 `features/tools/EmailPage.tsx`、主导航“邮件”和 `/tools/email` 路由；Tauri 桌面桥新增 `select_email_attachment_files`。当前闭环支持 SMTP 状态、收件人/主题/正文、用户显式附件路径、附件选择、附件打开和发送。附件缺失会被 API 拒绝；发送过程不写数据库、不创建默认附件目录、不按发票号或付款参考号跨域取数。单据管理中“自动生成发票/箱单/合同等附件后再发邮件”的旧流程已通过发票报表面板的 `document-email` 后台任务接入基础闭环。
- 旧 WinForms 设置页“测试邮件连接”已接入 Tauri/Web/API 主线：API sidecar 新增 `POST /api/tools/email/test-connection`，只读取程序根 `appsettings.json` 中已保存的 SMTP 配置，并通过 `IEmailService.TestConnectionAsync` 向已保存发件人地址发送测试邮件。Web 设置页新增“测试邮件连接”按钮；由于设置响应会清理密码等敏感字段，存在未保存设置时会提示先保存，避免前端回传密码或用半保存状态测试。该闭环不写数据库、不读取附件目录、不创建默认路径，也不读取发票/报关或付款/报销业务表。
- 旧 WinForms 设置页“根据邮箱自动推断 SMTP 服务器”已接入 Tauri/Web/API 主线：API sidecar 新增 `POST /api/tools/email/server-suggestion`，复用跨平台 `MailServerHelper.GetServerConfig(email)`，对 QQ、263、Gmail、Outlook 和企业域名返回 SMTP host、端口和 SSL 建议；Web 设置页“邮件与备份”新增“推断 SMTP”按钮，读取发件人地址或邮箱账号，仅把建议填入当前设置草稿，不自动保存。该接口只在内存中返回建议，不保存 `appsettings.json`、不写数据库、不创建目录、不读取发票/报关或付款/报销业务表；smoke 已覆盖 `user@qq.com -> smtp.qq.com:465 SSL`。
- 发票/报关类单据与付款/报销单据按独立数据域处理：发票报表不再按发票号读取付款数据，付款/报销报表也不再按参考号反查发票、客户或报关草稿；`Payment.InvoiceNo` 仅作为付款单自身的业务参考文本，旧模板中的 `Invoice.InvoiceNo` 只保留不查库的兼容空壳别名。`ApiReportEndpointIntegrationTests` 已补入同业务号发票/付款并存场景，覆盖 Tauri/Web 实际调用的 HTML 预览端点边界。
- 后端内置模板真实渲染回归继续补强：`ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplates_ShouldRenderFromProgramRootWithoutCrossDomainLeakage` 会把程序自带 `invoice_template.html`、`packing_list_template.html`、`contract_template.html`、`customs_declaration_template.html`、`payment_voucher_template.html`、`expense_reimbursement_template.html` 复制到隔离测试程序根 `Templates/`，再通过 `ReportHtmlService` 用种子发票/付款数据渲染。测试断言模板路径来自程序根 `Templates/`，核心标题和业务号存在，Scriban 占位符已消解，并确认发票/箱单/合同/报关单不泄漏匹配付款数据、付款/报销模板不泄漏匹配发票和客户数据；该测试类临时目录已从系统 temp 改为仓库 `.codex-runtime/ExportDocManager.Infrastructure.Tests`。
- 后端内置模板真实业务数据 PDF 回归已补入并继续增强：`ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplatesToPdf_ShouldUseProgramRootBrowserAndRuntimeDataRoot` 以仓库 `ExportDocManager` 作为程序根，读取程序根 `Templates/` 六个内置模板和程序根 `Browsers/ChromeForTesting` 中的 Chrome Headless Shell，通过 `ReportPdfRenderService + ChromiumHtmlToPdfService` 将发票、箱单、合同、报关单、付款单和费用报销单的种子业务数据真实渲染为 PDF。测试同时读取模板源码确认 `@page` 口径：发票、装箱单、合同为 `A4 portrait`，报关单为 `A4 landscape`，并继续通过 PDF `MediaBox` 校验实际输出方向。新增 `RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf_ShouldPreservePaginationAndDomainIsolation`，构造同一 `InvoiceNo` 下的 `实际数据` 发票、`报关数据` 发票和付款参考号，发票/箱单用 25 条真实明细生成 A4 纵向多页 PDF（当前基线发票 4 页、箱单 5 页），报关单用 18 条报关明细生成 A4 横向 2 页 PDF，付款单保持独立 1 页；测试断言实际数据不混入报关数据，报关数据不混入实际数据，付款项目不反查发票/报关域。PDF 目标路径仍位于隔离测试数据根 `.codex-runtime/ExportDocManager.Infrastructure.Tests/.../RenderedPdfs`，渲染器来自程序根 `Browsers/`，模板来自程序根 `Templates/`。这把内置模板验证从 HTML 字符串推进到真实 Chromium PDF、多页业务数据和同号数据域隔离层；仍不替代完整 PDF 页面像素级基线。
- 发票类型中的“报关数据/实际数据”按同一数据域内的独立发票记录处理：数据库以 `InvoiceNo + Type` 做唯一约束，保存时同样按 `InvoiceNo + Type` 匹配既有记录；同一发票号允许同时存在报关口径和实际出货口径两条记录。单一窗口 COO/ACD 草稿、提交包、回执和协同跟踪主链路以 `invoiceId` / `SourceInvoiceId` 关联源发票，不只靠发票号反查源数据；查询页新增类型筛选，API 集成测试已覆盖同号双类型并存、按类型查询和更新实际数据不覆盖报关数据。Tauri/Web 发票列表已新增独立“类型”列，同号发票在列表入口即可区分 `实际数据` 与 `报关数据`，避免操作者只看到同一发票号和状态时误选口径。Tauri/Web 发票编辑页已新增“生成报关数据/生成实际数据”入口，调用 `POST /api/invoices/{id}/clone-type` 从当前已保存发票按同一发票号复制表头与明细并写入目标类型；API 会先校验目标类型只能为 `实际数据` / `报关数据`，并在 `InvoiceNo + TargetType` 已存在时返回 `409`，明确不覆盖既有另一口径。该入口只读当前发票 ID、只写发票/明细运行数据根数据库，不读取或更新付款/报销单据。仪表盘同号去重用于避免同一票重复统计，已明确优先采用“实际数据”，没有实际口径时再回退到“报关数据”，并补入同号双类型仪表盘集成测试。
- 旧 WinForms 发票“反审核”和锁定编辑护栏已迁入 Tauri/Web/API 发票编辑闭环：服务层新增 `UnverifyInvoiceAsync`，只按当前 `invoiceId` 读取当前发票记录和明细，校验状态属于已锁定口径后退回 `Draft` 并保存；普通保存路径在更新前读取持久化发票状态，`Verified` / `Shipped` / `Completed` / `Cancelled` 等锁定状态会返回冲突，必须先反审核回草稿后再编辑。API 新增 `POST /api/invoices/{id}/unverify`，鉴权、非法 ID、未找到、状态无需反审核和并发保存失败均返回明确响应；React 发票编辑页按已保存状态判断是否可编辑，锁定后基础信息、贸易方、运输条款、唛头、明细、信用证导入和保存按钮进入只读，仍保留报表/单一窗口等读取或生成入口；调用生成客户端 `unverifyInvoice` 后刷新当前发票、发票列表、查询页和仪表盘缓存。该闭环不按发票号查找另一条“实际数据/报关数据”，不读取或更新付款/报销单据，不写文件、不创建缓存目录，也不改变 SQLite 位于运行数据根的策略。
- 旧 WinForms `QueryForm` 的查询视图状态已继续迁入 Tauri/Web：查询页现在支持每页 `20/50/100/200` 条，页面大小进入查询缓存键和 `/api/query/invoices` 请求；日期、客户/出口商、发票类型、运输方式、关键字、合同号、款名、款号和每页条数保存到浏览器 `localStorage`，重新进入查询页会恢复上次条件。该视图状态只属于前端本地偏好，不写业务数据库、不创建文件、不改变显式 Excel 导出路径，不读取付款/报销域，也不按发票号合并同号 `实际数据` / `报关数据`。
- 旧 WinForms `QueryForm` 的键盘工作流已继续迁入 Tauri/Web：筛选区 `Enter` / `Shift+Enter` 按旧 `RegisterEnterAsTab(pnlQuery)` 前后流转，关键字框 `Enter` 同时执行查询，页面级 `Ctrl+F` 聚焦关键字，`F5` 刷新当前查询结果而不刷新整个 WebView；结果行仍可用 `Enter` 打开当前发票 ID。该闭环只改前端焦点和查询状态，查询/导出仍沿用既有 API 与用户显式 `.xlsx` 路径，不新增文件落点。最新 `tauri:smoke:reports:local` 已在 `queryKeyboardCheck` 中验证 Enter/Shift+Enter、Ctrl+F、F5、关键字查询、结果行打开和临时发票清理。
- 遗留 WinForms 参考层已同步作为兼容护栏收紧：`ReportDataAssembler` / `ReportTemplateGlobalBuilder` 不再按发票号或付款参考号互查对方业务表，避免后续迁移或模板兼容逻辑误复用旧关联假设；新桌面主线仍以 Tauri/Web/API 为准。
- Tauri 设置页运行诊断路径按钮已补强 smoke 覆盖：`scripts/smoke-web-runtime-diagnostics.mjs` 新增 `--runtime-path-actions-check`，在模拟 Tauri IPC 中记录 `open_path` 调用；`scripts/smoke-tauri-desktop.ps1 -VerifyWebDiagnostics` 会把 `/healthz` 返回的程序根、数据根、数据库目录、SQLite 文件、日志目录、模板目录、OCR 模型目录和单一窗口目录作为期望路径传入。`npm --prefix apps\export-doc-tauri run tauri:smoke:web:local` 最新验证已点击 `打开程序根`、`打开数据根`、`打开数据库目录`、`打开SQLite 文件`、`打开日志目录`、`打开模板目录`、`打开OCR 模型` 和 `打开单一窗口目录`，并断言每个按钮都向 `open_path` 传递对应既有路径。该检查只验证桌面桥入口和参数，不实际打开系统文件管理器，不创建新默认目录、不写数据库、不写系统 C 盘。
- Tauri 设置页用户与权限管理已补强真实 UI CRUD smoke 覆盖：`scripts/smoke-web-runtime-diagnostics.mjs` 新增并增强 `--user-management-crud-check`，在 `#/settings` 的“用户与权限”区域通过真实表单和按钮创建临时 `smoke-ui-*` 用户、校验表格行、把角色从 `Finance` 改为 `User`、校验表单值和表格行后删除该用户；随后使用同一登录 token 调 `/api/users` 复核服务端创建、更新和删除状态，并断言 API DTO 不暴露 `PasswordHash`。`scripts/smoke-tauri-desktop.ps1 -VerifyWebDiagnostics` 已默认传入该检查。最新 smoke 命中 `createdApiUser.exists=true`、`updatedApiUser.exists=true`、`deletedApiUser.exists=false`、`apiDtoPasswordHashHidden=true`。该 smoke 真实启动 Tauri debug 壳、API sidecar 和 Web preview，用户账号只写运行数据根 SQLite，结束后删除临时用户，诊断 JSON/截图仍写程序根 `logs/`，浏览器 profile 仍写运行数据根 `App_Data/Smoke/BrowserSmokeProfile-*`；不写 AppData/ProgramData 或系统 C 盘默认路径。
- Tauri 桌面烟测已增强为六个内置模板逐个闭环检查：`scripts/smoke-tauri-desktop.ps1 -VerifyWebReports` / `npm --prefix apps\export-doc-tauri run tauri:smoke:reports:local` 会真实启动 Tauri debug 壳、API sidecar 与 Web 生产构建 preview，登录后进入 `#/reports/templates`，检查报表模板页标题、“新版设计器/源码/预览/保存”、模板生命周期、模板包导入导出、下载上传、真实数据预览入口和字段目录；路径、状态、存储等审查期调试 readout 已不作为界面元素或 smoke 期望。随后对发票、箱单、合同、报关单、付款单和报销单逐个深链接切换，确认页面下拉选中目标模板、内容 active 到目标模板，并等待 `.new-report-designer` 与“字段目录”出现。截图和 JSON 写程序根 `logs/`，浏览器 profile 写运行数据根 `App_Data/Smoke/BrowserReportsSmokeProfile-*`。
- Tauri 报表 smoke 已新增发票编辑页和付款编辑页闭环检查：脚本会在 smoke 数据根数据库创建临时发票和临时付款单，分别打开 `#/invoices/{id}` 和 `#/payments/{id}`，检查报表预览面板、模板选择、`输出 PDF` 和 `生成 PDF` 控件，点击“预览”并等待 iframe HTML 包含临时业务编号和客户/收款人名称，再断言“打印”按钮在预览后启用；为避免系统打印对话框阻塞自动化，脚本不点击打印按钮，最后删除临时发票和临时付款单。
- Tauri 报表 smoke 已新增发票编辑页信用证导入真实入口检查，并在 `InvoiceLetterOfCreditPanel.tsx` 抽取后复跑通过：`--invoice-letter-of-credit-check` 会创建临时发票，在 `App_Data/Smoke/BrowserReportsSmokeProfile-*` 下写入临时 `.txt` 信用证源文件，打开 `#/invoices/{id}` 后填写“来源文件”并点击“导入信用证”，断言页面回填的信用证文本包含 smoke marker 与临时发票号，退出时删除临时发票和临时源文件；最新验证的 `invoiceLetterOfCreditCheck.deletedInvoice` 与 `invoiceLetterOfCreditCheck.deletedSourceFile` 均为 `true`，没有引入系统 C 盘、AppData、ProgramData 或业务默认导入目录。
- Tauri 报表 smoke 已新增任务中心真实入口检查并扩展装柜方案/柜型控件、自动刷新状态、平面可视化、三维可视化和三维交互断言：`--job-center-check` 会打开 React `#/jobs`，断言装箱分析、自动刷新开关、装柜分析状态栏、方案名称、已存方案、柜型、保存/加载/删除方案、保存柜型、Excel 导入与托单、批量报表 ZIP、PDF 合并任务和任务表均可见；`containerProjectCrudCheck` 通过真实页面按钮保存临时装柜方案、加载恢复方案、捕获删除确认文案、删除方案并断言下拉项移除；随后 `containerAutoRefreshCheck` 等待状态栏 `data-auto-refresh-state="complete"`、文本包含“自动刷新”和“货物:”，并确认结果摘要已出现。smoke 仍会点击“分析”验证手动路径，检查装柜平面可视化区域、俯视/侧视/柜门三个 SVG、货物块和颜色图例均已渲染，并等待 `装柜三维可视化` 的 Three.js canvas ready 后用离屏 canvas 采样像素，分别在桌面视口和移动视口确认三维场景非空。本轮 smoke 会真实点击“暂停三维自动旋转”、切换“柜门”视角和“重置三维视角”，断言 canvas 的 `data-auto-rotate` / `data-view-preset` 与按钮 `aria-pressed` 状态同步。本轮继续增强 `excelToolOutputJobsCheck` 并保留 `reportBatchZipJobCheck`、`pdfMergeJobCheck`：`excelToolOutputJobsCheck` 真实填写“模板输出”“空白托单”“转换源/转换输出”和“发票 ID/发票托单”，等待 `ExcelTemplateExport`、`BlankBookingSheetExport`、`BookingSheetConvert`、`InvoiceBookingSheetExport` 成功，校验 `.xlsx` 文件头 `PK`、输出文件清理、转换源清理和临时发票删除；`reportBatchZipJobCheck` 创建两张临时发票后真实填写“批量报表 ZIP”任务并点击“开始”，等待 `ReportPdfZip` 成功，校验 ZIP 头 `PK`、输出文件清理、运行缓存 `Cache/ReportBatchZip/{jobId}` 清理和临时发票删除；`pdfMergeJobCheck` 在 smoke profile 下生成两份临时源 PDF，真实填写“PDF 合并任务”并点击“开始”，等待 `PdfMerge` 成功，校验输出 PDF 头 `%PDF`、输出文件清理和源 PDF 清理。该检查的装柜方案 CRUD 只写并清理运行数据根 SQLite 装柜表；自动分析和可视化只使用内存请求/响应；Excel、批量 ZIP 和 PDF 合并只写 smoke profile 下显式临时文件与对应运行缓存，不读写付款/报销域或系统 C 盘/AppData/ProgramData 默认路径。
- Tauri 报表 smoke 已增强智能 OCR 真实入口与模型质量门检查：`--smart-ocr-check` 会打开 React `#/tools/ocr`，断言图片路径、图片预览、粘贴图片、放大、缩小、重置缩放、识别结果、行框表格和识别/复制按钮均可见；`--smart-ocr-real-sample-check` 在程序根 `OcrModels/PaddleOCR/V6` 模型完整且 Windows 自动运行时可用时，用浏览器 canvas 生成内存 PNG 发票样张，通过 `/api/tools/ocr/recognize-image-content` 调用真实 PaddleOCR 模型，并要求 `INVOICE`、`ABC123`、`TOTAL`、`USD`、`45678`、`ACME` 至少命中 3 个关键 token。最新 smoke 实际命中 6/6，识别文本为 `INVOICE NO ABC123`、`TOTAL USD 456.78`、`CUSTOMER ACME EXPORT`，耗时约 2.7 秒；样张只通过请求体内存传递，不创建临时图片文件、不写数据库、不新增系统盘落点，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- Tauri 报表 smoke 已扩展审计日志真实入口和 Excel 导出检查：`--audit-log-check` 会打开 React `#/audit-logs`，断言审计日志标题、筛选字段、表格、详情区域、管理员管理区、输出路径、保留天数、删除确认、清理确认以及导出/删除/清理按钮均可见；`--audit-log-export-check` 在 mock Tauri runtime 下创建/删除临时用户以生成审计记录，填写 smoke profile 下显式 `.xlsx` 路径，点击“导出 Excel”，确认文件存在且“打开导出文件”调用 `open_path` 的路径精确等于该导出文件，最后删除 smoke 导出的 `.xlsx`。UI smoke 仍不点击删除当前结果或清理历史日志这类破坏性动作，相关行为由 API 管理测试覆盖；浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- Tauri 报表 smoke 已新增授权注册真实入口检查：`--license-check` 会打开 React `#/system/license`，断言机器码、授权文件、试用天数、剩余天数、注册码、刷新授权状态、复制机器码和注册按钮均可见；该检查不提交注册码、不写业务数据、不读取发票/报关或付款/报销表，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- Tauri 报表 smoke 的软件更新检查已同步改为 Tauri updater 入口：`--update-check` 打开 React `#/system/update`，确认 Tauri updater 主区、更新状态和更新日志存在；`--update-stage-check` 在 mock Tauri runtime 下模拟 `check_tauri_update` 返回带 `notes` 的新版本，点击“检查更新”后断言页面显示该更新日志，并点击“下载并安装”确认调用 `install_tauri_update`。该检查不再创建旧 API `update.json`、不暂存包、不调用 `handoff_update_package`，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- Tauri 报表 smoke 已新增汇率工具真实入口检查：`--exchange-rate-check` 会打开 React `#/tools/exchange-rates`，断言汇率表、状态区、可用货币区以及读取可用货币、刷新汇率、强制刷新汇率三个按钮均可见；该检查不访问远程汇率源、不写数据库、不创建业务文件，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- Tauri 报表 smoke 已新增邮件发送真实入口检查：`--email-check` 会打开 React `#/tools/email`，断言 SMTP 状态区、收件人、主题、正文、附件路径、附件表格、刷新状态、选择附件和发送邮件按钮均可见；该检查不发送邮件、不读取真实附件、不写数据库、不创建业务文件，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- 汇率工具已从旧 WinForms “今日汇率”和设置页常用币种管理迁成 Tauri/Web/API 闭环：sidecar 新增 `/api/tools/exchange-rates` 与 `/api/tools/exchange-rates/available-currencies`，复用 `IExchangeRateService` / `BocExchangeRateService` 和程序根 `appsettings.json` 中的源地址、缓存分钟、货币列表；Web 新增“汇率”导航页并在设置页补回汇率配置。本轮设置页继续恢复旧 `SettingsOperationsViewModel.Currencies` 行为，支持从远程源更新候选货币、候选/常用双列表、添加/移除、双击移动、上移/下移和最多 15 种常用货币限制；刷新候选只写当前设置草稿，保存仍通过既有 `/api/settings` 写程序根 `appsettings.json`，不新增数据库或目录。汇率结果只留在服务内存缓存和页面状态，不写默认输出目录、不新增数据库表，不与发票/报关单据或付款/报销单据数据域合并。

- 利润分析已从旧 WinForms 弹窗迁成发票编辑页内的 Tauri/Web/API 闭环：sidecar 新增 `POST /api/invoices/profit-analysis`，复用应用层 `IInvoiceProfitAnalysisService` 按旧 `ProfitAnalysisViewModel` 口径计算销售总额、汇率、销售额 RMB、采购成本、退税收入、预估毛利和毛利率；Web 发票编辑页新增 `InvoiceProfitAnalysisPanel`，使用当前发票草稿点击计算，支持未保存草稿的即时分析。该接口只处理请求体中的发票/发票草稿字段，内存计算后响应结果；不读取付款/报销单据，不写数据库、不落文件、不创建默认输出目录，也不把发票/报关类单据与付款/报销单据按编号隐式关联。
- 仪表盘已从旧 WinForms 首页迁成 Tauri/Web/API 闭环：sidecar 新增 `GET /api/dashboard`，复用应用层 `IDashboardService` 按旧 `DashboardViewModel` 口径汇总本月出口额、预估利润、退税额、订单状态数量、最新订单、待办事项和单一窗口批次近况；Web 新增 `DashboardPage` 并把登录默认入口改为 `#/dashboard`。该接口只读当前用户可见范围内的发票/报关类单据和单一窗口提交批次，不读取付款/报销单据，不写数据库、不落文件、不创建默认输出目录，也不把发票/报关类单据与付款/报销单据按编号隐式关联。
- Tauri 主窗口启动入口已同步改为仪表盘：debug 使用 `http://127.0.0.1:5173/#/dashboard`，release 使用 `index.html#/dashboard`，不再打开 `#/invoices`。本轮同时补齐 Tauri v2 IPC 授权：`src-tauri/permissions/desktop-bridge.toml` 定义 `allow-desktop-bridge` app permission 并显式放行 `get_desktop_runtime_context`、文件选择、保存路径、`open_path` 和更新交接等命令，`src-tauri/capabilities/main.json` 再把该权限授给 `main` 窗口并允许本机 dev URL，解决截图中 `get_desktop_runtime_context not allowed. Plugin not found` 的命令授权报错；前端仅把这类旧权限探测失败降级为 console warning，避免把内部插件/权限错误作为红字登录错误暴露给操作者。
- 本轮已重新编译 debug 桌面壳并用真实 WebView2 远程调试口复验：窗口实际打开 `http://127.0.0.1:5173/#/dashboard`，标题和左侧高亮均为“仪表盘”，在页面内直接执行 `window.__TAURI__.core.invoke('get_desktop_runtime_context')` 可返回 API 地址与 64 字符桌面令牌，页面文本未再出现 `Plugin not found` / `not allowed`。该探针数据根为 `D:\Rust\cargo-target-exportdoc\debug\App_Data\WebViewProbe`，仍跟随非系统盘运行目录。
- 当前构建目录口径已于 2026-07-10 收口：Tauri 主程序 Cargo debug/release 中间产物默认写仓库 `artifacts/cargo-target-exportdoc/`，离线注册机和 Rust Excel 分析器分别写 `artifacts/cargo-target-license-keygen/`、`artifacts/cargo-target-excel-analyzer/`；最终客户运行目录仍由整理脚本输出到 `artifacts/windows-desktop-run/ExportDocManager/`，内部注册机输出到同级 `KEY/`。脚本仍允许通过参数或 `CARGO_TARGET_DIR` 显式覆盖，但不再因检测到 `D:\Rust` 自动创建外部重复 target；旧 `D:\Rust\cargo-target-exportdoc` 已在新路径冷编译和真实 smoke 通过后清理，Rust 工具链自身仍保留在 `D:\Rust\.cargo` / `.rustup`。
- Tauri 报表 smoke 已新增仪表盘真实入口检查：`--dashboard-check` 会打开 React `#/dashboard`，断言本月出口额、本月预估利润、本月退税额、待处理订单、已出运、总订单量、最新订单、待办事项和刷新按钮均可见；该检查只读当前 smoke 数据根数据库和单一窗口批次摘要，不创建业务文件，浏览器 profile 仍位于运行数据根 `App_Data/Smoke`。
- 备份/恢复管理已从旧 WinForms “数据还原”能力迁成 Tauri/Web/API 基础闭环：sidecar 新增 `/api/backup`、`/api/backup/cleanup` 和 `/api/backup/restore`，复用 `IBackupService` / `BackupService`，Web 设置页新增“数据备份与还原”面板，可查看运行数据根 `Backups/`、创建 SQLite 备份、按保留天数清理旧备份，并通过备份列表文件名和 `RESTORE` 确认文本执行还原。还原端点只接受当前备份列表中的文件名，不接受任意路径；该闭环不读取发票/报关或付款/报销业务表，不生成导出目录，不写系统盘默认用户目录或全局程序数据目录。
- 备份/恢复管理本轮补强还原后重启复核：新增 API 集成测试在隔离运行数据根中创建备份、写入临时用户、执行 `/api/backup/restore`，随后使用同一 `AppRoot/DataRoot` 重启 sidecar 并确认临时用户已消失、内置 `admin` 与备份列表仍可读取。测试 harness 的 AppRoot/DataRoot 默认从系统临时目录改为工作区 `.codex-runtime/api-tests`，也可通过 `EXPORTDOCMANAGER_TEST_ROOT` 显式指定，避免 API 集成测试默认把 SQLite 根落到系统 C 盘临时目录。本轮继续新增专用 Tauri 图形还原 smoke：`scripts/smoke-web-runtime-diagnostics.mjs --backup-restore-check` 会在设置页真实点击“创建备份”、通过 API 创建临时用户、在页面选择该备份并输入 `RESTORE` 后点击“还原数据库”，等待页面返回“请重启桌面程序后继续使用”；`scripts/smoke-tauri-desktop.ps1 -VerifyBackupRestore` / `npm --prefix apps\export-doc-tauri run tauri:smoke:backup-restore:local` 随后停止当前 Tauri/API，用同一 `--app-root` / `--data-root` 重启 sidecar，确认临时用户已被快照移除、`admin` 与备份列表可读取，并删除本次 smoke 创建的备份 ZIP。
- WebDAV 云备份已从旧 WinForms 关机维护和设置页测试入口迁入 Tauri/Web/API，并补齐云端备份取回：sidecar 新增 `/api/backup/cloud/status`、`/api/backup/cloud/test-connection`、`/api/backup/cloud/upload-latest`、`/api/backup/cloud/backups` 和 `/api/backup/cloud/download`，复用 `ICloudSyncService` / `WebDavCloudSyncService`。设置页“邮件与备份”可保存 WebDAV 配置并测试已保存连接；“数据备份与还原”面板显示云备份状态和最新本地备份，可把运行数据根 `Backups/` 中当前数据库对应的最新 ZIP 上传到 WebDAV，也可列出远端 `.zip` 备份并把选中单文件名下载回运行数据根 `Backups/`。云上传不接受任意本地文件路径，云下载不接受任意远端路径或本地目标路径，下载后仍只进入本地备份列表，真正还原继续由既有 `/api/backup/restore` 的文件名白名单和 `RESTORE` 确认文本触发。该闭环不从发票/报关或付款/报销业务表取数，不按编号跨域关联；远端上传文件名沿用本地备份 ZIP 名，远端下载文件名必须来自 WebDAV 列表。验证：API 定向测试通过本地假 WebDAV 服务器真实覆盖 PUT 上传、`PROPFIND` 列表和 GET 下载，OpenAPI/生成客户端/Web typecheck/build 和路径扫描均通过。
- 旧 WinForms 主窗口退出维护已迁入 Tauri/API 生命周期：应用层新增 `IShutdownMaintenanceService` 端口，Infrastructure 实现复用 `ISettingsService`、`IBackupService`、`ICloudSyncService`、`IAuditLogService` 和 `IAppPathProvider`，按系统设置执行备份保留、本地 SQLite 备份、WebDAV 最新备份上传、审计日志保留清理和程序根文本日志清理。sidecar 新增 desktop-token-only 的 `POST /api/system/shutdown-maintenance`，该端点免网页登录 bearer 但显式要求 `X-ExportDocManager-Desktop-Token`；Tauri 主窗口关闭时先短超时调用该端点，再停止 sidecar，维护失败也不阻断退出。该生命周期动作只触达运行数据根 `Backups/`、运行数据根数据库 `AuditLogs` 表、程序根 `logs/` 和已保存 WebDAV 远端，不读取发票/报关或付款/报销业务表，不按同号发票或付款参考号跨域合并。验证：维护服务单元测试、API desktop-token 集成测试、OpenAPI 断言、认证中间件测试、Tauri 本地 `cargo-check` 和生产路径扫描通过。
- 旧 WinForms 主窗口 `关于...` 已迁入 Tauri/Web 系统信息页：React `AboutPage.tsx`、主导航“关于”和 `#/system/about` 路由只读取 `/healthz` 展示产品版本、绿色便携形态、Tauri/Web 环境、API/数据库状态和程序根、数据根、数据库、模板、OCR、单一窗口、日志等运行目录。旧 `/api/system/update` 元数据依赖已移除；该页只读运行元数据，不新增 API、不写数据库或文件，不读取发票/报关或付款/报销业务域。
- 旧 WinForms 设置页“立即清理旧日志”已迁入 Tauri/Web/API：Application 新增 `ISystemLogCleanupService`，Infrastructure 新增 `SystemLogCleanupService`，API 新增管理员端点 `POST /api/system/logs/cleanup`，Web 设置页“系统与数据库”区新增同名按钮。按钮在存在设置草稿时先保存当前设置，再按已保存的审计日志保留天数、文本日志保留天数和文本日志保留文件数执行清理；审计日志只操作运行数据根数据库 `AuditLogs` 表，文本日志只操作程序根 `logs/*.txt`，端点不接收任意路径、不生成默认导出目录、不读取发票/报关或付款/报销数据域。验证：Infrastructure/API 定向测试、OpenAPI client 生成、Web typecheck/build、Tauri check 和生产路径扫描通过。
- 旧 WinForms 设置页“系统设置恢复默认”已迁入 Tauri/Web 设置页：`#/settings` 的“系统与数据库”工具区新增“恢复默认”按钮，点击确认后只恢复当前页面草稿中的 `SystemSettings` 默认值，并同步清空旧端同一动作会清空的单一窗口 COO 默认申报资料；用户仍需点击“保存”才会通过既有 `/api/settings` 写程序根 `appsettings.json`。该动作不新增后端端点、不写数据库、不创建文件目录；受保护的密码/密钥字段仍由“更新敏感字段”开关控制，不绕过既有敏感字段保存策略。验证：Web build、Tauri check 和生产路径扫描通过。
- 旧 WinForms `ValidationResultForm` / `ValidationResultViewModel` 的设置校验结果和自动修复入口已迁入 Tauri/Web/API：sidecar 新增 `POST /api/settings/validate`，只校验请求体中的当前设置草稿，返回错误/警告、可自动修复标记和脱敏后的规范化设置；React 设置页“系统与数据库”工具区新增“校验设置”，结果面板提供“应用自动修复”。自动修复只回填页面草稿，用户仍需点击“保存”才会写程序根 `appsettings.json`；校验过程不保存配置、不写数据库、不创建目录、不测试外部连接、不读取发票/报关、付款/报销或单一窗口业务域。验证：`ApiSettingsTests` 定向测试、OpenAPI 暴露测试、生成客户端、Web typecheck/build 和路径扫描通过。
- Tauri 设置页 smoke 已补强备份创建真实闭环：`--backup-check` 会打开 React `#/settings`，等待备份目录显示运行数据根 `Backups`，并断言创建备份、清理旧备份、还原数据库、确认文本和备份表格均可见；`--backup-create-check` 在 `scripts/smoke-tauri-desktop.ps1 -VerifyWebDiagnostics` 默认启用后会真实点击“创建备份”，确认新 `.zip` 行和物理文件均位于 `App_Data/Smoke/Backups`，再只删除该 smoke 新建备份文件。普通诊断 smoke 仍不点击“还原数据库”，破坏性还原动作由专用 `-VerifyBackupRestore` smoke 覆盖；浏览器 profile 仍位于运行数据根 `App_Data/Smoke`，诊断 JSON 和截图仍写程序根 `logs/`。
- 发票页面整体迁入 `features/invoices`、发票列表/编辑/保存模型继续拆分、付款/报销表单面板和付款列表/编辑/保存模型继续拆分、任务中心装箱分析/装柜项目与柜型管理/Excel 工具面板拆分、装柜自动刷新状态闭环、装柜分析基础平面/三维可视化和三维交互控件、智能 OCR 路径/内存识别与剪贴板/缩放/拖拽/框叠加闭环、审计日志管理闭环、汇率工具闭环、仪表盘闭环和备份/恢复管理闭环接入后已复跑 `npm --prefix apps\export-doc-web run generate:api`、`npm --prefix apps\export-doc-web run typecheck:api`、`npm --prefix apps\export-doc-web run build`、`node --check scripts\smoke-web-runtime-diagnostics.mjs`、路径扫描、API 工具/架构定向测试、OCR OpenAPI 定向测试、装柜项目/柜型 API 定向测试、审计日志管理 API 定向测试、备份 API 定向测试、付款/发票数据域独立 API 定向测试、`npm --prefix apps\export-doc-tauri run tauri:check:local`、`npm --prefix apps\export-doc-tauri run tauri:compile:local`、`npm --prefix apps\export-doc-tauri run tauri:smoke:web:local`、`npm --prefix apps\export-doc-tauri run tauri:smoke:reports:local` 和 `npm --prefix apps\export-doc-tauri run tauri:smoke:backup-restore:local`；Tauri/Web/API 报表、信用证、付款预览、任务中心、OCR、汇率、仪表盘、备份管理、审计管理、装柜项目基础管理、装柜自动刷新、装柜平面/三维可视化和三维交互 smoke/定向测试仍通过，SQLite、备份目录、审计日志数据库记录、装柜项目/柜型数据库记录与浏览器 profile 继续位于 `App_Data/Smoke` 或用户显式路径，不新增系统 C 盘或 AppData/ProgramData 默认落点。
- 由于当前工作区路径包含 `C#`，Vite dev 模式会触发 `#` 路径转换问题；报表 smoke 已改为先执行 Web build 再用 `vite preview` 承载生产构建，贴近 Tauri 实际加载产物并避免旧 5173 端口残留污染。脚本启动前会清理本仓库残留的 5173 Vite 进程，退出时清理本次启动的子进程。
- 客户自定义旧 HTML 模板渲染回归已保留三条专项样本：`tests/ReportTemplateFixtures/customer_custom_invoice_legacy.html` 覆盖客户常见出口发票模板里的 `<br>` 多行字段、`thead` 多行重复表头、`rowspan`/`colspan` 合并单元格、百分比列宽和 `display: table-header-group` 分页提示；`tests/ReportTemplateFixtures/customer_custom_payment_voucher_legacy.html` 覆盖客户付款单模板里的 `Payment.*` 字段、多行收款信息、横向页面和付款域字段；`tests/ReportTemplateFixtures/customer_custom_compound_selector_invoice_legacy.html` 覆盖 `table.invoice-grid th/td`、`thead th`、`td.mark-cell`、`.invoice-grid .description`、`tbody td.amount` 和 `tbody tr.summary td` 等客户旧模板常见复合/后代 CSS 选择器。三条 fixture 现在进入视觉、打印媒体、PDF 结构和 PDF 像素回归，验证现存旧 HTML 模板在当前 Chromium 渲染链路中不空白、不明显塌版，并保持发票样本不混入 `Payment.*`、付款样本不混入 `Invoice.*` 的检查口径；新版可视化编辑不再依赖旧模板恢复状态。
- 客户旧模板与内置模板基础像素指标视觉烟测已补入并扩展：`scripts/test_report_template_visual_regression.mjs` 和 `npm --prefix apps/export-doc-web run test:template-visual` 复用程序根 `Browsers/ChromeForTesting` 中的 Chrome Headless Shell，按纵向/横向固定视口渲染三条客户旧 HTML fixture，并新增六个程序内置模板静态 HTML 布局截图，覆盖发票、箱单、合同、报关单、付款单和费用报销单。截图写工作区 `.codex-runtime/report-template-visual-regression/screenshots`，再解析 PNG 像素并断言尺寸、非白像素、暗色文字/边框、内容边界、颜色桶数量，以及客户旧模板关键颜色和内置模板 `#000000`、`#f2f2f2`、`#474747` 等关键文字/表格/边框颜色样本。这让模板恢复和内置模板审查从 JSON state/DOM 布局、后端 HTML 渲染推进到真实浏览器截图像素层；内置模板截图直接读取模板源，重点防止随程序 Chrome 下空白或主体版面塌陷，真实业务数据渲染和数据域隔离仍由后端渲染测试覆盖。当前仍是基础指标烟测，不是完整高保真打印/PDF 像素级基线；更多客户真实样本、多页业务数据、PDF 页面像素对比和跨平台字体差异仍待后续补齐。
- 客户旧模板与内置模板打印媒体像素回归已补入并增强到第一版布局指纹基线：`scripts/test_report_template_print_pixel_regression.mjs` 和 `npm --prefix apps/export-doc-web run test:template-print-pixels` 通过 Chrome DevTools Protocol 复用程序根 `Browsers/ChromeForTesting` 中的 Chrome Headless Shell，固定视口后调用 `Emulation.setEmulatedMedia({ media: "print" })` 再截图解析 PNG。本轮在视口截图和 `.full.png` 整页截图基础上新增 `tests/ReportTemplateFixtures/report_template_print_pixel_baselines.json`，对三条客户旧 HTML fixture、六个程序内置模板静态源、一条 `synthetic-print-media-sentinel` 和一条 `synthetic-print-full-page-sentinel` 共 `11` 个样本保存 viewport/full-page 两套 `32x32` 非白像素布局指纹、截图尺寸和内容边界；每次运行还会写出 `.codex-runtime/report-template-print-pixel-regression/print-pixel-baseline.actual.json` 供审查，并按哈明距离、内容边界和整页尺寸容差比对仓库基线。两个合成样本分别用屏幕/打印不同颜色断言测试确实进入 print media，以及断言整页截图达到 `640x1440` 并命中三段打印色块；其余模板继续断言非白像素、暗色文字/边框、内容边界、颜色桶数量和关键色命中。整页指标此前反向发现内置箱单和费用报销模板存在视口外横向内容宽度，已在模板 CSS 中增加 `box-sizing`、固定表格布局、长文本断词/换行，并把费用报销签字行改为可换行间距，修复后两者整页截图宽度均回到 `900x1270`；脚本同步把整页截图宽度固化为不超过视口 `1.02` 倍的回归门，防止后续模板修改重新引入横向溢出。截图、Chrome profile、合成样本、实际基线和汇总 JSON 写工作区 `.codex-runtime/report-template-print-pixel-regression`，不写业务数据库、不改变业务字段、不使用系统 C 盘临时目录；验证：`node --check scripts/test_report_template_print_pixel_regression.mjs`、`npm --prefix apps/export-doc-web run test:template-print-pixels`、`npm --prefix apps/export-doc-web run test:template-pdf-pixels`、`npm --prefix apps/export-doc-web run test:template-pdf`、Web typecheck/build、Tauri check 和路径扫描通过，当前汇总为 `11` 个样本，退出后无 `chrome-headless-shell` 残留。该项把模板验证推进到打印媒体 CSS、整页像素和可审查布局基线层，但仍不是原图级逐像素黄金基线，真实业务数据渲染和数据域隔离仍由后端渲染回归覆盖。
- 客户旧模板与内置模板 PDF 输出结构与内容流烟测已补入并继续增强：`scripts/test_report_template_pdf_regression.mjs` 和 `npm --prefix apps/export-doc-web run test:template-pdf` 复用程序根 `Browsers/ChromeForTesting` 中的 Chrome Headless Shell，将三条客户旧 HTML fixture、六个程序内置模板静态 HTML 源、一条结构化多表分页样本和一条合成分页样本打印为 PDF，PDF、Chrome profile、合成样本和汇总 JSON 写工作区 `.codex-runtime/report-template-pdf-regression`。脚本不再依赖旧可视化恢复模块或旧设计器 renderer，只验证现存 HTML 模板和结构化分页样本在 Chrome 打印链路中的 PDF 结构、页数、方向、内容流、文本操作符和绘制操作符。全部 `11` 个样本 `failedInflateCount=0` 并达到阈值；真实业务数据渲染和数据域隔离仍由后端渲染测试覆盖。
- 客户旧模板与内置模板 PDF 页面栅格像素回归已补入并扩展到多页，且继续纳入第一版布局黄金指纹基线：`scripts/test_report_template_pdf_pixel_regression.mjs` 和 `npm --prefix apps/export-doc-web run test:template-pdf-pixels` 先复用程序根 Chrome Headless Shell 将三条客户旧 HTML fixture、六个内置模板静态 HTML 源、结构化多表分页样本和合成分页样本打印为 PDF，再用程序根完整 Chrome 的 headless DevTools 打开 PDF viewer 并逐页截取页面 PNG。脚本解析 PNG 后定位最大白色纸张区域，断言截图尺寸、纸张区域比例/宽高、纸张内暗色文字/线条像素、非白像素、颜色桶和墨迹边界，防止 PDF 已生成但 viewer 中空白、未加载、纸张区域异常或主体内容丢失；当前汇总为 `18` 张 PDF 页面截图。
- 内置出口模板纸张方向已加入源码级回归护栏：基础视觉、打印媒体像素、PDF 结构和 PDF viewer 像素四条脚本都对 `invoice_template.html`、`packing_list_template.html`、`contract_template.html`、`customs_declaration_template.html` 新增 `expectedTemplatePageOrientation`，渲染前直接读取程序根模板源码并匹配 `@page { size: A4 ... }`。当前固定发票、装箱单、合同为 A4 纵向，报关单为 A4 横向；PDF 结构与后端真实渲染测试继续通过 `MediaBox` 复核实际输出方向。该护栏只读模板源码，测试产物仍写 `.codex-runtime/report-template-*`，不写业务数据库、不修改程序根模板、不新增系统盘默认落点；验证覆盖 `test:template-visual`、`test:template-print-pixels`、`test:template-pdf` 和 `test:template-pdf-pixels`。
- 旧 HTML 分页符渲染与新版导出已补入并继续增强：旧客户 HTML fixture 中的 `page-break-before` / `break-before` / `page-break-after` / `break-after`、分页类名和多表分页结构继续通过视觉、打印和 PDF 回归覆盖，确保现存模板在 Chrome 渲染链路中保留分页意图。新版设计器的分页块由 `features/report-designer/reportDesignerHtmlExporter.ts` 导出为 `report-page-break-row`，保留 `page-break-before: always` 与 `break-before: page`；新版主线不再把旧 HTML 反向恢复为旧表格画布。该项只在模板 HTML、前端 schema 和测试 fixture 中生效，不新增文件目录或数据库写入。
- 仍待补齐：原图级逐页逐像素 PDF/打印黄金基线、更多客户真实样本覆盖、真实业务多页数据/字体差异验证，以及复杂旧 HTML/分页脚本到设计器状态的精准反向还原。

不建议第一阶段采用纯 Rust 设计器库:

- Rust 生态里适合“可视化拖拽/表格化单据模板设计”的成熟免费库不足。
- `printpdf`、`genpdf` 等更适合代码生成 PDF，不负责用户可视化设计。
- `Typst` 很适合确定性排版，但模板 DSL 与现有 HTML/Scriban 模板体系不兼容，迁移成本高。
- 本项目已有可复用的 HTML/JS 设计器资产，Tauri 前端本身就是 Web 容器，先迁移这条路风险最低。

验收标准:

- 原有模板文件可打开、编辑、保存。
- 发票、箱单、合同、报关单、付款单、报销单的程序内置模板至少各有一个样例通过后端真实渲染测试。
- 设计器生成 HTML 后，Scriban 渲染结果与旧版关键字段一致。

## 9. OCR 和信用证导入重构

### 9.1 当前状态

当前 OCR:

- `Microsoft.ML.OnnxRuntime`
- `PP-OCRv6 Small ONNX`
- `OpenCvSharp4`
- `OpenCvSharp4.runtime.win`
- 全局静态 OCR 引擎 + lock，稳定优先，吞吐串行

模型大小:

- det: 约 9.4MB
- rec: 约 20.2MB
- 总计约 29.7MB

### 9.2 跨平台改造

第一阶段:

- 抽 `IOcrEngineFactory` 和 `IOcrRuntimeDiagnostics`。
- 把模型目录从 `AppDomain.CurrentDomain.BaseDirectory` 改为 `IResourcePathProvider`。
- Windows 继续使用当前 runtime。
- Docker/Linux 单独验证 ONNX Runtime + OpenCV native 依赖。

第二阶段:

- 允许 OCR 作为可选功能包。
- Docker 镜像分 `api` 和 `api-ocr`。
- 桌面轻量版首次 OCR 时下载模型包。

验收标准:

- 缺模型时返回明确错误。
- OCR 初始化错误不会拖垮整个 API。
- OCR 长任务支持取消。
- OCR 并发策略明确: 单 worker 或有限 worker 池。

## 10. 单一窗口模块重构

### 10.1 保留方向

单一窗口模块已经是独立子系统，建议作为 API 化优先模块之一。保留:

- COO/ACD 文档持久化
- 字段映射
- XML 生成
- XSD 校验
- 交接包 `.swpkg`
- 回执导入
- 状态回写
- 操作中心
- 协同基础

### 10.2 API 化注意点

当前桌面场景中，一些路径是机器级状态:

- 单一窗口客户端目录
- 本机工作站登记
- 导出包保存目录
- 回执导入目录

多端后应区分:

| 场景 | 处理 |
|---|---|
| Tauri 桌面离线 | 可继续保存本机路径，Tauri 选择目录 |
| Docker 团队版 | 路径应是服务端 volume 内路径 |
| Web 版 | 用户上传/下载文件，不能直接访问本机目录 |

Tauri 离线版的内置单一窗口客户端交换根应默认落在运行数据根 `SingleWindow/Client/{Cooimp|Acd|Others}`；外部客户端如果要求 `C:\ImpPath` 等特殊目录，必须由用户显式保存为客户端目录档案或请求参数，不再作为程序默认值。

### 10.3 推荐端点

```text
GET  /api/single-window/operation-center
GET  /api/single-window/coo/{invoiceId}
PUT  /api/single-window/coo/{invoiceId}
POST /api/single-window/coo/{invoiceId}/build-defaults
POST /api/single-window/coo/{invoiceId}/export-review
POST /api/single-window/coo/{invoiceId}/submit-package

GET  /api/single-window/acd/{invoiceId}
PUT  /api/single-window/acd/{invoiceId}
POST /api/single-window/acd/{invoiceId}/submit-package

POST /api/single-window/packages/import
POST /api/single-window/receipts/import
GET    /api/single-window/reference-catalog
PUT    /api/single-window/reference-catalog
DELETE /api/single-window/reference-catalog
```

验收标准:

- 旧 WinForms 与新 API 对同一发票生成的 COO XML 关键节点一致。
- `.swpkg` 包结构保持兼容。
- 回执导入后状态回写一致。
- 权限过滤与发票归属一致。

## 11. 数据库与部署模式

### 11.1 运行目录优先策略

本项目后续应默认采用“运行目录优先”的数据布局，尽量延续当前便携程序的使用习惯。用户把整个目录复制到 U 盘、移动硬盘、另一台电脑或指定业务盘时，数据库、模板、配置和导出文件应尽可能跟随迁移。

默认规则:

- 桌面 portable 模式: 数据库、缓存、备份、交接包等业务可写数据默认放到程序目录下 `App_Data`；`Templates/`、`OcrModels/`、`logs/` 保持程序根目录布局。
- 桌面 installed 模式: 业务数据也默认跟随安装目录的 `App_Data`，安装器必须提供数据目录选择；推荐用户选择非系统盘业务目录；模板、OCR 模型和日志继续使用运行目录下的固定目录。
- Docker 模式: 容器内业务数据使用 `/app/App_Data`，模板、OCR 模型、日志使用 `/app/Templates`、`/app/OcrModels`、`/app/logs`，宿主机通过 volume 映射到项目目录或服务器数据盘。
- Web/SaaS 模式: 不使用客户端本地路径，所有持久化数据走服务端数据库和服务端存储。
- 除非用户明确选择，不主动写入 `C:\ProgramData`、`C:\Users\<User>\AppData`、系统临时目录或注册表；授权防重置锚点可使用注册表/Keychain/Secret Service 保存小型签名元数据，不作为业务数据位置。

启动时必须校验:

- `App_Data` 是否存在，不存在则创建。
- 数据目录是否可写。
- 数据库文件是否可打开。
- 模板、导出、日志、缓存、OCR 模型目录是否可访问。
- 如果程序目录不可写，必须给出明确错误或引导用户选择数据目录，推荐非系统盘，不能静默切到 C 盘。
- 如果数据目录在系统 C 盘，应在安装器或设置界面提示预计占用空间，尤其是 OCR 模型、日志、导出文件、数据库备份。

安装版推荐路径:

```text
推荐:
D:\ExportDocManager\
  App_Data\
  Templates\
  OcrModels\
  logs\

或:
E:\业务软件\ExportDocManager\
  App_Data\
  Templates\
  OcrModels\
  logs\

不推荐作为默认:
C:\ProgramData\ExportDocManager\
C:\Users\<User>\AppData\Local\ExportDocManager\
```

### 11.2 SQLite 桌面离线版

保留:

- SQLite/SQLCipher
- 本机 `App_Data`
- 本机设置文件
- 本机模板和章图

改造:

- 默认数据库路径为 `App_Data/Database/exportdoc.db`。
- SQLCipher 密钥、本机配置和授权兼容文件默认放在运行数据根 `Security` 与 `Config`；不再保留 Windows 默认双写系统盘授权策略。
- 模板路径继续使用程序根目录 `Templates`，章图和附件进入 `App_Data/Files`。
- 导出文件默认进入 `App_Data/Exports`，用户也可以在设置中选择其他业务目录。
- OCR 模型继续随程序目录 `OcrModels` 发布。
- 正常运行日志默认进入运行数据根 `Logs`，便于随业务数据根统一备份、清理和打包问题现场；仅运行数据根尚未成功解析时的 Tauri 启动失败日志保留在程序根 `logs` 作为诊断例外。
- 缓存和临时文件默认进入 `App_Data/Cache`，启动时可做过期清理。
- 所有路径通过 `IAppPathProvider` 获取，不允许散落硬编码。

推荐配置:

```json
{
  "Storage": {
    "Mode": "Portable",
    "DataRoot": "App_Data"
  },
  "Database": {
    "Provider": "SQLite",
    "ConnectionString": "Data Source=App_Data/Database/exportdoc.db"
  }
}
```

### 11.3 PostgreSQL 团队/网页版

保留:

- 当前 PostgreSQL provider
- 发票/付款 `OwnerUserId / DepartmentId / CompanyScope`
- Admin 管理用户

补齐:

- 请求级用户上下文
- 部门/公司范围授权
- 归属改派
- 批量补属
- PostgreSQL 备份/还原
- 服务端迁移脚本

部署要求:

- Docker Compose 默认把业务数据映射到 `/app/App_Data`，并把模板、OCR 模型和日志分别映射到 `/app/Templates`、`/app/OcrModels`、`/app/logs`，便于备份和迁移。
- 上传文件、交接包、导出包、备份等业务数据仍分别映射到 `App_Data` 下的独立子目录。
- 数据库备份脚本输出到 `./data/backups`。
- Web 前端不能直接传绝对路径给后端，只能上传文件或选择服务端已授权目录。

### 11.4 EF Core 迁移策略

当前文档说明项目尚未正式投产，旧库迁移胶水不是最高优先级。但进入 Docker/Web 后必须有正式 schema 版本。

建议:

- 新增 EF Core migrations。
- API 启动时只做可控初始化，不静默破坏数据。
- 提供命令:

```text
dotnet ef migrations add ...
dotnet ef database update
ExportDocManager.Api --migrate
ExportDocManager.Api --seed-admin
```

验收标准:

- 空 PostgreSQL 可初始化。
- 空 SQLite 可初始化。
- 已有测试库可重复启动不重复 seed。
- 数据库 provider 配置错误时 fail fast，不回退到 SQLite。

### 11.5 路径与存储抽象验收标准

路径和存储抽象必须作为第一阶段基础能力完成，否则后续 Tauri、Docker、Web 会反复返工。

验收标准:

- API、Tauri、旧 WinForms adapter 都通过同一个 `IAppPathProvider` 获取目录。
- 业务服务只处理逻辑文件名、文件 ID 或相对业务路径。
- 任意用户输入路径必须经过目录穿越检查，禁止 `../`、绝对路径覆盖和跨目录写入。
- portable 包复制到另一个非系统盘目录后，数据库、模板、日志、导出功能仍可运行。
- Docker volume 中的目录结构与桌面 `App_Data` 保持一致。

## 12. 授权、密钥和安全

### 12.1 授权

当前 `LicenseManager` 依赖:

- Windows Registry
- WMI
- CommonApplicationData/LocalApplicationData

跨平台建议:

```csharp
public interface IMachineIdentityProvider
{
    string GetMachineId();
}
```

实现:

- Windows: Registry/WMI/机器名组合
- macOS: Keychain + IOPlatformUUID 或安装生成 ID
- Linux 桌面: `/etc/machine-id` 或安装生成 ID
- Docker: 不使用机器码，改为服务端 license 或配置 license key

便携模式调整:

- 现有 Windows 版兼容授权模块已改为运行数据根存储：旧 `LicenseManager` 使用 `Security/license.dat` 与 `Security/license.backup.dat`，不再默认双写系统盘用户/程序数据目录。
- Tauri/Web/API 主线已实现运行数据根授权闭环：`ILicenseService` / `RuntimeLicenseService` 使用 `Security/license.dat` 保存授权文件，使用 `Security/machine-id.seed` 保存机器码 seed，并复用共享授权码编解码。
- 如果需要更强防拷贝授权，可以在 installed 模式启用 OS Keyring/DPAPI/Keychain/libsecret，但必须作为可选策略。
- 授权服务只依赖 `IMachineIdentityProvider`、`ILicenseStore`、`IAppPathProvider`，不要直接依赖系统路径。

### 12.2 设置加密

当前 `SecurityHelper` 使用固定 AES Key/IV，不适合长期保留。

建议:

- 桌面 portable: 主密钥或密钥包默认放在 `App_Data/Security`，必要时要求用户设置主密码
- 桌面 installed: 可选 OS Keyring/DPAPI/libsecret/Keychain
- API 服务端: 环境变量或 Docker secret 提供主密钥
- 敏感字段: 邮件密码、WebDAV 密码、PostgreSQL 密码、AI Key

验收标准:

- 不同机器生成的本机密文不能互相解密，除非显式使用迁移密钥。
- Docker 中可以通过环境变量注入密钥。
- 配置导出时默认不导出明文密码。
- portable 目录打包迁移时，用户能选择“完整迁移可用”或“换机后重新输入敏感密码”两种安全策略。

## 13. 发布体积策略

### 13.1 当前体积

审查时统计:

| 资源 | 大小 |
|---|---:|
| 旧 `ExportDocManager/WebView2Runtime` | 635.9MB，仅作为历史体积参考 |
| 根目录 `OcrModels` | 约 29.7MB |
| 根目录 `Resources` | 约 29.4MB |
| 当前 Tauri/API/Web 运行目录 | 以 `artifacts/windows-desktop-run/ExportDocManager/` 为准，包含主程序、sidecar、模板、资源、OCR、浏览器 renderer 和注册机 |
| 当前分发包 | 以 Tauri bundle / NSIS 或运行目录 zip 的实际构建结果为准 |

判断:

- Windows WebView2 loader、Chromium/Chrome Headless Shell、OCR 模型和自包含 sidecar native 依赖是当前体积主要来源。
- ASP.NET Core API sidecar 不是体积核心问题。
- Go/Rust 重写主后端不会显著解决当前 800MB 级别包体。

### 13.2 新发布包建议

拆分发行:

| 版本 | 内容 | 目标 |
|---|---|---|
| 桌面轻量版 | Tauri + sidecar + 业务依赖，不带固定 WebView2，不带 OCR 模型 | 日常安装 |
| 桌面完整版 | 带 OCR 模型，可选带固定 WebView2 | 离线客户 |
| Docker API | API + PostgreSQL 支持，不带 WebView2 | 团队/服务器 |
| Docker API OCR | API + OCR native + 模型 | 需要 OCR |
| Web 静态包 | React 静态资源 | 服务端部署 |

预期:

- 不带固定 WebView2 时，桌面包会明显小于当前便携包。
- OCR 模型可选下载后，轻量版可进一步缩小。
- Docker 版不需要 WebView2，体积主要由 .NET runtime、业务依赖、PDF/OCR 可选组件决定。

### 13.3 便携包目录要求

桌面便携包应尽量保持“一个目录就是一个完整运行环境”:

```text
ExportDocManagerPortable/
  ExportDocManager.exe
  sidecar/
  web/
  runtimes/
  App_Data/
    WebView/
  README-PORTABLE.txt
```

发布策略:

- 首次启动自动创建 `App_Data` 子目录。
- 升级程序时只覆盖应用文件，不覆盖 `App_Data`。
- 日志、数据库、模板、章图、导出文件、OCR 模型都可随目录备份。
- 可提供“清理缓存”命令，只清理 `App_Data/Cache`，不触碰正式数据。
- 可提供“导出诊断包”命令，打包版本信息、配置摘要、最近日志，不包含明文密码。
- 如果安装目录无写权限，启动时提示用户选择数据目录，不自动写到 C 盘隐藏位置。
- Tauri WebView 的缓存、配置、Local Storage 和 Session Storage 必须通过 `data_directory` 固定到运行数据根 `WebView/`，不能继续使用系统默认用户配置目录。

安装版也应遵守同一原则:

- 安装器提供“程序安装目录”和“数据目录”选择。
- 默认数据目录优先为 `{安装目录}\App_Data`。
- 如果安装到 `C:\Program Files`，默认数据目录不应自动变成 AppData/ProgramData，而应提示选择非系统盘数据目录。
- 安装器界面应说明数据库、OCR 模型、导出文件、日志、备份会持续增长，建议放到 D/E 等业务盘。
- 升级时只更新程序文件，不移动、不清空、不覆盖用户选择的数据目录。

当前多平台/多架构实现与验收边界见 `多平台与多架构支持矩阵.md`。当前代码和 CI 覆盖 Windows/Linux/macOS 与 x64/ARM64 编译契约；Windows x64 已本机验证，非 Windows OCR、浏览器渲染器、平台安装器和真实 ARM64 设备仍需分别验收。

## 14. 测试与质量门槛

### 14.1 当前测试状态

当前测试覆盖不少，尤其是:

- 单一窗口
- 设置
- 导出/模板
- 工具能力
- ViewModel 工作流

本次审查尝试运行:

```text
dotnet test ExportDocManager.Tests\ExportDocManager.Tests.csproj --no-restore -v minimal
```

结果:

- 120 秒超时，未生成可用结果。
- 超时后残留的 `dotnet` 测试进程已清理。

这说明重构前需要拆分测试层级，避免每次回归都跑完整 Windows UI 测试。

### 14.2 新测试分层

推荐:

| 测试集 | 目标时长 | 内容 |
|---|---:|---|
| Fast Unit | < 60s | 纯业务、映射、模板渲染、权限策略 |
| Data Integration | < 3min | SQLite/PostgreSQL repository、事务、迁移 |
| API Integration | < 3min | 认证、权限、Controller、OpenAPI |
| File/Export Integration | < 5min | Excel/PDF/ZIP/模板包 |
| OCR Optional | 可单独跑 | OCR 模型、PDF 图片 OCR |
| E2E | 可单独跑 | Playwright 前端流程 |
| Legacy WinForms | 可单独跑 | 旧 UI 回归 |

### 14.3 必须补的测试

第一阶段必须补:

- `BusinessDataAccessPolicy` 改为请求级用户上下文后的权限测试。
- `ReportTemplateRenderer` 跨平台项目测试。
- `SingleWindowPayloadGenerator` API 化前后 XML 快照测试。
- `SingleWindowHandoffPackageService` 包结构兼容测试。
- `SettingsService` 新密钥方案读写测试。
- `IHtmlToPdfService` 可选 smoke test。
- `ExportDocManager.Api` `WebApplicationFactory` 集成测试。
- React 端核心页面 Playwright smoke test。

## 15. 分阶段执行计划

### 阶段 0: 基线与安全网

目标: 不改功能，先建立可迁移边界。

任务:

- 新增重构分支。
- 记录当前便携包体积。
- 把测试拆成快速测试和完整测试。
- 固化单一窗口 XML/交接包样例。
- 固化报表 HTML 渲染样例。
- 固化 Excel 导入/导出样例。
- 记录当前数据库、模板、OCR、日志、导出文件的真实落盘路径。
- 建立 `docs/refactor-log.md` 或在本文追加进度。

验收:

- 快速测试可在 60 秒内完成。
- 有至少 5 个关键业务样例文件。
- 有一份现有运行目录与可写数据目录清单。
- 当前 WinForms 仍可编译运行。

### 阶段 1: 新建跨平台项目骨架

目标: 让核心代码脱离 Windows 编译目标。

任务:

- 新建 `ExportDocManager.Domain`，目标 `net8.0`。
- 新建 `ExportDocManager.Application`，目标 `net8.0`。
- 新建 `ExportDocManager.Infrastructure`，目标 `net8.0`。
- 迁移实体、DTO、分页模型。
- 迁移 EF Core 上下文和 provider 配置。
- 迁移 `AppDbContextExecution`。
- 迁移不依赖 UI 的 Core/MasterData 服务。
- 新增 `IAppPathProvider`、`IFileStorage`、`ITemplateStorage` 等路径/存储抽象。
- 实现本地 `App_Data` 路径 provider，先供 WinForms 和 API 共用。
- 删除新项目中的 WinForms global using。

验收:

- 三个新项目可独立 `dotnet build`。
- `Domain/Application` 不引用 Windows 桌面包。
- 新项目中没有硬编码 `C:\`、AppData、ProgramData 的业务路径。
- 旧 WinForms 项目已从解决方案移除，不再作为阶段验收目标。

### 阶段 2: 用户上下文与权限改造

目标: 为 API 并发请求做准备。

任务:

- 新增 `ICurrentUserContext`。
- 改造 `BusinessDataAccessPolicy`。
- 改造 `UserService` 管理权限判断。
- 移除 LegacyWinForms adapter 过渡依赖。
- API 中实现基于 token/桌面访问令牌的 `CurrentUserContext`。

验收:

- 普通用户/Admin 权限测试通过。
- Tauri/Web/API 行为不回退，旧壳不再作为兼容验收目标。
- 不再有 Application 服务直接依赖静态 `SessionManager.CurrentUser`。

### 阶段 3: ASP.NET Core API MVP

目标: 先跑通 Web/Docker/桌面的共同后端。

任务:

- 新建 `ExportDocManager.Api`。
- 接入配置系统和 Serilog。
- 接入 EF Core SQLite/PostgreSQL。
- 实现 `/healthz`。
- 实现登录、当前用户。
- 实现发票列表分页、发票详情、保存、删除。
- 实现旧单据查询/导出 API：`GET /api/query/invoices` 和 `POST /api/query/invoices/export`，查询只读发票/明细，导出只写用户显式 `.xlsx` 路径。
- 实现客户/出口商/产品/付款基础 API。
- 配置 OpenAPI。
- 生成 TypeScript API client。

验收:

- `dotnet run --project ExportDocManager.Api` 可启动。
- Swagger 可访问。
- SQLite 模式可创建空库并登录。
- 发票列表 API 与旧 WinForms 查询结果一致。
- 单据查询 API 支持按发票类型区分“报关数据/实际数据”；同一发票号双类型并存时查询、导出和更新不互相覆盖。
- 邮件工具 API 支持 SMTP 状态读取和用户显式附件发送，附件缺失时拒绝，且不跨发票/报关与付款/报销数据域取数。

### 阶段 4: React Web 工作区 MVP

目标: 先做可用业务界面，不做全量功能。

任务:

- 新建 `apps/export-doc-web`。
- 接入路由、登录、API client。
- 实现仪表盘真实入口，接入 `GET /api/dashboard` 展示旧 WinForms 口径汇总、最新订单、待办事项和单一窗口近况。
- 实现发票列表、搜索、分页。
- 发票列表显示发票类型，便于同一发票号下区分“实际数据”和“报关数据”。
- 实现旧单据查询页，接入 `/api/query/invoices` 和 `/api/query/invoices/export`，保留类型筛选、可选每页条数、查询视图状态恢复、旧键盘工作流与显式 Excel 保存路径。
- 实现发票编辑页基础字段。
- 实现客户/出口商选择。
- 实现基础资料至少客户/出口商页。
- 实现错误提示、加载状态、空状态。

验收:

- 浏览器可完成登录 -> 查看发票 -> 新建/编辑/保存。
- 表格大数据分页不卡 UI。
- 前端无需 WinForms。

### 阶段 5: 报表与模板 API 化

目标: 让 Web/Tauri 能制作、预览和导出单据。

任务:

- 抽 `IReportTemplateRenderer`。
- 抽 `IReportTemplateCatalog`。
- 抽 `IHtmlToPdfService`。
- API 实现模板列表、模板保存、模板包路径式导入导出和浏览器上传下载。
- API 实现 HTML 预览渲染。
- API 实现 PDF job。
- 前端实现模板管理和预览。
- 迁移现有设计器 HTML/JS 到前端路由。

当前进展 2026-06-24:

- 已完成: `IReportTemplateService`、`IReportTemplateFieldCatalogService`、`IReportPdfRenderService`、模板列表、模板内容读取/管理员保存、模板新建/重命名/删除、字段目录 API、模板包路径式导出/导入、浏览器模板包下载/上传、源码模式格式化、新版 React 结构化可视化设计器、字段候选、自由列数 Row、多列票据格、四边边框、键盘快捷键、付款/报销数据域隔离、客户旧 HTML 渲染回归、内存样例预览、发票 HTML 预览、付款/报销 HTML 预览、发票/付款草稿 HTML 预览、发票/付款未保存草稿正式输出防错、发票/付款预览后手工打印入口、打印前字体/图片/布局就绪等待、发票/付款 PDF 保存对话框业务默认文件名、发票 PDF job、付款/报销 PDF job、批量 PDF ZIP job、单发票多模板单据包 ZIP job、单发票多模板单据邮件 job、旧发票列表 `.edpkg` 业务单据包导出/预览/导入、发票列表托单/单一窗口办理 Tauri 文件对话框闭环、单据包/邮件 PDF 文件名复用旧批量导出 `OutputFileNamePattern`、单据邮件默认主题/正文模板配置、单据包 ZIP 建议名复用旧 `OutputFolderPattern`、单据包 `合并 PDF` 默认值复用旧 `MergePdf` 设置、旧 `BatchExport.Items` 设置页维护与发票页内配置保存、React `#/reports/templates` 源码/新版设计器页和发票编辑页单据包/邮件附件任务入口。打印和 PDF 保存建议名均只使用前端内存 HTML、当前模板名和当前业务号；最终 PDF 仍由用户显式选择路径，不创建默认输出目录，付款/报销不通过 `Payment.InvoiceNo` 反查发票/报关数据。
- 已完成: 发票编辑页 HTML 预览已从“仅已保存发票”扩展为“当前草稿优先”。`POST /api/reports/invoices/draft/html-preview` 接收当前 `ApiInvoiceDetailDto` 草稿并只返回内存 HTML，React 发票编辑页在新建和编辑状态都会把 `normalizeInvoiceForSave` 后的当前草稿传给预览面板；未保存修改可立即预览和打印当前 HTML。正式 PDF、托单 Excel、单据包 ZIP/文件夹和单据邮件继续要求已保存 `invoiceId`，避免草稿误进入文件/任务副作用链路。草稿预览不写数据库、不写缓存、不创建默认输出目录，不读取付款/报销域，也不按发票号合并同号 `实际数据` / `报关数据`；同号双口径仍按当前草稿 `Type` 与当前记录 ID 隔离。真实 Tauri 报表 smoke 已断言保存预览、未保存草稿预览、字段恢复、单据包预览和打印按钮。
- 已完成: 旧 WinForms 唛头图片设计器进入 Tauri/Web/API 发票编辑闭环，图片保存到运行数据根 `Marks/`，发票表只保存图片路径和类型；React 发票编辑页已提供文本/图片切换、受控预览和画布设计器，本轮去除文字工具浏览器弹窗并改为对话框内文字输入。真实 `shippingMarkDesignerCheck` 已覆盖图片模式、编辑器打开、文字输入/编辑、矩形绘制、保存预览、发票保存和 API 持久化读回，最新结果为 `found=true`、`imageFileExists=true`、`persistedType=Image`、`persistedTextCleared=true`。该闭环不按发票号跨 `实际数据` / `报关数据` 覆盖，也不读取付款/报销域。
- 已完成: 旧 WinForms 发票明细单位候选回填进入 Tauri/Web 发票编辑闭环，`UnitEN -> UnitCN` 与 `CtnUnitEN -> CtnUnitCN` 均从单位主数据精确匹配，唯一候选自动回填，多候选行内选择；新增 `CtnUnitCN` 列后，商品库、发票保存和单一窗口映射继续保持包装单位中英文完整。
- 已完成: 旧 WinForms `HsCodeDetailForm` 详情查看已补入 Tauri/Web HS 编码联网结果表，补全远端详情后可在页面内展开查看 HS 编码、商品名称、法定单位、退税率、监管条件、检验检疫、申报要素和描述；未点击保存前不写本地库。
- 已完成: 旧 WinForms 商品资料编辑输入辅助进入 Tauri/Web 主数据闭环，商品编码、英文品名、中文品名、HS 编码、材质、品牌、原产地均有历史候选，HS 编码和英文单位保持大写，保存必填回到“英文品名”；`unitEN -> unitCN` 与 `packageUnitEN -> packageUnitCN` 均支持唯一中文候选自动回填、多候选人工选择和手工中文单位不覆盖；该闭环只读写商品/单位主数据，不与发票/报关或付款/报销数据域互查。
- 已完成: 旧 WinForms HS 编码本地库到商品录入的衔接进入 Tauri/Web 主数据闭环，商品编辑页按本地 HS 编码候选精确套用空白申报字段、中文单位和退税率；不新增后端端点或默认文件路径。
- 已完成: 旧 WinForms 主数据列表自动搜索、行内编辑、行内删除和 `Delete` 键删除当前行进入 Tauri/Web 通用主数据列表闭环，七类主数据共用既有 API 与运行数据根数据库，不新增后端端点或默认文件路径。真实 Tauri/Web smoke 已从客户代表性检查扩展到客户、出口商、收款对象、商品、港口、单位和 HS 编码七类主数据，逐类创建临时记录、进入编辑页真实点击删除、返回列表、校验成功提示并用详情接口确认 `404`；最新 `masterDataDeleteCheck` 证据为 `allDeleted=true`、`entityCount=7`，每类均 `successMessageFound=true`、`redirectedToList=true`、`cleanupDeleted=true`。
- 已完成: 旧 WinForms HS 编码导入/联网维护进入 Tauri/Web/API 主数据闭环，覆盖 Excel 路径导入、浏览器上传导入、远端查询、远端详情补全和显式保存本地；远端结果不自动入库，上传暂存目录位于运行数据根 `Cache/HsCodeImports` 并在导入后清理，不读取发票/报关或付款/报销数据域。
- 已完成: 旧 WinForms HS 编码本地库管理“删除选中”和“清空所有数据”进入 Tauri/Web/API 闭环，可在 HS 编码列表勾选当前页记录后批量删除，也可由管理员在页面内危险确认表单输入 `CLEAR` 后通过 `/api/master-data/hs-codes/clear-all` 清空当前运行数据根数据库中的 HS 编码记录；不再依赖浏览器 prompt，不删除导入源文件、不创建默认目录、不读取业务单据域。真实 Tauri smoke `hsCodeClearAllCheck` 已带 `App_Data/Smoke` 数据根防护验证清空后临时 HS 编码详情为 `404`。
- 已完成: 旧 WinForms 出口商单据章/报关章路径浏览进入 Tauri/Web 桌面桥闭环，桌面模式可选择图片路径并保存到出口商主数据字段；图片文件仍由用户显式选择和维护，不复制到默认目录。
- 已完成: 报表模板页已从旧独立可视化入口切换为新版 React 结构化设计器，源码模式保留；旧 iframe、`/designer/*` 静态托管和独立离线设计器资源已清理。新版设计器以 schema、字段目录、Row 多列行、明细表、条件块、图片/印章块和 HTML/Scriban 导出为核心，生成内容后走 API 保存/预览。
- 已完成: Tauri 桌面报表模板包 `.edtpl` / `.zip` 打开对话框和 `.edtpl` 保存对话框，导入导出路径仍由用户显式选择或手工输入，API 端继续做路径校验；Web/浏览器模式已通过 `/api/reports/templates/package/download|upload` 支持模板包下载和上传导入。
- 已完成: Tauri 报表模板 smoke 覆盖六个内置模板的深链接切换、页面选中、模板内容 active、新版设计器画布和字段目录出现；由于当前路径包含 `C#`，smoke 使用 Web 生产构建 preview，不再依赖 Vite dev。
- 已完成: 设置页运行诊断路径按钮 smoke 覆盖；`tauri:smoke:web:local` 会点击 8 个运行路径打开按钮，并断言传给 `open_path` 的路径与 `/healthz` 返回的程序根、数据根、数据库、SQLite、日志、模板、OCR 和单一窗口目录一致。
- 已完成: 设置页用户与权限 CRUD smoke 覆盖；`tauri:smoke:web:local` 会在真实 Tauri/Web/API 环境里创建临时用户、修改角色、校验表格/表单并删除该用户，最终用户列表回到仅含内置管理员的状态。
- 已完成: 报表渲染保持发票/报关数据域与付款/报销数据域独立，付款/报销端点使用 `paymentId`，不经由发票 ID，也不按参考号反查发票、客户或单一窗口草稿。
- 已完成: 同一发票号的 `实际数据` 与 `报关数据` 继续以 `InvoiceNo + Type` 独立保存和查询；单据包、发票报表和单一窗口链路均使用当前记录 ID，不因发票号相同而合并实际出货数据与报关申报数据。
- 已完成: Tauri/Web 发票编辑页可从当前已保存记录生成同一发票号的另一种口径，API 通过 `POST /api/invoices/{id}/clone-type` 复制表头和明细到目标 `实际数据` / `报关数据`，目标已存在时返回 `409` 且不覆盖；该入口不读取付款/报销域。
- 已完成: 数据库初始化流程补齐旧库兼容，新库和升级后的旧库都以 `InvoiceNo + Type` 作为发票实际/报关口径唯一边界；旧 `InvoiceNo` 单字段唯一索引会在初始化时迁出，空类型补为 `实际数据`，无法自动区分的重复旧数据会返回明确错误并要求人工清理。
- 已完成: Tauri/Web 发票列表补齐旧 WinForms 普通“复制发票”入口，保留复制表头、复制明细、重置状态、重置日期、清空金额五个选项及旧默认值；API `POST /api/invoices/{id}/clone` 返回新发票详情，前端复制成功后刷新缓存并跳转新记录。普通复制和同号双口径生成仍是两个独立入口，均不按发票号读取付款/报销数据域。
- 已完成: Tauri/Web 发票列表补齐旧 WinForms `.edpkg` 发票单据包入口，并继续把列表托单导出和单一窗口办理推进到真实桌面闭环；`invoiceListDesktopWorkflowCheck` 已从列表按钮验证 `.edpkg` 导出/选择/预览/导入、托单 `.xlsx` 任务、COO/ACD `.swpkg` 提交包、COO/ACD 回执包导入、Tauri 文件命令命中、临时发票和临时运行目录清理。导入冲突按 `InvoiceNo + Type` 判断，同号 `实际数据` / `报关数据` 可并存，且不读取付款/报销域。
- 已完成: Tauri/Web 高频编辑页补齐旧 WinForms 工作区未保存变更保护。新增共享 `UnsavedChangesProvider` / `useUnsavedChangesGuard`，在发票、付款/报销、主数据、单一窗口 COO/ACD 草稿页和单一窗口参考词典页按各自 normalize 后的保存 DTO 或页面草稿状态建立已保存快照；返回列表、返回发票、刷新单一窗口草稿、刷新参考词典、导入 JSON 参考词典、恢复内置参考词典、打开 COO/ACD、打开收款方资料库、侧栏/页签链接跳转、退出登录和窗口刷新/关闭前都会在有未保存草稿时确认。该能力只运行在前端内存与浏览器/WebView 离开事件中，不新增 API、数据库表或文件路径；发票实际/报关口径仍按当前记录隔离，COO/ACD 仍按当前 `SourceInvoiceId` 隔离，参考词典仍走既有运行数据根覆盖词典和上传预览链路，付款/报销仍不按 `InvoiceNo` 反查发票。
- 已完成: Tauri/Web/API 发票信用证 `AI合规审查` 入口已形成闭环，`POST /api/tools/letter-of-credit/review` 使用当前发票草稿和信用证文本即时返回审查报告；报告不落库、不生成文件、不按发票号读取另一种实际/报关口径，也不读取付款/报销域。
- 已完成: Tauri/Web 发票编辑页补齐旧 WinForms 表头扩展/报关/银行/特殊条款和总计字段入口，覆盖报关行名称/编码、备用字段 1-3、扩展字段 JSON、出口商中文地址、统一信用代码、银行名称、账号、SWIFT、汇率、特殊条款、总净重、采购总额、退税总额和利润总额；这些字段复用既有 `/api/invoices` DTO 保存当前发票记录，采购/退税/利润保持只读汇总展示，不新增 API、数据库表或默认文件目录，不按发票号合并同号 `实际数据` / `报关数据`，也不读取付款/报销域。
- 已完成: Tauri/Web 设置页补齐旧 WinForms AI 系统提示词维护入口，管理员可在“AI 与单一窗口”区编辑 `AI.SystemPrompt` 并保存到程序根 `appsettings.json`；该配置与 `AI.ApiKey` 的敏感字段保存控制解耦，不新增运行目录、不读取业务数据域。
- 已完成: Tauri/Web/API 设置页补齐旧 WinForms 单一窗口默认申报资料与 COO 签证机构候选联动入口；候选通过只读 `/api/single-window/coo/issuing-authorities` 获取，设置保存仍写程序根 `appsettings.json`，候选目录读取程序根资源和运行数据根覆盖文件，不新增业务数据库写入或系统盘默认落点。
- 已完成: Tauri/Web/API 单一窗口 COO 草稿页补齐旧 WinForms 编辑器高频候选项。新增只读 `/api/single-window/coo/editor-options`，从 Application 内置 catalog 返回申请类型、证书类别、证书类型、币制、贸易方式、生产商保密、展览证书、第三方发票、预计离港、企业承诺、货项标志、包装类型、最高税率、包装单位和按证型区分的原产标准/子标准；React 页面已把证书类型、申请类型、证书类别、签证机构、领证机构、币制、贸易方式、预计离港、生产商保密、展览证书、第三方发票、企业承诺、商品货项标志、包装单位、包装类型、原产标准、子标准、最高税率和附件证书类型恢复为 select/datalist 录入，并保留签证机构选择后联动领证机构与申请地址。该能力只读候选、不新增数据库表或文件落点，保存仍只写当前 `SourceInvoiceId` 的 COO 草稿，不读取付款/报销域。
- 已完成: Tauri/Web 发票编辑页补齐旧 WinForms 删除入口，确认后调用 `DELETE /api/invoices/{id}` 删除当前发票记录并返回列表提示；真实 UI smoke 已覆盖按钮、确认框、列表成功提示、详情接口 `404` 和临时数据清理。该入口只删除当前 ID，不按发票号删除另一种 `实际数据` / `报关数据`，也不读取付款/报销域。
- 已完成: Tauri/Web/API 单一窗口 COO/ACD 编辑器补齐旧 WinForms 高频工具、字段锁定查看/解锁和按分组/类别清理入口：`取默认` 可撤销，新增 `回填空白项`、`清空覆盖`、`撤销`、“字段锁定”弹窗、`清当前分组` 和 `清当前类字段`。字段锁定查询只读当前草稿与当前源发票建议值，`解锁选中` 只恢复当前已知锁定字段并写当前 `SourceInvoiceId` 草稿；范围清理复用 defaults 构建接口，在页面内存草稿中按旧 Application 字段清单恢复对应分组/类别并保留撤销快照。`--single-window-editor-tools-check` 已进入报表 smoke 并扩展到分区清理控件，最新真实 Tauri smoke 确认 COO/ACD 的 `scopedClearCheck` 和 `lockDialogCheck` 均命中。
- 已完成: Tauri/Web/API 单一窗口 COO 生产企业资料库选择/维护入口已形成闭环，`/api/single-window/coo/producer-profiles*` 支持查询、详情、新增、更新和删除，COO 明细行可选择资料套用、把当前行保存为资料、编辑和删除资料；资料只写运行数据根数据库表 `CustomsCooProducerProfiles`，不读取付款/报销域，不按发票号合并同号 `实际数据` / `报关数据`。
- 已完成: Tauri/Web 单一窗口 COO 明细行已补齐旧 WinForms `生成货物描述` 与 `复制原产标准/生产企业到后续项`。生成描述按旧端包装件数、包装单位、英文/中文品名口径写当前行 `goodsDesc`；复制动作只把当前行非空且不同的原产标准、辅助项、生产企业代码、名称、联系人和电话覆盖到后续行。两项均只改当前页面草稿并保留撤销，保存后才写当前 `SourceInvoiceId`；不新增 API、数据库表、文件落点或默认目录，不读取付款/报销域，也不按发票号合并同号 `实际数据` / `报关数据`。
- 已完成: Tauri/Web 单一窗口 COO 附件管理已补齐旧 WinForms 文件选择、分类/路径/备注维护、打开附件和删除附件入口；真实 smoke 已确认附件保存后重载存在、打开路径命中、删除保存后重载消失。附件作为当前 COO 草稿 `attachments` 保存，仍只绑定当前 `SourceInvoiceId`，不复制附件实体文件、不新增默认附件目录，不读取付款/报销域，也不按发票号合并同号 `实际数据` / `报关数据`。
- 待继续: 复杂旧 HTML/CSS/分页脚本精准反向还原、完整单据模板视觉回归、更多客户真实旧模板样本专项恢复断言。

验收:

- 发票、箱单、合同、报关单、付款单可预览，发票和付款/报销编辑页可在 HTML 预览后手工调用浏览器/WebView 打印。
- 至少一个 PDF job 可成功生成并下载。
- 旧模板关键字段渲染一致。

### 阶段 6: 单一窗口 API 化

目标: 保留现有单一窗口本地闭环，并支持 Web/团队模式。

任务:

- COO 编辑 API。
- ACD 编辑 API。
- 导出预检 API。
- 交接包导出 job。
- 回执导入 API。
- 操作中心 API。
- 参考词典维护 API。
- 前端实现操作中心、COO/ACD 编辑和参考词典维护。

当前补充完成:

- 发票列表已恢复旧端列表快捷操作的桌面闭环：`.edpkg` 发票业务包导出/选择/预览/导入、货代订舱托单导出、单一窗口办理、COO/ACD 提交包导出和回执包导入均优先走 Tauri 文件对话框；本轮新增 `--invoice-list-desktop-workflow-check` 并接入 `tauri:smoke:reports:local`，真实验证 `.edpkg/.xlsx/.swpkg` 均为 `PK` 头、托单任务 `Succeeded`、COO `Approved`、ACD `Accepted`、临时发票与 smoke 目录清理成功。
- 操作中心列表已恢复旧端选中批次快捷操作：查看详情、业务目录根保存、派生目录打开、派发到客户端 `OutBox`、自动收集回执并显式导出 `.swpkg` 回执包；本轮继续新增列表级“打包并导入”，把导出的回执包立即写回批次状态和回执日志。详情页继续保留完整手动回执导入/导出和客户端桥接工具。真实 Tauri/Web smoke 已把该列表级链路跑到 COO/ACD 双业务提交包、客户端目录保存、OutBox 派发、InBox 回执收集、回执包导出、回执包导入、COO `Approved` / ACD `Accepted` 状态复核和 `SwReceiptLogs` 记录复核。
- COO/ACD 编辑页已恢复旧端导出前预检分组对话框的核心操作：按分组查看问题、展开源资料变化、勾选可自动修复分组并修复所选分组；修复前会保护当前页面未保存草稿。
- COO/ACD 编辑页已接入共享未保存变更守卫：按当前 `SourceInvoiceId` 的 `normalizeCooDocumentForSave` / `normalizeAgentConsignmentDocumentForSave` 保存体比较草稿，返回发票、刷新草稿、侧栏/页签跳转或窗口关闭前会确认未保存修改；保存、自动修复、字段解锁和重新载入草稿后刷新基准快照，不按发票号合并同号 `实际数据` / `报关数据`，也不触达付款/报销域。
- 参考词典页已接入共享未保存变更守卫：手工编辑、粘贴、去重或 Excel 导入应用到草稿后，刷新参考词典、导入 JSON、恢复内置、侧栏/页签跳转或窗口关闭前都会确认未保存修改；保存、JSON 导入和恢复内置后刷新同步状态。JSON/Excel 仍复用既有 API 和运行数据根覆盖词典/上传预览链路，不新增默认文件目录。

验收:

- 同一发票生成的 COO/ACD XML 与旧版关键节点一致。
- `.swpkg` 兼容旧流程。
- 回执导入能更新状态。
- 权限过滤正确。

### 阶段 7: Tauri 桌面壳

目标: 让新前端作为桌面应用运行。

任务:

- 新建 `apps/export-doc-tauri`。
- 配置 sidecar。
- 实现 API 启动、端口发现、token 注入。
- 实现窗口管理。
- 实现文件选择/打开目录。
- 实现 portable `App_Data` 默认配置。
- 实现安装目录不可写时的数据目录选择或明确报错。
- 实现桌面轻量包构建。

验收:

- Windows 桌面可启动新 Tauri 应用。
- 无需手动启动 API。
- 数据库等业务数据默认写入运行目录 `App_Data`；模板、OCR 模型和日志保持运行目录 `Templates/`、`OcrModels/`、`logs/`。
- 可登录、查看发票、编辑保存。
- 关闭窗口后 sidecar 退出。

### 阶段 8: Docker 与网页版

目标: 支持服务端部署。

任务:

- 编写 `Dockerfile`。
- 编写 `docker-compose.yml`。
- 配置 PostgreSQL。
- 配置 volume: database/files/exports/backups 映射到 `App_Data`，templates/ocr-models/logs 映射到运行目录对应根目录。
- 配置迁移命令。
- 配置反向代理示例。
- 前端构建静态资源。

验收:

- `docker compose up` 后可访问 Web。
- PostgreSQL 初始化成功。
- Admin 初始密码流程明确。
- 文件导入/导出走 `/app/App_Data` volume。

### 阶段 9: 旧 WinForms 退场

目标: 在新功能覆盖后停止扩展旧 UI。

任务:

- 保留旧 WinForms 只做维护。
- 所有新增业务只进 API/React。
- 对照功能清单补齐 Web/Tauri。
- 逐步删除旧 UI 专用服务对业务服务的反向影响。

验收:

- 常用业务流程 Web/Tauri 覆盖。
- 旧 WinForms 可作为兼容版发布或停止发布。

## 16. 第一批可以直接开工的任务

建议按以下顺序开小 PR:

1. 新建 `ExportDocManager.Domain`，迁移 `Models/Entities`。
2. 新建 `ExportDocManager.Application`，迁移分页模型和纯 DTO。
3. 新建 `ExportDocManager.Infrastructure`，迁移 `AppDbContext` 和 `AppDbContextExecution`。
4. 新增 `IAppPathProvider` 和本地运行目录实现，先把数据库、模板、OCR、日志、导出目录纳入统一路径服务；模板/OCR/日志保持程序根目录现有布局，数据库等业务数据继续在 `App_Data`。
5. 新增 `IFileStorage`、`ITemplateStorage` 抽象，禁止业务服务直接拼绝对路径。
6. 拆 `ReportHelper.ReportType` 到非 UI 文件，去掉 `System.Windows.Forms` 引用。
7. 把 `ReportTemplateRenderer` 迁入 Application 并补测试。
8. 新增并使用 `ICurrentUserContext`，移除对静态 `SessionManager.CurrentUser` 的新依赖。
9. 改 `BusinessDataAccessPolicy` 支持显式用户上下文，后续不再新增旧调用兼容分支。
10. 新建并持续扩展 `ExportDocManager.Api`，实现 `/healthz`、OpenAPI、SQLite `App_Data` 数据目录启动验证，覆盖 portable、installed 和 Tauri sidecar 模式。

每个任务都应满足:

- 改动可编译。
- 旧 WinForms 不破。
- 至少有一个对应测试或 smoke 验证。
- 不混入 UI 重设计。

## 17. Go/Rust 的使用边界

### 17.1 不建议重写主业务后端

短中期不建议把主后端改 Go/Rust:

- 业务逻辑太多，重写回归风险高。
- EF Core、Scriban、ClosedXML、PDFsharp/PdfPig、ONNX 已经在 C# 中可用。
- 性能瓶颈不在 ASP.NET Core。
- 体积大头不在 C# API。

### 17.2 可以使用 Rust 的位置

适合:

- Tauri 主进程
- sidecar 启动/看护
- 本地文件系统能力
- 加密/校验/压缩小工具
- 桌面打印适配；当前已具备预览后浏览器/WebView 手工打印，后续继续补高保真视觉回归、批量正式打印和更多平台级打印设置
- 后续极少数性能关键 native 模块

### 17.3 可以使用 Go 的位置

适合:

- 独立更新服务器
- 简单同步中转服务
- 轻量授权服务
- 文件下载/对象存储代理

不建议:

- 第一阶段重写发票、付款、单一窗口、报表主业务。

## 18. AI 辅助迁移策略

AI 可以显著提速，但要用在边界明确的任务上。

适合 AI 做:

- 迁移纯 DTO/实体到新项目。
- 生成 API Controller/Minimal API。
- 生成 OpenAPI 类型和前端 client。
- 把 ViewModel 状态拆成前端页面状态。
- 写单元测试和快照测试。
- 搜索并替换 Windows 依赖。
- 生成 Dockerfile、compose、CI。

不适合直接交给 AI 做:

- 一次性把全部 C# 翻译成 Go/Rust。
- 无测试保护地改单一窗口字段映射。
- 无样例对比地改 PDF/报表模板。
- 无权限测试地改用户/归属过滤。

推荐工作方式:

```text
小任务 -> 生成代码 -> 编译 -> 测试 -> 对照旧行为 -> 合并
```

## 19. 风险矩阵

| 风险 | 等级 | 说明 | 缓解 |
|---|---|---|---|
| 单一窗口字段回归 | 高 | 字段多，外部规范约束强 | XML 快照、XSD 校验、样例包 |
| 报表/PDF 排版差异 | 高 | WebView/Chromium 差异可能影响打印 | PDF 快照、模板视觉验收 |
| 用户权限串扰 | 高 | 静态 `SessionManager` 不适合 API | 请求级用户上下文 |
| 测试过慢 | 中高 | 当前完整测试 120s 未完成 | 拆快速测试 |
| Docker native 依赖 | 中高 | OCR/PDF/Image native 包复杂 | 可选镜像 profile |
| 包体过大 | 中 | 固定 WebView2/OCR 是大头 | 拆轻量/完整版 |
| 前端表单复杂度 | 中 | 单据字段多 | 分模块页面、表单 schema |
| 旧 WinForms 与新 API 双线维护 | 中 | 迁移期成本增加 | 冻结旧 UI 新功能 |
| 授权跨平台 | 中 | Registry/WMI 不可用 | 抽机器身份 provider |

## 20. 验收里程碑

### M1: 跨平台核心可编译

- `Domain/Application/Infrastructure` 均为 `net8.0`。
- 不引用 WinForms/WebView2。
- 旧 WinForms 仍可编译。

### M2: API MVP 可用

- `/healthz`、登录、发票列表、发票编辑可用。
- SQLite 模式可运行。
- OpenAPI 可生成 TS client。

### M3: Web MVP 可用

- 浏览器完成登录、发票分页、编辑保存。
- 基础资料至少客户/出口商可用。
- 发票编辑页可用当前草稿完成旧 WinForms 利润分析计算，且不读取付款/报销数据域。

### M4: 报表可用

- HTML 预览可用。
- PDF job 可用。
- 模板设计器可保存模板；当前源码编辑、新版 React 结构化可视化设计器、模板新建/重命名/删除、字段目录 API 接入、模板包导入/导出、浏览器上传/下载、客户旧 HTML 渲染回归、覆盖六个内置模板的 Tauri 桌面报表模板页 smoke、样例预览，以及从发票/付款报销预览页深链进入设计器并按真实 `invoiceId` / `paymentId` 渲染当前模板已进入 Tauri/Web/API 闭环。新版设计器支持字段候选、自由列数 Row、多列票据格、四边边框、键盘快捷键和付款/报销数据域隔离；真实付款/报销保存预览只调用付款域 API，不通过 `Payment.InvoiceNo` 反查发票。正式 PDF/托单/单据包/邮件仍要求保存后的业务 ID，存在未保存草稿时会提示先保存并禁用正式输出。更多客户真实样本断言和完整视觉回归继续作为 M4 后续补齐项。

### M5: 单一窗口可用

- COO/ACD 编辑、XML、交接包、回执闭环可用。
- 与旧版关键输出一致。

### M6: Tauri 桌面可用

- Tauri 启动 sidecar。
- 本地 SQLite 离线可用。
- 文件选择/下载/打开目录可用。
- 首屏默认进入仪表盘，登录成功和未知路由也回到仪表盘，不再默认打开发票列表。
- Tauri v2 capability 授权完整，`get_desktop_runtime_context` 等桌面 IPC 命令不应在正常桌面启动时显示红字插件/权限错误。
- 登录或会话恢复后执行旧 WinForms 同口径静默更新检查；强制更新时业务工作区被门禁阻断，仅允许进入更新中心或通过 Tauri 关闭主窗口退出。
- 旧 WinForms `DefaultExportDirectory` 已承接到 Tauri 保存/文件夹对话框初始目录；主要导出入口仍只写用户显式选择路径，无效默认目录会被忽略，不创建系统盘默认输出目录。

### M7: Docker/Web 可用

- Docker Compose + PostgreSQL 可启动。
- 浏览器访问完整业务。
- volume 持久化模板和文件。

## 21. 最终建议

项目需要重构，但不是重写业务核心。正确重构重点是:

1. 把 C# 业务核心从 WinForms 编译目标里拆出来。
2. 建 ASP.NET Core API 作为桌面、Docker、Web 的共同后端。
3. 用 React/Vite 做统一前端，用 Tauri 打包桌面。
4. 打印/PDF/OCR/授权/文件对话框这些平台能力单独适配。
5. 把数据库、模板、日志、OCR 模型、导出文件默认放在运行目录内，保持便携部署能力；不强制全部进入 `App_Data`，现有 `Templates/`、`OcrModels/`、`logs/` 布局继续有效。
6. 用 `IAppPathProvider`、存储接口、任务接口和平台 adapter 保持解耦，避免新架构再次变成单体。
7. 用测试和样例锁住单一窗口、报表、导出、权限这些高风险业务。

只有当 API 边界稳定、性能数据证明某个模块确实成为瓶颈时，才考虑把该模块用 Rust 或 Go 单独替换。主业务后端短中期继续使用 ASP.NET Core，是当前成本、风险、速度最平衡的方案。

### 2026-07-15 审计日志导出契约补充

审计日志导出已按运行形态拆分为两个明确端点：Tauri 使用受 desktop token 保护的 `POST /api/audit-logs/save-to-path`，浏览器/容器使用不接收服务器绝对路径的 `POST /api/audit-logs/download`。后者以内存生成 Excel attachment，由浏览器默认下载目录接收文件；不创建服务器导出副本。旧 `/api/audit-logs/export` 契约已删除，未投产项目不保留兼容别名或旧 DTO。前端浏览器模式因此不再显示路径输入框，桌面模式继续使用原生保存对话框。

### 2026-07-15 全项目导出下载当前契约补充

导出能力现已按平台形成统一适配层：Tauri 原生文件对话框调用独立 `save-to-path` 端点并携带 desktop token；Web/容器对小文件使用 attachment，对 PDF、批量 ZIP、单据包、Excel 转换和 PDF 合并等耗时功能使用“创建任务 -> 查询状态 -> `/api/jobs/{jobId}/download`”。浏览器不输入服务器绝对路径，也不读取单一窗口本机交换箱目录。

浏览器任务输出统一位于运行数据根 `Exports/Browser`，文件上传统一位于运行数据根 `Cache/BrowserUploads` 并在结束后清理。普通 Web 任务响应只返回文件名，路径端点内部必须显式验证 desktop token。旧 `/export` 路由、旧 operationId、旧 DTO 和适配器已直接删除，不增加兼容别名。

该方案已经覆盖查询、审计日志、发票/付款报表、批量 ZIP、发票单据包、Excel 工具、PDF 合并、发票转移包、报表模板包、支持包及单一窗口提交/回执包。代码验证为 API `222/222`、全解决方案 `492/492`、Web `1833 modules` 和 Tauri Debug 编译通过。详细端点矩阵、权限、清理和部署验收边界见 `docs/全项目导出下载契约重构与多平台审查记录.md`。


