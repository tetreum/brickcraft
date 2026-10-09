using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brickcraft.Events
{
    /// <summary>
    /// One kind of event, see <see cref="EventManager"/>: whoever cares subscribes, whoever makes it
    /// happen raises it, and neither needs to know about the other.
    /// </summary>
    /// <typeparam name="T">What subscribers are told about it.</typeparam>
    public sealed class GameEvent<T> where T : class
    {
        private readonly List<Action<T>> listeners = new List<Action<T>>();
        // listeners can (un)subscribe while being told, so raising goes through a copy
        private Action<T>[] snapshot = new Action<T>[0];
        private bool isSnapshotStale;

        public void Subscribe(Action<T> listener) {
            if (listener != null && !listeners.Contains(listener)) {
                listeners.Add(listener);
                isSnapshotStale = true;
            }
        }

        public void Unsubscribe(Action<T> listener) {
            if (listeners.Remove(listener)) {
                isSnapshotStale = true;
            }
        }

        /// <summary>Tells every subscriber. One failing doesn't keep the others from being told.</summary>
        public void Raise(T data) {
            if (isSnapshotStale) {
                snapshot = listeners.ToArray();
                isSnapshotStale = false;
            }
            foreach (Action<T> listener in snapshot) {
                try {
                    listener(data);
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
        }

        public void Clear() {
            listeners.Clear();
            isSnapshotStale = true;
        }
    }
}
