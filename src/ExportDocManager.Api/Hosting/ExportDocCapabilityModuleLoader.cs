using ExportDocManager.Services.Infrastructure;
using System.Reflection;
using System.Runtime.Loader;

namespace ExportDocManager.Api.Hosting;

internal static class ExportDocCapabilityModuleLoader
{
    private const string AssemblyPrefix = "ExportDocManager.Infrastructure.";

    public static IReadOnlyList<IExportDocCapabilityModule> Load()
    {
        string assemblyLocation = typeof(ExportDocCapabilityModuleLoader).Assembly.Location;
        string baseDirectory = Path.GetDirectoryName(assemblyLocation)
            ?? throw new InvalidOperationException("无法确定 API 程序集所在目录，不能加载可裁剪能力模块。");
        var modules = new List<IExportDocCapabilityModule>();
        foreach (string assemblyPath in Directory.EnumerateFiles(
                     baseDirectory,
                     $"{AssemblyPrefix}*.dll",
                     SearchOption.TopDirectoryOnly))
        {
            Assembly assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                    string.Equals(candidate.Location, assemblyPath, StringComparison.OrdinalIgnoreCase))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            foreach (Type type in assembly.GetTypes()
                         .Where(type => !type.IsAbstract &&
                             typeof(IExportDocCapabilityModule).IsAssignableFrom(type)))
            {
                if (Activator.CreateInstance(type) is IExportDocCapabilityModule module)
                {
                    modules.Add(module);
                }
            }
        }

        return modules
            .GroupBy(module => module.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Single())
            .OrderBy(module => module.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
