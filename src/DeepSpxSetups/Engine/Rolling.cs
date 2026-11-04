using System;
using System.Collections.Generic;

namespace DeepSpx.Engine
{
    /// ATR de Wilder
    public sealed class WilderAtr
    {
        private readonly int _period;
        private double _atr, _prevClose;
        private int _n;

        public WilderAtr(int period) { _period = Math.Max(1, period); }

        public bool Ready => _n >= _period;
        public double Value => _atr;

        public void Add(double high, double low, double close)
        {
            double tr = _n == 0 ? high - low
                : Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose), Math.Abs(low - _prevClose)));
            if (_n < _period) _atr = (_atr * _n + tr) / (_n + 1);
            else _atr = (_atr * (_period - 1) + tr) / _period;
            _n++;
            _prevClose = close;
        }

        public void Reset() { _atr = 0; _prevClose = 0; _n = 0; }
    }

    /// Rang centile glissant : part (0 a 100) des valeurs de la fenetre strictement inferieures a une valeur.
    public sealed class RollingPercentile
    {
        private readonly int _capacity;
        private readonly Queue<double> _queue = new Queue<double>();
        private readonly List<double> _sorted = new List<double>();

        public RollingPercentile(int capacity) { _capacity = Math.Max(1, capacity); }

        public int Count => _sorted.Count;

        public void Add(double v)
        {
            _queue.Enqueue(v);
            int i = _sorted.BinarySearch(v);
            _sorted.Insert(i < 0 ? ~i : i, v);
            if (_queue.Count > _capacity)
            {
                double old = _queue.Dequeue();
                int j = _sorted.BinarySearch(old);
                if (j >= 0) _sorted.RemoveAt(j);
            }
        }

        public double Rank(double v)
        {
            if (_sorted.Count == 0) return 0;
            int lo = 0, hi = _sorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_sorted[mid] < v) lo = mid + 1; else hi = mid;
            }
            return 100.0 * lo / _sorted.Count;
        }

        public void Reset() { _queue.Clear(); _sorted.Clear(); }
    }
}
