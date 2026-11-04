using System;
using System.Collections.Generic;

namespace DeepSpx.Engine
{
    /// Moteur de setups sur barres 1 min fermees, identique en historique, en replay et en live.
    ///
    /// A chaque barre fermee (OnBar) :
    ///   1. l'entree en attente est executee a l'ouverture de cette barre (signal de la barre precedente : pas
    ///      d'anticipation) ;
    ///   2. la position ouverte est geree sur cette barre (stop prioritaire si stop et objectif sont touches dans la
    ///      meme barre, sortie au temps, FLAT de fin de seance) ;
    ///   3. les mesures sont mises a jour (ATR, rang du delta, VWAP, range d'ouverture, niveaux de la veille, DeepGamma) ;
    ///   4. le regime est calcule, puis les setups C, A et B sont evalues ; un signal devient une entree en attente pour
    ///      la barre suivante.
    public sealed class SetupEngine
    {
        private readonly EngineSettings S;
        private readonly WilderAtr _atr;
        private readonly RollingPercentile _absDelta;

        // Seance reguliere
        private bool _inRth;
        private DateTime _rthDate;
        private double _sumPv, _sumV;
        private double _orHigh, _orLow, _curHigh, _curLow, _prevHigh = double.NaN, _prevLow = double.NaN;
        private bool _orDone, _hasCur;
        private int _tradesToday, _lossesToday;
        private double _lastClose = double.NaN;

        // DeepGamma : derniere valeur valide (non nulle, non NaN) de chaque champ
        private double _zero = double.NaN, _major = double.NaN, _minor = double.NaN, _summary = double.NaN,
                       _netFlow = double.NaN, _mm = double.NaN;
        private int _lastValidBar = -1, _lastChangeBar = -1, _lastIndex = -1;
        // Fraicheur propre a Summary et Net Flow : en live, ils peuvent valoir 0 ou rester figes (issue #4, sonde du
        // 04/11/2026) alors que les niveaux d'Option Envelopes bougent encore.
        private int _summaryValidBar = -1, _summaryChangeBar = -1, _netFlowValidBar = -1;
        private readonly Queue<double> _netFlowHist = new Queue<double>();

        private Signal _pending, _position;
        private int _nextId = 1;

        public readonly List<Signal> Signals = new List<Signal>();
        public readonly List<Signal> NewSignals = new List<Signal>();
        public readonly List<Signal> NewFills = new List<Signal>();
        public readonly List<Signal> NewExits = new List<Signal>();
        public readonly SetupStats StatsA = new SetupStats(), StatsB = new SetupStats(), StatsC = new SetupStats(), StatsAll = new SetupStats();

        public EngineSettings Settings => S;

        // Mesures de la derniere barre, pour l'etude des scalps (ScalpStudy)
        public double LastDelta { get; private set; }
        public double LastDeltaRank { get; private set; } = double.NaN;
        public bool RankReady { get; private set; }

        // Signaux refuses par la regle de risque (25 ticks, 1,5R)
        public int RejectedRisk { get; private set; }
        public int RejectedReward { get; private set; }

        public SetupEngine(EngineSettings settings)
        {
            S = settings ?? new EngineSettings();
            _atr = new WilderAtr(S.AtrPeriod);
            _absDelta = new RollingPercentile(S.PercentileWindowBars);
        }

        // ------------------------------------------------------------ etat lisible (panneau, journal)
        public Regime Regime { get; private set; }
        public string RegimeReason { get; private set; } = "";
        public double Atr5 => _atr.Value * Math.Sqrt(5);
        public double Vwap => _sumV > 0 ? _sumPv / _sumV : double.NaN;
        public double ZeroGamma => _zero;
        public double MajorGamma => _major;
        public double MinorGamma => _minor;
        public double Summary => _summary;
        public double MmPressure => _mm;
        public double NetFlowSlope { get; private set; } = double.NaN;
        public int GammaAgeBars => _lastValidBar < 0 ? int.MaxValue : _lastIndex - _lastValidBar;
        public bool GammaStale { get; private set; }
        /// Summary absent, nul ou fige depuis plus de StaleGammaBars barres : son signe n'entre plus dans le regime.
        public bool SummaryStale { get; private set; } = true;
        public bool InRth => _inRth;
        public double OpeningHigh => _orDone ? _orHigh : double.NaN;
        public double OpeningLow => _orDone ? _orLow : double.NaN;
        public double PrevHigh => _prevHigh;
        public double PrevLow => _prevLow;
        public int TradesToday => _tradesToday;
        public int LossesToday => _lossesToday;
        public Signal Position => _position;
        public Signal Pending => _pending;
        public string Blocked { get; private set; } = "";

        // ------------------------------------------------------------ barre fermee
        public void OnBar(BarInput bar)
        {
            NewSignals.Clear();
            NewFills.Clear();
            NewExits.Clear();
            if (bar == null) return;
            _lastIndex = bar.Index;
            double tick = S.TickSize > 0 ? S.TickSize : 0.25;
            int minute = bar.Time.Hour * 60 + bar.Time.Minute;
            bool rth = minute >= S.RthStartMinute && minute < S.RthEndMinute;

            // Nouvelle seance reguliere
            if (rth && (!_inRth || bar.Time.Date != _rthDate))
            {
                if (_hasCur) { _prevHigh = _curHigh; _prevLow = _curLow; }
                _rthDate = bar.Time.Date;
                _sumPv = _sumV = 0;
                _orHigh = double.MinValue; _orLow = double.MaxValue; _orDone = false;
                _curHigh = double.MinValue; _curLow = double.MaxValue; _hasCur = false;
                _tradesToday = _lossesToday = 0;
            }
            _inRth = rth;

            // 1. Entree en attente : a l'ouverture de cette barre
            if (_pending != null)
            {
                var p = _pending;
                _pending = null;
                double fillPrice = bar.Open + (int)p.Side * S.SlippageTicks * tick;
                double fillRisk = (int)p.Side * (fillPrice - p.Stop) / tick;
                if (rth && minute < S.ForcedExitMinute && fillRisk > 0 && fillRisk <= S.MaxRiskTicks + 1e-9)
                {
                    p.Filled = true;
                    p.EntryBar = bar.Index;
                    p.EntryTime = bar.Time;
                    p.EntryPrice = bar.Open + (int)p.Side * S.SlippageTicks * tick;
                    _position = p;
                    NewFills.Add(p);
                }
                else
                {
                    // hors seance, ouverture deja au-dela du stop, ou risque porte au-dela de 25 ticks : pas d'entree
                    p.Closed = true;
                    p.Exit = ExitReason.Cancelled;
                    NewExits.Add(p);
                }
            }

            // 2. Gestion de la position
            if (_position != null) ManagePosition(bar, minute, rth, tick);

            // 3. Mesures
            double delta = bar.AskVolume - bar.BidVolume;
            double deltaRank = _absDelta.Rank(Math.Abs(delta));   // rang par rapport aux barres precedentes
            bool rankReady = _absDelta.Count >= S.PercentileMinSamples;
            LastDelta = delta;
            LastDeltaRank = deltaRank;
            RankReady = rankReady;
            if (rth) _absDelta.Add(Math.Abs(delta));
            _atr.Add(bar.High, bar.Low, bar.Close);
            if (rth)
            {
                double px = bar.Vwap > 0 ? bar.Vwap : (bar.High + bar.Low + bar.Close) / 3.0;
                double vol = bar.Volume > 0 ? bar.Volume : 1;
                _sumPv += px * vol;
                _sumV += vol;
                _curHigh = Math.Max(_curHigh, bar.High);
                _curLow = Math.Min(_curLow, bar.Low);
                _hasCur = true;
                if (minute < S.RthStartMinute + S.OpeningRangeMinutes)
                {
                    _orHigh = Math.Max(_orHigh, bar.High);
                    _orLow = Math.Min(_orLow, bar.Low);
                }
                else if (_orHigh > double.MinValue) _orDone = true;
            }
            UpdateGamma(bar.Index, bar.Gamma, rth);

            // 4. Regime puis setups
            double atr5 = Atr5;
            ComputeRegime(bar.Close, atr5);
            Blocked = "";
            if (!rth) Blocked = "hors seance reguliere";
            else if (minute < S.RthStartMinute + S.NoTradeFirstMinutes) Blocked = "premieres minutes";
            else if (minute >= S.LastEntryMinute) Blocked = "plus d'entree apres l'heure limite";
            else if (_position != null) Blocked = "en position";
            else if (_tradesToday >= S.MaxTradesPerDay) Blocked = "maximum de trades du jour";
            else if (_lossesToday >= S.MaxLossesPerDay) Blocked = "maximum de pertes du jour";
            else if (!_atr.Ready || !rankReady) Blocked = "mesures en cours de calcul";
            else if (Regime == Regime.Unknown) Blocked = "regime inconnu (" + RegimeReason + ")";
            else if (Regime == Regime.Transition) Blocked = "zone de transition autour du Zero Gamma";

            if (Blocked.Length == 0)
            {
                Signal s = null;
                if (s == null && S.EnablePin && Regime == Regime.LongGamma && minute >= S.PinStartMinute)
                    s = TryPin(bar, delta, deltaRank, atr5, tick);
                if (s == null && S.EnableWallReject && Regime == Regime.LongGamma)
                    s = TryWallReject(bar, delta, deltaRank, atr5, tick);
                if (s == null && S.EnableContinuation && Regime == Regime.ShortGamma)
                    s = TryContinuation(bar, delta, deltaRank, atr5, tick);
                if (s != null)
                {
                    // Regle de risque : entree attendue = cloture + glissement
                    double expected = bar.Close + (int)s.Side * S.SlippageTicks * tick;
                    double risk = (int)s.Side * (expected - s.Stop) / tick;
                    double reward = (int)s.Side * (s.Target - expected) / tick;
                    if (risk > S.MaxRiskTicks + 1e-9)
                    {
                        RejectedRisk++;
                        Blocked = KindLetter(s.Kind) + " refuse : risque " + risk.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ticks > " + S.MaxRiskTicks;
                        s = null;
                    }
                    else if (reward < S.MinRewardRisk * risk - 1e-9)
                    {
                        RejectedReward++;
                        Blocked = KindLetter(s.Kind) + " refuse : objectif " + reward.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " t < " +
                                  S.MinRewardRisk.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + " x risque " + risk.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " t";
                        s = null;
                    }
                }
                if (s != null)
                {
                    s.Id = _nextId++;
                    s.BarIndex = bar.Index;
                    s.Time = bar.Time;
                    s.Regime = Regime;
                    s.SignalClose = bar.Close;
                    s.Delta = delta;
                    s.DeltaRank = deltaRank;
                    s.Atr5 = atr5;
                    s.ZeroGamma = _zero;
                    s.DistZeroTicks = double.IsNaN(_zero) ? double.NaN : (bar.Close - _zero) / tick;
                    s.NetFlowSlope = NetFlowSlope;
                    s.Summary = _summary;
                    s.Vwap = Vwap;
                    _pending = s;
                    _tradesToday++;
                    Signals.Add(s);
                    NewSignals.Add(s);
                }
            }
            _lastClose = bar.Close;
        }

        // ------------------------------------------------------------ gestion de la position
        private void ManagePosition(BarInput bar, int minute, bool rth, double tick)
        {
            var p = _position;
            double slip = S.SlippageTicks * tick;
            int held = bar.Index - p.EntryBar;
            bool isLong = p.Side == Side.Long;
            bool stopHit = isLong ? bar.Low <= p.Stop : bar.High >= p.Stop;
            bool targetHit = isLong ? bar.High >= p.Target : bar.Low <= p.Target;
            int timeStop = p.Kind == SetupKind.WallReject ? S.TimeStopBarsA : p.Kind == SetupKind.Continuation ? S.TimeStopBarsB : S.TimeStopBarsC;

            if (stopHit)
            {
                // ouverture au-dela du stop (gap) : execution a l'ouverture, pas au stop
                double basePrice = isLong ? Math.Min(p.Stop, bar.Open) : Math.Max(p.Stop, bar.Open);
                Close(p, bar, ExitReason.Stop, basePrice - (int)p.Side * slip, tick);
            }
            else if (targetHit) Close(p, bar, ExitReason.Target, p.Target - (int)p.Side * slip, tick);
            else if (!rth || minute >= S.ForcedExitMinute) Close(p, bar, ExitReason.SessionEnd, bar.Close - (int)p.Side * slip, tick);
            else if (held >= timeStop) Close(p, bar, ExitReason.Time, bar.Close - (int)p.Side * slip, tick);
        }

        private void Close(Signal p, BarInput bar, ExitReason why, double price, double tick)
        {
            p.Closed = true;
            p.Exit = why;
            p.ExitBar = bar.Index;
            p.ExitTime = bar.Time;
            p.ExitPrice = price;
            p.ResultTicks = Math.Round((int)p.Side * (price - p.EntryPrice) / tick, 2);
            _position = null;
            if (p.ResultTicks < 0) _lossesToday++;
            StatsAll.Add(p.ResultTicks);
            if (p.Kind == SetupKind.WallReject) StatsA.Add(p.ResultTicks);
            else if (p.Kind == SetupKind.Continuation) StatsB.Add(p.ResultTicks);
            else if (p.Kind == SetupKind.Pin) StatsC.Add(p.ResultTicks);
            NewExits.Add(p);
        }

        // ------------------------------------------------------------ DeepGamma et regime
        private static bool Valid(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v != 0.0;

        private void UpdateGamma(int index, GammaInput g, bool rth)
        {
            if (g == null) g = new GammaInput();
            bool any = false, changed = false;
            Take(g.ZeroGamma, ref _zero, ref any, ref changed);
            Take(g.MajorGamma, ref _major, ref any, ref changed);
            Take(g.MinorGamma, ref _minor, ref any, ref changed);
            if (Valid(g.SummaryRegime))
            {
                if (double.IsNaN(_summary) || g.SummaryRegime != _summary) _summaryChangeBar = index;
                _summaryValidBar = index;
            }
            Take(g.SummaryRegime, ref _summary, ref any, ref changed);
            Take(g.NetFlow, ref _netFlow, ref any, ref changed);
            Take(g.MmPressure, ref _mm, ref any, ref changed);
            if (any) _lastValidBar = index;
            if (changed || _lastChangeBar < 0) _lastChangeBar = index;
            GammaStale = rth && index - _lastChangeBar > S.StaleGammaBars;

            SummaryStale = _summaryValidBar < 0 || index - _summaryValidBar > S.MaxGammaAgeBars ||
                           index - _summaryChangeBar > S.StaleGammaBars;

            if (Valid(g.NetFlow))
            {
                _netFlowHist.Enqueue(g.NetFlow);
                while (_netFlowHist.Count > S.NetFlowLookbackBars + 1) _netFlowHist.Dequeue();
                _netFlowValidBar = index;
            }
            else if (_netFlowValidBar >= 0 && index - _netFlowValidBar > S.MaxGammaAgeBars)
                _netFlowHist.Clear();                   // Net Flow perdu (0 en live) : plus de pente, pas de setup B
            if (_netFlowHist.Count == S.NetFlowLookbackBars + 1)
            {
                double first = _netFlowHist.Peek();
                NetFlowSlope = _netFlow - first;
            }
            else NetFlowSlope = double.NaN;
        }

        private static void Take(double v, ref double field, ref bool any, ref bool changed)
        {
            if (!Valid(v)) return;
            any = true;
            if (double.IsNaN(field) || v != field) changed = true;
            field = v;
        }

        private void ComputeRegime(double close, double atr5)
        {
            if (!Valid(_zero)) { Regime = Regime.Unknown; RegimeReason = "Zero Gamma absent"; return; }
            if (GammaAgeBars > S.MaxGammaAgeBars) { Regime = Regime.Unknown; RegimeReason = "DeepGamma trop ancien (" + GammaAgeBars + " barres)"; return; }
            if (GammaStale && S.BlockOnStaleGamma) { Regime = Regime.Unknown; RegimeReason = "DeepGamma fige depuis plus de " + S.StaleGammaBars + " barres"; return; }
            if (!(atr5 > 0)) { Regime = Regime.Unknown; RegimeReason = "ATR en cours de calcul"; return; }

            double dist = close - _zero;
            double band = S.TransitionAtr * atr5;
            int sign = S.UseSummarySign && Valid(_summary) && !SummaryStale ? Math.Sign(_summary) : 0;

            if (Math.Abs(dist) < band) { Regime = Regime.Transition; RegimeReason = "prix a moins de " + S.TransitionAtr + " ATR5 du Zero Gamma"; return; }
            if (dist < 0 || sign < 0)
            {
                Regime = Regime.ShortGamma;
                RegimeReason = dist < 0 ? "prix sous le Zero Gamma" : "Option Summary negatif";
                return;
            }
            // Au-dessus du Zero Gamma : long gamma si Summary positif, ou si Summary n'est pas utilise ou absent
            Regime = Regime.LongGamma;
            RegimeReason = sign > 0 ? "prix au-dessus du Zero Gamma, Summary positif"
                : "prix au-dessus du Zero Gamma" + (S.UseSummarySign && SummaryStale ? " (Summary absent ou fige, ignore)" : "");
        }

        // ------------------------------------------------------------ setup A : rejet de mur
        private Signal TryWallReject(BarInput bar, double delta, double rank, double atr5, double tick)
        {
            double vwap = Vwap;
            if (double.IsNaN(vwap)) return null;
            Signal best = null;
            Consider(ref best, WallAt(bar, _major, "Major Gamma", delta, rank, atr5, tick, vwap));
            Consider(ref best, WallAt(bar, _minor, "Minor Gamma", delta, rank, atr5, tick, vwap));
            return best;
        }

        private Signal WallAt(BarInput bar, double wall, string name, double delta, double rank, double atr5, double tick, double vwap)
        {
            if (!Valid(wall)) return null;
            double zone = S.ZoneTicks * tick, progress = S.AbsorbMaxProgressTicks * tick, minTarget = S.MinTargetTicks * tick;
            double stopDist = Math.Max(S.MinStopTicks * tick, S.StopAtr * atr5);

            // Resistance : contact par le bas, achats agressifs absorbes, prix etire au-dessus de la VWAP -> vente
            if (bar.High >= wall - zone && bar.High <= wall + progress && bar.Close < wall &&
                delta > 0 && rank >= S.AbsorbPct && bar.Close - vwap >= S.VwapDevAtr * atr5 && bar.Close - vwap >= minTarget)
                return Make(SetupKind.WallReject, Side.Short, name, wall, Math.Max(bar.High, wall) + stopDist, vwap);

            // Support : contact par le haut, ventes agressives absorbees, prix etire sous la VWAP -> achat
            if (bar.Low <= wall + zone && bar.Low >= wall - progress && bar.Close > wall &&
                delta < 0 && rank >= S.AbsorbPct && vwap - bar.Close >= S.VwapDevAtr * atr5 && vwap - bar.Close >= minTarget)
                return Make(SetupKind.WallReject, Side.Long, name, wall, Math.Min(bar.Low, wall) - stopDist, vwap);
            return null;
        }

        // ------------------------------------------------------------ setup B : continuation
        private Signal TryContinuation(BarInput bar, double delta, double rank, double atr5, double tick)
        {
            if (double.IsNaN(_lastClose) || rank < S.BreakDeltaPct) return null;
            var names = new List<string>();
            var levels = new List<double>();
            AddLevel(names, levels, "range d'ouverture haut", OpeningHigh);
            AddLevel(names, levels, "range d'ouverture bas", OpeningLow);
            AddLevel(names, levels, "plus haut de la veille", _prevHigh);
            AddLevel(names, levels, "plus bas de la veille", _prevLow);
            AddLevel(names, levels, "Major Gamma", _major);
            AddLevel(names, levels, "Minor Gamma", _minor);
            AddLevel(names, levels, "Zero Gamma", _zero);

            double brk = S.BreakTicks * tick, back = S.StopBackTicks * tick, minStop = S.MinStopTicks * tick, minTarget = S.MinTargetTicks * tick;
            bool flowUp = !S.RequireNetFlow || (!double.IsNaN(NetFlowSlope) && NetFlowSlope > 0);
            bool flowDown = !S.RequireNetFlow || (!double.IsNaN(NetFlowSlope) && NetFlowSlope < 0);

            for (int i = 0; i < levels.Count; i++)
            {
                double L = levels[i];
                if (_lastClose <= L && bar.Close >= L + brk && delta > 0 && flowUp)
                {
                    double stop = Math.Min(L - back, bar.Close - minStop);
                    double target = NextLevelAbove(levels, bar.Close + minTarget);
                    if (double.IsNaN(target)) target = bar.Close + Math.Max(minTarget, S.TargetAtr * atr5);
                    return Make(SetupKind.Continuation, Side.Long, names[i], L, stop, target);
                }
                if (_lastClose >= L && bar.Close <= L - brk && delta < 0 && flowDown)
                {
                    double stop = Math.Max(L + back, bar.Close + minStop);
                    double target = NextLevelBelow(levels, bar.Close - minTarget);
                    if (double.IsNaN(target)) target = bar.Close - Math.Max(minTarget, S.TargetAtr * atr5);
                    return Make(SetupKind.Continuation, Side.Short, names[i], L, stop, target);
                }
            }
            return null;
        }

        private static void AddLevel(List<string> names, List<double> levels, string name, double v)
        {
            if (!Valid(v)) return;
            names.Add(name);
            levels.Add(v);
        }

        private static double NextLevelAbove(List<double> levels, double from)
        {
            double best = double.NaN;
            foreach (var v in levels)
                if (v >= from && (double.IsNaN(best) || v < best)) best = v;
            return best;
        }

        private static double NextLevelBelow(List<double> levels, double from)
        {
            double best = double.NaN;
            foreach (var v in levels)
                if (v <= from && (double.IsNaN(best) || v > best)) best = v;
            return best;
        }

        // ------------------------------------------------------------ setup C : aimant de fin de seance
        private Signal TryPin(BarInput bar, double delta, double rank, double atr5, double tick)
        {
            if (!Valid(_major)) return null;
            double pin = _major;
            double dev = bar.Close - pin;
            double stopDist = Math.Max(S.MinStopTicks * tick, S.StopAtr * atr5);
            if (Math.Abs(dev) > S.PinMaxDistAtr * atr5 || Math.Abs(dev) < S.PinMinDevTicks * tick || rank < S.AbsorbPct) return null;
            // Au-dessus du strike : achats agressifs absorbes (cloture sous le plus haut) -> vente vers le strike
            if (dev > 0 && delta > 0 && bar.Close < bar.High)
                return Make(SetupKind.Pin, Side.Short, "strike aimant (Major Gamma)", pin, bar.High + stopDist, pin);
            if (dev < 0 && delta < 0 && bar.Close > bar.Low)
                return Make(SetupKind.Pin, Side.Long, "strike aimant (Major Gamma)", pin, bar.Low - stopDist, pin);
            return null;
        }

        // ------------------------------------------------------------ outils
        /// Stops et objectifs sur la grille des ticks : les niveaux DeepGamma (strike + base ES - SPX) et la VWAP n'y sont
        /// pas. Arrondi defavorable : stop un peu plus loin, objectif un peu plus loin aussi.
        private Signal Make(SetupKind kind, Side side, string levelName, double level, double stop, double target)
        {
            double tick = S.TickSize;
            return new Signal { Kind = kind, Side = side, LevelName = levelName, Level = level,
                                Stop = RoundStop(side, stop, tick), Target = RoundTarget(side, target, tick) };
        }

        public static double RoundStop(Side side, double price, double tick)
        {
            if (!(tick > 0) || double.IsNaN(price)) return price;
            double n = price / tick;
            return Math.Round((side == Side.Long ? Math.Floor(n + 1e-7) : Math.Ceiling(n - 1e-7)) * tick, 6);
        }

        public static double RoundTarget(Side side, double price, double tick)
        {
            if (!(tick > 0) || double.IsNaN(price)) return price;
            double n = price / tick;
            return Math.Round((side == Side.Long ? Math.Ceiling(n - 1e-7) : Math.Floor(n + 1e-7)) * tick, 6);
        }

        private static void Consider(ref Signal best, Signal candidate)
        {
            if (best == null && candidate != null) best = candidate;
        }

        public static string KindLetter(SetupKind k) =>
            k == SetupKind.WallReject ? "A" : k == SetupKind.Continuation ? "B" : k == SetupKind.Pin ? "C" : "?";
    }
}
