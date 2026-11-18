export function runtimeDiagnosticsHealth() {
  const appRoot = "E:/App", dataRoot = "E:/App/App_Data";
  return {
    appRoot, dataRoot, databaseRoot: `${dataRoot}/Database`, databaseProvider: "SQLite",
    productVersion: "1.0.0", informationalVersion: "1.0.0+test", status: "Healthy", checkedAt: "2026-11-18T01:00:00Z",
    runtimePaths: [
      ["appRoot", "程序", appRoot, "application", "read"],
      ["dataRoot", "业务数据", dataRoot, "data", "read-write"],
      ["logRoot", "运行日志", `${dataRoot}/Logs`, "data", "read-write"],
      ["cacheRoot", "缓存", `${dataRoot}/Cache`, "data", "read-write"],
    ].map(([key, label, path, storageClass, accessMode]) => ({ key, label, path, storageClass, accessMode,
      requirement: "required", exists: true, description: "位于显式配置的运行根目录中" })),
    runtimeDependencies: [
      { key: "database", label: "SQLite", requirement: "required", status: "ready", ready: true,
        resolvedPath: `${dataRoot}/Database/business.db`, message: "当前数据库连接及实例锁正常" },
      { key: "report-font", label: "报表字体", requirement: "optional", status: "ready", ready: true,
        resolvedPath: `${appRoot}/Resources/font.ttf`, message: "使用随包受控字体进行原生排版" },
      { key: "ocr", label: "智能 OCR", requirement: "optional", status: "disabled", ready: false,
        resolvedPath: `${appRoot}/OcrModels`, message: "已关闭" },
    ],
  };
}
