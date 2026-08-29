# Biblidéo Atlas

Biblidéo Atlas est la future application catalogue de la gamme Idéo. Elle est distincte de l’outil interne Biblidéo Post-production.

La V0.1 pose deux espaces :

- **Forge** pour indexer les bibliothèques, documenter les composants, composer les meubles et valider les fiches ;
- **Catalogue Atlas** pour prévisualiser l’expérience qui sera proposée aux clients.

## Principes déjà implémentés

- application Windows WPF en .NET 8, sans dépendance externe ;
- identité visuelle Idéo centralisée dans `App.xaml` ;
- stockage dans un dossier partagé choisi au premier lancement ;
- détection de concurrence et sauvegardes automatiques du catalogue ;
- comptes utilisateurs, mots de passe hachés PBKDF2 et permissions ;
- indexation des chemins et noms `.TOP` sans lecture du format propriétaire ;
- classement des noms non conformes dans les alertes sans masquer le composant ;
- environnement global SC ou EP, jamais mélangé dans un catalogue ;
- composition explicite des meubles à partir des fiches composants ;
- validation humaine et justification obligatoire en cas de forçage ;
- recherche et virtualisation pour les volumes importants ;
- mise à jour automatique via les GitHub Releases.

## Données partagées

Atlas crée automatiquement :

```text
<dossier partagé Atlas>
├── Configuration
├── Data
├── Backups
├── Images
├── Locks
└── Logs
```

Le chemin est modifiable. Un petit fichier local dans `%LOCALAPPDATA%\Ideo Solutions\Atlas` mémorise seulement l’emplacement partagé utilisé par le poste.

## Compilation

```powershell
dotnet build Atlas.sln --configuration Release
dotnet run --project tests/Atlas.Core.SmokeTests/Atlas.Core.SmokeTests.csproj --configuration Release
```

Les GitHub Actions produisent un `Atlas.exe` Windows autonome. Une release taguée `vX.Y.Z` devient la source des mises à jour automatiques.
