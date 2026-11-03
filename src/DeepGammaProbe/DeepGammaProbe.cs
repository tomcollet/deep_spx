// =====================================================================
//  DeepGammaProbe.cs  -  DLL de test DeepCharts (lecture seule)
//
//  Objectif : repondre a 6 questions avant de construire l'enregistreur
//    Q1  La DLL peut-elle lire les series des autres indicateurs du
//        graphique (dont DeepGamma) via GetSeriesValueById ?
//    Q2  Ces series ont-elles un historique (valeurs qui varient sur les
//        barres passees) ?
//    Q3  Le carnet (OnBook / RebuildDom) arrive-t-il ?
//    Q4  Les trades historiques arrivent-ils (OnTickCall = HistRT) ?
//    Q5  La DLL peut-elle ecrire des fichiers ?
//    Q6  En Deep Replay, tout cela fonctionne-t-il aussi ?
//
//  La sonde n'envoie aucun ordre et ne dessine rien. Elle ecrit dans
//  Documents\Deepchart\Probe\<date_heure>\ (un dossier par chargement).
//
//  Les appels dont la signature n'est pas documentee (GetSeriesValueById,
//  GetParamsById, RebuildDom) passent par reflexion : la sonde decouvre
//  leur signature exacte et l'ecrit dans api_dump.txt.
//
//  Compilation : dotnet build src/DeepGammaProbe -p:DeepchartDevId=TON-ID
//  Protocole de test : src/DeepGammaProbe/LISEZMOI.md
// =====================================================================

using System.Collections;
using System.Globalization;
using System.Reflection;
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
        public static IndicatorDescriptionBase Register() =>
            new IndicatorDescriptionBase { Name = "Probe - DeepGamma Recorder Test" };

        // ------------------------------------------------------------ parametres
        /// Premier identifiant d'indicateur teste (0 par defaut).
        public int IndicatorIdStart { get; set; } = 0;
        /// Nombre d'identifiants d'indicateurs testes a partir de IndicatorIdStart.
        public int MaxIndicators { get; set; } = 40;
        /// Nombre de series (subgraphs) testees par indicateur.
        public int MaxSeries { get; set; } = 20;
        /// Nombre de barres passees relues a la fin de l'historique.
        public int HistoryBarsToScan { get; set; } = 600;
        /// Duree d'enregistrement detaille des trades temps reel (minutes).
        public int TickLogMinutes { get; set; } = 30;
        /// Nombre maximum de trades historiques ecrits (les autres sont comptes).
        public int HistoryTicksToLog { get; set; } = 2000;
        /// Duree d'enregistrement detaille du carnet (minutes).
        public int BookLogMinutes { get; set; } = 3;

        // ------------------------------------------------------------ etat
        private readonly object _lock = new object();
        private const string Inv = "R";
        private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        private bool _initialized;
        private string _dir = "";
        private StreamWriter _api, _ticks, _ticksHist, _book, _series, _bars;
        private bool _ticksHeader, _ticksHistHeader;

        private DateTime _startWall, _historyEndWall, _firstRtWall, _lastSummary;
        private bool _historyDone, _historyEndedInRt;
        private int _historyLastIndex = -1;

        private long _nTickRt, _nTickHist, _nBook, _nOpenRt, _nOpenHist, _nCloseRt, _nCloseHist, _nEndRt, _nEndHist;
        private int _bookExamples, _bookSnapshotTries, _rtDiscoveryTries;
        private bool _bookSnapshotOk;
        private DateTime _lastBookSnapshotTry;
        private bool _tickTypeDumped;

        private MethodInfo _getSeries, _getParams, _rebuildDom;
        private readonly List<(int ind, int ser)> _readable = new List<(int ind, int ser)>();
        private readonly Dictionary<(int ind, int ser), HashSet<string>> _histDistinct = new Dictionary<(int ind, int ser), HashSet<string>>();
        private readonly Dictionary<(int ind, int ser), string> _lastValue = new Dictionary<(int ind, int ser), string>();
        private readonly Dictionary<(int ind, int ser), int> _histCoverage = new Dictionary<(int ind, int ser), int>();
        private int _histBarsScanned;
        private bool _summaryAfterFirstRt;
        private readonly Dictionary<string, (int count, string first)> _errors = new Dictionary<string, (int count, string first)>();

        private string _lastTickExch = "", _lastTickWall = "";
        private double _lastLagHours = double.NaN;

        // ------------------------------------------------------------ cycle de vie
        public override void OnSet(bool setDefault, bool themeOverride)
        {
            OnOpenCall = CallHandler.HistRT;
            OnCloseCall = CallHandler.HistRT;
            OnEndCall = CallHandler.HistRT;
            OnTickCall = CallHandler.HistRT;   // Q4 : trades historiques (desktop uniquement)
            OnBookCall = CallHandler.RT;       // Q3 : carnet (desktop uniquement)
            Description = "Probe DeepGamma";
        }

        public override void OnLoad()
        {
            lock (_lock)
            {
                try { InitRun(); }
                catch (Exception ex) { SafeLog("Probe : initialisation echouee : " + ex); }
            }
        }

        public override void OnOpen(bool isRt)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    if (!isRt) { _nOpenHist++; return; }
                    _nOpenRt++;
                    MarkRt();
                    if (!_historyDone) return;

                    // Si rien n'etait lisible a la fin de l'historique, on retente
                    // la decouverte en temps reel (DeepGamma peut calculer apres nous).
                    if (_readable.Count == 0 && _rtDiscoveryTries < 10 && _nOpenRt % 3 == 1)
                    {
                        _rtDiscoveryTries++;
                        DiscoverSeries("temps reel, essai " + _rtDiscoveryTries);
                    }

                    int b = VAn.BarIndex - 1;   // barre qui vient de se fermer
                    if (b >= 0)
                    {
                        WriteBar("rt", b);
                        RecordSeries("rt", b);
                    }
                }
                catch (Exception ex) { Err("OnOpen", ex); }
            }
        }

        public override void OnClose(bool isRt)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                if (isRt) _nCloseRt++; else _nCloseHist++;
            }
        }

        public override void OnTick(Feed.TickByTick tick, bool isRt, AggrInfo aggrInfo)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    object t = tick;
                    if (!_tickTypeDumped)
                    {
                        _tickTypeDumped = true;
                        Api("\n=== Type reel d'un trade (OnTick) ===");
                        DumpTypeMembers(t?.GetType());
                        Api("Exemple de trade   : " + Dump(t, 2));
                        Api("Exemple d'AggrInfo : " + Dump(aggrInfo, 2));
                    }

                    if (isRt)
                    {
                        _nTickRt++;
                        MarkRt();
                        TrackLag(t);
                        if ((DateTime.UtcNow - _firstRtWall).TotalMinutes <= TickLogMinutes)
                        {
                            if (!_ticksHeader) { _ticks.WriteLine("wall_utc;" + FlatHeader(t)); _ticksHeader = true; }
                            _ticks.WriteLine(Wall() + ";" + FlatValues(t));
                        }
                    }
                    else
                    {
                        _nTickHist++;
                        if (_nTickHist <= HistoryTicksToLog)
                        {
                            if (!_ticksHistHeader) { _ticksHist.WriteLine(FlatHeader(t)); _ticksHistHeader = true; }
                            _ticksHist.WriteLine(FlatValues(t));
                        }
                    }
                }
                catch (Exception ex) { Err("OnTick", ex); }
            }
        }

        public override void OnBook(Feed.BookUpdate updates)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    object u = updates;
                    _nBook++;
                    MarkRt();
                    if (_bookExamples < 3)
                    {
                        if (_bookExamples == 0)
                        {
                            Api("\n=== Type reel d'une mise a jour du carnet (OnBook) ===");
                            DumpTypeMembers(u?.GetType());
                        }
                        _bookExamples++;
                        Api("Exemple OnBook #" + _bookExamples + " : " + Dump(u, 3, 40));
                    }
                    if ((DateTime.UtcNow - _firstRtWall).TotalMinutes <= BookLogMinutes)
                        _book.WriteLine(Wall() + ";" + Dump(u, 3, 40));
                }
                catch (Exception ex) { Err("OnBook", ex); }
            }
        }

        public override void OnEnd(bool isRt)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    if (isRt) _nEndRt++; else _nEndHist++;

                    if (!_historyDone)
                    {
                        // Premier OnEnd = fin du calcul de l'historique.
                        _historyDone = true;
                        _historyEndWall = DateTime.UtcNow;
                        _historyEndedInRt = isRt;
                        _historyLastIndex = VAn.BarIndex;
                        DiscoverSeries("fin de l'historique");
                        ScanHistory();
                        WriteSummary();
                    }

                    if (isRt) TryBookSnapshot();
                    if (isRt && !_summaryAfterFirstRt) { _summaryAfterFirstRt = true; WriteSummary(); }

                    if ((DateTime.UtcNow - _lastSummary).TotalSeconds >= 10) WriteSummary();
                }
                catch (Exception ex) { Err("OnEnd", ex); }
            }
        }

        // ------------------------------------------------------------ initialisation
        private void InitRun()
        {
            CloseWriters();
            ResetState();

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) docs = Path.GetTempPath();
            _dir = Path.Combine(docs, "Deepchart", "Probe", DateTime.Now.ToString("yyyyMMdd_HHmmss", IC));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "file_write_test.txt"), "Ecriture OK " + Wall() + Environment.NewLine);   // Q5

            _api = Open("api_dump.txt");
            _ticks = Open("ticks_rt.csv");
            _ticksHist = Open("ticks_hist.csv");
            _book = Open("book_rt.txt");
            _series = Open("series_values.csv");
            _series.WriteLine("mode;bar_index;bar_time;indicator;series;value");
            _bars = Open("bars.csv");
            _bars.WriteLine("mode;bar_index;" + "bar_fields");

            _startWall = DateTime.UtcNow;
            _initialized = true;

            ResolveApi();
            DumpApi();

            string msg = "Probe actif. Sorties : " + _dir;
            SafeLog(msg);
            try { VAn.ShowMessage(msg, VAn.ChartName); } catch (Exception ex) { Err("ShowMessage", ex); }
        }

        private void ResetState()
        {
            _initialized = false;
            _ticksHeader = _ticksHistHeader = false;
            _historyDone = _historyEndedInRt = false;
            _historyLastIndex = -1;
            _firstRtWall = DateTime.MinValue;
            _lastSummary = DateTime.MinValue;
            _nTickRt = _nTickHist = _nBook = _nOpenRt = _nOpenHist = _nCloseRt = _nCloseHist = _nEndRt = _nEndHist = 0;
            _bookExamples = _bookSnapshotTries = _rtDiscoveryTries = 0;
            _bookSnapshotOk = false;
            _lastBookSnapshotTry = DateTime.MinValue;
            _tickTypeDumped = false;
            _readable.Clear(); _histDistinct.Clear(); _lastValue.Clear(); _errors.Clear(); _histCoverage.Clear();
            _histBarsScanned = 0;
            _summaryAfterFirstRt = false;
            _lastTickExch = _lastTickWall = "";
            _lastLagHours = double.NaN;
        }

        private StreamWriter Open(string name)
        {
            var w = new StreamWriter(Path.Combine(_dir, name), false, new UTF8Encoding(false));
            w.AutoFlush = true;   // aucune perte si DeepCharts ferme sans prevenir
            return w;
        }

        private void CloseWriters()
        {
            foreach (var w in new[] { _api, _ticks, _ticksHist, _book, _series, _bars })
                try { w?.Dispose(); } catch { }
            _api = _ticks = _ticksHist = _book = _series = _bars = null;
        }

        private void MarkRt()
        {
            if (_firstRtWall == DateTime.MinValue) _firstRtWall = DateTime.UtcNow;
        }

        // ------------------------------------------------------------ API par reflexion
        private void ResolveApi()
        {
            _getSeries = FindMethod(VAn, "GetSeriesValueById", 3);
            _getParams = FindMethod(VAn, "GetParamsById", 1);
            _rebuildDom = FindMethod(VAn, "RebuildDom", 0);
        }

        private static MethodInfo FindMethod(object target, string name, int preferredParams)
        {
            if (target == null) return null;
            var candidates = new List<MethodInfo>();
            var t = target.GetType();
            candidates.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == name));
            foreach (var it in t.GetInterfaces())
                candidates.AddRange(it.GetMethods().Where(m => m.Name == name));
            candidates.AddRange(t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name.EndsWith("." + name)));
            return candidates.FirstOrDefault(m => m.GetParameters().Length == preferredParams)
                ?? candidates.FirstOrDefault(m => m.GetParameters().Count(p => !p.IsOptional) <= preferredParams)
                ?? candidates.FirstOrDefault();
        }

        /// Construit les arguments d'un appel en convertissant les valeurs candidates.
        private static object[] BuildArgs(MethodInfo m, params object[] values)
        {
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType;
                if (i >= values.Length)
                {
                    if (ps[i].IsOptional) { args[i] = ps[i].DefaultValue; continue; }
                    return null;
                }
                var v = values[i];
                if (v == null || pt.IsInstanceOfType(v)) { args[i] = v; continue; }
                if (pt == typeof(string)) { args[i] = Convert.ToString(v, IC); continue; }
                try { args[i] = pt.IsEnum ? Enum.ToObject(pt, v) : Convert.ChangeType(v, pt, IC); }
                catch { return null; }
            }
            return args;
        }

        private (bool ok, object value, string error) CallApi(MethodInfo m, params object[] values)
        {
            if (m == null) return (false, null, "methode introuvable");
            var args = BuildArgs(m, values);
            if (args == null) return (false, null, "signature non geree : " + Signature(m));
            try { return (true, m.Invoke(VAn, args), null); }
            catch (TargetInvocationException tie)
            {
                var e = tie.InnerException ?? tie;
                return (false, null, e.GetType().Name + ": " + e.Message);
            }
            catch (Exception e) { return (false, null, e.GetType().Name + ": " + e.Message); }
        }

        // ------------------------------------------------------------ Q1 : decouverte des series
        private void DiscoverSeries(string when)
        {
            _readable.Clear();
            int last = VAn.BarIndex;
            string tag = DateTime.Now.ToString("HHmmss", IC);

            // Parametres de chaque indicateur (pour savoir qui est qui)
            using (var w = new StreamWriter(Path.Combine(_dir, "indicators_" + tag + ".txt"), false, new UTF8Encoding(false)))
            {
                w.WriteLine("Decouverte : " + when + " | barre courante " + last + " | " + Wall());
                w.WriteLine("GetParamsById : " + Signature(_getParams));
                for (int ind = IndicatorIdStart; ind < IndicatorIdStart + MaxIndicators; ind++)
                {
                    var r = CallApi(_getParams, ind);
                    w.WriteLine("--- indicateur " + ind + " : " + (r.ok ? Dump(r.value, 4, 60) : "ERREUR " + r.error));
                }
            }

            // Valeurs de chaque serie, a la barre courante et dans le passe
            using (var w = new StreamWriter(Path.Combine(_dir, "series_catalog_" + tag + ".csv"), false, new UTF8Encoding(false)))
            {
                w.WriteLine("indicator;series;value_now;value_minus_10;value_minus_60;value_minus_390;error");
                int[] offsets = { 0, 10, 60, 390 };
                for (int ind = IndicatorIdStart; ind < IndicatorIdStart + MaxIndicators; ind++)
                {
                    for (int ser = 0; ser < MaxSeries; ser++)
                    {
                        var cells = new string[offsets.Length];
                        string firstError = null;
                        bool hasValue = false;
                        for (int k = 0; k < offsets.Length; k++)
                        {
                            int b = last - offsets[k];
                            if (b < 0) { cells[k] = ""; continue; }
                            var r = CallApi(_getSeries, ind, ser, b);
                            if (r.ok)
                            {
                                cells[k] = Fmt(r.value);
                                if (HasValue(r.value)) hasValue = true;
                            }
                            else { cells[k] = "ERR"; if (firstError == null) firstError = r.error; }
                        }
                        if (hasValue) _readable.Add((ind, ser));
                        // On n'ecrit que les lignes utiles : valeur trouvee, ou premiere erreur de l'indicateur
                        if (hasValue || ser == 0)
                            w.WriteLine(ind + ";" + ser + ";" + string.Join(";", cells) + ";" + Clean(firstError ?? ""));
                    }
                }
            }
            SafeLog("Probe : " + _readable.Count + " series lisibles (" + when + ")");
        }

        // ------------------------------------------------------------ Q2 : historique
        private void ScanHistory()
        {
            int last = VAn.BarIndex;
            int from = Math.Max(0, last - HistoryBarsToScan);
            _histBarsScanned = last - from + 1;
            for (int b = from; b <= last; b++)
            {
                WriteBar("hist", b);
                RecordSeries("hist", b);
            }
        }

        private void RecordSeries(string mode, int b)
        {
            if (_readable.Count == 0) return;
            string bt = BarTime(b);
            foreach (var key in _readable)
            {
                var r = CallApi(_getSeries, key.ind, key.ser, b);
                string v = r.ok ? Fmt(r.value) : "ERR " + Clean(r.error);
                _series.WriteLine(mode + ";" + b + ";" + bt + ";" + key.ind + ";" + key.ser + ";" + v);
                _lastValue[key] = v;
                if (mode == "hist" && r.ok && HasValue(r.value))
                {
                    _histCoverage[key] = (_histCoverage.TryGetValue(key, out var c) ? c : 0) + 1;
                    if (!_histDistinct.TryGetValue(key, out var set)) _histDistinct[key] = set = new HashSet<string>();
                    if (set.Count < 1000) set.Add(v);
                }
            }
        }

        private void WriteBar(string mode, int b)
        {
            try
            {
                var bar = VAn.BarVars[b];
                object tot = null;
                var list = GetMember(bar, "VolTotList") as IList;
                if (list != null && list.Count > 0) tot = list[0];
                _bars.WriteLine(mode + ";" + b + ";" +
                    "exchDt=" + Fmt(GetMember(bar, "exchDt")) +
                    ";open=" + Fmt(GetMember(bar, "Open")) +
                    ";high=" + Fmt(GetMember(bar, "High")) +
                    ";low=" + Fmt(GetMember(bar, "Low")) +
                    ";close=" + Fmt(GetMember(bar, "Close")) +
                    ";totVol=" + Fmt(GetMember(tot, "TotVol")) +
                    ";askVol=" + Fmt(GetMember(tot, "AskVol")) +
                    ";bidVol=" + Fmt(GetMember(tot, "BidVol")));
            }
            catch (Exception ex) { Err("WriteBar", ex); }
        }

        private string BarTime(int b)
        {
            try { return Fmt(GetMember(VAn.BarVars[b], "exchDt")); }
            catch { return ""; }
        }

        // ------------------------------------------------------------ Q3 : instantane du carnet
        private void TryBookSnapshot()
        {
            if (_bookSnapshotOk || _bookSnapshotTries >= 5) return;
            if ((DateTime.UtcNow - _lastBookSnapshotTry).TotalSeconds < 30) return;
            _lastBookSnapshotTry = DateTime.UtcNow;
            _bookSnapshotTries++;
            var r = CallApi(_rebuildDom);
            string text = r.ok ? Dump(r.value, 4, 200) : "ERREUR " + r.error;
            File.AppendAllText(Path.Combine(_dir, "book_snapshot.txt"),
                "=== RebuildDom essai " + _bookSnapshotTries + " a " + Wall() + " ===" + Environment.NewLine + text + Environment.NewLine + Environment.NewLine);
            if (r.ok && r.value != null && !text.Contains("[0]")) _bookSnapshotOk = true;
        }

        // ------------------------------------------------------------ Q6 : live ou replay
        private void TrackLag(object tick)
        {
            var ex = GetMember(tick, "exDt");
            _lastTickExch = Fmt(ex);
            _lastTickWall = Wall();
            if (ex is DateTime dt) _lastLagHours = (DateTime.UtcNow - dt).TotalHours;
        }

        // ------------------------------------------------------------ resume
        private void WriteSummary()
        {
            _lastSummary = DateTime.UtcNow;
            var s = new StringBuilder();
            s.AppendLine("PROBE DEEPGAMMA - RESUME (mis a jour toutes les 10 s)");
            s.AppendLine("Ecrit a            : " + Wall());
            s.AppendLine("Dossier            : " + _dir);
            s.AppendLine("Symbole / graphique: " + SafeStr(() => VAn.SymbolName) + " / " + SafeStr(() => VAn.ChartName));
            s.AppendLine("Demarrage          : " + _startWall.ToString("o", IC));
            s.AppendLine("Fin historique     : " + (_historyDone ? _historyEndWall.ToString("o", IC) + " (barre " + _historyLastIndex + ", deja en temps reel : " + _historyEndedInRt + ")" : "pas encore"));
            s.AppendLine("Premier temps reel : " + (_firstRtWall == DateTime.MinValue ? "aucun" : _firstRtWall.ToString("o", IC)));
            s.AppendLine();
            s.AppendLine("Compteurs : OnOpen hist/rt = " + _nOpenHist + "/" + _nOpenRt + " | OnClose hist/rt = " + _nCloseHist + "/" + _nCloseRt + " | OnEnd hist/rt = " + _nEndHist + "/" + _nEndRt);
            s.AppendLine("           trades hist/rt = " + _nTickHist + "/" + _nTickRt + " | mises a jour carnet = " + _nBook);
            s.AppendLine("Dernier trade : heure bourse " + _lastTickExch + " | heure PC (UTC) " + _lastTickWall +
                         " | ecart " + (double.IsNaN(_lastLagHours) ? "?" : _lastLagHours.ToString("0.0", IC) + " h"));
            s.AppendLine();
            s.AppendLine("Signatures trouvees :");
            s.AppendLine("  GetSeriesValueById : " + Signature(_getSeries));
            s.AppendLine("  GetParamsById      : " + Signature(_getParams));
            s.AppendLine("  RebuildDom         : " + Signature(_rebuildDom));
            s.AppendLine();
            s.AppendLine("Series lisibles : " + _readable.Count);
            foreach (var key in _readable)
            {
                int distinct = _histDistinct.TryGetValue(key, out var set) ? set.Count : 0;
                int cover = _histCoverage.TryGetValue(key, out var cv) ? cv : 0;
                string last = _lastValue.TryGetValue(key, out var lv) ? lv : "";
                s.AppendLine("  indicateur " + key.ind + " serie " + key.ser + " : historique = " + cover + "/" + _histBarsScanned +
                             " barres avec valeur, " + distinct + " valeurs distinctes | derniere valeur = " + last);
            }
            s.AppendLine();

            int varying = _readable.Count(k => _histDistinct.TryGetValue(k, out var hs) && hs.Count > 1 &&
                                               _histCoverage.TryGetValue(k, out var hc) && hc >= _histBarsScanned / 2);
            s.AppendLine("VERDICT PROVISOIRE");
            s.AppendLine("  Q1 lecture des autres indicateurs : " + (_readable.Count > 0 ? "OUI (" + _readable.Count + " series)" : (_historyDone ? "NON pour l'instant (voir series_catalog_*.csv)" : "en attente")));
            s.AppendLine("  Q2 historique des series          : " + (!_historyDone ? "en attente" : varying > 0 ? "OUI (" + varying + " series renseignees sur au moins la moitie des barres relues et variables)" : "NON ou constant (a confirmer avec indicators_*.txt)"));
            s.AppendLine("  Q3 carnet                         : " + (_nBook > 0 ? "OUI (" + _nBook + " mises a jour)" : "aucune mise a jour recue") + (_bookSnapshotTries > 0 ? " | RebuildDom : voir book_snapshot.txt" : ""));
            s.AppendLine("  Q4 trades historiques             : " + (_nTickHist > 0 ? "OUI (" + _nTickHist + ")" : "aucun"));
            s.AppendLine("  Q5 ecriture de fichiers           : OUI");
            s.AppendLine("  Q6 replay                         : " + (double.IsNaN(_lastLagHours) ? "inconnu (pas de trade temps reel)" : _lastLagHours > 20 ? "REPLAY probable (trades dates de plus de 20 h)" : "LIVE probable"));
            s.AppendLine();
            s.AppendLine("Erreurs (" + _errors.Count + " types) :");
            foreach (var kv in _errors.Take(40)) s.AppendLine("  " + kv.Key + " x" + kv.Value.count + " : " + kv.Value.first);

            try { File.WriteAllText(Path.Combine(_dir, "summary.txt"), s.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { SafeLog("Probe : resume non ecrit : " + ex.Message); }
        }

        // ------------------------------------------------------------ inventaire de l'API
        private void DumpApi()
        {
            Api("PROBE DEEPGAMMA - INVENTAIRE DE L'API  " + Wall());
            Api(".NET " + Environment.Version + " | OS " + Environment.OSVersion + " | process " + SafeStr(() => System.Diagnostics.Process.GetCurrentProcess().ProcessName));

            Api("\n=== Classe de base Indicator ===");
            DumpTypeMembers(typeof(VolSysAPI.Indicators.Indicator));

            var vanProp = typeof(VolSysAPI.Indicators.Indicator).GetProperty("VAn");
            Api("\n=== VAn : type declare = " + Friendly(vanProp?.PropertyType) + " | type reel = " + Friendly(VAn?.GetType()) + " ===");
            if (VAn != null)
            {
                DumpTypeMembers(VAn.GetType());
                foreach (var it in VAn.GetType().GetInterfaces())
                {
                    Api("\n--- interface " + Friendly(it));
                    DumpTypeMembers(it);
                }
            }

            try
            {
                var barsType = VAn?.BarVars?.GetType();
                var barType = barsType != null && barsType.IsGenericType ? barsType.GetGenericArguments()[0] : null;
                Api("\n=== Barre (" + Friendly(barType) + ") ===");
                DumpTypeMembers(barType);
            }
            catch (Exception ex) { Err("DumpApi.Bar", ex); }

            var tradingProp = typeof(VolSysAPI.Indicators.Indicator).GetProperty("TradingApi");
            Api("\n=== TradingApi (inventaire seulement, aucun appel) : " + Friendly(tradingProp?.PropertyType) + " ===");
            if (tradingProp != null)
            {
                DumpTypeMembers(tradingProp.PropertyType);
                foreach (var it in tradingProp.PropertyType.GetInterfaces()) { Api("--- interface " + Friendly(it)); DumpTypeMembers(it); }
            }

            Api("\n=== Assemblies chargees (Vol*, Deep*, *Gamma*, *Option*) ===");
            var keyAsm = new HashSet<Assembly>();
            keyAsm.Add(typeof(VolSysAPI.Indicators.Indicator).Assembly);
            if (VAn != null) keyAsm.Add(VAn.GetType().Assembly);
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name ?? "";
                if (n.StartsWith("Vol", StringComparison.OrdinalIgnoreCase) || n.IndexOf("Deep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Gamma", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Api("  " + a.GetName().Name + " " + a.GetName().Version);
                    keyAsm.Add(a);
                }
            }

            string[] keywords = { "Option", "Gamma", "Gex", "Greek", "Strike", "Trading", "Order", "Dom", "Book", "Series" };
            var interesting = new List<Type>();
            foreach (var a in keyAsm)
            {
                Type[] types;
                try { types = a.GetExportedTypes(); }
                catch (Exception ex) { Api("  (types de " + a.GetName().Name + " illisibles : " + ex.GetType().Name + ")"); continue; }
                Api("\n=== Types publics de " + a.GetName().Name + " (" + types.Length + ") ===");
                foreach (var t in types.OrderBy(t => t.FullName).Take(3000)) Api("  " + t.FullName);
                interesting.AddRange(types.Where(t => keywords.Any(k => t.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)));
            }
            Api("\n=== Detail des types dont le nom contient " + string.Join("/", keywords) + " ===");
            foreach (var t in interesting.Distinct().Take(80))
            {
                Api("\n--- " + t.FullName);
                DumpTypeMembers(t);
            }
        }

        private void DumpTypeMembers(Type t)
        {
            if (t == null) { Api("  (type inconnu)"); return; }
            try
            {
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                {
                    if (cur != t) Api("  (herite de " + Friendly(cur) + ")");
                    foreach (var f in cur.GetFields(flags))
                        Api("  field " + Friendly(f.FieldType) + " " + f.Name + Attrs(f));
                    foreach (var p in cur.GetProperties(flags))
                        Api("  prop  " + Friendly(p.PropertyType) + " " + p.Name + " {" + (p.CanRead ? " get;" : "") + (p.CanWrite ? " set;" : "") + " }" + Attrs(p));
                    foreach (var m in cur.GetMethods(flags).Where(m => !m.IsSpecialName))
                        Api("  meth  " + Signature(m) + Attrs(m));
                    foreach (var e in cur.GetEvents(flags))
                        Api("  event " + Friendly(e.EventHandlerType) + " " + e.Name);
                    if (t.IsEnum) { Api("  valeurs : " + string.Join(", ", Enum.GetNames(t))); break; }
                    if (t.IsInterface) break;
                }
            }
            catch (Exception ex) { Api("  (erreur d'inventaire : " + ex.GetType().Name + " " + ex.Message + ")"); }
        }

        private static string Attrs(MemberInfo m)
        {
            try
            {
                var names = m.GetCustomAttributesData().Select(a => a.AttributeType.Name.Replace("Attribute", "")).ToArray();
                return names.Length == 0 ? "" : "   [" + string.Join(", ", names) + "]";
            }
            catch { return ""; }
        }

        private static string Signature(MethodInfo m)
        {
            if (m == null) return "(introuvable)";
            var ps = m.GetParameters().Select(p => Friendly(p.ParameterType) + " " + p.Name + (p.IsOptional ? " = " + Fmt(p.DefaultValue) : ""));
            return Friendly(m.ReturnType) + " " + (m.DeclaringType != null ? Friendly(m.DeclaringType) + "." : "") + m.Name + "(" + string.Join(", ", ps) + ")";
        }

        private static string Friendly(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.FullName ?? t.Name;
            string n = t.Name; int i = n.IndexOf('`'); if (i > 0) n = n.Substring(0, i);
            return n + "<" + string.Join(", ", t.GetGenericArguments().Select(Friendly)) + ">";
        }

        // ------------------------------------------------------------ serialisation generique
        private static readonly Dictionary<Type, List<(string name, Func<object, object> get)>> _accessors =
            new Dictionary<Type, List<(string name, Func<object, object> get)>>();

        private static readonly object _accLock = new object();

        private static List<(string name, Func<object, object> get)> Accessors(Type t)
        {
            lock (_accLock) return BuildAccessors(t);
        }

        private static List<(string name, Func<object, object> get)> BuildAccessors(Type t)
        {
            if (_accessors.TryGetValue(t, out var list)) return list;
            list = new List<(string name, Func<object, object> get)>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var ff = f; list.Add((ff.Name, o => ff.GetValue(o)));
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                var pp = p; list.Add((pp.Name, o => pp.GetValue(o)));
            }
            _accessors[t] = list;
            return list;
        }

        private static object GetMember(object o, string name)
        {
            if (o == null) return null;
            foreach (var a in Accessors(o.GetType()))
                if (a.name == name) { try { return a.get(o); } catch { return null; } }
            foreach (var a in Accessors(o.GetType()))
                if (string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase)) { try { return a.get(o); } catch { return null; } }
            return null;
        }

        private static bool IsScalar(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) ||
            t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid);

        private static string Fmt(object v)
        {
            if (v == null) return "null";
            switch (v)
            {
                case double d: return double.IsNaN(d) ? "NaN" : d.ToString(Inv, IC);
                case float f: return float.IsNaN(f) ? "NaN" : f.ToString(Inv, IC);
                case DateTime dt: return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", IC) + (dt.Kind == DateTimeKind.Utc ? "Z" : dt.Kind == DateTimeKind.Local ? "L" : "");
                case string s: return Clean(s);
            }
            if (IsScalar(v.GetType())) return Clean(Convert.ToString(v, IC));
            return Clean(Dump(v, 2, 20));
        }

        private static bool HasValue(object v)
        {
            if (v == null) return false;
            if (v is double d) return !double.IsNaN(d) && !double.IsInfinity(d);
            if (v is float f) return !float.IsNaN(f) && !float.IsInfinity(f);
            return true;
        }

        private static string Dump(object o, int depth, int maxItems = 20)
        {
            if (o == null) return "null";
            var t = o.GetType();
            if (IsScalar(t)) return Fmt(o);
            if (depth <= 0) return Friendly(t);
            if (o is IEnumerable en)
            {
                var sb = new StringBuilder();
                int total = 0;
                try
                {
                    foreach (var it in en)
                    {
                        if (total < maxItems) sb.Append(Dump(it, depth - 1, maxItems)).Append(" | ");
                        total++;
                        if (total >= 100000) break;
                    }
                }
                catch (Exception ex) { sb.Append("!" + ex.GetType().Name); }
                return Friendly(t) + "[" + total + "]{ " + sb + "}";
            }
            var parts = new List<string>();
            foreach (var a in Accessors(t))
            {
                object v;
                try { v = a.get(o); } catch (Exception ex) { v = "!" + ex.GetType().Name; }
                parts.Add(a.name + "=" + Dump(v, depth - 1, maxItems));
            }
            return Friendly(t) + "{" + string.Join(", ", parts) + "}";
        }

        private static string FlatHeader(object o) =>
            o == null ? "null" : string.Join(";", Accessors(o.GetType()).Select(a => a.name));

        private static string FlatValues(object o)
        {
            if (o == null) return "null";
            return string.Join(";", Accessors(o.GetType()).Select(a =>
            {
                try { return Fmt(a.get(o)); } catch (Exception ex) { return "!" + ex.GetType().Name; }
            }));
        }

        private static string Clean(string s) => (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

        // ------------------------------------------------------------ utilitaires
        private static string Wall() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", IC);

        private void Api(string line)
        {
            try { _api?.WriteLine(line); } catch { }
        }

        private void Err(string where, Exception ex)
        {
            string key = where + " / " + ex.GetType().Name;
            if (_errors.TryGetValue(key, out var e)) _errors[key] = (e.count + 1, e.first);
            else _errors[key] = (1, Clean(ex.Message));
        }

        private void SafeLog(string text)
        {
            try { VAn.AddToLog(text, VolumetricaAPI.LogLevelEnum.Information); } catch { }
        }

        private static string SafeStr(Func<object> f)
        {
            try { return Convert.ToString(f(), IC) ?? ""; } catch { return "?"; }
        }
    }
}
