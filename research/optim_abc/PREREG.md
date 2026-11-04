# PREREG : recherche d'une configuration des setups A/B/C

> **Fixé le 04/11/2026 à 20h30, à la demande de Tom** (« trouve une config qui maximise le winrate et les gains en
> ticks » ; graphique en minutes sur 1 an). Fixé avant tout calcul. Toute modification ultérieure est datée et
> justifiée en bas de page.

## Le piège à éviter

Sur 64 configurations, la meilleure en historique est toujours flatteuse, même s'il n'y a aucun avantage : c'est la
plus chanceuse. Elle ne se juge que sur des séances qu'elle n'a pas servi à choisir. D'où un découpage en trois :
recherche (où l'on classe), validation (où l'on vérifie, une fois, les 3 premières seulement), holdout (test final).

**Taux de gain et gains en ticks ne se maximisent pas ensemble** : un objectif proche et un stop lointain font gagner
souvent et perdre de l'argent. La règle de risque de Tom (stop ≤ 25 ticks, objectif ≥ 1,5R) borne cette géométrie ; le
classement se fait donc sur l'**espérance en ticks par trade**, et le taux de gain est rapporté à côté de son seuil de
rentabilité.

## Ce qui ne change pas

Le moteur v1.7 tel quel : séance 09:30-16:00 New York, pas d'entrée avant 09:35 ni après 15:45, à plat à 15:45, une
position à la fois, 3 trades et 2 pertes par jour au plus, entrée à l'ouverture suivante + 1 tick, sorties − 1 tick,
stop prioritaire dans la même barre, sortie à l'ouverture sur gap, stops et objectifs au tick, Summary lu 5 barres en
arrière, **règle de risque de Tom : signal refusé au-delà de 25 ticks de risque ou sous 1,5R**.

## La grille (64 configurations, un setup à la fois)

Objectif de sortie, pour A et B : **structurel** (VWAP pour A, niveau suivant pour B, comme en v1.6), **1,5R** ou **2R**
(objectif à 1,5 ou 2 fois le risque depuis l'entrée attendue).

| Setup | Paramètres | Configurations |
|---|---|---|
| A, rejet de mur (long gamma) | bande de transition 0,25 / 0,5 ATR5 × objectif (3) × absorption au 80e / 90e percentile × écart à la VWAP 1,0 / 1,5 ATR5 | 24 |
| B, continuation (short gamma) | bande de transition (2) × objectif (3) × delta de cassure au 70e / 80e / 90e percentile × Net Flow exigé ou non | 36 |
| C, aimant (long gamma, dès 14:00) | bande de transition (2) × stop à 0,5 / 1 ATR5 au-delà de l'extrême ; objectif : le strike | 4 |

**Contrôle** : chaque configuration tourne aussi **en sens inversé** (même entrée, stop et objectif en miroir). Une
configuration dont la version inversée fait aussi bien ne doit rien à son signal.

## Données et découpage

- Graphique options SPX, barres d'une minute, 1 an d'historique. **Séances avec DeepGamma antérieures au 16/08/2026**
  (le holdout d'A/B/C reste scellé). Un trade compte pour la séance de son signal.
- Le panneau rapporte la couverture : nombre de séances avec DeepGamma, première date, part des barres de séance
  ayant des volumes à l'ask et au bid (sans eux, pas de delta, pas de signal).
- **Recherche : les 60 % premières séances. Validation : les 40 % dernières.** Holdout : séances à partir du 16/08/2026,
  ouvert une seule fois, sur décision de Tom.

## Règle de sélection (fixée d'avance)

1. **Recherche** : pour chaque setup, configurations à au moins 30 trades, classées par la **borne basse de
   l'intervalle de confiance à 95 %** de l'espérance nette (bootstrap par séance). Cela pénalise les petits
   échantillons et les résultats tirés par quelques trades.
2. **Les 3 premières** de chaque setup, et elles seules, sont évaluées en **validation**. Les autres n'y sont jamais
   regardées.
3. Une configuration est **retenue** si, en validation : espérance nette ≥ +1 tick par trade, taux de gain au-dessus de
   son seuil de rentabilité, version inversée moins bonne, et espérance ≥ 0 avec 1 tick de plus par exécution
   (approximation : 2 ticks de moins par trade).
4. Parmi les retenues d'un setup, on garde **la mieux classée en recherche** : la validation dit oui ou non, elle ne
   classe pas.
5. Aucune retenue : le setup est sans avantage démontré dans cette grille, et on n'élargit pas la grille sur ces
   séances.

Ensuite, une seule fois : la configuration retenue sur le holdout (séances à partir du 16/08/2026), puis 4 semaines de
simulation en live.

## Registre des modifications

- 04/11/2026, 20h30 : version initiale.
