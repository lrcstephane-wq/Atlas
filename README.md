# Biblidéo Atlas

Biblidéo Atlas est la future application catalogue de la gamme Idéo. Elle est distincte de l’outil interne Biblidéo Post-production.

La V0.3 pose deux espaces :

- **Forge** pour parcourir visuellement les bibliothèques, documenter les composants, structurer les familles et variantes, composer les meubles et valider les fiches ;
- **Catalogue Atlas** pour prévisualiser une expérience client filtrable par univers, type, famille et tag.

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
- filtres famille et type recalculés dans la bibliothèque sélectionnée ;
- nom affiché proposé depuis le marqueur `#V=` sans écraser une modification manuelle ;
- familles distinctes par bibliothèque, synchronisées depuis l’index ;
- tags affectables à plusieurs familles et hérités par les composants puis les meubles, avec exceptions locales ;
- menu de paramètres structuré par module, les capacités paramétriques restant explicitement hors MVP ;
- validation humaine et justification obligatoire en cas de forçage ;
- recherche et virtualisation pour les volumes importants ;
- détection, téléchargement, contrôle SHA-256 et installation automatique des futures mises à jour via les GitHub Releases ;
- distribution principale sous forme de dossier autonome archivé en ZIP, sans compression interne ni auto-extraction de l’exécutable, afin d’éviter les comportements de packaging assimilables à un logiciel malveillant.

## Installation Windows

Télécharger `Atlas-win-x64.zip`, extraire entièrement le dossier puis lancer `Atlas.exe` depuis ce dossier. Le passage depuis une version antérieure à la V0.3 demande cette dernière extraction manuelle. Ensuite, le bouton de mise à jour télécharge le pack officiel, vérifie son intégrité et relance Atlas grâce au module statique `Atlas.Updater.exe`, sans script temporaire.

L’exécutable reste non signé pendant la phase de développement : Windows peut donc encore afficher un avertissement d’éditeur inconnu. Une détection antivirus de type cheval de Troie ne doit en revanche jamais être ignorée ni autorisée manuellement.

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
