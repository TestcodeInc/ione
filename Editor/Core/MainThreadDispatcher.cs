using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Ione.Core
{
    // Pumps queued Actions on EditorApplication.update so background
    // threads can marshal work back to the main thread.
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        static readonly Queue<Action> queue = new Queue<Action>();
        static readonly object queueLock = new object();

        public static volatile bool IsCompilingCached;
        public static volatile bool IsUpdatingCached;

        static MainThreadDispatcher()
        {
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            IsCompilingCached = EditorApplication.isCompiling;
            IsUpdatingCached = EditorApplication.isUpdating;
            while (true)
            {
                Action a;
                lock (queueLock)
                {
                    if (queue.Count == 0) break;
                    a = queue.Dequeue();
                }
                try { a(); } catch (Exception e) { Debug.LogError($"[ione] main action error: {e}"); }
            }
        }

        public static void Post(Action a)
        {
            if (a == null) return;
            lock (queueLock) queue.Enqueue(a);
        }

        public static T RunOnMain<T>(Func<T> fn)
        {
            T result = default;
            Exception err = null;
            var reset = new ManualResetEventSlim(false);
            Post(() =>
            {
                try { result = fn(); }
                catch (Exception e) { err = e; }
                finally { reset.Set(); }
            });
            if (!reset.Wait(TimeSpan.FromSeconds(5)))
            {
                Debug.LogWarning($"[ione] main thread busy >5s (compiling={IsCompilingCached}, updating={IsUpdatingCached})");
                if (!reset.Wait(TimeSpan.FromSeconds(115)))
                    throw new Exception("Main thread timeout (120s) - editor frozen");
            }
            if (err != null) throw err;
            return result;
        }
    }
}
