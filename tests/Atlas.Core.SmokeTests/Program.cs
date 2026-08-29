using Atlas.Core.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Assert(ComponentNameParser.TryParse("TIROIR#V=AVANTECH#I=470#R=YOU#C=STD", out var parsed), "Le nom conforme doit être reconnu.");
Assert(parsed.Type == "TIROIR" && parsed.Variant == "AVANTECH" && parsed.Index == "470", "Les marqueurs doivent être extraits.");
Assert(parsed.Range == "YOU" && parsed.Construction == "STD", "Les marqueurs optionnels doivent être extraits.");
Assert(!ComponentNameParser.TryParse("nom_incomplet", out _), "Un nom incomplet doit rester non classé.");

var demo = DemoCatalogFactory.Create();
Assert(demo.Components.Count >= 2 && demo.Furniture.Count >= 1, "Le catalogue de démonstration doit permettre l’aperçu de la V0.1.");
Assert(demo.Furniture[0].ComponentIds.All(id => demo.Components.Any(component => component.Id == id)), "La composition doit référencer des composants connus.");

var generatedSecret = Guid.NewGuid().ToString("N");
var account = UserAccountStore.CreateAccount("test-user", "Utilisateur de test", generatedSecret, Atlas.Core.Models.UserPermissions.Administer);
Assert(account.PasswordHash != generatedSecret && account.PasswordSalt.Length > 0, "Le secret ne doit jamais être stocké en clair.");

Console.WriteLine("Atlas.Core : contrôles métier réussis.");
