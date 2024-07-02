using System;
using System.Collections;
using System.Collections.Generic;
using ServerSync;
using UnityEngine;

namespace Seasons
{
    internal static class CustomSyncedValuesSynchronizer
    {
        private static readonly Queue<IEnumerator> coroutines = new Queue<IEnumerator>();
        private static readonly WaitWhile waitForServerUpdate = new WaitWhile(() => ConfigSync.ProcessingServerUpdate);

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            AddToQueue(AssignAfterServerUpdate(syncedValue, value, assignIfChanged: false));
        }

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            AddToQueue(AssignAfterServerUpdate(syncedValue, function, assignIfChanged: false));
        }

        public static void AssignValueIfChanged<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            AddToQueue(AssignAfterServerUpdate(syncedValue, value, assignIfChanged: true));
        }

        public static void AssignValueIfChanged<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            AddToQueue(AssignAfterServerUpdate(syncedValue, function, assignIfChanged: true));
        }

        private static IEnumerator AssignAfterServerUpdate<T>(CustomSyncedValue<T> syncedValue, Func<T> function, bool assignIfChanged)
        {
            yield return AssignAfterServerUpdate(syncedValue, function(), assignIfChanged);
        }

        private static IEnumerator AssignAfterServerUpdate<T>(CustomSyncedValue<T> syncedValue, T value, bool assignIfChanged)
        {
            if (assignIfChanged && syncedValue.Value.Equals(value))
                yield break;

            yield return waitForServerUpdate;
            syncedValue.AssignLocalValue(value);
        }

        private static void AddToQueue(IEnumerator coroutine)
        {
            coroutines.Enqueue(coroutine);
            if (coroutines.Count == 1)
                Seasons.instance.StartCoroutine(CoroutineCoordinator());
        }

        private static IEnumerator CoroutineCoordinator()
        {
            while (true)
            {
                while (coroutines.Count > 0)
                {
                    yield return Seasons.instance.StartCoroutine(coroutines.Peek());
                    coroutines.Dequeue();
                }
                if (coroutines.Count == 0)
                    yield break;
            }
        }
    }
}
