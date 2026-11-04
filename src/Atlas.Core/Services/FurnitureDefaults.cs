using Atlas.Core.Models;

namespace Atlas.Core.Services;

public static class FurnitureDefaults
{
    public static bool NormalizeStructure(FurnitureRecord furniture, AtlasReferenceData referenceData)
    {
        var changed = false;
        changed |= SetWhenEmpty(furniture.PrincipleConstruction,
            value => furniture.PrincipleConstruction = value,
            Resolve(referenceData.ConstructionPrinciples, "Non applicable"));
        changed |= SetWhenEmpty(furniture.PositionDos,
            value => furniture.PositionDos = value,
            Resolve(referenceData.BackPositions, "Non applicable"));
        changed |= SetWhenEmpty(furniture.TypeAssemblage,
            value => furniture.TypeAssemblage = value,
            Resolve(referenceData.AssemblyTypes, "Sans assemblage"));
        changed |= SetWhenEmpty(furniture.Forme,
            value => furniture.Forme = value,
            Resolve(referenceData.FurnitureForms, "Droit"));
        return changed;
    }

    private static bool SetWhenEmpty(string? current, Action<string> setter, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(current)) return false;
        setter(fallback);
        return true;
    }

    private static string Resolve(IEnumerable<string>? values, string preferred)
    {
        var available = (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        return available.FirstOrDefault(value => value.Equals(preferred, StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault()
               ?? preferred;
    }
}
