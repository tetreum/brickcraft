using System;
using System.Collections.Generic;
using System.Threading;

namespace Brickcraft
{
    /// <summary>
    /// Runs work on Unity's main thread from a background one: most of Unity's API is only safe there. Background
    /// code queues through <see cref="Run{T}"/> and waits; the main thread drains the queue every frame (see
    /// Towns.TownBuildings.Pump). Work queued while on the main thread runs right away.
    /// </summary>
    public static class MainThread
    {
        private static int mainThreadId;
        private static readonly Queue<Action> pending = new Queue<Action>();
        private static readonly object sync = new object();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void init() {
            Capture();
        }

        /// <summary>Remembers this thread as the main one (Unity's), so Run from another queues here.</summary>
        public static void Capture() {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Whether this is the main thread (true until <see cref="init"/> ran).</summary>
        public static bool IsMain {
            get { return mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == mainThreadId; }
        }

        /// <summary>
        /// Runs it on the main thread and returns its result, waiting if this isn't the main thread. If it throws, the
        /// exception is thrown here (the waiting thread's), not in the main thread's Pump.
        /// </summary>
        public static T Run<T>(Func<T> work) {
            if (IsMain) {
                return work();
            }
            T result = default(T);
            Exception failure = null;
            using (ManualResetEvent done = new ManualResetEvent(false)) {
                lock (sync) {
                    pending.Enqueue(() => {
                        try {
                            result = work();
                        } catch (Exception e) {
                            failure = e;
                        } finally {
                            done.Set();
                        }
                    });
                }
                done.WaitOne();
            }
            if (failure != null) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); // its own stack trace
            }
            return result;
        }

        /// <summary>Runs it on the main thread, waiting if this isn't the main thread.</summary>
        public static void Run(Action work) {
            Run<object>(() => {
                work();
                return null;
            });
        }

        /// <summary>
        /// Runs what background threads queued; the main thread calls this every frame. It doesn't throw: what the work
        /// throws goes back to the thread that queued it (see Run).
        /// </summary>
        public static void Pump() {
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != mainThreadId) {
                return;
            }
            while (true) {
                Action action;
                lock (sync) {
                    if (pending.Count == 0) {
                        return;
                    }
                    action = pending.Dequeue();
                }
                action();
            }
        }
    }
}