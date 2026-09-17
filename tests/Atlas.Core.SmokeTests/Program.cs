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
Assert(ComponentNameParser.SuggestDetailedDisplayName("Blum Porte Simple", "Facade#V=Blum_Porte_Simple#I=00#C=Recouvrement_Total") == "Blum Porte Simple · Recouvrement Total", "La composition doit distinguer les composants grâce à la valeur C.");
Assert(ComponentNameParser.SuggestDetailedDisplayName("Blum Porte Simple · Recouvrement Total", "Facade#V=Blum_Porte_Simple#I=00#C=Recouvrement_Total") == "Blum Porte Simple · Recouvrement Total", "La valeur C ne doit pas être ajoutée deux fois.");

var demo = DemoCatalogFactory.Create();
Assert(demo.Components.Count >= 2 && demo.Furniture.Count >= 1, "Le catalogue de démonstration doit permettre l’aperçu de la V0.1.");
Assert(demo.Furniture[0].ComponentIds.All(id => demo.Components.Any(component => component.Id == id)), "La composition doit référencer des composants connus.");
Assert(demo.Furniture[0].Universes.Count > 1, "Un meuble doit pouvoir appartenir à plusieurs univers.");
Assert(demo.UniverseDefinitions.Count >= 2 && demo.UniverseDefinitions.All(item => !string.IsNullOrWhiteSpace(item.Name)), "Les univers illustrés doivent être persistés comme des fiches administrables.");
demo.UniverseDefinitions[0].ImageRelativePath = Path.Combine("Images", "Universes", "cuisine.jpg");
Assert(demo.UniverseDefinitions[0].ImageRelativePath.Contains("Universes"), "Un univers doit pouvoir référencer une image partagée.");
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
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Count == 0, "Les tags de famille ne doivent plus être propagés automatiquement aux composants.");
Assert(ComponentTaxonomyStore.Resolve(userComponent, taxonomy).Count == 0, "Deux familles homonymes de bibliothèques différentes doivent rester distinctes.");
baseComponent.AddedTagIds.Add("tag-tiroir");
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Single().Label == "Tiroir", "Un tag doit pouvoir être ajouté directement au composant.");
var furniture = new FurnitureRecord { ComponentIds = ["base"] };
Assert(ComponentTaxonomyStore.Resolve(furniture, [baseComponent, userComponent], taxonomy).Single().Label == "Tiroir", "Le meuble doit recevoir les tags de ses composants.");
furniture.RemovedInheritedTagIds.Add("tag-tiroir");
Assert(ComponentTaxonomyStore.Resolve(furniture, [baseComponent, userComponent], taxonomy).Count == 0, "Le meuble doit pouvoir retirer un tag hérité.");
furniture.Usages.AddRange(["Four", "Micro-ondes"]);
Assert(furniture.Usages.Count == 2, "Les usages spécifiques d’un meuble doivent être cumulables.");
Assert(furniture.ConceptionDate is not null, "Une nouvelle fiche meuble doit porter une date de conception.");
taxonomy.Tags[0].Category = "Fonction";
Assert(taxonomy.Tags[0].Category == "Fonction", "Les tags doivent pouvoir être classés par catégorie.");

var searchDictionary = new[]
{
    new SearchSynonymRecord { Canonical = "sous evier", AliasesCsv = "evier, lavabo" },
    new SearchSynonymRecord { Canonical = "tiroir", AliasesCsv = "coulissant, rangement" }
};
var typoScore = CatalogSearchEngine.Score("meubel evrier",
[
    new CatalogSearchField("Meuble bas sous-évier", 1d),
    new CatalogSearchField("Cuisine", 0.6d)
], searchDictionary);
Assert(typoScore > 0.7d, "La recherche doit tolérer une inversion et une lettre parasite.");
Assert(CatalogSearchEngine.Score("lavabo", [new CatalogSearchField("Meuble sous évier")], searchDictionary) > 0.7d, "Un synonyme métier doit retrouver le concept canonique.");
Assert(CatalogSearchEngine.Score("charniere", [new CatalogSearchField("Meuble sous évier")], searchDictionary) == 0d, "Un terme sans rapport ne doit pas produire de faux résultat.");
Assert(CatalogSearchEngine.CorrectQuery("meubel evrier", ["meuble", "evier", "cuisine"], searchDictionary) == "meuble evier", "Atlas doit pouvoir expliquer la correction appliquée.");

var brandTag = new ComponentTagRecord { Id = "tag-blum", Label = "Blum", Category = "Marque" };
taxonomy.Tags.Add(brandTag);
taxonomy.Families[0].Types.Add(new ComponentTypeRecord { Name = "Coulissant", TagIds = [brandTag.Id] });
baseComponent.TypeCode = "Coulissant";
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).All(x => x.Id != brandTag.Id), "Les tags de type ne doivent plus être propagés automatiquement aux composants.");
baseComponent.AddedTagIds.Add(brandTag.Id);
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Any(x => x.Id == brandTag.Id), "Un tag de marque doit pouvoir être ajouté directement au composant.");
baseComponent.AddedTagIds.Add("tag-specifique");
taxonomy.Tags.Add(new ComponentTagRecord { Id = "tag-specifique", Label = "Legrabox", Category = "Gamme" });
Assert(ComponentTaxonomyStore.Resolve(baseComponent, taxonomy).Any(x => x.Id == "tag-specifique"), "Un tag propre doit pouvoir être ajouté à une fiche composant.");

var bulkComponentA = new ComponentRecord { Id = "bulk-a" };
var bulkComponentB = new ComponentRecord { Id = "bulk-b", AddedTagIds = ["tag-existant"] };
Assert(ComponentTaxonomyStore.ApplyDirectTags([bulkComponentA, bulkComponentB], ["tag-blum", "tag-specifique"], true) == 2, "L’affectation groupée doit modifier tous les composants sélectionnés.");
Assert(bulkComponentA.AddedTagIds.Contains("tag-blum") && bulkComponentB.AddedTagIds.Contains("tag-specifique"), "Tous les tags choisis doivent être ajoutés en masse.");
Assert(ComponentTaxonomyStore.ApplyDirectTags([bulkComponentA, bulkComponentB], ["tag-blum"], false) == 2, "Le retrait groupé doit modifier tous les composants concernés.");
Assert(!bulkComponentA.AddedTagIds.Contains("tag-blum") && !bulkComponentB.AddedTagIds.Contains("tag-blum"), "Le tag retiré ne doit rester sur aucun composant sélectionné.");

var taxonomyTestRoot = Path.Combine(Path.GetTempPath(), $"atlas-taxonomy-{Guid.NewGuid():N}");
try
{
    var persistedTaxonomy = new ComponentTaxonomy
    {
        Tags =
        [
            new ComponentTagRecord { Id = "tag-persisted", Label = "Tag persistant", Category = "Test" }
        ]
    };
    await ComponentTaxonomyStore.SaveAsync(taxonomyTestRoot, persistedTaxonomy);
    var reloadedTaxonomy = await ComponentTaxonomyStore.LoadAsync(taxonomyTestRoot);
    Assert(reloadedTaxonomy.Tags.Any(x => x.Id == "tag-persisted" && x.Label == "Tag persistant"), "Un tag enregistré doit être retrouvé après redémarrage.");
}
finally
{
    if (Directory.Exists(taxonomyTestRoot)) Directory.Delete(taxonomyTestRoot, true);
}

var generatedSecret = Guid.NewGuid().ToString("N");
var account = UserAccountStore.CreateAccount("test-user", "Utilisateur de test", generatedSecret, UserPermissions.Administer);
Assert(account.PasswordHash != generatedSecret && account.PasswordSalt.Length > 0, "Le secret ne doit jamais être stocké en clair.");

var appRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "Atlas.App");
var mainWindowXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
var catalogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "CatalogView.xaml"));
var horizonShellXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "HorizonShell.xaml"));
var themeXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Themes", "AtlasTheme.xaml"));
Assert(mainWindowXaml.Contains("WindowChrome") && !mainWindowXaml.Contains("AllowsTransparency=\"True\""), "La fenêtre principale doit conserver un chrome redimensionnable qui respecte la barre des tâches.");
Assert(mainWindowXaml.Contains("StateChanged=\"Window_StateChanged\""), "La fenêtre principale doit adapter son cadre au mode agrandi.");
Assert(catalogXaml.Contains("MiniFavoriteButton") && catalogXaml.Contains("VerticalScrollBarVisibility=\"Auto\""), "Horizon doit intégrer les favoris et conserver des zones défilantes.");
Assert(catalogXaml.Contains("MinWidth=\"340\"") && catalogXaml.Contains("MaxWidth=\"420\""), "Horizon doit protéger les dimensions du panneau produit.");
Assert(catalogXaml.Contains("ClientUniverseFacets") && catalogXaml.Contains("UniformToFill"), "Horizon doit présenter les univers comme une galerie illustrée.");
Assert(!catalogXaml.Contains("MaxWidth=\"1820\""), "Horizon doit utiliser toute la largeur disponible en plein écran.");
Assert(mainWindowXaml.Contains("NavPathIcon") && !mainWindowXaml.Contains("Text=\"⚙\""), "Le menu principal doit utiliser des icônes vectorielles cohérentes.");
Assert(mainWindowXaml.Contains("HorizonShell") && horizonShellXaml.Contains("Retour à la Forge"), "Horizon doit disposer d’un shell client autonome sans supprimer l’accès à la Forge.");
Assert(catalogXaml.IndexOf("<!-- Fiche produit -->", StringComparison.Ordinal) > catalogXaml.IndexOf("<!-- Collection -->", StringComparison.Ordinal), "La collection et la fiche produit doivent partager la hauteur principale d’Horizon.");
var settingsXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "SettingsView.xaml"));
Assert(settingsXaml.Contains("ChooseUniverseImage_OnClick") && settingsXaml.Contains("Enregistrer les univers"), "Les paramètres doivent permettre d’associer et d’enregistrer une image à chaque univers.");
Assert(themeXaml.Contains("BasedOn=\"{StaticResource {x:Type TextBlock}}\""), "Les titres explicites doivent hériter de la couleur de texte du thème sombre.");

Console.WriteLine("Atlas.Core : contrôles métier réussis.");
