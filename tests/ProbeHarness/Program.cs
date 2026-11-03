// Banc d'essai de la sonde : un faux graphique Deepchart (1 000 barres d'historique, puis 5 barres "temps reel"
// d'un replay), trois faux indicateurs, un carnet. La sonde ecrit dans le log (AddToLog) : le banc lit ces lignes et
// verifie les verdicts.
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

var probe = new DeepGammaProbe();
// VAn est en lecture seule dans l'API : le banc le renseigne par son champ (la reflexion est ici, pas dans la sonde)
typeof(VolSysAPI.Indicators.Indicator).GetField("<VAn>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
    .SetValue(probe, van);

probe.OnSet(true, false);
probe.OnLoad();

// Historique
for (int i = 0; i < 1000; i++)
{
    van.BarIndex = i;
    van.LastExDt = start.AddMinutes(i);
    probe.OnOpen(false);
    for (int k = 0; k < 3; k++)
        probe.OnTick(new Feed.TickByTick { price = 5800, Vol = 1, AggrSide = Feed.AggressorSideEnum.Ask, bid = 5799.75, ask = 5800, exDt = start.AddMinutes(i) }, false, new AggrInfo());
    probe.OnClose(false);
}
probe.OnEnd(false);

// Temps reel d'une seance passee (replay). L'indicateur 1 renvoie 0 en temps reel, comme dans l'issue #4.
van.Live = true;
for (int i = 1000; i < 1005; i++)
{
    van.BarIndex = i;
    van.LastExDt = start.AddMinutes(i);
    probe.OnOpen(true);
    for (int k = 0; k < 5; k++)
    {
        probe.OnTick(new Feed.TickByTick { price = 5800.25, Vol = 2, AggrSide = Feed.AggressorSideEnum.Bid, bid = 5800, ask = 5800.25, exDt = start.AddMinutes(i) }, true, new AggrInfo());
        probe.OnBook(new Feed.BookUpdate { MbpUpdates = new List<Feed.MbpUpdate> { new Feed.MbpUpdate { PinT = 23200, isAsk = false, Qty = 25 } } });
    }
    probe.OnClose(true);
    if (i == 1004) van.LastExDt = start.AddMinutes(i + 10);   // force le resume final (toutes les 5 min de bourse)
    probe.OnEnd(true);
}

var lines = van.Log.Where(l => l.StartsWith(DeepGammaProbe.Prefix)).ToList();
Console.WriteLine($"{lines.Count} lignes {DeepGammaProbe.Prefix} dans le log");
foreach (var g in lines.GroupBy(l => l.Split(';')[1]).OrderBy(g => g.Key))
    Console.WriteLine($"  {g.Key,-10} {g.Count(),6}");
string Last(string q) => lines.Last(l => l.StartsWith(DeepGammaProbe.Prefix + "VERDICT;" + q + ";"));
Console.WriteLine();
foreach (var q in new[] { "Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7" }) Console.WriteLine(Last(q));
Console.WriteLine();

var checks = new (string label, bool ok)[]
{
    ("Q1 : 9 series lisibles", Last("Q1").EndsWith("OUI 9 series lisibles")),
    ("Q2 : 3 series avec historique", Last("Q2").Contains("OUI 3 series")),
    ("Q3 : 25 mises a jour, 6 niveaux bid, 5 ask, 54 ticks", Last("Q3").EndsWith("OUI 25 mises a jour, jusqu'a 6 niveaux bid / 5 ask, 54 ticks de profondeur")),
    ("Q4 : 3000 trades historiques", Last("Q4").EndsWith("OUI 3000 trades historiques")),
    ("Q6 : replay vu par IsReplayData", Last("Q6").EndsWith("replay=True")),
    ("Q7 : 15 zeros sur 45 lectures temps reel", Last("Q7").EndsWith("15 valeurs nulles sur 45 lectures temps reel")),
    ("Catalogue : indicateur 1 serie 0", lines.Any(l => l.StartsWith("DSPX;CAT;1;0;"))),
    ("Instantane du carnet", lines.Any(l => l.StartsWith("DSPX;BOOKSNAP;try=1;reset=True,levels=10,"))),
    ("Statistiques par minute", lines.Count(l => l.StartsWith("DSPX;BOOKMIN;")) == 5),
    ("Aucune erreur", !lines.Any(l => l.StartsWith("DSPX;ERR;"))),
};
foreach (var c in checks) Console.WriteLine((c.ok ? "OK    " : "ECHEC ") + c.label);
return checks.All(c => c.ok) ? 0 : 1;

/// Faux VAn : la surface documentee. Trois indicateurs : 1 varie sur l'historique (0 en temps reel), 2 constant,
/// 3 seulement sur les dernieres barres.
public class FakeVAn : IMethodCloudAPI
{
    public List<Structure.BarClass> BarVars { get; } = new();
    public int BarIndex { get; set; }
    public int LastIndex() => BarVars.Count - 1;
    public string SymbolName => "ES-CME";
    public string ChartName => "SPX option chart";
    public DateTime LastExDt { get; set; }
    public bool IsReplayData => true;
    public object ChartPhase => Live ? "Realtime" : "History";
    public bool Live;
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

    public double GetSeriesValueById(int indicator, int series, int index)
    {
        if (indicator >= 4) throw new ArgumentOutOfRangeException(nameof(indicator));
        if (series > 2) throw new ArgumentOutOfRangeException(nameof(series));
        if (indicator == 0) return double.NaN;
        if (indicator == 1) return Live ? 0.0 : index * 0.5 + series;
        if (indicator == 2) return 5812.25 + series;
        return index > BarIndex - 5 ? 30.0 : double.NaN;
    }
}
