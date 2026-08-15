using ExportDocManager.Services.Infrastructure;
using System.Reflection;
using System.Runtime.Loader;

namespace ExportDocManager.Api.Hosting;

internal static class ExportDocCapabilityModuleLoader
{
    private static readonly CapabilityDescriptor[] CapabilityDescriptors =
    [
        new("excel", "ExportDocManager.Infrastructure.Excel.dll", "ExportDocManager.Infrastructure.Excel.ExcelCapabilityModule"),
        new("browser", "ExportDocManager.Infrastructure.Browser.dll", "ExportDocManager.Infrastructure.Browser.BrowserCapabilityModule"),
        new("pdf-ocr", "ExportDocManager.Infrastructure.PdfOcr.dll", "ExportDocManager.Infrastructure.PdfOcr.PdfOcrCapabilityModule")
    ];

    public static IReadOnlyList<IExportDocCapabilityModule> Load(string hostAssemblyPath)
    {
        string moduleRoot = Path.GetDirectoryName(Path.GetFullPath(hostAssemblyPath))
            ?? throw new InvalidOperationException("无法解析能力模块目录。");
        var modules = new List<IExportDocCapabilityModule>();
        foreach (CapabilityDescriptor descriptor in CapabilityDescriptors)
        {
            string assemblyPath = Path.Combine(moduleRoot, descriptor.AssemblyFileName);
            if (!File.Exists(assemblyPath))
            {
                continue;
            }

            Assembly assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                    string.Equals(candidate.GetName().Name, Path.GetFileNameWithoutExtension(descriptor.AssemblyFileName), StringComparison.Ordinal))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            Type moduleType = assembly.GetType(descriptor.TypeName, throwOnError: true, ignoreCase: false)
                ?? throw new InvalidOperationException($"能力模块类型不存在：{descriptor.TypeName}");
            if (Activator.CreateInstance(moduleType) is not IExportDocCapabilityModule module ||
                !string.Equals(module.Key, descriptor.Key, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"能力模块契约无效：{descriptor.TypeName}");
            }

            modules.Add(module);
        }

        return modules;
    }

    private sealed record CapabilityDescriptor(string Key, string AssemblyFileName, string TypeName);
}
