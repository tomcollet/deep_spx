using System;
using System.Collections.Generic;

namespace DeepSpx.Engine
{
    /// Reglages de la recherche de configuration (research/optim_abc/PREREG.md), fixes avant les donnees.
    public sealed class OptimizerSettings
    {
        public DateTime Start = DateTime.MinValue;               // premiere seance (incluse)
        public DateTime End = new DateTime(2026, 8, 16);         // exclue : holdout d'A/B/C scelle a partir du 16/08/2026
        public double DiscoveryShare = 0.6;
        public int MinTrades = 30;                               // en recherche, pour etre classee
        public int TopForValidation = 3;
        public double MinValidationTicks = 1.0;
        /// Classement (demande de Tom du 04/11/2026, 20h37) : par taux de gain (borne basse de Wilson a 95 %), parmi les
        /// configurations dont l'esperance en recherche est positive. Sans cette condition, le meilleur taux de gain
        /// est celui de l'objectif le plus proche et du stop le plus lointain, qui perd de l'argent.
        public bool RankByWinRate = true;
        public int BootstrapDraws = 500;
        public int Seed = 20261104;
    }

    public sealed class OptTrade
    {
        public int Session;
        public double Ticks;
        public bool Win;
    }

    /// Une configuration de la grille : un moteur normal et son controle en sens inverse.
    public sealed class OptConfig
    {
        public string Setup;           // "A", "B" ou "C"
        public string Label;           // parametres en clair
        public EngineSettings Settings;
        public SetupEngine Engine, Flipped;
        public readonly List<OptTrade> Trades = new List<OptTrade>();
        public readonly List<OptTrade> FlippedTrades = new List<OptTrade>();
    }

    public sealed class OptResult
    {
        public int Sessions, Trades, Wins;
        public double AvgTicks = double.NaN, TotalTicks, CiLow = double.NaN, CiHigh = double.NaN, AvgWinTicks = double.NaN, AvgLossTicks = double.NaN;
        public double WinPct => Trades > 0 ? 100.0 * Wins / Trades : double.NaN;
        /// Taux de gain qui annule l'esperance avec les gains et pertes moyens observes.
        /// Sans perte : 0 (tout taux de gain le depasse) ; sans gain : 100.
        public double BreakEvenPct =>
            Trades == 0 ? double.NaN : double.IsNaN(AvgWinTicks) ? 100 : double.IsNaN(AvgLossTicks) ? 0
            : 100.0 * -AvgLossTicks / (AvgWinTicks - AvgLossTicks);
    }

    public static class Wilson
    {
        /// Borne basse de l'intervalle de Wilson a 95 % d'une proportion (en %).
        public static double Low(int wins, int n)
        {
            if (n <= 0) return double.NaN;
            double z = 1.96, p = (double)wins / n, z2 = z * z;
            double centre = p + z2 / (2 * n);
            double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n));
            return 100.0 * (centre - half) / (1 + z2 / n);
        }
    }

    /// Verdict d'une configuration classee dans les 3 premieres de son setup.
    public sealed class OptVerdict
    {
        public OptConfig Config;
        public int Rank;
        public OptResult Discovery, Validation, ValidationFlipped;
        public bool Retained;
        public string Reason = "";
    }

    /// Fait tourner les 64 configurations (et leurs controles) barre par barre, puis classe et valide selon le PREREG.
    public sealed class Optimizer
    {
        private readonly OptimizerSettings P;
        private readonly Dictionary<DateTime, int> _sessionIndex = new Dictionary<DateTime, int>();
        public readonly List<DateTime> SessionDates = new List<DateTime>();
        public readonly List<OptConfig> Configs = new List<OptConfig>();
        public long RthBars { get; private set; }
        public long RthBarsWithVolume { get; private set; }
        public OptimizerSettings Settings => P;

        public Optimizer(EngineSettings baseSettings, OptimizerSettings settings)
        {
            P = settings;
            var transitions = new[] { 0.25, 0.5 };
            var targets = new[] { 0, 5, 10, 15, 20 };              // 0 structurel, sinon dixiemes de R : 0,5R a 2R
            foreach (double tr in transitions)
                foreach (int tg in targets)
                {
                    foreach (double absorb in new[] { 80.0, 90.0 })
                        foreach (double vwap in new[] { 1.0, 1.5 })
                        {
                            var s = Base(baseSettings, "A", tr, tg);
                            s.AbsorbPct = absorb;
                            s.VwapDevAtr = vwap;
                            Add("A", "transition " + Num(tr) + ", objectif " + TargetText(tg) + ", absorption " + Num(absorb) + ", VWAP " + Num(vwap) + " ATR5", s);
                        }
                    foreach (double brk in new[] { 70.0, 80.0, 90.0 })
                        foreach (bool nf in new[] { true, false })
                        {
                            var s = Base(baseSettings, "B", tr, tg);
                            s.BreakDeltaPct = brk;
                            s.RequireNetFlow = nf;
                            Add("B", "transition " + Num(tr) + ", objectif " + TargetText(tg) + ", delta " + Num(brk) + (nf ? ", Net Flow exige" : ", sans Net Flow"), s);
                        }
                }
            foreach (double tr in transitions)
                foreach (double stopAtr in new[] { 0.5, 1.0 })
                {
                    var s = Base(baseSettings, "C", tr, 0);
                    s.StopAtr = stopAtr;
                    Add("C", "transition " + Num(tr) + ", stop " + Num(stopAtr) + " ATR5, objectif le strike", s);
                }
        }

        private static EngineSettings Base(EngineSettings b, string setup, double transition, int target)
        {
            var s = b.Clone();
            s.EnableWallReject = setup == "A";
            s.EnableContinuation = setup == "B";
            s.EnablePin = setup == "C";
            s.TransitionAtr = transition;
            s.TargetMode = target == 0 ? 0 : 1;
            s.TargetR = target / 10.0;
            s.FlipSide = false;
            s.MaxRiskTicks = 0;                                     // regle de risque retiree par Tom (04/11/2026, 20h37)
            s.MinRewardRisk = 0;
            return s;
        }

        private void Add(string setup, string label, EngineSettings s)
        {
            var f = s.Clone();
            f.FlipSide = true;
            Configs.Add(new OptConfig { Setup = setup, Label = label, Settings = s, Engine = new SetupEngine(s), Flipped = new SetupEngine(f) });
        }

        private static string TargetText(int t) => t == 0 ? "structurel" : (t / 10.0).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "R";

        private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        public void OnBar(BarInput bar)
        {
            if (bar == null) return;
            var es = Configs.Count > 0 ? Configs[0].Settings : null;
            if (es != null)
            {
                int minute = bar.Time.Hour * 60 + bar.Time.Minute;
                bool rth = minute >= es.RthStartMinute && minute < es.RthEndMinute;
                bool inRange = bar.Time.Date >= P.Start.Date && bar.Time.Date < P.End.Date;
                bool gamma = bar.Gamma != null && !double.IsNaN(bar.Gamma.ZeroGamma) && bar.Gamma.ZeroGamma != 0;
                if (rth && inRange)
                {
                    RthBars++;
                    if (bar.AskVolume + bar.BidVolume > 0) RthBarsWithVolume++;
                    if (gamma && !_sessionIndex.ContainsKey(bar.Time.Date))
                    {
                        _sessionIndex[bar.Time.Date] = SessionDates.Count;
                        SessionDates.Add(bar.Time.Date);
                    }
                }
            }
            foreach (var c in Configs)
            {
                c.Engine.OnBar(bar);
                Collect(c.Engine, c.Trades);
                c.Flipped.OnBar(bar);
                Collect(c.Flipped, c.FlippedTrades);
            }
        }

        private void Collect(SetupEngine e, List<OptTrade> into)
        {
            foreach (var s in e.NewExits)
            {
                if (!s.Filled || s.Exit == ExitReason.Cancelled) continue;
                int session;
                if (!_sessionIndex.TryGetValue(s.Time.Date, out session)) continue;   // hors periode ou sans DeepGamma
                into.Add(new OptTrade { Session = session, Ticks = s.ResultTicks, Win = s.ResultTicks > 0 });
            }
        }

        // ------------------------------------------------------------ resultats
        public int DiscoverySessions => (int)Math.Ceiling(SessionDates.Count * P.DiscoveryShare);

        /// part : 0 toutes les seances, 1 recherche, 2 validation. extraTicks : cout ajoute par trade (robustesse).
        public OptResult Result(List<OptTrade> trades, int part, double extraTicks = 0, int seed = 0)
        {
            int n = SessionDates.Count, cut = DiscoverySessions;
            int from = part == 2 ? cut : 0, to = part == 1 ? cut : n;
            var r = new OptResult { Sessions = Math.Max(0, to - from) };
            if (r.Sessions <= 0) return r;
            var sum = new double[r.Sessions];
            var cnt = new int[r.Sessions];
            double winSum = 0, lossSum = 0;
            int losses = 0;
            foreach (var t in trades)
            {
                if (t.Session < from || t.Session >= to) continue;
                double v = t.Ticks - extraTicks;
                r.Trades++;
                r.TotalTicks += v;
                if (v > 0) { r.Wins++; winSum += v; } else { losses++; lossSum += v; }
                sum[t.Session - from] += v;
                cnt[t.Session - from]++;
            }
            if (r.Trades == 0) return r;
            r.AvgTicks = r.TotalTicks / r.Trades;
            if (r.Wins > 0) r.AvgWinTicks = winSum / r.Wins;
            if (losses > 0) r.AvgLossTicks = lossSum / losses;

            var rnd = new Random(P.Seed + seed);
            var means = new List<double>(P.BootstrapDraws);
            for (int d = 0; d < P.BootstrapDraws; d++)
            {
                double s = 0; int c = 0;
                for (int i = 0; i < r.Sessions; i++)
                {
                    int k = rnd.Next(r.Sessions);
                    s += sum[k];
                    c += cnt[k];
                }
                if (c > 0) means.Add(s / c);
            }
            if (means.Count >= 20)
            {
                means.Sort();
                r.CiLow = means[(int)Math.Floor(0.025 * (means.Count - 1))];
                r.CiHigh = means[(int)Math.Ceiling(0.975 * (means.Count - 1))];
            }
            return r;
        }

        /// Pour information : la configuration au taux de gain brut le plus eleve en recherche, gagnante ou non.
        public OptVerdict BestRawWinRate(string setup)
        {
            OptVerdict best = null;
            int i = 0;
            foreach (var c in Configs)
            {
                i++;
                if (c.Setup != setup) continue;
                var d = Result(c.Trades, 1, 0, i);
                if (d.Trades < P.MinTrades) continue;
                if (best == null || Wilson.Low(d.Wins, d.Trades) > Wilson.Low(best.Discovery.Wins, best.Discovery.Trades))
                    best = new OptVerdict { Config = c, Discovery = d };
            }
            return best;
        }

        /// Les 3 premieres configurations d'un setup en recherche, et leur verdict en validation (regle du PREREG).
        public List<OptVerdict> Select(string setup)
        {
            var ranked = new List<OptVerdict>();
            int i = 0;
            foreach (var c in Configs)
            {
                i++;
                if (c.Setup != setup) continue;
                var d = Result(c.Trades, 1, 0, i);
                if (d.Trades < P.MinTrades || double.IsNaN(d.CiLow)) continue;
                if (P.RankByWinRate && !(d.AvgTicks > 0)) continue;   // gagnante en recherche, sinon le taux de gain ne veut rien dire
                ranked.Add(new OptVerdict { Config = c, Discovery = d });
            }
            if (P.RankByWinRate)
                ranked.Sort((a, b) => Wilson.Low(b.Discovery.Wins, b.Discovery.Trades).CompareTo(Wilson.Low(a.Discovery.Wins, a.Discovery.Trades)));
            else
                ranked.Sort((a, b) => b.Discovery.CiLow.CompareTo(a.Discovery.CiLow));
            if (ranked.Count > P.TopForValidation) ranked.RemoveRange(P.TopForValidation, ranked.Count - P.TopForValidation);
            for (int k = 0; k < ranked.Count; k++)
            {
                var v = ranked[k];
                v.Rank = k + 1;
                v.Validation = Result(v.Config.Trades, 2, 0, 1000 + k);
                v.ValidationFlipped = Result(v.Config.FlippedTrades, 2, 0, 2000 + k);
                var robust = Result(v.Config.Trades, 2, 2, 3000 + k);
                var reasons = new List<string>();
                if (v.Validation.Trades == 0) reasons.Add("aucun trade en validation");
                else
                {
                    if (!(v.Validation.AvgTicks >= P.MinValidationTicks)) reasons.Add("esperance < +" + Num(P.MinValidationTicks) + " t");
                    if (!(v.Validation.WinPct > v.Validation.BreakEvenPct)) reasons.Add("taux de gain sous le seuil");
                    if (!(double.IsNaN(v.ValidationFlipped.AvgTicks) || v.ValidationFlipped.AvgTicks < v.Validation.AvgTicks)) reasons.Add("le sens inverse fait aussi bien");
                    if (!(robust.AvgTicks >= 0)) reasons.Add("negatif avec 1 tick de plus par execution");
                }
                v.Retained = reasons.Count == 0;
                v.Reason = v.Retained ? "RETENUE" : "rejetee : " + string.Join(", ", reasons);
            }
            return ranked;
        }
    }
}
