// =====================================================================
//  DeepGammaProbe.cs  -  sonde DeepCharts (lecture seule), version 2
//
//  Questions :
//    Q1  La DLL lit-elle les series des autres indicateurs du graphique
//        (DeepGamma) avec GetSeriesValueById ?
//    Q2  Ces series ont-elles un historique (valeurs variables sur les
//        barres passees) ?
//    Q3  Le carnet arrive-t-il (OnBook, RebuildDom), et sur combien de
//        niveaux ?
//    Q4  Les trades historiques arrivent-ils (OnTickCall = HistRT) ?
//    Q5  Le log de Deepchart sert-il de canal de sortie ? (non : chiffre sur disque)
//    Q6  Le graphique est-il un replay (VAn.IsReplayData) ?
//    Q7  En live, les series DeepGamma valent-elles 0 (issue #4 du depot
//        officiel) ?
//    Q8  Le carnet arrive-t-il aussi ordre par ordre (MBO : MboUpdates,
//        NumOffers, AggrID/RestID des trades) ?
//
//  Signatures relevees dans deepchart-api.txt (tools/ApiDump, VolSysAPI
//  1.4.6.0) : GetSeriesValueById(int, int, int) -> double[] ;
//  GetParamsById(int) -> BaseIndicatorPrms (name, v_series).
//
//  Deepchart refuse de charger une DLL qui utilise reflexion, fichiers,
//  reseau, threads, lock, Environment... (docs/troubleshooting). La sonde
//  n'utilise donc que l'API d'indicateurs, les types de base, les
//  collections generiques, LINQ, StringBuilder et CultureInfo, et ecrit
//  tout dans le log de Deepchart, une ligne par enregistrement, prefixe
//  "DSPX;". Verification : tools/DllCheck.
//
//  v4 : le fichier de log de Deepchart est chiffre sur disque. Les verdicts
//  et les series lisibles s'affichent donc aussi dans un panneau de texte
//  sur le graphique (annotations), lisible a l'ecran ou en capture.
//
//  Compilation : dotnet build src/DeepGammaProbe -p:DeepchartDevId=TON-ID
//  Protocole    : src/DeepGammaProbe/LISEZMOI.md
// =====================================================================

using System.Globalization;
using System.Text;
using VolSysAPI;
using VolSysAPI.Indicators;
using VolumetricaAPI.Chart;
// Trades et carnet : VolumetricaAPI.Connection.Structure (meme alias que les exemples officiels)
using Feed = VolumetricaAPI.Connection.Structure;
using VolumetricaControls;
using VolumetricaCore;
using static VolSysAPI.ExternalStructure;
using static VolSysAPI.Structure;

namespace DeepSpx.Probe
{
    public class DeepGammaProbe : VolSysAPI.Indicators.Indicator
    {
        public const string Prefix = "DSPX;";
        public const string Version = "4";

        public static IndicatorDescriptionBase Register() =>
            new IndicatorDescriptionBase
            {
                Name = "Probe - DeepGamma Recorder Test",
                Description = "Sonde en lecture seule : series des autres indicateurs, carnet, trades. Sorties dans le log de Deepchart (DSPX;)."
            };

        // ------------------------------------------------------------ parametres
        /// Premier identifiant d'indicateur teste.
        public int IndicatorIdStart { get; set; } = 0;
        /// Nombre d'identifiants testes a partir de IndicatorIdStart.
        public int MaxIndicators { get; set; } = 30;
        /// Series testees par indicateur.
        public int MaxSeries { get; set; } = 12;
        /// Barres passees relues a la fin de l'historique.
        public int HistoryBarsToScan { get; set; } = 600;
        /// Minutes de temps reel pendant lesquelles chaque serie lisible est ecrite.
        public int RtSeriesMinutes { get; set; } = 60;
        /// Minutes de temps reel pendant lesquelles les statistiques du carnet et des trades sont ecrites.
        public int RtStatsMinutes { get; set; } = 60;
        /// Panneau de texte sur le graphique : position (0 a 1), taille de police, nombre de series affichees.
        public double PanelX { get; set; } = 0.01;
        public double PanelY { get; set; } = 0.97;
        public double PanelLineStep { get; set; } = 0.028;
        public int PanelFontSize { get; set; } = 11;
        public int PanelMaxSeries { get; set; } = 14;

        // ------------------------------------------------------------ etat
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        private sealed class SeriesStat
        {
            public int Ind, Ser;
            public int Coverage;                       // barres relues avec une valeur non nulle
            public readonly HashSet<string> Distinct = new HashSet<string>();
            public double Min = double.MaxValue, Max = double.MinValue;
            public string First = "", Last = "";
            public int RtReads, RtZeros;               // Q7
            public int Points;                         // valeurs par point (longueur du tableau renvoye)
            public string LastRt = "";
        }

        private readonly List<SeriesStat> _readable = new List<SeriesStat>();
        private readonly Dictionary<int, string> _indNames = new Dictionary<int, string>();
        private readonly Dictionary<int, List<string>> _seriesNames = new Dictionary<int, List<string>>();
        private readonly List<IAnnotation> _panel = new List<IAnnotation>();
        private readonly List<string> _verdicts = new List<string>();
        private long _panelUpdates;
        private readonly Dictionary<string, int> _errorCount = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _errorFirst = new Dictionary<string, string>();
        private readonly SortedDictionary<int, int> _bids = new SortedDictionary<int, int>();
        private readonly SortedDictionary<int, int> _asks = new SortedDictionary<int, int>();

        private bool _historyDone, _bookSnapshotDone, _firstHistTickLogged, _firstRtTickLogged;
        private int _historyLastIndex, _histBarsScanned, _bookExamples, _snapshotTries, _rtDiscoveryTries, _rtMinutes;
        private long _nOpenHist, _nOpenRt, _nCloseHist, _nCloseRt, _nEndHist, _nEndRt;
        private long _nTickHist, _nTickRt, _nBook, _nBookResets;
        private long _nMbo, _nMboResets, _nLevelsWithOffers, _nTicksWithIds, _nIcebergTicks, _nSweepTicks;
        private int _maxBidLevels, _maxAskLevels, _maxDepthTicks;
        private DateTime _lastSummaryExch;

        // Statistiques de la minute en cours (temps reel)
        private long _mBookUpdates, _mBookResets, _mTicks, _mVol, _mAskVol, _mBidVol, _mMbo, _mTicksWithIds;

        // ------------------------------------------------------------ cycle de vie
        public override void OnSet(bool setDefault, bool themeOverride)
        {
            OnOpenCall = CallHandler.HistRT;
            OnCloseCall = CallHandler.HistRT;
            OnEndCall = CallHandler.HistRT;
            OnTickCall = CallHandler.HistRT;   // Q4 (desktop uniquement)
            OnBookCall = CallHandler.RT;       // Q3 (desktop uniquement)
            Description = "Probe DeepGamma v" + Version;
        }

        public override void OnLoad()
        {
            _readable.Clear(); _errorCount.Clear(); _errorFirst.Clear(); _bids.Clear(); _asks.Clear();
            _indNames.Clear(); _seriesNames.Clear(); _verdicts.Clear(); _panelUpdates = 0;
            CreatePanel();
            _historyDone = _bookSnapshotDone = _firstHistTickLogged = _firstRtTickLogged = false;
            _historyLastIndex = -1;
            _histBarsScanned = _bookExamples = _snapshotTries = _rtDiscoveryTries = _rtMinutes = 0;
            _nOpenHist = _nOpenRt = _nCloseHist = _nCloseRt = _nEndHist = _nEndRt = 0;
            _nTickHist = _nTickRt = _nBook = _nBookResets = 0;
            _nMbo = _nMboResets = _nLevelsWithOffers = _nTicksWithIds = _nIcebergTicks = _nSweepTicks = 0;
            _maxBidLevels = _maxAskLevels = _maxDepthTicks = 0;
            _lastSummaryExch = DateTime.MinValue;
            ResetMinute();

            StatusMessage = "Probe DeepGamma v" + Version + " : sorties dans le log de Deepchart, lignes " + Prefix;
            Log("META", new[] { "version=" + Version,
                "symbol=" + Safe(() => VAn.SymbolName),
                "chart=" + Safe(() => VAn.ChartName),
                "replay=" + Safe(() => Convert.ToString(VAn.IsReplayData, IC)),
                "phase=" + Safe(() => Convert.ToString(VAn.ChartPhase, IC)),
                "self_id=" + Safe(() => getId().ToString(IC)),
                "params=start:" + IndicatorIdStart + "/n:" + MaxIndicators + "/series:" + MaxSeries });
            Log("HEAD", new[] { "IND;indicator;name;hidden;series_count;series_names" });
            Log("HEAD", new[] { "CAT;indicator;series;value_now;value_minus_10;value_minus_60;value_minus_390;points;error" });
            Log("HEAD", new[] { "HSER;indicator;series;bars_with_value;bars_scanned;distinct;min;max;first;last" });
            Log("HEAD", new[] { "SER;mode;bar_index;bar_exch_time;indicator;series;value" });
            Log("HEAD", new[] { "BOOKMIN;exch_time;updates;resets;bid_levels;ask_levels;best_bid;best_ask;bid_depth_ticks;ask_depth_ticks;bid_qty_10;ask_qty_10;mbo_updates" });
            Log("HEAD", new[] { "TICKMIN;exch_time;trades;volume;ask_volume;bid_volume;trades_with_order_ids" });
        }

        public override void OnOpen(bool isRt)
        {
            try
            {
                if (!isRt) { _nOpenHist++; return; }
                _nOpenRt++;
                if (!_historyDone) return;

                // Rien de lisible a la fin de l'historique : on retente en temps reel
                if (_readable.Count == 0 && _rtDiscoveryTries < 10 && _nOpenRt % 3 == 1)
                {
                    _rtDiscoveryTries++;
                    Discover("temps reel, essai " + _rtDiscoveryTries);
                }

                int b = VAn.BarIndex - 1;   // la barre qui vient de se fermer
                if (b >= 0)
                {
                    _rtMinutes++;
                    if (_rtMinutes <= RtSeriesMinutes) RecordSeries("rt", b);
                    if (_rtMinutes <= RtStatsMinutes) WriteMinuteStats(b);
                }
                ResetMinute();
            }
            catch (Exception ex) { Err("OnOpen", ex); }
        }

        public override void OnClose(bool isRt)
        {
            if (isRt) _nCloseRt++; else _nCloseHist++;
        }

        public override void OnTick(Feed.TickByTick tick, bool isRt, AggrInfo aggrInfo)
        {
            try
            {
                if (tick == null) return;
                if (isRt)
                {
                    _nTickRt++;
                    _mTicks++;
                    _mVol += tick.Vol;
                    if (tick.AggrSide == Feed.AggressorSideEnum.Ask) _mAskVol += tick.Vol;
                    else if (tick.AggrSide == Feed.AggressorSideEnum.Bid) _mBidVol += tick.Vol;
                    if (tick.AggrID != 0 || tick.RestID != 0) { _nTicksWithIds++; _mTicksWithIds++; }
                    if ((tick.TradeType & Feed.TradeTypeEnum.Iceberg) != 0) _nIcebergTicks++;
                    if ((tick.TradeType & Feed.TradeTypeEnum.SweepOrder) != 0) _nSweepTicks++;
                    if (!_firstRtTickLogged) { _firstRtTickLogged = true; LogTick("TICK1RT", tick, aggrInfo); }
                }
                else
                {
                    _nTickHist++;
                    if (!_firstHistTickLogged) { _firstHistTickLogged = true; LogTick("TICK1HIST", tick, aggrInfo); }
                }
            }
            catch (Exception ex) { Err("OnTick", ex); }
        }

        public override void OnBook(Feed.BookUpdate updates)
        {
            try
            {
                _nBook++;
                _mBookUpdates++;
                if (_bookExamples < 3)
                {
                    _bookExamples++;
                    Log("BOOKEX", new[] { "n=" + _bookExamples, Describe(updates) });
                }
                ApplyBook(updates, false);
            }
            catch (Exception ex) { Err("OnBook", ex); }
        }

        public override void OnEnd(bool isRt)
        {
            try
            {
                if (isRt) _nEndRt++; else _nEndHist++;

                if (!_historyDone)
                {
                    // Premier OnEnd : fin du calcul de l'historique
                    _historyDone = true;
                    _historyLastIndex = VAn.BarIndex;
                    Log("HIST", new[] { "bars=" + (_historyLastIndex + 1), "hist_trades=" + _nTickHist,
                        "first_bar=" + BarTime(0), "last_bar=" + BarTime(_historyLastIndex), "ended_in_rt=" + isRt });
                    Discover("fin de l'historique");
                    ScanHistory();
                    WriteSummary("fin de l'historique");
                }

                if (isRt && !_bookSnapshotDone && _snapshotTries < 5 && _nEndRt % 50 == 1)
                {
                    _snapshotTries++;
                    var snap = VAn.RebuildDom();
                    Log("BOOKSNAP", new[] { "try=" + _snapshotTries, Describe(snap) });
                    ApplyBook(snap, true);
                    if (_bids.Count + _asks.Count > 0) _bookSnapshotDone = true;
                }

                if (isRt && _nEndRt % 20 == 1) UpdatePanel();

                if (isRt)
                {
                    DateTime now = VAn.LastExDt;
                    if (_lastSummaryExch == DateTime.MinValue || (now - _lastSummaryExch).TotalSeconds >= 300)
                    {
                        _lastSummaryExch = now;
                        WriteSummary("temps reel " + Fmt(now));
                    }
                }
            }
            catch (Exception ex) { Err("OnEnd", ex); }
        }

        // ------------------------------------------------------------ Q1 : decouverte
        private void Discover(string when)
        {
            _readable.Clear();
            int last = VAn.BarIndex;
            int[] offsets = { 0, 10, 60, 390 };
            Log("DISCOVER", new[] { "when=" + when, "bar=" + last });
            for (int ind = IndicatorIdStart; ind < IndicatorIdStart + MaxIndicators; ind++)
                LogIndicator(ind);
            for (int ind = IndicatorIdStart; ind < IndicatorIdStart + MaxIndicators; ind++)
            {
                for (int ser = 0; ser < MaxSeries; ser++)
                {
                    var cells = new string[offsets.Length];
                    string firstError = "";
                    bool hasValue = false;
                    int points = 0;
                    for (int k = 0; k < offsets.Length; k++)
                    {
                        int b = last - offsets[k];
                        if (b < 0) { cells[k] = ""; continue; }
                        double v;
                        int n;
                        string error;
                        if (TryRead(ind, ser, b, out v, out n, out error))
                        {
                            cells[k] = Fmt(v);
                            if (IsValue(v)) hasValue = true;
                            if (n > points) points = n;
                        }
                        else
                        {
                            cells[k] = "ERR";
                            if (firstError.Length == 0) firstError = error;
                        }
                    }
                    if (hasValue) _readable.Add(new SeriesStat { Ind = ind, Ser = ser, Points = points });
                    // Lignes utiles seulement : une valeur trouvee, ou la serie 0 de chaque indicateur
                    if (hasValue || ser == 0)
                        Log("CAT", new[] { ind.ToString(IC), ser.ToString(IC), cells[0], cells[1], cells[2], cells[3], points.ToString(IC), firstError });
                }
            }
            Log("DISCOVERED", new[] { "readable=" + _readable.Count, "when=" + when });
        }

        /// Nom de l'indicateur et de ses series : GetParamsById(int) -> BaseIndicatorPrms
        private void LogIndicator(int ind)
        {
            try
            {
                var p = VAn.GetParamsById(ind);
                if (p == null) return;
                var names = new List<string>();
                if (p.v_series != null)
                    foreach (var sp in p.v_series) names.Add(sp == null ? "" : sp.Name ?? "");
                Log("IND", new[] { ind.ToString(IC), p.name ?? "", p.Hidden.ToString(), names.Count.ToString(IC), string.Join("|", names) });
                _indNames[ind] = p.name ?? "";
                _seriesNames[ind] = names;
            }
            catch (Exception ex)
            {
                if (ind == IndicatorIdStart) Log("IND", new[] { ind.ToString(IC), "ERR " + (ex.Message ?? "") });
            }
        }

        /// double[] GetSeriesValueById(int IndicatorIndex, int SubgraphIndex, int Index) (deepchart-api.txt).
        /// Un point peut porter plusieurs valeurs : on garde la premiere et on note leur nombre.
        private bool TryRead(int ind, int ser, int bar, out double value, out int points, out string error)
        {
            value = double.NaN;
            points = 0;
            error = "";
            try
            {
                double[] raw = VAn.GetSeriesValueById(ind, ser, bar);
                if (raw == null) { error = "null"; return false; }
                points = raw.Length;
                if (raw.Length == 0) { error = "vide"; return false; }
                value = raw[0];
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message ?? "exception";
                return false;
            }
        }

        private static bool IsValue(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v != 0.0;

        // ------------------------------------------------------------ Q2 : historique
        private void ScanHistory()
        {
            int last = VAn.BarIndex;
            int from = Math.Max(0, last - HistoryBarsToScan);
            _histBarsScanned = last - from + 1;
            foreach (var s in _readable)
            {
                for (int b = from; b <= last; b++)
                {
                    double v; int n; string error;
                    if (!TryRead(s.Ind, s.Ser, b, out v, out n, out error) || !IsValue(v)) continue;
                    s.Coverage++;
                    string text = Fmt(v);
                    if (s.Distinct.Count < 500) s.Distinct.Add(text);
                    if (v < s.Min) s.Min = v;
                    if (v > s.Max) s.Max = v;
                    if (s.First.Length == 0) s.First = BarTime(b) + "=" + text;
                    s.Last = BarTime(b) + "=" + text;
                }
                Log("HSER", new[] { s.Ind.ToString(IC), s.Ser.ToString(IC), s.Coverage.ToString(IC), _histBarsScanned.ToString(IC),
                    s.Distinct.Count.ToString(IC), s.Coverage > 0 ? Fmt(s.Min) : "", s.Coverage > 0 ? Fmt(s.Max) : "", s.First, s.Last });
            }
        }

        private void RecordSeries(string mode, int b)
        {
            string bt = BarTime(b);
            foreach (var s in _readable)
            {
                double v; int n; string error;
                bool ok = TryRead(s.Ind, s.Ser, b, out v, out n, out error);
                if (mode == "rt" && ok) { s.RtReads++; if (v == 0.0) s.RtZeros++; s.LastRt = Fmt(v); }
                Log("SER", new[] { mode, b.ToString(IC), bt, s.Ind.ToString(IC), s.Ser.ToString(IC), ok ? Fmt(v) : "ERR " + error });
            }
        }

        // ------------------------------------------------------------ Q3 : carnet
        private void ApplyBook(Feed.BookUpdate updates, bool snapshot)
        {
            if (updates == null) return;
            if (updates.NeedResetMbo) _nMboResets++;
            if (updates.MboUpdates != null) { _nMbo += updates.MboUpdates.Count; _mMbo += updates.MboUpdates.Count; }
            if (updates.MbpUpdates == null) return;
            if (snapshot || updates.NeedResetMbp)
            {
                _bids.Clear();
                _asks.Clear();
                if (updates.NeedResetMbp) { _nBookResets++; _mBookResets++; }
            }
            foreach (var level in updates.MbpUpdates)
            {
                var side = level.isAsk ? _asks : _bids;
                if (level.Qty > 0) side[level.PinT] = level.Qty;
                else side.Remove(level.PinT);
                if (level.NumOffers > 0) _nLevelsWithOffers++;
            }
            if (_bids.Count > _maxBidLevels) _maxBidLevels = _bids.Count;
            if (_asks.Count > _maxAskLevels) _maxAskLevels = _asks.Count;
            int depth = Math.Max(DepthTicks(_bids), DepthTicks(_asks));
            if (depth > _maxDepthTicks) _maxDepthTicks = depth;
        }

        private static int DepthTicks(SortedDictionary<int, int> side) =>
            side.Count == 0 ? 0 : side.Keys.Last() - side.Keys.First();

        private static string Describe(Feed.BookUpdate u)
        {
            if (u == null) return "null";
            var sb = new StringBuilder();
            sb.Append("reset=").Append(u.NeedResetMbp).Append(",reset_mbo=").Append(u.NeedResetMbo)
              .Append(",best=").Append(Fmt(u.BestBidPrice)).Append('/').Append(Fmt(u.BestAskPrice))
              .Append(",exDt=").Append(Fmt(u.exDt))
              .Append(",levels=").Append(u.MbpUpdates == null ? "null" : u.MbpUpdates.Count.ToString(IC))
              .Append(",mbo=").Append(u.MboUpdates == null ? "null" : u.MboUpdates.Count.ToString(IC)).Append(",first=");
            if (u.MbpUpdates != null)
                foreach (var l in u.MbpUpdates.Take(6))
                    sb.Append(l.isAsk ? "A" : "B").Append(l.PinT.ToString(IC)).Append('x').Append(l.Qty.ToString(IC))
                      .Append('/').Append(l.NumOffers.ToString(IC)).Append("o ");
            if (u.MboUpdates != null)
            {
                sb.Append(",first_mbo=");
                foreach (var o in u.MboUpdates.Take(4))
                    sb.Append(o.IsAsk ? "A" : "B").Append(o.OrderId.ToString(IC)).Append('@').Append(Fmt(o.Price))
                      .Append('x').Append(o.Qty.ToString(IC)).Append('/').Append(o.Flags.ToString()).Append(' ');
            }
            return sb.ToString();
        }

        private void WriteMinuteStats(int b)
        {
            string t = BarTime(b);
            int bestBid = _bids.Count > 0 ? _bids.Keys.Last() : 0;
            int bestAsk = _asks.Count > 0 ? _asks.Keys.First() : 0;
            long bidQty10 = _bids.Reverse().Take(10).Sum(x => (long)x.Value);
            long askQty10 = _asks.Take(10).Sum(x => (long)x.Value);
            Log("BOOKMIN", new[] { t, _mBookUpdates.ToString(IC), _mBookResets.ToString(IC), _bids.Count.ToString(IC), _asks.Count.ToString(IC),
                PriceOf(bestBid), PriceOf(bestAsk), DepthTicks(_bids).ToString(IC), DepthTicks(_asks).ToString(IC),
                bidQty10.ToString(IC), askQty10.ToString(IC), _mMbo.ToString(IC) });
            Log("TICKMIN", new[] { t, _mTicks.ToString(IC), _mVol.ToString(IC), _mAskVol.ToString(IC), _mBidVol.ToString(IC), _mTicksWithIds.ToString(IC) });
        }

        private void ResetMinute()
        {
            _mBookUpdates = _mBookResets = _mTicks = _mVol = _mAskVol = _mBidVol = _mMbo = _mTicksWithIds = 0;
        }

        private string PriceOf(int pinT)
        {
            if (pinT == 0) return "";
            try { return Fmt(VAn.FromPinT(pinT).Price); } catch { return pinT.ToString(IC); }
        }

        // ------------------------------------------------------------ resume
        private void WriteSummary(string when)
        {
            int withHistory = _readable.Count(s => s.Coverage >= _histBarsScanned / 2 && s.Distinct.Count > 1);
            int rtReads = _readable.Sum(s => s.RtReads);
            int rtZeros = _readable.Sum(s => s.RtZeros);
            string replay = Safe(() => Convert.ToString(VAn.IsReplayData, IC));

            Log("SUM", new[] { "when=" + when,
                "open=" + _nOpenHist + "/" + _nOpenRt, "close=" + _nCloseHist + "/" + _nCloseRt, "end=" + _nEndHist + "/" + _nEndRt,
                "trades=" + _nTickHist + "/" + _nTickRt, "book_updates=" + _nBook, "book_resets=" + _nBookResets });
            _verdicts.Clear();
            _verdicts.Add("Q1 " + (_readable.Count > 0 ? "OUI " + _readable.Count + " series lisibles" : _historyDone ? "NON pour l'instant (lignes CAT)" : "en attente"));
            _verdicts.Add("Q2 " + (!_historyDone ? "en attente" : withHistory > 0 ? "OUI " + withHistory + " series renseignees sur au moins la moitie des barres relues et variables" : "NON ou constant (lignes HSER)"));
            _verdicts.Add("Q3 " + (_nBook > 0 ? "OUI " + _nBook + " mises a jour, jusqu'a " + _maxBidLevels + " niveaux bid / " + _maxAskLevels + " ask, " + _maxDepthTicks + " ticks de profondeur" : "aucune mise a jour recue"));
            _verdicts.Add("Q4 " + (_nTickHist > 0 ? "OUI " + _nTickHist + " trades historiques" : "aucun trade historique (Deepchart 16.5.4 ou plus requis, issue #1)"));
            _verdicts.Add("Q5 lignes envoyees au log (fichier chiffre sur disque : resultats lus sur ce panneau)");
            _verdicts.Add("Q6 replay=" + replay);
            _verdicts.Add("Q7 " + (rtReads == 0 ? "pas encore de lecture temps reel" : rtZeros + " valeurs nulles sur " + rtReads + " lectures temps reel"));
            _verdicts.Add("Q8 " + (_nBook == 0 ? "en attente du carnet" :
                (_nMbo > 0 ? "OUI " + _nMbo + " mises a jour MBO" : "NON pas de MBO") +
                ", niveaux avec nombre d'ordres : " + _nLevelsWithOffers +
                ", trades avec identifiants d'ordre : " + _nTicksWithIds + "/" + _nTickRt +
                ", icebergs : " + _nIcebergTicks + ", sweeps : " + _nSweepTicks));
            foreach (var v in _verdicts)
                Log("VERDICT", new[] { v.Substring(0, 2), v.Substring(3) });
            foreach (var key in _errorCount.Keys.Take(20))
                Log("ERR", new[] { key, "x" + _errorCount[key], _errorFirst[key] });
            UpdatePanel();
        }

        // ------------------------------------------------------------ panneau sur le graphique
        private void CreatePanel()
        {
            _panel.Clear();
            try
            {
                int lines = 12 + PanelMaxSeries;
                for (int i = 0; i < lines; i++)
                {
                    var a = VAn.CreateAnnotation(AnnotationType.Text);
                    a.CoordinateXType = CoordinateTypeEnum.Relative;
                    a.CoordinateYType = CoordinateTypeEnum.Relative;
                    a.X = PanelX;
                    a.Y = PanelY - i * PanelLineStep;
                    a.FontSize = PanelFontSize;
                    a.FontBold = i == 0;
                    a.Text = i == 0 ? "Probe DeepGamma v" + Version + " : chargement de l'historique..." : "";
                    VAn.AddAnnotation(IndVars.FrontAnnList, a);
                    _panel.Add(a);
                }
            }
            catch (Exception ex) { Err("CreatePanel", ex); }
        }

        private void UpdatePanel()
        {
            if (_panel.Count == 0) return;
            try
            {
                _panelUpdates++;
                var lines = new List<string>();
                lines.Add("Probe DeepGamma v" + Version + "  |  replay=" + Safe(() => Convert.ToString(VAn.IsReplayData, IC)) +
                          "  |  " + Safe(() => Convert.ToString(VAn.ChartPhase, IC)) + "  |  id sonde=" + Safe(() => getId().ToString(IC)) +
                          "  |  barres=" + (_historyLastIndex + 1) + "  |  maj " + Fmt(VAn.LastExDt));
                lines.AddRange(_verdicts);
                lines.Add("Carnet : " + _bids.Count + " niveaux bid / " + _asks.Count + " ask, MBO=" + _nMbo +
                          "  |  trades hist/rt=" + _nTickHist + "/" + _nTickRt);
                lines.Add("Series lisibles (indicateur / serie : valeur derniere barre fermee, couverture historique) :");
                foreach (var s in _readable.Take(PanelMaxSeries))
                    lines.Add("  " + s.Ind + " " + Name(s.Ind) + " / " + s.Ser + " " + SeriesName(s.Ind, s.Ser) + " : " +
                              (s.LastRt.Length > 0 ? s.LastRt + " (rt)" : s.Last) + "  [" + s.Coverage + "/" + _histBarsScanned + "]");
                if (_readable.Count > PanelMaxSeries) lines.Add("  ... et " + (_readable.Count - PanelMaxSeries) + " autres");
                foreach (var key in _errorCount.Keys.Take(2))
                    lines.Add("Erreur " + key + " x" + _errorCount[key] + " : " + _errorFirst[key]);

                for (int i = 0; i < _panel.Count; i++)
                    _panel[i].Text = i < lines.Count ? lines[i] : "";
            }
            catch (Exception ex) { Err("UpdatePanel", ex); }
        }

        private string Name(int ind)
        {
            string n;
            return _indNames.TryGetValue(ind, out n) ? n : "?";
        }

        private string SeriesName(int ind, int ser)
        {
            List<string> names;
            return _seriesNames.TryGetValue(ind, out names) && ser < names.Count ? names[ser] : "?";
        }

        // ------------------------------------------------------------ utilitaires
        private void LogTick(string kind, Feed.TickByTick t, AggrInfo aggrInfo)
        {
            Log(kind, new[] { "price=" + Fmt(t.price), "pinT=" + t.PinT.ToString(IC), "vol=" + t.Vol.ToString(IC),
                "qtyOrd=" + t.QtyOrd.ToString(IC), "side=" + t.AggrSide, "bid=" + Fmt(t.bid), "ask=" + Fmt(t.ask),
                "exDt=" + Fmt(t.exDt), "locDt=" + Fmt(t.locDt), "type=" + t.TradeType,
                "aggrId=" + t.AggrID.ToString(IC), "restId=" + t.RestID.ToString(IC), "counter=" + t.CounterSize.ToString(IC),
                "aggr=" + (aggrInfo == null ? "null" : "pinT:" + aggrInfo.PinT.ToString(IC) + "/vol:" + aggrInfo.Vol.ToString(IC) +
                    "/trades:" + aggrInfo.NumberOfTrades.ToString(IC) + "/maxQtyOrd:" + aggrInfo.MaxQtyOrd.ToString(IC) +
                    "/dir:" + aggrInfo.TradeDir) });
        }

        private string BarTime(int b)
        {
            try { return Fmt(VAn.BarVars[b].exchDt); } catch { return ""; }
        }

        private void Log(string kind, string[] fields)
        {
            var sb = new StringBuilder(Prefix);
            sb.Append(kind);
            foreach (var f in fields) sb.Append(';').Append(Clean(f));
            try { VAn.AddToLog(sb.ToString(), VolumetricaAPI.LogLevelEnum.Information); } catch { }
        }

        private void Err(string where, Exception ex)
        {
            // Pas de ex.GetType().Name : Type.Name passe par System.Reflection, que Deepchart refuse
            string key = where;
            int n;
            _errorCount[key] = _errorCount.TryGetValue(key, out n) ? n + 1 : 1;
            if (!_errorFirst.ContainsKey(key)) _errorFirst[key] = ex.Message ?? "";
        }

        private static string Fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("R", IC);

        private static string Fmt(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", IC);

        private static string Clean(string s) => (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch (Exception ex) { return "?" + (ex.Message ?? ""); }
        }
    }
}
