using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Shopping;

public sealed record ShoppingItem(string Id, string List, string Name, string Quantity, decimal? Budget, string Note, bool Bought, DateTimeOffset AddedAt);

/// <summary>
/// Local shopping lists (groceries, wishlist, project materials). JARVIS can compare products with
/// web_search and web_open, but it never places orders or enters payment details: the user buys.
/// </summary>
public sealed class ShoppingListTool : ITool
{
    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;

    public ShoppingListTool(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("JARVIS_SHOPPING_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "shopping.json");
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name => "shopping_list";

    public string Description =>
        "Keep the user's shopping lists. Arguments: action = add | bought | remove | show | clear_bought. " +
        "add: name, optional list (default 'groceries'; e.g. wishlist, lab), quantity, budget (rupees) and note. " +
        "bought/remove: name or id. show: optional list. To compare products or prices, use web_search and web_open " +
        "(Amazon.in, Flipkart, BigBasket, etc.) and summarise options with prices and links. Never place an order, " +
        "add to cart, or enter payment or address details: give the user the links and let them buy.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        try
        {
            lock (FileLock)
            {
                var items = Load();
                var list = Arg("list").ToLowerInvariant();
                string message;
                switch (Arg("action").ToLowerInvariant())
                {
                    case "add":
                        var name = Arg("name");
                        if (name.Length is 0 or > 200) throw new ArgumentException("name is required (up to 200 characters).");
                        if (items.Count >= 2000) throw new InvalidOperationException("Shopping lists are full (2,000 items).");
                        decimal? budget = decimal.TryParse(Arg("budget").TrimStart('₹').Replace(",", ""), out var b) && b > 0 ? b : null;
                        var item = new ShoppingItem(Guid.NewGuid().ToString("N")[..6], list.Length == 0 ? "groceries" : list, name,
                            Arg("quantity"), budget, Arg("note"), false, _clock());
                        items.Add(item);
                        message = $"Added {Describe(item)} to {item.List}.";
                        break;
                    case "bought":
                    case "remove":
                        var target = Find(items, Arg("id").Length > 0 ? Arg("id") : Arg("name"), list);
                        if (target is null) return Done(false, "No matching item on the list.");
                        if (Arg("action").Equals("remove", StringComparison.OrdinalIgnoreCase))
                        {
                            items.Remove(target);
                            message = $"Removed {target.Name}.";
                        }
                        else
                        {
                            items[items.IndexOf(target)] = target with { Bought = true };
                            message = $"Marked {target.Name} as bought.";
                        }
                        break;
                    case "show":
                        return Done(true, Show(items, list));
                    case "clear_bought":
                        var removed = items.RemoveAll(i => i.Bought && (list.Length == 0 || i.List == list));
                        message = $"Cleared {removed} bought item(s).";
                        break;
                    default:
                        return Done(false, "action must be add, bought, remove, show or clear_bought.");
                }
                Save(items);
                return Done(true, message);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            return Done(false, ex.Message);
        }
    }

    private static ShoppingItem? Find(List<ShoppingItem> items, string key, string list)
    {
        if (key.Length == 0) return null;
        var pool = items.Where(i => list.Length == 0 || i.List == list).ToList();
        return pool.FirstOrDefault(i => i.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? pool.FirstOrDefault(i => !i.Bought && i.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
               ?? pool.FirstOrDefault(i => !i.Bought && i.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    private static string Show(List<ShoppingItem> items, string list)
    {
        var shown = items.Where(i => list.Length == 0 || i.List == list).ToList();
        if (shown.Count == 0) return list.Length == 0 ? "All shopping lists are empty." : $"The {list} list is empty.";
        var groups = shown.GroupBy(i => i.List).OrderBy(g => g.Key).Select(g =>
        {
            var lines = g.OrderBy(i => i.Bought).Select(i => $"  [{(i.Bought ? "x" : " ")}] {Describe(i)} (id {i.Id})");
            var total = g.Where(i => !i.Bought && i.Budget is not null).Sum(i => i.Budget!.Value);
            return $"{g.Key}:\n" + string.Join("\n", lines) + (total > 0 ? $"\n  Budget still to spend: ₹{total:0.##}" : "");
        });
        return string.Join("\n", groups);
    }

    private static string Describe(ShoppingItem i) =>
        i.Name + (i.Quantity.Length > 0 ? $" x {i.Quantity}" : "") + (i.Budget is { } b ? $" (budget ₹{b:0.##})" : "") +
        (i.Note.Length > 0 ? $" - {i.Note}" : "");

    private List<ShoppingItem> Load() =>
        File.Exists(_path) ? JsonSerializer.Deserialize<List<ShoppingItem>>(File.ReadAllText(_path), JsonOptions) ?? new() : new();

    private void Save(List<ShoppingItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private static Task<ToolResult> Done(bool ok, string message) => Task.FromResult(new ToolResult(ok, message));
}
