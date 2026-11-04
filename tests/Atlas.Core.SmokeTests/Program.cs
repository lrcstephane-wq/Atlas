using Atlas.Core.Models;
using Atlas.Core.Services;
using System.Security.Cryptography;
using System.Text.Json;

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

var deploymentPolicyPath = Path.Combine(Path.GetTempPath(), $"atlas-deployment-{Guid.NewGuid():N}.json");
Assert(AtlasDeploymentPolicyStore.Load(deploymentPolicyPath).Mode == AtlasDeploymentMode.Production,
    "Une installation sans politique explicite doit rester en Production et ne jamais se mettre à jour automatiquement.");
AtlasDeploymentPolicyStore.Save(new AtlasDeploymentPolicy { Mode = AtlasDeploymentMode.Development }, deploymentPolicyPath);
Assert(AtlasDeploymentPolicyStore.Load(deploymentPolicyPath).AllowsAutomaticApplicationUpdates,
    "Le mode Développement Idéo doit être le seul à autoriser les mises à jour automatiques.");
File.WriteAllText(deploymentPolicyPath, "{\"SchemaVersion\":1,\"Mode\":\"development\"}");
Assert(AtlasDeploymentPolicyStore.Load(deploymentPolicyPath).Mode == AtlasDeploymentMode.Development,
    "La politique écrite par l’installateur Windows doit être comprise par Atlas.");
File.Delete(deploymentPolicyPath);

var demo = DemoCatalogFactory.Create();
Assert(demo.Components.Count >= 2 && demo.Furniture.Count >= 1, "Le catalogue de démonstration doit permettre l’aperçu de la V0.1.");
Assert(demo.Furniture[0].ComponentIds.All(id => demo.Components.Any(component => component.Id == id)), "La composition doit référencer des composants connus.");
var legacyBlankFurniture = new FurnitureRecord
{
    PrincipleConstruction = " ",
    PositionDos = "",
    TypeAssemblage = "",
    Forme = ""
};
Assert(FurnitureDefaults.NormalizeStructure(legacyBlankFurniture, new AtlasReferenceData())
       && legacyBlankFurniture.PrincipleConstruction == "Non applicable"
       && legacyBlankFurniture.PositionDos == "Non applicable"
       && legacyBlankFurniture.TypeAssemblage == "Sans assemblage"
       && legacyBlankFurniture.Forme == "Droit",
    "Une ancienne fiche dont les listes affichaient une valeur non enregistrée doit être régularisée automatiquement.");
var duplicatedStructureFurniture = new FurnitureRecord
{
    PrincipleConstruction = legacyBlankFurniture.PrincipleConstruction,
    PositionDos = legacyBlankFurniture.PositionDos,
    TypeAssemblage = legacyBlankFurniture.TypeAssemblage,
    Forme = legacyBlankFurniture.Forme
};
Assert(!FurnitureDefaults.NormalizeStructure(duplicatedStructureFurniture, new AtlasReferenceData())
       && duplicatedStructureFurniture.TypeAssemblage == "Sans assemblage",
    "Une duplication doit conserver les valeurs de structure réellement initialisées.");
Assert(demo.Furniture[0].Universes.Count > 1, "Un meuble doit pouvoir appartenir à plusieurs univers.");
Assert(demo.UniverseDefinitions.Count >= 2 && demo.UniverseDefinitions.All(item => !string.IsNullOrWhiteSpace(item.Name)), "Les univers illustrés doivent être persistés comme des fiches administrables.");
demo.UniverseDefinitions[0].ImageRelativePath = Path.Combine("Images", "Universes", "cuisine.jpg");
Assert(demo.UniverseDefinitions[0].ImageRelativePath.Contains("Universes"), "Un univers doit pouvoir référencer une image partagée.");
Assert(demo.FurnitureFamilies.Any(family => family.Id == demo.Furniture[0].FamilyId), "Une variante doit pouvoir référencer sa famille.");
demo.FurnitureTypes.Add("Meuble test");
demo.FurnitureUsages.Add("Usage test");
Assert(demo.FurnitureTypes.Contains("Meuble test") && demo.FurnitureUsages.Contains("Usage test"), "Les types de meubles et usages spécifiques doivent être persistables dans le catalogue.");

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
var liveSearchDocument = CatalogSearchEngine.Prepare([new CatalogSearchField("Meuble avec tiroirs et vasque", 1d)]);
Assert(CatalogSearchEngine.Score("vas", liveSearchDocument) > 0.8d,
    "Le dernier mot incomplet doit produire des résultats dès trois caractères.");
Assert(CatalogSearchEngine.Score("iroir", liveSearchDocument) > 0.7d,
    "Une lettre initiale oubliée doit rester reconnue comme une faute probable.");
Assert(CatalogSearchEngine.Score("miroir", liveSearchDocument, protectedTerms: SearchDictionaryService.ProtectedTerms) == 0d,
    "Un mot français protégé comme miroir ne doit jamais être transformé en tiroir.");
var falsePrefixDocument = CatalogSearchEngine.Prepare([new CatalogSearchField("Fourniture avec hayon", 1d)]);
Assert(CatalogSearchEngine.Score("four", falsePrefixDocument, protectedTerms: SearchDictionaryService.ProtectedTerms) == 0d
       && CatalogSearchEngine.Score("rayon", falsePrefixDocument, protectedTerms: SearchDictionaryService.ProtectedTerms) == 0d,
    "Un mot métier complet ne doit pas correspondre au début d’un autre mot comme fourniture ou à un terme voisin comme hayon.");
Assert(CatalogSearchEngine.ContainsExactPhrase("hayon", falsePrefixDocument)
       && !CatalogSearchEngine.ContainsExactPhrase("rayon", falsePrefixDocument)
       && !CatalogSearchEngine.ContainsExactPhrase("four", falsePrefixDocument),
    "La validation d’un concept reconnu doit utiliser des mots ou expressions entiers, sans tolérance approximative.");
Assert(CatalogSearchEngine.MatchesAnyNormalized(["Sans assemblage"], ["Sans assemblage\u00a0"]),
    "Une facette doit accepter les espaces invisibles hérités d’une ancienne saisie.");
Assert(CatalogSearchEngine.MatchesAnyNormalized(["Micro-ondes"], ["micro ondes"]),
    "Une facette doit comparer les libellés métier sous leur forme normalisée.");
Assert(!CatalogSearchEngine.MatchesAnyNormalized(["Sans assemblage"], ["Tourillons + vis"]),
    "La normalisation d’une facette ne doit pas créer de faux positif.");
Assert(SearchDictionaryService.EffectiveQuantity(false, 12) == 0
       && SearchDictionaryService.EffectiveQuantity(true, 0) == 1
       && SearchDictionaryService.EffectiveQuantity(true, 4) == 4,
    "La quantité recherchable doit dépendre de la présence réelle : une ancienne quantité ne doit pas ressusciter un élément décoché.");

var structuredDictionary = new SearchDictionaryDocument();
SearchDictionaryService.MergeOfficial(structuredDictionary, searchDictionary);
Assert(structuredDictionary.OfficialVersion == SearchDictionaryService.OfficialVersion && structuredDictionary.SchemaVersion == 2
       && structuredDictionary.Concepts.Count >= 90 && structuredDictionary.Synonyms.Count >= 120
       && structuredDictionary.Expressions.Count >= 40 && structuredDictionary.IgnoredTerms.Count >= 40,
    "Le socle officiel du dictionnaire doit être versionné et suffisamment riche.");
var liveSinkIntent = SearchDictionaryService.Interpret("vas", structuredDictionary,
    protectedTerms: SearchDictionaryService.ProtectedTerms, liveTyping: true);
Assert(liveSinkIntent.Constraints.Any(item => item.Target == SearchConceptTarget.FurnitureUsage && item.Value == "Vasque"),
    "La saisie dynamique vas doit déjà reconnaître le concept Vasque.");
var missingInitialIntent = SearchDictionaryService.Interpret("iroir", structuredDictionary,
    protectedTerms: SearchDictionaryService.ProtectedTerms, liveTyping: true);
Assert(missingInitialIntent.Constraints.Any(item => item.Target == SearchConceptTarget.DrawerCount),
    "La saisie iroir doit être corrigée vers le concept Tiroir.");
var protectedMirrorIntent = SearchDictionaryService.Interpret("miroir", structuredDictionary,
    protectedTerms: SearchDictionaryService.ProtectedTerms, liveTyping: true);
Assert(protectedMirrorIntent.Constraints.All(item => item.Target != SearchConceptTarget.DrawerCount)
       && protectedMirrorIntent.Constraints.Any(item => item.Target == SearchConceptTarget.GenericText && item.Value == "miroir")
       && protectedMirrorIntent.CorrectedQuery == "miroir",
    "Miroir doit rester un mot distinct et ne jamais déclencher les tiroirs.");
var protectedMirrorPrefixIntent = SearchDictionaryService.Interpret("miroi", structuredDictionary,
    protectedTerms: SearchDictionaryService.ProtectedTerms, liveTyping: true);
Assert(protectedMirrorPrefixIntent.Constraints.All(item => item.Target != SearchConceptTarget.DrawerCount)
       && protectedMirrorPrefixIntent.CorrectedQuery == "miroir"
       && protectedMirrorPrefixIntent.Constraints.Any(item => item.Target == SearchConceptTarget.GenericText && item.Value == "miroir")
       && CatalogSearchEngine.Score("miroir", liveSearchDocument,
           protectedTerms: SearchDictionaryService.ProtectedTerms) == 0d,
    "Le préfixe miroi doit rester rattaché à miroir et ne produire aucun faux résultat tiroir.");
var shelfIntent = SearchDictionaryService.Interpret("rayon", structuredDictionary,
    protectedTerms: SearchDictionaryService.ProtectedTerms, liveTyping: true);
Assert(shelfIntent.Constraints.Any(item => item.Target == SearchConceptTarget.ShelfCount && item.Mode == SearchConstraintMode.Minimum),
    "Rayon doit produire un critère obligatoire exigeant au moins un rayon.");
var sinkDoorIntent = SearchDictionaryService.Interpret("meuble porte sous evier", structuredDictionary);
Assert(sinkDoorIntent.Constraints.Any(item => item.Target == SearchConceptTarget.DoorCount && item.Mode == SearchConstraintMode.Minimum && item.Quantity == 1),
    "Une porte sans quantité doit signifier au moins une porte.");
Assert(sinkDoorIntent.Constraints.Any(item => item.Target == SearchConceptTarget.FurnitureUsage && item.Value == "Sous-évier"),
    "Sous évier doit être traduit en usage métier obligatoire.");
var storageIntent = SearchDictionaryService.Interpret("armoire de ragement avec penderie", structuredDictionary);
Assert(storageIntent.CorrectedQuery.Contains("rangement") && storageIntent.Constraints.Any(item => item.Target == SearchConceptTarget.ShelfCount)
       && storageIntent.Constraints.Any(item => item.Target == SearchConceptTarget.Hanging),
    "Une formulation régionale avec faute doit reconnaître rayons et penderie.");
var twoDoorsIntent = SearchDictionaryService.Interpret("meuble bas 2 portes", structuredDictionary);
Assert(twoDoorsIntent.Constraints.Any(item => item.Target == SearchConceptTarget.DoorCount && item.Mode == SearchConstraintMode.Exact && item.Quantity == 2),
    "Une quantité explicite doit être interprétée comme une quantité exacte.");
var hettichIntent = SearchDictionaryService.Interpret("je veux un meuble bas 4 tiroirs mais que du hettich", structuredDictionary, ["Hettich"]);
Assert(hettichIntent.Constraints.Any(item => item.Target == SearchConceptTarget.DrawerCount && item.Quantity == 4),
    "La quantité de tiroirs doit rester exacte dans une demande avec une marque.");
Assert(hettichIntent.Constraints.Any(item => item.Target == SearchConceptTarget.FurnitureTag && item.Value == "Hettich"),
    "Une marque active doit devenir un tag obligatoire à côté de la quantité demandée.");
var applianceIntent = SearchDictionaryService.Interpret("me faut un meuble micro onde avec un relevant au dessus sotn", structuredDictionary);
Assert(applianceIntent.Constraints.Any(item => item.Target == SearchConceptTarget.FurnitureUsage && item.Value == "Micro-ondes")
       && applianceIntent.Constraints.Any(item => item.Target == SearchConceptTarget.LiftUpCount),
    "Micro-ondes et hayon/relevant doivent rester compréhensibles dans une phrase naturelle bruitée.");
var liftIntent = SearchDictionaryService.Interpret("meuble haut avec porte relevable", structuredDictionary);
Assert(liftIntent.Constraints.Any(item => item.Target == SearchConceptTarget.LiftUpCount)
       && liftIntent.Constraints.All(item => item.Target != SearchConceptTarget.DoorCount),
    "Une expression longue doit primer : porte relevable ne doit pas compter une porte battante.");
var excludedBrandIntent = SearchDictionaryService.Interpret("tiroirs Blum mais pas Hettich", structuredDictionary, ["Blum", "Hettich"]);
Assert(excludedBrandIntent.Constraints.Any(item => item.Target == SearchConceptTarget.FurnitureTag && item.Value == "Hettich" && item.Mode == SearchConstraintMode.Exclude),
    "Une négation doit pouvoir exclure une marque ou un tag.");
var dictionaryTestRoot = Path.Combine(Path.GetTempPath(), $"atlas-dictionary-{Guid.NewGuid():N}");
try
{
    var dictionaryStore = new SearchDictionaryStore(dictionaryTestRoot);
    var dictionary = await dictionaryStore.LoadAsync();
    var shelfConcept = dictionary.Concepts.Single(item => item.Id == "element-rayon");
    shelfConcept.CustomAliasesCsv = "claie, planche réglable";
    dictionary.Synonyms.Add(new SearchSynonymEntry { ConceptId = shelfConcept.Id, Term = "rayonnette", IsOfficial = false });
    dictionary.Expressions.Add(new SearchExpressionRecord { Phrase = "rangement maison", ConceptIdsCsv = shelfConcept.Id, IsOfficial = false });
    dictionary.IgnoredTerms.Add(new SearchIgnoredTermRecord { Term = "vite fait", IsOfficial = false });
    dictionary.Concepts.Add(new SearchConceptRecord { Id = "client-perso", Label = "Meuble spécial", CustomAliasesCsv = "modèle Michel", IsOfficial = false });
    await dictionaryStore.SaveAsync(dictionary);
    var reloadedDictionary = await dictionaryStore.LoadAsync();
    Assert(reloadedDictionary.Concepts.Single(item => item.Id == "element-rayon").CustomAliasesCsv.Contains("claie")
           && reloadedDictionary.Concepts.Any(item => item.Id == "client-perso")
           && reloadedDictionary.Synonyms.Any(item => item.Term == "rayonnette")
           && reloadedDictionary.Expressions.Any(item => item.Phrase == "rangement maison")
           && reloadedDictionary.IgnoredTerms.Any(item => item.Term == "vite fait"),
        "Une mise à jour du socle officiel ne doit jamais écraser le vocabulaire ni les concepts du client.");
}
finally
{
    if (Directory.Exists(dictionaryTestRoot)) Directory.Delete(dictionaryTestRoot, true);
}

var legacyLayersRoot = Path.Combine(Path.GetTempPath(), $"atlas-legacy-layers-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(Path.Combine(legacyLayersRoot, "Data"));
    Directory.CreateDirectory(Path.Combine(legacyLayersRoot, "Configuration"));
    var legacyCatalog = DemoCatalogFactory.Create();
    legacyCatalog.Revision = 42;
    legacyCatalog.Settings.LibraryRoot = @"Z:\Bibliotheque historique";
    legacyCatalog.Furniture.Add(new FurnitureRecord { Id = "legacy-furniture", Reference = "000000777", DisplayName = "Meuble historique" });
    await File.WriteAllTextAsync(Path.Combine(legacyLayersRoot, "Data", "catalog.atlas.json"), JsonSerializer.Serialize(legacyCatalog));
    var migratedCatalogStore = new SharedCatalogStore(legacyLayersRoot);
    var migratedCatalog = await migratedCatalogStore.LoadAsync();
    Assert(migratedCatalog.Revision == 42 && migratedCatalog.Settings.LibraryRoot.Contains("historique")
           && migratedCatalog.Furniture.Any(item => item.Id == "legacy-furniture")
           && File.Exists(migratedCatalogStore.OfficialCatalogPath) && File.Exists(migratedCatalogStore.CatalogPath),
        "Le catalogue d’une installation existante doit être réparti automatiquement sans perdre ses meubles, sa révision ni ses chemins.");

    var legacyDictionary = new SearchDictionaryDocument();
    SearchDictionaryService.MergeOfficial(legacyDictionary);
    legacyDictionary.Synonyms.Add(new SearchSynonymEntry { Id = "legacy-client-synonym", ConceptId = "element-rayon", Term = "tablette historique", IsOfficial = false });
    await File.WriteAllTextAsync(Path.Combine(legacyLayersRoot, "Configuration", "search-dictionary.atlas.json"), JsonSerializer.Serialize(legacyDictionary));
    var migratedDictionaryStore = new SearchDictionaryStore(legacyLayersRoot);
    var migratedDictionary = await migratedDictionaryStore.LoadAsync();
    Assert(migratedDictionary.Synonyms.Any(item => item.Id == "legacy-client-synonym" && !item.IsOfficial)
           && File.Exists(migratedDictionaryStore.FilePath),
        "Les ajouts au dictionnaire d’un site existant doivent migrer dans la couche du site.");

    var legacyTaxonomy = new ComponentTaxonomy { Tags = [new ComponentTagRecord { Id = "legacy-tag", Label = "Tag historique" }] };
    await File.WriteAllTextAsync(Path.Combine(legacyLayersRoot, "Configuration", "component-taxonomy.json"), JsonSerializer.Serialize(legacyTaxonomy));
    var migratedTaxonomy = await ComponentTaxonomyStore.LoadAsync(legacyLayersRoot);
    Assert(migratedTaxonomy.Tags.Any(item => item.Id == "legacy-tag")
           && File.Exists(ComponentTaxonomyStore.GetOfficialPath(legacyLayersRoot)),
        "La taxonomie existante doit devenir le premier socle officiel sans perdre ses tags.");
    Assert(Directory.EnumerateFiles(Path.Combine(legacyLayersRoot, "Backups"), "*before-content-layers*").Count() >= 3,
        "Chaque ancien fichier métier doit être sauvegardé avant sa migration vers les nouvelles couches.");
}
finally
{
    if (Directory.Exists(legacyLayersRoot)) Directory.Delete(legacyLayersRoot, true);
}

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

var contentTestRoot = Path.Combine(Path.GetTempPath(), $"atlas-content-{Guid.NewGuid():N}");
try
{
    var authorRoot = Path.Combine(contentTestRoot, "author");
    var clientRoot = Path.Combine(contentTestRoot, "client");
    var authorFurniture = Path.Combine(contentTestRoot, "author-furniture");
    var clientFurniture = Path.Combine(contentTestRoot, "client-furniture");
    Directory.CreateDirectory(authorFurniture);
    Directory.CreateDirectory(clientFurniture);
    await File.WriteAllTextAsync(Path.Combine(authorFurniture, "000000999.top"), "opaque-top");
    await File.WriteAllTextAsync(Path.Combine(authorFurniture, "000000999.top.png"), "preview");

    var clientCatalogStore = new SharedCatalogStore(clientRoot);
    var clientCatalog = await clientCatalogStore.LoadAsync();
    clientCatalog.Settings.FurnitureRoot = clientFurniture;
    clientCatalog.Furniture.Add(new FurnitureRecord { Id = "client-site-furniture", Reference = "USR-TEST", DisplayName = "Meuble propre au site" });
    await clientCatalogStore.SaveAsync(clientCatalog, clientCatalog.Revision, "Client");
    var updatedClientFurniture = Path.Combine(contentTestRoot, "client-furniture-updated");
    await clientCatalogStore.SaveFurnitureRootAsync(clientCatalog, updatedClientFurniture, "Gestionnaire du site");
    var catalogAfterFurnitureRootUpdate = await clientCatalogStore.LoadAsync();
    Assert(catalogAfterFurnitureRootUpdate.Settings.FurnitureRoot == updatedClientFurniture
           && catalogAfterFurnitureRootUpdate.Furniture.Any(item => item.Id == "client-site-furniture"),
        "Le Gestionnaire du site doit pouvoir modifier uniquement le dossier commun sans altérer le catalogue.");
    clientFurniture = updatedClientFurniture;
    var clientDictionaryStore = new SearchDictionaryStore(clientRoot);
    var clientDictionary = await clientDictionaryStore.LoadAsync();
    clientDictionary.Synonyms.Add(new SearchSynonymEntry { Id = "client-synonym", ConceptId = "element-rayon", Term = "planche maison", IsOfficial = false });
    await clientDictionaryStore.SaveAsync(clientDictionary);
    var clientTaxonomy = await ComponentTaxonomyStore.LoadAsync(clientRoot);
    clientTaxonomy.Tags.Add(new ComponentTagRecord { Id = "client-tag", Label = "Tag du site" });
    await ComponentTaxonomyStore.SaveAsync(clientRoot, clientTaxonomy);

    var officialCatalog = DemoCatalogFactory.Create();
    officialCatalog.Furniture.Add(new FurnitureRecord
    {
        Id = "official-pack-furniture", Reference = "000000999", DisplayName = "Meuble du pack",
        SourceRelativePath = "000000999.top", ImageRelativePath = "000000999.top.png", Status = RecordStatus.Publiee
    });
    var officialDictionary = new SearchDictionaryDocument();
    SearchDictionaryService.MergeOfficial(officialDictionary);
    officialDictionary.Synonyms.Add(new SearchSynonymEntry { Id = "official-pack-synonym", ConceptId = "element-rayon", Term = "tablette de pack" });
    var officialTaxonomy = new ComponentTaxonomy { Tags = [new ComponentTagRecord { Id = "official-tag", Label = "Tag Atlas" }] };

    using var contentSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var packagePath = Path.Combine(contentTestRoot, "Contenus-Atlas-test.atlaspack");
    var authorPackageService = new AtlasContentPackageService(authorRoot, contentSigningKey.ExportSubjectPublicKeyInfoPem());
    await authorPackageService.CreateAsync(packagePath, "2026.11-test", "0.18.0", officialCatalog, officialDictionary, officialTaxonomy, new AtlasReferenceData(),
        authorRoot, authorFurniture, contentSigningKey.ExportECPrivateKeyPem());
    var packageInfo = await authorPackageService.InspectAsync(packagePath);
    Assert(packageInfo.ContentVersion == "2026.11-test" && packageInfo.FurnitureCount == officialCatalog.Furniture.Count,
        "Un pack signé doit être contrôlable avant son installation.");

    var clientPackageService = new AtlasContentPackageService(clientRoot, contentSigningKey.ExportSubjectPublicKeyInfoPem());
    await clientPackageService.InstallAsync(packagePath, clientFurniture, new Version(0, 18, 0));
    var mergedCatalog = await clientCatalogStore.LoadAsync();
    var mergedDictionary = await clientDictionaryStore.LoadAsync();
    var mergedTaxonomy = await ComponentTaxonomyStore.LoadAsync(clientRoot);
    Assert(mergedCatalog.Furniture.Any(item => item.Id == "official-pack-furniture") && mergedCatalog.Furniture.Any(item => item.Id == "client-site-furniture"),
        "Une mise à jour officielle doit ajouter ses meubles sans supprimer les adaptations du site.");
    Assert(mergedDictionary.Synonyms.Any(item => item.Id == "official-pack-synonym" && item.IsOfficial)
           && mergedDictionary.Synonyms.Any(item => item.Id == "client-synonym" && !item.IsOfficial),
        "Le dictionnaire Atlas et le vocabulaire du site doivent rester dans deux couches distinctes.");
    Assert(mergedTaxonomy.Tags.Any(item => item.Id == "official-tag") && mergedTaxonomy.Tags.Any(item => item.Id == "client-tag"),
        "Les Tags Atlas d’un pack ne doivent pas écraser les tags propres au site.");
    Assert(File.Exists(Path.Combine(clientFurniture, "000000999.top")) && File.Exists(Path.Combine(clientFurniture, "000000999.top.png")),
        "Les fichiers TopSolid et leurs aperçus doivent rejoindre le dossier de meubles configuré chez le client.");
    Assert((await clientPackageService.ReadInstalledStateAsync()).ContentVersion == "2026.11-test",
        "Atlas doit mémoriser la version de contenu installée indépendamment de la version applicative.");

    var invalidSignatureRejected = false;
    using (var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
    {
        try { await new AtlasContentPackageService(clientRoot, otherKey.ExportSubjectPublicKeyInfoPem()).InspectAsync(packagePath); }
        catch (CryptographicException) { invalidSignatureRejected = true; }
    }
    Assert(invalidSignatureRejected, "Un pack qui n’est pas signé par Idéo doit être refusé.");
}
finally
{
    if (Directory.Exists(contentTestRoot)) Directory.Delete(contentTestRoot, true);
}

var generatedSecret = Guid.NewGuid().ToString("N");
var account = UserAccountStore.CreateAccount("test-user", "Utilisateur de test", generatedSecret, UserPermissions.Administer);
Assert(account.PasswordHash != generatedSecret && account.PasswordSalt.Length > 0, "Le secret ne doit jamais être stocké en clair.");
var creatorAccount = UserAccountStore.CreateAccount("creator", "Créateur de test", generatedSecret, UserPermissions.Read | UserPermissions.ManageOwnFurniture);
Assert(creatorAccount.IsCreator && !creatorAccount.IsAdministrator && !creatorAccount.CanEdit, "Le Créateur doit gérer ses meubles sans obtenir les droits de la Forge.");

var workspaceTestRoot = Path.Combine(Path.GetTempPath(), $"atlas-workspace-{Guid.NewGuid():N}");
try
{
    var sourceWorkspace = Path.Combine(workspaceTestRoot, "source");
    var targetWorkspace = Path.Combine(workspaceTestRoot, "target");
    var manifest = await AtlasWorkspaceService.EnsureAsync(sourceWorkspace, "Site test");
    var accountStore = new UserAccountStore(sourceWorkspace);
    await accountStore.AddAsync("admin", "Administrateur", generatedSecret,
        UserPermissions.Edit | UserPermissions.Validate | UserPermissions.Administer | UserPermissions.IdeoAdministrator);
    var migratedManager = (await accountStore.LoadAsync()).Single();
    Assert(migratedManager.IsSiteManager && !migratedManager.IsAdministrator && !migratedManager.CanEdit,
        "Un ancien administrateur local doit devenir Gestionnaire du site sans conserver la Forge ni le contournement de licence.");
    await AtlasWorkspaceService.CopyAndVerifyAsync(sourceWorkspace, targetWorkspace);
    Assert(AtlasWorkspaceService.IsWorkspace(targetWorkspace) && File.Exists(Path.Combine(targetWorkspace, "Configuration", "users.atlas.json")), "Le déplacement doit conserver l’identité de l’espace et ses comptes.");
}
finally
{
    if (Directory.Exists(workspaceTestRoot)) Directory.Delete(workspaceTestRoot, true);
}

var customerTestRoot = Path.Combine(Path.GetTempPath(), $"atlas-customer-{Guid.NewGuid():N}");
try
{
    var shared = Path.Combine(customerTestRoot, "shared");
    var furnitureRoot = Path.Combine(customerTestRoot, "furniture");
    Directory.CreateDirectory(furnitureRoot);
    await File.WriteAllTextAsync(Path.Combine(furnitureRoot, "Meuble_Michel.top"), "opaque");
    await File.WriteAllTextAsync(Path.Combine(furnitureRoot, "Meuble_Michel.top.png"), "preview");
    var customerStore = new CustomerFurnitureStore(shared);
    var customerCatalog = new CustomerFurnitureCatalog { FurnitureRoot = furnitureRoot };
    Assert(customerStore.Synchronize(customerCatalog) == 1 && customerCatalog.Furniture.Count == 1, "Un .TOP déposé doit rejoindre le catalogue client séparé.");
    Assert(customerCatalog.Furniture[0].ImageRelativePath.EndsWith(".top.png", StringComparison.OrdinalIgnoreCase), "L’aperçu voisin du .TOP doit être détecté automatiquement.");
    customerCatalog.ClientUniverses.Add(new PersonalTagRecord { Id = "sur-mesure", Label = "Sur mesure" });
    customerCatalog.ClientTags.Add(new PersonalTagRecord { Id = "michel", Label = "Modèle Michel" });
    customerCatalog.Furniture[0].ClientUniverseIds.Add("sur-mesure");
    customerCatalog.Furniture[0].ClientTagIds.Add("michel");
    var exclusion = new CustomerFurnitureExclusion
    {
        LocationId = customerCatalog.Furniture[0].LocationId,
        RelativeTopPath = "Meuble_ignore.top",
        DisplayName = "Meuble ignoré"
    };
    customerCatalog.Exclusions.Add(exclusion);
    await customerStore.SaveAsync(customerCatalog);
    var reloadedCustomerCatalog = await customerStore.LoadAsync();
    Assert(reloadedCustomerCatalog.Furniture.Single().ClientTagIds.Contains("michel") && reloadedCustomerCatalog.Furniture.Single().ClientUniverseIds.Contains("sur-mesure"), "Les Univers Client et Tags Client doivent être conservés.");
    Assert(reloadedCustomerCatalog.Exclusions.Single().DisplayName == "Meuble ignoré" && !customerStore.CatalogPath.Contains("catalog.atlas.json", StringComparison.OrdinalIgnoreCase), "Les exclusions doivent rester hors de la base Atlas officielle.");
    reloadedCustomerCatalog.Locations.Clear();
    reloadedCustomerCatalog.Furniture.Clear();
    reloadedCustomerCatalog.FurnitureRoot = string.Empty;
    await customerStore.SaveAsync(reloadedCustomerCatalog);
    var withoutLocation = await customerStore.LoadAsync();
    Assert(withoutLocation.Locations.Count == 0 && string.IsNullOrWhiteSpace(withoutLocation.FurnitureRoot),
        "Un emplacement Client retiré doit rester retiré après redémarrage et ne pas être recréé par la migration historique.");
}
finally
{
    if (Directory.Exists(customerTestRoot)) Directory.Delete(customerTestRoot, true);
}

using (var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    var privateKey = signingKey.ExportECPrivateKeyPem();
    var publicKey = signingKey.ExportSubjectPublicKeyInfoPem();
    var licensePath = Path.Combine(Path.GetTempPath(), $"atlas-license-{Guid.NewGuid():N}", "Licence.fautpastoucher");
    const string deviceA = "ATL-1111-2222-3333-4444-5555";
    const string deviceB = "ATL-AAAA-BBBB-CCCC-DDDD-EEEE";
    var licenseService = new AtlasLicenseService(publicKey, licensePath, deviceA);
    var validUntil = new DateOnly(2027, 3, 31);
    var licenseCode = AtlasLicenseService.Generate("Client trimestriel", validUntil, privateKey, "test-license");
    var validLicense = licenseService.Validate(licenseCode, new DateOnly(2027, 3, 31));
    Assert(validLicense.IsValid && validLicense.Customer == "Client trimestriel" && validLicense.ValidUntil == validUntil, "Une licence signée doit rester valide jusqu’à la date librement choisie incluse.");
    Assert(!validLicense.HasFeature(AtlasLicenseService.CreatorFeature), "Une ancienne licence sans option doit rester valide sans ouvrir le module Créateur.");
    var creatorLicenseCode = AtlasLicenseService.Generate("Client Créateur", validUntil, privateKey, "creator-license", [AtlasLicenseService.CreatorFeature]);
    var creatorLicense = licenseService.Validate(creatorLicenseCode, new DateOnly(2027, 3, 1));
    Assert(creatorLicense.IsValid && creatorLicense.HasFeature(AtlasLicenseService.CreatorFeature), "L’option Créateur doit être incluse dans le contenu signé de la licence.");
    var deviceLicenseCode = AtlasLicenseService.Generate("Client par poste", validUntil, privateKey, "device-license", [AtlasLicenseService.CreatorFeature], deviceA);
    Assert(licenseService.Validate(deviceLicenseCode, new DateOnly(2027, 3, 1)).IsValid, "Une licence liée au poste prévu doit être acceptée.");
    Assert(new AtlasLicenseService(publicKey, licensePath, deviceB).Validate(deviceLicenseCode, new DateOnly(2027, 3, 1)).State == AtlasLicenseState.DeviceMismatch, "Une licence copiée sur un autre poste doit être refusée.");
    Assert(licenseService.Validate(licenseCode, new DateOnly(2027, 4, 1)).State == AtlasLicenseState.Expired, "La licence doit expirer le lendemain de sa date de fin.");
    Assert(licenseService.Validate(licenseCode + "X", new DateOnly(2027, 3, 1)).State == AtlasLicenseState.Invalid, "Un code modifié doit être refusé.");
    Assert(licenseService.Install(licenseCode, new DateOnly(2027, 3, 1)).IsValid && File.Exists(licensePath), "Un code valide doit être écrit dans Licence.fautpastoucher.");
    Assert(AtlasLicenseService.PrivateKeyMatchesPublicKey(privateKey, publicKey),
        "La clé privée Idéo doit être reconnue par une preuve cryptographique et non par son seul nom de fichier.");
    using (var unrelatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        Assert(!AtlasLicenseService.PrivateKeyMatchesPublicKey(unrelatedKey.ExportECPrivateKeyPem(), publicKey),
            "Une clé privée étrangère ne doit jamais ouvrir l’accès permanent Idéo.");
    var sharedKeyRoot = Path.Combine(Path.GetTempPath(), $"atlas-shared-key-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(sharedKeyRoot, "Configuration"));
    var sharedKeyPath = Path.Combine(sharedKeyRoot, "Configuration", "Atlas-Licence-ClePrivee.pem");
    await File.WriteAllTextAsync(sharedKeyPath, privateKey);
    Assert(AtlasLicenseService.FindMatchingSigningPrivateKeyPath(sharedKeyRoot, publicKey) == sharedKeyPath,
        "Atlas doit reconnaître la clé Idéo valide lorsqu’elle se trouve dans la configuration partagée.");
    Directory.Delete(sharedKeyRoot, true);
    Directory.Delete(Path.GetDirectoryName(licensePath)!, true);
}

using (var adminSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    var now = new DateTimeOffset(2027, 2, 12, 14, 0, 0, TimeSpan.Zero);
    const string device = "ATL-1111-2222-3333-4444-5555";
    var challenge = IdeoAdminAccessService.CreateChallenge(device, now);
    var response = IdeoAdminAccessService.GenerateResponse(challenge.RequestCode, "Stéphane",
        adminSigningKey.ExportECPrivateKeyPem(), now, TimeSpan.FromMinutes(5));
    var grant = challenge.ValidateAndConsume(response, adminSigningKey.ExportSubjectPublicKeyInfoPem(), now.AddMinutes(1));
    Assert(grant.OperatorName == "Stéphane" && challenge.IsConsumed,
        "Une réponse signée doit ouvrir une seule session Idéo pour la demande et le poste prévus.");
    var replayRejected = false;
    try { challenge.ValidateAndConsume(response, adminSigningKey.ExportSubjectPublicKeyInfoPem(), now.AddMinutes(2)); }
    catch (InvalidOperationException) { replayRejected = true; }
    Assert(replayRejected, "Une réponse Administrateur Idéo déjà utilisée doit être définitivement refusée.");
    var otherChallenge = IdeoAdminAccessService.CreateChallenge(device, now);
    var otherRequestRejected = false;
    try { otherChallenge.ValidateAndConsume(response, adminSigningKey.ExportSubjectPublicKeyInfoPem(), now.AddMinutes(2)); }
    catch (UnauthorizedAccessException) { otherRequestRejected = true; }
    Assert(otherRequestRejected, "Une réponse Idéo ne doit pas fonctionner dans une autre fenêtre, même sur le même poste.");
}

var assetResolverRoot = Path.Combine(Path.GetTempPath(), $"atlas-assets-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(assetResolverRoot);
    var resolvedPreview = Path.Combine(assetResolverRoot, "000000123.top.png");
    await File.WriteAllTextAsync(resolvedPreview, "preview");
    var movedFurniture = new FurnitureRecord
    {
        Reference = "000000123",
        SourceRelativePath = @"D:\AncienServeur\000000123.top",
        ImageRelativePath = @"D:\AncienServeur\000000123.top.png"
    };
    Assert(FurnitureAssetResolver.ResolveImage(movedFurniture, assetResolverRoot) == resolvedPreview,
        "L’image doit être retrouvée par son nom lorsque le chemin enregistré provient d’un autre poste ou serveur.");
}
finally
{
    if (Directory.Exists(assetResolverRoot)) Directory.Delete(assetResolverRoot, true);
}

var appRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "Atlas.App");
var mainWindowXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
var catalogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "CatalogView.xaml"));
var themeXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Themes", "AtlasTheme.xaml"));
Assert(mainWindowXaml.Contains("WindowChrome") && !mainWindowXaml.Contains("AllowsTransparency=\"True\""), "La fenêtre principale doit conserver un chrome redimensionnable qui respecte la barre des tâches.");
Assert(mainWindowXaml.Contains("StateChanged=\"Window_StateChanged\""), "La fenêtre principale doit adapter son cadre au mode agrandi.");
var mainWindowCode = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml.cs"));
Assert(mainWindowCode.Contains("WmGetMinMaxInfo") && mainWindowCode.Contains("MonitorFromWindow") && mainWindowCode.Contains("WorkArea"), "Le plein écran doit respecter la zone de travail du moniteur et sa barre des tâches.");
var launchWindowXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "LaunchWindow.xaml"));
var appCode = await File.ReadAllTextAsync(Path.Combine(appRoot, "App.xaml.cs"));
Assert(launchWindowXaml.Contains("Administrateur") && launchWindowXaml.Contains("Créateur") && launchWindowXaml.Contains("Utilisateur") && launchWindowXaml.Contains("OUVRIR HORIZON"), "Le lancement doit proposer explicitement les trois modes Atlas.");
Assert(appCode.Contains("OpenAdministratorSessionAsync") && appCode.Contains("LogoutAdministratorAsync") && appCode.Contains("atlas-horizon-user"), "Atlas doit pouvoir ouvrir et fermer une session administrateur sans redémarrage.");
Assert(catalogXaml.Contains("FilterPanelTemplate") && catalogXaml.Contains("IsClientFilterPanelPinned") && catalogXaml.Contains("VerticalScrollBarVisibility=\"Auto\""), "Horizon doit proposer des panneaux de filtres recherchables, épinglables et défilants.");
Assert(catalogXaml.Contains("MinWidth=\"300\"") && catalogXaml.Contains("MaxWidth=\"370\""), "Horizon doit protéger les dimensions du panneau produit.");
Assert(catalogXaml.Contains("ClientUniverseFacets") && catalogXaml.Contains("UniformToFill"), "Horizon doit présenter les univers comme une galerie illustrée au-dessus du catalogue.");
Assert(catalogXaml.Contains("UniverseCard") && catalogXaml.Contains("Height=\"82\""), "La galerie des univers doit rester compacte et lisible sans occuper le menu principal.");
Assert(!catalogXaml.Contains("MaxWidth=\"1820\""), "Horizon doit utiliser toute la largeur disponible en plein écran.");
Assert(mainWindowXaml.Contains("NavPathIcon") && !mainWindowXaml.Contains("Text=\"⚙\""), "Le menu principal doit utiliser des icônes vectorielles cohérentes.");
Assert(mainWindowXaml.Contains("Content=\"›\"") && mainWindowXaml.Contains("InverseBoolVisibility") && mainWindowXaml.Contains("ToolTip=\"Développer le menu\""), "Le menu réduit doit toujours présenter un bouton visible permettant de le développer.");
Assert(mainWindowXaml.Contains("<views:CatalogView") && !mainWindowXaml.Contains("<views:HorizonShell"), "Horizon doit rester intégré dans la coque Atlas commune.");
Assert(mainWindowXaml.Contains("Grid.Row=\"3\"") && mainWindowXaml.Contains("Text=\"SYSTÈME\""), "Les paramètres doivent rester ancrés en bas du menu commun.");
Assert(catalogXaml.Contains("ReferenceCaption") && catalogXaml.Contains("TargetItemWidth=\"235\""), "La fiche client doit exposer une référence adaptée à sa source et la grille doit rester dense.");
var viewModelSource = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainViewModel.cs"));
var searchDictionaryServiceSource = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "src", "Atlas.Core", "Services", "SearchDictionaryService.cs"));
var settingsXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "SettingsView.xaml"));
Assert(viewModelSource.Contains("IsUserMode") && viewModelSource.Contains("IsCreatorMode") && viewModelSource.Contains("IsAdministrativeMode") && viewModelSource.Contains("IsLicenseRestricted"), "Les modes utilisateur, Créateur et les sessions sans licence doivent conserver leurs périmètres propres.");
Assert(appCode.Contains("IdeoAdminAccessWindow") && appCode.Contains("UserPermissions.IdeoAdministrator")
       && !appCode.Contains("CreateFirstAdministratorAsync"),
    "L’Administrateur Idéo doit être une session éphémère signée et ne jamais être créé comme compte client persistant.");
Assert(appCode.Contains("FindMatchingSigningPrivateKeyPath") && appCode.Contains("permanentDevelopmentSession")
       && mainWindowCode.Contains("IsPermanentIdeoDevelopmentSession"),
    "Une vraie clé privée Idéo, locale ou partagée, doit ouvrir un accès interne permanent sans désactiver la protection des clients.");
var adminAccessServiceSource = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "src", "Atlas.Core", "Services", "IdeoAdminAccessService.cs"));
Assert(adminAccessServiceSource.Contains("RandomNumberGenerator.GetBytes(24)") && adminAccessServiceSource.Contains("ValidateAndConsume")
       && adminAccessServiceSource.Contains("expectedDeviceId") && adminAccessServiceSource.Contains("expectedNonce"),
    "L’accès Idéo doit être lié au poste, à une demande aléatoire en mémoire et consommé une seule fois.");
Assert(mainWindowXaml.Contains("ChangeRole_Click") && mainWindowXaml.Contains("RoleActionLabel") && appCode.Contains("ChangeRoleAsync"), "La coque commune doit ramener au choix du rôle sans imposer un redémarrage.");
Assert(mainWindowXaml.Contains("LicenseBannerText") && viewModelSource.Contains("Atlas porte votre conception avec Biblidéo"), "Le bandeau principal doit afficher l’état et l’échéance de la licence plutôt que les informations techniques de l’environnement.");
var furnitureXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "FurnitureView.xaml"));
Assert(viewModelSource.Contains("NextFurnitureReference()") && viewModelSource.Contains("ToString(\"D9\")"), "Les nouveaux meubles doivent recevoir une référence automatique sur neuf chiffres.");
Assert(viewModelSource.Contains("ValidateFurnitureReference") && viewModelSource.Contains("exactement 9 chiffres") && viewModelSource.Contains("est déjà utilisée par"), "Une référence meuble modifiée doit rester numérique, unique et explicite en cas de conflit.");
Assert(viewModelSource.Contains("BuildFurnitureRenamePlan") && viewModelSource.Contains("RollbackFurnitureRenames") && viewModelSource.Contains("File.Move(rename.OldTop, rename.NewTop)"), "Le renommage de référence doit renommer le .TOP de manière réversible.");
Assert(viewModelSource.Contains("ArchiveFurnitureAsync") && viewModelSource.Contains("RestoreFurnitureAsync") && viewModelSource.Contains("PermanentlyDeleteFurnitureAsync"), "Les meubles publiés doivent pouvoir passer par une corbeille récupérable.");
Assert(furnitureXaml.Contains("Mettre à la corbeille") && furnitureXaml.Contains("Restaurer") && furnitureXaml.Contains("Supprimer définitivement"), "La Forge doit exposer tout le cycle de vie de la corbeille.");
Assert(!furnitureXaml.Contains("Fiche directe") && !furnitureXaml.Contains("Famille + variantes"), "Les anciens boutons de mode de création doivent être retirés.");
Assert(furnitureXaml.Contains("Dupliquer") && furnitureXaml.Contains("Voir dans Horizon") && furnitureXaml.Contains("MODIFICATIONS NON ENREGISTRÉES"), "La fiche meuble doit offrir les raccourcis de contrôle validés.");
Assert(furnitureXaml.Contains("Ouvrir le catalogue") && !furnitureXaml.Contains("ItemsSource=\"{Binding FurnitureView}\""), "La fiche meuble doit libérer sa largeur et ouvrir le catalogue dans une fenêtre dédiée.");
var furnitureCatalogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "FurnitureCatalogWindow.xaml"));
var furnitureCatalogCode = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "FurnitureCatalogWindow.xaml.cs"));
Assert(furnitureCatalogXaml.Contains("Icon=\"pack://application:,,,/Atlas;component/Assets/Atlas.ico\""),
    "La fenêtre du catalogue de la Forge doit afficher l’icône Atlas dans Windows.");
Assert(furnitureCatalogXaml.Contains("EnableRowVirtualization=\"True\"") && furnitureCatalogXaml.Contains("VirtualizationMode=\"Recycling\"")
       && furnitureCatalogXaml.Contains("Ouvrir dans la Forge") && furnitureCatalogXaml.Contains("Fiches à compléter"),
    "Le catalogue de la Forge doit rester fluide sur de gros volumes et proposer recherche, filtres et ouverture explicite.");
Assert(furnitureCatalogXaml.Contains("Content=\"Liste\"") && furnitureCatalogXaml.Contains("Content=\"Cartes\"")
       && furnitureCatalogXaml.Contains("VirtualizingWrapPanel") && furnitureCatalogXaml.Contains("CardImage_OnLoaded"),
    "Le catalogue doit proposer une vue Cartes responsive dont les aperçus sont chargés uniquement à l’affichage.");
Assert(mainWindowCode.Contains("OpenFurnitureCatalog") && mainWindowCode.Contains("_furnitureCatalogWindow is { IsLoaded: true }")
       && mainWindowXaml.Contains("PreviewKeyDown=\"Window_OnPreviewKeyDown\""),
    "Une seule fenêtre Catalogue doit être ouverte et rester accessible par Ctrl+K.");
Assert(furnitureCatalogCode.Contains("FurnitureCatalogWindowStateStore") && furnitureCatalogCode.Contains("IntersectsVirtualScreen")
       && viewModelSource.Contains("TryOpenFurnitureAsync") && viewModelSource.Contains("ChooseThree"),
    "Le catalogue doit mémoriser son écran et protéger le changement de meuble en présence de modifications non enregistrées.");
Assert(furnitureXaml.Contains("Width=\"430\"") && furnitureXaml.Contains("Height=\"170\"")
       && furnitureXaml.Contains("IMAGE DISPONIBLE") && furnitureXaml.Contains("FICHIER TOPSOLID"),
    "L’aperçu de la fiche meuble doit rester compact et séparer clairement l’image du fichier TopSolid.");
Assert(viewModelSource.Contains("linkedComponents") && viewModelSource.Contains("component.TechnicalName") && viewModelSource.Contains("furniture.NicheOuverte") && viewModelSource.Contains("furniture.UseCasesCsv"), "La recherche Horizon doit couvrir toute la fiche et les composants liés.");
Assert(viewModelSource.Contains("CatalogSearchEngine.Prepare(SearchFieldsFor")
       && viewModelSource.Contains("Task.Delay(180")
       && !viewModelSource.Contains("_lastClientSearchMatches"),
    "La recherche Horizon doit utiliser un index préparé et temporiser la frappe sans réutiliser une liste devenue incorrecte.");
Assert(catalogXaml.Contains("ClientSearchSuggestions") && catalogXaml.Contains("ClientSearchSuggestion_OnClick"),
    "Horizon doit proposer des complétions métier pendant la saisie.");
Assert(viewModelSource.Contains("if (!MatchesSearchConstraint(furniture, constraint, document)")
       && viewModelSource.Contains("EffectiveQuantity(furniture.Rayon, furniture.NombreRayons)")
       && viewModelSource.Contains("ClientPublishedTotal"),
    "Un concept reconnu doit exclure tout meuble non conforme et la quantité doit rester cohérente avec la présence réelle de l’élément.");
Assert(settingsXaml.Contains("Concepts métier") && settingsXaml.Contains("SearchConceptGrid")
       && settingsXaml.Contains("SearchTestMatches"),
    "Les paramètres doivent permettre d’inspecter les concepts et de tester une demande sur le catalogue réel.");
Assert(searchDictionaryServiceSource.Contains("stored.OfficialVersion < SearchDictionaryService.OfficialVersion")
       && searchDictionaryServiceSource.Contains("SearchDictionaryService.MergeOfficial(stored)"),
    "Une installation existante doit recevoir le dictionnaire Atlas enrichi sans écraser les ajouts du site.");
Assert(viewModelSource.Contains("$\"{SelectedFurniture.Reference}.top\"") && viewModelSource.Contains("File.Copy(sourceTop, targetTop, false)"), "Le fichier TopSolid doit être copié sous la référence Atlas sans écraser l’original.");
Assert(viewModelSource.Contains("Settings.FurnitureRoot") && viewModelSource.Contains("ChooseFurnitureRootCommand") && viewModelSource.Contains("ResolveFurniturePath"), "Les meubles doivent disposer d’un dossier central configurable distinct de la bibliothèque des composants.");
Assert(catalogXaml.Contains("Grid.Column=\"2\"") && catalogXaml.Contains("ItemsSource=\"{Binding ClientFurnitureView}\""), "La collection et la fiche produit doivent partager la hauteur principale d’Horizon.");
Assert(catalogXaml.Contains("COMPOSITION DU MEUBLE") && catalogXaml.Contains("SelectedClientCompositionLines") && catalogXaml.Contains("Binding Quantity"), "Horizon doit afficher automatiquement les composants Biblidéo déjà liés au meuble et leurs quantités.");
Assert(viewModelSource.Contains("RefreshSelectedClientComposition") && viewModelSource.Contains("SelectedClientFurniture.ComponentLines"), "La composition Horizon doit provenir directement de la fiche meuble existante, sans seconde saisie.");
Assert(furnitureXaml.Contains("Actualiser l’image…") && viewModelSource.Contains("ChooseFurnitureImageCommand") && viewModelSource.Contains("File.Copy(dialog.FileName, targetImage, true)"), "La fiche d’identité doit permettre de remplacer l’image du meuble indépendamment du fichier .TOP.");
Assert(settingsXaml.Contains("LicensePanel") && settingsXaml.Contains("Vérifier et installer la licence") && settingsXaml.Contains("Date de fin de validité"), "Les paramètres doivent permettre au client d’installer son code et à l’administrateur de choisir librement l’échéance.");
Assert(settingsXaml.Contains("Autoriser le module Créateur") && settingsXaml.Contains("LicenseFeaturesLabel"), "Le générateur doit permettre d’inclure explicitement le module Créateur et d’afficher les options signées.");
Assert(settingsXaml.Contains("IDENTIFIANT DE CE POSTE") && settingsXaml.Contains("NewLicenseDeviceId") && viewModelSource.Contains("deviceId: NewLicenseDeviceId"), "La licence doit être générée pour l’identifiant transmis par un poste précis.");
Assert(settingsXaml.Contains("Contenus Atlas") && settingsXaml.Contains("Choisir et installer un pack") && settingsXaml.Contains("Générer le pack de contenus"),
    "Les paramètres doivent permettre d’installer et de générer les contenus métier sans republier l’application.");
Assert(viewModelSource.Contains("InstallContentPackageAsync") && viewModelSource.Contains("GenerateContentPackageAsync"),
    "L’application doit exposer les deux parcours séparés d’installation client et de publication Idéo.");
Assert(settingsXaml.Contains("Déplacer l’espace Atlas") && settingsXaml.Contains("LocalLibraryRootOverride") && viewModelSource.Contains("AtlasWorkspaceService.CopyAndVerifyAsync"), "L’espace Atlas doit être déplaçable et les chemins communs surchargeables localement.");
var installerScript = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "deployment", "Atlas.iss"));
Assert(installerScript.Contains("Ideo_Atlas\\Application") && installerScript.Contains("Ideo Solutions\\Atlas") && installerScript.Contains("Uninstallable=yes"),
    "L’installateur Windows doit créer l’arborescence Atlas, le dossier machine de la licence et un désinstalleur déclaré à Windows.");
Assert(installerScript.Contains("RemoveAllLocalData") && installerScript.Contains("Les dossiers réseau externes ne sont jamais supprimés")
       && installerScript.Contains("ATLASMODE|production"),
    "La désinstallation doit distinguer conservation et suppression complète, tandis qu’une installation client reste en Production par défaut.");
Assert(mainWindowXaml.Contains("UpdateLabel") && mainWindowXaml.Contains("Visibility=\"{Binding IsAdministrator"),
    "Le déclenchement manuel d’une mise à jour applicative ne doit être visible que pour l’administrateur.");
Assert(viewModelSource.Contains("AllowsAutomaticApplicationUpdates") && viewModelSource.Contains("IsAdministrator && !IsBusy"),
    "Les mises à jour automatiques doivent être réservées au développement Idéo et la commande manuelle à l’administrateur.");
Assert(catalogXaml.Contains("HasFullHorizonAccess") && catalogXaml.Contains("Catalogue en consultation uniquement") && catalogXaml.Contains("ClientUniverseFacets"), "Une licence absente ou expirée doit masquer recherche, filtres, univers et transfert TopSolid.");
Assert(viewModelSource.Contains("AtlasLicenseService.SigningPrivateKeyPath") && viewModelSource.Contains("IsAdministrator || IsLicenseValid"), "L’administrateur doit contourner la licence sans exposer la clé privée aux installations clientes.");
Assert(settingsXaml.Contains("ChooseUniverseImage_OnClick") && settingsXaml.Contains("Enregistrer les univers"), "Les paramètres doivent permettre d’associer et d’enregistrer une image à chaque univers.");
Assert(settingsXaml.Contains("Settings.FurnitureRoot") && settingsXaml.Contains("ChooseFurnitureRootCommand") && settingsXaml.Contains("SaveFurnitureRootCommand"), "Le Gestionnaire du site doit pouvoir configurer et enregistrer le dossier commun des meubles.");
Assert(viewModelSource.Contains("ChooseFurnitureRootCommand = new(_ => ChooseFurnitureRoot(), _ => CanManageSite)") && viewModelSource.Contains("SaveFurnitureRootAsync"), "Le réglage du dossier commun des meubles doit être réservé au Gestionnaire du site ou à l’Administrateur Idéo.");
Assert(settingsXaml.Contains("Width=\"260\"") && themeXaml.Contains("CalendarDayButton") && themeXaml.Contains("MinWidth\" Value=\"280"), "Le générateur de licence doit proposer une date compacte et un calendrier lisible.");
Assert(settingsXaml.Contains("FurnitureTypeList") && settingsXaml.Contains("FurnitureUsageList") && settingsXaml.Contains("Renommer"), "Les paramètres doivent administrer les types de meubles et usages spécifiques.");
Assert(settingsXaml.Contains("Header=\"Synonymes\"") && settingsXaml.Contains("Header=\"Expressions\"")
       && settingsXaml.Contains("Header=\"Mots ignorés\"") && settingsXaml.Contains("Header=\"Demandes enregistrées\"")
       && settingsXaml.Contains("Header=\"Laboratoire\""), "Les paramètres doivent séparer les cinq outils d’administration du dictionnaire.");
Assert(settingsXaml.Contains("SynonymSearchBox") && settingsXaml.Contains("ExpressionSearchBox")
       && settingsXaml.Contains("IgnoredTermSearchBox") && settingsXaml.Contains("RequestSearchBox"),
    "Chaque liste du dictionnaire doit proposer une recherche dédiée.");
var settingsCode = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "SettingsView.xaml.cs"));
Assert(settingsCode.Contains("SearchDictionaryFilter_OnChanged") && settingsCode.Contains("MatchesDictionarySearch"),
    "Les recherches du dictionnaire doivent filtrer les termes, expressions et demandes affichés.");
Assert(settingsCode.Contains("AddFurnitureType_OnClick") && settingsCode.Contains("RenameFurnitureUsage_OnClick") && settingsCode.Contains("UpdateFurnitureVocabularyAsync"), "Les référentiels meubles doivent être ajoutables, renommables, supprimables et enregistrés.");
Assert(settingsCode.Contains("SaveSearchDictionaryAsync") && viewModelSource.Contains("SearchDictionaryService.Interpret") && viewModelSource.Contains("ScheduleUnknownSearchObservation"), "Le dictionnaire doit être persistant, interpréter les intentions et mémoriser les recherches sans résultat.");
Assert(viewModelSource.Contains("DefaultFurnitureTypes") && viewModelSource.Contains("DefaultFurnitureUsages") && viewModelSource.Contains("_catalog.FurnitureTypes = FurnitureTypes.ToList()"), "Les valeurs historiques doivent migrer vers des référentiels configurables et persistés.");
var customerFurnitureXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "CustomerFurnitureView.xaml"));
var customerFurnitureCatalogXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "CustomerFurnitureCatalogWindow.xaml"));
var customerFurnitureCatalogCode = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "CustomerFurnitureCatalogWindow.xaml.cs"));
Assert(customerFurnitureCatalogXaml.Contains("Icon=\"pack://application:,,,/Atlas;component/Assets/Atlas.ico\""),
    "La fenêtre du catalogue personnel doit afficher l’icône Atlas dans Windows.");
Assert(mainWindowXaml.Contains("CommandParameter=\"MyFurniture\"") && customerFurnitureXaml.Contains("SelectedCustomerPersonalTagOptions") && customerFurnitureXaml.Contains("CanManageCustomerFurniture"), "Le Gestionnaire doit consulter Mes meubles tandis que les modifications restent réservées au Créateur licencié.");
Assert(viewModelSource.Contains("CanViewCustomerFurniture => IsAdministrator || IsSiteManagerMode")
       && viewModelSource.Contains("CanManageCustomerFurniture => IsAdministrator || (IsCreatorMode"),
    "La consultation du catalogue personnel et sa gestion doivent relever de droits distincts.");
Assert(customerFurnitureCatalogXaml.Contains("CATALOGUE PERSONNEL") && customerFurnitureCatalogXaml.Contains("Content=\"Liste\"")
       && customerFurnitureCatalogXaml.Contains("Content=\"Cartes\"") && customerFurnitureCatalogCode.Contains("OpenCustomerFurniture"),
    "Mes meubles doit proposer un catalogue personnel détachable, en liste ou cartes, ouvrable dans la page principale.");
Assert(catalogXaml.Contains("CommandParameter=\"Source\"") && catalogXaml.Contains("CommandParameter=\"Tags Client\"") && catalogXaml.Contains("SourceLabel"), "Horizon doit distinguer la source et filtrer les Tags Client sans les mélanger aux tags Atlas.");
Assert(catalogXaml.Contains("FurnitureCard_OnMouseDoubleClick") && catalogXaml.Contains("FurnitureChoice_OnClick")
       && catalogXaml.Contains("ClientSelectionItems") && catalogXaml.Contains("PrepareClientSelectionLabel"),
    "Horizon doit prévisualiser au simple clic, sélectionner au double clic et afficher chaque meuble d’une sélection multiple.");
Assert(catalogXaml.Contains("MoreFiltersPopup") && !catalogXaml.Contains("<Button.ContextMenu>"),
    "Les filtres complémentaires doivent utiliser un panneau Atlas lisible sans bande blanche native.");
Assert(viewModelSource.Contains("CustomerFurnitureStore") && viewModelSource.Contains("BuildCustomerFurnitureRecord") && viewModelSource.Contains("ManageOwnFurniture"), "Les meubles clients doivent être indexés séparément et réservés au profil Créateur.");
Assert(viewModelSource.Contains("IsCreatorFeatureLicensed") && viewModelSource.Contains("NewLicenseCreatorEnabled") && viewModelSource.Contains("features: features"), "L’accès Créateur doit exiger à la fois le profil utilisateur et l’option signée dans la licence.");
var componentsXaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "Views", "ComponentsView.xaml"));
Assert(componentsXaml.Contains("Binding DetailedName"), "La Forge doit afficher le libellé complet des composants possédant un code C=.");
Assert(themeXaml.Contains("BasedOn=\"{StaticResource {x:Type TextBlock}}\""), "Les titres explicites doivent hériter de la couleur de texte du thème sombre.");
Assert(themeXaml.Contains("ContentSource=\"Header\"") && themeXaml.Contains("TargetType=\"TabItem\""),
    "Le gabarit des onglets doit afficher explicitement leur libellé Header.");

Console.WriteLine("Atlas.Core : contrôles métier réussis.");
