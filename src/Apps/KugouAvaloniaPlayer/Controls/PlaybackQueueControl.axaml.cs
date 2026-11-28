using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

public partial class PlaybackQueueControl : UserControl
{
    private const double DragStartThreshold = 6.0;
    private const double AutoScrollEdgeZone = 44.0;
    private const double MaxAutoScrollStep = 14.0;

    private VelaContextMenu? _queueItemMenu;
    private readonly DispatcherTimer _autoScrollTimer;

    private VelaListBoxItem? _pendingItemContainer;
    private SongItem? _pendingSong;
    private int _pendingSourceIndex = -1;
    private Point _pressPointInHost;
    private double _pressOffsetYInItem;

    private bool _isDragging;
    private SongItem? _draggedSong;
    private int _dragSourceIndex = -1;
    private int _currentInsertSlot = -1;
    private double _dragOffsetYInItem;
    private double _dragPreviewLeft = 10;
    private double _dragPreviewWidth = 340;
    private Point _lastPointerPosInHost;
    private ScrollViewer? _scrollViewer;
    private IPointer? _capturedPointer;

    public PlaybackQueueControl()
    {
        AddToNextCommand = new RelayCommand<SongItem?>(AddToNext);
        ViewSingerCommand = new RelayCommand<SingerLite?>(ViewSinger);
        ShowPlaylistDialogCommand = new AsyncRelayCommand<SongItem?>(ShowPlaylistDialogAsync);
        InitializeComponent();

        _autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _autoScrollTimer.Tick += AutoScrollTimer_OnTick;

        QueueListBox.AddHandler(PointerPressedEvent, QueueListBox_OnPointerPressed, RoutingStrategies.Tunnel);
        QueueListBox.AddHandler(PointerMovedEvent, QueueListBox_OnPointerMoved, RoutingStrategies.Tunnel);
        QueueListBox.AddHandler(PointerReleasedEvent, QueueListBox_OnPointerReleased, RoutingStrategies.Tunnel);
        QueueListBox.AddHandler(PointerCaptureLostEvent, QueueListBox_OnPointerCaptureLost, RoutingStrategies.Bubble);
    }

    public ICommand AddToNextCommand { get; }
    public ICommand ViewSingerCommand { get; }
    public ICommand ShowPlaylistDialogCommand { get; }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void AddToNext(SongItem? song)
    {
        if (song != null)
            ((IPlaybackCommands?)ViewModel?.Player)?.AddToNext(song);
    }

    private void ViewSinger(SingerLite? singer)
    {
        if (singer != null)
        {
            this.FindAncestorOfType<VelaDrawerHost>()?.Close();
            ViewModel?.SongInteractions.NavigateToSinger(singer);
        }
    }

    private async Task ShowPlaylistDialogAsync(SongItem? song)
    {
        if (song != null && ViewModel is { } viewModel)
            await viewModel.SongInteractions.ShowAddToPlaylistDialogAsync(song);
    }

    private void QueueItemRoot_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control target || target.DataContext is not SongItem song)
            return;

        _queueItemMenu ??= new VelaContextMenu { MenuMinWidth = 188 };
        _queueItemMenu.Items.Clear();

        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "下一首播放",
            Icon = KugouIcons.QueueNext,
            Command = AddToNextCommand,
            CommandParameter = song
        });

        if (song.Singers.Count == 1)
        {
            var only = song.Singers[0];
            var name = string.IsNullOrWhiteSpace(only.Name) ? "未知歌手" : only.Name;
            _queueItemMenu.Items.Add(new VelaMenuItem
            {
                Header = $"查看歌手：{name}",
                Icon = KugouIcons.Singer,
                Command = ViewSingerCommand,
                CommandParameter = only
            });
        }
        else if (song.Singers.Count > 1)
        {
            var singerParent = new VelaMenuItem
            {
                Header = "查看歌手",
                Icon = KugouIcons.Singer
            };
            foreach (var singer in song.Singers)
            {
                singerParent.Items.Add(new VelaMenuItem
                {
                    Header = string.IsNullOrWhiteSpace(singer.Name) ? "未知歌手" : singer.Name,
                    Icon = KugouIcons.Singer,
                    Command = ViewSingerCommand,
                    CommandParameter = singer
                });
            }
            _queueItemMenu.Items.Add(singerParent);
        }

        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "添加到歌单",
            Icon = KugouIcons.PlaylistAdd,
            Command = ShowPlaylistDialogCommand,
            CommandParameter = song
        });

        _queueItemMenu.Items.Add(new VelaMenuItem { IsSeparator = true });
        _queueItemMenu.Items.Add(new VelaMenuItem
        {
            Header = "从队列移除",
            Icon = KugouIcons.Trash,
            IsDestructive = true,
            Command = ViewModel?.Player.RemoveFromQueueCommand,
            CommandParameter = song
        });

        _queueItemMenu.ShowAt(target, showAtPointer: e.TryGetPosition(target, out _));
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        _autoScrollTimer.Stop();
        _queueItemMenu?.Close();
        _queueItemMenu?.Items.Clear();
        _queueItemMenu = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void QueueListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.Player.CanReorderDisplayPlaybackQueue != true)
            return;

        var point = e.GetCurrentPoint(QueueListHost);
        if (!point.Properties.IsLeftButtonPressed)
        {
            if (_isDragging)
            {
                CancelDrag();
                e.Handled = true;
            }
            return;
        }

        if (e.Source is not Visual sourceVisual || IsInteractiveNonDragSource(sourceVisual))
            return;

        var itemContainer = sourceVisual as VelaListBoxItem ?? sourceVisual.FindAncestorOfType<VelaListBoxItem>();
        if (itemContainer is null || itemContainer.DataContext is not SongItem song)
            return;

        var sourceIndex = QueueListBox.IndexFromContainer(itemContainer);
        if (sourceIndex < 0)
            return;

        _pendingItemContainer = itemContainer;
        _pendingSong = song;
        _pendingSourceIndex = sourceIndex;
        _pressPointInHost = point.Position;
        _pressOffsetYInItem = e.GetPosition(itemContainer).Y;
    }

    private void QueueListBox_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
        {
            if (_pendingSong is null || _pendingItemContainer is null)
                return;

            var point = e.GetCurrentPoint(QueueListHost);
            if (!point.Properties.IsLeftButtonPressed)
            {
                ClearPendingDrag();
                return;
            }

            var delta = point.Position - _pressPointInHost;
            if (Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < DragStartThreshold)
                return;

            BeginDrag(e.Pointer, point.Position);
            e.Handled = true;
            return;
        }

        _lastPointerPosInHost = e.GetPosition(QueueListHost);
        UpdateDragVisuals();
        e.Handled = true;
    }

    private void QueueListBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            ClearPendingDrag();
            return;
        }

        var sourceIndex = _dragSourceIndex;
        var insertSlot = _currentInsertSlot;
        var totalCount = ViewModel?.Player.DisplayPlaybackQueue.Count ?? 0;

        CancelDrag();

        if (totalCount > 1 &&
            sourceIndex >= 0 && sourceIndex < totalCount &&
            insertSlot >= 0 && insertSlot <= totalCount &&
            insertSlot != sourceIndex && insertSlot != sourceIndex + 1)
        {
            var targetIndex = insertSlot > sourceIndex ? insertSlot - 1 : insertSlot;
            targetIndex = Math.Clamp(targetIndex, 0, totalCount - 1);
            ViewModel?.Player.MoveQueueItem(sourceIndex, targetIndex);
        }

        e.Handled = true;
    }

    private void QueueListBox_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging)
            CancelDrag();
        else
            ClearPendingDrag();
    }

    private void BeginDrag(IPointer pointer, Point currentPosInHost)
    {
        if (_pendingSong is null || _pendingItemContainer is null)
            return;

        _isDragging = true;
        _draggedSong = _pendingSong;
        _dragSourceIndex = _pendingSourceIndex;
        _currentInsertSlot = _pendingSourceIndex;
        _lastPointerPosInHost = currentPosInHost;
        _dragOffsetYInItem = Math.Clamp(
            _pressOffsetYInItem,
            8,
            Math.Max(8, _pendingItemContainer.Bounds.Height - 8));

        var itemOrigin = _pendingItemContainer.TranslatePoint(new Point(0, 0), QueueListHost);
        _dragPreviewLeft = itemOrigin?.X ?? 10;
        _dragPreviewWidth = Math.Max(140, _pendingItemContainer.Bounds.Width);
        _scrollViewer = QueueListBox.FindDescendantOfType<ScrollViewer>();

        _capturedPointer = pointer;
        pointer.Capture(QueueListBox);

        QueueListBox.Classes.Set("is-dragging", true);
        _pendingItemContainer.Classes.Set("dragging", true);

        DragPreviewTitle.Text = _draggedSong.Name;
        DragPreviewSinger.Text = _draggedSong.Singer;
        DragPreviewCard.Width = _dragPreviewWidth;
        DragPreviewCard.IsVisible = true;

        DropIndicator.Width = _dragPreviewWidth;

        ClearPendingDrag();
        UpdateDragVisuals();
        _autoScrollTimer.Start();
    }

    private void CancelDrag()
    {
        _autoScrollTimer.Stop();

        if (_capturedPointer is not null)
        {
            if (ReferenceEquals(_capturedPointer.Captured, QueueListBox))
                _capturedPointer.Capture(null);
            _capturedPointer = null;
        }

        QueueListBox.Classes.Set("is-dragging", false);
        foreach (var container in QueueListBox.GetRealizedContainers())
        {
            if (container is VelaListBoxItem item)
                item.Classes.Set("dragging", false);
        }

        DragPreviewCard.IsVisible = false;
        DropIndicator.IsVisible = false;

        _isDragging = false;
        _draggedSong = null;
        _dragSourceIndex = -1;
        _currentInsertSlot = -1;
        _scrollViewer = null;
        ClearPendingDrag();
    }

    private void ClearPendingDrag()
    {
        _pendingItemContainer = null;
        _pendingSong = null;
        _pendingSourceIndex = -1;
    }

    private void UpdateDragVisuals()
    {
        if (!_isDragging)
            return;

        var hostHeight = QueueListHost.Bounds.Height;
        var previewHeight = DragPreviewCard.Bounds.Height > 0 ? DragPreviewCard.Bounds.Height : 52;
        var previewTop = Math.Clamp(_lastPointerPosInHost.Y - _dragOffsetYInItem, 0, Math.Max(0, hostHeight - previewHeight));
        Canvas.SetLeft(DragPreviewCard, _dragPreviewLeft);
        Canvas.SetTop(DragPreviewCard, previewTop);

        var realized = new List<(int Index, double TopY, double BottomY, double MidY)>();
        foreach (var container in QueueListBox.GetRealizedContainers())
        {
            if (container is not VelaListBoxItem item)
                continue;

            var index = QueueListBox.IndexFromContainer(item);
            item.Classes.Set("dragging", index == _dragSourceIndex && ReferenceEquals(item.DataContext, _draggedSong));

            if (index < 0)
                continue;

            var topLeft = item.TranslatePoint(new Point(0, 0), QueueListHost);
            if (topLeft is null)
                continue;

            var topY = topLeft.Value.Y;
            var bottomY = topY + item.Bounds.Height;
            realized.Add((index, topY, bottomY, (topY + bottomY) * 0.5));
        }

        if (realized.Count == 0)
        {
            DropIndicator.IsVisible = false;
            return;
        }

        realized.Sort((a, b) => a.Index.CompareTo(b.Index));

        int insertSlot;
        double indicatorY;

        if (_lastPointerPosInHost.Y < realized[0].MidY)
        {
            insertSlot = realized[0].Index;
            indicatorY = realized[0].TopY;
        }
        else if (_lastPointerPosInHost.Y >= realized[^1].MidY)
        {
            insertSlot = realized[^1].Index + 1;
            indicatorY = realized[^1].BottomY;
        }
        else
        {
            insertSlot = realized[^1].Index + 1;
            indicatorY = realized[^1].BottomY;

            for (var i = 1; i < realized.Count; i++)
            {
                if (_lastPointerPosInHost.Y < realized[i].MidY)
                {
                    insertSlot = realized[i].Index;
                    indicatorY = (realized[i - 1].BottomY + realized[i].TopY) * 0.5;
                    break;
                }
            }
        }

        _currentInsertSlot = insertSlot;

        var clampedIndicatorTop = Math.Clamp(indicatorY - 3, 0, Math.Max(0, hostHeight - 6));
        Canvas.SetLeft(DropIndicator, _dragPreviewLeft);
        Canvas.SetTop(DropIndicator, clampedIndicatorTop);
        DropIndicator.IsVisible = true;
    }

    private void AutoScrollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_isDragging || _scrollViewer is null)
            return;

        var hostHeight = QueueListHost.Bounds.Height;
        if (hostHeight <= AutoScrollEdgeZone * 2)
            return;

        double deltaY = 0;
        if (_lastPointerPosInHost.Y < AutoScrollEdgeZone)
        {
            var ratio = Math.Clamp((AutoScrollEdgeZone - _lastPointerPosInHost.Y) / AutoScrollEdgeZone, 0, 1);
            deltaY = -Math.Max(2.0, MaxAutoScrollStep * ratio);
        }
        else if (_lastPointerPosInHost.Y > hostHeight - AutoScrollEdgeZone)
        {
            var ratio = Math.Clamp((_lastPointerPosInHost.Y - (hostHeight - AutoScrollEdgeZone)) / AutoScrollEdgeZone, 0, 1);
            deltaY = Math.Max(2.0, MaxAutoScrollStep * ratio);
        }

        if (Math.Abs(deltaY) < 0.01)
            return;

        var maxOffsetY = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        var nextOffsetY = Math.Clamp(_scrollViewer.Offset.Y + deltaY, 0, maxOffsetY);
        if (Math.Abs(nextOffsetY - _scrollViewer.Offset.Y) < 0.01)
            return;

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, nextOffsetY);
        UpdateDragVisuals();
    }

    private static bool IsInteractiveNonDragSource(Visual source)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is VelaListBoxItem)
                return false;

            if (current is Button or ScrollBar or Thumb or RepeatButton)
                return true;
        }

        return false;
    }
}
