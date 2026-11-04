# Indicateur de setups (v1.8)

`src/DeepSpxSetups` : indicateur DeepCharts **« DeepSpx - Setups gamma x order flow »**. Il signale des achats et des
ventes selon les trois setups de [STRATEGIE.md](STRATEGIE.md), dessine les signaux et leur résultat simulé, et
**n'envoie aucun ordre**.

> **Rien n'est encore validé.** Les seuils sont les valeurs de départ fixées *avant* tout test (STRATEGIE.md), avec les
> substitutions ci-dessous. Aucune n'a été réglée sur des données. Un signal n'est pas une recommandation tant que
> l'étape 4 de la [feuille de route](FEUILLE_DE_ROUTE.md) n'est pas passée.

## Montage

```
Graphique options SPX (ES-CME, 1 min) + indicateurs DeepGamma + DeepSpx Setups
  └─ SetupsIndicator.cs : lit les barres et DeepGamma, dessine, écrit le log
       └─ Engine/ (C# pur) : régime, zones, setups, sorties simulées, statistiques
```

- **Repérage par nom** (`GetParamsById`) : `Option Envelopes` (séries Zero, Major, Minor Gamma), `Option Summary`
  (Regime), `Option Net Flow` (Difference), `Option Market Maker Pressure` (MM Pressure, journalisé seulement). Le
  premier indicateur trouvé de chaque nom est retenu : avec deux Option Summary, c'est celui ajouté en premier. Les
  paramètres `EnvelopesId`, `SummaryId`, `NetFlowId` et `MmPressureId` forcent un numéro.
- **Historique** : rejoué d'un bloc à la fin du chargement (`OnEnd(false)`), barre par barre, une fois que DeepGamma a
  calculé ses valeurs. **Temps réel** : une barre à chaque fermeture (`OnClose`). Même moteur, même ordre des calculs.
- **Barres** : 1 minute. Delta = volume à l'ask − volume au bid de la barre.
- **Heure des barres** : sur le graphique options SPX de Tom, `exchDt` est à **l'heure de New York** (constaté le
  04/11/2026 : dernière barre 12:19 à 18:19 heure de Paris, première barre 18:00 = réouverture Globex). Toutes les
  heures ci-dessous sont donc à New York, comme dans STRATEGIE.md. L'indicateur le vérifie au chargement (heure de la
  première barre après chaque interruption : 18:00 ou 09:30 à New York, 17:00 ou 08:30 à Chicago) et décale ses horaires
  d'une heure si les barres sont à l'heure de Chicago. Ligne `DSPX;CLOCK`, et rappel dans le panneau. Paramètre
  `BarTimeZone` : 0 automatique, 1 New York, 2 Chicago.
- **Indicateurs DeepGamma ajoutés après coup** : repérés dans les 15 minutes, sans recharger. Le panneau signale ceux
  qui manquent et ce que cela coupe (sans Net Flow, pas de setup B ; sans Summary, régime lu sur le Zero Gamma seul).

## Régime

| Régime | Condition (dans cet ordre) |
|---|---|
| Inconnu | Zero Gamma absent ; dernière valeur DeepGamma valide vieille de plus de 15 barres ; aucune valeur DeepGamma n'a changé depuis 45 barres de séance (figé) ; ATR pas encore prêt |
| Transition | prix à moins de 0,5 ATR5 du Zero Gamma |
| Short gamma | prix sous le Zero Gamma, **ou** Option Summary / Regime négatif |
| Long gamma | prix au-dessus du Zero Gamma, et Summary positif (ou absent, ou `UseSummarySign` désactivé) |

Une valeur DeepGamma à 0 ou NaN est considérée comme absente ; la dernière valeur valide est conservée. Les règles
« inconnu » couvrent les deux défauts possibles en live (issue #4 : 0, ou valeur figée).

**Summary et Market Maker Pressure sont lus 5 barres en arrière** (`SummaryLagBars`), en historique comme en live :
DeepGamma ne les remplit qu'avec environ 3 minutes de retard (0 sur la barre qui se ferme, constaté le 04/11/2026). Lire
la barre courante en historique reviendrait à utiliser en backtest une valeur que le live n'a pas encore. Les lignes
`DSPX;LAG` (30 premières minutes de temps réel) donnent Summary et Pressure sur les 7 dernières barres, pour mesurer ce
retard.

Summary et Net Flow ont en plus leur propre contrôle, parce qu'ils peuvent tomber à 0 ou se figer en live alors que les
niveaux d'Option Envelopes bougent encore (sonde du 04/11/2026) :
- **Summary** absent depuis plus de 15 barres, ou inchangé depuis plus de 45 : son signe est ignoré, le régime est lu
  sur le Zero Gamma seul (panneau : « figé, ignoré ») ;
- **Net Flow** absent depuis plus de 15 barres : plus de pente, donc plus de setup B.

## Setups

**A. Rejet de mur (long gamma).** Mur = Major ou Minor Gamma.
- Vente : le plus haut touche le mur (4 ticks en dessous au plus, 2 ticks au-dessus au plus), la clôture reste
  sous le mur, le delta est acheteur et au-dessus de son 90e percentile, et la clôture est à au moins 1,5 ATR5
  au-dessus de la VWAP de séance. Achat : symétrique sous un mur.
- Stop : 1 ATR5 (8 ticks au moins) au-delà de l'extrême de la barre. Objectif : VWAP. Sortie au temps : 45 barres.

**B. Continuation (short gamma).** Niveaux : range d'ouverture 30 min (haut, bas), plus haut et plus bas de la séance
régulière précédente, Major, Minor et Zero Gamma.
- Achat : la clôture précédente est sous le niveau, la clôture passe au-dessus d'au moins 1 tick, le delta est acheteur
  et au-dessus de son 80e percentile, la pente du Net Flow sur 5 barres est positive. Vente : symétrique.
- Stop : 4 ticks de l'autre côté du niveau (8 ticks au moins de la clôture). Objectif : niveau suivant dans le sens du
  trade (8 ticks au moins), sinon 2 ATR5. Sortie au temps : 60 barres.

**C. Aimant de fin de séance (long gamma, à partir de 14:00 New York).** Strike aimant = Major Gamma.
- Le prix est à 8 ticks au moins et 2 ATR5 au plus du strike, le delta pousse à l'opposé du strike (au-dessus de son
  90e percentile) et la barre ne clôture pas sur son extrême : entrée vers le strike.
- Objectif : le strike. Stop : 1 ATR5 au-delà de l'extrême. FLAT à 15:45.

**Règle de risque (désactivée depuis v1.8, retirée par Tom le 04/11/2026 à 20h37)** : `MaxRiskTicks` et
`MinRewardRisk` (0 = sans limite). Posée à 25 ticks de risque au plus et un objectif d'au moins 1,5 fois le risque. Le signal est mesuré depuis l'entrée attendue (clôture + 1 tick) ; s'il ne respecte pas la
règle, il est **refusé**, pas ajusté (panneau : « refusé : risque… » ou « refusé : objectif… », compteurs dans `HIST`).
À l'ouverture suivante, une entrée qui porterait le risque au-delà de 25 ticks, ou qui ouvre déjà au-delà du stop, est
annulée. Conséquence attendue : le setup C (objectif = le strike, stop à 1 ATR5 au-delà de l'extrême) passera rarement.

**Filtres communs** : séance régulière 09:30-16:00 New York ; pas d'entrée les 5 premières minutes ni après 15:45 ;
FLAT de tout à 15:45 ; une position à la fois ; 3 signaux par jour au plus ; arrêt après 2 pertes.

## Recherche de configuration A/B/C (v1.7)

Selon [research/optim_abc/PREREG.md](../research/optim_abc/PREREG.md) : 104 configurations depuis v1.8 (A 40, B 60,
C 4 ; bande de transition, objectif structurel / 0,5R / 1R / 1,5R / 2R, sans règle de risque, classement par taux de gain
parmi les configurations gagnantes en recherche ; seuils d'absorption, d'écart à la VWAP, de delta de cassure, Net Flow
exigé ou non, stop de C), chacune avec son contrôle en sens inverse, rejouées par l'indicateur sur l'historique chargé
(barres d'une minute, 1 an). Séances avec DeepGamma **avant le 16/08/2026** (holdout d'A/B/C scellé), 60 % en
recherche, 40 % en validation.

- Le rejeu se fait par tranches de `OptBudgetMs` (250 ms) à chaque appel de Deepchart, pour ne pas le figer (pas de
  thread dans le bac à sable) ; le panneau affiche l'avancement. `OptSynchronous` : tout d'un coup au chargement.
- Panneau : couverture (séances, dates, part des barres avec volumes ask/bid), puis par setup la configuration retenue
  (ou la n° 1 et la raison du rejet), ses chiffres en recherche et en validation, et son sens inverse.
- Log : `DSPX;OPTALL` (toute la grille, en recherche seulement), `DSPX;OPTINFO`, `DSPX;OPTRANK` (les 3 premières de
  chaque setup, avec leur validation et leur verdict).
- Une configuration retenue se reporte en live par les paramètres `TransitionAtr`, `TargetMode`, `TargetR`,
  `AbsorbPct`, `VwapDevAtr`, `BreakDeltaPct`, `RequireNetFlow`, `StopAtr` et `EnableSetupA/B/C`.

## Étude des scalps (H1, H2), close

Close le 04/11/2026 (échec, [résultats](../research/scalp_flux/RESULTATS.md)) ; désactivée par défaut (`ShowStudy`).

Calculée par l'indicateur sur l'historique chargé, selon [research/scalp_flux/PREREG.md](../research/scalp_flux/PREREG.md),
et affichée dans le panneau (critère principal : stop 16, objectif 24 ticks) :
- **H1** : minute d'agression extrême (rang du delta ≥ 90) suivie en short gamma, contrée en long gamma ; contrôles
  « toujours suivie », « toujours contrée », « hasard » ;
- **H2** : saut de Net Flow (rang ≥ 90) dans son sens ; contrôles « sans ES même sens », « hasard » ;
- par ligne : trades, taux de gain et seuil de rentabilité, espérance nette par trade et son intervalle de confiance
  (bootstrap par séance), découverte (60 % premières séances) et validation (40 % dernières) ; puis la robustesse à
  2 ticks par exécution.
- Séances : celles avec DeepGamma jusqu'au **04/11/2026 inclus** (holdout ouvert pour les scalps par décision de Tom) ;
  les séances live à partir du 05/11 sont le **test final** et n'entrent pas dans l'étude. Moins de 40 séances :
  « non concluant ».
- Lignes `DSPX;STUDYINFO` et `DSPX;STUDY` (toute la grille : stops 12, 16, 20, 24) dans le log.

## Exécution simulée

- Signal à la clôture d'une barre → entrée à **l'ouverture de la barre suivante**, + 1 tick contre soi.
- Sorties : stop, objectif, temps ou fin de séance, toutes − 1 tick contre soi. **Stop et objectif touchés dans la même
  barre → stop.** Barre qui ouvre au-delà du stop (gap) : sortie à l'ouverture, pas au stop.
- **Stops et objectifs sur la grille des ticks** : les niveaux DeepGamma (strike + base ES - SPX, par exemple 7600,35)
  et la VWAP n'y sont pas. Arrondi défavorable : stop au tick suivant vers l'extérieur, objectif au tick qui demande au
  prix d'aller un peu plus loin.
- Résultat en ticks, affiché à la sortie (« +52 t ») et dans le panneau.

## Substitutions par rapport à STRATEGIE.md

| Prévu | Implémenté | Raison |
|---|---|---|
| Distances en Expected Move | distances en **ATR5** (ATR de Wilder 30 barres × √5) | Expected Move n'expose pas de série lisible |
| Call / Put walls | **Major / Minor Gamma** | les murs d'Option Levels ne sont pas lisibles |
| Absorption « carnet qui se recharge » | barre qui touche le mur sans le dépasser, delta extrême contre elle | même calcul en historique et en live : le carnet n'a pas d'historique |
| Cassure avec « profondeur d'en face qui baisse » | non utilisé (v1) | idem, carnet absent de l'historique |
| Filtre d'annonces macro | non implémenté | pas d'agenda lisible par la DLL ; éviter les jours d'annonce à la main |

## Sorties

- **Graphique** : flèche et lettre du setup (« B achat ») sur la barre du signal ; résultat en ticks sur la barre de
  sortie ; info-bulle avec niveau, stop, objectif et régime ; panneau en haut à gauche (régime et raison, niveaux
  DeepGamma, âge des données, VWAP, ATR5, range d'ouverture, veille, état, signaux du jour, 5 derniers signaux).
- **Log de Deepchart** (fenêtre Log, ou `tools/read_log_window.py`) :
  - `DSPX;MAP` : numéros des indicateurs DeepGamma retenus ;
  - `DSPX;SIG` : id, heure, setup, sens, régime, niveau, clôture, stop, objectif, delta, rang du delta, ATR5, Zero
    Gamma, distance au Zero Gamma en ticks, pente du Net Flow, Summary, VWAP ;
  - `DSPX;FILL` : entrée simulée ;
  - `DSPX;EXIT` : sortie (raison, prix, ticks) ;
  - `DSPX;HIST` : bilan du rejeu de l'historique ;
  - `DSPX;STATE` : état complet (régime et raison, niveaux, Summary, pente du Net Flow, VWAP, ATR5, signaux du jour,
    position, horloge, repérage), à la première minute de temps réel puis toutes les 5 minutes : un fichier lu par
    `read_log_window.py` le contient même si les lignes du chargement n'étaient plus à l'écran ;
  - `DSPX;LAG` : Summary et Pressure sur les 7 dernières barres, chaque minute (30 premières minutes).
- **Statistiques** (`ShowStats`, désactivé par défaut) : les séances récentes sont dans la période scellée (à partir du
  16/08/2026). On peut regarder les signaux, on ne règle rien dessus.

## Paramètres de l'indicateur

`EnableSetupA/B/C`, `RequireNetFlow`, `UseSummarySign`, `BlockOnStaleGamma`, `MaxTradesPerDay` (3), `MaxLossesPerDay`
(2), `SlippageTicks` (1), `MaxRiskTicks` (25), `MinRewardRisk` (1.5), `ShowStudy`, `StudyStartDate` (0),
`StudyEndDate` (20261105, exclu : ne pas dépasser sans décision, au-delà l'étude entame le test final), `RunOptimizer`,
`OptStartDate`, `OptEndDate` (20260816, exclu), `OptBudgetMs` (250), `OptSynchronous`, `TransitionAtr` (0.5),
`TargetMode` (0), `TargetR` (2), `AbsorbPct` (90), `VwapDevAtr` (1.5), `BreakDeltaPct` (80), `StopAtr` (1), `ApplyOptionOffset` (ajoute l'écart indice/future de la barre aux niveaux DeepGamma, si ceux-ci
s'avèrent être en prix SPX), `EnvelopesId`, `SummaryId`, `NetFlowId`, `MmPressureId` (-1 = automatique),
`MaxIndicatorScan`, `BarTimeZone`, `SummaryLagBars` (5), `LagLogMinutes` (30), `ShowStats`, `PanelFontSize`, `PanelX`, `PanelY`, `PanelLineStep`. Les autres seuils sont dans
`Engine/EngineSettings.cs`.

## Vérifications

```
dotnet run --project tests/EngineChecks     # 28 scénarios du moteur, dont l'heure de New York
dotnet build tests/SetupsCompileCheck       # compile contre l'imitation de l'API, avertissements = erreurs
dotnet run --project tools/DllCheck -- tests/SetupsCompileCheck/bin/Debug/net10.0/SetupsCompileCheck.dll
dotnet run --project tests/SetupsHarness    # faux graphique : horloge Chicago et New York, graphique sans Summary ni Net Flow
```

## Constaté sur le graphique de Tom (04/11/2026, 18h19)

- **Niveaux en prix ES** : Major 7739,175 et Minor 7679,175 (écart de 60 points, mêmes décimales qui varient dans la
  journée : 7729,325 une heure plus tôt). Ce sont des strikes SPX décalés de la base ES - SPX : DeepGamma convertit
  déjà ses niveaux en prix ES. **Confirmé par Tom à 19h58 : le Zero Gamma du panneau tombe sur la ligne tracée.**
  `ApplyOptionOffset` reste désactivé.
- **Historique DeepGamma** : depuis le 20/10/2026 sur ce graphique (14 900 barres de 1 minute). Tout est dans la
  période scellée : les études demanderont des séances plus anciennes (Deep Replay ou historique plus long).
- **Ce graphique-là** n'avait ni Option Summary ni Option Net Flow ni Market Maker Pressure : à remettre pour les
  setups.

## À vérifier

1. ~~Zero Gamma du panneau = ligne Zero Gamma tracée par DeepGamma.~~ Confirmé le 04/11/2026.
2. **DeepGamma en live** (issue #4) : si le panneau passe en « régime inconnu, DeepGamma figé » ou « trop ancien »
   pendant la séance US, la lecture live est bloquée et l'indicateur ne signale rien, comme prévu.
3. **Summary toujours négatif** sur les séances lues le 04/11/2026 : le régime sera surtout short gamma, donc surtout des
   setups B.
