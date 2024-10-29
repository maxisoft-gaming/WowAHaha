// See https://aka.ms/new-console-template for more information

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Maxisoft.Utils.Collections;
using Maxisoft.Utils.Collections.Dictionaries;
using Maxisoft.Utils.Collections.Lists;
using MoonSharp.Interpreter;

static Dictionary<long, WowExpansion> ExtractExpansionDictionary(DynValue? result)
{
    var res = new Dictionary<long, WowExpansion>();
    if (result?.IsNilOrNan() ?? true)
    {
        throw new InvalidOperationException($"Empty lua result, got {result?.Type}");
    }

    if (result.Type is DataType.Tuple)
    {
        result = result.Tuple[0];
    }

    if (result.Type is not DataType.Table)
    {
        throw new InvalidOperationException($"Invalid lua result, expected table, got {result.Type}");
    }

    DynValue? itemIdToVersionId = result.Table.Get("itemIdToVersionId");
    if (itemIdToVersionId.Type is not DataType.Table)
    {
        throw new InvalidOperationException($"Invalid lua result, expected table, got {itemIdToVersionId.Type}");
    }

    DynValue? versionIdToVersion = result.Table.Get("versionIdToVersion");
    if (versionIdToVersion.Type is not DataType.Table)
    {
        throw new InvalidOperationException($"Invalid lua result, expected table, got {versionIdToVersion.Type}");
    }

    var expansionIndex = new Dictionary<double, WowExpansion>(versionIdToVersion.Table.Length);
    foreach (TablePair tablePair in versionIdToVersion.Table.Pairs)
    {
        DynValue? value = tablePair.Value;
        if (value.Type is not DataType.Table)
        {
            throw new InvalidOperationException($"Invalid lua result, expected table, got {value.Type}");
        }

        DynValue? major = value.Table.Get("major");
        if (major?.Type is not DataType.Number)
        {
            throw new InvalidOperationException($"Invalid lua result, expected number, got {major.Type}");
        }

        var expansion = (WowExpansion)major.Number;
        if (expansion <= 0)
        {
            expansion = WowExpansion.Unknown;
        }

        if (expansion > (WowExpansion)((int)WowExpansion.Latest * 2))
        {
            throw new InvalidOperationException($"Invalid expansion {expansion}, this software may need a update");
        }

        if (tablePair.Key.Type is not DataType.Number)
        {
            throw new InvalidOperationException($"Invalid lua result, expected number, got {tablePair.Key.Type}");
        }

        expansionIndex[tablePair.Key.Number] = expansion;
    }

    foreach (TablePair tablePair in itemIdToVersionId.Table.Pairs)
    {
        if (tablePair.Key.Type is not DataType.Number)
        {
            throw new InvalidOperationException($"Invalid lua result, expected number, got {tablePair.Key.Type}");
        }

        var itemId = (long)tablePair.Key.Number;
        if (itemId < 0)
        {
            throw new InvalidOperationException($"Invalid itemId {itemId}, this software may need a update");
        }

        if (tablePair.Value.Type is not DataType.Number)
        {
            throw new InvalidOperationException($"Invalid lua result, expected number, got {tablePair.Value.Type}");
        }

        if (!expansionIndex.TryGetValue(tablePair.Value.Number, out WowExpansion expansion))
        {
            Console.Error.WriteLine($"ItemId {itemId} has no expansion");
            continue;
        }

        res[itemId] = expansion;
    }


    return res;
}

Assembly assembly = typeof(Program).Assembly;
var assemblyName = assembly.GetName().Name;
var resourceName = $"{assemblyName}.ItemVersionAddon.ItemVersion.Data.lua";
await using Stream? stream = assembly.GetManifestResourceStream(resourceName);
if (stream is null)
{
    Console.Error.WriteLine("Resource not found, please check the name of the resource !");
    Console.Error.WriteLine("Here's the list of available resources:");
    foreach (var resource in assembly.GetManifestResourceNames())
    {
        Console.Error.WriteLine("- " + resource);
    }

    throw new InvalidOperationException($"Resource {resourceName} not found in assembly {assemblyName}");
}

using var reader = new StreamReader(stream);
var content = await reader.ReadToEndAsync();

if (content.Length < 1 << 10)
{
    throw new InvalidOperationException($"Resource {resourceName} is empty");
}

content = $$"""
            return (function ()
              local AddonTable = {["injected"] = true}
              (function(...)
              
                {{content}}
                
                
              end)(AddonTable, AddonTable)
              return AddonTable
            end)()
            """;
DynValue? result = Script.RunString(content);

Dictionary<long, WowExpansion> rawExpansions = ExtractExpansionDictionary(result);

CommunityFix(rawExpansions);
OrderedDictionary<long, WowExpansion> expansions = Compress(rawExpansions);

var options = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(expansions, options);

Console.WriteLine(json);
return;

static void CommunityFix(Dictionary<long, WowExpansion> expansions)
{
    // TODO generate stub for dynamically read those values from ItemVersion/Community.lua
    expansions[168583] = WowExpansion.Shadowlands; // Widowbloom
    expansions[168586] = WowExpansion.Shadowlands; // Rising Glory
    expansions[168589] = WowExpansion.Shadowlands; // Marrowroot
    expansions[169701] = WowExpansion.Shadowlands; // Death Blossom
    expansions[171315] = WowExpansion.Shadowlands; // Nightshade
}

static OrderedDictionary<long, WowExpansion> Compress(Dictionary<long, WowExpansion> itemToExpansions)
{
    var sorted = new OrderedDictionary<long, WowExpansion>(itemToExpansions.Count);
    var previous = (WowExpansion)(-1);
    ArrayList<long> keys = itemToExpansions.Keys.ToArrayList();
    keys.Sort();
    sorted.Add(keys[0], itemToExpansions[keys[0]]);
    foreach (var itemId in keys.AsSpan())
    {
        itemToExpansions.TryGetValue(itemId, out WowExpansion expansion);
        if (expansion == previous)
        {
            continue;
        }

        sorted[itemId] = expansion;
        previous = expansion;
    }

    if (!sorted.ContainsKey(keys[^1]))
    {
        sorted.Add(keys[^1], itemToExpansions[keys[^1]]);
    }

    return sorted;
}

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public enum WowExpansion
{
    Unknown = 0,
    Classic = 1,
    TheBurningCrusade = 2,
    WrathOfTheLichKing = 3,
    Cataclysm = 4,
    MistsOfPandaria = 5,
    WarlordsOfDraenor = 6,
    Legion = 7,
    BattleForAzeroth = 8,
    Shadowlands = 9,
    Dragonflight = 10,
    TheWarWithin = 11,

    Latest = 11 // point to latest expansion
}

