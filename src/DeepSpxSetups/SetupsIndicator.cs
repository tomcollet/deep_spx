// =====================================================================
//  SetupsIndicator.cs  -  indicateur DeepCharts : setups d'achat et de vente
//  regime gamma SPX (DeepGamma) x zones x order flow ES
//
//  - Repere les indicateurs DeepGamma du graphique par leur nom (GetParamsById) :
//    Option Envelopes (Zero, Major, Minor Gamma), Option Summary (Regime),
//    Option Net Flow (Difference), Option Market Maker Pressure.
//  - A la fin de l'historique, rejoue toutes les barres fermees dans le moteur
//    (Engine/), puis le nourrit a chaque barre fermee en temps reel : meme
//    code en historique, en replay et en live.
//  - Dessine les signaux (fleches, lettre du setup, resultat simule) et un
//    panneau d'etat ; ecrit DSPX;SIG / FILL / EXIT dans le log de Deepchart.
//  - Aucun ordre n'est envoye.
//
//  Regles : docs/SETUPS.md. Bac a sable de Deepchart : voir CLAUDE.md.
// =====================================================================

using System.Globalization;
using System.Text;
using DeepSpx.Engine;
using VolSysAPI;
using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
using Feed = VolumetricaAPI.Connection.Structure;
using VolumetricaControls;
using VolumetricaCore;
using static VolSysAPI.ExternalStructure;
using static VolSysAPI.Structure;

namespace DeepSpx.Setups
{
    public class SetupsIndicator : VolSysAPI.Indicators.Indicator
    {
        public const string Prefix = "DSPX;";
        public const string Version = "1.2";

        public static IndicatorDescriptionBase Register() =>
            new IndicatorDescriptionBase
            {
                Name = "DeepSpx - Setups gamma x order flow",
                Description = "Setups A (rejet de mur, long gamma), B (continuation, short gamma), C (aimant de fin de seance) a partir de DeepGamma et de l'order flow ES. Signaux seulement, aucun ordre."
            };

        // ------------------------------------------------------------ parametres (noms enregistres par le workspace : ne pas renommer)
        public bool EnableSetupA { get; set; } = true;
        public bool EnableSetupB { get; set; } = true;
        public bool EnableSetupC { get; set; } = true;
        public bool RequireNetFlow { get; set; } = true;
        public bool UseSummarySign { get; set; } = true;
        public bool BlockOnStaleGamma { get; set; } = true;
        public int MaxTradesPerDay { get; set; } = 3;
        public int MaxLossesPerDay { get; set; } = 2;
        public double SlippageTicks { get; set; } = 1;
        /// Ajoute OptionOffsetPrice de la barre aux niveaux DeepGamma (si les niveaux sont en prix SPX et non ES)
        public bool ApplyOptionOffset { get; set; } = false;
        /// Numeros forces des indicateurs DeepGamma (-1 : reperage automatique par le nom)
        public int EnvelopesId { get; set; } = -1;
        public int SummaryId { get; set; } = -1;
        public int NetFlowId { get; set; } = -1;
        public int MmPressureId { get; set; } = -1;
        public int MaxIndicatorScan { get; set; } = 40;
        /// Heure des barres : 0 = detection automatique (reouverture Globex 18:00 a New York, 17:00 a Chicago ; ou
        /// ouverture 09:30 / 08:30 sur un graphique en seance reguliere), 1 = New York, 2 = Chicago.
        public int BarTimeZone { get; set; } = 0;
        /// Statistiques simulees dans le panneau. Desactivees par defaut : la periode recente est scellee (holdout), on
        /// n'y regle aucun parametre.
        public bool ShowStats { get; set; } = false;
        public int PanelFontSize { get; set; } = 11;
        public double PanelX { get; set; } = 0.01;
        public double PanelY { get; set; } = 0.03;
        public double PanelLineStep { get; set; } = 0.028;

        // ------------------------------------------------------------ etat
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        private SetupEngine _engine;
        private bool _historyDone;
        private int _lastProcessed;
        private long _endRt;
        private double _tick;
        private readonly List<IAnnotation> _panel = new List<IAnnotation>();

        private int _envId, _envZero, _envMajor, _envMinor, _sumId, _sumSer, _nfId, _nfSer, _mmId, _mmSer;
        private string _mapText = "";
        private bool _chicago;
        private string _clockText = "";
        private int _rtCloses;

        private const int PanelLines = 18;

        // ------------------------------------------------------------ cycle de vie
        public override void OnSet(bool setDefault, bool themeOverride)
        {
            OnCloseCall = CallHandler.RT;     // l'historique est rejoue d'un bloc a la fin du chargement
            OnEndCall = CallHandler.HistRT;
            Description = "Setups A/B/C";
        }

        public override void OnLoad()
        {
            _historyDone = false;
            _lastProcessed = -1;
            _endRt = 0;
            _tick = 0.25;
            _envId = _sumId = _nfId = _mmId = -1;
            _mapText = "";
            _chicago = false;
            _clockText = "";
            _rtCloses = 0;
            _engine = new SetupEngine(BuildSettings());
            CreatePanel();
            StatusMessage = "DeepSpx Setups v" + Version + " : signaux seulement, aucun ordre";
        }

        public override void OnEnd(bool isRt)
        {
            try
            {
                if (!_historyDone)
                {
                    _historyDone = true;
                    _tick = DetectTick();
                    DetectClock();
                    _engine = new SetupEngine(BuildSettings());
                    MapGamma();
                    int last = VAn.BarIndex - 1;              // la derniere barre est encore ouverte
                    for (int b = 0; b <= last; b++) Process(b);
                    Log("HIST", new[] { "bars=" + (last + 1), "signals=" + _engine.Signals.Count, "map=" + _mapText,
                        "clock=" + (_chicago ? "Chicago" : "NewYork"), "session=" + _engine.Settings.SessionText() });
                    UpdatePanel();
                    return;
                }
                if (isRt)
                {
                    _endRt++;
                    if (_endRt % 25 == 1) UpdatePanel();
                }
            }
            catch (Exception ex) { Log("ERR", new[] { "OnEnd", ex.Message ?? "" }); }
        }

        public override void OnClose(bool isRt)
        {
            if (!isRt || !_historyDone) return;
            try
            {
                int b = VAn.BarIndex;
                if (b > _lastProcessed)
                {
                    // un indicateur DeepGamma ajoute apres celui-ci est repere dans les 15 minutes
                    _rtCloses++;
                    if ((_envId < 0 || _sumId < 0 || _nfId < 0) && _rtCloses % 15 == 1) MapGamma();
                    Process(b);
                    UpdatePanel();
                }
            }
            catch (Exception ex) { Log("ERR", new[] { "OnClose", ex.Message ?? "" }); }
        }

        // ------------------------------------------------------------ moteur
        private EngineSettings BuildSettings()
        {
            var s = new EngineSettings
            {
                TickSize = _tick,
                EnableWallReject = EnableSetupA,
                EnableContinuation = EnableSetupB,
                EnablePin = EnableSetupC,
                RequireNetFlow = RequireNetFlow,
                UseSummarySign = UseSummarySign,
                BlockOnStaleGamma = BlockOnStaleGamma,
                MaxTradesPerDay = MaxTradesPerDay,
                MaxLossesPerDay = MaxLossesPerDay,
                SlippageTicks = SlippageTicks
            };
            if (_chicago) s.ShiftClock(-60);
            return s;
        }

        /// Les heures de seance du moteur suivent l'heure des barres. Repere : l'heure de la premiere barre apres une
        /// interruption d'au moins 30 minutes (reouverture Globex, ou ouverture sur un graphique en seance reguliere).
        private void DetectClock()
        {
            int ny = 0, chi = 0;
            try
            {
                var bars = VAn.BarVars;
                int last = Math.Min(VAn.BarIndex, bars.Count - 1);
                for (int b = 1; b <= last; b++)
                {
                    if ((bars[b].exchDt - bars[b - 1].exchDt).TotalMinutes < 30) continue;
                    int tod = bars[b].exchDt.Hour * 60 + bars[b].exchDt.Minute;
                    if (Near(tod, 18 * 60) || Near(tod, 9 * 60 + 30)) ny++;
                    else if (Near(tod, 17 * 60) || Near(tod, 8 * 60 + 30)) chi++;
                }
            }
            catch { }
            bool detectedChicago = chi > ny;
            string how;
            if (BarTimeZone == 1) { _chicago = false; how = "parametre"; }
            else if (BarTimeZone == 2) { _chicago = true; how = "parametre"; }
            else { _chicago = detectedChicago; how = ny + chi > 0 ? "detecte" : "par defaut, non detecte"; }
            _clockText = (_chicago ? "Chicago" : "New York") + " (" + how + ")";
            if (BarTimeZone != 0 && ny + chi > 0 && detectedChicago != _chicago)
                _clockText += " ATTENTION : les barres semblent a l'heure de " + (detectedChicago ? "Chicago" : "New York");
            Log("CLOCK", new[] { _chicago ? "Chicago" : "NewYork", how, "reopen_ny=" + ny, "reopen_chicago=" + chi });
        }

        private static bool Near(int tod, int target) => tod >= target && tod <= target + 5;

        private double DetectTick()
        {
            try
            {
                var bars = VAn.BarVars;
                for (int i = bars.Count - 1; i >= 0 && i >= bars.Count - 50; i--)
                    if (bars[i].CloseInT > 0 && bars[i].Close > 0)
                    {
                        double t = bars[i].Close / bars[i].CloseInT;
                        // arrondi au 1/10 000 pour eviter les residus de calcul
                        return Math.Round(t * 10000) / 10000;
                    }
            }
            catch { }
            return 0.25;
        }

        private void Process(int b)
        {
            var bar = VAn.BarVars[b];
            var input = new BarInput
            {
                Index = b,
                Time = bar.exchDt,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close
            };
            var tot = bar.VolTotList != null && bar.VolTotList.Length > 0 ? bar.VolTotList[0] : null;
            if (tot != null)
            {
                input.Volume = tot.TotVol;
                input.AskVolume = tot.AskVol;
                input.BidVolume = tot.BidVol;
                input.Vwap = tot.VwapPrice;
            }
            ReadGamma(b, bar, input.Gamma);
            _engine.OnBar(input);
            _lastProcessed = b;

            foreach (var s in _engine.NewExits) OnExit(s);
            foreach (var s in _engine.NewFills) Log("FILL", new[] { s.Id.ToString(IC), Fmt(s.EntryTime), Fmt(s.EntryPrice) });
            foreach (var s in _engine.NewSignals) OnSignal(s, bar);
        }

        // ------------------------------------------------------------ DeepGamma
        private void MapGamma()
        {
            _envId = _sumId = _nfId = _mmId = -1;
            _envZero = 0; _envMajor = 1; _envMinor = 2; _sumSer = 0; _nfSer = 2; _mmSer = 0;
            for (int ind = 0; ind < MaxIndicatorScan; ind++)
            {
                BaseIndicatorPrms p = null;
                try { p = VAn.GetParamsById(ind); } catch { }
                if (p == null || p.name == null) continue;
                string n = p.name.ToLowerInvariant();
                if (_envId < 0 && n.Contains("envelope"))
                {
                    _envId = ind;
                    _envZero = SeriesIndex(p, "zero", 0);
                    _envMajor = SeriesIndex(p, "major", 1);
                    _envMinor = SeriesIndex(p, "minor", 2);
                }
                else if (_sumId < 0 && n.Contains("summary")) { _sumId = ind; _sumSer = SeriesIndex(p, "regime", 0); }
                else if (_nfId < 0 && n.Contains("net flow")) { _nfId = ind; _nfSer = SeriesIndex(p, "diff", 2); }
                else if (_mmId < 0 && n.Contains("pressure")) { _mmId = ind; _mmSer = SeriesIndex(p, "mm", 0); }
            }
            if (EnvelopesId >= 0) _envId = EnvelopesId;
            if (SummaryId >= 0) _sumId = SummaryId;
            if (NetFlowId >= 0) _nfId = NetFlowId;
            if (MmPressureId >= 0) _mmId = MmPressureId;
            string text = "envelopes=" + _envId + "(" + _envZero + "/" + _envMajor + "/" + _envMinor + ") summary=" + _sumId + "(" + _sumSer +
                          ") netflow=" + _nfId + "(" + _nfSer + ") pressure=" + _mmId + "(" + _mmSer + ")";
            if (text == _mapText) return;
            _mapText = text;
            Log("MAP", new[] { _mapText });
        }

        private static int SeriesIndex(BaseIndicatorPrms p, string key, int fallback)
        {
            if (p.v_series == null) return fallback;
            for (int i = 0; i < p.v_series.Count; i++)
            {
                var sp = p.v_series[i];
                if (sp != null && sp.Name != null && sp.Name.ToLowerInvariant().Contains(key)) return i;
            }
            return fallback;
        }

        private void ReadGamma(int b, BarClass bar, GammaInput g)
        {
            double offset = ApplyOptionOffset && bar.OptionOffsetPrice.HasValue ? bar.OptionOffsetPrice.Value : 0;
            g.ZeroGamma = Shift(Read(_envId, _envZero, b), offset);
            g.MajorGamma = Shift(Read(_envId, _envMajor, b), offset);
            g.MinorGamma = Shift(Read(_envId, _envMinor, b), offset);
            g.SummaryRegime = Read(_sumId, _sumSer, b);
            g.NetFlow = Read(_nfId, _nfSer, b);
            g.MmPressure = Read(_mmId, _mmSer, b);
        }

        private double Read(int ind, int ser, int b)
        {
            if (ind < 0) return double.NaN;
            try
            {
                double[] v = VAn.GetSeriesValueById(ind, ser, b);
                return v != null && v.Length > 0 ? v[0] : double.NaN;
            }
            catch { return double.NaN; }
        }

        private static double Shift(double v, double offset) =>
            double.IsNaN(v) || v == 0.0 ? v : v + offset;

        // ------------------------------------------------------------ dessin et journal
        private void OnSignal(Signal s, BarClass bar)
        {
            bool isLong = s.Side == Side.Long;
            string letter = SetupEngine.KindLetter(s.Kind);
            double y = isLong ? bar.Low - 3 * _tick : bar.High + 3 * _tick;
            var color = IMethodAPI.GetColorRefForTheme(isLong ? ColorReferenceEnum.Up : ColorReferenceEnum.Down, ColorTypeEnum.Fill);
            try
            {
                var arrow = VAn.CreateAnnotation(isLong ? AnnotationType.ArrowUp : AnnotationType.ArrowDown);
                arrow.CoordinateXType = CoordinateTypeEnum.Absolute;
                arrow.CoordinateYType = CoordinateTypeEnum.Absolute;
                arrow.X = s.BarIndex + 1;
                arrow.Y = y;
                arrow.BackColor = color;
                arrow.LineColor = color;
                arrow.Tooltip = Describe(s);
                VAn.AddAnnotation(IndVars.Ann_List, arrow);

                var label = VAn.CreateAnnotation(AnnotationType.Text);
                label.CoordinateXType = CoordinateTypeEnum.Absolute;
                label.CoordinateYType = CoordinateTypeEnum.Absolute;
                label.X = s.BarIndex + 1;
                label.Y = isLong ? y - 4 * _tick : y + 4 * _tick;
                label.Text = letter + (isLong ? " achat" : " vente");
                label.ForeColor = color;
                label.FontSize = 10;
                label.FontBold = true;
                label.Tooltip = Describe(s);
                VAn.AddAnnotation(IndVars.Ann_List, label);
            }
            catch { }

            Log("SIG", new[] { s.Id.ToString(IC), Fmt(s.Time), letter, isLong ? "LONG" : "SHORT", s.Regime.ToString(), s.LevelName,
                Fmt(s.Level), Fmt(s.SignalClose), Fmt(s.Stop), Fmt(s.Target), Fmt(s.Delta), Fmt(s.DeltaRank), Fmt(s.Atr5),
                Fmt(s.ZeroGamma), Fmt(s.DistZeroTicks), Fmt(s.NetFlowSlope), Fmt(s.Summary), Fmt(s.Vwap) });
        }

        private void OnExit(Signal s)
        {
            Log("EXIT", new[] { s.Id.ToString(IC), Fmt(s.ExitTime), s.Exit.ToString(), Fmt(s.ExitPrice), Fmt(s.ResultTicks) });
            if (s.Exit == ExitReason.Cancelled || s.ExitBar < 0) return;
            try
            {
                var bar = VAn.BarVars[s.ExitBar];
                var t = VAn.CreateAnnotation(AnnotationType.Text);
                t.CoordinateXType = CoordinateTypeEnum.Absolute;
                t.CoordinateYType = CoordinateTypeEnum.Absolute;
                t.X = s.ExitBar + 1;
                t.Y = s.Side == Side.Long ? bar.High + 3 * _tick : bar.Low - 3 * _tick;
                t.Text = (s.ResultTicks >= 0 ? "+" : "") + s.ResultTicks.ToString("0.#", IC) + " t";
                t.ForeColor = IMethodAPI.GetColorRefForTheme(s.ResultTicks >= 0 ? ColorReferenceEnum.Up : ColorReferenceEnum.Down, ColorTypeEnum.Text);
                t.FontSize = 9;
                t.Tooltip = SetupEngine.KindLetter(s.Kind) + " #" + s.Id + " : " + s.Exit;
                VAn.AddAnnotation(IndVars.Ann_List, t);
            }
            catch { }
        }

        private string Describe(Signal s)
        {
            return SetupEngine.KindLetter(s.Kind) + " #" + s.Id + " " + (s.Side == Side.Long ? "achat" : "vente") +
                   " | " + s.LevelName + " " + Fmt(s.Level) + " | stop " + Fmt(s.Stop) + " | objectif " + Fmt(s.Target) +
                   " | regime " + s.Regime;
        }

        // ------------------------------------------------------------ panneau
        private void CreatePanel()
        {
            _panel.Clear();
            try
            {
                for (int i = 0; i < PanelLines; i++)
                {
                    var a = VAn.CreateAnnotation(AnnotationType.Text);
                    a.CoordinateXType = CoordinateTypeEnum.Relative;
                    a.CoordinateYType = CoordinateTypeEnum.Relative;
                    a.X = PanelX;
                    a.Y = PanelY + i * PanelLineStep;
                    a.FontSize = PanelFontSize;
                    a.FontBold = i == 0;
                    a.Text = i == 0 ? "DeepSpx Setups v" + Version + " : chargement..." : "";
                    VAn.AddAnnotation(IndVars.FrontAnnList, a);
                    _panel.Add(a);
                }
            }
            catch { }
        }

        private void UpdatePanel()
        {
            if (_panel.Count == 0 || _engine == null) return;
            try
            {
                var e = _engine;
                var lines = new List<string>();
                lines.Add("DeepSpx Setups v" + Version + "  |  " + RegimeText(e.Regime) + "  |  " + e.RegimeReason);
                lines.Add("Zero Gamma " + Fmt(e.ZeroGamma) + "  |  Major " + Fmt(e.MajorGamma) + "  |  Minor " + Fmt(e.MinorGamma) +
                          "  |  Summary " + Short(e.Summary) + (e.SummaryStale && !double.IsNaN(e.Summary) ? " (fige, ignore)" : "") +
                          "  |  pente Net Flow " + Short(e.NetFlowSlope));
                lines.Add("DeepGamma : derniere valeur il y a " + (e.GammaAgeBars == int.MaxValue ? "-" : e.GammaAgeBars.ToString(IC)) +
                          " barres" + (e.GammaStale ? "  |  FIGE" : "") + "  |  " + _mapText);
                lines.Add("Heure des barres : " + _clockText + "  |  seance " + e.Settings.SessionText());
                string missing = MissingText();
                if (missing.Length > 0) lines.Add("ATTENTION : " + missing);
                lines.Add("VWAP " + Fmt(e.Vwap) + "  |  ATR5 " + e.Atr5.ToString("0.00", IC) + "  |  range d'ouverture " + Fmt(e.OpeningLow) +
                          " - " + Fmt(e.OpeningHigh) + "  |  veille " + Fmt(e.PrevLow) + " - " + Fmt(e.PrevHigh));
                string state = e.Position != null ? "EN POSITION : " + Describe(e.Position) + " | entree " + Fmt(e.Position.EntryPrice)
                    : e.Pending != null ? "SIGNAL, entree a l'ouverture de la barre suivante : " + Describe(e.Pending)
                    : "Attente" + (e.Blocked.Length > 0 ? " (" + e.Blocked + ")" : " d'un setup");
                lines.Add(state);
                lines.Add("Aujourd'hui : " + e.TradesToday + " signal(s), " + e.LossesToday + " perte(s)  |  limites " + MaxTradesPerDay + " / " + MaxLossesPerDay);
                if (ShowStats)
                {
                    lines.Add("Simulation (indicative, ne pas regler dessus) : " + StatText("tous", e.StatsAll));
                    lines.Add("  " + StatText("A", e.StatsA) + "  |  " + StatText("B", e.StatsB) + "  |  " + StatText("C", e.StatsC));
                }
                lines.Add("Derniers signaux :");
                int shown = 0;
                for (int i = e.Signals.Count - 1; i >= 0 && shown < 5; i--, shown++)
                {
                    var s = e.Signals[i];
                    string res = s.Closed ? (s.Exit == ExitReason.Cancelled ? "annule" : s.Exit + " " + s.ResultTicks.ToString("+0.#;-0.#;0", IC) + " t") : s.Filled ? "en cours" : "en attente";
                    lines.Add("  " + Fmt(s.Time) + "  " + Describe(s) + "  ->  " + res);
                }
                for (int i = 0; i < _panel.Count; i++) _panel[i].Text = i < lines.Count ? lines[i] : "";
            }
            catch { }
        }

        /// Indicateurs DeepGamma absents du graphique, et ce que cela coupe.
        private string MissingText()
        {
            var parts = new List<string>();
            if (_envId < 0) parts.Add("Option Envelopes absent : aucun regime, aucun signal");
            if (_nfId < 0 && RequireNetFlow && EnableSetupB) parts.Add("Option Net Flow absent : setup B impossible");
            if (_sumId < 0 && UseSummarySign) parts.Add("Option Summary absent : regime lu sur le Zero Gamma seul");
            return string.Join("  |  ", parts);
        }

        private static string RegimeText(Regime r) =>
            r == Regime.LongGamma ? "LONG GAMMA (rejets, aimant)" : r == Regime.ShortGamma ? "SHORT GAMMA (continuation)" :
            r == Regime.Transition ? "TRANSITION (pas de trade)" : "REGIME INCONNU";

        private static string StatText(string name, SetupStats st) =>
            name + " " + st.Trades + " trades, " + st.WinRate.ToString("0", IC) + " % gagnants, " + st.AvgTicks.ToString("+0.0;-0.0;0", IC) + " t/trade";

        // ------------------------------------------------------------ utilitaires
        private void Log(string kind, string[] fields)
        {
            var sb = new StringBuilder(Prefix);
            sb.Append(kind);
            foreach (var f in fields) sb.Append(';').Append((f ?? "").Replace(";", ","));
            try { VAn.AddToLog(sb.ToString(), VolumetricaAPI.LogLevelEnum.Information); } catch { }
        }

        private static string Fmt(double v) => double.IsNaN(v) ? "-" : v.ToString("0.##", IC);

        private static string Fmt(DateTime t) => t.ToString("yyyy-MM-dd HH:mm", IC);

        private static string Short(double v)
        {
            if (double.IsNaN(v)) return "-";
            double a = Math.Abs(v);
            if (a >= 1e9) return (v / 1e9).ToString("0.00", IC) + " Md";
            if (a >= 1e6) return (v / 1e6).ToString("0.00", IC) + " M";
            if (a >= 1e3) return (v / 1e3).ToString("0.0", IC) + " k";
            return v.ToString("0.##", IC);
        }
    }
}
