﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Lucene.Net.Util
{
    public unsafe class UnmanagedStringArray
    {
        private class Segment : IDisposable
        {
            public readonly int Size;

            public byte* Start;
            public byte* CurrentPosition => Start + Used;
            public int Free => Size - Used;
            public int Used;

            public Segment(int size)
            {
                Start = (byte*) Marshal.AllocHGlobal(size);
                Used = 0;
                Size = size;
                MemoryMonitor.Instance.Add(size);
            }

            public void Add(ushort size, out byte* position)
            {
                position = CurrentPosition;
                *(ushort*) CurrentPosition = size;

                Used += sizeof(short) + size;
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                if (Start != null)
                {
                    Marshal.FreeHGlobal((IntPtr) Start);
                    MemoryMonitor.Instance.Free(Size);
                }
                Start = null;
            }

            ~Segment()
            {
                Dispose();
            }
        }

        public struct UnmanagedString : IComparable
        {
            public byte* Start;

            public int Size => IsNull ? 0 : *(ushort*) Start;
            public Span<byte> StringAsBytes => new Span<byte>(Start + sizeof(ushort), Size);
            public bool IsNull => Start == default;
            
            public override string ToString()
            {
                return Encoding.UTF8.GetString(StringAsBytes.ToArray());
            }

            public static int CompareOrdinal(UnmanagedString strA, UnmanagedString strB)
            {
                if (strA.IsNull && strB.IsNull)
                    return 0;

                if (strB.IsNull)
                    return 1;

                if (strA.IsNull)
                    return -1;

                return strA.StringAsBytes.SequenceCompareTo(strB.StringAsBytes);
            }

            public static int CompareOrdinal(UnmanagedString strA, Span<byte> strB)
            {
                if (strA.IsNull && strB == null)
                    return 0;

                if (strB == null)
                    return 1;

                if (strA.IsNull)
                    return -1;

                return strA.StringAsBytes.SequenceCompareTo(strB);
            }

            public static int CompareOrdinal(Span<byte> strA, UnmanagedString strB)
            {
                return -CompareOrdinal(strB, strA);
            }

            public int CompareTo(object other)
            {
                return CompareOrdinal(this, (UnmanagedString) other);
            }
        }

        private UnmanagedString[] _strings;
        private List<Segment> _segments = new List<Segment>();

        public int Length => _index;
        public int _index = 1;

        public UnmanagedStringArray(int size)
        {
            _strings = new UnmanagedString[size];
        }

        private Segment GetSegment(int size)
        {
            if (_segments.Count == 0)
                return GetAndAddNewSegment(4096);

            // naive but simple
            var seg = _segments[_segments.Count - 1];
            if (seg.Free > size)
                return seg;

            return GetAndAddNewSegment(Math.Min(1024 * 1024, seg.Size * 2));
        }

        private Segment GetAndAddNewSegment(int segmentSize)
        {
            var newSegment = new Segment(segmentSize);
            _segments.Add(newSegment);
            return newSegment;
        }

        public void Add(Span<char> str)
        {
            var size = (ushort) Encoding.UTF8.GetByteCount(str);
            var segment = GetSegment(size + sizeof(ushort));

            segment.Add(size, out var position);

            Encoding.UTF8.GetBytes(str, new Span<byte>(position + sizeof(ushort), size));

            _strings[_index].Start = position;
            _index++;
        }

        public UnmanagedString this[int position]
        {
            get => _strings[position];
            set => _strings[position] = value;
        }

        public class MemoryMonitor
        {
            public static readonly MemoryMonitor Instance;

            private int _numberOfSegments;
            public int NumberOfSegments => _numberOfSegments;

            private long _allocatedMemory;
            public long AllocatedMemory => _allocatedMemory;

            static MemoryMonitor()
            {
                Instance = new MemoryMonitor();
            }

            public void Add(int size)
            {
                Interlocked.Increment(ref _numberOfSegments);
                Interlocked.Add(ref _allocatedMemory, size);
            }

            public void Free(int size)
            {
                Interlocked.Decrement(ref _numberOfSegments);
                Interlocked.Add(ref _allocatedMemory, -size);
            }
        }
    }
}
