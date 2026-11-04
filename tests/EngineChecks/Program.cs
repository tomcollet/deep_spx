// Scenarios synthetiques du moteur de setups (src/DeepSpxSetups/Engine), hors de Deepchart.
// Lancement : dotnet run --project tests/EngineChecks (code de sortie 1 si un controle echoue).
using DeepSpx.Engine;

var failures = 0;
void Check(string label, bool ok, string detail = "")
{
    Console.WriteLine((ok ? "OK    " : "ECHEC ") + label + (ok || detail.Length == 0 ? "" : "  -> " + detail));
    if (!ok) failures++;
}

// ---------------------------------------------------------------- outils
// Les scenarios 1 a 8 sont ecrits a l'heure de Chicago (seance 08:30-15:00) : reglages decales de -60 minutes.
// Le scenario 9 verifie le reglage par defaut, a l'heure de New York.
EngineSettings Chicago()
{
    var st = new EngineSettings();
    st.ShiftClock(-60);
    return st;
}

// Sans la regle de risque de Tom (25 ticks, 1,5R) : pour tester la mecanique d'un setup seule
EngineSettings ChicagoNoRiskRule()
{
    var st = Chicago();
    st.MaxRiskTicks = 100000;
    st.MinRewardRisk = 0;
    return st;
}

EngineSettings ChicagoLimited()
{
    var st = Chicago();
    st.MaxTradesPerDay = 1;
    return st;
}

var day1 = new DateTime(2026, 10, 13);
var day2 = new DateTime(2026, 10, 14);

// Seance reguliere calme (08:30-15:00 Chicago) : prix 5000, petits deltas, un pic a 5020 et un creux a 4980
List<BarInput> CalmDay(DateTime d, Func<int, GammaInput> gamma, ref int index)
{
    var list = new List<BarInput>();
    var rnd = new Random(7);
    for (int m = 0; m < 390; m++)
    {
        double high = m == 120 ? 5020 : 5001, low = m == 200 ? 4980 : 4999;
        list.Add(new BarInput
        {
            Index = index, Time = d.AddMinutes(8 * 60 + 30 + m), Open = 5000, High = high, Low = low, Close = 5000,
            Volume = 200, AskVolume = 100 + rnd.Next(0, 40), BidVolume = 100 + rnd.Next(0, 40), Gamma = gamma(index)
        });
        index++;
    }
    return list;
}

BarInput Bar(DateTime d, int minuteOfDay, double o, double h, double l, double c, long delta, GammaInput g, ref int index)
{
    var b = new BarInput
    {
        Index = index, Time = d.AddMinutes(minuteOfDay), Open = o, High = h, Low = l, Close = c,
        Volume = 400 + Math.Abs(delta), AskVolume = 200 + Math.Max(0, delta), BidVolume = 200 + Math.Max(0, -delta), Gamma = g
    };
    index++;
    return b;
}

// Summary bouge un peu a chaque barre (comme le vrai : 242 Md puis 281 Md en 50 minutes le 04/11/2026), sans changer
// de signe ; il reste fige si le Net Flow l'est.
GammaInput G(double zero, double major, double minor, double summary, double netFlow) =>
    new GammaInput { ZeroGamma = zero, MajorGamma = major, MinorGamma = minor, SummaryRegime = summary == 0 ? 0 : summary + netFlow, NetFlow = netFlow, MmPressure = 1 };

List<Signal> Run(SetupEngine e, IEnumerable<BarInput> bars)
{
    var fills = new List<Signal>();
    foreach (var b in bars) { e.OnBar(b); fills.AddRange(e.NewFills); }
    return fills;
}

// ================================================================ 1. Regime
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(4900, 5060, 4970, 1e9, nf += 1), ref idx));
    Check("Regime : prix au-dessus du Zero Gamma, Summary positif -> LONG GAMMA", e.Regime == Regime.LongGamma, e.Regime + " " + e.RegimeReason);

    var e2 = new SetupEngine(Chicago());
    idx = 0; nf = 0;
    Run(e2, CalmDay(day1, i => G(4900, 5060, 4970, -1e9, nf += 1), ref idx));
    Check("Regime : Summary negatif -> SHORT GAMMA", e2.Regime == Regime.ShortGamma, e2.Regime + " " + e2.RegimeReason);

    var e3 = new SetupEngine(Chicago());
    idx = 0; nf = 0;
    Run(e3, CalmDay(day1, i => G(5100, 5060, 4970, 1e9, nf += 1), ref idx));
    Check("Regime : prix sous le Zero Gamma -> SHORT GAMMA", e3.Regime == Regime.ShortGamma, e3.Regime + " " + e3.RegimeReason);

    var e4 = new SetupEngine(Chicago());
    idx = 0; nf = 0;
    Run(e4, CalmDay(day1, i => G(5000.25, 5060, 4970, 1e9, nf += 1), ref idx));
    Check("Regime : prix a moins de 0,5 ATR5 du Zero Gamma -> TRANSITION", e4.Regime == Regime.Transition, e4.Regime + " " + e4.RegimeReason);

    var e5 = new SetupEngine(Chicago());
    idx = 0;
    Run(e5, CalmDay(day1, i => G(4900, 5060, 4970, 1e9, 42), ref idx));
    Check("Regime : DeepGamma fige plus de 45 barres -> INCONNU", e5.Regime == Regime.Unknown && e5.GammaStale, e5.Regime + " " + e5.RegimeReason);

    var e6 = new SetupEngine(Chicago());
    idx = 0; nf = 0;
    Run(e6, CalmDay(day1, i => i < 300 ? G(4900, 5060, 4970, 1e9, nf += 1) : G(0, 0, 0, 0, 0), ref idx));
    Check("Regime : DeepGamma a 0 en live (issue #4) pendant 90 barres -> INCONNU", e6.Regime == Regime.Unknown, e6.Regime + " " + e6.RegimeReason);
    Check("Aucun signal sur une seance calme", e.Signals.Count + e2.Signals.Count + e3.Signals.Count == 0, (e.Signals.Count + e2.Signals.Count + e3.Signals.Count).ToString());
}

// ================================================================ 2. Setup B : cassure du range d'ouverture en short gamma
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    for (int m = 0; m < 30; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    for (int m = 30; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5003, 5001, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    int signalIndex = idx;
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // 09:10 : cassure
    bars.Add(Bar(day2, 551, 5006.5, 5010, 5006, 5009, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 552, 5009, 5014, 5008, 5013, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 553, 5013, 5021, 5012, 5020.5, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var fills = Run(e, bars);
    var s = e.Signals.FirstOrDefault();
    Check("B : un signal d'achat a la cloture de la barre de cassure", s != null && s.Kind == SetupKind.Continuation && s.Side == Side.Long && s.BarIndex == signalIndex,
        s == null ? "aucun signal" : s.Kind + " " + s.Side + " barre " + s.BarIndex);
    if (s != null)
    {
        Check("B : niveau = range d'ouverture haut (5004)", s.LevelName == "range d'ouverture haut" && s.Level == 5004, s.LevelName + " " + s.Level);
        Check("B : stop 5003, objectif = plus haut de la veille 5020", s.Stop == 5003 && s.Target == 5020, s.Stop + " / " + s.Target);
        Check("B : entree a l'ouverture suivante + 1 tick (pas d'anticipation)", s.EntryBar == signalIndex + 1 && s.EntryPrice == 5006.75, s.EntryBar + " @ " + s.EntryPrice);
        Check("B : sortie a l'objectif, +52 ticks apres glissement", s.Closed && s.Exit == ExitReason.Target && s.ResultTicks == 52, s.Exit + " " + s.ResultTicks);
        Check("B : statistiques mises a jour", e.StatsB.Trades == 1 && e.StatsB.Wins == 1 && e.StatsAll.Trades == 1, e.StatsB.Trades + "/" + e.StatsB.Wins);
    }
}

// ================================================================ 3. Setup B sans confirmation du Net Flow : pas de signal
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 1000;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf -= 1), ref idx));   // Net Flow en baisse
    var bars = new List<BarInput>();
    for (int m = 0; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf -= 1), ref idx));
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf -= 1), ref idx));
    Run(e, bars);
    Check("B : Net Flow en baisse -> pas d'achat sur la cassure", e.Signals.Count == 0, e.Signals.Count + " signal(s)");
}

// ================================================================ 4. Stop prioritaire si stop et objectif dans la meme barre
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    for (int m = 0; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 551, 5006.5, 5025, 4995, 5010, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // touche les deux
    Run(e, bars);
    var s = e.Signals.FirstOrDefault();
    Check("Sortie : stop et objectif dans la meme barre -> stop", s != null && s.Exit == ExitReason.Stop && s.ExitPrice == 5002.75,
        s == null ? "aucun signal" : s.Exit + " @ " + s.ExitPrice);
    Check("Sortie : resultat -16 ticks (5002,75 - 5006,75)", s != null && s.ResultTicks == -16, s == null ? "" : s.ResultTicks.ToString());
}

// ================================================================ 5. Limite de trades par jour
{
    var e = new SetupEngine(ChicagoLimited());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    for (int m = 0; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 551, 5006.5, 5006.5, 5002.5, 5003, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // stop
    for (int m = 52; m < 60; m++) bars.Add(Bar(day2, 510 + m, 5003, 5004, 5002, 5003, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 570, 5003, 5008, 5003, 5007, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // nouvelle cassure
    Run(e, bars);
    Check("Limite : 1 trade par jour -> la seconde cassure est ignoree", e.Signals.Count == 1, e.Signals.Count + " signal(s), bloque : " + e.Blocked);
}

// ================================================================ 6. Setup A : rejet du Major Gamma en long gamma
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(4900, 5020, 4970, 1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    double px = 5000;
    for (int m = 0; m < 60; m++)
    {
        double o = px; px += 0.25;
        bars.Add(Bar(day2, 510 + m, o, px + 0.5, o - 0.5, px, 10, G(4900, 5020, 4970, 1e9, nf += 1), ref idx));
    }
    // 09:30 : px = 5015 ; montee jusqu'au mur
    for (int m = 60; m < 70; m++)
    {
        double o = px; px += 0.25;
        bars.Add(Bar(day2, 510 + m, o, px + 0.5, o - 0.5, px, 10, G(4900, 5020, 4970, 1e9, nf += 1), ref idx));
    }
    int signalIndex = idx;
    bars.Add(Bar(day2, 580, px, 5020.25, px - 0.25, 5018.5, 700, G(4900, 5020, 4970, 1e9, nf += 1), ref idx));   // contact, absorption
    for (int m = 71; m < 100; m++)
    {
        double o = px; px -= 0.5;
        bars.Add(Bar(day2, 510 + m, o, o + 0.25, px - 0.25, px, -10, G(4900, 5020, 4970, 1e9, nf += 1), ref idx));
    }
    Run(e, bars);
    var s = e.Signals.FirstOrDefault();
    Check("A : vente au contact du Major Gamma avec achats absorbes", s != null && s.Kind == SetupKind.WallReject && s.Side == Side.Short && s.BarIndex == signalIndex,
        s == null ? "aucun signal (" + e.Blocked + ", regime " + e.Regime + ")" : s.Kind + " " + s.Side + " barre " + s.BarIndex);
    if (s != null)
    {
        Check("A : niveau Major Gamma 5020, objectif = VWAP sous le prix", s.LevelName == "Major Gamma" && s.Target < s.SignalClose, s.LevelName + " objectif " + s.Target);
        Check("A : stop au-dessus du plus haut", s.Stop > 5020.25, s.Stop.ToString());
        Check("A : sortie a l'objectif (VWAP)", s.Closed && s.Exit == ExitReason.Target && s.ResultTicks > 0, s.Exit + " " + s.ResultTicks);
    }
}

// ================================================================ 7. Setup C : aimant de fin de seance, et FLAT a 14:45
// Mecanique testee sans la regle de risque : avec un stop a 1 ATR5 au-dela de l'extreme et le strike pour objectif, ce
// signal depasse 25 ticks de risque et n'atteint pas 1,5R.
for (int pass = 0; pass < 2; pass++)
{
    bool withRule = pass == 1;
    var e = new SetupEngine(withRule ? Chicago() : ChicagoNoRiskRule());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(4900, 5000, 4950, 1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    // Barres de 2 points d'amplitude jusqu'a 13:30, prix 5004 (au-dessus du strike 5000)
    for (int m = 0; m < 300; m++)
        bars.Add(Bar(day2, 510 + m, 5004, 5005, 5003, 5004, 10, G(4900, 5000, 4950, 1e9, nf += 1), ref idx));
    int signalIndex = idx;
    bars.Add(Bar(day2, 810, 5004, 5006, 5003, 5004.5, 700, G(4900, 5000, 4950, 1e9, nf += 1), ref idx));   // 13:30
    for (int m = 301; m < 380; m++)
        bars.Add(Bar(day2, 510 + m, 5004, 5005, 5003, 5004, 10, G(4900, 5000, 4950, 1e9, nf += 1), ref idx));   // ne revient pas
    Run(e, bars);
    var s = e.Signals.FirstOrDefault(x => x.Kind == SetupKind.Pin);
    if (withRule)
    {
        Check("Regle de risque : le meme signal C est refuse (risque > 25 ticks ou objectif < 1,5R)", s == null && e.RejectedRisk + e.RejectedReward >= 1,
            s == null ? "risque " + e.RejectedRisk + ", objectif " + e.RejectedReward : "signal accepte");
        continue;
    }
    Check("C : vente vers le strike aimant (Major Gamma 5000)", s != null && s.Side == Side.Short && s.Target == 5000 && s.BarIndex == signalIndex,
        s == null ? "aucun signal C (" + e.Blocked + ", regime " + e.Regime + ")" : s.Side + " objectif " + s.Target + " barre " + s.BarIndex);
    if (s != null)
        Check("C : FLAT obligatoire a 14:45", s.Closed && s.Exit == ExitReason.SessionEnd && s.ExitTime.Hour == 14 && s.ExitTime.Minute == 45,
            s.Exit + " a " + s.ExitTime.ToString("HH:mm"));
    Check("Limites : signal C compte dans les trades du jour", e.TradesToday >= 1, e.TradesToday.ToString());
}

// ================================================================ 8. Pas d'entree les 5 premieres minutes ni hors seance
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    bars.Add(Bar(day2, 300, 5000, 5010, 5000, 5009, 900, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // 05:00 : hors seance
    bars.Add(Bar(day2, 511, 5009, 5030, 5009, 5025, 900, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));   // 08:31 : casse la veille
    Run(e, bars);
    Check("Filtres : aucun signal hors seance ni dans les 5 premieres minutes", e.Signals.Count == 0, e.Signals.Count + " signal(s)");
}

// ================================================================ 9. Heure de New York (reglage par defaut)
// Meme cassure que le scenario 2, horodatee a New York : meme signal, une heure plus tard sur l'horloge.
// Les barres a l'heure de Chicago lues avec les reglages de New York ne donnent pas ce signal (09:10 = avant 09:30).
{
    SetupEngine RunB(EngineSettings st, int shift)
    {
        var e = new SetupEngine(st);
        int idx = 0;
        double nf = 0;
        foreach (var b in CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx)) { b.Time = b.Time.AddMinutes(shift); e.OnBar(b); }
        var bars = new List<BarInput>();
        for (int m = 0; m < 30; m++) bars.Add(Bar(day2, 510 + shift + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        for (int m = 30; m < 40; m++) bars.Add(Bar(day2, 510 + shift + m, 5002, 5003, 5001, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        bars.Add(Bar(day2, 550 + shift, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        bars.Add(Bar(day2, 551 + shift, 5006.5, 5010, 5006, 5009, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        bars.Add(Bar(day2, 552 + shift, 5009, 5014, 5008, 5013, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        bars.Add(Bar(day2, 553 + shift, 5013, 5021, 5012, 5020.5, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
        Run(e, bars);
        return e;
    }
    var ny = RunB(new EngineSettings(), 60);
    var s = ny.Signals.FirstOrDefault();
    Check("New York : seance 09:30-16:00, aimant des 14:00, FLAT 15:45", new EngineSettings().SessionText() == "09:30-16:00, aimant des 14:00, FLAT 15:45",
        new EngineSettings().SessionText());
    Check("New York : meme signal B a 10:10, meme resultat +52 ticks", s != null && s.Time.Hour == 10 && s.Time.Minute == 10 && s.Level == 5004 && s.ResultTicks == 52,
        s == null ? "aucun signal" : s.Time.ToString("HH:mm") + " " + s.Level + " " + s.ResultTicks);
    var wrong = RunB(new EngineSettings(), 0);
    Check("Barres de Chicago lues a l'heure de New York : pas de signal a 09:10", !wrong.Signals.Any(x => x.Time.Hour == 9 && x.Time.Minute == 10),
        string.Join(", ", wrong.Signals.Select(x => x.Time.ToString("HH:mm"))));
}

// ================================================================ 10. Summary et Net Flow a 0 en live (issue #4), niveaux encore vivants
// Summary negatif dans l'historique, puis 0 : son signe ne doit plus decider du regime. Net Flow a 0 : plus de pente.
{
    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0, zg = 4900;
    Run(e, CalmDay(day1, i => G(4900, 5060, 4970, -1e9, nf += 1), ref idx));
    Check("Summary vivant et negatif -> SHORT GAMMA", e.Regime == Regime.ShortGamma && !e.SummaryStale, e.Regime + " " + e.RegimeReason);
    var live = new List<BarInput>();
    for (int m = 0; m < 60; m++)
        live.Add(Bar(day2, 510 + m, 5000, 5001, 4999, 5000, 10, new GammaInput { ZeroGamma = zg += 0.01, MajorGamma = 5060, MinorGamma = 4970, SummaryRegime = 0, NetFlow = 0, MmPressure = 0 }, ref idx));
    Run(e, live);
    Check("Summary a 0 depuis 60 barres -> ignore, regime lu sur le Zero Gamma (LONG GAMMA)", e.SummaryStale && e.Regime == Regime.LongGamma && e.RegimeReason.Contains("Summary absent ou fige"),
        e.Regime + " " + e.RegimeReason);
    Check("Net Flow a 0 depuis 60 barres -> plus de pente (setup B impossible)", double.IsNaN(e.NetFlowSlope), e.NetFlowSlope.ToString());
}

// ================================================================ 11. Grille des ticks et gap au-dela du stop
// Niveaux du 02/11/2026 vus sur le graphique de Tom : Minor Gamma 7600,35 (stop 7599,35), VWAP 7660,86.
{
    Check("Ticks : stop d'achat 7599,35 -> 7599,25", SetupEngine.RoundStop(Side.Long, 7599.35, 0.25) == 7599.25);
    Check("Ticks : stop de vente 7631,05 -> 7631,25", SetupEngine.RoundStop(Side.Short, 7631.05, 0.25) == 7631.25);
    Check("Ticks : objectif de vente 7660,86 -> 7660,75", SetupEngine.RoundTarget(Side.Short, 7660.86, 0.25) == 7660.75);
    Check("Ticks : objectif d'achat 7641,40 -> 7641,50, 7641,50 inchange",
        SetupEngine.RoundTarget(Side.Long, 7641.40, 0.25) == 7641.5 && SetupEngine.RoundTarget(Side.Long, 7641.5, 0.25) == 7641.5);

    var e = new SetupEngine(Chicago());
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    for (int m = 0; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));      // signal, stop 5003
    bars.Add(Bar(day2, 551, 5006.5, 5008, 5006, 5007, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));     // entree 5006,75
    bars.Add(Bar(day2, 552, 4998, 4999, 4996, 4997, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));       // ouvre sous le stop
    Run(e, bars);
    var s = e.Signals.FirstOrDefault();
    Check("Gap sous le stop : sortie a l'ouverture 4998 - 1 tick = 4997,75 (-36 ticks)", s != null && s.Exit == ExitReason.Stop && s.ExitPrice == 4997.75 && s.ResultTicks == -36,
        s == null ? "aucun signal" : s.Exit + " " + s.ExitPrice + " " + s.ResultTicks);
}

// ================================================================ 12. Etude des scalps (ScalpStudy) sur un marche synthetique
// 50 seances de fevrier a avril 2026, a l'heure de New York, plus 5 seances a partir du 05/11 (test final, a exclure).
// Seances paires en short gamma (prix sous le Zero Gamma, Summary negatif), impaires en long gamma.
// Toutes les 30 minutes (xx:10 et xx:40) : delta extreme, puis 6 barres de 6 ticks dans le sens du delta en short gamma,
// contre lui en long gamma. A xx:25 et xx:55 : saut de Net Flow, puis 6 barres dans son sens, quel que soit le regime.
// Attendu : H1 regime et H2 Net Flow gagnent a chaque fois (+23 ticks), leurs controles autour de la moitie.
{
    var st = new EngineSettings();                        // New York
    var engine = new SetupEngine(st);
    var study = new ScalpStudy(st, new ScalpSettings());
    int idx = 0;
    double price = 5000, nf = 0;
    var days = new List<DateTime>();
    for (var d = new DateTime(2026, 2, 2); days.Count < 50; d = d.AddDays(1))
        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) days.Add(d);
    for (var d = new DateTime(2026, 11, 5); days.Count < 55; d = d.AddDays(1))
        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) days.Add(d);
    for (int k = 0; k < days.Count; k++)
    {
        bool shortGamma = k % 2 == 0;
        int drift = 0, driftLeft = 0, flip = 1;
        for (int m = 0; m < 390; m++)
        {
            int minuteOfDay = 9 * 60 + 30 + m;
            double open = price;
            double move = driftLeft > 0 ? drift * 1.5 : 0;   // 6 ticks par barre pendant la derive
            if (driftLeft > 0) driftLeft--;
            double close = open + move;
            long delta = (m % 2 == 0) ? 10 : -10;
            double dnf = (m % 2 == 0) ? 10 : -10;
            int mm = m % 30;
            if (mm == 10 && m >= 10)                         // evenement H1
            {
                flip = -flip;
                delta = 800L * flip;
                drift = shortGamma ? flip : -flip;
                driftLeft = 6;
            }
            else if (mm == 25)                               // evenement H2
            {
                flip = -flip;
                dnf = 1e6 * flip;
                drift = flip;
                driftLeft = 6;
            }
            nf += dnf;
            price = close;
            var b = new BarInput
            {
                Index = idx++, Time = days[k].AddMinutes(minuteOfDay), Open = open, Close = close,
                High = Math.Max(open, close) + 0.25, Low = Math.Min(open, close) - 0.25,
                Volume = 400 + Math.Abs(delta), AskVolume = 200 + Math.Max(0, delta), BidVolume = 200 + Math.Max(0, -delta),
                Gamma = new GammaInput
                {
                    ZeroGamma = (shortGamma ? price + 100 : price - 100) + idx * 0.001, MajorGamma = price + 300, MinorGamma = price - 300,
                    SummaryRegime = (shortGamma ? -1e9 : 1e9) + idx, NetFlow = nf, MmPressure = 1
                }
            };
            engine.OnBar(b);
            study.OnBar(b, engine);
        }
    }
    var h1 = study.Result(study.Find(ScalpRule.H1Regime, 16), 0);
    var h1f = study.Result(study.Find(ScalpRule.H1Follow, 16), 0);
    var h1r = study.Result(study.Find(ScalpRule.H1Random, 16), 0);
    var h2 = study.Result(study.Find(ScalpRule.H2NetFlow, 16), 0);
    var h2r = study.Result(study.Find(ScalpRule.H2Random, 16), 0);
    var h1d = study.Result(study.Find(ScalpRule.H1Regime, 16), 1);
    var h1v = study.Result(study.Find(ScalpRule.H1Regime, 16), 2);
    Check("Etude : 50 seances jusqu'au 04/11, les 5 seances du test final (a partir du 05/11) exclues", study.SessionDates.Count == 50, study.SessionDates.Count.ToString());
    Check("Etude : decouverte 30 seances, validation 20", h1d.Sessions == 30 && h1v.Sessions == 20, h1d.Sessions + "/" + h1v.Sessions);
    Check("Etude : H1 regime gagne a chaque fois, +23 ticks (objectif 24 - 1 tick)", h1.Trades > 100 && h1.Wins == h1.Trades && Math.Abs(h1.AvgTicks - 23) < 1e-6,
        h1.Trades + " trades, " + h1.Wins + " gagnants, " + h1.AvgTicks);
    Check("Etude : seuil de rentabilite a 1,5R = 42,5 % (stop 16 + 1 tick sur 40)", Math.Abs(h1.BreakEvenPct - 42.5) < 1e-9, h1.BreakEvenPct.ToString());
    Check("Etude : intervalle de confiance calcule et autour de +23", h1.CiLow <= 23 && h1.CiHigh >= 23, h1.CiLow + " / " + h1.CiHigh);
    Check("Etude : H1 toujours suivie autour de la moitie (le regime fait la difference)", h1f.WinPct > 35 && h1f.WinPct < 65, h1f.WinPct.ToString("0.0"));
    Check("Etude : H1 hasard autour de la moitie", h1r.WinPct > 30 && h1r.WinPct < 70, h1r.WinPct.ToString("0.0"));
    Check("Etude : H2 Net Flow gagne a chaque fois", h2.Trades > 100 && h2.Wins == h2.Trades, h2.Trades + " trades, " + h2.Wins + " gagnants");
    Check("Etude : H2 hasard autour de la moitie", h2r.WinPct > 30 && h2r.WinPct < 70, h2r.WinPct.ToString("0.0"));
    Check("Etude : aucun stop au-dela de 25 ticks, objectifs a 1,5R", study.Accounts.All(a => a.StopTicks <= 25 && a.TargetTicks == (int)Math.Round(a.StopTicks * 1.5)));
    var h1slip = study.Result(study.Find(ScalpRule.H1Regime, 16, 2), 0);
    Check("Etude : robustesse a 2 ticks par execution, H1 regime a +22 ticks (objectif 24 - 2)", h1slip.Trades > 100 && Math.Abs(h1slip.AvgTicks - 22) < 1e-6 &&
        Math.Abs(h1slip.BreakEvenPct - 45) < 1e-9, h1slip.Trades + " trades, " + h1slip.AvgTicks + ", seuil " + h1slip.BreakEvenPct);
}

// ================================================================ 13. Objectif a 2R et controle en sens inverse (scenario 2)
foreach (var variant in new[] { "2R", "inverse" })
{
    var st = Chicago();
    if (variant == "2R") { st.TargetMode = 1; st.TargetR = 2.0; } else st.FlipSide = true;
    var e = new SetupEngine(st);
    int idx = 0;
    double nf = 0;
    Run(e, CalmDay(day1, i => G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    var bars = new List<BarInput>();
    for (int m = 0; m < 30; m++) bars.Add(Bar(day2, 510 + m, 5002, 5004, 5000, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    for (int m = 30; m < 40; m++) bars.Add(Bar(day2, 510 + m, 5002, 5003, 5001, 5002, 10, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 550, 5003, 5007, 5003, 5006, 600, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 551, 5006.5, 5010, 5006, 5009, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 552, 5009, 5014, 5008, 5013, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    bars.Add(Bar(day2, 553, 5013, 5021, 5012, 5020.5, 50, G(5100, 5060, 4970, -1e9, nf += 1), ref idx));
    Run(e, bars);
    var sg = e.Signals.FirstOrDefault();
    if (variant == "2R")
        Check("Objectif 2R : entree attendue 5006,25, risque 13 ticks -> objectif 5012,75, atteint", sg != null && sg.Target == 5012.75 && sg.Exit == ExitReason.Target,
            sg == null ? "aucun signal" : sg.Target + " " + sg.Exit);
    else
        Check("Sens inverse : vente, stop 5009 et objectif 4992 en miroir, stoppee par la hausse", sg != null && sg.Side == Side.Short && sg.Stop == 5009 && sg.Target == 4992 &&
            sg.Exit == ExitReason.Stop && sg.ResultTicks < 0, sg == null ? "aucun signal" : sg.Side + " " + sg.Stop + " " + sg.Target + " " + sg.Exit + " " + sg.ResultTicks);
}

// ================================================================ 14. Optimizer sur un marche synthetique ou seul le setup B a un avantage
// 60 seances de fevrier a avril 2026 (New York), plus 5 seances apres le 16/08 (holdout d'A/B/C, a exclure). Short gamma
// tous les jours. Range d'ouverture 5000-5004, cassure a 10:10 avec un delta extreme, puis hausse jusqu'a 5030.
{
    var st = new EngineSettings();
    var opt = new Optimizer(st, new OptimizerSettings());
    int idx = 0;
    double nf = 0;
    var days = new List<DateTime>();
    for (var d = new DateTime(2026, 2, 2); days.Count < 60; d = d.AddDays(1))
        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) days.Add(d);
    for (var d = new DateTime(2026, 8, 17); days.Count < 65; d = d.AddDays(1))
        if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) days.Add(d);
    foreach (var day in days)
    {
        double price = 5002;
        for (int m = 0; m < 390; m++)
        {
            double o = price, c = price, h, l;
            long delta = m % 2 == 0 ? 10 : -10;
            if (m < 40) { o = 5002; c = 5002; h = 5004; l = 5000; }
            else if (m == 40) { o = 5003; c = 5006; h = 5007; l = 5003; delta = 800; }           // 10:10 : cassure
            else if (m <= 52) { o = price; c = Math.Min(5030, price + 2.25); h = c + 0.25; l = o - 0.25; }
            else { h = price + 0.5; l = price - 0.5; }
            price = c;
            nf += 1;
            opt.OnBar(new BarInput
            {
                Index = idx++, Time = day.AddMinutes(9 * 60 + 30 + m), Open = o, High = h, Low = l, Close = c,
                Volume = 400 + Math.Abs(delta), AskVolume = 200 + Math.Max(0, delta), BidVolume = 200 + Math.Max(0, -delta),
                Gamma = new GammaInput { ZeroGamma = 5200 + idx * 0.001, MajorGamma = 5300, MinorGamma = 4900, SummaryRegime = -1e9 - idx, NetFlow = nf, MmPressure = 1 }
            });
        }
    }
    Check("Optimizer : 64 configurations (A 24, B 36, C 4)", opt.Configs.Count == 64 && opt.Configs.Count(c => c.Setup == "A") == 24 &&
        opt.Configs.Count(c => c.Setup == "B") == 36 && opt.Configs.Count(c => c.Setup == "C") == 4, opt.Configs.Count.ToString());
    Check("Optimizer : 60 seances avant le 16/08, les 5 du holdout exclues ; recherche 36 / validation 24",
        opt.SessionDates.Count == 60 && opt.DiscoverySessions == 36, opt.SessionDates.Count + " / " + opt.DiscoverySessions);
    var topB = opt.Select("B");
    Check("Optimizer : 3 configurations B classees, la premiere retenue", topB.Count == 3 && topB[0].Retained, topB.Count == 0 ? "aucune" : topB[0].Reason);
    Check("Optimizer : la premiere B gagne en recherche et en validation, son sens inverse perd",
        topB.Count > 0 && topB[0].Discovery.AvgTicks > 1 && topB[0].Validation.AvgTicks > 1 && topB[0].ValidationFlipped.AvgTicks < 0,
        topB.Count == 0 ? "" : topB[0].Discovery.AvgTicks + " / " + topB[0].Validation.AvgTicks + " / " + topB[0].ValidationFlipped.AvgTicks);
    Check("Optimizer : aucun trade A ni C (jamais de long gamma) -> rien a classer", opt.Select("A").Count == 0 && opt.Select("C").Count == 0);
    Check("Optimizer : aucun trade compte apres le 16/08", opt.Configs.All(c => c.Trades.All(t => t.Session < 60)));
}

Console.WriteLine(failures == 0 ? "\nTous les controles sont OK." : "\n" + failures + " controle(s) en echec.");
return failures == 0 ? 0 : 1;
