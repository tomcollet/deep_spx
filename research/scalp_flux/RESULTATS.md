# Résultats : scalps order flow ES et flux d'options (H1, H2)

Calcul de l'indicateur v1.6 sur le graphique options SPX de Tom, le 04/11/2026 à 20h23 (panneau relevé en capture
d'écran). Protocole : [PREREG.md](PREREG.md), fixé avant de regarder les données.

**41 séances avec DeepGamma, du 08/09/2026 au 04/11/2026** (au moins 40 : l'étude est concluante). Découverte : les
25 premières séances ; validation : les 16 dernières.

## Critère principal : stop 16 ticks, objectif 24 ticks (1,5R), sortie à 15 minutes

Seuil de rentabilité : 42,5 % de gagnants. Espérance nette par trade, 1 tick par exécution ; intervalle de confiance à
95 % par bootstrap des séances.

| Règle | Trades | Gagnants | Espérance | IC 95 % | Découverte | Validation |
|---|---|---|---|---|---|---|
| **H1 régime** (agression suivie en short gamma, contrée en long gamma) | 792 | 39,3 % | **−1,9** | −3,2 à −0,7 | −1,4 (454) | −2,6 (338) |
| H1 toujours suivie | 924 | 39,3 % | −1,8 | −2,9 à −0,5 | −1,9 (523) | −1,7 (401) |
| H1 toujours contrée | 984 | 39,7 % | −1,6 | −2,7 à −0,5 | −1,9 (561) | −1,2 (423) |
| H1 hasard | 948 | 41,2 % | −1,0 | −2,0 à 0,0 | −1,2 (534) | −0,8 (414) |
| **H2 Net Flow** (saut de Net Flow, dans son sens) | 971 | 38,3 % | **−2,1** | −3,2 à −0,9 | −2,3 (532) | −1,7 (439) |
| H2 sans ES même sens | 913 | 38,9 % | −1,8 | −2,9 à −0,9 | −2,2 (504) | −1,4 (409) |
| H2 hasard | 955 | 36,0 % | −2,8 | −3,7 à −1,9 | −2,5 (527) | −3,1 (428) |

Robustesse, 2 ticks par exécution : H1 régime −3,7, H2 Net Flow −3,8 ticks par trade.

La grille complète (stops 12, 20, 24) est dans les lignes `DSPX;STUDY` du log ; elle ne change pas la décision, qui se
prend au critère principal.

## Verdict : échec d'H1 et d'H2

- **Aucun critère du PREREG n'est rempli** : espérances négatives, intervalles de confiance sous zéro, en découverte
  comme en validation.
- **Les règles font comme le hasard.** Un bracket placé au hasard depuis l'ouverture perd en moyenne le coût
  d'exécution, environ 2 ticks (1 tick à l'entrée, qui décale le bracket, et 1 tick à la sortie). Toutes les
  règles sont dans cette zone. La part de gagnants aussi : environ 37,5 % pour un bracket +25 / −15 ticks depuis
  l'ouverture sur une marche au hasard, entre 36 et 41 % ici.
- **Le régime gamma n'apporte rien à cet horizon** : suivre, contrer, ou choisir selon le régime donne la même chose.
- **Le flux d'options ne précède pas l'ES** à 15 minutes : H2 ne bat pas son tirage au sort de façon utile.

Conformément au PREREG, **la piste « scalp order flow + flux d'options » est close sur cette chaîne de données.** Ces
41 séances ont servi : une variante (autres seuils, autres horizons) testée dessus serait du sur-ajustement. Une
nouvelle hypothèse de scalp demanderait un nouveau PREREG et d'autres séances (Deep Replay d'avant le 08/09, ou les
séances live à venir).

Ce résultat rejoint `tomcollet/deep` (order flow au contact des niveaux sans edge sur l'ES 2025).

## Ce qui reste ouvert

- **Le carnet** (déséquilibres, recharge au contact, sweeps reconstitués par `AggrID`) : jamais mesuré, pas d'historique.
  Il faut l'enregistrer en live (une ligne par minute dans le log) pendant 20 à 30 séances.
- **Les setups A/B/C**, à l'horizon minutes-heures, où le coût pèse moins : étude sur les séances d'avant le 16/08/2026.
