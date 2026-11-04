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
    public enum LogLevelEnum { Verbose = 0, Information = 1, Warning = 2, Error = 3 }
}

// Aligne sur deepchart-api.txt (VolumetricaCore 1.0.0.0, VolSysAPI 1.4.6.0, sortie de tools/ApiDump, 04/11/2026).
// Seuls les membres utiles au projet sont repris ; les espaces de noms des types de VolumetricaCore ne sont pas dans
// l'inventaire et suivent les using des exemples officiels.
namespace VolumetricaAPI.Connection
{
    public static class Structure
    {
        public enum AggressorSideEnum { Between = 0, Bid = -1, Ask = 1 }

        [System.Flags]
        public enum TradeTypeEnum { None = 0, Regular = 1, Iceberg = 2, SweepOrder = 4, Auction = 8, Extended = 16, OddTrade = 32, AfterHour = 64, VolumeOnly = 128 }

        public enum MboFlagsEnum { None = 0, Iceberg = 1 }

        public class TickByTick
        {
            public System.DateTime exDt, locDt;
            public double price, bid, ask;
            public int QtyOrd, Vol;
            public long CumVol;
            public ulong AggrID, RestID;
            public TradeTypeEnum TradeType;
            public int CounterSize, PinT;
            public double SubDouble;
            public bool IsRtRecived, IntPriceCalc;
            public AggressorSideEnum AggrSide;
            public bool IsNewDay;
        }

        /// Un niveau de prix du carnet (MBP)
        public class BookInfo
        {
            public double Price;
            public int PinT, Qty, DiffQty, NumOffers;
            public bool isAsk, IsShiftOutside;
            public int TempDiff, MboMaxQty;
        }

        /// Un ordre du carnet (MBO)
        public class MboInfo
        {
            public ulong OrderId;
            public bool IsAsk;
            public int Qty;
            public double Price;
            public int PinT, MaxQty, CumFillQty;
            public MboFlagsEnum Flags;
        }

        public class BookUpdate
        {
            public bool NeedResetMbp, NeedResetMbo;
            public double BestBidPrice, BestAskPrice;
            public int BestBidInT, BestAskInT;
            public System.DateTime exDt, appDt;
            public List<BookInfo> MbpUpdates;
            public List<MboInfo> MboUpdates;
        }
    }
}

namespace VolSysAPI
{
    public static class ExternalStructure
    {
        public enum CallHandler { Off = 0, HistRT = 1, RT = 2 }

        public class IndicatorDescriptionBase
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string HelpLink { get; set; }
            public List<string> Tags { get; set; }
        }

        public class Series_Prop
        {
            public string Name { get; set; }
            public string ShortName { get; set; }
        }

        /// Ce que renvoie GetParamsById : reglages d'un indicateur du graphique
        public class BaseIndicatorPrms
        {
            public HashSet<string> HiddenProp;
            public string name { get; set; }
            public bool Hidden { get; set; }
            public string ReferenceGuid { get; set; }
            public List<Series_Prop> v_series { get; set; }
        }
    }

    public static class Structure
    {
        public enum ChartPhaseEnum { HistoricalFull = 1, Backtesting = 2, HistoricalPartial = 4, Realtime = 8, RealtimeFlushing = 16 }

        public enum TradeDirStatus { Ask = 0, Bid = 1, BelowBid = 2, AboveAsk = 3, Unknown = 4 }

        /// Trade agrege (avec SetCalculateAggrTrades)
        public class AggrInfo
        {
            public int PinT;
            public TradeDirStatus TradeDir;
            public int Vol, MaxQtyOrd, NumberOfTrades;
            public double StartPrice, EndPrice, AvgPrice;
        }

        public class InfoVolClass
        {
            public int CumDeltaVol;
            public long Countervalue;
            public double VwapPrice;
            public long BidVol, AskVol;
            public int AskNTVol, BidNTVol;
            public long TotVol;
            public int TotNTVol;
        }

        public class VbpLevel
        {
            public int TotVol, AskVol, BidVol;
        }

        public class BarClass
        {
            public System.DateTime dt, exchDt;
            public int Index;
            public double Open, Close, High, Low, Ask, Bid, LastAsk, LastBid;
            public int OpenInT, CloseInT, HighInT, LowInT;
            public System.DateTime lastDtInBar;
            public bool IsNewDay;
            public int SessionIndex;
            public List<int> PriceList;
            public InfoVolClass[] VolTotList;
            public Dictionary<int, VbpLevel[]> VbpListAll;
            public double? OptionOffsetPrice;
        }
    }

    public struct DoubleP
    {
        public double Price;
        public int AddInt, SubInt;
        public double AddDouble;
    }

    public interface IMethodAPI
    {
        static ColorRef GetColorRefForTheme(ColorReferenceEnum reference, ColorTypeEnum type, object range = null) => new ColorRef();

        string ChartName { get; }
        string SymbolName { get; }
        string SymbolFullName { get; }
        bool IsReplayData { get; }
        List<Structure.BarClass> BarVars { get; }
        int BarIndex { get; }
        System.DateTime LastExDt { get; }
        Structure.ChartPhaseEnum ChartPhase { get; }
        VolumetricaAPI.Connection.Structure.BookUpdate RebuildDom();
        void SetCalculateAggrTrades();
        int LastIndex();
        DoubleP FromPinT(int pInT);
        int GetDoubleTicksDiff(double IniPrice, double EndPrice);
        double DoubleAddSubTicks(double Price, int Tick);
        void ShowMessage(string Text, string Title);
        void PlayAlert(string AlertName);
        ExternalStructure.BaseIndicatorPrms GetParamsById(int IndicatorIndex);
        double[] GetSeriesValueById(int IndicatorIndex, int SubgraphIndex, int Index);
        void AddToLog(string message, VolumetricaAPI.LogLevelEnum logLevel = VolumetricaAPI.LogLevelEnum.Information, string source = null, bool? showToast = null);
    }

    public interface IMethodCloudAPI : IMethodAPI
    {
        IChartPoint AddPointToSeries(IChartSeries Series, int IndexBar, double YValue = 0, bool IsHidden = false, bool IsBreak = false);
        IAnnotation CreateAnnotation(AnnotationType annType);
        IAnnGroup CreateAnnGroup();
        void AddAnnotation(IAnnGroup AnnGroup, IAnnotation NewAnn);
    }

    // API d'ordres (desktop). Espace de noms reel non visible dans l'inventaire.
    public enum OrderTypeEnum { Market = 0, Limit = 1, Stop = 2, StopLimit = 3 }
    public enum OrderStatusEnum { Submitted = 0, PendingRequest = 1, PendingCancel = 2, PendingModify = 3, Filled = 4, Canceled = 5, Internal = 6, Failed = 7, Waiting = 8 }

    public class BracketParam
    {
        public decimal Quantity { get; set; }
        public int? Ticks { get; set; }
        public double? Price { get; set; }
        public bool IsTarget { get; set; }
    }

    public class OrderInsertRequest
    {
        public OrderTypeEnum OrderType { get; set; }
        public decimal Quantity { get; set; }
        public double? Price { get; set; }
        public List<BracketParam> Brackets { get; set; }
        public long? RefPositionId { get; set; }
        public int? BarsExpiration { get; set; }
    }

    public class OrderStatusInfo
    {
        public long OriginalOrderId { get; set; }
        public long OrderId { get; set; }
        public decimal PendingQuantity { get; set; }
        public decimal FilledQuantity { get; set; }
        public OrderTypeEnum OrderType { get; set; }
        public OrderStatusEnum OrderStatus { get; set; }
    }

    public interface ITradingAPI
    {
        decimal PositionNetQuantity { get; }
        decimal PositionBuyQuantity { get; }
        decimal PositionSellQuantity { get; }
        long InsertOrder(OrderInsertRequest param, ref string reason);
        long ModifyOrderPrice(long orderId, double price, ref string reason);
        long ModifyOrderQuantity(long orderId, decimal quantity, ref string reason);
        bool RemoveOrder(long orderId, ref string reason);
        long InsertBracket(long orderId, BracketParam param, ref string reason);
        bool FlatPosition(long positionId, ref string reason);
        void FlatAllPositions();
        void CancelAllOrders();
        List<OrderStatusInfo> GetAllOrdersStatus();
        OrderStatusInfo GetOrderInfo(long orderId);
    }
}

namespace VolSysAPI.Indicators
{
    using VolSysAPI;
    using static VolSysAPI.ExternalStructure;
    using static VolSysAPI.Structure;

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
        protected ITradingAPI TradingApi { get; }
        public int getId() => 0;

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
