using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;

namespace PervasiveDigital.Utilities
{
    public class CircularBuffer : ICollection
    {
        // Capacity growth multiplier and constant. Capacity grows as _capacity * _growM + _growC
        private readonly int _growM;
        private readonly int _growC;

        // current capacity
        private int _capacity;
        // ring pointers
        private int _head;
        private int _tail;
        // current occupied space (count of bytes in the ring)
        private int _size;
        // the actual data
        private byte[] _buffer;

        private object _syncRoot = new object();

        public CircularBuffer(int capacity, int growthMultiplier, int growthConstant)
        {
            if (capacity==0 || growthMultiplier==0 || (growthMultiplier==1 && growthConstant==0))
                throw new ArgumentException("capacity must be non-zero and 1*growthMultiplier+growthConstant must be non-zero");

            _size = 0;
            _head = 0;
            _tail = 0;
            _buffer = new byte[capacity];
            _growM = growthMultiplier;
            _growC = growthConstant;
        }

        public int Capacity
        {
            get { return _capacity; }
            set
            {
                if (value == _capacity)
                    return;

                if (value < _size)
                    throw new ArgumentOutOfRangeException("value", "New capacity is smaller than current size");

                var dest = new byte[value];
                if (_size > 0)
                    CopyTo(dest, 0);
                _buffer = dest;
                _capacity = value;
            }
        }

        public int Size
        {
            get { return _size; }
        }

        public void Clear()
        {
            _head = _tail = _size = 0;
        }

        public bool Contains(byte item)
        {
            int idx = _head;
            for (int i = 0; i < _size; i++, idx++)
            {
                if (idx == _capacity)
                    idx = 0;

                if (_buffer[idx] == item)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Find the first occurrence of a byte in the buffer
        /// </summary>
        /// <param name="item">The value to search for</param>
        /// <returns>The offset of the found value, or -1 if not found</returns>
        public int IndexOf(byte item)
        {
            int idx = _head;
            for (int i = 0; i < _size; i++, idx++)
            {
                if (idx == _capacity)
                    idx = 0;

                if (_buffer[idx] == item)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// A greedy sequence matcher that will match the first occurrence of seq in the circular buffer.  This routine
        /// returns the offset of the matched sequence or -1 if the sequence does not appear in the stream.
        /// </summary>
        /// <param name="seq">The sequence of bytes to search for</param>
        /// <returns>Offset of the first match, or -1 if not found</returns>
        public int IndexOf(byte[] seq)
        {
            // can't have a match, so don't bother searching
            if (_size < seq.Length)
                return -1;

            int iOffsetFirst = -1;  // offset of first matched char
            int idxFirst = -1; // index of first matched char

            var idxSeq = 0;
            var lenSeq = seq.Length;
            int idx = _head;

            int iOffset = 0;
            while (iOffset < _size)
            {
                if (idx == _capacity)
                    idx = 0;

                if (_buffer[idx] == seq[idxSeq])
                {
                    // Mark where we found the first character so that we can restart the search there if the match fails,
                    //  or so that we can return the offset of the first matched char.
                    if (idxSeq == 0)
                    {
                        iOffsetFirst = iOffset;
                        idxFirst = idx;
                    }
                    // did we reach the end of the matching sequence?  If so, return the offset of the first char we matched.
                    if (++idxSeq >= lenSeq)
                        return iOffsetFirst;
                }
                else
                {
                    // mismatch - reset the search so that we pick up at the first char after the start of the current failed match
                    idxSeq = 0;
                    // if we had begun a match, pick up the search again on the first char after the start of the broken match candidate
                    if (idxFirst != -1)
                    {
                        iOffset = iOffsetFirst;
                        idx = idxFirst;
                        idxFirst = -1;
                        iOffsetFirst = -1;
                    }
                }
                ++iOffset;
                ++idx;
            }

            return -1;
        }

        public int Put(byte[] src)
        {
            return Put(src, 0, src.Length);
        }

        public int Put(byte[] src, int offset, int count)
        {
            if (count > _capacity - _size)
            {
                Grow(_size + count);
            }

            int srcIndex = offset;
            int segmentLength = count;
            if ((_capacity - _tail) < segmentLength)
                segmentLength = _capacity - _tail;

            // First segment
            Array.Copy(src, srcIndex, _buffer, _tail, segmentLength);
            _tail += segmentLength;
            if (_tail >= _capacity)
                _tail -= _capacity;

            // Optionally, a second segment
            srcIndex += segmentLength;
            segmentLength = count - segmentLength;
            if (segmentLength > 0)
            {
                Array.Copy(src, srcIndex, _buffer, _tail, segmentLength);
                _tail += segmentLength;
                if (_tail >= _capacity)
                    _tail -= _capacity;
            }

            //for (int i = 0; i < count; i++, _tail++, srcIndex++)
            //{
            //    if (_tail == _capacity)
            //        _tail = 0;
            //    _buffer[_tail] = src[srcIndex];
            //}

            _size = _size + count;
            return count;
        }

        public void Put(byte b)
        {
            if (1 > _capacity - _size)
            {
                Grow(_size + 1);
            }

            if (_tail == _capacity)
                _tail = 0;
            _buffer[_tail++] = b;
            _size = _size + 1;
        }

        private void Grow(int target)
        {
            int newCapacity = _capacity;
            while (newCapacity < target)
            {
                newCapacity = (newCapacity * _growM) + _growC;
            }
            this.Capacity = newCapacity;
        }

        public void Skip(int count)
        {
            if (count > _size)
                throw new ArgumentOutOfRangeException("count", "Skip count:" + count + " Size:" + _size);

            _head += count;
            _size -= count;

            if (_head >= _capacity)
                _head -= _capacity;
        }

        public byte[] Get(int count)
        {
            var dest = new byte[count];
            Get(dest);
            return dest;
        }

        public int Get(byte[] dst)
        {
            return Get(dst, 0, dst.Length);
        }

        public int Get(byte[] dest, int offset, int count)
        {
            if (count > _size)
                throw new ArgumentOutOfRangeException("count","Requested bytes=" + count + " Available bytes=" + _size);
            int actualCount = System.Math.Min(count, _size);
            int dstIndex = offset;
            for (int i = 0; i < actualCount; i++, _head++, dstIndex++)
            {
                if (_head == _capacity)
                    _head = 0;
                dest[dstIndex] = _buffer[_head];
            }
            _size -= actualCount;
            return actualCount;
        }

        public byte Get()
        {
            if (_size == 0)
                throw new InvalidOperationException("Empty");

            var item = _buffer[_head];
            if (++_head == _capacity)
                _head = 0;
            _size--;
            return item;
        }

        object ICollection.SyncRoot
        {
            get { return _syncRoot; }
        }

        public void CopyTo(byte[] array)
        {
            CopyTo(array, 0);
        }

        public void CopyTo(Array array, int index)
        {
            CopyTo(0, (byte[])array, index, _size);
        }

        public void CopyTo(int index, byte[] array, int arrayIndex, int count)
        {
            if (count > _size)
                throw new ArgumentOutOfRangeException("count", "Count too large");

            int bufferIndex = _head;
            for (int i = 0; i < count; i++, bufferIndex++, arrayIndex++)
            {
                if (bufferIndex == _capacity)
                    bufferIndex = 0;
                array[arrayIndex] = _buffer[bufferIndex];
            }
        }
        int ICollection.Count
        {
            get { return _size; }
        }

        bool ICollection.IsSynchronized
        {
            get { return false; }
        }

        public IEnumerator GetEnumerator()
        {
            int bufferIndex = _head;
            for (int i = 0; i < _size; i++, bufferIndex++)
            {
                if (bufferIndex == _capacity)
                    bufferIndex = 0;

                yield return _buffer[bufferIndex];
            }
        }
    }
}
