using Atlas.Core.Models;
using Atlas.Core.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Assert(ComponentNameParser.TryParse("TIROIR#V=AVANTECH#I=470#R=YOU#C=STD", out var parsed), "Le nom conforme doit être reconnu.");
Assert(parsed.Type == "TIROIR" && parsed.Variant == "AVANTECH" && parsed.Index == "470", "Les marqueurs doivent être extraits.");
Assert(parsed.Range == "YOU" && parsed.Construction == "STD", "Les marqueurs optionnels doivent être extraits.");
Assert(!ComponentNameParser.TryParse("nom_incomplet", out _), "Un nom incomplet doit rester non classé.");
Assert(ComponentNameParser.SuggestDisplayName("Accessoires#V=Poignee_Bouton#I=00#R=NR") == "Poignee Bouton", "Le nom affiché proposé doit reprendre V et remplacer les underscores.");

var demo = DemoCatalogFactory.Create();
Assert(demo.Components.Count >= 2 && demo.Furniture.Count >= 1, "Le catalogue de démonstration doit permettre l’aperçu de la V0.1.");
Assert(demo.Furniture[0].ComponentIds.All(id => demo.Components.Any(component => component.Id == id)), "La composition doit référencer des composants connus.");
Assert(demo.Furniture[0].Universes.Count > 1, "Un meuble doit pouvoir appartenir à plusieurs univers.");
Assert(demo.FurnitureFamilies.Any(family => family.Id == demo.Furniture[0].FamilyId), "Une variante doit pouvoir référencer sa famille.");

var drawerTag = new CapabilityTagRecord { Id = "cap-tiroirs", Label = "Tiroirs", DefaultFamilyNames = ["Coulissants"] };
var doorTag = new CapabilityTagRecord { Id = "cap-portes", Label = "Portes", DefaultFamilyNames = ["Charnières"] };
var testComponent = new ComponentRecord { FamilyName = "Coulissants" };
Assert(CapabilityTagStore.Resolve(testComponent, [drawerTag, doorTag]).Single().Id == drawerTag.Id, "Une capacité doit être héritée depuis la famille détectée.");
testComponent.RemovedInheritedCapabilityIds.Add(drawerTag.Id);
Assert(CapabilityTagStore.Resolve(testComponent, [drawerTag, doorTag]).Count == 0, "Une capacité héritée doit pouvoir être désactivée localement.");
testComponent.AddedCapabilityIds.Add(doorTag.Id);
Assert(CapabilityTagStore.Resolve(testComponent, [drawerTag, doorTag]).Single().Id == doorTag.Id, "Une capacité spécifique doit pouvoir être ajoutée localement.");
testComponent.AtlasFamilyNameOverride = "Charnières";
Assert(testComponent.EffectiveFamilyName == "Charnières" && testComponent.IsFamilyOverridden, "La famille Atlas doit pouvoir déroger à la famille Biblidéo.");
testComponent.UseDetectedFamily();
Assert(testComponent.EffectiveFamilyName == "Coulissants" && !testComponent.IsFamilyOverridden, "Le retour à la famille Biblidéo doit supprimer la dérogation.");

var taxonomy = new ComponentTaxonomy
{
    Families =
    [
        new ComponentFamilyRecord { Id = "fam-base", LibraryName = "_Ideo_Base", Name = "Coulissants", TagIds = ["tag-tiroir"] },
        new ComponentFamilyRecord { Id = "fam-user", LibraryName = "_Ideo_Utilisateur", Name = "Coulissants" }
    ],
    Tags = [new ComponentTagRecord { Id = "tag-tiroir", Label = "Tiroir" }]
};
var baseComponent = new ComponentRecord { Id = "base", LibraryName = "_Ideo_Base", FamilyName = "Coulissants" };
var userComponent = new ComponentRecord { Id = "user", LibraryName = "_Ideo_Utilisateur", FamilyName = "Coulissants" };
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Single().Label == "Tiroir", "Le tag de famille doit être hérité par le composant.");
Assert(ComponentTaxonomyStore.Resolve(userComponent, taxonomy).Count == 0, "Deux familles homonymes de bibliothèques différentes doivent rester distinctes.");
baseComponent.RemovedInheritedTagIds.Add("tag-tiroir");
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Count == 0, "Un tag hérité doit pouvoir être exclu sur un composant.");
baseComponent.RemovedInheritedTagIds.Clear();
var furniture = new FurnitureRecord { ComponentIds = ["base"] };
Assert(ComponentTaxonomyStore.Resolve(furniture, [baseComponent, userComponent], taxonomy).Single().Label == "Tiroir", "Le meuble doit recevoir les tags de ses composants.");
furniture.RemovedInheritedTagIds.Add("tag-tiroir");
Assert(ComponentTaxonomyStore.Resolve(furniture, [baseComponent, userComponent], taxonomy).Count == 0, "Le meuble doit pouvoir retirer un tag hérité.");

var generatedSecret = Guid.NewGuid().ToString("N");
var account = UserAccountStore.CreateAccount("test-user", "Utilisateur de test", generatedSecret, UserPermissions.Administer);
Assert(account.PasswordHash != generatedSecret && account.PasswordSalt.Length > 0, "Le secret ne doit jamais être stocké en clair.");

Console.WriteLine("Atlas.Core : contrôles métier réussis.");
