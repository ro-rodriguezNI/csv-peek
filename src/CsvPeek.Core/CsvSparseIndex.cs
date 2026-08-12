namespace CsvPeek.Core;

public sealed class CsvSparseIndex
{
    private readonly object _gate = new();
    private readonly List<long> _offsets = [];

    public CsvSparseIndex(int interval = 2048) => Interval = interval;

    public int Interval { get; }
    public long RecordsScanned { get; private set; }
    public long RecordCount { get; private set; }
    public bool IsComplete { get; private set; }

    public void Reset(long firstOffset)
    {
        lock (_gate)
        {
            _offsets.Clear();
            _offsets.Add(firstOffset);
            RecordsScanned = 0;
            RecordCount = 0;
            IsComplete = false;
        }
    }

    public void Observe(CsvRecord record)
    {
        lock (_gate)
        {
            if (record.Number % Interval == 0)
            {
                int slot = checked((int)(record.Number / Interval));
                if (slot == _offsets.Count)
                    _offsets.Add(record.StartOffset);
                else if (slot < _offsets.Count)
                    _offsets[slot] = record.StartOffset;
            }
            RecordsScanned = Math.Max(RecordsScanned, record.Number + 1);
        }
    }

    public void Complete(long recordCount)
    {
        lock (_gate)
        {
            RecordsScanned = recordCount;
            RecordCount = recordCount;
            IsComplete = true;
        }
    }

    public (long RecordNumber, long Offset) FindCheckpoint(long rawRecordNumber)
    {
        lock (_gate)
        {
            if (_offsets.Count == 0)
                throw new InvalidOperationException("El índice todavía no contiene ningún punto de acceso.");
            int slot = (int)Math.Min(rawRecordNumber / Interval, _offsets.Count - 1L);
            return ((long)slot * Interval, _offsets[slot]);
        }
    }

    public long[] SnapshotOffsets()
    {
        lock (_gate)
            return _offsets.ToArray();
    }

    internal void Load(long[] offsets, long recordCount)
    {
        lock (_gate)
        {
            _offsets.Clear();
            _offsets.AddRange(offsets);
            RecordsScanned = recordCount;
            RecordCount = recordCount;
            IsComplete = true;
        }
    }
}

