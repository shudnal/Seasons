using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ConditionalConfigSync;
using UnityEngine;

namespace Seasons
{
    internal sealed class DictionaryContentComparer<TKey, TValue> : IEqualityComparer<Dictionary<TKey, TValue>>
    {
        public static readonly DictionaryContentComparer<TKey, TValue> Instance = new DictionaryContentComparer<TKey, TValue>();

        public bool Equals(Dictionary<TKey, TValue> first, Dictionary<TKey, TValue> second)
        {
            if (ReferenceEquals(first, second))
                return true;

            if (first == null || second == null || first.Count != second.Count)
                return false;

            EqualityComparer<TValue> valueComparer = EqualityComparer<TValue>.Default;
            foreach (KeyValuePair<TKey, TValue> item in first)
            {
                if (!second.TryGetValue(item.Key, out TValue value) || !valueComparer.Equals(item.Value, value))
                    return false;
            }

            return true;
        }

        public int GetHashCode(Dictionary<TKey, TValue> dictionary)
        {
            return dictionary?.Count ?? 0;
        }
    }

    internal static class CustomSyncedValuesSynchronizer
    {
        private enum AssignmentMode
        {
            Default,
            IfChanged,
            AndNotify,
        }

        private sealed class PendingAssignment
        {
            public CustomSyncedValueBase Target;
            public Action AssignDefault;
            public Action AssignIfChanged;
            public Action AssignAndNotify;
            public AssignmentMode Mode;
            public long Sequence;
        }

        private static readonly Dictionary<CustomSyncedValueBase, PendingAssignment> pendingAssignments = new();
        private static readonly WaitWhile waitForTextureCaching = new WaitWhile(() => Controllers.TextureCachingController.InProcess);

        private static bool coordinatorRunning;
        private static long nextSequence;

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(value),
                () => syncedValue.AssignLocalValueIfChanged(value),
                () => syncedValue.AssignLocalValueAndNotify(value),
                AssignmentMode.Default);
        }

        public static void AssignValueSafe<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(function()),
                () => syncedValue.AssignLocalValueIfChanged(function()),
                () => syncedValue.AssignLocalValueAndNotify(function()),
                AssignmentMode.Default);
        }

        public static void AssignValueSafeIfChanged<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(value),
                () => syncedValue.AssignLocalValueIfChanged(value),
                () => syncedValue.AssignLocalValueAndNotify(value),
                AssignmentMode.IfChanged);
        }

        public static void AssignValueSafeIfChanged<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(function()),
                () => syncedValue.AssignLocalValueIfChanged(function()),
                () => syncedValue.AssignLocalValueAndNotify(function()),
                AssignmentMode.IfChanged);
        }

        public static void AssignValueSafeAndNotify<T>(this CustomSyncedValue<T> syncedValue, T value)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(value),
                () => syncedValue.AssignLocalValueIfChanged(value),
                () => syncedValue.AssignLocalValueAndNotify(value),
                AssignmentMode.AndNotify);
        }

        public static void AssignValueSafeAndNotify<T>(this CustomSyncedValue<T> syncedValue, Func<T> function)
        {
            QueueAssignment(
                syncedValue,
                () => syncedValue.AssignLocalValue(function()),
                () => syncedValue.AssignLocalValueIfChanged(function()),
                () => syncedValue.AssignLocalValueAndNotify(function()),
                AssignmentMode.AndNotify);
        }

        private static void QueueAssignment(
            CustomSyncedValueBase syncedValue,
            Action assignDefault,
            Action assignIfChanged,
            Action assignAndNotify,
            AssignmentMode mode)
        {
            if (!Controllers.TextureCachingController.InProcess && pendingAssignments.Count == 0 && !coordinatorRunning)
            {
                ApplyAssignment(assignDefault, assignIfChanged, assignAndNotify, mode);
                return;
            }

            if (pendingAssignments.TryGetValue(syncedValue, out PendingAssignment pending))
            {
                // Keep the newest value/function. A queued forced notification is preserved so
                // initial data processing cannot be lost to a later equal file watcher update.
                pending.AssignDefault = assignDefault;
                pending.AssignIfChanged = assignIfChanged;
                pending.AssignAndNotify = assignAndNotify;
                pending.Mode = MergeModes(pending.Mode, mode);
            }
            else
            {
                pendingAssignments.Add(syncedValue, new PendingAssignment
                {
                    Target = syncedValue,
                    AssignDefault = assignDefault,
                    AssignIfChanged = assignIfChanged,
                    AssignAndNotify = assignAndNotify,
                    Mode = mode,
                    Sequence = nextSequence++,
                });
            }

            if (!coordinatorRunning)
            {
                coordinatorRunning = true;
                Seasons.instance.StartCoroutine(AssignmentCoordinator());
            }
        }

        private static AssignmentMode MergeModes(AssignmentMode current, AssignmentMode next)
        {
            return current == AssignmentMode.AndNotify || next == AssignmentMode.AndNotify
                ? AssignmentMode.AndNotify
                : next;
        }

        private static void ApplyAssignment(
            Action assignDefault,
            Action assignIfChanged,
            Action assignAndNotify,
            AssignmentMode mode)
        {
            switch (mode)
            {
                case AssignmentMode.AndNotify:
                    assignAndNotify();
                    break;
                case AssignmentMode.IfChanged:
                    assignIfChanged();
                    break;
                default:
                    assignDefault();
                    break;
            }
        }

        private static IEnumerator AssignmentCoordinator()
        {
            try
            {
                while (pendingAssignments.Count > 0)
                {
                    yield return waitForTextureCaching;

                    PendingAssignment[] assignments = pendingAssignments.Values
                        .OrderByDescending(assignment => assignment.Target.Priority)
                        .ThenBy(assignment => assignment.Sequence)
                        .ToArray();

                    pendingAssignments.Clear();

                    foreach (PendingAssignment assignment in assignments)
                    {
                        ApplyAssignment(
                            assignment.AssignDefault,
                            assignment.AssignIfChanged,
                            assignment.AssignAndNotify,
                            assignment.Mode);
                    }
                }
            }
            finally
            {
                coordinatorRunning = false;

                // An assignment may have been queued after the loop condition but before the coroutine completed.
                if (pendingAssignments.Count > 0)
                {
                    coordinatorRunning = true;
                    Seasons.instance.StartCoroutine(AssignmentCoordinator());
                }
            }
        }
    }
}
