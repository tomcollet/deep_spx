// Moteur de setups deep_spx : C# pur, sans dependance a Deepchart.
// Compile dans la DLL de l'indicateur et dans tests/EngineChecks. Contraintes du bac a sable de Deepchart :
// pas de params, pas d'indexeur, pas de reflexion, de fichiers, de threads ni de lock.
using System;
using System.Collections.Generic;

namespace DeepSpx.Engine
{
    public enum Regime { Unknown = 0, LongGamma = 1, ShortGamma = 2, Transition = 3 }

    public enum Side { None = 0, Long = 1, Short = -1 }

    /// A : rejet de mur (long gamma) ; B : continuation (short gamma) ; C : aimant de fin de seance (long gamma)
    public enum SetupKind { None = 0, WallReject = 1, Continuation = 2, Pin = 3 }

    public enum ExitReason { None = 0, Target = 1, Stop = 2, Time = 3, SessionEnd = 4, Cancelled = 5 }

    /// Valeurs DeepGamma lues a une barre. NaN = absente.
    public sealed class GammaInput
    {
        public double ZeroGamma = double.NaN;
        public double MajorGamma = double.NaN;
        public double MinorGamma = double.NaN;
        public double SummaryRegime = double.NaN;   // Option Summary / Regime : signe = regime gamma market maker
        public double NetFlow = double.NaN;         // Option Net Flow / Difference
        public double MmPressure = double.NaN;      // Option Market Maker Pressure / MM Pressure (journalise seulement)
    }

    /// Une barre fermee. Time = heure de bourse (Chicago pour le CME) du debut de la barre.
    public sealed class BarInput
    {
        public int Index;
        public DateTime Time;
        public double Open, High, Low, Close;
        public long Volume, AskVolume, BidVolume;
        public double Vwap;                         // VWAP de la barre, 0 si absent
        public GammaInput Gamma = new GammaInput();
    }

    public sealed class Signal
    {
        public int Id;
        public int BarIndex;                        // barre dont la cloture declenche le signal
        public DateTime Time;
        public SetupKind Kind;
        public Side Side;
        public Regime Regime;
        public string LevelName = "";
        public double Level;
        public double SignalClose;                  // cloture de la barre du signal (entree reelle : ouverture suivante)
        public double Stop, Target;

        public bool Filled;
        public int EntryBar = -1;
        public DateTime EntryTime;
        public double EntryPrice;

        public bool Closed;
        public int ExitBar = -1;
        public DateTime ExitTime;
        public double ExitPrice;
        public ExitReason Exit;
        public double ResultTicks;

        // Contexte au moment du signal, pour le journal
        public double DeltaRank, Delta, Atr5, ZeroGamma, DistZeroTicks, NetFlowSlope, Summary, Vwap;
    }

    public sealed class SetupStats
    {
        public int Trades, Wins;
        public double SumTicks;
        public double WinRate => Trades == 0 ? 0 : 100.0 * Wins / Trades;
        public double AvgTicks => Trades == 0 ? 0 : SumTicks / Trades;

        public void Add(double ticks)
        {
            Trades++;
            if (ticks > 0) Wins++;
            SumTicks += ticks;
        }
    }
}
