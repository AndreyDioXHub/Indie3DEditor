using System.Collections.Generic;
using Indie3DEditor.EditMesh;
using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Assemble: pack every UV island into a near-square mosaic (MaxRects, Best Short
    /// Side Fit, no rotation) at its current size, then scale the whole mosaic
    /// uniformly into the 0–1 UV square. Relative island sizes are kept.
    /// </summary>
    public static class IndieEditUVAssemble
    {
        public const float MaxPaddingPercent = 5f;

        private const string PaddingKey = "Indie3DEditor.EditUV.AssemblePaddingPercent";
        private const double PackEpsilon = 1e-9;
        private const double InitialFillRatio = 0.9;
        private const double GrowFactor = 1.05;
        private const int MaxPackAttempts = 400;
        private const float MoveEpsilon = 1e-7f;

        /// <summary>Padding as a percent (0–5) of each island's own UV area.</summary>
        public static float PaddingPercent
        {
            get => Mathf.Clamp(SessionState.GetFloat(PaddingKey, 0.25f), 0f, MaxPaddingPercent);
            set => SessionState.SetFloat(PaddingKey, Mathf.Clamp(value, 0f, MaxPaddingPercent));
        }

        private struct PackRect
        {
            public double X;
            public double Y;
            public double W;
            public double H;

            public PackRect(double x, double y, double w, double h)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
            }

            public double Right => X + W;
            public double Top => Y + H;
        }

        private sealed class Island
        {
            public readonly List<int> Vertices = new List<int>();
            public Vector2 Min;
            public Vector2 Max;
            public double Area;
            public double Margin;
            public double PackW;
            public double PackH;
            public PackRect Placed;
        }

        public static bool CanAssemble(IndieEditUVComponent uv)
        {
            return uv != null &&
                   uv.Data != null &&
                   uv.Data.faces.Count > 0 &&
                   uv.Data.uvs.Count > 0 &&
                   !IndieEditUVPelt.IsActive;
        }

        public static void Apply(IndieEditUVComponent uv)
        {
            if (!CanAssemble(uv))
                return;

            IndieEditableMeshData data = uv.Data;

            if (!TryComputeLayout(data, PaddingPercent, out Dictionary<int, Vector2> target))
                return;

            if (!AnyMoved(data, target))
                return;

            IndieEditUVUndo.Register(uv, "Assemble");

            foreach (var pair in target)
                data.uvs[pair.Key] = pair.Value;

            IndieEditUVSession.RefreshUvsOnly(uv);
            EditorUtility.SetDirty(uv);
            IndieEditUVNotify.UvsChanged(uv, target.Keys);
        }

        private static bool TryComputeLayout(
            IndieEditableMeshData data,
            float paddingPercent,
            out Dictionary<int, Vector2> target
        )
        {
            target = null;
            List<Island> islands = CollectIslands(data);

            if (islands.Count == 0)
                return false;

            double padding = paddingPercent / 100.0;
            double minMargin = SolveTotalMargin(islands, padding);
            double totalArea = 0.0;

            for (int i = 0; i < islands.Count; i++)
            {
                Island island = islands[i];
                double w = island.Max.x - island.Min.x;
                double h = island.Max.y - island.Min.y;
                island.Margin = System.Math.Max(
                    SolveMargin(w, h, island.Area * padding),
                    minMargin
                );
                island.PackW = w + 2.0 * island.Margin;
                island.PackH = h + 2.0 * island.Margin;
                totalArea += island.PackW * island.PackH;
            }

            if (!NormalizeSizes(islands, totalArea, out double normalize))
                return false;

            islands.Sort(CompareLargestFirst);

            if (!PackIntoGrowingBin(islands, out double usedW, out double usedH))
            {
                Debug.LogWarning("Edit UV Assemble: islands could not be packed.");
                return false;
            }

            double fit = 1.0 / System.Math.Max(usedW, usedH);
            target = new Dictionary<int, Vector2>();

            for (int i = 0; i < islands.Count; i++)
            {
                Island island = islands[i];
                double offsetX = island.Placed.X + island.Margin * normalize;
                double offsetY = island.Placed.Y + island.Margin * normalize;

                for (int v = 0; v < island.Vertices.Count; v++)
                {
                    int index = island.Vertices[v];
                    Vector2 local = data.uvs[index] - island.Min;
                    target[index] = new Vector2(
                        (float)((local.x * normalize + offsetX) * fit),
                        (float)((local.y * normalize + offsetY) * fit)
                    );
                }
            }

            return true;
        }

        /// <summary>
        /// Band m around a w×h box whose extra area equals <paramref name="extraArea"/>:
        /// 4m² + 2(w+h)m − extraArea = 0.
        /// </summary>
        private static double SolveMargin(double w, double h, double extraArea)
        {
            if (extraArea <= 0.0)
                return 0.0;

            double b = 2.0 * (w + h);
            return (-b + System.Math.Sqrt(b * b + 16.0 * extraArea)) / 8.0;
        }

        /// <summary>
        /// Floor for every island margin: the same band rule applied to the whole UV area
        /// as one square, so small islands still get a visible gap.
        /// </summary>
        private static double SolveTotalMargin(List<Island> islands, double padding)
        {
            if (padding <= 0.0)
                return 0.0;

            double totalUvArea = 0.0;

            for (int i = 0; i < islands.Count; i++)
                totalUvArea += islands[i].Area;

            if (totalUvArea <= 0.0)
                return 0.0;

            double side = System.Math.Sqrt(totalUvArea);
            return SolveMargin(side, side, totalUvArea * padding);
        }

        /// <summary>
        /// Scale pack sizes so the total padded area is 1; keeps packing in a stable
        /// numeric range regardless of how small the source UVs are.
        /// </summary>
        private static bool NormalizeSizes(List<Island> islands, double totalArea, out double normalize)
        {
            double maxSide = 0.0;

            for (int i = 0; i < islands.Count; i++)
                maxSide = System.Math.Max(maxSide, System.Math.Max(islands[i].PackW, islands[i].PackH));

            if (maxSide <= 0.0)
            {
                normalize = 0.0;
                return false;
            }

            normalize = totalArea > 0.0 ? 1.0 / System.Math.Sqrt(totalArea) : 1.0 / maxSide;
            double minSide = 1e-6 * maxSide * normalize;

            for (int i = 0; i < islands.Count; i++)
            {
                Island island = islands[i];
                island.PackW = System.Math.Max(island.PackW * normalize, minSide);
                island.PackH = System.Math.Max(island.PackH * normalize, minSide);
            }

            return true;
        }

        private static int CompareLargestFirst(Island a, Island b)
        {
            double sideA = System.Math.Max(a.PackW, a.PackH);
            double sideB = System.Math.Max(b.PackW, b.PackH);
            int bySide = sideB.CompareTo(sideA);

            if (bySide != 0)
                return bySide;

            return (b.PackW * b.PackH).CompareTo(a.PackW * a.PackH);
        }

        /// <summary>
        /// Start from a square sized to the padded area; when an island does not fit,
        /// grow the shorter side and repack so the mosaic stays near-square.
        /// </summary>
        private static bool PackIntoGrowingBin(List<Island> islands, out double usedW, out double usedH)
        {
            double totalArea = 0.0;
            double maxW = 0.0;
            double maxH = 0.0;

            for (int i = 0; i < islands.Count; i++)
            {
                totalArea += islands[i].PackW * islands[i].PackH;
                maxW = System.Math.Max(maxW, islands[i].PackW);
                maxH = System.Math.Max(maxH, islands[i].PackH);
            }

            double side = System.Math.Sqrt(totalArea / InitialFillRatio);
            double binW = System.Math.Max(side, maxW);
            double binH = System.Math.Max(side, maxH);

            for (int attempt = 0; attempt < MaxPackAttempts; attempt++)
            {
                if (TryPack(islands, binW, binH, out usedW, out usedH))
                    return true;

                if (binW <= binH)
                    binW *= GrowFactor;
                else
                    binH *= GrowFactor;
            }

            usedW = 0.0;
            usedH = 0.0;
            return false;
        }

        private static bool TryPack(
            List<Island> islands,
            double binW,
            double binH,
            out double usedW,
            out double usedH
        )
        {
            var free = new List<PackRect> { new PackRect(0.0, 0.0, binW, binH) };
            var split = new List<PackRect>(8);
            usedW = 0.0;
            usedH = 0.0;

            for (int i = 0; i < islands.Count; i++)
            {
                Island island = islands[i];

                if (!FindBestShortSideFit(free, island.PackW, island.PackH, out PackRect placed))
                    return false;

                island.Placed = placed;
                PlaceRect(free, split, placed);
                usedW = System.Math.Max(usedW, placed.Right);
                usedH = System.Math.Max(usedH, placed.Top);
            }

            return true;
        }

        private static bool FindBestShortSideFit(
            List<PackRect> free,
            double w,
            double h,
            out PackRect placed
        )
        {
            placed = default;
            double bestShort = double.MaxValue;
            double bestLong = double.MaxValue;
            bool found = false;

            for (int i = 0; i < free.Count; i++)
            {
                PackRect rect = free[i];

                if (rect.W + PackEpsilon < w || rect.H + PackEpsilon < h)
                    continue;

                double leftoverW = rect.W - w;
                double leftoverH = rect.H - h;
                double shortSide = System.Math.Min(leftoverW, leftoverH);
                double longSide = System.Math.Max(leftoverW, leftoverH);

                if (shortSide < bestShort || (shortSide == bestShort && longSide < bestLong))
                {
                    bestShort = shortSide;
                    bestLong = longSide;
                    placed = new PackRect(rect.X, rect.Y, w, h);
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// MaxRects split: every free rect touched by <paramref name="placed"/> is replaced
        /// by its up-to-four maximal leftovers, then contained free rects are pruned.
        /// </summary>
        private static void PlaceRect(List<PackRect> free, List<PackRect> split, PackRect placed)
        {
            split.Clear();

            for (int i = free.Count - 1; i >= 0; i--)
            {
                PackRect rect = free[i];

                if (!Intersects(rect, placed))
                    continue;

                SplitFreeRect(rect, placed, split);
                free[i] = free[free.Count - 1];
                free.RemoveAt(free.Count - 1);
            }

            for (int s = 0; s < split.Count; s++)
            {
                PackRect candidate = split[s];
                bool contained = false;

                for (int i = 0; i < free.Count; i++)
                {
                    if (Contains(free[i], candidate))
                    {
                        contained = true;
                        break;
                    }
                }

                if (contained)
                    continue;

                for (int i = free.Count - 1; i >= 0; i--)
                {
                    if (!Contains(candidate, free[i]))
                        continue;

                    free[i] = free[free.Count - 1];
                    free.RemoveAt(free.Count - 1);
                }

                free.Add(candidate);
            }
        }

        private static void SplitFreeRect(PackRect rect, PackRect placed, List<PackRect> output)
        {
            if (placed.X > rect.X + PackEpsilon)
                AddSplit(output, rect.X, rect.Y, placed.X - rect.X, rect.H);

            if (placed.Right < rect.Right - PackEpsilon)
                AddSplit(output, placed.Right, rect.Y, rect.Right - placed.Right, rect.H);

            if (placed.Y > rect.Y + PackEpsilon)
                AddSplit(output, rect.X, rect.Y, rect.W, placed.Y - rect.Y);

            if (placed.Top < rect.Top - PackEpsilon)
                AddSplit(output, rect.X, placed.Top, rect.W, rect.Top - placed.Top);
        }

        private static void AddSplit(List<PackRect> output, double x, double y, double w, double h)
        {
            if (w > PackEpsilon && h > PackEpsilon)
                output.Add(new PackRect(x, y, w, h));
        }

        private static bool Intersects(PackRect a, PackRect b)
        {
            return a.X < b.Right - PackEpsilon &&
                   b.X < a.Right - PackEpsilon &&
                   a.Y < b.Top - PackEpsilon &&
                   b.Y < a.Top - PackEpsilon;
        }

        private static bool Contains(PackRect outer, PackRect inner)
        {
            return inner.X >= outer.X - PackEpsilon &&
                   inner.Y >= outer.Y - PackEpsilon &&
                   inner.Right <= outer.Right + PackEpsilon &&
                   inner.Top <= outer.Top + PackEpsilon;
        }

        /// <summary>
        /// Island = faces connected through shared vertex indices, so pieces already
        /// split by UV seams stay separate. Faces with an index outside the UV channel are skipped.
        /// </summary>
        private static List<Island> CollectIslands(IndieEditableMeshData data)
        {
            int uvCount = data.uvs.Count;
            var parent = new int[uvCount];

            for (int i = 0; i < uvCount; i++)
                parent[i] = i;

            var validFaces = new List<int>(data.faces.Count);

            for (int f = 0; f < data.faces.Count; f++)
            {
                var face = data.faces[f];

                if (!InRange(face.a, uvCount) || !InRange(face.b, uvCount) || !InRange(face.c, uvCount))
                    continue;

                validFaces.Add(f);
                Union(parent, face.a, face.b);
                Union(parent, face.a, face.c);
            }

            var byRoot = new Dictionary<int, Island>();
            var seen = new HashSet<int>();

            for (int i = 0; i < validFaces.Count; i++)
            {
                var face = data.faces[validFaces[i]];
                int root = Find(parent, face.a);

                if (!byRoot.TryGetValue(root, out Island island))
                {
                    island = new Island
                    {
                        Min = new Vector2(float.PositiveInfinity, float.PositiveInfinity),
                        Max = new Vector2(float.NegativeInfinity, float.NegativeInfinity)
                    };
                    byRoot.Add(root, island);
                }

                Vector2 a = data.uvs[face.a];
                Vector2 b = data.uvs[face.b];
                Vector2 c = data.uvs[face.c];
                island.Area += System.Math.Abs(IndieEditMeshPickMath.Cross(b - a, c - a)) * 0.5;

                AddIslandVertex(island, seen, data, face.a);
                AddIslandVertex(island, seen, data, face.b);
                AddIslandVertex(island, seen, data, face.c);
            }

            return new List<Island>(byRoot.Values);
        }

        private static void AddIslandVertex(Island island, HashSet<int> seen, IndieEditableMeshData data, int index)
        {
            if (!seen.Add(index))
                return;

            Vector2 uv = data.uvs[index];
            island.Vertices.Add(index);
            island.Min = Vector2.Min(island.Min, uv);
            island.Max = Vector2.Max(island.Max, uv);
        }

        private static bool AnyMoved(IndieEditableMeshData data, Dictionary<int, Vector2> target)
        {
            foreach (var pair in target)
            {
                if ((data.uvs[pair.Key] - pair.Value).sqrMagnitude > MoveEpsilon * MoveEpsilon)
                    return true;
            }

            return false;
        }

        private static bool InRange(int index, int count) => index >= 0 && index < count;

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra != rb)
                parent[rb] = ra;
        }
    }
}
