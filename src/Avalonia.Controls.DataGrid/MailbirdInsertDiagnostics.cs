// TEMPORARY DIAGNOSTIC — not for release. Traces the interaction between row-group collapse
// bookkeeping and rows inserted into an already-collapsed group (CU-86d43b20k follow-up).
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Avalonia.Controls
{
    internal static class MailbirdInsertDiagnostics
    {
        // Fixed path: the app relaunches itself, which loses any environment variable.
        private const string LOG_PATH =
            "/Users/secelead/Projects/MailbirdNext/AppData/Debug/Logs/dg-insert-diag.log";

        private static readonly object Gate = new object();
        private static bool _armed;

        private static void Write(string line)
        {
            lock (Gate)
            {
                try
                {
                    if (!_armed)
                    {
                        _armed = true;
                        File.AppendAllText(LOG_PATH,
                            $"=== probe armed {DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} ==={Environment.NewLine}");
                    }

                    File.AppendAllText(LOG_PATH,
                        $"{DateTime.Now:HH:mm:ss.fff} | {line}{Environment.NewLine}");
                }
                catch
                {
                    // Diagnostics must never take the app down.
                }
            }
        }

        private static string Ranges(IndexToValueTable<bool> table)
        {
            try
            {
                return table == null ? "<null>" : table.MailbirdDescribeRanges();
            }
            catch (Exception exception)
            {
                return "<error:" + exception.GetType().Name + ">";
            }
        }

        /// <summary>
        /// The guard that skips an inverted collapse range. A group collapsed here gets its flag
        /// set but none of its slots, which leaves it half-collapsed and its rows on screen.
        /// </summary>
        public static void EmptyCollapseRange(int startSlot, int endSlot, int slotCount, IndexToValueTable<bool> collapsed)
        {
            Write(string.Format(CultureInfo.InvariantCulture,
                "!!! EMPTY-COLLAPSE-RANGE startSlot={0} endSlot={1} slotCount={2} ranges={3}",
                startSlot, endSlot, slotCount, Ranges(collapsed)));
        }

        /// <summary>Every entry into EnsureRowGroupVisibility, including the ones that do nothing.</summary>
        public static void Ensure(
            DataGridRowGroupInfo info,
            bool requested,
            bool slotVisible,
            bool headerSlotCollapsed,
            int firstScrollingSlot,
            IndexToValueTable<bool> collapsed)
        {
            if (info == null)
            {
                Write("ENSURE info=<NULL> requested=" + requested + "  <-- group could not be resolved");
                return;
            }

            string branch = info.IsVisible == requested
                ? "EARLY-RETURN(no change)"
                : slotVisible ? "via ToggleExpandCollapse" : headerSlotCollapsed ? "mark-only" : "UpdateRowGroupVisibility";

            // Quiet unless something is wrong; the desync shows up in State/Audit instead.
            _ = branch;
        }

        /// <summary>Each row/header insert, with the collapsed decision the caller computed.</summary>
        public static void Insert(
            int insertSlot,
            DataGridRowGroupInfo parent,
            bool isCollapsed,
            bool isGroupHeader,
            int slotCount,
            int visibleSlotCount,
            IndexToValueTable<bool> collapsed)
        {
            // Only the contradictory case is worth a disk write; a row inserted into a collapsed
            // parent must itself be collapsed.
            if (parent == null || parent.IsVisible || isCollapsed)
            {
                return;
            }

            Write(string.Format(CultureInfo.InvariantCulture,
                "!!! INSERT-VISIBLE-INTO-COLLAPSED slot={0} kind={1} parentSlot={2} parentVisible={3} parentLastSub={4} isCollapsed={5} slotCount={6} visibleSlots={7} ranges={8}",
                insertSlot, isGroupHeader ? "header" : "row",
                parent == null ? -1 : parent.Slot,
                parent == null ? (object)"<null>" : parent.IsVisible,
                parent == null ? -1 : parent.LastSubItemSlot,
                isCollapsed, slotCount, visibleSlotCount, Ranges(collapsed)));
        }

        /// <summary>The visibility flag flip itself, so a silent desync is attributable.</summary>
        public static void VisibilityChanged(int slot, bool oldValue, bool newValue, string source)
        {
            // Retained as a no-op: the flag transitions are reconstructible from the anomaly dump.
            _ = slot; _ = oldValue; _ = newValue; _ = source;
        }

        /// <summary>Fires when an element is put on screen for a slot the collapsed table hides.</summary>
        public static void Displayed(
            int slot,
            object element,
            bool slotIsCollapsed,
            int firstScrollingSlot,
            int lastScrollingSlot,
            int visibleSlotCount,
            int slotCount,
            IndexToValueTable<bool> collapsed)
        {
            if (!slotIsCollapsed)
            {
                return;
            }

            Write(string.Format(CultureInfo.InvariantCulture,
                "!!! DISPLAY-COLLAPSED slot={0} element={1} window=[{2}..{3}] visibleSlots={4} slotCount={5} ranges={6}",
                slot, element == null ? "<null>" : element.GetType().Name,
                firstScrollingSlot, lastScrollingSlot, visibleSlotCount, slotCount, Ranges(collapsed)));
        }

        private static DateTime _lastState;
        private static string _lastStateLine = string.Empty;

        /// <summary>
        /// Throttled ground-truth dump: what the grid believes about every group versus what the
        /// display window is showing. Catches the symptom whatever the mechanism, including a
        /// header whose chevron disagrees with its own RowGroupInfo.
        /// </summary>
        public static void State(
            string tag,
            System.Collections.Generic.IEnumerable<DataGridRowGroupInfo> groups,
            int firstScrollingSlot,
            int lastScrollingSlot,
            int visibleSlotCount,
            int slotCount,
            int displayedRowCount,
            IndexToValueTable<bool> collapsed)
        {
            var builder = new StringBuilder();
            int headers = 0;
            int expanded = 0;
            foreach (var info in groups)
            {
                headers++;
                if (info.IsVisible)
                {
                    expanded++;
                }

                builder.Append(info.Slot).Append(info.IsVisible ? "+" : "-").Append(' ');
            }

            // Everything collapsed means the only visible slots are the headers themselves.
            bool anomaly = (expanded == 0 && visibleSlotCount > headers)
                || (expanded == 0 && displayedRowCount > 0);

            if (!anomaly)
            {
                return;
            }

            string line = string.Format(CultureInfo.InvariantCulture,
                "STATE {0} groups={1} expanded={2} visibleSlots={3} slotCount={4} window=[{5}..{6}] displayedRows={7} flags=[{8}] ranges={9}{10}",
                tag, headers, expanded, visibleSlotCount, slotCount, firstScrollingSlot, lastScrollingSlot,
                displayedRowCount, builder.ToString().Trim(), Ranges(collapsed),
                anomaly ? "   <<<< ANOMALY: all groups collapsed but visibleSlots exceeds header count" : string.Empty);

            // Rate-limit: an anomaly persists across many measures, one line per second is plenty.
            if (line == _lastStateLine && (DateTime.Now - _lastState).TotalSeconds < 1)
            {
                return;
            }

            _lastState = DateTime.Now;
            _lastStateLine = line;
            Write(line);
        }

        /// <summary>Snapshot of one group's agreement between its flag and the slot table.</summary>
        public static void Audit(
            string tag,
            DataGridRowGroupInfo info,
            int collapsedChildCount,
            IndexToValueTable<bool> collapsed)
        {
            if (info == null)
            {
                return;
            }

            var builder = new StringBuilder();
            int from = info.Slot + 1;
            int to = info.LastSubItemSlot;

            // GetIndexCount answers this over the table's ranges, so the probe stays O(ranges)
            // instead of walking every child slot on every insert - that cost was enough to move
            // the timing of the paging burst this is meant to observe.
            int childCount = to >= from ? to - from + 1 : 0;
            int collapsedChildren = collapsedChildCount;
            int visibleChildren = childCount - collapsedChildren;

            bool disagrees = !info.IsVisible && visibleChildren > 0;

            // Silent unless the group's own flag disagrees with the slot table.
            if (!disagrees)
            {
                return;
            }

            builder.Append("AUDIT ").Append(tag)
                .Append(" slot=").Append(info.Slot)
                .Append(" isVisible=").Append(info.IsVisible)
                .Append(" children=[").Append(from).Append("..").Append(to).Append(']')
                .Append(" collapsedChildren=").Append(collapsedChildren)
                .Append(" visibleChildren=").Append(visibleChildren)
                .Append(" ranges=").Append(Ranges(collapsed));

            if (disagrees)
            {
                builder.Append("   <<< DESYNC: group flagged collapsed but children are visible");
            }

            Write(builder.ToString());
        }
    }
}
