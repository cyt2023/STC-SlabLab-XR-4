using System.Collections.Generic;

namespace UnityVolumeRendering
{
    /// <summary>Keeps view lifetime bookkeeping out of the public API facade.</summary>
    internal static class VolumeSTCubeViewRegistry
    {
        private static readonly Dictionary<string, VolumeSTCubeView> Views =
            new Dictionary<string, VolumeSTCubeView>();

        public static bool Contains(string viewId)
        {
            return !string.IsNullOrEmpty(viewId) && Views.ContainsKey(viewId);
        }

        public static void AddOrReplace(VolumeSTCubeView view)
        {
            if (view != null && !string.IsNullOrEmpty(view.viewId))
                Views[view.viewId] = view;
        }

        public static VolumeSTCubeView Get(string viewId)
        {
            if (string.IsNullOrEmpty(viewId))
                return null;
            Views.TryGetValue(viewId, out VolumeSTCubeView view);
            return view;
        }

        public static bool Remove(string viewId)
        {
            return !string.IsNullOrEmpty(viewId) && Views.Remove(viewId);
        }

        public static List<VolumeSTCubeView> Snapshot()
        {
            return new List<VolumeSTCubeView>(Views.Values);
        }

        public static void Clear()
        {
            Views.Clear();
        }
    }
}
