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

## Contraintes techniques héritées

- **Classe existante `ACTVerinDL14`** : fournie dans le projet `WpfActiback`. Ne pas modifier.
- **Dépendance `ACTValise`** : objet de configuration central de WpfActiback.  
  Requis par `MoveDown`, `MoveToToise`, `MoveTo`, `MoveToFormatFloat`.  
  Contient : seuil de force (`ForceToise`), hauteur maxi (`HauteurToise`), lecture capteur (`MonManagerThread.MyF`).
- **Bibliothèque `SitStand` / `Usb2Lin`** : SDK propriétaire Linak, distribué en DLL.  
  Lève des `SEHException` et `ConnectionException` lors de déconnexions USB.
- **Unités internes** : `ACTVerinDL14` travaille en **cm** (float). Le point zéro physique est `_hauteurAZero = 15300` (unités internes = 1/100 de cm, soit 153 cm plancher).
- **Unités exposées à l'utilisateur** : **mm** (conversion dans `ToiseService`).

---

## Spécifications déduites de l'analyse de `ACTVerinDL14`

### Méthodes disponibles et leurs prérequis

| Méthode | ACTValise requis | Remarque |
|---|---|---|
| `MoveUp()` | Non | Mouvement continu vers le haut |
| `MoveDown(valise)` | Oui | Vérifie `MyF < ForceToise` avant de descendre |
| `Stop()` | Non | Envoie commande 0x80 |
| `MoveToToise(cm, valise)` | Oui | Boucle jusqu'à ±0.01 cm de la cible |
| `MoveTo(cm, valise)` | Oui | Boucle jusqu'à ±0.1 cm de la cible (moins précis) |
| `GetPositionToise()` | Non | Retourne la hauteur en **cm** |
| `OpenVerin()` | Non | Retourne bool, met à jour `EtatCarteArduinoVerin` |
| `CloseDevice()` | Non | Guard anti-réentrance via `Interlocked` |

### Événements

- `VerinDisconnected` : déclenché via `SetDisconnected()` à la première détection d'erreur.  
  À écouter pour mettre à jour l'UI sans polling.

### Points de fragilité documentés dans le code source

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
│  - Wraps ACTVerinDL14                                  │
│  - Conversion mm ↔ cm                                  │
│  - Relaye VerinDisconnected                            │
│  - IDisposable → CloseDevice() à la fermeture          │
└────────────────┬────────────────────────────────────────┘
                 │ composition
┌────────────────▼────────────────────────────────────────┐
│  ACTVerinDL14 (WpfActiback — ne pas modifier)          │
│  + ACTValise  (config, capteur de force, Arduino)      │
│  + Usb2Lin    (SDK Linak SitStand)                     │
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
│   ├── ToiseService.cs             ← Façade sur ACTVerinDL14, unités en mm
│   └── DefaultValise.cs            ← Stub ACTValise sans capteur de force
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

### 1. Référence projet dans `ToiseApp.csproj`

```xml
<!-- Si WpfActiback est un projet de la même solution -->
<ProjectReference Include="..\WpfActiback\WpfActiback.csproj" />

<!-- Si c'est une DLL précompilée -->
<Reference Include="WpfActiback">
  <HintPath>..\libs\WpfActiback.dll</HintPath>
</Reference>
<Reference Include="SitStand">
  <HintPath>..\libs\SitStand.dll</HintPath>
</Reference>
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

`_hauteurAZero = 15300` est codé en dur dans `ACTVerinDL14`.  
La position minimale physique est donc **1530 mm (153 cm)**.  
La valeur par défaut de `TargetHeightMm` dans le ViewModel est initialisée à `1530f`.

---

## Comportements importants à connaître

- **Refresh désactivé pendant `IsBusy`** : quand un `MoveToHeight` (async) est en cours, le timer ne lit pas la position pour éviter les conflits d'accès au handle.
- **`Stop()` annule le `MoveToHeight` en cours** : via `CancellationTokenSource` dans `ToiseViewModel`.
- **Dispose chain** : `MainWindow.OnClosed` → `ToiseViewModel.Dispose()` → `ToiseService.Dispose()` → `ACTVerinDL14.CloseDevice()`.
- **Thread-safety** : tous les retours vers l'UI depuis `Task.Run` passent par `Application.Current.Dispatcher.Invoke`.

---

## Évolutions possibles

- Ajouter une **limite basse configurable** (actuellement gérée uniquement côté `ACTVerinDL14`).
- Intégrer le **retour capteur de force** si un Arduino est présent (`MonManagerThread.MyF`).
- Ajouter un **historique des positions** (log CSV).
- Passer à **MVVM Toolkit** (CommunityToolkit.Mvvm) si le projet grossit (source generators, `ObservableProperty`).
