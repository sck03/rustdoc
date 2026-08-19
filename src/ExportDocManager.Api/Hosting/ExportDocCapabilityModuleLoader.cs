using ExportDocManager.Services.Infrastructure;
using System.Reflection;
using System.Runtime.Loader;

namespace ExportDocManager.Api.Hosting;

internal static class ExportDocCapabilityModuleLoader
{
    public static IReadOnlyList<IExportDocCapabilityModule> Load(string hostAssemblyPath)
    {
        string moduleRoot = Path.GetDirectoryName(Path.GetFullPath(hostAssemblyPath))
            ?? throw new InvalidOperationException("无法解析能力模块目录。");
        var modules = new List<IExportDocCapabilityModule>();
        var registeredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string assemblyPath in Directory.EnumerateFiles(
            moduleRoot,
            "ExportDocManager.Infrastructure.*.dll",
            SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            Assembly assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                    string.Equals(candidate.GetName().Name, Path.GetFileNameWithoutExtension(assemblyPath), StringComparison.Ordinal))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            foreach (Type moduleType in assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                    typeof(IExportDocCapabilityModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                if (Activator.CreateInstance(moduleType) is not IExportDocCapabilityModule module ||
                    string.IsNullOrWhiteSpace(module.Key))
                {
                    throw new InvalidOperationException($"能力模块契约无效：{moduleType.FullName}");
                }
                if (!registeredKeys.Add(module.Key))
                {
                    throw new InvalidOperationException($"能力模块键重复：{module.Key}");
                }

                modules.Add(module);
            }
        }

        return modules.OrderBy(module => module.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
