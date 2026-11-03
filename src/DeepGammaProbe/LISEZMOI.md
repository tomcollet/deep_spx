# Sonde DeepGamma : protocole de test

La sonde est un indicateur DeepCharts **en lecture seule**. Elle n'envoie aucun ordre, ne dessine rien et ne modifie
pas le graphique. Elle écrit uniquement dans `Documents\Deepchart\Probe\<date_heure>\`, avec un nouveau dossier à
chaque chargement.

Elle répond à six questions :

| # | Question | Réponse dans |
|---|---|---|
| Q1 | La DLL lit-elle les séries des indicateurs DeepGamma ? | `summary.txt`, `series_catalog_*.csv` |
| Q2 | Ces séries ont-elles un historique ? | `summary.txt`, `series_values.csv` |
| Q3 | Le carnet arrive-t-il (OnBook, RebuildDom) ? | `summary.txt`, `book_rt.txt`, `book_snapshot.txt` |
| Q4 | Les trades historiques arrivent-ils ? | `summary.txt`, `ticks_hist.csv` |
| Q5 | La DLL peut-elle écrire des fichiers ? | `file_write_test.txt` |
| Q6 | Tout ça marche-t-il en Deep Replay ? | `summary.txt` (test C) |

---

## 0. Inventaire de l'API (2 min, sans lancer DeepCharts)

Depuis le dossier du dépôt :

```
dotnet run --project tools\ApiDump
```

L'outil lit `VolSysAPI.dll` et `VolumetricaCore.dll` comme des fichiers et écrit `deepchart-api.txt`. On y trouve la
signature exacte de `GetSeriesValueById`, que la doc ne donne pas. Envoie-moi ce fichier : il n'est pas committé, car
il décrit des DLL propriétaires.

## 1. Compilation

```
dotnet build src\DeepGammaProbe -p:DeepchartDevId=TON-ID
```

La DLL `DeepSpx.Probe.dll` est écrite dans `Documents\Deepchart\Indicators`, et DeepCharts la recharge sans
redémarrer. Le developer id reste en ligne de commande pour ne jamais être committé.

Si Deepchart n'est pas dans `C:\Program Files\Volumetrica Trading\Deepchart\`, ajoute
`-p:DeepchartDir="D:\chemin\Deepchart\"`.

## 2. Le graphique

1. **New → Option Chart → SPX** (symbole ES-CME). Unité **1 minute**, **5 jours** d'historique.
2. **Ajoute les indicateurs DeepGamma de ton offre** :
   - Option Summary, source *Gex (market maker)* ;
   - Option Levels/Envelopes ;
   - Option Levels / OI (RT), source *Market Maker* ;
   - Expected Move ;
   - Net Flow et Option Delta si ton offre les inclut.
3. **Ajoute la sonde en dernier** : liste des indicateurs → *Personal* → **Probe - DeepGamma Recorder Test**.
4. **Note l'ordre dans lequel tu as ajouté les indicateurs.** Ça permet de faire correspondre les numéros d'indicateur
   aux noms.

## 3. Les trois tests

**A. Historique (faisable tout de suite, même marché fermé)**
Attends la fin du chargement. `summary.txt` apparaît et se met à jour toutes les 10 s. Ce test répond à Q1, Q2, Q4 et Q5.

**B. Live (15 min de marché)**
Pour voir DeepGamma bouger, fais-le pendant la séance US (15h30-22h heure de Paris). Ce test répond à Q3 et montre les
valeurs DeepGamma minute par minute en temps réel.

**C. Deep Replay (20 à 30 min)**
Ouvre une séance passée en replay (un jour d'octobre, par exemple) avec le même graphique et la sonde. Laisse tourner
20 à 30 min en vitesse x10. Ce test répond à Q6 : on saura si DeepGamma est rejoué sur desktop.

**Lire le résultat** : section `VERDICT PROVISOIRE` en bas de `summary.txt`.

**Si Q1 = NON** : dans le Pattern Builder, regarde les *Indicator ID* qu'il propose pour les indicateurs DeepGamma. Si
ce sont de grands nombres (1001, par exemple), règle le paramètre `IndicatorIdStart` de la sonde sur ce nombre et
relance. La signature relevée par ApiDump (étape 0) dit aussi quel type d'identifiant est attendu.

## 4. Ce qu'il faut renvoyer

Le dossier de chaque test, zippé. Au minimum :
- `summary.txt` ;
- `api_dump.txt` (l'API telle que la voit la DLL en cours d'exécution) ;
- `indicators_*.txt` ;
- `series_catalog_*.csv` ;
- `deepchart-api.txt` de l'étape 0.

Ne les committe pas : ce sont des données de marché et des descriptions de DLL propriétaires (`data/` et les sorties
sont dans `.gitignore`).

## Fichiers produits

| Fichier | Contenu |
|---|---|
| `summary.txt` | Résumé et verdict, mis à jour toutes les 10 s |
| `api_dump.txt` | Signatures réelles de l'API (VAn, Indicator, trade, carnet, TradingApi en inventaire seulement) |
| `indicators_*.txt` | Paramètres de chaque indicateur du graphique (GetParamsById) |
| `series_catalog_*.csv` | Valeur de chaque série testée, maintenant et 10, 60 et 390 barres avant |
| `series_values.csv` | Séries lisibles, barre par barre (historique relu, puis temps réel) |
| `bars.csv` | Barres 1 min : OHLC et volumes ask/bid, pour contrôler l'alignement avec les .tdatac |
| `ticks_rt.csv` / `ticks_hist.csv` | Trades temps réel (30 min) et premiers trades historiques |
| `book_rt.txt` / `book_snapshot.txt` | Mises à jour du carnet (3 min) et état complet du carnet |
| `file_write_test.txt` | Preuve d'écriture |

## Paramètres de la sonde

| Paramètre | Défaut | Rôle |
|---|---|---|
| IndicatorIdStart | 0 | Premier numéro d'indicateur testé |
| MaxIndicators | 40 | Nombre de numéros testés |
| MaxSeries | 20 | Séries testées par indicateur |
| HistoryBarsToScan | 600 | Barres passées relues à la fin du chargement |
| TickLogMinutes | 30 | Durée d'enregistrement détaillé des trades temps réel |
| HistoryTicksToLog | 2000 | Trades historiques écrits (les autres sont seulement comptés) |
| BookLogMinutes | 3 | Durée d'enregistrement détaillé du carnet |

## Vérifier la sonde sans DeepCharts

```
dotnet run --project tests\ProbeHarness
```

Le banc simule un graphique (historique, puis séance rejouée, trois faux indicateurs, un carnet) contre l'imitation de
l'API (`tests/DeepchartApiStubs`) et vérifie les six verdicts. Il doit finir sur huit lignes `OK`.
