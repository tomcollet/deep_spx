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

var van = new FakeVAn();
var day1 = new DateTime(2026, 10, 13);
var day2 = new DateTime(2026, 10, 14);
void Add(DateTime t, double o, double h, double l, double c, long ask, long bid)
{
    van.BarVars.Add(new Structure.BarClass
    {
        exchDt = t, Index = van.BarVars.Count, Open = o, High = h, Low = l, Close = c,
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
Add(day2.AddMinutes(550), 5003, 5007, 5003, 5006, 800, 200);      // 09:10 : cassure du range d'ouverture
Add(day2.AddMinutes(551), 5006.5, 5010, 5006, 5009, 250, 200);   // barre encore ouverte a la fin de l'historique

var ind = new SetupsIndicator();
typeof(VolSysAPI.Indicators.Indicator).GetField("<VAn>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ind, van);
typeof(VolSysAPI.Indicators.Indicator).GetField("<IndVars>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
    .SetValue(ind, new VolSysAPI.Indicators.IndVarsClass());

ind.OnSet(true, false);
ind.OnLoad();
van.BarIndex = van.BarVars.Count - 1;
ind.OnEnd(false);                                                 // rejeu de l'historique

// Temps reel : la barre 09:11 se ferme, puis 09:12 et 09:13 montent jusqu'a l'objectif (plus haut de la veille 5020)
Add(day2.AddMinutes(552), 5009, 5014, 5008, 5013, 250, 200);
van.BarIndex = van.BarVars.Count - 2; ind.OnClose(true);          // ferme 09:11
van.BarIndex = van.BarVars.Count - 1; ind.OnClose(true);          // ferme 09:12
Add(day2.AddMinutes(553), 5013, 5021, 5012, 5020.5, 250, 200);
van.BarIndex = van.BarVars.Count - 1; ind.OnClose(true);          // ferme 09:13 : objectif
ind.OnEnd(true);

var lines = van.Log.Where(l => l.StartsWith(SetupsIndicator.Prefix)).ToList();
foreach (var l in lines) Console.WriteLine(l);
var panel = van.Annotations.Where(a => a.CoordinateXType == CoordinateTypeEnum.Relative).Select(a => a.Text ?? "").ToList();
Console.WriteLine("\nPanneau :");
foreach (var t in panel.Where(t => t.Length > 0)) Console.WriteLine("  | " + t);
var marks = van.Annotations.Where(a => a.CoordinateXType == CoordinateTypeEnum.Absolute).ToList();
Console.WriteLine();

var sig = lines.FirstOrDefault(l => l.StartsWith("DSPX;SIG;"));
var checks = new (string label, bool ok)[]
{
    ("Reperage par nom des indicateurs DeepGamma", lines.Any(l => l == "DSPX;MAP;envelopes=1(0/1/2) summary=10(0) netflow=15(2) pressure=7(0)")),
    ("Signal B achat sur la barre de cassure (rejeu de l'historique)", sig != null && sig.StartsWith("DSPX;SIG;1;2026-10-14 09:10;B;LONG;ShortGamma;range d'ouverture haut;5004;5006;5003;5020;")),
    ("Entree a l'ouverture suivante + 1 tick", lines.Any(l => l == "DSPX;FILL;1;2026-10-14 09:11;5006.75")),
    ("Sortie a l'objectif en temps reel : +52 ticks", lines.Any(l => l == "DSPX;EXIT;1;2026-10-14 09:13;Target;5019.75;52")),
    ("Fleche + etiquette du signal, resultat a la sortie", marks.Count(a => a.AnnotationType == AnnotationType.ArrowUp) == lines.Count(l => l.StartsWith("DSPX;SIG;") && l.Contains(";LONG;")) && marks.Any(a => a.Text == "B achat") && marks.Any(a => a.Text == "+52 t")),
    ("Panneau : regime short gamma", panel.Any(t => t.Contains("SHORT GAMMA"))),
    ("Panneau : dernier signal et son resultat", panel.Any(t => t.Contains("B #1 achat") && t.Contains("Target +52 t"))),
    ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
};
foreach (var c in checks) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
return checks.All(c => c.ok) ? 0 : 1;

/// Faux VAn : faux indicateurs DeepGamma. Envelopes Zero 5100 / Major 5060 / Minor 4970 (prix sous le Zero Gamma),
/// Summary negatif, Net Flow croissant (pente positive), MM Pressure constant.
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

    public BaseIndicatorPrms GetParamsById(int IndicatorIndex)
    {
        if (!Indicators.TryGetValue(IndicatorIndex, out var d)) return null;
        return new BaseIndicatorPrms { name = d.name, v_series = d.series.Select(n => new Series_Prop { Name = n }).ToList() };
    }

    public double[] GetSeriesValueById(int IndicatorIndex, int SubgraphIndex, int Index)
    {
        switch (IndicatorIndex)
        {
            case 0: return new[] { BarVars[Index].Close };
            case 1: return new[] { SubgraphIndex == 0 ? 5100.0 : SubgraphIndex == 1 ? 5060.0 : 4970.0 };
            case 7: return new[] { 1e8, 9e7 };
            case 10: return new[] { -2e9 };
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
    public int LineWidth { get; set; }
    public VolumetricaCore.ColorRef BackColor { get; set; }
    public string Text { get; set; }
    public VolumetricaCore.ColorRef ForeColor { get; set; }
    public int FontSize { get; set; }
    public bool FontBold { get; set; }
    public string Tooltip { get; set; }
}
