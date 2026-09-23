using System;
using Psd2Ugui.Core.Contract;

namespace Psd2Ugui.Core.Imaging
{
    /// <summary>九宫检测的可调项。</summary>
    public sealed class NineSliceOptions
    {
        /// <summary>先去掉四周全透明的边（UI 图通常需要）。</summary>
        public bool TrimTransparent = true;

        public int AlphaThreshold;

        /// <summary>把可拉伸区裁到 1 像素，导出体积最小。</summary>
        public bool Minimize = true;

        /// <summary>判定相邻行/列“相同”时允许的通道误差。</summary>
        public int Tolerance;
    }

    /// <summary>九宫检测结果。</summary>
    public sealed class NineSliceResult
    {
        /// <summary>最终要导出的图（已去空、已最小化）。整张透明时为 null。</summary>
        public Bitmap Sprite;

        /// <summary>基于 Sprite 的九宫边框，全 0 表示不需要九宫。</summary>
        public UiBorder Border = new UiBorder(0, 0, 0, 0);

        /// <summary>Sprite 在输入位图里的位置。</summary>
        public UiRect SourceRect = UiRect.Empty;

        /// <summary>整张图是同一个颜色（可以退化成纯色填充）。</summary>
        public bool IsUniform;

        /// <summary>是否给出了九宫（边框非全 0）。</summary>
        public bool IsSliceable
        {
            get { return !Border.IsZero; }
        }

        /// <summary>结论说明，用于诊断与调试。</summary>
        public string Reason = string.Empty;

        public override string ToString()
        {
            return (Sprite == null ? "空" : Sprite.Width + "x" + Sprite.Height) +
                   " border(" + Border.Left + "," + Border.Bottom + "," + Border.Right + "," + Border.Top + ") " +
                   Reason;
        }
    }

    /// <summary>
    /// 九宫检测：靠“相邻行/列完全一致”来定位可拉伸区。
    ///
    /// 判定依据是 uGUI 的九宫语义：
    /// - 上下边框之间的行必须彼此相同 → 纵向拉伸时画面不变
    /// - 左右边框之间的列必须彼此相同 → 横向拉伸时画面不变
    /// - 两个区间的交集自然就是纯色，缩放不会糊掉细节
    ///
    /// 圆角矩形会拿到“圆角半径”作为边框，纯色/直角矩形拿到全 0（无需九宫），
    /// 中间有渐变或纹理的图会判定为不可九宫，避免拉伸时出现鬼影。
    /// </summary>
    public static class NineSliceDetector
    {
        public static NineSliceResult Detect(Bitmap bitmap, NineSliceOptions options = null)
        {
            options = options ?? new NineSliceOptions();
            var result = new NineSliceResult();
            if (bitmap == null || bitmap.IsEmpty)
            {
                result.Reason = "空图";
                return result;
            }

            Bitmap work = bitmap;
            var sourceRect = new UiRect(0d, 0d, bitmap.Width, bitmap.Height);
            if (options.TrimTransparent)
            {
                work = bitmap.TrimTransparent(out sourceRect, options.AlphaThreshold);
                if (work == null)
                {
                    result.Reason = "整张图透明";
                    return result;
                }
            }

            result.SourceRect = sourceRect;
            result.IsUniform = IsUniform(work);
            if (result.IsUniform)
            {
                result.Sprite = work;
                result.Reason = "纯色图，无需九宫";
                return result;
            }

            int rowStart;
            int rowEnd;
            FindRepeatRun(work, true, options.Tolerance, out rowStart, out rowEnd);
            int columnStart;
            int columnEnd;
            FindRepeatRun(work, false, options.Tolerance, out columnStart, out columnEnd);

            int top = rowEnd > rowStart ? rowStart : 0;
            int bottom = rowEnd > rowStart ? work.Height - rowEnd : 0;
            int left = columnEnd > columnStart ? columnStart : 0;
            int right = columnEnd > columnStart ? work.Width - columnEnd : 0;

            if (left + right <= 0 && top + bottom <= 0)
            {
                result.Sprite = work;
                result.Reason = "未检测到边框（拉伸会变形，按普通图导出）";
                return result;
            }

            if (left + right >= work.Width || top + bottom >= work.Height)
            {
                result.Sprite = work;
                result.Reason = "可拉伸区为空，放弃九宫";
                return result;
            }

            string failure = Verify(work, left, top, right, bottom, options.Tolerance);
            if (failure != null)
            {
                result.Sprite = work;
                result.Reason = failure;
                return result;
            }

            result.Border = new UiBorder(left, bottom, right, top);
            result.Sprite = options.Minimize ? Minimize(work, left, top, right, bottom) : work;
            result.Reason = "九宫 " + result.Border;
            return result;
        }

        /// <summary>
        /// 找最长的“连续相同行（或列）”区间，返回 [start, end)。
        /// 只看长度 ≥ 2 的区间，单行/单列不构成可拉伸区。
        /// </summary>
        private static void FindRepeatRun(Bitmap bitmap, bool rows, int tolerance, out int start, out int end)
        {
            int count = rows ? bitmap.Height : bitmap.Width;
            int bestStart = 0;
            int bestEnd = 0;
            int runStart = 0;
            for (int i = 1; i < count; i++)
            {
                bool same = rows
                    ? RowsEqual(bitmap, i - 1, i, tolerance)
                    : ColumnsEqual(bitmap, i - 1, i, tolerance);
                if (!same)
                {
                    runStart = i;
                    continue;
                }

                if (i - runStart + 1 > bestEnd - bestStart)
                {
                    bestStart = runStart;
                    bestEnd = i + 1;
                }
            }

            start = bestStart;
            end = bestEnd;
        }

        private static bool RowsEqual(Bitmap bitmap, int first, int second, int tolerance)
        {
            int width = bitmap.Width;
            int offsetA = first * width * 4;
            int offsetB = second * width * 4;
            for (int x = 0; x < width; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    int a = bitmap.Pixels[offsetA + x * 4 + channel];
                    int b = bitmap.Pixels[offsetB + x * 4 + channel];
                    if (Math.Abs(a - b) > tolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ColumnsEqual(Bitmap bitmap, int first, int second, int tolerance)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            for (int y = 0; y < height; y++)
            {
                int offsetA = (y * width + first) * 4;
                int offsetB = (y * width + second) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int a = bitmap.Pixels[offsetA + channel];
                    int b = bitmap.Pixels[offsetB + channel];
                    if (Math.Abs(a - b) > tolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsUniform(Bitmap bitmap)
        {
            byte r = bitmap.Pixels[0];
            byte g = bitmap.Pixels[1];
            byte b = bitmap.Pixels[2];
            byte a = bitmap.Pixels[3];
            for (int i = 4; i < bitmap.Pixels.Length; i += 4)
            {
                if (bitmap.Pixels[i] != r || bitmap.Pixels[i + 1] != g || bitmap.Pixels[i + 2] != b ||
                    bitmap.Pixels[i + 3] != a)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 逐块验证九宫是不是真的“拉伸等于复制”。任意一块不满足就放弃九宫。
        ///
        /// - 中间块要被两个方向拉伸 → 必须是纯色
        /// - 上/下条带只被横向拉伸 → 每一行在中间宽度内必须是纯色
        /// - 左/右条带只被纵向拉伸 → 每一列在中间高度内必须是纯色
        /// - 某条带为空（边框为 0）时，检查退化成对整幅图的要求，
        ///   正好挡住“只在字符间隙里找到一列纯色就当成九宫”这类误判
        /// </summary>
        private static string Verify(Bitmap bitmap, int left, int top, int right, int bottom, int tolerance)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            int centerLeft = left;
            int centerRight = width - right;
            int centerTop = top;
            int centerBottom = height - bottom;

            if (!IsConstantBlock(bitmap, centerLeft, centerTop, centerRight, centerBottom, tolerance))
            {
                return "可拉伸区不是纯色，放弃九宫";
            }

            for (int y = 0; y < centerTop; y++)
            {
                if (!IsConstantRow(bitmap, y, centerLeft, centerRight, tolerance))
                {
                    return "上边框不是横向一致的，放弃九宫";
                }
            }

            for (int y = centerBottom; y < height; y++)
            {
                if (!IsConstantRow(bitmap, y, centerLeft, centerRight, tolerance))
                {
                    return "下边框不是横向一致的，放弃九宫";
                }
            }

            for (int x = 0; x < centerLeft; x++)
            {
                if (!IsConstantColumn(bitmap, x, centerTop, centerBottom, tolerance))
                {
                    return "左边框不是纵向一致的，放弃九宫";
                }
            }

            for (int x = centerRight; x < width; x++)
            {
                if (!IsConstantColumn(bitmap, x, centerTop, centerBottom, tolerance))
                {
                    return "右边框不是纵向一致的，放弃九宫";
                }
            }

            return null;
        }

        private static bool IsConstantRow(Bitmap bitmap, int y, int x0, int x1, int tolerance)
        {
            int width = bitmap.Width;
            int first = (y * width + x0) * 4;
            for (int x = x0 + 1; x < x1; x++)
            {
                int index = (y * width + x) * 4;
                if (!SamePixel(bitmap, first, index, tolerance))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsConstantColumn(Bitmap bitmap, int x, int y0, int y1, int tolerance)
        {
            int width = bitmap.Width;
            int first = (y0 * width + x) * 4;
            for (int y = y0 + 1; y < y1; y++)
            {
                int index = (y * width + x) * 4;
                if (!SamePixel(bitmap, first, index, tolerance))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SamePixel(Bitmap bitmap, int first, int second, int tolerance)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                if (Math.Abs(bitmap.Pixels[first + channel] - bitmap.Pixels[second + channel]) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>检查 [x0,x1) x [y0,y1) 区域是不是同一个颜色。</summary>
        private static bool IsConstantBlock(Bitmap bitmap, int x0, int y0, int x1, int y1, int tolerance)
        {
            int width = bitmap.Width;
            int first = (y0 * width + x0) * 4;
            byte r = bitmap.Pixels[first];
            byte g = bitmap.Pixels[first + 1];
            byte b = bitmap.Pixels[first + 2];
            byte a = bitmap.Pixels[first + 3];
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int index = (y * width + x) * 4;
                    if (Math.Abs(bitmap.Pixels[index] - r) > tolerance ||
                        Math.Abs(bitmap.Pixels[index + 1] - g) > tolerance ||
                        Math.Abs(bitmap.Pixels[index + 2] - b) > tolerance ||
                        Math.Abs(bitmap.Pixels[index + 3] - a) > tolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>把可拉伸区压到 1 像素：中间只留一行一列，保留四周的边框像素。</summary>
        public static Bitmap Minimize(Bitmap bitmap, int left, int top, int right, int bottom)
        {
            int width = left + 1 + right;
            int height = top + 1 + bottom;
            var result = new Bitmap(width, height);
            int sourceWidth = bitmap.Width;
            int sourceHeight = bitmap.Height;
            for (int y = 0; y < height; y++)
            {
                int sourceY = Map(y, top, bottom, sourceHeight);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Map(x, left, right, sourceWidth);
                    int source = (sourceY * sourceWidth + sourceX) * 4;
                    int target = (y * width + x) * 4;
                    result.Pixels[target] = bitmap.Pixels[source];
                    result.Pixels[target + 1] = bitmap.Pixels[source + 1];
                    result.Pixels[target + 2] = bitmap.Pixels[source + 2];
                    result.Pixels[target + 3] = bitmap.Pixels[source + 3];
                }
            }

            return result;
        }

        /// <summary>
        /// 目标坐标 → 源坐标：起点侧边框原样保留，中间 1 像素取可拉伸区的第一行/列，
        /// 末尾侧边框从源图尾部对齐取。
        /// </summary>
        private static int Map(int position, int border, int oppositeBorder, int sourceLength)
        {
            if (position < border)
            {
                return position;
            }

            if (position == border)
            {
                return border;
            }

            return sourceLength - (oppositeBorder - (position - border - 1));
        }
    }
}
