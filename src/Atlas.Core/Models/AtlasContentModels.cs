namespace Atlas.Core.Models;

[Flags]
public enum AtlasContentModules
{
    None = 0,
    Catalog = 1,
    SearchDictionary = 2,
    Taxonomy = 4,
    ReferenceData = 8,
    WorkspaceAssets = 16,
    FurnitureAssets = 32,
    All = Catalog | SearchDictionary | Taxonomy | ReferenceData | WorkspaceAssets | FurnitureAssets
}

public sealed class AtlasReferenceData
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> FurnitureForms { get; set; } = ["Droit", "Angle", "Courbe", "Trapèze", "Pan coupé", "Sous rampant"];
    public List<string> ConstructionPrinciples { get; set; } = ["Non applicable", "Montant filant", "Traverse filante"];
    public List<string> BackPositions { get; set; } = ["Non applicable", "Sans dos", "Appliqué", "Rainuré", "Intérieur"];
    public List<string> AssemblyTypes { get; set; } = ["Sans assemblage", "Vis", "Tourillons", "Tourillons + vis", "Excentrique", "Tourillons + excentriques", "Clamex", "Cabineo", "Vis auto-tourillonnante"];
    public List<string> TagCategories { get; set; } = ["Fonction", "Marque", "Gamme", "Implantation", "Technologie", "Système", "Autre"];
    public List<string> DoorTypes { get; set; } = ["Applique", "Semi-applique", "Encastrée"];
    public List<string> DrawerTypes { get; set; } = ["Applique", "Encastré"];
    public List<string> SearchConceptCategories { get; set; } = ["Éléments", "Usages", "Types", "Formes", "Construction", "Assemblages", "Tags", "Expressions", "Autre"];
}

public sealed class AtlasContentManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackageId { get; set; } = Guid.NewGuid().ToString("N");
    public string ContentVersion { get; set; } = string.Empty;
    public string MinimumApplicationVersion { get; set; } = "0.18.0";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Publisher { get; set; } = "Idéo Solutions";
    public AtlasContentModules Modules { get; set; } = AtlasContentModules.All;
    public Dictionary<string, string> Sha256 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AtlasInstalledContentState
{
    public int SchemaVersion { get; set; } = 1;
    public string ContentVersion { get; set; } = "Socle initial";
    public string PackageId { get; set; } = string.Empty;
    public DateTimeOffset? InstalledUtc { get; set; }
    public AtlasContentModules Modules { get; set; }
}

public sealed class CatalogSiteOverlay
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ModifiedBy { get; set; } = string.Empty;
    public WorkspaceSettings Settings { get; set; } = new();

    public List<ComponentRecord> Components { get; set; } = [];
    public List<string> RemovedComponentIds { get; set; } = [];
    public List<CapabilityTagRecord> CapabilityTags { get; set; } = [];
    public List<string> RemovedCapabilityTagIds { get; set; } = [];
    public List<FurnitureRecord> Furniture { get; set; } = [];
    public List<string> RemovedFurnitureIds { get; set; } = [];
    public List<FurnitureFamilyRecord> FurnitureFamilies { get; set; } = [];
    public List<string> RemovedFurnitureFamilyIds { get; set; } = [];
    public List<CatalogUniverseRecord> UniverseDefinitions { get; set; } = [];
    public List<string> RemovedUniverseIds { get; set; } = [];
    public List<string> AddedFurnitureTypes { get; set; } = [];
    public List<string> RemovedFurnitureTypes { get; set; } = [];
    public List<string> AddedFurnitureUsages { get; set; } = [];
    public List<string> RemovedFurnitureUsages { get; set; } = [];
}

public sealed class SearchDictionarySiteOverlay
{
    public int SchemaVersion { get; set; } = 1;
    public List<SearchConceptRecord> Concepts { get; set; } = [];
    public List<SearchSynonymEntry> Synonyms { get; set; } = [];
    public List<SearchExpressionRecord> Expressions { get; set; } = [];
    public List<SearchIgnoredTermRecord> IgnoredTerms { get; set; } = [];
    public List<SearchUnknownQueryRecord> UnknownQueries { get; set; } = [];
    public Dictionary<string, bool> OfficialActiveStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> OfficialConceptCustomAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ComponentTaxonomySiteOverlay
{
    public int SchemaVersion { get; set; } = 1;
    public List<ComponentFamilyRecord> Families { get; set; } = [];
    public List<string> RemovedFamilyIds { get; set; } = [];
    public List<ComponentTagRecord> Tags { get; set; } = [];
    public List<string> RemovedTagIds { get; set; } = [];
}
