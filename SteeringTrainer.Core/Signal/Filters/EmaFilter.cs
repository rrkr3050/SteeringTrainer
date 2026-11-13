namespace SteeringTrainer.Core.Signal.Filters
{
    /// <summary>
    /// 指数移動平均 (EMA) フィルタ
    /// alpha=0 → パススルー（フィルタなし）
    /// alpha=0.9 → 強いスムージング
    /// </summary>
    public class EmaFilter : IFilter
    {
        private double _alpha;
        private double _previous;
        private bool _initialized;

        /// <param name="alpha">スムージング係数 (0.0 ～ 0.99)。0=フィルタなし</param>
        public EmaFilter(double alpha = 0.1)
        {
            _alpha = Math.Clamp(alpha, 0.0, 0.99);
        }

        public double Alpha
        {
            get => _alpha;
            set => _alpha = Math.Clamp(value, 0.0, 0.99);
        }

        public double Filter(double value, double dt)
        {
            if (!_initialized)
            {
                _previous = value;
                _initialized = true;
                return value;
            }

            var filtered = _alpha * _previous + (1.0 - _alpha) * value;
            _previous = filtered;
            return filtered;
        }

        public void Reset()
        {
            _initialized = false;
            _previous = 0;
        }
    }
}
