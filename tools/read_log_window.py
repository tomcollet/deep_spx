#!/usr/bin/env python3
"""Lit les lignes DSPX; affichees dans la fenetre "Log" de DeepCharts.

Le fichier de log de Deepchart est chiffre sur disque, mais la fenetre Log de l'application affiche le texte en clair.
Ce script lit cette fenetre par l'API d'accessibilite de Windows (UI Automation), comme le ferait un lecteur d'ecran :
il ne touche ni aux fichiers ni au code de DeepCharts.

Prerequis (Windows) :
    pip install uiautomation

Usage, fenetre Log de DeepCharts ouverte et visible (onglet "Log list", tri par heure decroissante) :
    python tools/read_log_window.py --dump                                  # structure de la fenetre (diagnostic)
    python tools/read_log_window.py --out data/probe/log_dspx.txt           # une lecture des lignes visibles
    python tools/read_log_window.py --follow --out data/probe/log_dspx.txt  # suit la fenetre, ajoute les nouvelles lignes

Le fichier produit se relit avec tools/extract_dspx.py. Une grille "virtualisee" n'expose que les lignes visibles :
agrandis la fenetre, et en mode --follow les lignes les plus recentes (en haut) sont lues a chaque passage.
"""
import argparse
import pathlib
import sys
import time

PREFIX = "DSPX;"
ROW_TYPES = {"DataItemControl", "ListItemControl", "TreeItemControl"}

try:
    import uiautomation as auto
except ImportError:
    print("Module manquant. Installe-le : pip install uiautomation", file=sys.stderr)
    sys.exit(1)


def find_window(title: str):
    win = auto.WindowControl(searchDepth=1, Name=title)
    if not win.Exists(3, 0.5):
        return None
    return win


def texts_of(ctrl) -> list:
    """Textes portes par un controle : nom, valeur, valeur d'accessibilite."""
    out = []
    try:
        if ctrl.Name:
            out.append(ctrl.Name)
    except Exception:
        pass
    for getter in ("GetValuePattern", "GetLegacyIAccessiblePattern"):
        try:
            pattern = getattr(ctrl, getter)()
            value = getattr(pattern, "Value", "") if pattern else ""
            if value:
                out.append(value)
        except Exception:
            pass
    return out


def dump(win, max_depth: int, max_nodes: int) -> None:
    n = 0
    for ctrl, depth in auto.WalkControl(win, includeTop=True, maxDepth=max_depth):
        name = (ctrl.Name or "").replace("\n", " ")[:90]
        print(f"{'  ' * depth}{ctrl.ControlTypeName}  name='{name}'  class='{ctrl.ClassName}'  id='{ctrl.AutomationId}'")
        n += 1
        if n >= max_nodes:
            print(f"... arrete apres {max_nodes} controles (--max-nodes)")
            break


def read_rows(win, max_depth: int, max_rows: int) -> list:
    """Lignes visibles contenant DSPX; : 'heure<TAB>message' si la grille expose des lignes, sinon le message seul."""
    rows, found_row_controls = [], False
    for ctrl, _ in auto.WalkControl(win, includeTop=False, maxDepth=max_depth):
        if ctrl.ControlTypeName not in ROW_TYPES:
            continue
        found_row_controls = True
        cells = []
        for sub, _ in auto.WalkControl(ctrl, includeTop=True, maxDepth=4):
            for t in texts_of(sub):
                if t not in cells:
                    cells.append(t)
        message = next((c for c in cells if PREFIX in c), None)
        if message is None:
            continue
        stamp = next((c for c in cells if len(c) >= 19 and c[:4].isdigit() and c[4] == "-"), "")
        rows.append(f"{stamp}\t{message[message.find(PREFIX):]}")
        if len(rows) >= max_rows:
            break
    if not found_row_controls:
        # Grille sans controles de ligne : on prend tout texte qui contient DSPX;
        for ctrl, _ in auto.WalkControl(win, includeTop=False, maxDepth=max_depth):
            for t in texts_of(ctrl):
                if PREFIX in t:
                    rows.append("\t" + t[t.find(PREFIX):])
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--title", default="Log", help="titre de la fenetre (defaut : Log)")
    ap.add_argument("--dump", action="store_true", help="affiche la structure de la fenetre et s'arrete")
    ap.add_argument("--out", type=pathlib.Path, help="fichier ou ajouter les lignes lues")
    ap.add_argument("--follow", action="store_true", help="relit la fenetre en boucle et ajoute les nouvelles lignes")
    ap.add_argument("--interval", type=float, default=1.0, help="secondes entre deux lectures en mode --follow")
    ap.add_argument("--max-depth", type=int, default=25)
    ap.add_argument("--max-rows", type=int, default=400)
    ap.add_argument("--max-nodes", type=int, default=600)
    args = ap.parse_args()

    win = find_window(args.title)
    if win is None:
        print(f"Fenetre '{args.title}' introuvable : ouvre la fenetre Log de DeepCharts.", file=sys.stderr)
        return 1
    if args.dump:
        dump(win, min(args.max_depth, 12), args.max_nodes)
        return 0

    seen = set()
    out = args.out.open("a", encoding="utf-8") if args.out else None
    try:
        while True:
            rows = read_rows(win, args.max_depth, args.max_rows)
            # La fenetre affiche les plus recentes en haut : on ecrit dans l'ordre chronologique
            new = [r for r in reversed(rows) if r not in seen]
            for r in new:
                seen.add(r)
                if out:
                    out.write(r + "\n")
                else:
                    print(r)
            if out:
                out.flush()
            if not args.follow:
                print(f"{len(rows)} lignes {PREFIX} visibles, {len(new)} ecrites" + (f" dans {args.out}" if out else ""),
                      file=sys.stderr)
                if not rows:
                    print("Aucune ligne lue : lance --dump et envoie le resultat.", file=sys.stderr)
                return 0
            if new:
                print(f"{time.strftime('%H:%M:%S')}  +{len(new)} lignes", file=sys.stderr)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        return 0
    finally:
        if out:
            out.close()


if __name__ == "__main__":
    sys.exit(main())
