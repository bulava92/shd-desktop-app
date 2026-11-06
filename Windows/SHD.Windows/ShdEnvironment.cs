namespace SHD.Windows;

internal enum ShdEnvironment
{
    Production,
    Dev
}

internal static class ShdEnvironmentExtensions
{
    public static string BaseUrl(this ShdEnvironment environment) => environment switch
    {
        ShdEnvironment.Dev => "https://dev-pm.shd.guru",
        _ => "https://pm.shd.guru"
    };

    public static string DisplayName(this ShdEnvironment environment) => environment switch
    {
        ShdEnvironment.Dev => "SHD Dev",
        _ => "SHD Production"
    };

    public static bool IsDevelopmentHost(string host) =>
        host.Equals("dev-pm.shd.guru", StringComparison.OrdinalIgnoreCase);

    public static bool IsProductionHost(string host) =>
        host.Equals("pm.shd.guru", StringComparison.OrdinalIgnoreCase);

    public static bool IsInternalHost(string host) =>
        host.Equals("shd.guru", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".shd.guru", StringComparison.OrdinalIgnoreCase);
}
