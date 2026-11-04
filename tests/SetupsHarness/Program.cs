// Banc d'essai de l'indicateur de setups : faux graphique Deepchart (une seance calme, puis une cassure du range
// d'ouverture en short gamma), faux indicateurs DeepGamma aux numeros vus chez Tom le 04/11/2026 (1 Option Envelopes,
// 7 Option Market Maker Pressure, 10 Option Summary, 15 Option Net Flow). Verifie le reperage par nom, le rejeu de
// l'historique, le temps reel, le journal DSPX; et le panneau.
using System.Reflection;
using DeepSpx.Setups;
using VolSysAPI;
using VolumetricaAPI.Chart;
using Feed = VolumetricaAPI.Connection.Structure;
using static VolSysAPI.ExternalStructure;

// Le meme graphique est joue deux fois : barres a l'heure de Chicago (ouverture 08:30) puis a l'heure de New York
// (09:30, cas du graphique de Tom). L'indicateur doit reconnaitre l'horloge et donner le meme signal.
var allOk = true;
foreach (var (shift, clock) in new[] { (0, "Chicago"), (60, "NewYork") })
{
    var van = new FakeVAn();
    RunChart(van, shift, clock);
}

// Graphique de Tom du 04/11/2026 a 18h19 : Option Envelopes present, ni Option Summary ni Option Net Flow.
{
    var van = new FakeVAn();
    van.Hidden.Add(10);
    van.Hidden.Add(15);
    RunChart(van, 60, "NewYork");
}
// Etude arretee au 14/10 (StudyEndDate = 20261014) : seule la seance du 13/10 compte.
{
    var van = new FakeVAn();
    RunChart(van, 60, "NewYork", 20261014);
}
// Optimisation ouverte jusqu'au 31/12 avec un budget de 0 ms : rejeu par tranches de 200 barres sur plusieurs appels.
{
    var van = new FakeVAn();
    RunChart(van, 60, "NewYork", 0, 20261231);
}
return allOk ? 0 : 1;

void RunChart(FakeVAn van, int shift, string clock, int studyEnd = 0, int optEnd = 0)
{
    bool complete = van.Hidden.Count == 0;
    var day1 = new DateTime(2026, 10, 13);
    var day2 = new DateTime(2026, 10, 14);
    void Add(DateTime t, double o, double h, double l, double c, long ask, long bid)
    {
        van.BarVars.Add(new Structure.BarClass
        {
            exchDt = t.AddMinutes(shift), Index = van.BarVars.Count, Open = o, High = h, Low = l, Close = c,
            OpenInT = (int)(o / 0.25), HighInT = (int)(h / 0.25), LowInT = (int)(l / 0.25), CloseInT = (int)(c / 0.25),
            VolTotList = new[] { new Structure.InfoVolClass { TotVol = ask + bid, AskVol = ask, BidVol = bid, VwapPrice = (h + l + c) / 3 } }
        });
    }
    var rnd = new Random(7);
    for (int m = 0; m < 390; m++)
        Add(day1.AddMinutes(510 + m), 5000, m == 120 ? 5020 : 5001, m == 200 ? 4980 : 4999, 5000, 100 + rnd.Next(0, 40), 100 + rnd.Next(0, 40));
    for (int m = 0; m < 30; m++) Add(day2.AddMinutes(510 + m), 5002, 5004, 5000, 5002, 210, 200);
    for (int m = 30; m < 40; m++) Add(day2.AddMinutes(510 + m), 5002, 5003, 5001, 5002, 210, 200);
    int signalBar = van.BarVars.Count;
    Add(day2.AddMinutes(550), 5003, 5007, 5003, 5006, 800, 200);      // 09:10 Chicago : cassure du range d'ouverture
    Add(day2.AddMinutes(551), 5006.5, 5010, 5006, 5009, 250, 200);   // barre encore ouverte a la fin de l'historique

    var ind = new SetupsIndicator();
    if (studyEnd > 0) { ind.StudyEndDate = studyEnd; ind.ShowStudy = true; }
    if (optEnd > 0) { ind.OptEndDate = optEnd; ind.OptBudgetMs = 0; }
    typeof(VolSysAPI.Indicators.Indicator).GetField("<VAn>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ind, van);
    typeof(VolSysAPI.Indicators.Indicator).GetField("<IndVars>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(ind, new VolSysAPI.Indicators.IndVarsClass());

    ind.OnSet(true, false);
    ind.OnLoad();
    van.BarIndex = van.BarVars.Count - 1;
    ind.OnEnd(false);                                                 // rejeu de l'historique

    // Temps reel : la barre suivant la cassure se ferme, puis les deux suivantes montent jusqu'a l'objectif (plus haut de la veille 5020)
    Add(day2.AddMinutes(552), 5009, 5014, 5008, 5013, 250, 200);
    van.BarIndex = van.BarVars.Count - 2; ind.OnClose(true);
    van.BarIndex = van.BarVars.Count - 1; ind.OnClose(true);
    Add(day2.AddMinutes(553), 5013, 5021, 5012, 5020.5, 250, 200);
    van.BarIndex = van.BarVars.Count - 1; ind.OnClose(true);          // objectif
    ind.OnEnd(true);

    string T(int minute) => day2.AddMinutes(minute + shift).ToString("yyyy-MM-dd HH:mm");
    var lines = van.Log.Where(l => l.StartsWith(SetupsIndicator.Prefix)).ToList();
    Console.WriteLine("==== Barres a l'heure de " + clock);
    foreach (var l in lines) Console.WriteLine(l);
    var panel = van.Annotations.Where(a => a.CoordinateXType == CoordinateTypeEnum.Relative).Select(a => a.Text ?? "").ToList();
    Console.WriteLine("\nPanneau :");
    foreach (var t in panel.Where(t => t.Length > 0)) Console.WriteLine("  | " + t);
    var marks = van.Annotations.Where(a => a.CoordinateXType == CoordinateTypeEnum.Absolute).ToList();
    Console.WriteLine();

    var sig = lines.FirstOrDefault(l => l.StartsWith("DSPX;SIG;"));
    if (optEnd > 0)
    {
        var o = new (string label, bool ok)[]
        {
            ("Optimisation par tranches : terminee, 2 seances, 64 configurations", lines.Any(l => l.StartsWith("DSPX;OPTINFO;sessions=2;") && l.Contains("configs=64"))),
            ("Optimisation : toute la grille en recherche (64 lignes OPTALL)", lines.Count(l => l.StartsWith("DSPX;OPTALL;")) == 64),
            ("Optimisation : moins de 30 trades -> rien de classe, dit dans le panneau",
                panel.Count(t => t.Contains(" : aucune configuration a 30 trades ou plus en recherche")) == 3),
            ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
        };
        Console.WriteLine("(optimisation ouverte jusqu'au 31/12, par tranches)");
        foreach (var c in o) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
        Console.WriteLine();
        allOk &= o.All(c => c.ok);
        return;
    }
    if (studyEnd > 0)
    {
        var opened = new (string label, bool ok)[]
        {
            ("StudyEndDate respecte : 1 seance comptee", lines.Any(l => l.StartsWith("DSPX;STUDYINFO;sessions=1;") && l.Contains("end_exclusive=2026-10-14"))),
            ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
        };
        Console.WriteLine("(etude arretee au 14/10)");
        foreach (var c in opened) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
        Console.WriteLine();
        allOk &= opened.All(c => c.ok);
        return;
    }
    if (!complete)
    {
        var partial = new (string label, bool ok)[]
        {
            ("Sans Summary ni Net Flow : reperage", lines.Any(l => l == "DSPX;MAP;envelopes=1(0/1/2) summary=-1(0) netflow=-1(2) pressure=7(0)")),
            ("Sans Net Flow : pas de setup B", sig == null),
            ("Sans Summary : regime lu sur le Zero Gamma (prix dessous -> short gamma)", panel.Any(t => t.Contains("SHORT GAMMA"))),
            ("Panneau : avertissement Net Flow et Summary absents", panel.Any(t => t.StartsWith("ATTENTION") && t.Contains("Net Flow absent") && t.Contains("Summary absent"))),
            ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
        };
        Console.WriteLine("(graphique sans Option Summary ni Option Net Flow)");
        foreach (var c in partial) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
        Console.WriteLine();
        allOk &= partial.All(c => c.ok);
        return;
    }
    var checks = new (string label, bool ok)[]
    {
        ("Horloge des barres reconnue : " + clock, lines.Any(l => l.StartsWith("DSPX;CLOCK;" + clock + ";detecte;"))),
        ("Reperage par nom des indicateurs DeepGamma", lines.Any(l => l == "DSPX;MAP;envelopes=1(0/1/2) summary=10(0) netflow=15(2) pressure=7(0)")),
        ("Signal B achat sur la barre de cassure (rejeu de l'historique)", sig != null && sig.StartsWith("DSPX;SIG;1;" + T(550) + ";B;LONG;ShortGamma;range d'ouverture haut;5004;5006;5003;5020;")),
        ("Entree a l'ouverture suivante + 1 tick", lines.Any(l => l == "DSPX;FILL;1;" + T(551) + ";5006.75")),
        ("Sortie a l'objectif en temps reel : +52 ticks", lines.Any(l => l == "DSPX;EXIT;1;" + T(553) + ";Target;5019.75;52")),
        ("Summary lu 5 barres avant le signal (pas de vue sur l'avenir en historique)",
            sig != null && sig.Split(';')[18] == FakeVAn.SummaryAt(signalBar - 5).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
        ("Etude scalps close : desactivee par defaut (ni panneau ni lignes STUDY)",
            !lines.Any(l => l.StartsWith("DSPX;STUDY")) && !panel.Any(t => t.StartsWith("Etude scalps"))),
        ("Optimisation A/B/C : holdout d'A/B/C scelle par defaut (seances d'octobre non comptees)",
            lines.Any(l => l.StartsWith("DSPX;OPTINFO;sessions=0;") && l.Contains("end_exclusive=2026-08-16")) &&
            panel.Any(t => t.StartsWith("Optimisation A/B/C : aucune seance avec DeepGamma avant le 16/08/2026"))),
        ("Ligne STATE en temps reel (horloge, reperage, regime)", lines.Any(l => l.StartsWith("DSPX;STATE;") && l.Contains(";horloge=" + clock + ";") &&
            l.Contains("map=envelopes=1(0/1/2)") && l.Contains("regime=ShortGamma"))),
        ("Lignes LAG en temps reel (Summary a 0 sur la barre qui se ferme)", lines.Any(l => l.StartsWith("DSPX;LAG;") && l.Contains("summary=0|0|0|-"))),
        ("Fleche + etiquette du signal, resultat a la sortie", marks.Count(a => a.AnnotationType == AnnotationType.ArrowUp) == lines.Count(l => l.StartsWith("DSPX;SIG;") && l.Contains(";LONG;")) && marks.Any(a => a.Text == "B achat") && marks.Any(a => a.Text == "+52 t")),
        ("Panneau : regime short gamma", panel.Any(t => t.Contains("SHORT GAMMA"))),
        ("Panneau : heure des barres et horaires de seance", panel.Any(t => t.StartsWith("Heure des barres : " + (clock == "Chicago" ? "Chicago" : "New York") + " (detecte)") &&
            t.Contains(clock == "Chicago" ? "08:30-15:00" : "09:30-16:00"))),
        ("Panneau : dernier signal et son resultat", panel.Any(t => t.Contains("B #1 achat") && t.Contains("Target +52 t"))),
        ("Panneau : aucun indicateur DeepGamma manquant", !panel.Any(t => t.StartsWith("ATTENTION"))),
        ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
    };
    foreach (var c in checks) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
    Console.WriteLine();
    allOk &= checks.All(c => c.ok);
}

/// Faux VAn : faux indicateurs DeepGamma. Envelopes Zero ~5100 / Major 5060 / Minor 4970 (prix sous le Zero Gamma),
/// Summary negatif qui bouge, Net Flow croissant (pente positive), MM Pressure constant.
public class FakeVAn : IMethodCloudAPI
{
    public List<Structure.BarClass> BarVars { get; } = new();
    public int BarIndex { get; set; }
    public int LastIndex() => BarVars.Count - 1;
    public string SymbolName => "ES-CME";
    public string SymbolFullName => "ES-CME 12-26";
    public string ChartName => "SPX option chart";
    public DateTime LastExDt => BarVars.Count > 0 ? BarVars[^1].exchDt : DateTime.MinValue;
    public bool IsReplayData => false;
    public Structure.ChartPhaseEnum ChartPhase => Structure.ChartPhaseEnum.Realtime;
    public List<string> Log = new();
    public List<FakeAnn> Annotations = new();
    public HashSet<int> Hidden = new();

    public IChartPoint AddPointToSeries(IChartSeries Series, int IndexBar, double YValue = 0, bool IsHidden = false, bool IsBreak = false) => null;
    public IAnnotation CreateAnnotation(AnnotationType annType) { var a = new FakeAnn { AnnotationType = annType }; Annotations.Add(a); return a; }
    public IAnnGroup CreateAnnGroup() => null;
    public void AddAnnotation(IAnnGroup AnnGroup, IAnnotation NewAnn) { }
    public void SetCalculateAggrTrades() { }
    public DoubleP FromPinT(int pInT) => new DoubleP { Price = pInT * 0.25 };
    public int GetDoubleTicksDiff(double IniPrice, double EndPrice) => (int)Math.Round((EndPrice - IniPrice) / 0.25);
    public double DoubleAddSubTicks(double Price, int Tick) => Price + Tick * 0.25;
    public void ShowMessage(string Text, string Title) => Log.Add("[message] " + Text);
    public void PlayAlert(string AlertName) { }
    public void AddToLog(string message, VolumetricaAPI.LogLevelEnum logLevel = VolumetricaAPI.LogLevelEnum.Information, string source = null, bool? showToast = null) => Log.Add(message);
    public Feed.BookUpdate RebuildDom() => new Feed.BookUpdate();

    private static readonly Dictionary<int, (string name, string[] series)> Indicators = new()
    {
        [0] = ("Price", new[] { "Price" }),
        [1] = ("Option Envelopes", new[] { "Zero Gamma", "Major Gamma", "Minor Gamma" }),
        [7] = ("Option Market Maker Pressure", new[] { "MM Pressure", "Average 1" }),
        [10] = ("Option Summary", new[] { "Regime" }),
        [15] = ("Option Net Flow", new[] { "Call", "Put", "Difference" }),
        [17] = ("DeepSpx - Setups gamma x order flow", new string[0]),
    };

    public static double SummaryAt(int index) => -2e9 - index * 1e6;

    public BaseIndicatorPrms GetParamsById(int IndicatorIndex)
    {
        if (Hidden.Contains(IndicatorIndex) || !Indicators.TryGetValue(IndicatorIndex, out var d)) return null;
        return new BaseIndicatorPrms { name = d.name, v_series = d.series.Select(n => new Series_Prop { Name = n }).ToList() };
    }

    public double[] GetSeriesValueById(int IndicatorIndex, int SubgraphIndex, int Index)
    {
        if (Hidden.Contains(IndicatorIndex)) return null;
        switch (IndicatorIndex)
        {
            case 0: return new[] { BarVars[Index].Close };
            // Zero Gamma derive un peu a chaque barre, comme sur le graphique de Tom (7714,67 -> 7715,00 en 10 minutes)
            case 1: return new[] { SubgraphIndex == 0 ? 5100.0 + Index * 0.001 : SubgraphIndex == 1 ? 5060.0 : 4970.0 };
            // Summary et Pressure : 0 sur les 3 dernieres barres, comme chez Tom (remplis avec ~3 minutes de retard)
            case 7: return new[] { Index > LastIndex() - 3 ? 0.0 : 1e8 + Index, 9e7 };
            case 10: return new[] { Index > LastIndex() - 3 ? 0.0 : SummaryAt(Index) };
            case 15: return new[] { SubgraphIndex == 2 ? 1000.0 + Index : 0.0 };
            default: return null;
        }
    }
}

public class FakeAnn : IAnnotation
{
    public AnnotationType AnnotationType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public CoordinateTypeEnum CoordinateXType { get; set; }
    public CoordinateTypeEnum CoordinateYType { get; set; }
    public ChartAreaRef Area { get; set; }
    public bool Visible { get; set; }
    public bool PlotForeground { get; set; }
    public VolumetricaCore.ColorRef LineColor { get; set; }
    public float LineWidth { get; set; }
    public VolumetricaCore.ColorRef BackColor { get; set; }
    public string Text { get; set; }
    public VolumetricaCore.ColorRef ForeColor { get; set; }
    public float FontSize { get; set; }
    public bool FontBold { get; set; }
    public string Tooltip { get; set; }
}
