# Stratégie : régime gamma × zone × déclencheur order flow

Instrument : **ES** (MES pour le dimensionnement). Horizon : **quelques minutes à quelques heures**, intraday, aucune
position au-delà de la séance régulière.

## L'hypothèse économique

L'edge intraday vient d'acteurs **obligés** de trader, pas d'indicateurs :

- **Couverture des market makers (gamma).** Quand leur gamma net est positif, ils vendent la hausse et achètent la
  baisse : les mouvements s'amortissent, le prix revient vers les gros strikes. Quand il est négatif, ils achètent la
  hausse et vendent la baisse : les mouvements s'amplifient.
- **Dérive du charm et aimantation en fin de séance.** Les couvertures 0DTE se dénouent l'après-midi, et le prix tend à
  coller aux gros strikes du jour.
- **La liquidité du carnet** décide si un flux passe (le prix bouge) ou est absorbé (le prix tient).

Les options donnent le **régime** et les **zones**. L'order flow donne le **moment**. L'order flow seul ne suffit pas :
dans le dépôt `deep`, les signaux d'agressivité et de delta n'ont rien prédit au contact des niveaux, et la cassure de
la veille n'a pas fait mieux que le hasard sur l'ES en 2025. Le régime gamma est la variable qui manquait.

Les données de positionnement viennent de **DeepGamma** (données Cboe par catégorie de participant, mises à jour chaque
minute sur SPX), lues depuis le graphique par la DLL. Voir [SDK_DEEPCHARTS.md](SDK_DEEPCHARTS.md).

## Les trois couches

| Couche | Rôle | Paramètres | Source |
|---|---|---|---|
| Régime (chaque minute) | Quel type de trade est permis | GEX MM net (signe, percentile), prix vs Zero Gamma, Net Flow MM, Expected Move, heure, agenda macro | DeepGamma, horloge, fichier agenda |
| Zones | Où agir | Major/Minor Gamma, Call/Put walls, Zero Gamma, VWAP, range d'ouverture 30 min, plus haut et plus bas de la veille, POC/VAH/VAL | DeepGamma, barres, profil |
| Déclencheurs | Quand agir | Absorption, delta par minute, OFI 30 s, épuisement de la profondeur, delta cumulé | Trades et carnet ES |

## Les trois setups (valeurs de départ, à tester, pas à optimiser)

### A. Rejet de mur, en régime long gamma

- **Régime** : GEX MM net positif, prix au-dessus du Zero Gamma d'au moins 0,2 × Expected Move.
- **Zone** : à 4 ticks ou moins d'un Call wall (vente) ou d'un Put wall (achat), écart à la VWAP d'au moins
  0,5 × Expected Move.
- **Déclencheur** : absorption. Delta agressif dans le sens du mouvement au-dessus de son 90e percentile, avancée du prix
  de 2 ticks au plus, carnet qui se recharge au niveau.
- **Sortie** : objectif VWAP ou niveau gamma suivant ; stop au-delà du mur + 1 ATR 5 min ; sortie forcée après 45 min.

### B. Continuation, en régime short gamma

- **Régime** : GEX MM net négatif ou prix sous le Zero Gamma.
- **Zone** : cassure d'un niveau (range d'ouverture, plus bas ou plus haut de la veille, Put wall).
- **Déclencheur** : bougie 1 min qui clôture au-delà du niveau ; delta et OFI 30 s dans le même sens au-dessus de leur
  80e percentile ; profondeur d'en face en baisse d'au moins 30 % par rapport à sa moyenne 5 min ; Net Flow MM dans le
  sens du trade.
- **Sortie** : niveau gamma suivant ou 1 × Expected Move, avec stop suiveur ; stop si le prix repasse le niveau de
  4 ticks.

### C. Aimant de fin de séance (14h-15h45, heure de New York)

- **Régime** : GEX MM net positif, gros strike 0DTE à moins de 0,3 × Expected Move.
- **Déclencheur** : écart au strike avec absorption.
- **Sortie** : objectif au strike ; FLAT obligatoire à 15h45.

## Filtres communs

- **Pas de trade** : les 5 premières minutes de la séance, à moins de 15 min d'une annonce macro, dans la zone de
  transition autour du Zero Gamma.
- **Limites** : 3 trades par jour au plus, arrêt après 2 pertes, perte journalière plafonnée sous la limite de la prop
  firm.

## Discipline des paramètres

- Seuils en **percentiles glissants sur 20 séances**, pour s'adapter à la volatilité.
- **4 paramètres libres au plus par setup**, grille fixée avant le test et écrite dans le PREREG de l'étude.
- Méta-modèle (gradient boosting qui filtre et dimensionne) seulement si un setup dépasse 500 occurrences.

Coûts, découpage des données et critères d'acceptation : [PROTOCOLE.md](PROTOCOLE.md).
