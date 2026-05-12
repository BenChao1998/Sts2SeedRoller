using System.Reflection;
using System.Reflection.Emit;

var asm = Assembly.LoadFrom(@"I:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll");

DumpType(asm, "MegaCrit.Sts2.Core.Factories.CardFactory");
DumpType(asm, "MegaCrit.Sts2.Core.Commands.RewardsCmd");
DumpMatchingTypes(asm, "Player");
DumpMatchingTypes(asm, "CardCreationOptions");
DumpMatchingTypes(asm, "RewardsSet");
DumpMatchingTypes(asm, "CombatRoom");
DumpMatchingTypes(asm, "PlayerRngSet");
DumpMatchingTypes(asm, "PlayerOddsSet");
DumpMatchingTypes(asm, "YUMMY");
DumpMatchingTypes(asm, "COOKIE");
DumpMatchingTypes(asm, "TEZCATARA");
DumpMatchingTypes(asm, "Upgrade");
DumpExactShortName(asm, "Player");
DumpExactShortName(asm, "AbstractRoom");
DumpType(asm, "MegaCrit.Sts2.Core.Entities.Players.Player");
DumpType(asm, "MegaCrit.Sts2.Core.Runs.CardCreationOptions");
DumpType(asm, "MegaCrit.Sts2.Core.Rooms.AbstractRoom");
DumpType(asm, "MegaCrit.Sts2.Core.Rooms.CombatRoom");
DumpType(asm, "MegaCrit.Sts2.Core.Random.PlayerRngSet");
DumpType(asm, "MegaCrit.Sts2.Core.Odds.PlayerOddsSet");
DumpType(asm, "MegaCrit.Sts2.Core.Odds.CardRarityOdds");
DumpType(asm, "MegaCrit.Sts2.Core.Odds.PotionRewardOdds");
DumpType(asm, "MegaCrit.Sts2.Core.Models.Relics.YummyCookie");
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Models.Relics.YummyCookie",
    "AfterObtained",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Models.Relics.YummyCookie",
    "AfterCloned",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Models.Relics.YummyCookie",
    "AfterObtained",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Models.Relics.YummyCookie+<AfterObtained>d__10",
    "MoveNext",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Commands.CardSelectCmd",
    "FromDeckForUpgrade",
    parameterCount: 2,
    parameterTypeNames: ["Player", "CardSelectorPrefs"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Commands.CardSelectCmd+<FromDeckForUpgrade>d__13",
    "MoveNext",
    parameterCount: 0,
    parameterTypeNames: []);
DumpEnum(asm, "MegaCrit.Sts2.Core.Rooms.RoomType");
DumpEnum(asm, "MegaCrit.Sts2.Core.Runs.CardCreationSource");
DumpEnum(asm, "MegaCrit.Sts2.Core.Runs.CardRarityOddsType");
DumpEnum(asm, "MegaCrit.Sts2.Core.Runs.CardCreationFlags");
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "GetDistinctForCombat",
    parameterCount: 4,
    parameterTypeNames: ["Player", "IEnumerable`1", "Int32", "Rng"]);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "CreateForReward",
    parameterCount: 3,
    parameterTypeNames: ["Player", "Int32", "CardCreationOptions"]);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "CreateForReward",
    parameterCount: 3,
    parameterTypeNames: ["Player", "IEnumerable`1", "CardCreationOptions"]);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "FilterForCombat",
    parameterCount: 1,
    parameterTypeNames: ["IEnumerable`1"]);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Runs.CardCreationOptions",
    "GetPossibleCards",
    parameterCount: 1,
    parameterTypeNames: ["Player"]);
DumpMethodReferences(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "RollForRarity",
    parameterCount: 5,
    parameterTypeNames: ["Player", "CardRarityOddsType", "CardCreationSource", "HashSet`1", "Boolean"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "CreateForReward",
    parameterCount: 3,
    parameterTypeNames: ["Player", "IEnumerable`1", "CardCreationOptions"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Runs.CardCreationOptions",
    "GetPossibleCards",
    parameterCount: 1,
    parameterTypeNames: ["Player"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "FilterForCombat",
    parameterCount: 1,
    parameterTypeNames: ["IEnumerable`1"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "RollForRarity",
    parameterCount: 5,
    parameterTypeNames: ["Player", "CardRarityOddsType", "CardCreationSource", "HashSet`1", "Boolean"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory+<>c",
    "<FilterForCombat>b__8_0",
    parameterCount: 1,
    parameterTypeNames: ["CardModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Runs.CardCreationOptions+<>c__DisplayClass35_0",
    "<GetPossibleCards>b__0",
    parameterCount: 1,
    parameterTypeNames: ["CardPoolModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Runs.CardCreationOptions+<>c__DisplayClass35_0",
    "<GetPossibleCards>b__1",
    parameterCount: 1,
    parameterTypeNames: ["CardModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory+<>c",
    "<CreateForReward>b__13_0",
    parameterCount: 1,
    parameterTypeNames: ["CardModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory+<>c",
    "<CreateForReward>b__13_1",
    parameterCount: 1,
    parameterTypeNames: ["CardModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory+<>c__DisplayClass13_0",
    "<CreateForReward>b__2",
    parameterCount: 1,
    parameterTypeNames: ["CardModel"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Runs.CardCreationOptions",
    "ForRoom",
    parameterCount: 2,
    parameterTypeNames: ["Player", "RoomType"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Commands.RewardsCmd",
    "OfferForRoomEnd",
    parameterCount: 2,
    parameterTypeNames: ["Player", "AbstractRoom"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "Roll",
    parameterCount: 1,
    parameterTypeNames: ["CardRarityOddsType"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "RollWithBaseOdds",
    parameterCount: 1,
    parameterTypeNames: ["CardRarityOddsType"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "RollWithoutChangingFutureOdds",
    parameterCount: 1,
    parameterTypeNames: ["CardRarityOddsType"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "RollWithoutChangingFutureOdds",
    parameterCount: 2,
    parameterTypeNames: ["CardRarityOddsType", "Single"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "GetBaseOdds",
    parameterCount: 2,
    parameterTypeNames: ["CardRarityOddsType", "CardRarity"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_RarityGrowth",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_RegularRareOdds",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_ShopCommonOdds",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_ShopRareOdds",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_EliteCommonOdds",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.CardRarityOdds",
    "get_EliteRareOdds",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "CreateForReward",
    parameterCount: 3,
    parameterTypeNames: ["Player", "Int32", "CardCreationOptions"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "RollForUpgrade",
    parameterCount: 4,
    parameterTypeNames: ["Player", "CardModel", "Decimal", "Rng"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Factories.CardFactory",
    "RollForUpgrade",
    parameterCount: 3,
    parameterTypeNames: ["Player", "CardModel", "Decimal"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Odds.PotionRewardOdds",
    "Roll",
    parameterCount: 3,
    parameterTypeNames: ["Player", "AscensionManager", "RoomType"]);
DumpType(asm, "MegaCrit.Sts2.Core.Rewards.RewardsSet");
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet",
    "GenerateRewardsFor",
    parameterCount: 2,
    parameterTypeNames: ["Player", "AbstractRoom"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet",
    "WithRewardsFromRoom",
    parameterCount: 1,
    parameterTypeNames: ["AbstractRoom"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rooms.AbstractRoom",
    "GenerateRewards",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rooms.CombatRoom",
    "GenerateRewards",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rooms.CombatRoom",
    "OnPlayerEnter",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rooms.CombatRoom",
    "OnPlayerExit",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rooms.CombatRoom",
    "GetRewards",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet",
    "GenerateWithoutOffering",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet",
    "Offer",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet",
    "RollForPotionAndAddTo",
    parameterCount: 3,
    parameterTypeNames: ["ICollection`1", "Player", "RoomType"]);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet+<GenerateWithoutOffering>d__20",
    "MoveNext",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RewardsSet+<Offer>d__21",
    "MoveNext",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.GoldReward",
    "Populate",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.CardReward",
    "Populate",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.RelicReward",
    "Populate",
    parameterCount: 0,
    parameterTypeNames: []);
DumpMethodBody(
    asm,
    "MegaCrit.Sts2.Core.Rewards.PotionReward",
    "Populate",
    parameterCount: 0,
    parameterTypeNames: []);

static void DumpType(Assembly asm, string fullName)
{
    var type = asm.GetType(fullName);
    Console.WriteLine($"TYPE {fullName}");
    if (type == null)
    {
        Console.WriteLine("  (missing)");
        Console.WriteLine();
        return;
    }

    foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
    {
        var parameters = string.Join(", ", ctor.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));
        Console.WriteLine($"  ctor({parameters})");
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                 .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                 .Take(80))
    {
        Console.WriteLine($"  prop {property.PropertyType.Name} {property.Name}");
    }

    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                 .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
                 .Take(120))
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));
        Console.WriteLine($"  method {method.ReturnType.Name} {method.Name}({parameters})");
    }

    Console.WriteLine();
}

static void DumpEnum(Assembly asm, string fullName)
{
    Console.WriteLine($"ENUM {fullName}");
    var type = asm.GetType(fullName);
    if (type == null || !type.IsEnum)
    {
        Console.WriteLine("  (missing)");
        Console.WriteLine();
        return;
    }

    foreach (var name in Enum.GetNames(type))
    {
        var value = Convert.ToInt64(Enum.Parse(type, name));
        Console.WriteLine($"  {name}={value}");
    }

    Console.WriteLine();
}

static void DumpMatchingTypes(Assembly asm, string shortName)
{
    Console.WriteLine($"MATCH {shortName}");
    foreach (var type in asm.GetTypes()
                 .Where(type => string.Equals(type.Name, shortName, StringComparison.OrdinalIgnoreCase) ||
                                type.FullName?.Contains(shortName, StringComparison.OrdinalIgnoreCase) == true)
                 .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase)
                 .Take(40))
    {
        Console.WriteLine($"  {type.FullName}");
    }

    Console.WriteLine();
}

static void DumpExactShortName(Assembly asm, string shortName)
{
    Console.WriteLine($"EXACT {shortName}");
    foreach (var type in asm.GetTypes()
                 .Where(type => string.Equals(type.Name, shortName, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(type => type.FullName, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"  {type.FullName}");
    }

    Console.WriteLine();
}

static void DumpMethodReferences(
    Assembly asm,
    string typeName,
    string methodName,
    int parameterCount,
    IReadOnlyList<string>? parameterTypeNames = null)
{
    Console.WriteLine($"REFS {typeName}.{methodName}({string.Join(", ", parameterTypeNames ?? [])})");
    var type = asm.GetType(typeName);
    var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        .FirstOrDefault(candidate =>
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != parameterCount)
            {
                return false;
            }

            if (parameterTypeNames == null || parameterTypeNames.Count == 0)
            {
                return true;
            }

            var parameters = candidate.GetParameters();
            if (parameters.Length != parameterTypeNames.Count)
            {
                return false;
            }

            for (var index = 0; index < parameters.Length; index++)
            {
                if (!string.Equals(parameters[index].ParameterType.Name, parameterTypeNames[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        });
    if (method == null)
    {
        Console.WriteLine("  (missing)");
        Console.WriteLine();
        return;
    }

    var body = method.GetMethodBody();
    if (body == null)
    {
        Console.WriteLine("  (no body)");
        Console.WriteLine();
        return;
    }

    var bytes = body.GetILAsByteArray();
    if (bytes == null)
    {
        Console.WriteLine("  (no il)");
        Console.WriteLine();
        return;
    }

    for (var i = 0; i <= bytes.Length - 5; i++)
    {
        var op = bytes[i];
        if (op != 0x28 && op != 0x6F && op != 0x73 && op != 0x74 && op != 0x7B && op != 0x7C)
        {
            continue;
        }

        var token = BitConverter.ToInt32(bytes, i + 1);
        try
        {
            var member = method.Module.ResolveMember(token);
            Console.WriteLine($"  {op:X2} {member.DeclaringType?.FullName}::{member.Name}");
        }
        catch
        {
        }
    }

    Console.WriteLine();
}

static void DumpMethodBody(
    Assembly asm,
    string typeName,
    string methodName,
    int parameterCount,
    IReadOnlyList<string>? parameterTypeNames = null)
{
    Console.WriteLine($"BODY {typeName}.{methodName}({string.Join(", ", parameterTypeNames ?? [])})");
    var method = FindMethod(asm, typeName, methodName, parameterCount, parameterTypeNames);
    if (method == null)
    {
        Console.WriteLine("  (missing)");
        Console.WriteLine();
        return;
    }

    var body = method.GetMethodBody();
    var bytes = body?.GetILAsByteArray();
    if (bytes == null)
    {
        Console.WriteLine("  (no il)");
        Console.WriteLine();
        return;
    }

    var oneByte = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .Where(op => op.Size == 1)
        .ToDictionary(op => (byte)op.Value);
    var twoByte = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .Where(op => op.Size == 2)
        .ToDictionary(op => (byte)(op.Value & 0xFF));

    var i = 0;
    while (i < bytes.Length)
    {
        var offset = i;
        OpCode opCode;
        if (bytes[i] == 0xFE)
        {
            opCode = twoByte[bytes[i + 1]];
            i += 2;
        }
        else
        {
            opCode = oneByte[bytes[i]];
            i += 1;
        }

        object? operand = null;
        var operandSize = 0;
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                operand = bytes[i];
                operandSize = 1;
                break;
            case OperandType.ShortInlineBrTarget:
                operand = (sbyte)bytes[i] + i + 1;
                operandSize = 1;
                break;
            case OperandType.InlineVar:
                operand = BitConverter.ToUInt16(bytes, i);
                operandSize = 2;
                break;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
                operand = BitConverter.ToInt32(bytes, i);
                operandSize = 4;
                if (opCode.OperandType == OperandType.InlineBrTarget)
                {
                    operand = (int)operand + i + 4;
                }
                break;
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                var token = BitConverter.ToInt32(bytes, i);
                operandSize = 4;
                try
                {
                    var member = method.Module.ResolveMember(token);
                    operand = member is null ? $"token:0x{token:X8}" : member;
                }
                catch
                {
                    try
                    {
                        operand = method.Module.ResolveString(token);
                    }
                    catch
                    {
                        operand = $"token:0x{token:X8}";
                    }
                }
                break;
            case OperandType.ShortInlineR:
                operand = BitConverter.ToSingle(bytes, i);
                operandSize = 4;
                break;
            case OperandType.InlineR:
                operand = BitConverter.ToDouble(bytes, i);
                operandSize = 8;
                break;
            case OperandType.InlineI8:
                operand = BitConverter.ToInt64(bytes, i);
                operandSize = 8;
                break;
            case OperandType.InlineSwitch:
                var count = BitConverter.ToInt32(bytes, i);
                operandSize = 4 + (count * 4);
                var targets = new int[count];
                for (var index = 0; index < count; index++)
                {
                    targets[index] = BitConverter.ToInt32(bytes, i + 4 + (index * 4)) + i + operandSize;
                }
                operand = string.Join(", ", targets);
                break;
        }

        i += operandSize;
        Console.WriteLine($"  IL_{offset:X4}: {opCode.Name}{(operand == null ? string.Empty : " " + FormatOperand(operand))}");
    }

    Console.WriteLine();
}

static MethodInfo? FindMethod(
    Assembly asm,
    string typeName,
    string methodName,
    int parameterCount,
    IReadOnlyList<string>? parameterTypeNames)
{
    var type = asm.GetType(typeName);
    return type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        .FirstOrDefault(candidate =>
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != parameterCount)
            {
                return false;
            }

            if (parameterTypeNames == null || parameterTypeNames.Count == 0)
            {
                return true;
            }

            var parameters = candidate.GetParameters();
            if (parameters.Length != parameterTypeNames.Count)
            {
                return false;
            }

            for (var index = 0; index < parameters.Length; index++)
            {
                if (!string.Equals(parameters[index].ParameterType.Name, parameterTypeNames[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        });
}

static string FormatOperand(object operand) =>
    operand switch
    {
        MethodBase method => $"{method.DeclaringType?.FullName}::{method.Name}",
        FieldInfo field => $"{field.DeclaringType?.FullName}::{field.Name}",
        Type type => type.FullName ?? type.Name,
        _ => operand.ToString() ?? string.Empty
    };
