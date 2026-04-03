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

| Domaine | Windows | Linux |
|---|---|---|
| Framework UI | WPF / .NET 4.8 | Avalonia UI / .NET 8 |
| Pattern | MVVM (RelayCommand, INotifyPropertyChanged) | idem |
| SDK matériel | SitStand / Usb2Lin (Linak, DLL propriétaire) | HidSharp (USB HID direct) |
| Projet | `ToiseApp.csproj` | `ToiseApp.Linux/ToiseApp.Linux.csproj` |

---

## Structure du projet

```
ToiseApp/                              ← projet Windows (net48 + WPF)
├── App.xaml / App.xaml.cs            ← Composition root
├── Helpers/RelayCommand.cs           ← ICommand générique
├── Model/
│   ├── VerinDL14.cs                  ← Pilotage via SitStand SDK
│   ├── VerinDL14Linux.cs             ← Pilotage via HidSharp (partagé Linux)
│   └── ToiseService.cs               ← Façade unités mm (Windows)
├── ViewModel/ToiseViewModel.cs       ← Logique de présentation
└── View/MainWindow.xaml(.cs)         ← UI WPF

ToiseApp.Linux/                        ← projet Linux (net8.0 + Avalonia)
├── App.axaml / App.axaml.cs          ← Composition root
├── Helpers/RelayCommand.cs           ← ICommand sans CommandManager WPF
├── Models/ToiseService.cs            ← Façade unités mm (Linux)
├── ViewModels/ToiseViewModel.cs      ← Même logique, threading Avalonia
└── Views/MainWindow.axaml(.cs)       ← UI Avalonia
```

---

## Prérequis

### DLLs propriétaires Linak (Windows uniquement)

Créer un dossier `libs/` à la racine et y copier :

```
libs/
├── SitStand.Base.dll
└── LinakUsbDll.dll
```

> Ces DLLs ne sont pas versionnées (voir `.gitignore`). Les obtenir auprès de Linak.

---

## Déploiement Windows

### Build

```powershell
dotnet build ToiseApp.csproj -c Release
```

L'exécutable et les DLLs Linak sont copiés automatiquement dans `bin\Release\net48\`.

### Lancement

1. Placer les DLLs Linak dans `libs/`
2. Connecter la toise en USB
3. Lancer `bin\Release\net48\ToiseApp.exe`

---

## Déploiement Linux

### 1. Publier depuis Windows (cross-compilation)

```bash
cd ToiseApp.Linux
dotnet publish -c Release -r linux-x64 --self-contained -o ../publish/linux-x64
```

> Pour Raspberry Pi 4/5, remplacer `linux-x64` par `linux-arm64`.

### 2. Transférer sur la machine Linux

```bash
# Via SCP
scp -r publish/linux-x64 user@192.168.x.x:/home/user/ToiseApp

# Ou copier le dossier publish/linux-x64/ via clé USB / partage réseau
```

### 3. Configurer la machine Linux (une seule fois)

```bash
# Rendre l'exécutable lançable
chmod +x /home/user/ToiseApp/ToiseApp.Linux

# Règle udev pour accès USB Linak sans root
echo 'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="12d3", MODE="0666"' \
     | sudo tee /etc/udev/rules.d/99-linak.rules
sudo udevadm control --reload-rules && sudo udevadm trigger

# Dépendances système pour Avalonia (Debian/Ubuntu)
sudo apt install libgl1 libice6 libsm6 libx11-6 libfontconfig1

# Dépendances système pour Avalonia (Fedora/RHEL)
sudo dnf install mesa-libGL libICE libSM libX11 fontconfig
```


### Diagnostic

| Symptôme | Commande |
|---|---|
| Vérin non détecté | `lsusb \| grep -i linak` |
| Accès refusé `/dev/hidraw*` | `ls -la /dev/hidraw*` → vérifier `MODE="0666"` |
| Erreur d'affichage | `echo $DISPLAY` → doit retourner `:0` |
| Bibliothèque manquante | `ldd ./ToiseApp.Linux \| grep "not found"` |

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
