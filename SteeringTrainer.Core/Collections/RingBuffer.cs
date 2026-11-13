using System;
using System.Collections.Generic;

namespace SteeringTrainer.Core.Collections
{
    /// <summary>
    /// スレッドセーフではない固定長リングバッファ。
    /// UIスレッド側でのコピー取得用。
    /// </summary>
    public class RingBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head;  // 次に書き込む位置
        private int _count;

        public int Capacity => _buffer.Length;
        public int Count => _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new T[capacity];
        }

        public void Push(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }

        /// <summary>古い順に全要素をリストとして取得</summary>
        public List<T> ToList()
        {
            var result = new List<T>(_count);
            if (_count == 0) return result;

            int start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                result.Add(_buffer[(start + i) % _buffer.Length]);
            }
            return result;
        }

        /// <summary>最新N件を古い順で取得</summary>
        public List<T> ToList(int n)
        {
            n = Math.Min(n, _count);
            var result = new List<T>(n);
            if (n == 0) return result;

            int start = (_head - n + _buffer.Length * 2) % _buffer.Length;
            for (int i = 0; i < n; i++)
            {
                result.Add(_buffer[(start + i) % _buffer.Length]);
            }
            return result;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }
}
