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
        ShdEnvironment.Dev => "https://dev-shd.xyz.su",
        _ => "https://shd.xyz.su"
    };

    public static string DisplayName(this ShdEnvironment environment) => environment switch
    {
        ShdEnvironment.Dev => "SHD Dev",
        _ => "SHD Production"
    };

    public static bool IsDevelopmentHost(string host) =>
        host.Equals("dev-shd.xyz.su", StringComparison.OrdinalIgnoreCase);

    public static bool IsProductionHost(string host) =>
        host.Equals("shd.xyz.su", StringComparison.OrdinalIgnoreCase);

    public static bool IsInternalHost(string host) =>
        host.Equals("xyz.su", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".xyz.su", StringComparison.OrdinalIgnoreCase);
}
