namespace DeepSpx.Engine
{
    /// Reglages du moteur. Valeurs de depart prises dans docs/STRATEGIE.md, avec les substitutions de docs/SETUPS.md
    /// (l'Expected Move n'est pas lisible : les distances sont exprimees en ATR 5 min). A ne pas regler sur la periode
    /// scellee (seances a partir du 16/08/2026).
    public sealed class EngineSettings
    {
        // ---- Marche et seance, a l'heure des barres. Par defaut l'heure de New York : c'est celle des barres (exchDt)
        //      du graphique options SPX de Tom (constate le 04/11/2026), et celle de docs/STRATEGIE.md. Barres a l'heure
        //      de Chicago : ShiftClock(-60).
        public double TickSize = 0.25;
        public int RthStartMinute = 9 * 60 + 30;        // 09:30 New York
        public int RthEndMinute = 16 * 60;              // 16:00
        public int NoTradeFirstMinutes = 5;             // pas d'entree les 5 premieres minutes
        public int LastEntryMinute = 15 * 60 + 45;      // 15:45 : plus d'entree
        public int ForcedExitMinute = 15 * 60 + 45;     // 15:45 : FLAT obligatoire
        public int OpeningRangeMinutes = 30;

        // ---- Mesures
        public int AtrPeriod = 30;                      // ATR de Wilder sur barres 1 min ; ATR 5 min = ATR 1 min x racine(5)
        public int PercentileWindowBars = 2000;         // fenetre glissante (barres de seance reguliere) des |delta|
        public int PercentileMinSamples = 200;
        public int NetFlowLookbackBars = 5;

        // ---- Regime
        public double TransitionAtr = 0.5;              // |prix - Zero Gamma| < 0,5 ATR5 : zone de transition, pas de trade
        public bool UseSummarySign = true;              // le signe d'Option Summary / Regime entre dans le regime
        public int MaxGammaAgeBars = 15;                // derniere valeur DeepGamma valide vieille de plus de 15 barres : regime inconnu
        public int StaleGammaBars = 45;                 // aucune valeur DeepGamma n'a change depuis 45 barres de seance : fige
        public bool BlockOnStaleGamma = true;

        // ---- Setup A : rejet de mur (long gamma)
        public bool EnableWallReject = true;
        public int ZoneTicks = 4;                       // contact a 4 ticks ou moins du mur
        public double VwapDevAtr = 1.5;                 // cloture a au moins 1,5 ATR5 de la VWAP (prix etire)
        public double AbsorbPct = 90;                   // delta agressif vers le mur au-dessus de son 90e percentile
        public int AbsorbMaxProgressTicks = 2;          // le prix ne depasse pas le mur de plus de 2 ticks

        // ---- Setup B : continuation (short gamma)
        public bool EnableContinuation = true;
        public int BreakTicks = 1;                      // cloture au-dela du niveau d'au moins 1 tick
        public double BreakDeltaPct = 80;               // delta dans le sens de la cassure au-dessus du 80e percentile
        public bool RequireNetFlow = true;              // Net Flow dans le sens du trade (pente sur 5 barres)
        public int StopBackTicks = 4;                   // stop : retour de 4 ticks de l'autre cote du niveau
        public double TargetAtr = 2.0;                  // objectif par defaut s'il n'y a pas de niveau suivant

        // ---- Setup C : aimant de fin de seance (long gamma)
        public bool EnablePin = true;
        public int PinStartMinute = 14 * 60;            // 14:00 New York
        public double PinMaxDistAtr = 2.0;              // le strike aimant (Major Gamma) a moins de 2 ATR5
        public int PinMinDevTicks = 8;                  // ecart minimal au strike pour entrer

        // ---- Sorties communes
        public double StopAtr = 1.0;                    // A et C : stop a 1 ATR5 au-dela de l'extreme
        public int MinStopTicks = 8;
        public int MinTargetTicks = 8;
        public int TimeStopBarsA = 45;
        public int TimeStopBarsB = 60;
        public int TimeStopBarsC = 105;

        // ---- Regle de risque de Tom (04/11/2026), pour tous les setups : jamais plus de 25 ticks de risque, et un
        //      objectif d'au moins 1,5 fois le risque. Un signal qui ne la respecte pas est refuse, pas ajuste.
        public int MaxRiskTicks = 25;
        public double MinRewardRisk = 1.5;

        // ---- Risque et execution simulee
        public int MaxTradesPerDay = 3;
        public int MaxLossesPerDay = 2;
        public double SlippageTicks = 1;                // chaque execution (entree et sortie) au marche, 1 tick contre soi

        /// Decale toutes les heures de seance (barres a l'heure de Chicago : -60).
        public void ShiftClock(int minutes)
        {
            RthStartMinute += minutes;
            RthEndMinute += minutes;
            LastEntryMinute += minutes;
            ForcedExitMinute += minutes;
            PinStartMinute += minutes;
        }

        /// Heures de seance en clair, pour le panneau.
        public string SessionText()
        {
            return Hm(RthStartMinute) + "-" + Hm(RthEndMinute) + ", aimant des " + Hm(PinStartMinute) + ", FLAT " + Hm(ForcedExitMinute);
        }

        private static string Hm(int minute) => (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
    }
}
