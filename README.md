# Biblidéo Atlas

Biblidéo Atlas est la future application catalogue de la gamme Idéo. Elle est distincte de l’outil interne Biblidéo Post-production.

## Recherche métier v0.14

Le dictionnaire partagé d’Horizon distingue cinq espaces d’administration : synonymes, expressions composées, mots ignorés, demandes réelles et laboratoire de diagnostic. Le socle Atlas est versionné et enrichi à chaque mise à jour, tandis que les ajouts propres au site restent conservés dans `Configuration/search-dictionary.atlas.json`.

Les demandes saisies dans Horizon sont mémorisées après temporisation avec leur interprétation et leur nombre de résultats. L’administrateur peut documenter l’interprétation attendue et valider les cas qui serviront de références métier.

La V0.3 pose deux espaces :

- **Forge** pour parcourir visuellement les bibliothèques, documenter les composants, structurer les familles et variantes, composer les meubles et valider les fiches ;
- **Catalogue Atlas** pour prévisualiser une expérience client filtrable par univers, type, famille et tag.

## Principes déjà implémentés

- application Windows WPF en .NET 8, sans dépendance externe ;
- design system sombre « Atelier premium » centralisé dans `App.xaml` ;
- stockage dans un dossier partagé choisi au premier lancement ;
- détection de concurrence et sauvegardes automatiques du catalogue ;
- comptes Utilisateur, Créateur et Gestionnaire du site, avec mots de passe hachés PBKDF2 ;
- session Administrateur Idéo éphémère, ouverte par une réponse signée liée au poste et à une demande à usage unique ;
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
- dictionnaire métier versionné : expressions régionales, fautes, quantités exactes, usages, marques et critères structurels ;
- compléments de vocabulaire propres à chaque site, conservés automatiquement lors des mises à jour du socle Idéo ;
- interprétation visible des recherches naturelles et historique partagé des demandes sans résultat ;
- détection, téléchargement et contrôle SHA-256 des futures mises à jour via les GitHub Releases ;
- mises à jour automatiques uniquement sur les postes de développement Idéo ;
- en Production, déclenchement exclusivement manuel et réservé à la session Administrateur Idéo ;
- distribution principale sous forme d’un véritable installateur Windows avec désinstallation standard ou suppression complète des données locales.

## Installation Windows

Pour une première installation, télécharger puis lancer `Atlas-Setup-X.Y.Z-win-x64.exe`. Atlas est installé dans `C:\Ideo_Atlas\Application`, crée ses raccourcis et apparaît dans les applications installées de Windows.

Une installation client est toujours configurée en mode Production : aucune mise à jour applicative ne se lance automatiquement et seule une session Administrateur Idéo voit le bouton permettant de la déclencher. Le même installateur peut réparer ou mettre à niveau l’application sans supprimer les données.

La désinstallation Windows propose de conserver la licence et les données locales pour une réinstallation, ou de les supprimer complètement après une seconde confirmation. Les dossiers réseau externes ne sont jamais supprimés.

L’exécutable reste non signé pendant la phase de développement : Windows peut donc encore afficher un avertissement d’éditeur inconnu. Une détection antivirus de type cheval de Troie ne doit en revanche jamais être ignorée ni autorisée manuellement.

## Données partagées

Atlas crée automatiquement :

```text
<dossier partagé Atlas>
├── Configuration
│   └── search-dictionary.atlas.json
├── Data
├── Backups
├── Images
├── Locks
└── Logs
```

Le chemin est modifiable. Le poste mémorise son emplacement dans `C:\Ideo_Atlas\Configuration\poste.json`. Le dictionnaire officiel est fusionné à chaque mise à jour avec les expressions propres au site, sans les écraser.

## Accès et contenus

Le premier compte créé est un **Gestionnaire du site** : il configure les chemins, les comptes, la licence reçue et installe les fichiers `.atlaspack` signés. Il n’accède ni à la Forge ni aux générateurs Idéo. Le profil **Créateur** reste soumis à l’option correspondante de la licence ; le profil **Utilisateur** ouvre Horizon.

L’**Administrateur Idéo** n’est jamais enregistré dans `users.atlas.json`. Stéphane ou Thomas génèrent sur un poste interne une réponse valable quelques minutes à partir de la demande affichée par le poste client. La réponse est liée à l’identifiant du poste et au défi aléatoire conservé en mémoire ; elle devient inutilisable dès sa validation. La session se ferme manuellement ou automatiquement après inactivité.

Les `.atlaspack` mettent à jour le catalogue Atlas, le dictionnaire, les référentiels, les fichiers `.TOP` et leurs aperçus sans remplacer les ajouts du site. La génération refuse désormais un pack dont un fichier meuble référencé est absent.

## Compilation

```powershell
dotnet build Atlas.sln --configuration Release
dotnet run --project tests/Atlas.Core.SmokeTests/Atlas.Core.SmokeTests.csproj --configuration Release
```

Les GitHub Actions produisent l’installateur `Atlas-Setup-X.Y.Z-win-x64.exe` et le paquet différentiel utilisé par le module de mise à jour. Une release taguée `vX.Y.Z` devient la version applicative disponible pour les administrateurs.
