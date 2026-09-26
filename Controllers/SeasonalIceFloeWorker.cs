using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Seasons
{
    // One background calculator for all floes. Only queue bookkeeping is locked;
    // neither Unity's main thread nor the worker waits for the other's calculations.
    internal static class FloeForecastWorker
    {
        internal sealed class Result
        {
            internal readonly FloeWaveMath.Sample[] Samples;
            internal readonly string Error;
            internal readonly double QueueMilliseconds, WorkMilliseconds;
            internal Result(FloeWaveMath.Sample[] samples, string error, double queued, double work)
            { Samples = samples; Error = error; QueueMilliseconds = queued; WorkMilliseconds = work; }
        }

        internal sealed class Ticket
        {
            internal readonly FloeWaveMath.Input Input;
            internal readonly double Start, Step, Deadline;
            internal readonly int Count, Epoch;
            internal readonly long Sequence, EnqueuedAt;
            internal readonly bool HasFirst;
            internal readonly FloeWaveMath.Sample First;
            internal int Cancelled;
            internal Result Completion;
            internal double End => Start + Step * (Count - 1);

            internal Ticket(FloeWaveMath.Input input, double start, double end, double deadline,
                int epoch, long sequence, bool hasFirst, FloeWaveMath.Sample first)
            {
                Input = input;
                Start = start;
                Count = Math.Max(2, (int)Math.Ceiling((end - start) / input.Spacing) + 1);
                Step = (end - start) / (Count - 1);
                Deadline = deadline;
                Epoch = epoch;
                Sequence = sequence;
                HasFirst = hasFirst;
                First = first;
                EnqueuedAt = Stopwatch.GetTimestamp();
            }

            internal Result TakeResult() => Interlocked.Exchange(ref Completion, null);
        }

        private sealed class Order : IComparer<Ticket>
        {
            public int Compare(Ticket a, Ticket b)
            {
                if (ReferenceEquals(a, b)) return 0;
                int comparison = a.Deadline.CompareTo(b.Deadline);
                return comparison != 0 ? comparison : a.Sequence.CompareTo(b.Sequence);
            }
        }

        private static readonly object gate = new object();
        private static readonly SortedSet<Ticket> pending = new SortedSet<Ticket>(new Order());
        private const int Capacity = 2048;
        private static Thread thread;
        private static int epoch, stopping;
        private static long sequence;
        private static string startupError;
        internal static long Submitted, Finished, Cancelled, Rejected, Errors, Knots;
        internal static long TotalWorkTicks, TotalQueueTicks;
        internal static int Active, HighWater;
        internal static int Epoch => Volatile.Read(ref epoch);
        internal static string StartupError => startupError;
        internal static int Queued { get { lock (gate) return pending.Count; } }

        internal static Ticket Submit(FloeWaveMath.Input input, double start, double end, double deadline,
            bool hasFirst, FloeWaveMath.Sample first)
        {
            if (input == null || double.IsNaN(start) || double.IsInfinity(start) || !(end > start) ||
                double.IsInfinity(end) || double.IsNaN(deadline) || double.IsInfinity(deadline))
                return null;
            lock (gate)
            {
                if (stopping != 0 || pending.Count >= Capacity)
                {
                    Interlocked.Increment(ref Rejected);
                    return null;
                }
                if (thread == null)
                {
                    try
                    {
                        thread = new Thread(Run) { IsBackground = true, Name = "Seasons floe wave forecasts" };
                        thread.Start();
                    }
                    catch (Exception exception)
                    {
                        startupError = exception.Message;
                        stopping = 1;
                        Interlocked.Increment(ref Errors);
                        return null;
                    }
                }
                Ticket ticket = new Ticket(input, start, end, deadline, epoch, ++sequence, hasFirst, first);
                pending.Add(ticket);
                HighWater = Math.Max(HighWater, pending.Count);
                Interlocked.Increment(ref Submitted);
                Monitor.Pulse(gate);
                return ticket;
            }
        }

        internal static void Cancel(Ticket ticket)
        {
            if (ticket == null || Interlocked.Exchange(ref ticket.Cancelled, 1) != 0)
                return;
            Interlocked.Increment(ref Cancelled);
            lock (gate) pending.Remove(ticket);
            // Completion is immutable; dropping the reference never races with array reuse.
            ticket.TakeResult();
        }

        internal static void ResetWorld()
        {
            lock (gate)
            {
                Interlocked.Increment(ref epoch);
                foreach (Ticket ticket in pending)
                {
                    Interlocked.Exchange(ref ticket.Cancelled, 1);
                    Interlocked.Increment(ref Cancelled);
                }
                pending.Clear();
                Monitor.Pulse(gate);
            }
        }

        internal static void ResetCounters()
        {
            Interlocked.Exchange(ref Submitted, 0);
            Interlocked.Exchange(ref Finished, 0);
            Interlocked.Exchange(ref Cancelled, 0);
            Interlocked.Exchange(ref Rejected, 0);
            Interlocked.Exchange(ref Errors, 0);
            Interlocked.Exchange(ref Knots, 0);
            Interlocked.Exchange(ref TotalWorkTicks, 0);
            Interlocked.Exchange(ref TotalQueueTicks, 0);
            lock (gate) HighWater = pending.Count;
        }

        internal static void Shutdown()
        {
            lock (gate)
            {
                Volatile.Write(ref stopping, 1);
                ResetWorld();
                Monitor.PulseAll(gate);
            }
            // No Join/Wait on the main thread. A running small block observes cancellation
            // between knots and exits. A world reset keeps this one thread asleep for reuse.
        }

        private static bool Invalid(Ticket ticket) => Volatile.Read(ref stopping) != 0 ||
            Volatile.Read(ref ticket.Cancelled) != 0 || ticket.Epoch != Epoch;

        private static void Run()
        {
            while (true)
            {
                Ticket ticket;
                lock (gate)
                {
                    while (pending.Count == 0 && stopping == 0)
                        Monitor.Wait(gate);
                    if (stopping != 0) return;
                    ticket = pending.Min;
                    pending.Remove(ticket);
                }
                if (Invalid(ticket)) continue;
                Volatile.Write(ref Active, 1);
                long started = Stopwatch.GetTimestamp();
                FloeWaveMath.Sample[] samples = null;
                string error = null;
                int calculated = 0;
                try
                {
                    samples = new FloeWaveMath.Sample[ticket.Count];
                    int first = 0;
                    if (ticket.HasFirst)
                    {
                        samples[0] = ticket.First;
                        first = 1; // Appending never recomputes an already accepted endpoint.
                    }
                    for (int i = first; i < ticket.Count; i++)
                    {
                        if (Invalid(ticket)) break;
                        samples[i] = ticket.Input.Evaluate(ticket.Start + ticket.Step * i);
                        calculated++;
                    }
                }
                catch (Exception exception)
                {
                    error = exception.GetType().Name + ": " + exception.Message;
                    samples = null;
                    Interlocked.Increment(ref Errors);
                }
                long ended = Stopwatch.GetTimestamp();
                Volatile.Write(ref Active, 0);
                Interlocked.Add(ref Knots, calculated);
                Interlocked.Add(ref TotalWorkTicks, ended - started);
                Interlocked.Add(ref TotalQueueTicks, started - ticket.EnqueuedAt);
                if (Invalid(ticket)) continue;
                Result result = new Result(samples, error,
                    (started - ticket.EnqueuedAt) * 1000.0 / Stopwatch.Frequency,
                    (ended - started) * 1000.0 / Stopwatch.Frequency);
                Volatile.Write(ref ticket.Completion, result);
                Interlocked.Increment(ref Finished);
            }
        }
    }
}
