using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilterVisualPlatform
{
    /// <summary>
    /// 毫秒级滑动窗口计数器（线程安全）
    /// - 支持任意统计窗口（毫秒）
    /// - 支持任意桶粒度（毫秒），如 100ms、50ms；窗口=5s 建议粒度 100ms~200ms
    /// - 可统计字节速率/帧率等（调用 Add 时传入数量）
    /// - GetRatePerSecond(windowMs) 可临时指定任意窗口，不用新建实例
    /// </summary>
    public class SlidingMsRateCounter
    {
        private readonly int _bucketMs;      // 每个桶的时间粒度（毫秒）
        private readonly int _bucketsCount;  // 桶个数（内部环形数组大小）
        private readonly Bucket[] _buckets;  // 环形桶
        private long _total;                 // 累计总量
        private readonly object _sync = new object();

        private struct Bucket
        {
            public long Key;   // 桶的时间键：Unix毫秒 / bucketMs
            public long Count; // 该桶内累计数量
        }

        /// <param name="windowMs">建议的主要统计窗口（毫秒）。仅用于确定桶数组规模，可在 Get 时用别的窗口。</param>
        /// <param name="bucketMs">桶粒度（毫秒）。窗口越小，粒度可设更小；一般取 50~200ms。</param>
        public SlidingMsRateCounter(int windowMs = 5000, int bucketMs = 100)
        {
            if (bucketMs <= 0) bucketMs = 100;
            if (windowMs < bucketMs) windowMs = bucketMs;

            _bucketMs = bucketMs;

            // 桶数量按“窗口需要 + 余量”分配，避免频繁覆盖
            int needed = (int)Math.Ceiling(windowMs / (double)_bucketMs);
            _bucketsCount = Math.Max(needed * 3, 32);
            _buckets = new Bucket[_bucketsCount];
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>增加数量（如收到 N 字节，或 1 帧）。线程安全。</summary>
        public void Add(long n)
        {
            if (n <= 0) return;

            long nowMs = NowMs();
            long key = nowMs / _bucketMs;           // 当前毫秒落到哪个桶
            int idx = (int)(key % _bucketsCount);   // 环形索引

            lock (_sync)
            {
                if (_buckets[idx].Key != key)
                {
                    // 覆盖旧桶
                    _buckets[idx].Key = key;
                    _buckets[idx].Count = 0;
                }
                _buckets[idx].Count += n;
                _total += n;
            }
        }

        /// <summary>获取“过去 windowMs 毫秒”的总量。</summary>
        public long GetWindowSum(int windowMs)
        {
            if (windowMs <= 0) return 0;
            long nowKey = NowMs() / _bucketMs;
            long windowBuckets = (long)Math.Ceiling(windowMs / (double)_bucketMs);

            long sum = 0;
            lock (_sync)
            {
                for (int i = 0; i < _bucketsCount; i++)
                {
                    var b = _buckets[i];
                    // 在窗口内的桶累加
                    if (b.Key != 0 && (nowKey - b.Key) < windowBuckets)
                        sum += b.Count;
                }
            }
            return sum;
        }

        /// <summary>获取“过去 windowMs 毫秒”的平均速率（每秒）。</summary>
        public double GetRatePerSecond(int windowMs)
        {
            if (windowMs <= 0) return 0;
            long sum = GetWindowSum(windowMs);
            return sum * 1000.0 / windowMs; // 转换为 每秒
        }

        /// <summary>累计总量（自进程启动以来）。</summary>
        public long GetTotal()
        {
            lock (_sync) return _total;
        }
    }
}
