using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Avalonia.Controls.DataGridTests;

public class DataGridRowGroupCollapseTests
{
    [AvaloniaFact]
    public void Collapsing_A_Group_Before_Its_Rows_Are_Laid_Out_Still_Hides_Them()
    {
        // The grid is asked to collapse a group whose rows have reached the view but not yet the
        // slot bookkeeping - the sequence a list gets when rows stream in while collapse state is
        // being restored. CollapseSlotsInTable is then handed an empty range (startSlot past
        // endSlot, because SlotCount does not cover the group yet), so the group's flag is set
        // while none of its slots are, and the rows render under a collapsed header.
        var target = CreateGroupedTarget(out var items);
        var view = (DataGridCollectionView)target.ItemsSource;

        // A brand new group, collapsed in the same beat it appears - no layout pass in between.
        items.Add(new Model("Late arrival", "Group Z"));
        var lateGroup = view.Groups!
            .OfType<DataGridCollectionViewGroup>()
            .Single(group => Equals(group.Key, "Group Z"));

        target.CollapseRowGroup(lateGroup, collapseAllSubgroups: false);
        target.UpdateLayout();

        // More rows arrive for the group that is already flagged collapsed.
        items.Add(new Model("Later arrival", "Group Z"));
        target.UpdateLayout();

        Assert.DoesNotContain(
            VisibleRows(target),
            row => ((Model)row.DataContext!).Group == "Group Z");
    }

    [AvaloniaFact]
    public void Re_Collapsing_A_Group_Whose_Slots_Escaped_Repairs_It()
    {
        // Collapsing an already-collapsed group is what a view-level "restore the persisted state"
        // sweep does. It must be able to repair a group whose flag and slots disagree, rather than
        // deciding there is nothing to do because the flag already reads collapsed.
        var target = CreateGroupedTarget(out var items);
        var view = (DataGridCollectionView)target.ItemsSource;

        items.Add(new Model("Late arrival", "Group Z"));
        var lateGroup = view.Groups!
            .OfType<DataGridCollectionViewGroup>()
            .Single(group => Equals(group.Key, "Group Z"));

        target.CollapseRowGroup(lateGroup, collapseAllSubgroups: false);
        target.UpdateLayout();
        items.Add(new Model("Later arrival", "Group Z"));
        target.UpdateLayout();

        // The sweep runs again over every group it believes should be collapsed.
        target.CollapseRowGroup(lateGroup, collapseAllSubgroups: false);
        target.UpdateLayout();

        Assert.DoesNotContain(
            VisibleRows(target),
            row => ((Model)row.DataContext!).Group == "Group Z");
    }

    [AvaloniaFact]
    public void Collapsing_A_Group_Whose_Header_Slot_Is_Not_Materialised_Still_Hides_Its_Rows()
    {
        // The condition seen in the field: the collapse targets a group whose header slot equals
        // SlotCount, i.e. the slot does not exist yet because the view is mid-rebuild. The range
        // then comes out empty and the group keeps its rows while its flag says collapsed.
        var target = CreateGroupedTarget(out var items);
        var view = (DataGridCollectionView)target.ItemsSource;

        // Rebuild the view under the grid, the way a re-sort or a regroup does, and collapse a
        // group in the same beat - before any layout pass re-establishes the slots.
        items.Add(new Model("Late arrival", "Group Z"));
        view.Refresh();

        var lateGroup = view.Groups!
            .OfType<DataGridCollectionViewGroup>()
            .Single(group => Equals(group.Key, "Group Z"));

        target.CollapseRowGroup(lateGroup, collapseAllSubgroups: false);
        target.UpdateLayout();

        Assert.DoesNotContain(
            VisibleRows(target),
            row => ((Model)row.DataContext!).Group == "Group Z");
    }

    [AvaloniaFact]
    public void Collapse_Deferred_From_LoadingRowGroup_Still_Hides_The_Rows()
    {
        // How a consumer restores persisted collapse state: LoadingRowGroup fires as each header
        // materialises, and the collapse is posted rather than run inline, because calling it from
        // inside the event races the grid's own group-info initialisation. By the time the post
        // runs the grid has moved on, and the group it names can have a header slot that is not
        // materialised - which is when the range comes out empty and the collapse is dropped.
        var target = CreateGroupedTarget(out var items);
        var view = (DataGridCollectionView)target.ItemsSource;

        target.LoadingRowGroup += (_, e) =>
        {
            if (e.RowGroupHeader.DataContext is DataGridCollectionViewGroup group
                && Equals(group.Key, "Group Z"))
            {
                Dispatcher.UIThread.Post(
                    () => target.CollapseRowGroup(group, collapseAllSubgroups: false),
                    DispatcherPriority.Background);
            }
        };

        items.Add(new Model("Late arrival", "Group Z"));
        target.UpdateLayout();

        // Keep the grid busy so the posted collapse lands against changed bookkeeping.
        items.Add(new Model("Later arrival", "Group Z"));
        view.Refresh();
        target.UpdateLayout();

        Dispatcher.UIThread.RunJobs();
        target.UpdateLayout();

        Assert.DoesNotContain(
            VisibleRows(target),
            row => ((Model)row.DataContext!).Group == "Group Z");
    }

    private static DataGrid CreateGroupedTarget(out ObservableCollection<Model> items)
    {
        items = new ObservableCollection<Model>(
            Enumerable
                .Range(0, 60)
                .Select(index => new Model($"Item {index}", $"Group {index / 20}")));

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

        var target = new DataGrid
        {
            Columns =
            {
                new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") }
            },
            ItemsSource = view,
            HeadersVisibility = DataGridHeadersVisibility.All,
        };

        root.Content = target;
        root.Show();
        target.UpdateLayout();

        return target;
    }

    private static IEnumerable<DataGridRow> VisibleRows(DataGrid target) =>
        target.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Where(row => row.IsVisible && row.DataContext is Model);

    private class Model(string name, string group)
    {
        public string Name { get; } = name;

        public string Group { get; } = group;
    }
}
