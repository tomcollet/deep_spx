# PREREG : scalps sur l'order flow ES et le flux d'options

> **Fixé le 04/11/2026 à 20h, à la demande de Tom**, avec sa règle de risque et sa décision sur le holdout. Les règles
> ci-dessous sont celles que calcule l'indicateur (`src/DeepSpxSetups/Engine/ScalpStudy.cs`, v1.6). Toute modification
> ultérieure est datée et justifiée en bas de page.

## Pourquoi cette étude

Tom veut savoir s'il existe, en plus des setups A/B/C, des **scalps** fondés sur l'order flow de l'ES et le flux
d'options SPX.

Ce que l'on sait déjà (`tomcollet/deep`, `docs/RESULTATS.md`) : sur l'ES 2025, l'order flow au contact des niveaux
(delta, gros ordres agressifs, volume, durée du contact) ne fait pas mieux que des résultats mélangés, sur environ
9 330 règles ; les rejets de niveaux font aussi bien sur des niveaux décalés au hasard. Cette étude ne teste que ce qui
est **nouveau ici** :

1. le **régime gamma** des market makers comme condition de l'order flow ;
2. le **flux d'options** (Option Net Flow), à jour chaque minute en live (constaté le 04/11/2026).

Hors champ : le carnet (pas d'historique ; étude séparée après au moins 20 séances enregistrées en live, ou si le
Deep Replay le rejoue), Summary et Market Maker Pressure comme déclencheurs (3 minutes de retard en live ; Summary
sert seulement au régime, lu 5 barres en arrière).

## Règle de risque de Tom

**Jamais plus de 25 ticks de risque, objectif d'au moins 1,5 fois le risque.** Les scalps sont donc des brackets
stop / objectif à 1,5R, stop au plus 25 ticks.

## Définitions

- **Séance** : 09:30-16:00 New York. Événements entre 09:35 et 15:30 ; tout est à plat à 15:45.
- **Barre** : 1 minute. Décision à la clôture de t ; **entrée à l'ouverture de t+1**, + 1 tick contre soi.
- **Delta** : volume à l'ask − volume au bid de la minute. **Rang du delta** : percentile de |delta| parmi les 2 000
  dernières minutes de séance, calculé avant d'ajouter la minute (celui du moteur, 200 minutes au moins).
- **ΔNF** : variation sur une minute d'Option Net Flow / Difference ; **rang de ΔNF** calculé de la même façon.
- **Régime** : celui du moteur (v1.6), sans changement : Zero Gamma, bande de transition 0,5 ATR5, signe de Summary
  lu 5 barres en arrière.
- **Trade** : bracket depuis le prix d'entrée, **stop S ticks, objectif 1,5 × S ticks**, sortie au temps après
  15 minutes, à plat à 15:45. Stop et objectif dans la même minute : stop. Ouverture au-delà du stop : sortie à
  l'ouverture. Sortie − 1 tick contre soi. **Une position à la fois** par règle et par stop.
- **Coûts** : 1 tick par exécution. Seuil de rentabilité à 1,5R : (S + 1) / 2,5 S, soit **42,5 % pour S = 16**.

## Hypothèses

**H1, agression selon le régime.** Événement : minute dont le rang du delta est ≥ 90. Sens : celui du delta en short
gamma (continuation), l'inverse en long gamma (retour). Rien en transition ni en régime inconnu.
Contrôles, aux mêmes événements : **toujours suivie**, **toujours contrée**, **sens tiré au sort**.

**H2, le flux d'options précède l'ES.** Événement : minute dont le rang de ΔNF est ≥ 90. Sens : celui de ΔNF (Net Flow
qui monte : achat).
Contrôles : **sans ES dans le même sens** (on retire les événements où le delta ES va dans le même sens avec un rang
≥ 50 : l'effet ne doit pas venir de l'ES lui-même), **sens tiré au sort**.

**Critère principal, pour les deux : stop 16 ticks, objectif 24 ticks.** Grille rapportée sans sélection : stops 12,
16, 20 et 24 ticks (objectifs 18, 24, 30, 36). Aucun seuil (90e percentile, fenêtre de 2 000 minutes, 15 minutes)
n'est optimisé dans cette étude.

## Données et découpage

- **Holdout ouvert pour les scalps, décision de Tom du 04/11/2026.** Données : l'historique chargé sur le graphique
  options SPX (environ 60 jours), séances avec DeepGamma **jusqu'au 04/11/2026 inclus**. Les setups A/B/C gardent leur
  holdout (séances à partir du 16/08/2026) : leurs statistiques restent masquées.
- **Découpage chronologique** : 60 % premières séances en découverte, 40 % dernières en validation.
- **Test final** : les séances live **à partir du 05/11/2026**, en simulation, jamais incluses dans l'étude
  (`StudyEndDate` = 20261105). Au moins 20 séances, évaluées une seule fois.
- **Minimum 40 séances** pour découverte + validation. En dessous : « non concluant », affiché dans le panneau.
- Calcul dans la DLL (même code C# que le moteur). Sorties : bloc du panneau, lignes `DSPX;STUDYINFO` et `DSPX;STUDY`
  (une par règle et par stop), à la fin du chargement puis toutes les heures.

## Critères de décision

Un scalp n'est construit (setup D pour H1, E pour H2) que si, **au critère principal** :

1. découverte : espérance nette **≥ +1 tick par trade**, intervalle de confiance à 95 % (bootstrap par séance) au-dessus
   de 0 ;
2. découverte : la règle bat chacun de ses contrôles (H1 : suivie, contrée, hasard ; H2 : hasard, et H2 sans ES reste
   positive) ;
3. validation : espérance nette ≥ +1 tick ;
4. robustesse : ≥ 0 avec 2 ticks par exécution (calculée par l'indicateur, ligne « Robustesse » du panneau).

Puis le test final sur les séances live : espérance nette ≥ +1 tick.

Les cellules de la grille sont toutes rapportées, retenues ou non. Une grille de seuils, si elle est voulue, fera
l'objet d'un autre PREREG.

## Ce qui conclurait à l'échec

L'échec d'H1 et d'H2 au critère principal clôt la piste « scalp order flow + flux d'options » sur cette chaîne de
données. Il reste le carnet (étude séparée) et les setups A/B/C.

## Registre des modifications

- 04/11/2026, 20h03 : brouillon (sorties au temps, horizons 1 à 10 minutes, séances d'avant le 16/08).
- 04/11/2026, 20h08 : demande de Tom. Brackets à 1,5R et stop au plus 25 ticks (sa règle de risque) à la place des
  sorties au temps ; contrôles explicites ; calcul dans l'indicateur ; holdout ouvert pour les scalps (60 jours
  chargés), test final sur les séances live à partir du 05/11/2026. Aucune donnée regardée avant ce changement.
