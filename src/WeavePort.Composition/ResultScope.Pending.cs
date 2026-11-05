namespace WeavePort.Composition;

public sealed partial class ResultScope
{
    private PendingResult Begin(CancellationToken cancellationToken) => new(this, cancellationToken);

    /// <summary>Creates a function output destination whose committed outputs become results of this scope.</summary>
    public ResultOutputs AsOutputDestination()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
        }

        return new ResultOutputs(this);
    }

    internal OutputReservation BeginOutput(CancellationToken cancellationToken) => new(Begin(cancellationToken));

    // Narrow handle so ResultOutputs never sees the scope's file stream or path.
    internal sealed class OutputReservation(PendingResult pending) : IAsyncDisposable
    {
        internal ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => pending.Output.WriteAsync(block, cancellationToken);
        internal Task<ResultHandle> CommitAsync() => pending.CommitAsync();
        internal void Abort(Exception cause) => pending.Abort(cause);
        public ValueTask DisposeAsync() => pending.DisposeAsync();
    }

    // One reserved result file: quota-checked while written, visible only after commit, deleted on abort.
    // It holds a scope operation so disposal waits for it, and observes the scope lifetime.
    internal sealed class PendingResult : IAsyncDisposable
    {
        private readonly ResultScope _owner;
        private readonly Operation _active;
        private readonly CancellationTokenSource _linked;
        private readonly string _id = Guid.NewGuid().ToString("N");
        private readonly FileStream? _file;
        private long _reserved;
        private bool _finished;

        internal PendingResult(ResultScope owner, CancellationToken cancellationToken)
        {
            _owner = owner;
            Output = Stream.Null;
            _active = owner.Enter();
            _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner._lifetime.Token);
            try
            {
                Reserve();
                _file = owner.CreateResultFile(_id);
                Output = new BoundedOutput(_file, ReserveBytes, _linked.Token);
            }
            catch (Exception error)
            {
                try
                {
                    Abort(error);
                }
                finally
                {
                    _linked.Dispose();
                    _active.Dispose();
                }

                throw;
            }
        }

        internal Stream Output { get; }
        internal CancellationToken Token => _linked.Token;

        internal async Task<ResultHandle> CommitAsync()
        {
            _linked.Token.ThrowIfCancellationRequested();
            await _file!.FlushAsync(_linked.Token);
            if (_file.Length != _reserved)
            {
                throw new IOException("Producer writes did not complete consistently.");
            }

            await _file.DisposeAsync();
            var handle = new ResultHandle(_id, _reserved);
            lock (_owner._sync)
            {
                _owner._results.Add(_id, handle);
            }

            _finished = true;
            return handle;
        }

        // Removes the partial file and releases its reservations; repeated calls do nothing.
        // If removal fails, the reservation is retained conservatively until scope cleanup.
        internal void Abort(Exception cause)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Output.Dispose();
            _file?.Dispose();
            _owner.RemovePartial(_id, cause);
            lock (_owner._sync)
            {
                _owner._bytes -= _reserved;
                _owner._objects--;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Abort(new OperationCanceledException("Result was neither committed nor aborted."));
            }
            finally
            {
                if (_file is not null)
                {
                    await _file.DisposeAsync();
                }

                _linked.Dispose();
                _active.Dispose();
            }
        }

        private void Reserve()
        {
            lock (_owner._sync)
            {
                if (_owner._objects >= _owner._limits.MaximumObjects)
                {
                    _finished = true;
                    throw new IOException("Result object quota exceeded.");
                }

                _owner._objects++;
            }
        }

        private void ReserveBytes(int count)
        {
            lock (_owner._sync)
            {
                if (count > _owner._limits.MaximumObjectBytes - _reserved || count > _owner._limits.MaximumScopeBytes - _owner._bytes)
                {
                    throw new IOException("Result byte quota exceeded.");
                }

                _reserved += count;
                _owner._bytes += count;
            }
        }
    }
}
