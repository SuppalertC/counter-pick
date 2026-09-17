using System.Reflection;

namespace DotaComboBoard.Services;

public static class AppVersionInfo
{
    public static string Current { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informationalVersion = typeof(AppVersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+')[0];
        }

        return typeof(AppVersionInfo).Assembly.GetName().Version?.ToString(3) ?? "1.3.0";
    }
}
