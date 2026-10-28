using System.Text.Json;

namespace SHD.Windows;

internal static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SHDDesktop",
        "settings.json");

    public static ShdEnvironment LoadEnvironment()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (Enum.TryParse<ShdEnvironment>(settings?.Environment, ignoreCase: true, out var environment))
                return environment;
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (UnauthorizedAccessException) { }

        return ShdEnvironment.Production;
    }

    public static void SaveEnvironment(ShdEnvironment environment)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new AppSettings(environment.ToString())));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record AppSettings(string Environment);
}
