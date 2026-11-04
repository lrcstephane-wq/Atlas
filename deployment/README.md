# Déploiement Windows Atlas

L’installateur produit par `Atlas.iss` utilise toujours `C:\Ideo_Atlas\Application` et s’enregistre dans les applications installées de Windows.

## Installation client

Lancer normalement `Atlas-Setup-<version>-win-x64.exe`. Le poste est configuré en mode `Production` : aucune mise à jour applicative n’est recherchée automatiquement. Seul un administrateur Atlas voit le bouton permettant de la déclencher manuellement.

## Poste de développement Idéo

Lancer une première fois l’installateur avec :

```powershell
Atlas-Setup-<version>-win-x64.exe /ATLASMODE=development
```

Le mode est conservé lors des mises à jour et autorise la vérification automatique configurée dans Atlas.

## Désinstallation

La désinstallation Windows propose soit de conserver les données locales et la licence, soit d’effacer intégralement les dossiers locaux Atlas. Aucun chemin réseau externe n’est supprimé.
