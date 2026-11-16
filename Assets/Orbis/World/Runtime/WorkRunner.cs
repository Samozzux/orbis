using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Esegue lavori pesanti (decompressione, mesh dei tile) senza bloccare il gioco (CLAUDE.md: niente lavoro
    /// pesante sul main thread). Dove ci sono i thread (iOS, Android, desktop) usa un thread di lavoro;
    /// sul web (niente thread) avanza i lavori a passi con un limite di millisecondi per frame.
    /// Un lavoro è un IEnumerator: ogni "yield" è un punto in cui si può fermare.
    /// </summary>
    public sealed class WorkRunner : IDisposable
    {
        private sealed class Job
        {
            public IEnumerator<object> Steps;
            public Action Done;
            public Action<Exception> Failed;
            public float Priority;
            public Exception Error;
        }

        private readonly bool _threaded;
        private readonly List<Job> _pending = new List<Job>();
        private readonly Queue<Job> _finished = new Queue<Job>();
        private readonly object _lock = new object();
        private readonly Thread _thread;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private volatile bool _stop;
        private Job _current;

        /// <summary>Millisecondi per frame dedicati ai lavori quando non ci sono thread.</summary>
        public float FrameBudgetMs = 6f;

        public bool Threaded => _threaded;

        public int PendingCount
        {
            get
            {
                lock (_lock) return _pending.Count + (_current != null ? 1 : 0);
            }
        }

        public WorkRunner(bool? threaded = null)
        {
            _threaded = threaded ?? Application.platform != RuntimePlatform.WebGLPlayer;
            if (_threaded)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = "Orbis.World", Priority = System.Threading.ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }

        /// <summary>Accoda un lavoro. <paramref name="done"/> e <paramref name="failed"/> vengono chiamati sul main thread.</summary>
        public void Enqueue(IEnumerator<object> steps, float priority, Action done, Action<Exception> failed = null)
        {
            lock (_lock) _pending.Add(new Job { Steps = steps, Priority = priority, Done = done, Failed = failed });
            _signal.Set();
        }

        /// <summary>Va chiamato a ogni frame dal main thread: consegna i lavori finiti (e sul web li esegue).</summary>
        public void Tick()
        {
            if (!_threaded)
            {
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalMilliseconds < FrameBudgetMs)
                {
                    if (_current == null && !TakeNext(out _current)) break;
                    if (!Advance(_current))
                    {
                        lock (_lock) _finished.Enqueue(_current);
                        _current = null;
                    }
                }
            }
            while (true)
            {
                Job j;
                lock (_lock)
                {
                    if (_finished.Count == 0) break;
                    j = _finished.Dequeue();
                }
                if (j.Error != null)
                {
                    if (j.Failed != null) j.Failed(j.Error);
                    else UnityEngine.Debug.LogException(j.Error);
                }
                else j.Done?.Invoke();
            }
        }

        private bool TakeNext(out Job job)
        {
            lock (_lock)
            {
                job = null;
                if (_pending.Count == 0) return false;
                int best = 0;
                for (int i = 1; i < _pending.Count; i++)
                    if (_pending[i].Priority < _pending[best].Priority) best = i;
                job = _pending[best];
                _pending.RemoveAt(best);
                return true;
            }
        }

        /// <summary>Fa un passo del lavoro; false quando è finito (o in errore).</summary>
        private static bool Advance(Job j)
        {
            try
            {
                return j.Steps.MoveNext();
            }
            catch (Exception e)
            {
                j.Error = e;
                return false;
            }
        }

        private void Loop()
        {
            while (!_stop)
            {
                if (!TakeNext(out var job))
                {
                    _signal.WaitOne(100);
                    continue;
                }
                lock (_lock) _current = job;
                while (!_stop && Advance(job)) { }
                lock (_lock)
                {
                    _current = null;
                    _finished.Enqueue(job);
                }
            }
        }

        /// <summary>Toglie i lavori non ancora iniziati che soddisfano la condizione.</summary>
        public void Cancel(Predicate<IEnumerator<object>> match)
        {
            lock (_lock) _pending.RemoveAll(j => match(j.Steps));
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
        }
    }
}
