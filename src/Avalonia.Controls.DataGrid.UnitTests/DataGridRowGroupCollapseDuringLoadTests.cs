using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

public class DataGridRowGroupCollapseDuringLoadTests
{
    [AvaloniaFact]
    public void Collapsing_Synchronously_From_LoadingRowGroup_Keeps_Flag_And_Slot_Table_In_Agreement()
    {
        // A consumer restoring persisted collapse state does exactly this: collapse the group as
        // its header loads. LoadingRowGroup is raised from inside AddSlots, which is rebuilding
        // SlotCount from zero, so for the LAST group UpdateRowGroupVisibility derives endSlot from
        // a SlotCount that does not cover the group yet and the range comes out inverted. Upstream
        // writes it and later throws out of GetDisplayedElement; a build that merely skips the empty
        // range leaves the group's IsVisible flag false with none of its child slots collapsed.
        //
        // The assertion is that invariant, not "no rows on screen": after the load the grid makes
        // the first cell current, which legitimately expands that cell's group, and rows of a
        // broken later group can sit below the fold where they are never realised.
        var items = new ObservableCollection<Model>(
            Enumerable.Range(0, 60).Select(index => new Model($"Item {index}", $"Group {index / 20}")));
        var view = new DataGridCollectionView(items);
        view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(Model.Group)));

        var root = new Window
        {
            Width = 400,
            Height = 300,
            Styles =
            {
                new StyleInclude((Uri?)null)
                {
                    Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml")
                },
            }
        };

        // The grid must already be measured when the grouped source arrives: a source swapped into
        // a live grid takes RefreshRowsAndColumns(clearRows: true) into RefreshRows and its
        // AddSlots walk. A source present before the first measure only populates the header table.
        var target = new DataGrid
        {
            Columns = { new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") } },
            ItemsSource = new DataGridCollectionView(new ObservableCollection<Model>()),
            HeadersVisibility = DataGridHeadersVisibility.All,
        };

        var collapsed = new HashSet<object?>();
        target.LoadingRowGroup += (_, e) =>
        {
            if (e.RowGroupHeader.DataContext is DataGridCollectionViewGroup group)
            {
                collapsed.Add(group.Key);
                target.CollapseRowGroup(group, collapseAllSubgroups: false);
            }
        };

        root.Content = target;
        root.Show();
        target.UpdateLayout();

        var exception = Record.Exception(() =>
        {
            target.ItemsSource = view;
            target.UpdateLayout();
            target.UpdateLayout();
        });

        Assert.Null(exception);
        Assert.Equal(3, collapsed.Count);

        var disagreements = new List<string>();
        foreach (var slot in target.RowGroupHeadersTable.GetIndexes())
        {
            var info = target.RowGroupHeadersTable.GetValueAt(slot);
            if (info.IsVisible)
            {
                continue;
            }

            int firstChild = info.Slot + 1;
            int childCount = info.LastSubItemSlot - firstChild + 1;
            int collapsedChildren = childCount > 0 ? target.GetCollapsedSlotCount(firstChild, info.LastSubItemSlot) : 0;
            if (collapsedChildren != childCount)
            {
                disagreements.Add(
                    $"{info.CollectionViewGroup?.Key}: flagged collapsed, {childCount - collapsedChildren} of {childCount} child slots still visible");
            }
        }

        Assert.True(disagreements.Count == 0, string.Join("; ", disagreements));
    }

    private class Model(string name, string group)
    {
        public string Name { get; } = name;

        public string Group { get; } = group;
    }
}
