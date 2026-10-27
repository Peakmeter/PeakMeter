using System.IO;
using System.Text.Json;

namespace PeakMeter;

/// <summary>
/// The game's pets: names, genus and which monsters drop their souls (data/pets.json, built from A2Tools-Plugins'
/// pet-souls plugin, GPL-3.0, from the game's own tables).
/// </summary>
public sealed class PetBook
{
    public sealed record Drop(int Npc, string? Chance, int Level, string? Region, int Spawns,
        IReadOnlyDictionary<string, string> NpcNames, IReadOnlyDictionary<string, string> MapNames,
        int MapId = 0, IReadOnlyDictionary<string, string>? ZoneNames = null, int Teleport = 0,
        IReadOnlyDictionary<string, string>? TeleportNames = null);

    public sealed record Pet(int Id, IReadOnlyDictionary<string, string> Names, string? Genus, IReadOnlyList<Drop> Drops)
    {
        public bool Special => Genus == "special";
        public bool ChestOnly => Drops.Count == 0;
    }

    public IReadOnlyDictionary<int, Pet> Pets { get; }
    /// <summary>Monster (NPC code) → the pets its kill gives souls to.</summary>
    public IReadOnlyDictionary<int, int[]> ByNpc { get; }
    private readonly Dictionary<string, Dictionary<string, string>> _genus = new();

    private PetBook(Dictionary<int, Pet> pets, Dictionary<int, int[]> byNpc)
    {
        Pets = pets;
        ByNpc = byNpc;
    }

    public static PetBook Load(string dataDir)
    {
        var pets = new Dictionary<int, Pet>();
        var byNpc = new Dictionary<int, int[]>();
        var book = new PetBook(pets, byNpc);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "pets.json")));
            var root = doc.RootElement;
            foreach (var p in root.GetProperty("pets").EnumerateObject())
            {
                if (!int.TryParse(p.Name, out var id)) continue;
                var drops = new List<Drop>();
                if (p.Value.TryGetProperty("drops", out var ds))
                    foreach (var d in ds.EnumerateArray())
                        drops.Add(new Drop(d.GetProperty("npc").GetInt32(), Str(d, "chance"),
                            d.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0,
                            Str(d, "region"), d.TryGetProperty("spawns", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt32() : 0,
                            Texts(d, "name"), Texts(d, "map"),
                            Int(d, "mapId"), Texts(d, "zone"), Int(d, "teleport"), Texts(d, "teleportName")));
                pets[id] = new Pet(id, Texts(p.Value, "names"), Str(p.Value, "genus"), drops);
            }
            foreach (var n in root.GetProperty("npcs").EnumerateObject())
                if (int.TryParse(n.Name, out var npc))
                    byNpc[npc] = n.Value.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (root.TryGetProperty("genus", out var g))
                foreach (var x in g.EnumerateObject()) book._genus[x.Name] = Texts(g, x.Name);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return book;
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    /// <summary>Every soul drop on a map: (monster drop, pet).</summary>
    public IEnumerable<(Drop Drop, Pet Pet)> DropsOn(int mapId) =>
        mapId == 0 ? [] : Pets.Values.SelectMany(p => p.Drops.Where(d => d.MapId == mapId).Select(d => (d, p)));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Dictionary<string, string> Texts(JsonElement e, string name)
    {
        var map = new Dictionary<string, string>();
        if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            foreach (var x in v.EnumerateObject())
                if (x.Value.GetString() is { Length: > 0 } s) map[x.Name] = s;
        return map;
    }

    /// <summary>A text in the meter's language, else English, else any.</summary>
    public static string Local(IReadOnlyDictionary<string, string> texts, string fallback = "") =>
        texts.TryGetValue(L.Lang, out var s) ? s
        : texts.TryGetValue(L.Lang.Split('-')[0], out s) ? s
        : texts.TryGetValue("en", out s) ? s
        : texts.Values.FirstOrDefault() ?? fallback;

    public string Name(int pet) => Pets.TryGetValue(pet, out var p) ? Local(p.Names, $"#{pet}") : $"#{pet}";

    public string GenusName(string? genus) => genus is not null && _genus.TryGetValue(genus, out var t) ? Local(t, genus) : "";
}

/// <summary>
/// The player's pet collection: level and souls per pet, per server (the game keeps one collection for all characters
/// on a server). Synced from the login list, counted up with every soul picked up; saved in
/// %AppData%\PeakMeter\pets-collection.json.
/// </summary>
public sealed class PetCollection
{
    public const int MaxLevel = 3;
    /// <summary>Souls a pet needs to go from this level to the next: 5, 25, 75.</summary>
    public static int Need(int level) => level switch { 0 => 5, 1 => 25, 2 => 75, _ => 0 };
    private static readonly System.Windows.Media.Brush Green = Frozen(0x5C, 0xD0, 0x8A);
    private static readonly System.Windows.Media.Brush Orange = Frozen(0xF0, 0xA0, 0x40);
    private static readonly System.Windows.Media.Brush Red = Frozen(0xF0, 0x60, 0x5A);

    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>At a glance: green = level 3 (full), orange = level 2 (halfway), red = level 0-1 (most souls missing).</summary>
    public static System.Windows.Media.Brush ColorOf(int level) => level >= MaxLevel ? Green : level == 2 ? Orange : Red;

    /// <summary>Souls still needed to reach level 3.</summary>
    public static int ToMax(int level, int points)
    {
        var sum = 0;
        for (var l = level; l < MaxLevel; l++) sum += Need(l);
        return Math.Max(0, sum - points);
    }

    private sealed class Store
    {
        public Dictionary<string, Dictionary<string, int[]>> Servers { get; set; } = new();
        public Dictionary<string, DateTimeOffset> ListAt { get; set; } = new();
    }

    private readonly string _path = Path.Combine(Settings.Dir, "pets-collection.json");
    private readonly object _gate = new();
    private Store _store = new();

    /// <summary>Raised (any thread) when counts changed.</summary>
    public event Action? Changed;

    public PetCollection(bool persistent)
    {
        if (!persistent) { _path = ""; return; }
        try
        {
            if (File.Exists(_path)) _store = JsonSerializer.Deserialize<Store>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    /// <summary>True once the game's pet list was seen for this server.</summary>
    public bool Known(int server)
    {
        lock (_gate) return _store.ListAt.ContainsKey(server.ToString());
    }

    public DateTimeOffset? ListAt(int server)
    {
        lock (_gate) return _store.ListAt.TryGetValue(server.ToString(), out var at) ? at : null;
    }

    public (int Level, int Points) Get(int server, int pet)
    {
        lock (_gate)
            return _store.Servers.TryGetValue(server.ToString(), out var s) && s.TryGetValue(pet.ToString(), out var v) && v.Length == 2
                ? (v[0], v[1])
                : (0, 0);
    }

    /// <summary>The login list: replaces what was known for the server.</summary>
    public void SetList(int server, IReadOnlyList<(int Pet, int Level)> levels, IReadOnlyList<(int Pet, int Points)> points)
    {
        if (server <= 0) return;
        lock (_gate)
        {
            var map = new Dictionary<string, int[]>();
            foreach (var (pet, level) in levels) map[pet.ToString()] = [level, 0];
            foreach (var (pet, p) in points)
            {
                var key = pet.ToString();
                map[key] = map.TryGetValue(key, out var v) ? [v[0], p] : [0, p];
            }
            _store.Servers[server.ToString()] = map;
            _store.ListAt[server.ToString()] = DateTimeOffset.Now;
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Souls picked up: the count levels the pet up as the game does, starting again from 0.</summary>
    public void AddSouls(int server, int pet, int amount)
    {
        if (server <= 0) return;
        lock (_gate)
        {
            var (level, points) = Get(server, pet);
            points += amount;
            while (level < MaxLevel && points >= Need(level))
            {
                points -= Need(level);
                level++;
            }
            if (level >= MaxLevel) points = 0;
            Put(server, pet, level, points);
            Save();
        }
        Changed?.Invoke();
    }

    public void SetLevel(int server, int pet, int level)
    {
        if (server <= 0) return;
        lock (_gate)
        {
            var (old, points) = Get(server, pet);
            if (level <= old) return;
            Put(server, pet, level, 0);
            Save();
        }
        Changed?.Invoke();
    }

    private void Put(int server, int pet, int level, int points)
    {
        if (!_store.Servers.TryGetValue(server.ToString(), out var s)) _store.Servers[server.ToString()] = s = new();
        s[pet.ToString()] = [level, points];
    }

    private void Save()
    {
        if (_path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_store));
        }
        catch (IOException) { }
    }
}
