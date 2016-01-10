﻿using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;


namespace RioSharp
{
    public class RioFixedBufferPool : IDisposable
    {
        internal IntPtr BufferPointer, segmentpointer;
        internal int TotalLength;
        ConcurrentQueue<RioBufferSegment> _availableSegments = new ConcurrentQueue<RioBufferSegment>();
        internal RioBufferSegment[] allSegments;

        public RioFixedBufferPool(int segmentCount, int segmentLength)
        {
            allSegments = new RioBufferSegment[segmentCount];
            TotalLength = segmentCount * segmentLength;
            BufferPointer = Marshal.AllocHGlobal(TotalLength);
            segmentpointer = Marshal.AllocHGlobal(Marshal.SizeOf<RIO_BUFSEGMENT>() * segmentCount);
            
            for (int i = 0; i < segmentCount; i++)
            {
                var b = new RioBufferSegment(this, BufferPointer ,segmentpointer, i, segmentLength );
                allSegments[i] = b;
                _availableSegments.Enqueue(b);
            }
        }

        public void SetBufferId(IntPtr id)
        {
            for (int i = 0; i < allSegments.Length; i++)
                allSegments[i].SetBufferId(id);
        }

        public bool TryGetBuffer(out RioBufferSegment buf)
        {
            return _availableSegments.TryDequeue(out buf);
        }

        public RioBufferSegment GetBuffer()
        {
            RioBufferSegment buf;
            do
            {
                if (_availableSegments.TryDequeue(out buf))
                    return buf;
            } while (true);
        }

        public RioBufferSegment GetBuffer(int requestedBufferSize)
        {
            RioBufferSegment buf;
            do
            {
                if (_availableSegments.TryDequeue(out buf))
                    return buf;
            } while (true);
        }

        public void ReleaseBuffer(RioBufferSegment bufferIndex)
        {
            _availableSegments.Enqueue(bufferIndex);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(BufferPointer);
            Marshal.FreeHGlobal(segmentpointer);
        }
    }
}