using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace OpenCode.Workspace.Core.Tests;

internal static class OracleRuntimeSmokeAssemblyResolver
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += ResolveOracleRuntimeSmoke;
    }

    private static Assembly? ResolveOracleRuntimeSmoke(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (!string.Equals(assemblyName.Name, "OracleRuntimeSmoke", StringComparison.Ordinal))
        {
            return null;
        }

        var repositoryRoot = TestPaths.RepositoryRoot;
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(repositoryRoot, "tools", "OracleRuntimeSmoke", "bin", configuration, "net10.0", "OracleRuntimeSmoke.dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
