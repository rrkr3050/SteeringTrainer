namespace SteeringTrainer.Core.Signal.Filters
{
    public interface IFilter
    {
        /// <summary>フィルタを適用してフィルタ後の値を返す</summary>
        double Filter(double value, double dt);

        /// <summary>フィルタ状態をリセット</summary>
        void Reset();
    }
}
