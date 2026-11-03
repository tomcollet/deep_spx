# Ce qu'on sait du SDK DeepCharts

Sources : [documentation officielle](https://docs-indicators.deepcharts.com), [dépôt d'exemples officiel](https://github.com/VolumetricaTrading/deepchart-indicators-api),
le dépôt `tomcollet/deep` (OF Scalper, qui tourne en production). « Confirmé » veut dire vu dans du code qui compile
contre les vraies DLL.

## Confirmé

- **Indicateur** : classe C# dérivée de `VolSysAPI.Indicators.Indicator`, dans une DLL net10.0. Elle référence
  `VolSysAPI.dll` et `VolumetricaCore.dll` du dossier de Deepchart (`Private = false`) et s'écrit dans
  `Documents\Deepchart\Indicators`. Deepchart surveille le dossier et recharge la DLL.
- **Developer id** : métadonnée d'assemblage `DeepchartDevId`. Sans elle, ou avec l'id d'un autre utilisateur, la DLL
  est ignorée.
- **`using`** (identiques dans les exemples officiels et dans `deep`) :
  ```csharp
  using VolSysAPI;
  using VolSysAPI.Indicators;
  using VolumetricaAPI.Chart;
  using Feed = VolumetricaAPI.Connection.Structure;
  using VolumetricaControls;
  using VolumetricaCore;
  using static VolSysAPI.ExternalStructure;
  using static VolSysAPI.Structure;
  ```
- **Cycle de vie** : `OnSet` (obligatoire), `OnPreInit`, `OnInit`, `OnLoad`, `OnOpen(isRt)`,
  `OnTick(Feed.TickByTick, isRt, AggrInfo)`, `OnClose(isRt)`, `OnEnd(isRt)`, `OnBook(Feed.BookUpdate)`.
- **Abonnements** : `OnOpenCall`, `OnCloseCall`, `OnTickCall`, `OnEndCall`, `OnBookCall` valent `CallHandler.Off`,
  `RT` ou `HistRT`. `OnBookCall = CallHandler.RT` est confirmé.
- **Trade** : `price`, `PinT`, `Vol`, `AggrSide` (Ask / Bid / Between), `bid`, `ask`, `exDt`.
- **Carnet** : `BookUpdate { NeedResetMbp, MbpUpdates }` ; chaque `MbpUpdate { PinT, isAsk, Qty }`, Qty = 0 retire le
  niveau. État complet par `VAn.RebuildDom()`. Carnet agrégé par prix (MBP), pas les ordres individuels.
- **Barres** : `VAn.BarVars[i]` (`Open`, `High`, `Low`, `Close`, `exchDt`, `VolTotList[0]` avec `TotVol`, `AskVol`,
  `BidVol`, `VwapPrice`) ; barre courante `VAn.BarIndex`.
- **Desktop uniquement** : carnet, trades de l'historique (`HistRT`), lecture des autres indicateurs, autre symbole,
  `TradingApi`. Un indicateur qui en utilise un est calculé sur le poste, jamais sur le serveur.
- **Alertes** : message à l'écran et son, rien d'autre. Pas de webhook documenté.

## Lecture des autres indicateurs

- `GetParamsById(index)` et `GetSeriesValueById(indicator, series, index)` (desktop). **Types des arguments non
  documentés** : `tools/ApiDump` les relève dans les DLL, et la sonde aussi en cours d'exécution.
- D'après les mesures de `deep` : **Deep Stats** expose 21 séries ; **Deep Trades** et **Deep Profile** n'en exposent
  aucune.
- **DeepGamma** : inconnu. C'est la question Q1 de la sonde.

## DeepGamma (d'après la documentation commerciale, à confirmer par la sonde)

- SPX : mises à jour chaque minute à partir des données Cboe par catégorie de participant ; niveaux et exposition
  « market maker » (GEX, DEX, Net Flow).
- Indicateurs : Option Summary (GEX net en oscillateur, avec sous-séries), Option Levels/Envelopes (Zero, Major et Minor
  Gamma), Option Levels / OI (RT) avec source Market Maker, Expected Move, GEX Profile et Heatmap. Dans l'offre
  Institutional : Net Flow, Option Delta, Dynamic Walls, Market Maker Pressure, Exhaustion Detector.
- Les profils et heatmaps par strike sont probablement dessinés en annotations, donc sans doute illisibles par
  `GetSeriesValueById`.
- Replay annoncé jusqu'à 9 mois d'order flow et de flux options SPX. Disponibilité sur desktop : question Q6.
- Pas de charm ni de vanna exposés. Net Flow et Option Delta en sont les meilleurs substituts.

## Inconnues que la sonde doit lever

1. Signature exacte de `GetSeriesValueById` et identifiants des indicateurs DeepGamma.
2. Séries DeepGamma lisibles, et sur quelle profondeur d'historique.
3. Carnet en Deep Replay.
4. DeepGamma rejoué sur desktop.
