# Recherche (Python)

Le labo d'études. Rien n'y est encore codé : il démarre à l'étape 2 de la [feuille de route](../docs/FEUILLE_DE_ROUTE.md),
une fois l'enregistreur en place.

## Organisation prévue

```
research/
  lib/                 lecture des .tdatac et des sorties de la DLL, agrégation minute UTC, coûts, simulateur
  facts/               étape 3 : faits connus (vol réalisée vs GEX MM, etc.)
  setup_a_wall/        un dossier par étude : PREREG.md, code, results/
  setup_b_momentum/
  setup_c_pin/
  scalp_flux/          scalps 1-10 min : order flow ES selon le régime, flux d'options (PREREG en brouillon)
```

## Règles

- **PREREG.md avant tout test**, sur le modèle de `tomcollet/deep/research/*/PREREG.md`.
- Coûts, découpage et critères : [docs/PROTOCOLE.md](../docs/PROTOCOLE.md). **Holdout scellé : séances à partir du
  16/08/2026.**
- Données hors du dépôt : `data/` à la racine (ignoré par git), ou un chemin passé en argument.
- Le lecteur `.tdatac` existe déjà (`read_tdatac.py` du banc `Ticks/lab` sur le Mac) : le reprendre ici plutôt que le
  réécrire.
