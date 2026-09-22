// TEMPORARY DIAGNOSTIC - not for release. Full ordered trace of row-group collapse bookkeeping.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Avalonia.Controls
{
    /// <summary>TEMPORARY: the one public seam the consuming app uses to write into the same ordered trace.</summary>
    public static class MailbirdTrace
    {
        public static void Seq(string line) => MailbirdInsertDiagnostics.Seq(line);
    }

    internal static class MailbirdInsertDiagnostics
    {
        // Fixed path: the app relaunches itself, which loses any environment variable.
        private const string LOG_PATH =
            "/Users/secelead/Projects/MailbirdNext/AppData/Debug/Logs/dg-insert-diag.log";

        private static readonly object _gate = new object();
        private static bool _armed;
        private static long _seq;
        [ThreadStatic] private static int _depth;

        /// <summary>One ordered line. Shared by the control and the app so both sides interleave exactly.</summary>
        public static void Seq(string line)
        {
            long n = Interlocked.Increment(ref _seq);
            lock (_gate)
            {
                try
                {
                    if (!_armed)
                    {
                        _armed = true;
                        File.AppendAllText(LOG_PATH,
                            $"=== armed {DateTime.Now:HH:mm:ss.ffffff} pid={Environment.ProcessId} ==={Environment.NewLine}");
                    }

                    File.AppendAllText(LOG_PATH,
                        string.Format(CultureInfo.InvariantCulture, "#{0:D6} d{1} {2:HH:mm:ss.ffffff} | {3}{4}",
                            n, _depth, DateTime.Now, line, Environment.NewLine));
                }
                catch
                {
                    // Diagnostics must never take the app down.
                }
            }
        }

        /// <summary>Re-entrancy tracking: depth rises while inside the scope, so a collapse that runs
        /// from within the view's own mutation shows up as depth > 1.</summary>
        public static IDisposable Scope(string name)
        {
            Seq("ENTER " + name);
            _depth++;
            return new ScopeExit(name);
        }

        private sealed class ScopeExit : IDisposable
        {
            private readonly string _name;
            public ScopeExit(string name) { _name = name; }
            public void Dispose() { _depth--; Seq("EXIT  " + _name); }
        }

        private static string Ranges(IndexToValueTable<bool> table)
        {
            try { return table == null ? "<null>" : table.MailbirdDescribeRanges(); }
            catch (Exception exception) { return "<error:" + exception.GetType().Name + ">"; }
        }

        private static string Key(DataGridRowGroupInfo info) =>
            info?.CollectionViewGroup?.Key?.ToString() ?? "?";

        public static void Groups(string tag, IEnumerable<DataGridRowGroupInfo> infos, int slotCount, int visibleSlotCount, IndexToValueTable<bool> collapsed)
        {
            var parts = infos.Select(i => string.Format(CultureInfo.InvariantCulture,
                "{0}[slot={1} lastSub={2} vis={3} items={4}]", Key(i), i.Slot, i.LastSubItemSlot, i.IsVisible ? "+" : "-",
                i.CollectionViewGroup?.ItemCount ?? -1));
            Seq(string.Format(CultureInfo.InvariantCulture, "GROUPS {0} slotCount={1} visibleSlots={2} {3} ranges={4}",
                tag, slotCount, visibleSlotCount, string.Join(" ", parts), Ranges(collapsed)));
        }

        public static void Ensure(DataGridRowGroupInfo info, bool requested, bool slotVisible, bool headerSlotCollapsed,
            int firstScrollingSlot, IndexToValueTable<bool> collapsed, int slotCount = -1)
        {
            if (info == null) { Seq("ENSURE info=<NULL> requested=" + requested + "  <-- group could not be resolved"); return; }
            string branch = info.IsVisible == requested ? "EARLY-RETURN(no change)"
                : slotVisible ? "via ToggleExpandCollapse" : headerSlotCollapsed ? "mark-only" : "UpdateRowGroupVisibility";
            bool stale = slotCount >= 0 && info.Slot >= slotCount;
            Seq(string.Format(CultureInfo.InvariantCulture,
                "ENSURE key={0} slot={1} lastSub={2} items={3} wasVisible={4} requested={5} slotVisible={6} headerSlotCollapsed={7} first={8} slotCount={9} branch={10}{11} ranges={12}",
                Key(info), info.Slot, info.LastSubItemSlot, info.CollectionViewGroup?.ItemCount ?? -1, info.IsVisible, requested,
                slotVisible, headerSlotCollapsed, firstScrollingSlot, slotCount, branch,
                stale ? "   <<< STALE: header slot >= SlotCount" : string.Empty, Ranges(collapsed)));
        }

        public static void Insert(int insertSlot, DataGridRowGroupInfo parent, bool isCollapsed, bool isGroupHeader,
            int slotCount, int visibleSlotCount, IndexToValueTable<bool> collapsed)
        {
            bool contradiction = parent != null && !parent.IsVisible && !isCollapsed;
            Seq(string.Format(CultureInfo.InvariantCulture,
                "{0}ADD kind={1} insertSlot={2} parentKey={3} parentSlot={4} parentVisible={5} parentLastSub={6} parentItems={7} isCollapsed={8} slotCount={9} visibleSlots={10} ranges={11}",
                contradiction ? "!!! INSERT-VISIBLE-INTO-COLLAPSED " : string.Empty,
                isGroupHeader ? "header" : "row", insertSlot, Key(parent),
                parent == null ? -1 : parent.Slot, parent == null ? (object)"<null>" : parent.IsVisible,
                parent == null ? -1 : parent.LastSubItemSlot, parent?.CollectionViewGroup?.ItemCount ?? -1,
                isCollapsed, slotCount, visibleSlotCount, Ranges(collapsed)));
        }

        public static void VisibilityChanged(int slot, bool oldValue, bool newValue, string source) =>
            Seq(string.Format(CultureInfo.InvariantCulture, "SETVISIBLE slot={0} {1} -> {2} source={3}", slot, oldValue, newValue, source));

        public static void EmptyCollapseRange(int startSlot, int endSlot, int slotCount, IndexToValueTable<bool> collapsed) =>
            Seq(string.Format(CultureInfo.InvariantCulture,
                "!!! EMPTY-COLLAPSE-RANGE startSlot={0} endSlot={1} slotCount={2} ranges={3}", startSlot, endSlot, slotCount, Ranges(collapsed)));

        public static void Displayed(int slot, object element, bool slotIsCollapsed, int firstScrollingSlot, int lastScrollingSlot,
            int visibleSlotCount, int slotCount, IndexToValueTable<bool> collapsed)
        {
            if (!slotIsCollapsed) return;
            Seq(string.Format(CultureInfo.InvariantCulture,
                "!!! DISPLAY-COLLAPSED slot={0} element={1} window=[{2}..{3}] visibleSlots={4} slotCount={5} ranges={6}",
                slot, element == null ? "<null>" : element.GetType().Name, firstScrollingSlot, lastScrollingSlot, visibleSlotCount, slotCount, Ranges(collapsed)));
        }

        private static string _lastState = string.Empty;
        public static void State(string tag, IEnumerable<DataGridRowGroupInfo> groups, int firstScrollingSlot, int lastScrollingSlot,
            int visibleSlotCount, int slotCount, int displayedRowCount, IndexToValueTable<bool> collapsed)
        {
            var b = new StringBuilder(); int headers = 0, expanded = 0;
            foreach (var info in groups) { headers++; if (info.IsVisible) expanded++; b.Append(info.Slot).Append(info.IsVisible ? "+" : "-").Append(' '); }
            bool anomaly = expanded == 0 && (visibleSlotCount > headers || displayedRowCount > 0);
            if (!anomaly) return;
            string line = string.Format(CultureInfo.InvariantCulture,
                "STATE {0} groups={1} expanded={2} visibleSlots={3} slotCount={4} window=[{5}..{6}] displayedRows={7} flags=[{8}] ranges={9}   <<<< ANOMALY",
                tag, headers, expanded, visibleSlotCount, slotCount, firstScrollingSlot, lastScrollingSlot, displayedRowCount, b.ToString().Trim(), Ranges(collapsed));
            if (line == _lastState) return;
            _lastState = line; Seq(line);
        }

        public static void Audit(string tag, DataGridRowGroupInfo info, int collapsedChildCount, IndexToValueTable<bool> collapsed)
        {
            if (info == null) return;
            int from = info.Slot + 1, to = info.LastSubItemSlot;
            int childCount = to >= from ? to - from + 1 : 0;
            int visibleChildren = childCount - collapsedChildCount;
            if (!(!info.IsVisible && visibleChildren > 0)) return;
            Seq(string.Format(CultureInfo.InvariantCulture,
                "AUDIT {0} key={1} slot={2} isVisible={3} children=[{4}..{5}] collapsedChildren={6} visibleChildren={7} ranges={8}   <<< DESYNC",
                tag, Key(info), info.Slot, info.IsVisible, from, to, collapsedChildCount, visibleChildren, Ranges(collapsed)));
        }
    }
}
