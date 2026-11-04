# Ce qu'on sait du SDK DeepCharts

Sources : la documentation officielle (lue en entier dans le [dépôt d'exemples](https://github.com/VolumetricaTrading/deepchart-indicators-api),
dossier `docs/`), les issues de ce dépôt, `tomcollet/deep` (OF Scalper, qui tourne dans Deepchart) et
**`deepchart-api.txt`**, l'inventaire produit par `tools/ApiDump` sur le poste de Tom le 04/11/2026 (VolSysAPI 1.4.6.0,
VolumetricaCore 1.0.0.0 ; non committé). **Confirmé** veut dire vu dans du code qui compile contre les vraies DLL ou dans
cet inventaire ; **documenté**, écrit dans la doc mais pas encore vu ; **hypothèse**, à lever par la sonde.

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

- La DLL **ne peut ni écrire de fichier ni ouvrir de socket.** Le seul canal prévu est le **log de Deepchart**
  (`VAn.AddToLog`). **Constaté le 04/11/2026 sur le poste de Tom : le fichier `Documents\Deepchart\Logs\various_*.log`
  est chiffré** (lignes base64 de données chiffrées). Il n'est donc pas lisible par un programme externe, et on ne
  cherche pas à le déchiffrer. Restent : l'affichage sur le graphique (séries, annotations, ce que fait la sonde v4),
  les éventuelles fonctions d'export de DeepCharts, et l'API d'ordres de la DLL. Voir `docs/ARCHITECTURE.md`.
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
- Abonnements dans `OnSet` : `OnOpenCall`, `OnCloseCall`, `OnTickCall`, `OnEndCall` (`Off` = 0 par défaut,
  `HistRT` = 1, `RT` = 2) ; `OnBookCall = CallHandler.RT` pour le carnet (desktop).
- `getId()` : identifiant de l'indicateur sur le graphique.
- `StatusMessage` (message affiché au chargement), `NeedRecalculate`, `IsValid` (faux après une erreur).

## 3. Données de marché

**Confirmé**
- Barres : `VAn.BarVars[i]` (`Open`/`High`/`Low`/`Close` et leurs versions en ticks entiers, `exchDt` heure de bourse,
  `dt` heure locale pour l'affichage, `IsNewDay`, `SessionIndex`, `Index`). Totaux `VolTotList[0]` : `TotVol`, `AskVol`,
  `BidVol`, nombres de trades `TotNTVol`/`AskNTVol`/`BidNTVol`, `MaxDeltaVol`, `MinDeltaVol`, `VwapPrice`. Ne jamais
  lire après `VAn.BarIndex` pendant l'historique.
- **`exchDt` est à l'heure de New York** sur le graphique options SPX (ES-CME) : constaté le 04/11/2026 (dernière barre
  12:19 à 18:19 heure de Paris ; première barre de l'historique à 18:00, réouverture Globex). Ce n'est pas l'heure de
  Chicago du CME. L'indicateur de setups le vérifie lui-même (`DSPX;CLOCK`).
- **Niveaux d'Option Envelopes déjà en prix ES** : Major et Minor Gamma = strike SPX + base ES - SPX (décimales qui
  varient dans la journée : 7729,325 puis 7739,175 ; Major - Minor = 60 points). Constaté le 04/11/2026, à confirmer
  sur la ligne tracée.
- **Historique DeepGamma** : le graphique 1 minute du 04/11/2026 couvrait 15 jours (première barre le 20/10/2026,
  14 900 barres), avec des valeurs DeepGamma au moins 390 barres en arrière. Profondeur maximale non testée. Recharger seulement l'indicateur ne relivre pas les trades historiques (`hist_trades=0`).
- Volume par prix : `NeedVbp = true`, puis `bar.PriceList` et `bar.VbpListAll[pinT][0]` (peut être `null`).
- Trade (`Feed.TickByTick`) : `price`, `PinT`, `Vol`, `QtyOrd`, `CumVol`, `AggrSide` (`Ask` = 1, `Bid` = -1,
  `Between` = 0), `bid`, `ask` au moment du trade, `exDt` heure de bourse, `locDt` heure locale, **`AggrID` et `RestID`
  (identifiants de l'ordre agresseur et de l'ordre passif)**, `TradeType` (drapeaux `Iceberg`, `SweepOrder`, `Auction`,
  `Extended`...), `CounterSize`, `IsNewDay`.
- Barre : `VolTotList` est un tableau `InfoVolClass[]` (volumes en `long`, plus `CumDeltaVol`) ; la barre a aussi `Ask`,
  `Bid`, `LastAsk`, `LastBid`, `lastDtInBar` et **`OptionOffsetPrice`** (écart indice / future sur un graphique options).
- `ChartPhase` : `HistoricalFull`, `Backtesting`, `HistoricalPartial`, `Realtime`, `RealtimeFlushing`.
- `AggrInfo` (trade agrégé, avec `SetCalculateAggrTrades()`) : `PinT`, `TradeDir`, `Vol`, `MaxQtyOrd`,
  `NumberOfTrades`, `StartPrice`, `EndPrice`, `AvgPrice`.
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

**Confirmé (inventaire)**
- `OnBook(Feed.BookUpdate)` et `VAn.RebuildDom()`, desktop uniquement.
- `BookUpdate` : `NeedResetMbp`, **`NeedResetMbo`**, `BestBidPrice`, `BestAskPrice`, `BestBidInT`, `BestAskInT`, `exDt`,
  `appDt`, `MbpUpdates` (`List<BookInfo>`) et **`MboUpdates` (`List<MboInfo>`)**.
- `BookInfo` (un niveau de prix) : `Price`, `PinT`, `Qty`, `DiffQty`, **`NumOffers`** (nombre d'ordres au niveau),
  `isAsk`, `IsShiftOutside`, `MboMaxQty`. `Qty = 0` retire le niveau.
- **`MboInfo` (un ordre)** : `OrderId`, `IsAsk`, `Qty`, `Price`, `PinT`, `MaxQty`, `CumFillQty`, `Flags` (`Iceberg`).

**Ce que ça change** : `deep` notait que l'API ne transmettait que le carnet par prix. L'API actuelle prévoit le carnet
**ordre par ordre** (MBO) et l'identifiant des ordres dans chaque trade. Reste à savoir si ton flux les remplit : c'est
la question Q8 de la sonde. Sinon, icebergs et retraits se déduisent du carnet par prix et des trades, comme le fait
`deep/src/OrderFlowScalper/Engine/BookAnalyzer.cs`.

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

- **Signatures (inventaire)** : `double[] GetSeriesValueById(int IndicatorIndex, int SubgraphIndex, int Index)` (un
  point peut porter plusieurs valeurs) et `BaseIndicatorPrms GetParamsById(int IndicatorIndex)`, qui donne le **nom**
  de l'indicateur (`name`), `Hidden` et la liste de ses séries (`v_series[].Name`). Desktop uniquement.
- `IndicatorCategoryEnum` contient `DeepGamma` et `DeepGammaCboe` : les indicateurs DeepGamma sont des indicateurs comme
  les autres, lisibles par ces deux méthodes.
- **Issue #4 (ouverte, 21/10/2026, Deepchart 16.5.4)** : un développeur lit **Option Market Maker Pressure**, un
  indicateur DeepGamma, avec `VAn.GetSeriesValueById(3, 0, barIndex)` sur un graphique ES 1 min.
  - **En replay**, les valeurs sont justes et varient (de l'ordre de 4,79E+09).
  - **En live**, l'appel renvoie 0, pour la barre en cours comme pour les barres fermées, alors que l'indicateur
    affiche des valeurs non nulles. Testé dans `OnEnd`, `OnClose` et `OnOpen`. Aucune réponse de Volumetrica à ce jour.
- Ce que l'issue établit : **un indicateur DeepGamma est lisible**, et le numéro d'indicateur est sans doute sa position
  sur le graphique (la sonde v3 l'écrit avec son nom).
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
- `TradingApi` (`ITradingAPI`, desktop, membre protégé de l'indicateur) : **API d'ordres complète** (inventaire).
  - Position : `PositionNetQuantity`, `PositionBuyQuantity`, `PositionSellQuantity`.
  - Ordres : `InsertOrder(OrderInsertRequest, ref reason)` → identifiant ; `OrderInsertRequest` = `OrderType` (`Market`,
    `Limit`, `Stop`, `StopLimit`), `Quantity`, `Price`, `Brackets`, `RefPositionId`, `BarsExpiration`.
  - Brackets : `BracketParam` (quantité, ticks ou prix, objectif ou stop, OCO, passage au point mort, stop suiveur) ;
    `InsertBracket`, `ModifyBracket`.
  - Gestion : `ModifyOrderPrice`, `ModifyOrderQuantity`, `RemoveOrder`, `FlatPosition`, **`FlatAllPositions()`**,
    **`CancelAllOrders()`**, `GetAllOrdersStatus()`, `GetOrderInfo()`.
  - Non utilisé pour l'instant (le pupitre reste la voie retenue), mais c'est une voie d'exécution directe depuis la DLL.

## Ce qui reste à lever

1. ~~Signatures de `GetSeriesValueById`, champs de `BookUpdate`, membres d'`AggrInfo`~~ → réglé par `deepchart-api.txt`.
2. Séries DeepGamma lisibles, numéros et noms, profondeur d'historique → sonde, test A.
3. Valeurs DeepGamma à 0 en live (issue #4) → sonde, test B.
4. Carnet en replay, nombre de niveaux, MBO rempli ou non (Q8) → sonde, tests B et C.
5. ~~Où Deepchart écrit son log~~ → `Documents\Deepchart\Logs\various_*.log`, chiffré. Reste à savoir si DeepCharts
   affiche ce log en clair dans une fenêtre, et s'il sait exporter les valeurs d'un indicateur.
