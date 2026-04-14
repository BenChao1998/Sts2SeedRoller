using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;
using SeedModel.Rng;

namespace SeedModel.Sts2.Ancients;

internal sealed record AncientGenerationContext(
    uint RunSeed,
    CharacterId Character,
    int ActIndex,
    IReadOnlyList<CharacterId> UnlockedCharacters);

internal abstract class AncientEventLogic
{
    protected AncientEventLogic(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public abstract IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng);

    protected static AncientOptionResult CreateOption(string optionId, string? note = null) =>
        new(optionId, note);

    protected static IReadOnlyList<AncientOptionResult> Build(params AncientOptionResult[] options) => options;

    protected static string GetCharacterName(CharacterId id) =>
        id switch
        {
            CharacterId.Ironclad => "Ironclad",
            CharacterId.Silent => "Silent Huntress",
            CharacterId.Defect => "Defect",
            CharacterId.Necrobinder => "Necrobinder",
            CharacterId.Regent => "Regent",
            _ => id.ToString()
        };
}

internal sealed record AncientOptionResult(string OptionId, string? Note = null);

internal sealed class OrobasEventLogic : AncientEventLogic
{
    private static readonly string[] Pool1 =
    [
        "ELECTRIC_SHRYMP",
        "GLASS_EYE",
        "SAND_CASTLE"
    ];

    private static readonly string[] Pool2 =
    [
        "ALCHEMICAL_COFFER",
        "DRIFTWOOD",
        "RADIANT_PEARL"
    ];

    private static readonly string[] Pool3 =
    [
        "TOUCH_OF_OROBAS",
        "ARCHAIC_TOOTH"
    ];

    public OrobasEventLogic()
        : base("OROBAS", "Orobas")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var pool1 = Pool1.ToList();

        var otherCharacters = context.UnlockedCharacters
            .Where(c => c != context.Character)
            .ToList();
        var chosenCharacter = otherCharacters.Count == 0
            ? context.Character
            : rng.NextItem(otherCharacters);

        var special = rng.NextFloat() < 1f / 3f
            ? CreateOption("PRISMATIC_GEM")
            : CreateOption("SEA_GLASS", $"Character: {GetCharacterName(chosenCharacter)}");
        pool1.Add(special.OptionId);

        var option1 = CreateOption(pool1[rng.NextInt(pool1.Count)], special.Note);
        var option2 = CreateOption(Pool2[rng.NextInt(Pool2.Length)]);
        var option3 = CreateOption(Pool3[rng.NextInt(Pool3.Length)]);

        return Build(option1, option2, option3);
    }
}

internal sealed class PaelEventLogic : AncientEventLogic
{
    private static readonly string[] Pool1 =
    [
        "PAELS_FLESH",
        "PAELS_HORN",
        "PAELS_TEARS"
    ];

    private static readonly string[] Pool3 =
    [
        "PAELS_EYE",
        "PAELS_BLOOD"
    ];

    public PaelEventLogic()
        : base("PAEL", "Pael")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var option1 = CreateOption(Pool1[rng.NextInt(Pool1.Length)]);

        var pool2 = new List<string>
        {
            "PAELS_WING",
            "PAELS_CLAW",
            "PAELS_TOOTH"
        };
        pool2.AddRange(pool2);
        pool2.Add("PAELS_GROWTH");
        var option2 = CreateOption(pool2[rng.NextInt(pool2.Count)]);

        var pool3 = Pool3.ToList();
        pool3.Add("PAELS_LEGION");
        var option3 = CreateOption(pool3[rng.NextInt(pool3.Count)]);

        return Build(option1, option2, option3);
    }
}

internal sealed class TezcataraEventLogic : AncientEventLogic
{
    private static readonly string[] Pool1 =
    [
        "NUTRITIOUS_SOUP",
        "VERY_HOT_COCOA",
        "YUMMY_COOKIE"
    ];

    private static readonly string[] Pool2 =
    [
        "BIIIG_HUG",
        "STORYBOOK",
        "SEAL_OF_GOLD",
        "TOASTY_MITTENS"
    ];

    private static readonly string[] Pool3 =
    [
        "GOLDEN_COMPASS",
        "PUMPKIN_CANDLE",
        "TOY_BOX"
    ];

    public TezcataraEventLogic()
        : base("TEZCATARA", "Tezcatara")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var option1 = CreateOption(Pool1[rng.NextInt(Pool1.Length)]);
        var option2 = CreateOption(Pool2[rng.NextInt(Pool2.Length)]);
        var option3 = CreateOption(Pool3[rng.NextInt(Pool3.Length)]);
        return Build(option1, option2, option3);
    }
}

internal sealed class NonupeipeEventLogic : AncientEventLogic
{
    private static readonly string[] BasePool =
    [
        "BLESSED_ANTLER",
        "BRILLIANT_SCARF",
        "DELICATE_FROND",
        "DIAMOND_DIADEM",
        "FUR_COAT",
        "GLITTER",
        "JEWELRY_BOX",
        "LOOMING_FRUIT",
        "SIGNET_RING"
    ];

    public NonupeipeEventLogic()
        : base("NONUPEIPE", "Nonupeipe")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var pool = BasePool.ToList();
        pool.Add("BEAUTIFUL_BRACELET");
        rng.Shuffle(pool);

        return Build(
            CreateOption(pool[0]),
            CreateOption(pool[1]),
            CreateOption(pool[2]));
    }
}

internal sealed class TanxEventLogic : AncientEventLogic
{
    private static readonly string[] BasePool =
    [
        "CLAWS",
        "CROSSBOW",
        "IRON_CLUB",
        "MEAT_CLEAVER",
        "SAI",
        "SPIKED_GAUNTLETS",
        "TANXS_WHISTLE",
        "THROWING_AXE",
        "WAR_HAMMER"
    ];

    public TanxEventLogic()
        : base("TANX", "Tanx")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var pool = BasePool.ToList();
        pool.Add("TRI_BOOMERANG");
        rng.Shuffle(pool);

        return Build(
            CreateOption(pool[0]),
            CreateOption(pool[1]),
            CreateOption(pool[2]));
    }
}

internal sealed class VakuuEventLogic : AncientEventLogic
{
    private static readonly string[] Pool1 =
    [
        "BLOOD_SOAKED_ROSE",
        "WHISPERING_EARRING",
        "FIDDLE"
    ];

    private static readonly string[] Pool2 =
    [
        "PRESERVED_FOG",
        "SERE_TALON",
        "DISTINGUISHED_CAPE"
    ];

    private static readonly string[] Pool3 =
    [
        "CHOICES_PARADOX",
        "MUSIC_BOX",
        "LORDS_PARASOL",
        "JEWELED_MASK"
    ];

    public VakuuEventLogic()
        : base("VAKUU", "Vakuu")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var list1 = Pool1.ToList();
        rng.Shuffle(list1);

        var list2 = Pool2.ToList();
        rng.Shuffle(list2);

        var list3 = Pool3.ToList();
        rng.Shuffle(list3);

        return Build(
            CreateOption(list1[0]),
            CreateOption(list2[0]),
            CreateOption(list3[0]));
    }
}

internal sealed class DarvEventLogic : AncientEventLogic
{
    private sealed record RelicSet(Func<AncientGenerationContext, bool> Filter, string[] Relics);

    private static readonly RelicSet[] RelicSets =
    [
        new(context => true, ["ASTROLABE"]),
        new(context => true, ["BLACK_STAR"]),
        new(context => true, ["CALLING_BELL"]),
        new(context => true, ["EMPTY_CAGE"]),
        new(context => true, ["PANDORAS_BOX"]),
        new(context => true, ["RUNIC_PYRAMID"]),
        new(context => true, ["SNECKO_EYE"]),
        new(context => context.ActIndex == 1, ["ECTOPLASM", "SOZU"]),
        new(context => context.ActIndex == 2, ["PHILOSOPHERS_STONE", "VELVET_CHOKER"])
    ];

    public DarvEventLogic()
        : base("DARV", "Darv")
    {
    }

    public override IReadOnlyList<AncientOptionResult> GenerateOptions(AncientGenerationContext context, GameRng rng)
    {
        var pool = new List<AncientOptionResult>();
        foreach (var set in RelicSets)
        {
            if (!set.Filter(context))
            {
                continue;
            }

            var relicId = set.Relics[rng.NextInt(set.Relics.Length)];
            pool.Add(CreateOption(relicId));
        }

        rng.Shuffle(pool);

        if (rng.NextBool())
        {
            return Build(pool[0], pool[1], CreateOption("DUSTY_TOME"));
        }

        return Build(pool[0], pool[1], pool[2]);
    }
}
