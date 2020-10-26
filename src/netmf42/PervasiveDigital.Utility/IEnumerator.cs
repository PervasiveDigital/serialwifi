using System;
using Microsoft.SPOT;

namespace System.Collections
{
    public interface IEnumerator
    {
        object Current { get; }

        bool MoveNext();
        void Reset();
    }
}
