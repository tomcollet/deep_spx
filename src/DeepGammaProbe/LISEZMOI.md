# Sonde DeepGamma : protocole de test (version 4)

La sonde est un indicateur DeepCharts **en lecture seule** : aucun ordre, rien de dessiné, le graphique n'est pas
modifié.

**Deepchart refuse de charger une DLL qui utilise fichiers, réseau, réflexion, threads ou `lock`**
([troubleshooting](https://docs-indicators.deepcharts.com/troubleshooting)). La sonde envoie donc ses enregistrements au
**log de Deepchart** (lignes `DSPX;`). **Constat du 04/11/2026 : ce log est chiffré sur disque**
(`Documents\Deepchart\Logs\various_*.log`), donc illisible hors de DeepCharts. Depuis la v4, la sonde affiche aussi
ses verdicts, ses compteurs et les séries lisibles (avec leurs noms) dans **un panneau de texte en haut à gauche du
graphique** : c'est ce panneau qu'on lit, en capture d'écran. `tools/extract_dspx.py` ne sert que si une fenêtre de
DeepCharts permet un jour de copier le log en clair.

Elle répond à huit questions :

| # | Question | Lignes du log |
|---|---|---|
| Q1 | La DLL lit-elle les séries des indicateurs DeepGamma ? | `CAT` |
| Q2 | Ces séries ont-elles un historique ? | `HSER` |
| Q3 | Le carnet arrive-t-il, sur combien de niveaux ? | `BOOKEX`, `BOOKSNAP`, `BOOKMIN` |
| Q4 | Les trades historiques arrivent-ils ? | `HIST`, `TICK1HIST` |
| Q5 | Le log sert-il de canal de sortie ? | toutes |
| Q6 | Le graphique est-il un replay (`VAn.IsReplayData`) ? | `META`, `VERDICT` |
| Q7 | En live, les séries DeepGamma valent-elles 0 ? (issue #4 du dépôt officiel) | `SER`, `VERDICT` |
| Q8 | Le carnet arrive-t-il ordre par ordre (MBO), avec les identifiants d'ordre dans les trades ? | `BOOKEX`, `BOOKMIN`, `TICK1RT`, `VERDICT` |

Les verdicts sont réécrits à la fin de l'historique, puis toutes les 5 minutes de bourse (lignes `VERDICT`).

---

## 0. Avant de compiler (2 min, sans lancer DeepCharts)

- **Version de DeepCharts : 16.5.4 au minimum.** En 16.5.3, `HistRT` ne livrait aucun trade historique (issue #1,
  corrigée en 16.5.4 d'après Volumetrica).
- **Inventaire de l'API** (fait le 04/11/2026, VolSysAPI 1.4.6.0) :
  ```
  dotnet run --project tools\ApiDump
  ```
  À refaire après chaque mise à jour de DeepCharts : si une signature change, la sonde et les stubs doivent suivre.
  `deepchart-api.txt` n'est pas committé, car il décrit des DLL propriétaires.

## 1. Compilation et contrôle

```
dotnet build src\DeepGammaProbe -p:DeepchartDevId=TON-ID
dotnet run --project tools\DllCheck -- "$env:USERPROFILE\Documents\Deepchart\Indicators\DeepSpx.Probe.dll"
```

La DLL `DeepSpx.Probe.dll` va dans `Documents\Deepchart\Indicators`, et DeepCharts la recharge seul. `DllCheck` doit
répondre `OK ... aucune interdite`. Si Deepchart refuse quand même la DLL, une notification « not allowed » s'affiche
et son log en donne la raison : envoie-moi cette ligne.

**Si la compilation échoue sur `GetSeriesValueById`**, le message du compilateur et `deepchart-api.txt` donnent la
vraie signature : envoie-les-moi.

## 2. Le graphique

1. **New → Option Chart → SPX** (symbole ES-CME). Unité **1 minute**, **5 jours** d'historique.
2. **Ajoute les indicateurs DeepGamma de ton offre** : Option Summary (source *Gex (market maker)*), Option
   Levels/Envelopes, Option Levels / OI (RT) en source *Market Maker*, Expected Move, et si possible Net Flow, Option
   Delta et Market Maker Pressure.
3. **Ajoute la sonde en dernier** : liste des indicateurs → *Personal* → **Probe - DeepGamma Recorder Test**.
4. **Note l'ordre d'ajout des indicateurs.** Les numéros lus par la sonde suivent sans doute cet ordre (dans l'issue
   #4, Market Maker Pressure est l'indicateur 3) : c'est ce qui permettra de nommer chaque série.

## 3. Les trois tests

**A. Historique (tout de suite, même marché fermé)** : attends la fin du chargement. Ce test répond à Q1, Q2, Q4 et Q5.

**B. Live (30 min de séance US, 15h30-22h heure de Paris)** : ce test répond à Q3 en live, et à Q7, le point critique.
Si les séries DeepGamma valent 0 en live alors qu'elles bougent à l'écran, c'est le bug de l'issue #4.

**C. Deep Replay (30 min en vitesse x10)** : une séance passée, même graphique, même sonde. Ce test dit si DeepGamma est
lisible en replay (l'issue #4 dit oui) et **si le replay rejoue le carnet** (lignes `BOOKMIN` non vides).

## 4. Récupérer les résultats

**Panneau (v4)** : une capture d'écran du graphique suffit (fais-en une après le test A, à 15h45 pendant le test B, et à
la fin du test C). Si les séries lisibles dépassent la hauteur du graphique, augmente la hauteur de la fenêtre ou
baisse `PanelFontSize`.

**Fenêtre Log de DeepCharts** : elle affiche les lignes `DSPX;` en clair, mais Ctrl+A / Ctrl+C n'y copie que l'heure
et le niveau. `tools/read_log_window.py` la lit par l'API d'accessibilité de Windows (comme un lecteur d'écran) :

```powershell
pip install uiautomation
python tools\read_log_window.py --out data\probe\testA\log.txt            # lignes visibles
python tools\read_log_window.py --follow --out data\probe\testB\log.txt   # en continu pendant le test B
python tools\extract_dspx.py data\probe\testA\log.txt --out data\probe\testA
```

Si rien n'est lu : `python tools\read_log_window.py --dump` affiche la structure de la fenêtre, à m'envoyer.

**Fichier de log** (chiffré, inutilisable tel quel) :

Trouve le log de Deepchart qui contient les lignes de la sonde (PowerShell, après un test) :

```powershell
Get-ChildItem "$env:USERPROFILE\Documents\Deepchart", $env:LOCALAPPDATA, $env:APPDATA -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-2) } |
  Select-String -Pattern 'DSPX;' -List | Select-Object Path
```

Pour suivre les lignes en direct (et mesurer le délai du log : chaque `BOOKMIN` doit apparaître juste après le
changement de minute) :

```powershell
Get-Content -Path "CHEMIN_DU_LOG" -Wait -Tail 0 | Select-String "DSPX;"
```

Puis :

```
python tools\extract_dspx.py CHEMIN_DU_LOG --out data\probe\testA
```

Un fichier CSV par type de ligne, et les derniers verdicts à l'écran. **Envoie-moi le dossier de chaque test** (et
`deepchart-api.txt`). `data/` n'est pas committé.

## Types de lignes

| Ligne | Contenu |
|---|---|
| `META` | Version, symbole, graphique, replay, phase du graphique, paramètres |
| `HEAD` | En-tête des lignes suivantes |
| `HIST` | Fin de l'historique : nombre de barres et de trades historiques, première et dernière barre |
| `IND` | Numéro, nom, état masqué et noms des séries de chaque indicateur du graphique (`GetParamsById`) |
| `CAT` | Catalogue : valeur de chaque série testée maintenant et 10, 60 et 390 barres avant, nombre de valeurs par point, ou l'erreur |
| `HSER` | Par série lisible : barres renseignées sur l'historique relu, valeurs distinctes, min, max, première et dernière |
| `SER` | Par série lisible et par minute de temps réel : la valeur |
| `BOOKEX`, `BOOKSNAP` | Premières mises à jour du carnet et état complet (`RebuildDom`) |
| `BOOKMIN` | Par minute : mises à jour, resets, niveaux bid/ask, meilleurs prix, profondeur en ticks, quantités sur 10 niveaux, mises à jour MBO |
| `TICKMIN` | Par minute : trades, volume, volume à l'ask et au bid, trades portant les identifiants d'ordre |
| `TICK1HIST`, `TICK1RT` | Premier trade historique et premier trade temps réel, tous champs |
| `SUM`, `VERDICT`, `ERR` | Compteurs, verdicts Q1 à Q8, erreurs |

## Paramètres de la sonde

| Paramètre | Défaut | Rôle |
|---|---|---|
| IndicatorIdStart | 0 | Premier numéro d'indicateur testé |
| MaxIndicators | 30 | Nombre de numéros testés |
| MaxSeries | 12 | Séries testées par indicateur |
| HistoryBarsToScan | 600 | Barres passées relues à la fin du chargement |
| RtSeriesMinutes | 60 | Minutes de temps réel pendant lesquelles les séries sont écrites |
| RtStatsMinutes | 60 | Minutes de temps réel pendant lesquelles carnet et trades sont résumés |

## Vérifier sans DeepCharts

```
dotnet build tests\ProbeCompileCheck
dotnet run --project tools\DllCheck -- tests\ProbeCompileCheck\bin\Debug\net10.0\ProbeCompileCheck.dll
dotnet run --project tests\ProbeHarness
```

Le banc simule un graphique (historique, replay, trois faux indicateurs dont un à 0 en temps réel, un carnet par prix et par ordre) et lit
les lignes `DSPX;` écrites dans le faux log. Il doit finir sur douze lignes `OK`.
