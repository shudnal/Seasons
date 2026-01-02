using System;
using System.Collections;
using System.Collections.Generic;
using ServerSync;
using UnityEngine;

namespace Seasons
{
    internal static class CustomSyncedValuesSynchronizer
    {
        private sealed class QueuedAssignment
        {
            public object Target; // CustomSyncedValue<T>
            public IEnumerator Coroutine;
        }

        private static readonly Queue<QueuedAssignment> coroutines = new();
        private static readonly WaitWhile waitForServerUpdate = new WaitWhile(() => ConfigSync.ProcessingServerUpdate);
        private static readonly WaitWhile waitForTextureCaching = new WaitWhile(() => Controllers.TextureCachingController.InProcess);

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            AddToQueue(syncedValue, AssignAfterServerUpdate(syncedValue, value, assignIfChanged: false));
        }

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            AddToQueue(syncedValue, AssignAfterServerUpdate(syncedValue, function, assignIfChanged: false));
        }

        public static void AssignValueIfChanged<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            AddToQueue(syncedValue, AssignAfterServerUpdate(syncedValue, value, assignIfChanged: true));
        }

        public static void AssignValueIfChanged<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            AddToQueue(syncedValue, AssignAfterServerUpdate(syncedValue, function, assignIfChanged: true));
        }

        private static IEnumerator AssignAfterServerUpdate<T>(CustomSyncedValue<T> syncedValue, Func<T> function, bool assignIfChanged)
        {
            yield return waitForServerUpdate;
            yield return waitForTextureCaching;

            T value = function();

            if (assignIfChanged && syncedValue.Value.Equals(value))
                yield break;

            syncedValue.AssignLocalValue(value);
        }

        private static IEnumerator AssignAfterServerUpdate<T>(CustomSyncedValue<T> syncedValue, T value, bool assignIfChanged)
        {
            yield return waitForServerUpdate;
            yield return waitForTextureCaching;

            if (assignIfChanged && syncedValue.Value.Equals(value))
                yield break;

            syncedValue.AssignLocalValue(value);
        }

        private static void AddToQueue<T>(CustomSyncedValue<T> syncedValue, IEnumerator coroutine)
        {
            QueuedAssignment seasonDayAssignment = null;

            int count = coroutines.Count;
            for (int i = 0; i < count; i++)
            {
                var item = coroutines.Dequeue();
                if (ReferenceEquals(item.Target, Seasons.currentSeasonDay))
                    seasonDayAssignment = item;
                else if (!ReferenceEquals(item.Target, syncedValue))
                    coroutines.Enqueue(item);
            }

            coroutines.Enqueue(new QueuedAssignment
            {
                Target = syncedValue,
                Coroutine = coroutine
            });

            if (seasonDayAssignment != null && !ReferenceEquals(seasonDayAssignment.Target, syncedValue))
                coroutines.Enqueue(seasonDayAssignment);

            if (coroutines.Count == 1)
                Seasons.instance.StartCoroutine(CoroutineCoordinator());
        }

        private static IEnumerator CoroutineCoordinator()
        {
            while (coroutines.Count > 0)
            {
                var item = coroutines.Dequeue();
                yield return Seasons.instance.StartCoroutine(item.Coroutine);
            }
        }
    }
}
