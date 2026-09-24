using System.Collections.Generic;

namespace MagicBrawl.Core
{
    /// <summary>
    /// 确定性随机数发生器：splitmix64 播种 + xorshift128+ 生成。
    ///
    /// 铁律（见 `Docs/engineering/04-架构与接口.md` §1 第 3 条）：Core 内所有随机必须走这里，
    /// 禁止 System.Random / UnityEngine.Random —— 保证「同种子 → 同对局」，
    /// 自测复现与未来 lockstep 联机都依赖这一点。
    /// </summary>
    public sealed class Rng
    {
        private ulong _s0;
        private ulong _s1;

        /// <summary>本发生器使用的种子。</summary>
        public int Seed { get; }

        public Rng(int seed)
        {
            Seed = seed;
            unchecked
            {
                ulong z = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL;
                _s0 = SplitMix64(ref z);
                z += 0x9E3779B97F4A7C15UL;
                _s1 = SplitMix64(ref z);
                if (_s0 == 0UL && _s1 == 0UL)
                {
                    _s1 = 1UL;
                }
            }
        }

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>下一个 64 位无符号随机数。</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                ulong x = _s0;
                ulong y = _s1;
                _s0 = y;
                x ^= x << 23;
                _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
                return _s1 + y;
            }
        }

        /// <summary>[0, maxExclusive) 内的均匀随机整数。maxExclusive ≤ 0 时返回 0。</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1)
            {
                return 0;
            }

            ulong range = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);

            ulong v;
            do
            {
                v = NextUInt64();
            }
            while (v >= limit);

            return (int)(v % range);
        }

        /// <summary>返回 [min, maxExclusive) 内的整数。</summary>
        public int Next(int min, int maxExclusive)
        {
            return min + Next(maxExclusive - min);
        }

        /// <summary>原地洗牌（Fisher–Yates）。</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>从列表中等概率取一个元素；列表为空返回 default。</summary>
        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0)
            {
                return default(T);
            }

            return list[Next(list.Count)];
        }
    }
}
