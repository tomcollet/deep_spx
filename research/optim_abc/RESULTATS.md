# Résultats : recherche de configuration A/B/C

Protocole : [PREREG.md](PREREG.md).

## Couverture des données (graphique options SPX, 1 an de barres d'une minute, 04/11/2026)

- **DeepGamma n'existe dans l'historique qu'à partir du 11/05/2026.** Avant le holdout (16/08/2026) : **44 séances**,
  27 en recherche, 17 en validation. Volumes à l'ask et au bid sur 100 % des barres de séance.
- C'est peu : une configuration à 1 trade par jour fait environ 27 trades en recherche.

## Passage 1 (v1.7, 04/11/2026 à 20h37) : 64 configurations, règle de risque de Tom (25 ticks, 1,5R)

| Setup | Résultat |
|---|---|
| A | aucune configuration à 30 trades en recherche |
| B | n° 1 (bande de transition 0,25, objectif 1,5R, delta au 70e percentile, sans Net Flow) : recherche 55 trades, 34,5 % de gagnants pour un seuil de 42,6 %, **−3,0 ticks par trade** (IC −8,0 à +2,0), total −164 ticks ; validation 35 trades, 37,1 %, +0,2 tick par trade, sens inverse −7,9. **Rejetée** (espérance < +1 tick, négative avec 1 tick de plus par exécution) |
| C | aucune configuration à 30 trades en recherche |

**Aucune configuration retenue.** La meilleure configuration B, choisie parmi 36, perd en recherche.

## Passage 2 (v1.8) : 104 configurations, sans règle de risque, classement par taux de gain

À venir.
