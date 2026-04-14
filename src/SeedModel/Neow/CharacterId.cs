using System;

namespace SeedModel.Neow;

public enum CharacterId
{
    Ironclad,
    Silent,
    Defect,
    Necrobinder,
    Regent
}

public static class CharacterIdExtensions
{
    public static bool TryParse(string? value, out CharacterId id)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, ignoreCase: true, out id))
        {
            return true;
        }

        id = CharacterId.Ironclad;
        return false;
    }
}
