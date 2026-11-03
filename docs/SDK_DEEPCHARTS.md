# Ce qu'on sait du SDK DeepCharts

Sources : la documentation officielle (lue en entier dans le [dépôt d'exemples](https://github.com/VolumetricaTrading/deepchart-indicators-api),
dossier `docs/`), les issues de ce dépôt, et `tomcollet/deep` (OF Scalper, qui tourne dans Deepchart). **Confirmé** veut
dire vu dans du code qui compile contre les vraies DLL ; **documenté**, écrit dans la doc mais pas encore vu ;
**hypothèse**, à lever par la sonde ou par `tools/ApiDump`.

## 1. Le bac à sable : ce qu'une DLL a le droit de faire

Deepchart contrôle une DLL avant de la charger, et la refuse si elle utilise autre chose que ce qu'un indicateur peut
utiliser (doc `troubleshooting`). **Documenté, et confirmé par `tools/DllCheck` de `deep`.**

- **Autorisé** : l'API d'indicateurs (`VolSysAPI`, `VolumetricaAPI.Chart`, les types de `VolumetricaCore` que l'API
  donne) ; les types de base de `System` (nombres, chaînes, `Math`, `DateTime`, `TimeSpan`, `Convert`, `Random`,
  exceptions courantes, `Func`, `Action`) ; `System.Collections.Generic` ; LINQ to objects ; `StringBuilder` ;
  `CultureInfo` ; les attributs de paramètres ; `Debug`, `Stopwatch`, `Console` en développement.
- **Refusé** : réflexion, `dynamic`, **fichiers, réseau**, processus, **threads, tâches et `async`, `lock`**, `unsafe` et
  `DllImport`, références à d'autres bibliothèques. Piège connu : un indexeur `this[int]` fait référencer
  `System.Reflection.DefaultMemberAttribute`, refusé. De même `ex.GetType().Name` (`MemberInfo.Name`).

**Conséquences pour le projet**

- La DLL **ne peut ni écrire de fichier ni ouvrir de socket.** Le seul canal de sortie est le **log de Deepchart**
  (`VAn.AddToLog`), qu'un programme externe lit ensuite. C'est ce que fait déjà `deep` (journal CSV préfixé `OFS;`).
  La sonde fait de même (`DSPX;`), `tools/extract_dspx.py` relit le log.
- Pas de découverte de l'API par réflexion dans la DLL : les signatures se lisent hors de Deepchart avec
  `tools/ApiDump`, qui ouvre les DLL comme des fichiers.
- Toujours passer `tools/DllCheck` sur la DLL avant de la charger.

## 2. Indicateur, compilation, cycle de vie (confirmé)

- Classe dérivée de `VolSysAPI.Indicators.Indicator`, avec un `public static IndicatorDescriptionBase Register()`. DLL
  net10.0 qui référence `VolSysAPI.dll` et `VolumetricaCore.dll` (`Private = false`), écrite dans
  `Documents\Deepchart\Indicators`. Deepchart surveille le dossier et recharge la DLL.
- Developer id : métadonnée d'assemblage `DeepchartDevId`, à passer en ligne de commande.
- `using` :
  ```csharp
  using VolSysAPI;
  using VolSysAPI.Indicators;
  using VolumetricaAPI.Chart;
  using Feed = VolumetricaAPI.Connection.Structure;   // alias obligatoire : deux types s'appellent Structure
  using VolumetricaControls;
  using VolumetricaCore;
  using static VolSysAPI.ExternalStructure;
  using static VolSysAPI.Structure;
  ```
- Ordre des appels : `OnSet` (obligatoire), `OnPreInit`, `OnInit`, `OnLoad` (tout remettre à zéro ici), puis pour chaque
  barre de l'historique `OnOpen` et `OnClose`, puis **un seul `OnEnd(false)`** à la fin de l'historique. En temps réel :
  `OnTick` à chaque trade, `OnClose` + `OnOpen` à chaque nouvelle barre, `OnEnd(true)` après chaque lot de mises à jour.
  `OnDispose` n'est jamais appelé sur desktop.
- Abonnements dans `OnSet` : `OnOpenCall`, `OnCloseCall`, `OnTickCall`, `OnEndCall` (`Off` par défaut, `RT`,
  `HistRT`) ; `OnBookCall = CallHandler.RT` pour le carnet (desktop).
- `StatusMessage` (message affiché au chargement), `NeedRecalculate`, `IsValid` (faux après une erreur).

## 3. Données de marché

**Confirmé**
- Barres : `VAn.BarVars[i]` (`Open`/`High`/`Low`/`Close` et leurs versions en ticks entiers, `exchDt` heure de bourse,
  `dt` heure locale pour l'affichage, `IsNewDay`, `SessionIndex`, `Index`). Totaux `VolTotList[0]` : `TotVol`, `AskVol`,
  `BidVol`, nombres de trades `TotNTVol`/`AskNTVol`/`BidNTVol`, `MaxDeltaVol`, `MinDeltaVol`, `VwapPrice`. Ne jamais
  lire après `VAn.BarIndex` pendant l'historique.
- Volume par prix : `NeedVbp = true`, puis `bar.PriceList` et `bar.VbpListAll[pinT][0]` (peut être `null`).
- Trade (`Feed.TickByTick`) : `price`, `PinT`, `Vol`, `AggrSide` (`Ask`, `Bid`, `Between`), `bid`, `ask` au moment du
  trade, `exDt` heure de bourse.
- Prix en ticks entiers : `FromPinT(pinT).Price`, `FromPrice`, `GetDoubleTicksDiff`, `GetIntTicksDiff`,
  `DoubleAddSubTicks`, `IntAddSubTicks`.

**Documenté**
- `VAn.IsReplayData` (le graphique est un replay), `VAn.ChartPhase` (historique ou temps réel), `LastExDt`, `LastIndex()`,
  `ParamType`/`Param1`/`Param2` (type et taille des barres), `IniTimeSession`/`EndTimeSession`, `GetIniSessionDt`,
  `GetSecondsToClose()`.
- `BuildTicksBar(bar, filter)` : les trades d'une barre reconstruits à partir de son volume par prix.
- `SetCalculateAggrTrades()` (desktop) : trades agrégés en ordres agressifs. Probablement ce que transporte l'argument
  `AggrInfo` d'`OnTick` ; ses membres sont à lire dans `deepchart-api.txt`.
- `RequestResolution(paramType, param1, param2, daysToLoad, basedOnMinute)` (autre taille de barres) et
  `RequestResolution(symbol, ...)` (autre symbole, desktop).

**Problèmes connus (issues du dépôt officiel)**
- **#1** : `OnTickCall = HistRT` ne livrait aucun trade historique en 16.5.3. Corrigé en 16.5.4 selon Volumetrica.
- **#2** : `RequestResolution` appelé dans `OnLoad` fait planter l'application ; appelé dans `OnPreInit` sur des barres
  construites à partir des minutes, il fonctionne (1 100 jours en 6 s environ) ; construit à partir des ticks, il reste
  bloqué.
- **#3** : la première demande d'une résolution fige sa profondeur d'historique pour la session (`daysToLoad` plus grand
  ignoré ensuite, jusqu'au redémarrage).

## 4. Carnet d'ordres (le flux L2)

**Confirmé**
- `OnBook(Feed.BookUpdate)` et `VAn.RebuildDom()`, desktop uniquement. `BookUpdate { NeedResetMbp, MbpUpdates }`, chaque
  `MbpUpdate { PinT, isAsk, Qty }` ; `Qty = 0` retire le niveau ; `NeedResetMbp` oblige à vider le carnet tenu à jour.
- **Carnet par prix (MBP), pas les ordres individuels.** Même avec un flux MBO, Deepchart agrège par prix avant de
  transmettre (`deep`, « À propos du MBO »). Icebergs, recharges et retraits se **déduisent** en croisant les variations
  par niveau avec les trades exécutés à ce prix : `deep/src/OrderFlowScalper/Engine/BookAnalyzer.cs` le fait déjà.

**Ce que ça veut dire pour ton flux L2**
- Le L2 acheté sur le site de DeepCharts alimente le DOM de Deepchart, et c'est **ce même carnet que la DLL reçoit**
  par `OnBook`. Rien d'autre à brancher : il suffit d'être sur le graphique, en desktop, avec ce flux actif.
- **Inconnu (sonde)** : le nombre de niveaux transmis (`BOOKMIN` : niveaux par côté, profondeur en ticks) ; et
  **si Deep Replay rejoue le carnet** (test C). `deep` note seulement que l'historique ne le contient pas.
- **Historique du carnet** : un flux L2 temps réel n'est en général pas livré avec son historique. Si le replay ne le
  rejoue pas, il faudra l'enregistrer nous-mêmes à partir de maintenant (lignes du log), ou acheter l'historique MBO
  CME (Databento ; les données historiques CME ne demandent pas de licence).
- **Lire ce flux hors de DeepCharts** : pas par le SDK. Seulement par l'API du fournisseur, si ton abonnement le permet
  explicitement. Un flux acheté pour l'affichage dans une plateforme n'autorise pas, en général, son usage par un
  programme externe (licence non-display CME). On reste sur `OnBook`.

## 5. Lecture des autres indicateurs (DeepGamma)

- `GetSeriesValueById(indicator, series, index)` et `GetParamsById(index)`, desktop uniquement.
- **Issue #4 (ouverte, 21/10/2026, Deepchart 16.5.4)** : un développeur lit **Option Market Maker Pressure**, un
  indicateur DeepGamma, avec `VAn.GetSeriesValueById(3, 0, barIndex)` sur un graphique ES 1 min.
  - **En replay**, les valeurs sont justes et varient (de l'ordre de 4,79E+09).
  - **En live**, l'appel renvoie 0, pour la barre en cours comme pour les barres fermées, alors que l'indicateur
    affiche des valeurs non nulles. Testé dans `OnEnd`, `OnClose` et `OnOpen`. Aucune réponse de Volumetrica à ce jour.
- Ce que l'issue établit : **trois `int`** en argument, **un indicateur DeepGamma est lisible**, et le numéro
  d'indicateur est sans doute sa position sur le graphique. Type de retour : à lire dans `deepchart-api.txt`.
- D'après `deep` : Deep Stats expose 21 séries ; Deep Trades et Deep Profile n'en exposent aucune.
- **Conséquence** : pour la recherche (replay), la voie DeepGamma est ouverte. Pour le live, elle dépend du correctif
  de l'issue #4 (question Q7 de la sonde).

## 6. DeepGamma (documentation commerciale, à confirmer)

- SPX : mises à jour chaque minute à partir des données Cboe par catégorie de participant ; niveaux et exposition
  « market maker » (GEX, DEX, Net Flow).
- Indicateurs : Option Summary (GEX net en oscillateur, sous-séries), Option Levels/Envelopes (Zero, Major et Minor
  Gamma), Option Levels / OI (RT) en source Market Maker, Expected Move, GEX Profile et Heatmap (probablement des
  annotations, donc illisibles). Offre Institutional : Net Flow, Option Delta, Dynamic Walls, Market Maker Pressure,
  Exhaustion Detector.
- Replay annoncé jusqu'à 9 mois d'order flow et de flux options SPX.
- Pas de charm ni de vanna exposés ; Net Flow et Option Delta en sont les meilleurs substituts.

## 7. Messages, ordres

- `ShowMessage` et `PlayAlert` : temps réel seulement. `AddToLog(message, level, source, showToast)` : le log de l'app.
- `TradingApi` (`ITradingAPI`, desktop) : ordres et positions de l'utilisateur. Non utilisé ; membres dans
  `deepchart-api.txt`.

## Ce qui reste à lever

1. Type de retour de `GetSeriesValueById`, champs MBO éventuels de `BookUpdate`, membres d'`AggrInfo` → `tools/ApiDump`.
2. Séries DeepGamma lisibles, numéros, profondeur d'historique → sonde, test A.
3. Valeurs DeepGamma à 0 en live (issue #4) → sonde, test B.
4. Carnet en replay, nombre de niveaux → sonde, tests B et C.
5. Où Deepchart écrit son log, à quelle vitesse il est vidé sur disque → test A, puis mesure dédiée si on s'en sert en
   live.
