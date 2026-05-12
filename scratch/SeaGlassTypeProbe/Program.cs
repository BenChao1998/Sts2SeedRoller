using System.Reflection;
using System.Reflection.Emit;

var asmPath = Path.Combine(AppContext.BaseDirectory, "sts2.dll");
var asm = Assembly.LoadFrom(asmPath);

foreach (var type in asm.GetTypes()
             .Where(type => type.FullName != null &&
                            (type.FullName.Contains("Sea", StringComparison.OrdinalIgnoreCase) ||
                             type.FullName.Contains("Glass", StringComparison.OrdinalIgnoreCase) ||
                             type.FullName.Contains("Orobas", StringComparison.OrdinalIgnoreCase))))
{
    Console.WriteLine(type.FullName);
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
    {
        Console.WriteLine($"  {method}");
    }
}

DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass", "AfterObtained");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<AfterObtained>d__15", "MoveNext");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_0");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_1");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_2");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Models.Events.Orobas", "GenerateInitialOptions");
DumpMethods(asm, "MegaCrit.Sts2.Core.Factories.CardFactory", "CreateForReward");
DumpMethods(asm, "MegaCrit.Sts2.Core.Commands.CardSelectCmd", "FromSimpleGridForRewards");
DumpMethodIl(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_0");
DumpMethodIl(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_1");
DumpMethodIl(asm, "MegaCrit.Sts2.Core.Models.Relics.SeaGlass+<>c", "<AfterObtained>b__15_2");
DumpMethods(asm, "MegaCrit.Sts2.Core.Runs.CardCreationOptions", "ForNonCombatWithUniformOdds");
DumpMethods(asm, "MegaCrit.Sts2.Core.Runs.CardCreationOptions", "WithFlags");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Runs.CardCreationOptions", "ForNonCombatWithUniformOdds");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Runs.CardCreationOptions", "WithFlags");
DumpMethodReferences(asm, "MegaCrit.Sts2.Core.Factories.CardFactory", "CreateForReward");

return;

static void DumpMethodReferences(Assembly asm, string typeName, string methodName)
{
    var type = asm.GetType(typeName);
    var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal));
    if (method == null)
    {
        Console.WriteLine($"Missing method: {typeName}.{methodName}");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"=== {typeName}.{methodName} ===");
    foreach (var reference in ReadReferencedMembers(method))
    {
        Console.WriteLine(reference);
    }
}

static void DumpMethods(Assembly asm, string typeName, string methodName)
{
    var type = asm.GetType(typeName);
    Console.WriteLine();
    Console.WriteLine($"=== methods {typeName}.{methodName} ===");
    foreach (var method in type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                 .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) ?? [])
    {
        Console.WriteLine(method);
    }
}

static void DumpMethodIl(Assembly asm, string typeName, string methodName)
{
    var type = asm.GetType(typeName);
    var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .FirstOrDefault(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal));
    if (method == null)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"=== il {typeName}.{methodName} ===");
    var body = method.GetMethodBody();
    if (body == null)
    {
        return;
    }

    var il = body.GetILAsByteArray();
    var position = 0;
    while (position < il.Length)
    {
        var offset = position;
        var opCode = ReadOpCode(il, ref position);
        object? operand = null;
        switch (opCode.OperandType)
        {
            case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            {
                var token = BitConverter.ToInt32(il, position);
                position += 4;
                try { operand = method.Module.ResolveMember(token); } catch { operand = token; }
                break;
            }
            case OperandType.InlineString:
            {
                var token = BitConverter.ToInt32(il, position);
                position += 4;
                try { operand = method.Module.ResolveString(token); } catch { operand = token; }
                break;
            }
            case OperandType.ShortInlineI:
                operand = (sbyte)il[position];
                position += 1;
                break;
            case OperandType.InlineI:
                operand = BitConverter.ToInt32(il, position);
                position += 4;
                break;
            case OperandType.InlineI8:
                operand = BitConverter.ToInt64(il, position);
                position += 8;
                break;
            case OperandType.ShortInlineVar:
                operand = il[position];
                position += 1;
                break;
            case OperandType.InlineVar:
                operand = BitConverter.ToUInt16(il, position);
                position += 2;
                break;
            case OperandType.ShortInlineBrTarget:
                operand = (sbyte)il[position];
                position += 1;
                break;
            case OperandType.InlineBrTarget:
                operand = BitConverter.ToInt32(il, position);
                position += 4;
                break;
            case OperandType.ShortInlineR:
                operand = BitConverter.ToSingle(il, position);
                position += 4;
                break;
            case OperandType.InlineR:
                operand = BitConverter.ToDouble(il, position);
                position += 8;
                break;
            case OperandType.InlineSwitch:
            {
                var count = BitConverter.ToInt32(il, position);
                position += 4 + (count * 4);
                operand = $"switch[{count}]";
                break;
            }
        }

        Console.WriteLine($"{offset:D3}: {opCode.Name}{(operand != null ? $" {operand}" : string.Empty)}");
    }
}

static IEnumerable<string> ReadReferencedMembers(MethodInfo method)
{
    var body = method.GetMethodBody();
    if (body == null)
    {
        yield break;
    }

    var module = method.Module;
    var il = body.GetILAsByteArray();
    var position = 0;
    while (position < il.Length)
    {
        var opCode = ReadOpCode(il, ref position);
        switch (opCode.OperandType)
        {
            case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            {
                var token = BitConverter.ToInt32(il, position);
                position += 4;
                MemberInfo? member = null;
                try
                {
                    member = module.ResolveMember(token);
                }
                catch
                {
                    // ignored
                }

                if (member != null)
                {
                    yield return $"{opCode.Name}: {member.DeclaringType?.FullName}.{member.Name}";
                }

                break;
            }
            case OperandType.InlineString:
            {
                var token = BitConverter.ToInt32(il, position);
                position += 4;
                string? value = null;
                try
                {
                    value = module.ResolveString(token);
                }
                catch
                {
                    // ignored
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return $"{opCode.Name}: \"{value}\"";
                }

                break;
            }
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                position += 1;
                break;
            case OperandType.InlineVar:
                position += 2;
                break;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
            case OperandType.ShortInlineR:
                position += 4;
                break;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                position += 8;
                break;
            case OperandType.InlineSwitch:
            {
                var count = BitConverter.ToInt32(il, position);
                position += 4 + (count * 4);
                break;
            }
            case OperandType.InlineNone:
            default:
                break;
        }
    }
}

static OpCode ReadOpCode(byte[] il, ref int position)
{
    var singleByteOpCodes = BuildSingleByteOpCodes();
    var multiByteOpCodes = BuildMultiByteOpCodes();
    var value = il[position++];
    if (value != 0xFE)
    {
        return singleByteOpCodes[value];
    }

    var second = il[position++];
    return multiByteOpCodes[second];
}

static OpCode[] BuildSingleByteOpCodes()
{
    var table = new OpCode[0x100];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is not OpCode opCode)
        {
            continue;
        }

        var value = (ushort)opCode.Value;
        if (value <= 0xFF)
        {
            table[value] = opCode;
        }
    }

    return table;
}

static OpCode[] BuildMultiByteOpCodes()
{
    var table = new OpCode[0x100];
    foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    {
        if (field.GetValue(null) is not OpCode opCode)
        {
            continue;
        }

        var value = (ushort)opCode.Value;
        if ((value & 0xFF00) == 0xFE00)
        {
            table[value & 0xFF] = opCode;
        }
    }

    return table;
}
