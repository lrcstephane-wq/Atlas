# Biblidéo Atlas

Biblidéo Atlas est la future application catalogue de la gamme Idéo. Elle est distincte de l’outil interne Biblidéo Post-production.

La V0.2 pose deux espaces :

- **Forge** pour parcourir visuellement les bibliothèques, documenter les composants, structurer les familles et variantes, composer les meubles et valider les fiches ;
- **Catalogue Atlas** pour prévisualiser une expérience client filtrable par univers et type.

## Principes déjà implémentés

- application Windows WPF en .NET 8, sans dépendance externe ;
- design system sombre « Atelier premium » centralisé dans `App.xaml` ;
- stockage dans un dossier partagé choisi au premier lancement ;
- détection de concurrence et sauvegardes automatiques du catalogue ;
- comptes utilisateurs, mots de passe hachés PBKDF2 et permissions ;
- indexation des chemins et noms `.TOP` sans lecture du format propriétaire ;
- exploitation des aperçus `.top.png`, avec vues mosaïque et liste virtualisées ;
- classement des noms non conformes dans les alertes sans masquer le composant ;
- environnement global SC ou EP, jamais mélangé dans un catalogue ;
- composition explicite des meubles à partir des fiches composants ;
- sélection multiple des composants et multi-univers sur une même fiche meuble ;
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
