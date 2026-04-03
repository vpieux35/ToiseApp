# ToiseApp — Cahier des charges & spécifications techniques

## Contexte et besoin métier

Application de contrôle d'une **toise motorisée Linak DL14** connectée en USB via un pont USB-to-LIN (bibliothèque `SitStand` / `Usb2Lin`).

L'application est destinée à un usage médical ou ergonomique : piloter la hauteur d'une toise (colonne motorisée) depuis un poste opérateur.

---

## Cahier des charges fonctionnel

| # | Exigence | Priorité |
|---|---|---|
| F1 | Monter la toise (mouvement continu) | Obligatoire |
| F2 | Descendre la toise (mouvement continu) | Obligatoire |
| F3 | Définir un **pas de déplacement en mm** et monter/descendre par incréments | Obligatoire |
| F4 | Saisir une **hauteur cible en mm** et déplacer la toise vers cette hauteur | Obligatoire |
| F5 | Arrêt immédiat du mouvement | Obligatoire |
| F6 | Affichage en temps réel de la position courante (mm et cm) | Obligatoire |
| F7 | Détection de déconnexion USB et signal visuel | Obligatoire |
| F8 | Reconnexion au vérin sans redémarrer l'application | Souhaitable |

---

## Contraintes techniques

- **Bibliothèque `SitStand` / `Usb2Lin`** : SDK propriétaire Linak, distribué en DLL.  
  Seule dépendance externe. Lève des `SEHException` et `ConnectionException` lors de déconnexions USB.
- **`VerinDL14`** : classe écrite dans ToiseApp (`Model/VerinDL14.cs`). N'utilise que `SitStand.dll`. Inspirée de `VerinDL14.cs` (WpfSpineoPTV). Aucun lien avec WpfActiback.
- **Unités internes** : `VerinDL14` travaille en **cm** (float). Le point zéro physique est `HauteurAZero = 15300` (unités internes = 1/100 de cm, soit 153 cm plancher).
- **Unités exposées à l'utilisateur** : **mm** (conversion dans `ToiseService`).
- **Pas de vérification de charge** : sans capteur de force Arduino, `MoveDown()` descend sans condition (contrairement à `VerinDL14` qui vérifiait `MyF < ForceToise`).

---

## API de `VerinDL14` (Model/VerinDL14.cs)

### Méthodes publiques

| Méthode | Remarque |
|---|---|
| `OpenVerin()` | Retourne bool. Appelé automatiquement dans le constructeur. Peut être rappelé pour reconnecter. |
| `MoveUp()` | Mouvement continu vers le haut |
| `MoveDown()` | Mouvement continu vers le bas (sans vérification de charge) |
| `Stop()` | Envoie commande 0x80 |
| `MoveToToise(cm)` | Boucle jusqu'à ±0.01 cm de la cible — bloquant, à appeler via `Task.Run` |
| `GetPositionToise()` | Retourne la hauteur en **cm** |
| `CloseDevice()` | Guard anti-réentrance via `Interlocked` |

### Événements

- `VerinDisconnected` : déclenché via `SetDisconnected()` à la première détection d'erreur.  
  Relayé par `ToiseService.Disconnected` vers le ViewModel.

### Points de fragilité

- **SEHException** : levée par le SDK Linak lors de perte USB. Gérée via `SafeCloseHandle()` + `GC.SuppressFinalize`.
- **Double libération du handle** : corrigée par `_closingFlag` Interlocked + suppression du finalizer avant `CloseDevice()`.
- **Ré-entrance sur `CloseDevice()`** : protégée par `Interlocked.CompareExchange`.

---

## Architecture

### Pattern : MVVM (MVC adapté à WPF)

```
┌─────────────────────────────────────────────────────────┐
│  View (MainWindow.xaml)                                 │
│  - DataBinding → ToiseViewModel                        │
│  - Events → Commands (ICommand)                        │
└────────────────┬────────────────────────────────────────┘
                 │ DataContext / ICommand
┌────────────────▼────────────────────────────────────────┐
│  ViewModel (ToiseViewModel)                             │
│  - Propriétés bindables (INotifyPropertyChanged)       │
│  - Commandes (RelayCommand)                            │
│  - DispatcherTimer (refresh position 500 ms)           │
│  - Gestion async des déplacements (Task.Run)           │
└────────────────┬────────────────────────────────────────┘
                 │ appels métier
┌────────────────▼────────────────────────────────────────┐
│  ToiseService (Model)                                   │
│  - Wraps VerinDL14                                     │
│  - Conversion mm ↔ cm                                  │
│  - Relaye VerinDisconnected                            │
│  - IDisposable → CloseDevice() à la fermeture          │
└────────────────┬────────────────────────────────────────┘
                 │ composition
┌────────────────▼────────────────────────────────────────┐
│  VerinDL14 (ToiseApp.Model — dans ce projet)           │
│  + Usb2Lin  (SDK Linak SitStand — DLL propriétaire)    │
└─────────────────────────────────────────────────────────┘
```

### Structure des fichiers

```
ToiseApp/
├── CLAUDE.md                       ← ce fichier
├── ToiseApp.csproj
├── App.xaml                        ← ShutdownMode = OnMainWindowClose
├── App.xaml.cs                     ← Composition root (wiring manuel)
├── Helpers/
│   └── RelayCommand.cs             ← ICommand générique (Action + Func<bool>)
├── Model/
│   ├── VerinDL14.cs                ← Pilotage direct Linak via Usb2Lin
│   └── ToiseService.cs             ← Façade sur VerinDL14, unités en mm
├── ViewModel/
│   └── ToiseViewModel.cs           ← Toute la logique de présentation
└── View/
    ├── MainWindow.xaml             ← UI déclarative, zéro logique métier
    └── MainWindow.xaml.cs          ← OnClosed → Dispose du ViewModel
```

---

## Choix technologiques

| Domaine | Choix | Justification |
|---|---|---|
| Framework UI | **WPF (.NET 4.8)** | Même stack que `WpfActiback` ; évite les conflits de runtime |
| Pattern | **MVVM** | Standard WPF ; binding natif ; testabilité du ViewModel sans UI |
| Commandes | **RelayCommand** | Pattern standard, évite les dépendances à des frameworks lourds (Prism, MVVM Toolkit) |
| Refresh position | **DispatcherTimer** | S'exécute sur le thread UI ; pas besoin de `Dispatcher.Invoke` pour les propriétés bindées |
| Déplacements bloquants | **Task.Run** | `MoveToToise` est synchrone et bloquant dans le SDK ; on l'isole en tâche de fond |
| Injection de dépendances | **Manuelle (Composition Root)** | Projet simple ; pas de conteneur IoC pour éviter la surcharge |
| Unité exposée | **mm** | Plus précis et plus naturel pour l'utilisateur final |

---

## Points d'intégration à configurer

### 1. Ajout des DLLs tierces

#### Répertoire à créer

Créer un dossier `libs/` **à la racine du projet** (au même niveau que `ToiseApp.csproj`) :

```
ToiseApp/
├── libs/
│   ├── WpfActiback.dll   ← copier ici
│   ├── SitStand.dll      ← copier ici (SDK Linak)
│   └── Usb2Lin.dll       ← copier ici (pont USB-to-LIN)
├── ToiseApp.csproj
└── ...
```

> **Commande PowerShell pour créer le dossier :**
> ```powershell
> mkdir libs
> ```
> Puis copier les DLLs fournies par Linak / WpfActiback dans ce dossier.

#### Référence dans `ToiseApp.csproj`

Les références sont déjà configurées dans le `.csproj` avec `HintPath` pointant vers `libs\` :

```xml
<ItemGroup>
  <Reference Include="WpfActiback">
    <HintPath>libs\WpfActiback.dll</HintPath>
  </Reference>
  <Reference Include="SitStand">
    <HintPath>libs\SitStand.dll</HintPath>
  </Reference>
  <Reference Include="Usb2Lin">
    <HintPath>libs\Usb2Lin.dll</HintPath>
  </Reference>
</ItemGroup>
```

Les DLLs sont aussi déclarées en `Content` avec `CopyToOutputDirectory = PreserveNewest` : elles seront automatiquement copiées dans `bin\Debug\` ou `bin\Release\` à chaque build.

#### Git — ne pas versionner les DLLs binaires

Ajouter dans `.gitignore` :

```
libs/
```

> Les DLLs Linak sont propriétaires et ne doivent pas être poussées sur GitHub.  
> Les développeurs doivent les obtenir séparément et les placer dans `libs/` manuellement.

#### Alternative : référencer WpfActiback comme projet source

Si WpfActiback est disponible en code source dans la même solution, commenter le bloc `<Reference>` et décommenter :

```xml
<ProjectReference Include="..\WpfActiback\WpfActiback.csproj" />
```

### 2. `DefaultValise` vs vraie `ACTValise`

Dans `App.xaml.cs`, ligne ~20 :

```csharp
// Sans capteur de force (ForceToise = "9999", MyF = 0)
ACTValise valise = DefaultValise.Create(hauteurMaxiCm: 210);

// Avec votre vraie configuration
ACTValise valise = new ACTValise(/* paramètres réels */);
```

> **Important** : `DefaultValise.cs` est un stub qui doit être adapté à la vraie
> structure de `ACTValise`. Si `ACTValise` a un constructeur paramétré ou charge
> un fichier XML, ajustez en conséquence.

### 3. Hauteur plancher

`_hauteurAZero = 15300` est codé en dur dans `VerinDL14`.  
La position minimale physique est donc **1530 mm (153 cm)**.  
La valeur par défaut de `TargetHeightMm` dans le ViewModel est initialisée à `1530f`.

---

## Comportements importants à connaître

- **Refresh désactivé pendant `IsBusy`** : quand un `MoveToHeight` (async) est en cours, le timer ne lit pas la position pour éviter les conflits d'accès au handle.
- **`Stop()` annule le `MoveToHeight` en cours** : via `CancellationTokenSource` dans `ToiseViewModel`.
- **Dispose chain** : `MainWindow.OnClosed` → `ToiseViewModel.Dispose()` → `ToiseService.Dispose()` → `VerinDL14.CloseDevice()`.
- **Thread-safety** : tous les retours vers l'UI depuis `Task.Run` passent par `Application.Current.Dispatcher.Invoke`.

---

## Support Linux — Stratégie de build multi-plateforme

### Contrainte fondamentale

**WPF ne fonctionne que sur Windows.** Pour Linux, il faut :

| Couche | Windows | Linux |
|---|---|---|
| UI framework | WPF (.NET 4.8) | **Avalonia UI** (.NET 8) |
| Driver vérin | `VerinDL14.cs` (SitStand SDK) | **`VerinDL14Linux.cs`** (HidSharp) |
| Runtime | `net48` | `net8.0` |

### Structure recommandée (deux projets dans une solution)

```
ToiseApp.sln
├── ToiseApp/               ← projet Windows (net48, WPF, SitStand)
│   ├── Model/
│   │   ├── VerinDL14.cs
│   │   ├── VerinDL14Linux.cs   ← fichier présent mais non référencé dans ce csproj
│   │   └── ToiseService.cs
│   └── ...
└── ToiseApp.Linux/         ← projet Linux (net8.0, Avalonia)  [à créer]
    ├── App.axaml
    ├── App.axaml.cs        ← composition root avec VerinDL14Linux
    ├── Views/
    │   └── MainWindow.axaml
    ├── ViewModels/
    │   └── ToiseViewModel.cs   ← identique ; remplacer DispatcherTimer par timer Avalonia
    └── ToiseApp.Linux.csproj
```

> Pour éviter la duplication, `ToiseService.cs`, `ToiseViewModel.cs` et `RelayCommand.cs`
> peuvent être partagés via un projet bibliothèque `ToiseApp.Core` (net8.0) si le projet grossit.

### `VerinDL14Linux.cs` — Implémentation HidSharp

Fichier : `Model/VerinDL14Linux.cs`  
Dépendance NuGet : `HidSharp` (≥ 2.1.0)

**Correspondance avec l'implémentation Windows :**

| Windows (SitStand SDK) | Linux (HidSharp) |
|---|---|
| `new Usb2Lin()` + `FindAllLinakDevices()` | `DeviceList.Local.GetHidDevices(0x12D3)` |
| `_cbd6.OpenDevice(path)` → `SafeHandle` | `device.TryOpen(out HidStream stream)` |
| `_cbd6.SetFeature(handle, buf[64])` | `stream.SetFeatureReport(new byte[65] { reportId, ...buf })` |
| `_cbd6.GetFeature(handle)` → `byte[64]` | `stream.GetFeatureReport(buf[65])` → payload = `buf[1..64]` |
| `SEHException` + `ConnectionException` | `IOException` / `TimeoutException` |

**Point clé HidSharp** : `SetFeatureReport` / `GetFeatureReport` exigent `byte[0]` = Report ID.  
Le payload de 64 octets (identique au protocole Windows) commence à `byte[1]`.  
`HidReportId = 0x00` est la valeur par défaut pour les devices à rapport unique.  
Si le device en expose plusieurs, l'identifier avec : `lsusb -v | grep bReportID`

**Prérequis système Linux** — règle udev pour accès sans root :
```bash
echo 'SUBSYSTEM=="hidraw", ATTRS{idVendor}=="12d3", MODE="0666"' \
     | sudo tee /etc/udev/rules.d/99-linak.rules
sudo udevadm control --reload-rules && sudo udevadm trigger
```

Vérifier que le device est détecté :
```bash
lsusb | grep -i linak       # → Bus 001 Device 003: ID 12d3:xxxx Linak
ls /dev/hidraw*              # → /dev/hidraw0 (ou similaire)
```

### Générer les builds

#### Build Windows (projet actuel)

```powershell
# Debug
dotnet build ToiseApp/ToiseApp.csproj -c Debug

# Release auto-contenu (un seul exe + DLLs dans bin\Release\net48\)
dotnet build ToiseApp/ToiseApp.csproj -c Release
```

> Les DLLs Linak (`SitStand.Base.dll`, `LinakUsbDll.dll`) doivent être dans `libs/`
> et seront copiées automatiquement dans le dossier de sortie.

#### Build Linux (projet ToiseApp.Linux — à créer)

```bash
# Créer le projet Avalonia (une fois)
dotnet new avalonia.mvvm -n ToiseApp.Linux -f net8.0

# Ajouter HidSharp
cd ToiseApp.Linux
dotnet add package HidSharp

# Build
dotnet build -c Release

# Publish — exécutable self-contained pour Linux x64
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux-x64

# Publish — exécutable self-contained pour Linux ARM64 (Raspberry Pi 4, etc.)
dotnet publish -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
```

#### Matrice des RIDs (Runtime Identifiers)

| Cible | RID | Notes |
|---|---|---|
| Windows x64 | `win-x64` | Standard PC |
| Windows x86 | `win-x86` | 32 bits (rare) |
| Linux x64 | `linux-x64` | Standard PC/serveur |
| Linux ARM64 | `linux-arm64` | Raspberry Pi 4, Pi 5 |
| Linux ARM32 | `linux-arm` | Raspberry Pi 2/3 |

---

## Évolutions possibles

- Ajouter une **limite basse configurable** (actuellement gérée uniquement côté `VerinDL14`).
- Intégrer le **retour capteur de force** si un Arduino est présent (`MonManagerThread.MyF`).
- Ajouter un **historique des positions** (log CSV).
- Passer à **MVVM Toolkit** (CommunityToolkit.Mvvm) si le projet grossit (source generators, `ObservableProperty`).
- Extraire **`ToiseApp.Core`** (bibliothèque net8.0 partagée) pour mutualiser `ToiseService`, `ToiseViewModel`, `RelayCommand` entre le projet Windows et Linux.
