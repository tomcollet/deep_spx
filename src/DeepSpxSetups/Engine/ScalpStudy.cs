using System;
using System.Collections.Generic;

namespace DeepSpx.Engine
{
    /// Regles de l'etude des scalps (research/scalp_flux/PREREG.md).
    public enum ScalpRule
    {
        H1Regime,   // agression extreme : suivie en short gamma, contree en long gamma
        H1Follow,   // controle : toujours suivie
        H1Fade,     // controle : toujours contree
        H1Random,   // controle : sens tire au sort
        H2NetFlow,  // saut de Net Flow : dans son sens
        H2Pure,     // idem, sans agression ES dans le meme sens
        H2Random    // controle : sens tire au sort
    }

    /// Reglages de l'etude, fixes dans le PREREG avant les donnees.
    public sealed class ScalpSettings
    {
        public DateTime StudyStart = DateTime.MinValue;          // premiere seance etudiee (incluse)
        // Exclue. Decision de Tom du 04/11/2026 : le holdout est ouvert pour les scalps ; recherche et validation sur
        // l'historique charge jusqu'au 04/11 inclus, test final sur les seances live a partir du 05/11/2026.
        public DateTime StudyEnd = new DateTime(2026, 11, 5);
        public static readonly DateTime ForwardTestStart = new DateTime(2026, 11, 5);
        public double EventPct = 90;                             // evenement : rang >= 90e percentile
        public int[] StopTicks = { 12, 16, 20, 24 };             // regle de Tom : jamais plus de 25 ticks
        public int PrimaryStopTicks = 16;                        // critere principal : stop 16, objectif 24
        public double RewardRisk = 1.5;                          // regle de Tom : 1,5R minimum
        public int TimeStopBars = 15;
        public int FirstEntryOffset = 5;                         // pas d'evenement avant 09:35
        public int LastEventBeforeEnd = 30;                      // ni apres 15:30
        public double DiscoveryShare = 0.6;                      // 60 % premieres seances : decouverte
        public int BootstrapDraws = 500;
        public int Seed = 20261104;
    }

    public sealed class ScalpTrade
    {
        public int Session;
        public double Ticks;
        public ExitReason Exit;
    }

    /// Un compte simule par regle et par stop : une position a la fois.
    public sealed class ScalpAccount
    {
        public ScalpRule Rule;
        public int StopTicks;
        public int TargetTicks;
        public double SlipTicks = 1;  // glissement par execution (robustesse : 2)
        public readonly List<ScalpTrade> Trades = new List<ScalpTrade>();

        internal int Pending;        // sens de l'entree en attente (0 : aucune)
        internal int Side;           // sens de la position (0 : aucune)
        internal double Entry, Stop, Target;
        internal int EntryBar;

        public string Name => ScalpStudy.RuleText(Rule) + " SL" + StopTicks + "/TP" + TargetTicks + (SlipTicks != 1 ? " glissement " + SlipTicks : "");
    }

    /// Resultat d'un compte sur un ensemble de seances.
    public sealed class ScalpResult
    {
        public int Sessions, Trades, Wins;
        public double AvgTicks = double.NaN, CiLow = double.NaN, CiHigh = double.NaN, BreakEvenPct;
        public double WinPct => Trades > 0 ? 100.0 * Wins / Trades : double.NaN;
    }

    /// Etude des scalps de 1 a 15 minutes : agression ES selon le regime (H1), saut de Net Flow (H2). Fonctionne barre
    /// par barre a cote de SetupEngine, dont elle lit le regime et le rang du delta. Seules les seances
    /// [StudyStart, StudyEnd) avec DeepGamma sont comptees : par defaut jusqu'au 04/11/2026 inclus, les seances live
    /// suivantes formant le test final.
    public sealed class ScalpStudy
    {
        private readonly EngineSettings S;
        private readonly ScalpSettings P;
        private readonly RollingPercentile _absDnf;
        private readonly Random _rnd;
        private double _lastNf = double.NaN;
        private DateTime _sessionDate = DateTime.MinValue;

        public readonly List<ScalpAccount> Accounts = new List<ScalpAccount>();
        public readonly List<DateTime> SessionDates = new List<DateTime>();
        public int EventsH1 { get; private set; }
        public int EventsH2 { get; private set; }
        public ScalpSettings Settings => P;

        public ScalpStudy(EngineSettings engineSettings, ScalpSettings settings)
        {
            S = engineSettings;
            P = settings;
            _absDnf = new RollingPercentile(S.PercentileWindowBars);
            _rnd = new Random(P.Seed);
            foreach (int st in P.StopTicks)
            {
                if (S.MaxRiskTicks > 0 && st > S.MaxRiskTicks) continue;
                Add(ScalpRule.H1Regime, st);
                Add(ScalpRule.H2NetFlow, st);
            }
            foreach (var r in new[] { ScalpRule.H1Follow, ScalpRule.H1Fade, ScalpRule.H1Random, ScalpRule.H2Pure, ScalpRule.H2Random })
                Add(r, P.PrimaryStopTicks);
            // robustesse (critere 4 du PREREG) : 2 ticks par execution au critere principal
            Add(ScalpRule.H1Regime, P.PrimaryStopTicks, 2);
            Add(ScalpRule.H2NetFlow, P.PrimaryStopTicks, 2);
        }

        private void Add(ScalpRule rule, int stop, double slipTicks = -1)
        {
            Accounts.Add(new ScalpAccount { Rule = rule, StopTicks = stop, TargetTicks = (int)Math.Round(stop * P.RewardRisk),
                                            SlipTicks = slipTicks >= 0 ? slipTicks : S.SlippageTicks });
        }

        public ScalpAccount Find(ScalpRule rule, int stop, double slipTicks = -1)
        {
            double slip = slipTicks >= 0 ? slipTicks : S.SlippageTicks;
            foreach (var a in Accounts) if (a.Rule == rule && a.StopTicks == stop && a.SlipTicks == slip) return a;
            return null;
        }

        /// A appeler apres SetupEngine.OnBar sur la meme barre.
        public void OnBar(BarInput bar, SetupEngine engine)
        {
            if (bar == null || engine == null) return;
            double tick = S.TickSize > 0 ? S.TickSize : 0.25;
            int minute = bar.Time.Hour * 60 + bar.Time.Minute;
            bool rth = minute >= S.RthStartMinute && minute < S.RthEndMinute;
            bool inStudy = bar.Time.Date >= P.StudyStart.Date && bar.Time.Date < P.StudyEnd.Date;

            // une seance ne compte que si DeepGamma y est present (Zero Gamma ou Net Flow)
            bool gamma = bar.Gamma != null && (Valid(bar.Gamma.ZeroGamma) || Valid(bar.Gamma.NetFlow));
            if (rth && inStudy && gamma && bar.Time.Date != _sessionDate)
            {
                _sessionDate = bar.Time.Date;
                SessionDates.Add(_sessionDate);
            }
            int session = SessionDates.Count - 1;

            // 1. Entrees en attente, puis positions
            foreach (var a in Accounts)
            {
                double slip = a.SlipTicks * tick;
                if (a.Pending != 0)
                {
                    int side = a.Pending;
                    a.Pending = 0;
                    if (rth && minute < S.ForcedExitMinute)
                    {
                        a.Side = side;
                        a.Entry = bar.Open + side * slip;
                        a.Stop = a.Entry - side * a.StopTicks * tick;
                        a.Target = a.Entry + side * a.TargetTicks * tick;
                        a.EntryBar = bar.Index;
                    }
                }
                if (a.Side != 0) Manage(a, bar, minute, rth, tick, slip, session);
            }

            // 2. Flux d'options : variation de Net Flow sur une minute, et son rang
            double nf = bar.Gamma != null ? bar.Gamma.NetFlow : double.NaN;
            double dnf = double.NaN;
            if (Valid(nf) && Valid(_lastNf)) dnf = nf - _lastNf;
            if (Valid(nf)) _lastNf = nf;
            double dnfRank = double.NaN;
            bool dnfReady = _absDnf.Count >= S.PercentileMinSamples;
            if (!double.IsNaN(dnf))
            {
                dnfRank = _absDnf.Rank(Math.Abs(dnf));
                if (rth) _absDnf.Add(Math.Abs(dnf));
            }

            // 3. Evenements, a la cloture : entree a l'ouverture de la barre suivante
            if (!rth || !inStudy) return;
            if (minute < S.RthStartMinute + P.FirstEntryOffset || minute >= S.RthEndMinute - P.LastEventBeforeEnd) return;
            if (!engine.RankReady) return;

            double delta = engine.LastDelta;
            int sd = Math.Sign(delta);
            if (sd != 0 && engine.LastDeltaRank >= P.EventPct)
            {
                EventsH1++;
                int random = _rnd.Next(2) == 0 ? 1 : -1;      // tire a chaque evenement, que le compte soit libre ou non
                int regimeSide = engine.Regime == Regime.ShortGamma ? sd : engine.Regime == Regime.LongGamma ? -sd : 0;
                Signal(ScalpRule.H1Regime, regimeSide);
                Signal(ScalpRule.H1Follow, sd);
                Signal(ScalpRule.H1Fade, -sd);
                Signal(ScalpRule.H1Random, random);
            }
            int sn = double.IsNaN(dnf) ? 0 : Math.Sign(dnf);
            if (sn != 0 && dnfReady && dnfRank >= P.EventPct)
            {
                EventsH2++;
                int random = _rnd.Next(2) == 0 ? 1 : -1;
                bool esSameWay = sd == sn && engine.LastDeltaRank >= 50;
                Signal(ScalpRule.H2NetFlow, sn);
                Signal(ScalpRule.H2Pure, esSameWay ? 0 : sn);
                Signal(ScalpRule.H2Random, random);
            }
        }

        private void Signal(ScalpRule rule, int side)
        {
            if (side == 0) return;
            foreach (var a in Accounts)
                if (a.Rule == rule && a.Side == 0 && a.Pending == 0) a.Pending = side;
        }

        private void Manage(ScalpAccount a, BarInput bar, int minute, bool rth, double tick, double slip, int session)
        {
            bool isLong = a.Side > 0;
            bool stopHit = isLong ? bar.Low <= a.Stop : bar.High >= a.Stop;
            bool targetHit = isLong ? bar.High >= a.Target : bar.Low <= a.Target;
            if (stopHit)
            {
                double basePrice = isLong ? Math.Min(a.Stop, bar.Open) : Math.Max(a.Stop, bar.Open);
                Close(a, basePrice - a.Side * slip, ExitReason.Stop, tick, session);
            }
            else if (targetHit) Close(a, a.Target - a.Side * slip, ExitReason.Target, tick, session);
            else if (!rth || minute >= S.ForcedExitMinute) Close(a, bar.Close - a.Side * slip, ExitReason.SessionEnd, tick, session);
            else if (bar.Index - a.EntryBar >= P.TimeStopBars) Close(a, bar.Close - a.Side * slip, ExitReason.Time, tick, session);
        }

        private void Close(ScalpAccount a, double price, ExitReason why, double tick, int session)
        {
            a.Trades.Add(new ScalpTrade { Session = Math.Max(0, session), Ticks = Math.Round(a.Side * (price - a.Entry) / tick, 2), Exit = why });
            a.Side = 0;
        }

        // ------------------------------------------------------------ resultats
        public int DiscoverySessions => (int)Math.Ceiling(SessionDates.Count * P.DiscoveryShare);

        /// part : 0 toutes les seances, 1 decouverte, 2 validation.
        public ScalpResult Result(ScalpAccount a, int part)
        {
            int n = SessionDates.Count, cut = DiscoverySessions;
            int from = part == 2 ? cut : 0, to = part == 1 ? cut : n;
            var r = new ScalpResult { Sessions = Math.Max(0, to - from) };
            // seuil de rentabilite : gain = objectif - glissement de sortie, perte = stop + glissement de sortie
            r.BreakEvenPct = 100.0 * (a.StopTicks + a.SlipTicks) / (a.StopTicks + a.TargetTicks);
            if (r.Sessions <= 0) return r;

            var sum = new double[r.Sessions];
            var cnt = new int[r.Sessions];
            double total = 0;
            foreach (var t in a.Trades)
            {
                if (t.Session < from || t.Session >= to) continue;
                r.Trades++;
                if (t.Ticks > 0) r.Wins++;
                total += t.Ticks;
                sum[t.Session - from] += t.Ticks;
                cnt[t.Session - from]++;
            }
            if (r.Trades == 0) return r;
            r.AvgTicks = total / r.Trades;

            // intervalle de confiance a 95 % : bootstrap par seance (les trades d'une meme seance ne sont pas independants)
            var rnd = new Random(P.Seed + (int)a.Rule * 97 + a.StopTicks + (int)(a.SlipTicks * 1000));
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

        public static string RuleText(ScalpRule r)
        {
            switch (r)
            {
                case ScalpRule.H1Regime: return "H1 regime";
                case ScalpRule.H1Follow: return "H1 toujours suivie";
                case ScalpRule.H1Fade: return "H1 toujours contree";
                case ScalpRule.H1Random: return "H1 hasard";
                case ScalpRule.H2NetFlow: return "H2 Net Flow";
                case ScalpRule.H2Pure: return "H2 sans ES meme sens";
                default: return "H2 hasard";
            }
        }

        private static bool Valid(double v) => !double.IsNaN(v) && v != 0.0;
    }
}
