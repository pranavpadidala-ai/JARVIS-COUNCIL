using System.Text.RegularExpressions;
using System.Xml.Linq;
using Council.Security;
using Microsoft.Data.Sqlite;

namespace Council.App.Notify;

public sealed record WindowsNotification(string App, DateTimeOffset At, string Title, string Body);

/// <summary>
/// Reads recent Windows notifications (WhatsApp, Gmail, Teams, Outlook, system alerts...) from the
/// notification database Windows keeps in the user's profile. Unpackaged apps cannot use the official
/// listener API, but this file is the user's own data. It is copied to a temp folder (Windows keeps the
/// original open), read, and the copy is deleted immediately. Nothing is sent anywhere by this class.
/// </summary>
public sealed class NotificationReader
{
    private readonly string _databasePath;

    public NotificationReader(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db");
    }

    public IReadOnlyList<WindowsNotification> Recent(DateTimeOffset since, int max)
    {
        if (!File.Exists(_databasePath)) throw new FileNotFoundException("The Windows notification database was not found.");
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jarvis-wpn-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var copy = Path.Combine(temp, "wpn.db");
            CopyShared(_databasePath, copy);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(_databasePath + suffix)) CopyShared(_databasePath + suffix, copy + suffix);

            var result = new List<WindowsNotification>();
            using (var connection = new SqliteConnection($"Data Source={copy};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT h.PrimaryId, n.ArrivalTime, n.Payload FROM Notification n " +
                    "JOIN NotificationHandler h ON h.RecordId = n.HandlerId " +
                    "WHERE n.Type = 'toast' AND n.ArrivalTime >= $since ORDER BY n.ArrivalTime DESC LIMIT $max";
                command.Parameters.AddWithValue("$since", since.UtcDateTime.ToFileTimeUtc());
                command.Parameters.AddWithValue("$max", Math.Clamp(max, 1, 200));
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var payload = reader.GetValue(2) switch { byte[] b => System.Text.Encoding.UTF8.GetString(b), var o => o?.ToString() ?? "" };
                    var (title, body) = ParseToast(payload);
                    if (title.Length == 0 && body.Length == 0) continue;
                    result.Add(new WindowsNotification(FriendlyApp(reader.GetString(0)),
                        DateTimeOffset.FromFileTime(reader.GetInt64(1)), SecretRedactor.Redact(title), SecretRedactor.Redact(body)));
                }
            }
            return result;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Title is the first text element of the toast; the rest is the body.</summary>
    public static (string Title, string Body) ParseToast(string xml)
    {
        try
        {
            var texts = XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "text")
                .Select(e => e.Value.Trim()).Where(t => t.Length > 0).ToList();
            if (texts.Count == 0) return ("", "");
            var body = string.Join(" | ", texts.Skip(1));
            return (texts[0], body.Length > 400 ? body[..400] + "..." : body);
        }
        catch (System.Xml.XmlException)
        {
            return ("", "");
        }
    }

    /// <summary>"5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App" becomes "WhatsApp".</summary>
    public static string FriendlyApp(string primaryId)
    {
        var name = primaryId.Split('!')[0];
        var underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        if (KnownApps.TryGetValue(name, out var known)) return known;
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return name.Length == 0 ? primaryId : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WhatsAppDesktop"] = "WhatsApp", ["WhatsApp"] = "WhatsApp", ["OutlookForWindows"] = "Outlook", ["MSTeams"] = "Teams",
        ["YourPhone"] = "Phone Link", ["Chrome"] = "Chrome", ["Brave"] = "Brave"
    };

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(destination);
        input.CopyTo(output);
    }
}
