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
        private static readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            AddToQueue(AssignAfterServerUpdate(syncedValue, value));
        }

        private static IEnumerator AssignAfterServerUpdate<T>(CustomSyncedValue<T> syncedValue, T value)
        {
            yield return waitForServerUpdate;
            syncedValue.AssignLocalValue(value);
            yield return waitForFixedUpdate;
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
