# ToiseApp

Application WPF de pilotage d'une **toise motorisée Linak DL14** connectée en USB via un pont USB-to-LIN.

---

## Objectif

ToiseApp permet à un opérateur (médical ou ergonomique) de contrôler la hauteur d'une colonne motorisée depuis un poste fixe :

- Monter / descendre en continu
- Déplacer par **incrément configurable (mm)**
- Atteindre une **hauteur cible précise (mm)**
- Stopper immédiatement le mouvement
- Visualiser la position courante en **temps réel (mm / cm)**
- Détecter une déconnexion USB et permettre une **reconnexion sans redémarrage**

L'unité utilisateur est le **millimètre**. La position zéro physique correspond à **1530 mm (153 cm)** plancher, codée en dur dans le SDK Linak (`_hauteurAZero = 15300`).

---

## Stack technique

| Domaine | Choix |
|---|---|
| Framework | WPF / .NET 4.8 |
| Pattern | MVVM (RelayCommand, INotifyPropertyChanged) |
| SDK matériel | SitStand / Usb2Lin (Linak, DLL propriétaire) |
| Dépendance métier | `ACTVerinDL14` + `ACTValise` (projet WpfActiback) |

---

## Structure du projet

```
ToiseApp/
├── App.xaml / App.xaml.cs        ← Composition root
├── Helpers/RelayCommand.cs       ← ICommand générique
├── Model/
│   ├── ToiseService.cs           ← Façade sur ACTVerinDL14 (unités mm)
│   └── DefaultValise.cs          ← Stub ACTValise sans capteur de force
├── ViewModel/ToiseViewModel.cs   ← Logique de présentation
└── View/MainWindow.xaml(.cs)     ← UI déclarative
```

---

## Prise en main rapide

1. Référencer `WpfActiback.dll` et `SitStand.dll` dans `ToiseApp.csproj`
2. Adapter `DefaultValise.Create(hauteurMaxiCm: 210)` dans `App.xaml.cs` si une vraie `ACTValise` est disponible
3. Compiler en **x86** (contrainte du SDK Linak)
4. Connecter la toise en USB avant de lancer l'application

---

## Workflow Git

### Branches principales

| Branche | Rôle |
|---|---|
| `main` | Code stable, releasable à tout moment — **protégée** |
| `develop` *(optionnelle)* | Intégration des features avant merge sur `main` |

### Branches de travail

Le nom de branche suit le format : `<type>/<description-courte>`

| Préfixe | Usage | Exemple |
|---|---|---|
| `feat/` | Nouvelle fonctionnalité | `feat/historique-positions` |
| `fix/` | Correction de bug | `fix/deconnexion-usb-crash` |
| `chore/` | Maintenance, outillage, dépendances, refactoring sans impact métier | `chore/upgrade-dotnet48` |
| `docs/` | Documentation uniquement | `docs/readme-setup` |

### Cycle de vie d'une branche

```
main
 └── feat/ma-feature          ← créée depuis main
      │   [commits]
      └── Pull Request → main  ← revue + merge squash ou merge commit
```

```bash
# Créer une branche depuis main
git checkout main
git pull origin main
git checkout -b feat/ma-feature

# Pousser et ouvrir une PR
git push -u origin feat/ma-feature
# → ouvrir la PR sur GitHub vers main
```

### Règles sur `main`

- **Pas de push direct** sur `main`
- Tout changement passe par une **Pull Request**
- La PR doit être relue avant merge (au moins 1 approbation si équipe > 1)
- Le titre de la PR reprend le format `<type>: <description>` :
  - `feat: ajout historique des positions CSV`
  - `fix: crash SEHException lors déconnexion USB`
  - `chore: mise à jour références SitStand`

### Messages de commit

Format : `<type>(<scope optionnel>): <message court en impératif>`

```
feat(viewmodel): ajouter déplacement par incrément configurable
fix(service): gérer SEHException sur perte USB sans crash
chore(deps): référencer SitStand v2.3.1
docs: mettre à jour README section installation
```

### Versioning et tags

Les releases sont taguées sur `main` :

```bash
git tag -a v1.0.0 -m "Première release stable"
git push origin v1.0.0
```

Format de version : `vMAJEUR.MINEUR.PATCH` (semver)

---

## Contribution

1. Créer une branche depuis `main` avec le bon préfixe
2. Commiter des changements atomiques avec des messages clairs
3. Ouvrir une Pull Request vers `main`
4. Attendre la revue et corriger les retours
5. Merger après approbation (pas de force-push sur `main`)
