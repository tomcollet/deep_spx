// Repris de tomcollet/deep (tests/DeepchartApiStubs). A garder aligne sur l'API reelle (deepchart-api.txt).
// Imitation de l'API Deepchart, d'après la documentation. Corps vides : seuls les types et signatures comptent.
#pragma warning disable CS0067, CS0649, CS1591
using System.Collections.Generic;
using VolumetricaAPI.Chart;
using VolumetricaCore;

namespace VolumetricaCore
{
    public enum ColorReferenceEnum { Up, Down, Bid, Ask, Neutral }
    public enum ColorTypeEnum { Stroke, Fill, Text }

    public class ColorRef
    {
        public static ColorRef FromRgb(byte r, byte g, byte b) => new ColorRef();
        public static ColorRef FromArgb(byte a, byte r, byte g, byte b) => new ColorRef();
        public static ColorRef Yellow => new ColorRef();
        // Les vraies dll prennent un byte (vu au premier build sur Windows)
        public ColorRef WithOpacity(byte alpha) => this;
    }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class ChartSeriesAttribute : System.Attribute
    {
        public ChartSeriesAttribute(string name) { }
        public SeriesType CurrentStyle { get; set; }
        public int AvailableStyle { get; set; }
        public int LineWidth { get; set; }
        public ColorRef PrimaryColor { get; set; }
        public ColorRef SecondaryColor { get; set; }
        public ColorRef ThirdColor { get; set; }
        public ColorRef FourthColor { get; set; }
        public bool OverrideAutoColor { get; set; }
        public bool Enabled { get; set; }
    }

    public class ChartSeriesDefinition
    {
        public string Name { get; }
        public IChartSeries BaseSeries { get; }
        public bool Enabled { get; set; }
        public ColorRef PrimaryColor { get; }
        public ColorRef SecondaryColor { get; }
    }
}

namespace VolumetricaControls
{
    // Dans les vraies dll, cet attribut est réservé à Windows : chaque usage donne l'avertissement CA1416
    [System.AttributeUsage(System.AttributeTargets.Property)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]
    public class VolCustomAttribute : System.Attribute
    {
        public int CategoryIndex { get; set; }
        public int PropertyIndex { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public double IncrementValue { get; set; }
        public int DecimalPosToShow { get; set; }
        public bool IsVisible { get; set; }
        public bool IsAlert { get; set; }
    }

    public class DynamicList
    {
        public string SelValue { get; set; }
    }
}

namespace VolumetricaAPI.Chart
{
    [System.Flags]
    public enum SeriesType { Line = 1, Bars = 2, Area = 4, Range = 8, Square = 16, Cicle = 32, Diamond = 64, Cross = 128, TriangleUp = 256, TriangleDown = 512 }
    public enum AnnotationType { Line, Rectangle, Text, HorizontalLine, VerticalLine, HorizontalRay, Ellipse, Triangle, ArrowUp, ArrowDown, Diamond }
    public enum CoordinateTypeEnum { Absolute, Relative, Pixel, Session }

    public class ChartAreaRef
    {
        public static ChartAreaRef Main => new ChartAreaRef();
    }

    public interface IChartPoint
    {
        double[] Y { get; }
        bool IsHidden { get; set; }
        bool BreakPoint { get; set; }
        ColorRef LineColor { get; set; }
        ColorRef BackColor { get; set; }
        string Text { get; set; }
    }

    public interface IChartSeries
    {
        string Name { get; }
        List<IChartPoint> Points { get; }
    }

    public interface IAnnotation
    {
        AnnotationType AnnotationType { get; }
        double X { get; set; }
        double Y { get; set; }
        double X2 { get; set; }
        double Y2 { get; set; }
        CoordinateTypeEnum CoordinateXType { get; set; }
        CoordinateTypeEnum CoordinateYType { get; set; }
        ChartAreaRef Area { get; set; }
        bool Visible { get; set; }
        bool PlotForeground { get; set; }
        ColorRef LineColor { get; set; }
        int LineWidth { get; set; }
        ColorRef BackColor { get; set; }
        string Text { get; set; }
        ColorRef ForeColor { get; set; }
        int FontSize { get; set; }
        bool FontBold { get; set; }
        string Tooltip { get; set; }
    }

    public interface IAnnGroup
    {
        void AddGroup(IAnnGroup group);
        void RemoveAnnotation(IAnnotation annotation);
        void Clear();
        bool Visible { get; set; }
    }

    public interface IAxis
    {
        bool YSame { get; set; }
        int DecimalPlaceShown { get; set; }
        bool FormatAutoNumber { get; set; }
    }
}

namespace VolumetricaAPI
{
    public enum LogLevelEnum { Debug, Information, Warning, Error }
}

namespace VolumetricaAPI.Connection
{
    public static class Structure
    {
        public enum AggressorSideEnum { Ask, Bid, Between }

        public class TickByTick
        {
            public double price;
            public int PinT;
            public int Vol;
            public AggressorSideEnum AggrSide;
            public double bid, ask;
            public System.DateTime exDt;
        }

        public class MbpUpdate
        {
            public int PinT;
            public bool isAsk;
            public int Qty;
        }

        public class BookUpdate
        {
            public bool NeedResetMbp;
            public List<MbpUpdate> MbpUpdates;
        }
    }
}

namespace VolSysAPI
{
    public static class ExternalStructure
    {
        public enum CallHandler { Off, RT, HistRT }

        public class IndicatorDescriptionBase
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string HelpLink { get; set; }
            public List<string> Tags { get; set; }
        }

        public class AggrInfo { }
    }

    public static class Structure
    {
        public class InfoVolClass
        {
            public int TotVol, AskVol, BidVol;
            public double VwapPrice;
        }

        public class VbpLevel
        {
            public int TotVol, AskVol, BidVol;
        }

        public class BarClass
        {
            public double Open, High, Low, Close;
            public int OpenInT, HighInT, LowInT, CloseInT;
            public System.DateTime exchDt, dt;
            public bool IsNewDay;
            public int SessionIndex, Index;
            public List<InfoVolClass> VolTotList;
            public List<int> PriceList;
            public Dictionary<int, VbpLevel[]> VbpListAll;
        }
    }

    public class PriceInfo
    {
        public double Price;
    }

    public interface IMethodAPI
    {
        static ColorRef GetColorRefForTheme(ColorReferenceEnum reference, ColorTypeEnum type, object range = null) => new ColorRef();
    }

    public interface IMethodCloudAPI : IMethodAPI
    {
        List<Structure.BarClass> BarVars { get; }
        int BarIndex { get; }
        int LastIndex();
        string SymbolName { get; }
        string ChartName { get; }
        System.DateTime LastExDt { get; }
        IChartPoint AddPointToSeries(IChartSeries series, int indexBar, double yValue = 0, bool IsHidden = false, bool isBreak = false);
        IAnnotation CreateAnnotation(AnnotationType annotationType);
        IAnnGroup CreateAnnGroup();
        void AddAnnotation(IAnnGroup group, IAnnotation annotation);
        PriceInfo FromPinT(int pinT);
        double GetDoubleTicksDiff(double from, double to);
        double DoubleAddSubTicks(double price, int ticks);
        void ShowMessage(string text, string title);
        void PlayAlert(string alertName);
        VolumetricaAPI.Connection.Structure.BookUpdate RebuildDom();
        void AddToLog(string message, VolumetricaAPI.LogLevelEnum level, string source = null, bool? showToast = null);

        // Ajouts deep_spx. Documentés dans docs/reference/chart-api du dépôt officiel ; types exacts à confirmer par
        // tools/ApiDump (deepchart-api.txt).
        bool IsReplayData { get; }
        object ChartPhase { get; }
        // Appel GetSeriesValueById(3, 0, barIndex), trois int, vu dans l'issue #4 du dépôt officiel (code compilé
        // contre les vraies dll). Type de retour non documenté : double ici.
        double GetSeriesValueById(int indicator, int series, int index);
    }
}

namespace VolSysAPI.Indicators
{
    using VolSysAPI;
    using static VolSysAPI.ExternalStructure;

    public class IndVarsClass
    {
        public IAnnGroup Ann_List { get; }
        public IAnnGroup FrontAnnList { get; }
    }

    public abstract class Indicator
    {
        public IMethodCloudAPI VAn { get; }
        public IndVarsClass IndVars { get; }
        public IAxis AxisX { get; }
        public IAxis AxisY { get; }
        public ChartAreaRef ChartArea { get; }
        public CallHandler OnOpenCall { get; set; }
        public CallHandler OnCloseCall { get; set; }
        public CallHandler OnTickCall { get; set; }
        public CallHandler OnEndCall { get; set; }
        public CallHandler OnBookCall { get; set; }
        public bool NeedVbp { get; set; }
        public string Description { get; set; }
        public string StatusMessage { get; set; }

        public abstract void OnSet(bool setDefault, bool themeOverride);
        public virtual void OnPreInit() { }
        public virtual void OnInit() { }
        public virtual void OnLoad() { }
        public virtual void OnOpen(bool isRt) { }
        public virtual void OnTick(VolumetricaAPI.Connection.Structure.TickByTick tick, bool isRt, AggrInfo aggrInfo) { }
        public virtual void OnClose(bool isRt) { }
        public virtual void OnEnd(bool isRt) { }
        public virtual void OnBook(VolumetricaAPI.Connection.Structure.BookUpdate updates) { }
    }
}
