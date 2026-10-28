using System.Security.Cryptography;
using System.Text;

namespace SHD.Windows;

internal sealed record DesktopAuthCallback(string Code, string State);

internal static class DesktopAuthFlow
{
    public const string CallbackUri = "shd-auth://callback";
    private const string ClientId = "shd-desktop";

    public static PendingDesktopAuth Create(ShdEnvironment environment, BrowserTab targetTab)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = environment.BaseUrl() + "/desktop-auth/authorize" +
                  "?client_id=" + Uri.EscapeDataString(ClientId) +
                  "&redirect_uri=" + Uri.EscapeDataString(CallbackUri) +
                  "&response_type=code" +
                  "&state=" + Uri.EscapeDataString(state) +
                  "&code_challenge=" + Uri.EscapeDataString(challenge) +
                  "&code_challenge_method=S256";

        return new PendingDesktopAuth(state, verifier, environment, targetTab, new Uri(url));
    }

    public static Uri CompleteUri(ShdEnvironment environment) =>
        new(environment.BaseUrl() + "/desktop-auth/complete");

    public static bool TryParseCallback(string value, out DesktopAuthCallback? callback, out bool serverReturnedError)
    {
        callback = null;
        serverReturnedError = false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("shd-auth", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("callback", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath is not ("" or "/") || uri.UserInfo.Length != 0 || uri.Port != -1 ||
            uri.Fragment.Length != 0)
            return false;

        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = Decode(separator < 0 ? pair : pair[..separator]);
            var item = Decode(separator < 0 ? "" : pair[(separator + 1)..]);
            if (!values.TryGetValue(name, out var list)) values[name] = list = [];
            list.Add(item);
        }

        if (values.ContainsKey("error"))
        {
            if (values["error"].Count != 1 ||
                !values.TryGetValue("state", out var errorStates) || errorStates.Count != 1 || string.IsNullOrWhiteSpace(errorStates[0]))
                return false;
            serverReturnedError = true;
            callback = new DesktopAuthCallback("", errorStates[0]);
            return true;
        }

        if (!values.TryGetValue("code", out var codes) || codes.Count != 1 || string.IsNullOrWhiteSpace(codes[0]) ||
            !values.TryGetValue("state", out var states) || states.Count != 1 || string.IsNullOrWhiteSpace(states[0]))
            return false;

        callback = new DesktopAuthCallback(codes[0], states[0]);
        return true;
    }

    public static string FormEncode(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

internal sealed record PendingDesktopAuth(string State, string Verifier, ShdEnvironment Environment, BrowserTab TargetTab, Uri AuthorizationUri);
