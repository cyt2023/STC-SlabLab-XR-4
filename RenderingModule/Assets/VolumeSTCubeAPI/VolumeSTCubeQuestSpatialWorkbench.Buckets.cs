using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private string SelectedCellSnapshotId()
        {
            int index = SourceCellIndex(
                selectedGridColumn, selectedGridRow);
            index = Mathf.Clamp(index, 0, facetCellSnapshotIds.Length - 1);
            return !string.IsNullOrWhiteSpace(facetCellSnapshotIds[index])
                ? facetCellSnapshotIds[index]
                : s4dSnapshotId;
        }

        private Rect SelectedGridCellUv()
        {
            int columns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : activeGridColumns);
            int rows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : activeGridRows);
            int timeIndex = Mathf.Clamp(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow), 0, columns - 1);
            int depthIndex = Mathf.Clamp(
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow), 0, rows - 1);
            return new Rect(
                timeIndex / (float)columns,
                (rows - 1 - depthIndex) / (float)rows,
                1.0f / columns,
                1.0f / rows);
        }

        private static S4DIndexBucketRequest[] CopyAnalysisBuckets(S4DIndexBucketRequest[] source)
        {
            if (source == null) return null;
            S4DIndexBucketRequest[] copy = new S4DIndexBucketRequest[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                S4DIndexBucketRequest bucket = source[index];
                if (bucket != null)
                    copy[index] = new S4DIndexBucketRequest
                    {
                        id = bucket.id, label = bucket.label, variableId = bucket.variableId,
                        indices = bucket.indices != null ? (int[])bucket.indices.Clone() : null
                    };
            }
            return copy;
        }

        private static int SelectedBucketCount(bool[] mask)
        {
            int count = 0;
            if (mask != null)
                for (int index = 0; index < mask.Length; index++)
                    if (mask[index])
                        count++;
            return Mathf.Max(1, count);
        }

        private static int CountSelectedTicks(bool[] ticks)
        {
            int count = 0;
            if (ticks == null)
                return count;
            for (int i = 0; i < ticks.Length; i++)
                if (ticks[i])
                    count++;
            return count;
        }

        private static S4DIndexBucketRequest[] ExpandSelectedBuckets(
            S4DIndexBucketRequest[] source, bool[] selected, string prefix)
        {
            List<S4DIndexBucketRequest> result =
                new List<S4DIndexBucketRequest>();
            for (int index = 0; index < source.Length; index++)
            {
                if (index >= selected.Length || !selected[index])
                {
                    result.Add(source[index]);
                    continue;
                }
                S4DIndexBucketRequest[] children = SplitBucketIntoChildren(
                    source[index], prefix + "_" + source[index].id);
                result.AddRange(children);
            }
            return result.ToArray();
        }

        private void UpdateActiveRepresentativeIndices()
        {
            for (int column = 0; column < matrixTimes.Length; column++)
                if (activeTimeBuckets != null && column < activeTimeBuckets.Length)
                    matrixTimes[column] = RepresentativeIndex(activeTimeBuckets[column].indices);
            for (int row = 0; row < matrixDepths.Length; row++)
                if (activeDepthBuckets != null && row < activeDepthBuckets.Length)
                    matrixDepths[row] = RepresentativeIndex(activeDepthBuckets[row].indices);
        }

        private static bool TryGetIndexRange(
            S4DIndexBucketRequest bucket, out int first, out int last)
        {
            first = last = 0;
            if (bucket == null || bucket.indices == null || bucket.indices.Length == 0)
                return false;
            first = int.MaxValue;
            last = int.MinValue;
            for (int index = 0; index < bucket.indices.Length; index++)
            {
                first = Mathf.Min(first, bucket.indices[index]);
                last = Mathf.Max(last, bucket.indices[index]);
            }
            return true;
        }

        private static bool RollupGroupsAreValid(int[] groups, int count)
        {
            if (groups == null || count <= 0)
                return false;
            bool foundGroup = false;
            for (int group = 1; group <= 3; group++)
            {
                int first = -1;
                int last = -1;
                int groupCount = 0;
                for (int index = 0; index < count && index < groups.Length;
                    index++)
                {
                    if (groups[index] != group)
                        continue;
                    if (first < 0)
                        first = index;
                    last = index;
                    groupCount++;
                }
                if (groupCount == 0)
                    continue;
                foundGroup = true;
                if (groupCount < 2 || last - first + 1 != groupCount)
                    return false;
            }
            return foundGroup;
        }

        private static string AuthoredBucketSummary(
            S4DIndexBucketRequest[] buckets, bool oneBased)
        {
            string[] labels = AuthoredBucketButtonLabels(buckets, oneBased);
            for (int index = 0; index < labels.Length; index++)
                labels[index] = labels[index].Replace("\n", " ").ToLowerInvariant();
            return string.Join("  /  ", labels);
        }

        private int DisplayDepthBucketIndex(int column, int row)
        {
            return activeGridTransposed ? column : row;
        }

        private int SourceCellIndex(int column, int row)
        {
            int sourceColumns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
            int timeIndex = Mathf.Clamp(DisplayTimeBucketIndex(column, row), 0, sourceColumns - 1);
            int sourceRows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
            int depthIndex = Mathf.Clamp(DisplayDepthBucketIndex(column, row), 0, sourceRows - 1);
            return Mathf.Clamp(depthIndex * sourceColumns + timeIndex, 0,
                matrixMinimums.Length - 1);
        }

        private static void SetAllTicks(bool[] ticks, bool value)
        {
            if (ticks == null)
                return;
            for (int index = 0; index < ticks.Length; index++)
                ticks[index] = value;
        }

        private static bool BucketChanged(S4DIndexBucketRequest[] previous,
            S4DIndexBucketRequest[] current, int index)
        {
            if (previous == null || current == null ||
                index >= previous.Length || index >= current.Length)
                return true;
            int[] left = previous[index] != null ? previous[index].indices : null;
            int[] right = current[index] != null ? current[index].indices : null;
            if (left == null || right == null || left.Length != right.Length)
                return true;
            for (int value = 0; value < left.Length; value++)
                if (left[value] != right[value])
                    return true;
            return false;
        }

        private static S4DIndexBucketRequest[] FilterBucketsByMask(
            S4DIndexBucketRequest[] buckets, bool[] mask)
        {
            if (buckets == null || buckets.Length == 0 || mask == null)
                return buckets;
            List<S4DIndexBucketRequest> selected =
                new List<S4DIndexBucketRequest>();
            int count = Mathf.Min(buckets.Length, mask.Length);
            for (int index = 0; index < count; index++)
                if (mask[index] && buckets[index] != null)
                    selected.Add(buckets[index]);
            // ToggleAxisBucketSelection prevents this state, but retaining one
            // bucket here makes restored/legacy scenes safe as well.
            if (selected.Count == 0 && buckets[0] != null)
                selected.Add(buckets[0]);
            return selected.ToArray();
        }

        private string[] RequestedRematerializationCellIds(
            S4DIndexBucketRequest[] timeBuckets,
            S4DIndexBucketRequest[] depthBuckets)
        {
            if (!rematerializingStaleCells)
                return new string[0];
            List<string> result = new List<string>();
            for (int depth = 0; depth < depthBuckets.Length; depth++)
                for (int time = 0; time < timeBuckets.Length; time++)
                {
                    int index = depth * timeBuckets.Length + time;
                    if (index < rematerializedCellMask.Length &&
                        rematerializedCellMask[index])
                        result.Add(timeBuckets[time].id + "__" +
                            depthBuckets[depth].id);
                }
            return result.ToArray();
        }

        private static S4DIndexBucketRequest[] SplitBucketIntoChildren(
            S4DIndexBucketRequest source, string prefix)
        {
            int count = source.indices != null ? source.indices.Length : 0;
            if (count < 3)
                return new[] { source };
            S4DIndexBucketRequest[] children = new S4DIndexBucketRequest[3];
            for (int child = 0; child < 3; child++)
            {
                int start = Mathf.FloorToInt(child * count / 3.0f);
                int end = child == 2
                    ? count
                    : Mathf.FloorToInt((child + 1) * count / 3.0f);
                int[] indices = new int[Mathf.Max(1, end - start)];
                for (int index = 0; index < indices.Length; index++)
                    indices[index] = source.indices[Mathf.Min(start + index, count - 1)];
                children[child] = Bucket(
                    prefix + "_child_" + (child + 1),
                    source.label + " " + (child + 1),
                    indices);
            }
            return children;
        }

        private static S4DIndexBucketRequest[] MergeBucketGroups(
            S4DIndexBucketRequest[] source, int[] groups, string mergedId)
        {
            List<S4DIndexBucketRequest> result = new List<S4DIndexBucketRequest>();
            for (int index = 0; index < source.Length; index++)
            {
                int group = index < groups.Length ? groups[index] : 0;
                if (group <= 0)
                {
                    result.Add(source[index]);
                    continue;
                }
                bool firstInGroup = index == 0 || groups[index - 1] != group;
                if (!firstInGroup)
                    continue;
                List<int> mergedIndices = new List<int>();
                List<string> mergedLabels = new List<string>();
                for (int member = index; member < source.Length &&
                    member < groups.Length && groups[member] == group; member++)
                {
                    if (source[member].indices != null)
                        mergedIndices.AddRange(source[member].indices);
                    mergedLabels.Add(source[member].label);
                }
                result.Add(Bucket(
                    mergedId + "_" + group,
                    "Group " + group + " (" +
                        string.Join("+", mergedLabels.ToArray()) + ")",
                    mergedIndices.ToArray()));
            }
            return result.ToArray();
        }

        private static int RepresentativeIndex(int[] indices)
        {
            return indices == null || indices.Length == 0
                ? 0
                : indices[indices.Length / 2];
        }

        private static S4DIndexBucketRequest Bucket(string id, string label,
            int[] indices, string variableId = null)
        {
            return new S4DIndexBucketRequest
            {
                id = id,
                label = label,
                indices = indices,
                variableId = variableId
            };
        }

        private static string SafeBucketId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "variable";
            StringBuilder result = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = char.ToLowerInvariant(value[index]);
                result.Append(char.IsLetterOrDigit(character) ? character : '_');
            }
            return result.ToString().Trim('_');
        }
    }
}
