// Banc d'essai de la sonde : un faux graphique Deepchart (1 000 barres d'historique, puis 5 barres "temps reel"
// rejouees d'une seance passee), trois faux indicateurs, un carnet. Verifie les verdicts de summary.txt.
using System.Reflection;
using DeepSpx.Probe;
using VolSysAPI;
using VolumetricaAPI.Chart;
using Feed = VolumetricaAPI.Connection.Structure;
using static VolSysAPI.ExternalStructure;

var van = new FakeVAn();
var start = new DateTime(2026, 10, 14, 8, 30, 0, DateTimeKind.Unspecified);
for (int i = 0; i < 1005; i++)
    van.BarVars.Add(new Structure.BarClass
    {
        Open = 5800, High = 5801, Low = 5799, Close = 5800.25, exchDt = start.AddMinutes(i), Index = i,
        VolTotList = new List<Structure.InfoVolClass> { new Structure.InfoVolClass { TotVol = 100, AskVol = 60, BidVol = 40 } }
    });

var probe = new DeepGammaProbe { TickLogMinutes = 5, BookLogMinutes = 5 };
// VAn est en lecture seule dans l'API : le banc le renseigne par son champ
typeof(VolSysAPI.Indicators.Indicator).GetField("<VAn>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
    .SetValue(probe, van);

probe.OnSet(true, false);
probe.OnLoad();

// Historique
for (int i = 0; i < 1000; i++)
{
    van.BarIndex = i;
    probe.OnOpen(false);
    for (int k = 0; k < 3; k++)
        probe.OnTick(new Feed.TickByTick { price = 5800, Vol = 1, AggrSide = Feed.AggressorSideEnum.Ask, bid = 5799.75, ask = 5800, exDt = start.AddMinutes(i) }, false, new AggrInfo());
    probe.OnClose(false);
}
probe.OnEnd(false);

// Temps reel d'une seance passee (replay)
for (int i = 1000; i < 1005; i++)
{
    van.BarIndex = i;
    probe.OnOpen(true);
    for (int k = 0; k < 5; k++)
    {
        probe.OnTick(new Feed.TickByTick { price = 5800.25, Vol = 2, AggrSide = Feed.AggressorSideEnum.Bid, bid = 5800, ask = 5800.25, exDt = start.AddMinutes(i) }, true, new AggrInfo());
        probe.OnBook(new Feed.BookUpdate { MbpUpdates = new List<Feed.MbpUpdate> { new Feed.MbpUpdate { PinT = 23200, isAsk = false, Qty = 25 } } });
    }
    probe.OnClose(true);
    if (i == 1004) Thread.Sleep(10500);   // laisse passer le delai de 10 s du resume
    probe.OnEnd(true);
}

string dir = van.Log.First(l => l.StartsWith("Probe actif")).Split(" : ")[1];
Console.WriteLine("Dossier : " + dir);
foreach (var f in Directory.GetFiles(dir).OrderBy(f => f))
    Console.WriteLine($"  {Path.GetFileName(f),-34} {new FileInfo(f).Length,9} octets");
string summary = File.ReadAllText(Path.Combine(dir, "summary.txt"));
string api = File.ReadAllText(Path.Combine(dir, "api_dump.txt"));
Console.WriteLine("\n" + summary);

var checks = new (string label, bool ok)[]
{
    ("Q1 : 9 series lisibles", summary.Contains("Q1 lecture des autres indicateurs : OUI (9 series)")),
    ("Q2 : 3 series avec historique", summary.Contains("Q2 historique des series          : OUI (3 series")),
    ("Q3 : 25 mises a jour du carnet", summary.Contains("Q3 carnet                         : OUI (25 mises a jour)")),
    ("Q4 : 3000 trades historiques", summary.Contains("Q4 trades historiques             : OUI (3000)")),
    ("Q6 : replay detecte", summary.Contains("REPLAY probable")),
    ("Signature GetSeriesValueById relevee", api.Contains("GetSeriesValueById(System.Int32 indicator, System.Int32 series, System.Int32 index)")),
    ("Instantane du carnet ecrit", File.ReadAllText(Path.Combine(dir, "book_snapshot.txt")).Contains("MbpUpdates")),
    ("Aucune erreur", summary.Contains("Erreurs (0 types)")),
};
foreach (var c in checks) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
return checks.All(c => c.ok) ? 0 : 1;

/// Faux VAn : la surface documentee (IMethodCloudAPI) plus les deux methodes non documentees que la sonde
/// trouve par reflexion. Trois indicateurs : 1 varie sur l'historique, 2 constant, 3 seulement sur les dernieres barres.
public class FakeVAn : IMethodCloudAPI
{
    public List<Structure.BarClass> BarVars { get; } = new();
    public int BarIndex { get; set; }
    public int LastIndex() => BarVars.Count - 1;
    public string SymbolName => "ES-CME";
    public string ChartName => "SPX option chart";
    public DateTime LastExDt => DateTime.UtcNow;
    public List<string> Log = new();

    public IChartPoint AddPointToSeries(IChartSeries series, int indexBar, double yValue = 0, bool IsHidden = false, bool isBreak = false) => null;
    public IAnnotation CreateAnnotation(AnnotationType annotationType) => null;
    public IAnnGroup CreateAnnGroup() => null;
    public void AddAnnotation(IAnnGroup group, IAnnotation annotation) { }
    public PriceInfo FromPinT(int pinT) => new PriceInfo { Price = pinT * 0.25 };
    public double GetDoubleTicksDiff(double from, double to) => (to - from) / 0.25;
    public double DoubleAddSubTicks(double price, int ticks) => price + ticks * 0.25;
    public void ShowMessage(string text, string title) => Log.Add("[message] " + text);
    public void PlayAlert(string alertName) { }
    public void AddToLog(string message, VolumetricaAPI.LogLevelEnum level, string source = null, bool? showToast = null) => Log.Add(message);

    public Feed.BookUpdate RebuildDom() => new Feed.BookUpdate
    {
        NeedResetMbp = true,
        MbpUpdates = Enumerable.Range(0, 10).Select(i => new Feed.MbpUpdate { PinT = 23250 + i, isAsk = i >= 5, Qty = 10 + i }).ToList()
    };

    public object GetParamsById(int index)
    {
        if (index >= 4) throw new ArgumentOutOfRangeException(nameof(index));
        string[] names = { "Probe", "Option Summary", "Option Levels/Envelopes", "Expected Move" };
        return new Dictionary<string, string> { ["Name"] = names[index], ["Source"] = "Gex (market maker)" };
    }

    public double GetSeriesValueById(int indicator, int series, int index)
    {
        if (indicator >= 4) throw new ArgumentOutOfRangeException(nameof(indicator));
        if (series > 2) throw new ArgumentOutOfRangeException(nameof(series));
        if (indicator == 0) return double.NaN;
        if (indicator == 1) return index * 0.5 + series;
        if (indicator == 2) return 5812.25 + series;
        return index > BarIndex - 5 ? 30.0 : double.NaN;
    }
}
