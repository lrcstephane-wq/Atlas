using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Atlas.App;
using Atlas.Core.Models;
using Atlas.Core.Services;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "visual-snapshots"));
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), $"atlas-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var catalog = BuildVisualCatalog(root);
            var store = new SharedCatalogStore(root);
            await store.SaveAsync(catalog, 0, "Contrôle visuel");
            var bootstrap = new LocalBootstrap { SharedRoot = root };
            var account = UserAccountStore.CreateAccount("visual", "Contrôle visuel", "Atlas-Visual-2026", UserPermissions.Read | UserPermissions.Edit | UserPermissions.Validate | UserPermissions.Administer);
            var vm = new MainViewModel(store, new UserAccountStore(root), bootstrap, account);
            await vm.InitializeAsync();
            vm.CurrentPage = "Catalog";

            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            foreach (var size in new[] { (1366, 768), (1920, 1080), (2560, 1440) })
                Render(vm, output, size.Item1, size.Item2);
            app.Shutdown();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static AtlasCatalog BuildVisualCatalog(string root)
    {
        var catalog = DemoCatalogFactory.Create();
        catalog.Settings.AutoUpdate = false;
        catalog.UniverseDefinitions.Clear();
        var universes = new[] { "Cuisine", "Dressing", "Salle de bain", "Bibliothèque", "Séjour", "Bureau / Tertiaire", "Buanderie", "Agencement commercial", "Chambre", "Hôtellerie / Hébergement", "Restaurant / Bar" };
        for (var index = 0; index < universes.Length; index++)
        {
            var path = Path.Combine("Images", "Universes", $"universe-{index:00}.png");
            DrawPreview(Path.Combine(root, path), universes[index], index, true);
            catalog.UniverseDefinitions.Add(new CatalogUniverseRecord { Name = universes[index], Description = "Collection professionnelle", ImageRelativePath = path, SortOrder = index });
        }
        catalog.Universes = universes.ToList();
        catalog.Furniture.Clear();
        var names = new[] { "Meuble sous-évier", "Colonne four", "Meuble vasque", "Bibliothèque ouverte", "Banquette coffre", "Bureau direction", "Meuble buanderie", "Comptoir accueil", "Dressing penderie", "Tête de lit", "Bar technique", "Console murale", "Armoire portes", "Meuble TV", "Présentoir boutique", "Rangement sous pente", "Caisson imprimante", "Meuble machine à café" };
        for (var index = 0; index < names.Length; index++)
        {
            var path = Path.Combine("Images", "Furniture", $"furniture-{index:00}.png");
            DrawPreview(Path.Combine(root, path), names[index], index, false);
            catalog.Furniture.Add(new FurnitureRecord
            {
                Id = $"visual-{index:00}", Reference = $"MEU-{index + 1:0000}", DisplayName = names[index], Description = "Mobilier paramétrique prêt à intégrer au projet.",
                ImageRelativePath = path, SourceRelativePath = $"Models\\{names[index]}.top", Status = RecordStatus.Publiee,
                TypeMeuble = index % 3 == 0 ? "Colonne" : "Meuble bas", Forme = "Droit", Universes = [universes[index % universes.Length]],
                Usages = [index % 2 == 0 ? "Rangement" : "Technique"], PrincipleConstruction = "Montant filant", TypeAssemblage = "Tourillons + excentriques", PositionDos = "Rainuré"
            });
        }
        return catalog;
    }

    private static void DrawPreview(string path, string label, int seed, bool landscape)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var width = landscape ? 720 : 620;
        var height = landscape ? 420 : 520;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var dark = new Color { A = 255, R = (byte)(18 + seed % 3 * 8), G = (byte)(34 + seed % 5 * 7), B = (byte)(48 + seed % 4 * 9) };
            dc.DrawRectangle(new LinearGradientBrush(dark, Color.FromRgb(8, 20, 31), 35), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 22, 45, 58)), null, new Rect(width * .12, height * .2, width * .76, height * .58));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(85 + seed * 7 % 80), (byte)(95 + seed * 11 % 70), (byte)(90 + seed * 13 % 65))), null, new Rect(width * .22, height * .34, width * .56, height * .36));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 73, 208, 244)), 2), new Point(width * .22, height * .7), new Point(width * .78, height * .7));
            var text = new FormattedText(label, CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), landscape ? 34 : 28, Brushes.White, 1);
            dc.DrawText(text, new Point(24, height - text.Height - 22));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Render(MainViewModel vm, string output, int width, int height)
    {
        var window = new MainWindow(vm) { Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, $"horizon-{width}x{height}.png"));
        encoder.Save(stream);
        window.Close();
    }
}
