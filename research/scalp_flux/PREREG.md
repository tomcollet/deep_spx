# PREREG : scalps de 1 à 10 minutes sur l'order flow ES et le flux d'options

> **Brouillon du 04/11/2026, à valider par Tom avant de regarder la moindre donnée.** Une fois validé, ce fichier ne
> change plus ; toute modification ultérieure est datée et justifiée en bas de page.

## Pourquoi cette étude

Tom veut savoir s'il existe, en plus des setups A/B/C (horizon minutes à heures), des **scalps** fondés sur l'order flow
de l'ES et le flux d'options SPX.

Ce que l'on sait déjà (`tomcollet/deep`, `docs/RESULTATS.md`) : sur l'ES 2025, l'order flow au contact des niveaux
(delta, gros ordres agressifs, volume, durée du contact) ne fait pas mieux que des résultats mélangés, sur environ
9 330 règles ; les rejets de niveaux font aussi bien sur des niveaux décalés au hasard. Refaire ces tests n'a pas de
sens. Cette étude ne teste que ce qui est **nouveau ici** :

1. le **régime gamma** des market makers comme condition de l'order flow ;
2. le **flux d'options** (Option Net Flow), lisible à la minute et à jour en live (constaté le 04/11/2026).

Hors champ : le carnet (pas d'historique ; étude séparée après au moins 20 séances enregistrées en live, ou si le
Deep Replay le rejoue), Summary et Market Maker Pressure comme déclencheurs (3 minutes de retard en live, inutilisables
à cet horizon ; Summary sert seulement au régime, lu 5 barres en arrière).

## Définitions

- **Séance** : 09:30-16:00 New York. Événements entre 09:35 et 15:35 seulement (un horizon de 10 minutes doit finir
  avant 15:45).
- **Barre** : 1 minute, clôture à t. Toute décision prise à la clôture de t ; **entrée à l'ouverture de t+1**.
- **Delta** : volume à l'ask − volume au bid de la minute. **Rang du delta** : percentile de |delta| dans les 2 000
  dernières minutes de séance (comme le moteur), calculé avant d'ajouter la minute.
- **ΔNF** : variation sur une minute d'Option Net Flow / Difference. **Rang de ΔNF** : percentile de |ΔNF| dans les
  2 000 dernières minutes de séance.
- **Régime** : celui du moteur (`src/DeepSpxSetups/Engine`, v1.5), sans changement : Zero Gamma, bande de transition
  0,5 ATR5, signe de Summary lu 5 barres en arrière.
- **Rendement signé à h minutes** : `R_h = s × (Close(t+h) − Open(t+1)) / tick`, où s = +1 ou −1 est le sens annoncé
  par l'hypothèse. Sortie au temps uniquement : **pas de stop ni d'objectif** dans cette étude (un TP20 / SL40 gagne
  65 % du temps sur n'importe quel signal ; seule l'espérance compte).
- **Coûts** : 1 tick par exécution, donc **2 ticks par aller-retour**. Robustesse à 4 ticks par aller-retour.

## Hypothèses (fixées avant les données)

**H1, agression selon le régime.** Événement : minute dont le rang du delta est ≥ 90. Sens : celui du delta en short
gamma (continuation), l'inverse en long gamma (retour). Rien en transition ni en régime inconnu.
- Critère principal : **h = 5**. Secondaires, rapportés sans sélection : h = 1, 3, 10.
- Contrôle : mêmes événements, régimes permutés entre séances (1 000 tirages). H1 n'est retenue que si l'écart
  short gamma − long gamma réel dépasse 95 % des tirages.

**H2, le flux d'options précède l'ES.** Événement : minute dont le rang de ΔNF est ≥ 90. Sens : celui de ΔNF (Net Flow
qui monte : achat).
- Critère principal : **h = 3**. Secondaires : h = 1, 5, 10.
- Contrôle de l'order flow ES : régression `R_h(sens ΔNF) ~ a + b × signe(ΔNF) + c × rang signé du delta`, erreurs
  groupées par séance. H2 n'est retenue que si **b > 0** et si l'effet ne disparaît pas quand on retire les événements
  où le delta ES va dans le même sens.

## Données et découpage

- **Séances antérieures au 16/08/2026 uniquement** (holdout scellé). Sources : l'historique du graphique options SPX
  s'il remonte assez loin, sinon le Deep Replay, séance par séance.
- Calcul **dans la DLL**, avec le même code C# que le moteur, et sortie d'un **tableau de résultats** dans le log
  (quelques dizaines de lignes par lot de séances), pas des minutes brutes : le canal de sortie (fenêtre Log lue par
  `tools/read_log_window.py`) ne suit pas des dizaines de milliers de lignes.
- **Découpage chronologique** : 60 % premières séances en découverte, 40 % dernières en validation, **un seul passage**
  sur la validation.
- **Minimum 40 séances** au total. En dessous, l'étude conclut « données insuffisantes » sans résultat.

## Critères de décision

Un scalp n'est construit (setup D pour H1, E pour H2) que si, **pour le critère principal** :

1. découverte : signe prédit, intervalle de confiance à 95 % de l'espérance brute (bootstrap par séance) qui exclut 0 ;
2. découverte : espérance nette **≥ +1 tick** après 2 ticks d'aller-retour ;
3. validation : espérance nette ≥ +1 tick, et ≥ 0 à 4 ticks d'aller-retour ;
4. positif sur chaque moitié de la période de découverte.

Les 8 cellules (2 hypothèses × 4 horizons) sont toutes rapportées, retenues ou non. Aucun seuil (90e percentile,
fenêtre de 2 000 minutes) n'est optimisé dans cette étude : une grille, si elle est voulue, fera l'objet d'un autre
PREREG.

## Ce qui conclurait à l'échec

L'échec d'H1 et d'H2 au critère principal clôt la piste « scalp order flow + flux d'options » sur cette chaîne de
données. Il reste alors le carnet (étude séparée) et les setups A/B/C.

## Registre des modifications

- 04/11/2026 : brouillon.
