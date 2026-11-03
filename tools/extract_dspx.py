#!/usr/bin/env python3
"""Extrait les lignes DSPX; du log de Deepchart et les range par type d'enregistrement.

La DLL n'a pas le droit d'ecrire de fichier (Deepchart refuse fichiers, reseau, reflexion...) : elle ecrit dans le log
de Deepchart avec VAn.AddToLog, une ligne par enregistrement, prefixe "DSPX;". Ce script retrouve ces lignes, quel que
soit ce que Deepchart ajoute devant (heure, niveau), et ecrit un fichier par type : CAT.csv, HSER.csv, SER.csv,
BOOKMIN.csv, TICKMIN.csv, VERDICT.csv...

Usage :
    python tools/extract_dspx.py CHEMIN_DU_LOG [CHEMIN_DU_LOG ...] --out dossier_de_sortie

Bibliotheque standard uniquement.
"""
import argparse
import collections
import pathlib
import sys

PREFIX = "DSPX;"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("logs", nargs="+", type=pathlib.Path, help="fichiers de log de Deepchart")
    parser.add_argument("--out", type=pathlib.Path, default=pathlib.Path("dspx_out"), help="dossier de sortie")
    args = parser.parse_args()

    records = collections.defaultdict(list)
    total = 0
    for log in args.logs:
        with log.open(encoding="utf-8", errors="replace") as f:
            for line in f:
                pos = line.find(PREFIX)
                if pos < 0:
                    continue
                fields = line[pos + len(PREFIX):].rstrip("\r\n").split(";")
                if not fields or not fields[0]:
                    continue
                records[fields[0]].append(fields[1:])
                total += 1

    if total == 0:
        print("Aucune ligne DSPX; trouvee. Verifie le fichier de log et que la sonde a bien tourne.", file=sys.stderr)
        return 1

    args.out.mkdir(parents=True, exist_ok=True)
    for kind, rows in sorted(records.items()):
        path = args.out / f"{kind}.csv"
        with path.open("w", encoding="utf-8", newline="") as f:
            for row in rows:
                f.write(";".join(row) + "\n")
        print(f"{kind:<10} {len(rows):>7} lignes -> {path}")

    print("\nVerdicts (derniers ecrits) :")
    last = {}
    for row in records.get("VERDICT", []):
        if row:
            last[row[0]] = ";".join(row[1:])
    for q in sorted(last):
        print(f"  {q} : {last[q]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
