// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT;

namespace Files.App.Dialogs
{
	public sealed partial class BulkRenameDialog : ContentDialog, IDialog<BulkRenameDialogViewModel>
	{
		private const double TextTrackLeftPadding = 6.0;
		private const string EditorFontFamilyName = "Cascadia Mono, Consolas, Courier New";
		private const double EditorFontSize = 13.5;

		private readonly DispatcherQueueTimer _caretBlinkTimer;
		private readonly DispatcherQueueTimer _warningClearTimer;
		private readonly SolidColorBrush _caretBrush;

		private TextBlock? _measureProbe;
		private double _probeAnchorWidth = -1;
		private bool _caretBlinkVisible = true;
		private bool _editorHasFocus;

		private bool _isDraggingSelection;
		private Grid? _dragRowGrid;
		private FrameworkElement? _dragTextHost;
		private BulkRenameItemViewModel? _dragItem;

		private FrameworkElement RootAppElement
		{
			[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
			get => (FrameworkElement)MainWindow.Instance.Content;
		}

		public BulkRenameDialogViewModel ViewModel
		{
			get => (BulkRenameDialogViewModel)DataContext;
			set => DataContext = value;
		}

		public BulkRenameDialog()
		{
			InitializeComponent();

			_caretBrush = (SolidColorBrush)Resources["MultiCursorCaretBrush"];

			_caretBlinkTimer = DispatcherQueue.CreateTimer();
			_caretBlinkTimer.Interval = TimeSpan.FromMilliseconds(530);
			_caretBlinkTimer.IsRepeating = true;
			_caretBlinkTimer.Tick += CaretBlinkTimer_Tick;

			_warningClearTimer = DispatcherQueue.CreateTimer();
			_warningClearTimer.Interval = TimeSpan.FromMilliseconds(2600);
			_warningClearTimer.IsRepeating = false;
			_warningClearTimer.Tick += WarningClearTimer_Tick;
		}

		public new async Task<DialogResult> ShowAsync()
		{
			return (DialogResult)await base.ShowAsync();
		}

		private void BulkRenameDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
		{
			SyncCaretAccentColor();
			FocusEditor();
		}

		private void BulkRenameDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
		{
			_caretBlinkTimer.Stop();
			_warningClearTimer.Stop();
		}

		private void BulkRenameDialog_ActualThemeChanged(FrameworkElement sender, object args)
		{
			SyncCaretAccentColor();
		}

		private async void BulkRenameDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			if (!ViewModel.CanCommit)
			{
				args.Cancel = true;
				return;
			}

			var deferral = args.GetDeferral();
			try
			{
				await ViewModel.CommitRenameAsync();
			}
			finally
			{
				deferral.Complete();
			}
		}

		private void SyncCaretAccentColor()
		{
			if (AccentBrushProbe.Background is SolidColorBrush accentBrush)
			{
				_caretBrush.Color = accentBrush.Color;
			}
			else if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var colorObj) &&
				colorObj is Color accentColor)
			{
				_caretBrush.Color = accentColor;
			}

			EditorSurfaceBorder.BorderBrush = _editorHasFocus
				? AccentBrushProbe.BorderBrush
				: SurfaceBrushProbe.BorderBrush;
		}

		private void FocusEditor()
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				EditorFocusHost.Focus(FocusState.Programmatic);
				ResetCaretBlink();
			});
		}

		private void ResetCaretBlink()
		{
			_caretBlinkVisible = true;
			_caretBrush.Opacity = 1.0;

			if (_editorHasFocus)
			{
				_caretBlinkTimer.Stop();
				_caretBlinkTimer.Start();
			}
		}

		private void CaretBlinkTimer_Tick(DispatcherQueueTimer sender, object args)
		{
			if (!_editorHasFocus)
				return;

			_caretBlinkVisible = !_caretBlinkVisible;
			_caretBrush.Opacity = _caretBlinkVisible ? 1.0 : 0.0;
		}

		private void WarningClearTimer_Tick(DispatcherQueueTimer sender, object args)
		{
			ViewModel.SetTransientWarning(string.Empty);
		}

		private void ScheduleTransientWarningAutoClear()
		{
			if (string.IsNullOrEmpty(ViewModel.TransientWarning))
				return;

			_warningClearTimer.Stop();
			_warningClearTimer.Start();
		}

		private void EditorFocusHost_GotFocus(object sender, RoutedEventArgs e)
		{
			_editorHasFocus = true;
			EditorSurfaceBorder.BorderBrush = AccentBrushProbe.BorderBrush;
			ResetCaretBlink();
		}

		private void EditorFocusHost_LostFocus(object sender, RoutedEventArgs e)
		{
			_editorHasFocus = false;
			_caretBlinkTimer.Stop();
			_caretBrush.Opacity = 0.25;
			EditorSurfaceBorder.BorderBrush = SurfaceBrushProbe.BorderBrush;
		}

		private void EditorSurfaceBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			EditorFocusHost.Focus(FocusState.Pointer);
			ResetCaretBlink();
		}

		// ---- Keyboard Handling ----

		private async void EditorFocusHost_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			bool ctrl = IsKeyDown(VirtualKey.Control);
			bool shift = IsKeyDown(VirtualKey.Shift);
			bool alt = IsKeyDown(VirtualKey.Menu);

			// Allow AltGr (Ctrl+Alt on European/Spanish keyboards) to pass through to CharacterReceived
			if (ctrl && alt)
				return;

			switch (e.Key)
			{
				case VirtualKey.Left:
					ViewModel.MoveLeft(extendSelection: shift, byWord: ctrl);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Right:
					ViewModel.MoveRight(extendSelection: shift, byWord: ctrl);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Home:
					ViewModel.MoveHome(extendSelection: shift);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.End:
					ViewModel.MoveEnd(extendSelection: shift);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Back:
					ViewModel.Backspace(byWord: ctrl);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Delete:
					ViewModel.Delete(byWord: ctrl);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Escape:
					if (ViewModel.RestoreAllCursors())
					{
						ResetCaretBlink();
						e.Handled = true;
					}
					return;

				case VirtualKey.Enter:
					// Let ContentDialog commit when valid; prevent closing or beep when invalid
					if (!ViewModel.CanCommit)
						e.Handled = true;
					return;

				case VirtualKey.A when ctrl:
					if (shift)
						ViewModel.RestoreAllCursors();
					ViewModel.SelectAll();
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Z when ctrl:
					if (shift)
						ViewModel.Redo();
					else
						ViewModel.Undo();
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.Y when ctrl:
					ViewModel.Redo();
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.N when ctrl && shift:
					ViewModel.InsertSequentialNumbers();
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.C when ctrl:
					CopyToClipboard(cut: false);
					e.Handled = true;
					return;

				case VirtualKey.X when ctrl:
					CopyToClipboard(cut: true);
					ResetCaretBlink();
					e.Handled = true;
					return;

				case VirtualKey.V when ctrl:
					e.Handled = true;
					await PasteFromClipboardAsync();
					ResetCaretBlink();
					return;
			}
		}

		private void EditorFocusHost_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
		{
			if (char.IsControl(args.Character))
				return;

			ViewModel.InsertCharacter(args.Character);
			ScheduleTransientWarningAutoClear();
			ResetCaretBlink();
			args.Handled = true;
		}

		private void CopyToClipboard(bool cut)
		{
			var text = ViewModel.GetSelectedTextForCopy(cut);
			if (string.IsNullOrEmpty(text))
				return;

			try
			{
				var package = new DataPackage();
				package.SetText(text);
				Clipboard.SetContent(package);
			}
			catch
			{
				// Clipboard busy
			}
		}

		private async Task PasteFromClipboardAsync()
		{
			try
			{
				var content = Clipboard.GetContent();
				if (content.Contains(StandardDataFormats.Text))
				{
					var text = await content.GetTextAsync();
					ViewModel.PasteText(text);
					ScheduleTransientWarningAutoClear();
				}
			}
			catch
			{
				// Clipboard unavailable
			}
		}

		// ---- Pointer / Mouse Positioning & Drag Selection ----

		private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
		{
			if (sender is Grid rowGrid && SurfaceBrushProbe.Background is Brush brush)
			{
				rowGrid.Background = brush;
			}
		}

		private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
		{
			if (sender is Grid rowGrid)
				rowGrid.Background = new SolidColorBrush(Colors.Transparent);
		}

		private void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (sender is not Grid rowGrid || rowGrid.DataContext is not BulkRenameItemViewModel item)
				return;

			var point = e.GetCurrentPoint(rowGrid);
			if (!point.Properties.IsLeftButtonPressed)
				return;

			var textHost = GetTextTrackHost(rowGrid);
			if (textHost is null)
				return;

			var textX = e.GetCurrentPoint(textHost).Position.X - TextTrackLeftPadding;
			var (charIndex, isPastEnd) = HitTestCharIndex(item.EditableText, textX);

			bool shift = IsKeyDown(VirtualKey.Shift);
			bool alt = IsKeyDown(VirtualKey.Menu);

			ViewModel.SetCursorFromClick(item, charIndex, isPastEnd, extendSelection: shift, singleRowOnly: alt);

			_isDraggingSelection = true;
			_dragRowGrid = rowGrid;
			_dragTextHost = textHost;
			_dragItem = item;
			rowGrid.CapturePointer(e.Pointer);

			EditorFocusHost.Focus(FocusState.Pointer);
			ResetCaretBlink();
			e.Handled = true;
		}

		private void Row_PointerMoved(object sender, PointerRoutedEventArgs e)
		{
			if (!_isDraggingSelection || _dragTextHost is null || _dragItem is null)
				return;

			var textX = e.GetCurrentPoint(_dragTextHost).Position.X - TextTrackLeftPadding;
			var (charIndex, isPastEnd) = HitTestCharIndex(_dragItem.EditableText, textX);

			ViewModel.ExtendSelectionFromDrag(charIndex, isPastEnd);
			ResetCaretBlink();
			e.Handled = true;
		}

		private void Row_PointerReleased(object sender, PointerRoutedEventArgs e)
		{
			EndDragSelection(e.Pointer);
		}

		private void Row_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
		{
			EndDragSelection(null);
		}

		private void EndDragSelection(Pointer? pointer)
		{
			if (_isDraggingSelection && _dragRowGrid is not null && pointer is not null)
			{
				try
				{
					_dragRowGrid.ReleasePointerCapture(pointer);
				}
				catch
				{
					// Already released
				}
			}

			_isDraggingSelection = false;
			_dragRowGrid = null;
			_dragTextHost = null;
			_dragItem = null;
		}

		private void Row_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
		{
			if (sender is not Grid rowGrid || rowGrid.DataContext is not BulkRenameItemViewModel item)
				return;

			var textHost = GetTextTrackHost(rowGrid);
			if (textHost is null)
				return;

			var textX = e.GetPosition(textHost).X - TextTrackLeftPadding;
			var (charIndex, _) = HitTestCharIndex(item.EditableText, textX);

			ViewModel.SelectWordAt(charIndex);
			ResetCaretBlink();
			e.Handled = true;
		}

		// ---- Toolbar Handlers ----

		private void EditExtensionsToggle_Click(object sender, RoutedEventArgs e)
		{
			ViewModel.SetEditExtensions(EditExtensionsToggle.IsChecked == true);
			FocusEditor();
		}

		private void InsertCounterButton_Click(object sender, RoutedEventArgs e)
		{
			ViewModel.InsertSequentialNumbers();
			FocusEditor();
		}

		private void ResetButton_Click(object sender, RoutedEventArgs e)
		{
			ViewModel.ResetAll();
			FocusEditor();
		}

		private void RestoreAllCursorsButton_Click(object sender, RoutedEventArgs e)
		{
			ViewModel.RestoreAllCursors();
			FocusEditor();
		}

		// ---- Character Hit-Testing & Helpers ----

		private static FrameworkElement? GetTextTrackHost(Grid rowGrid)
		{
			if (rowGrid.FindName("TextTrackHost") is FrameworkElement found)
				return found;

			return rowGrid.Children.Count > 2 ? rowGrid.Children[2] as FrameworkElement : null;
		}

		private (int CharIndex, bool IsPastEnd) HitTestCharIndex(string text, double x)
		{
			if (string.IsNullOrEmpty(text) || x <= 0)
				return (0, false);

			double totalWidth = MeasureTextWidth(text);
			if (x >= totalWidth + 8.0)
				return (text.Length, true);

			if (x >= totalWidth)
				return (text.Length, false);

			int lo = 0;
			int hi = text.Length;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (MeasureTextWidth(text[..mid]) < x)
					lo = mid + 1;
				else
					hi = mid;
			}

			if (lo > 0)
			{
				double rightWidth = MeasureTextWidth(text[..lo]);
				double leftWidth = MeasureTextWidth(text[..(lo - 1)]);
				if (x - leftWidth < rightWidth - x)
					lo--;
			}

			return (lo, false);
		}

		private double MeasureTextWidth(string text)
		{
			if (string.IsNullOrEmpty(text))
				return 0;

			_measureProbe ??= new TextBlock
			{
				FontFamily = new FontFamily(EditorFontFamilyName),
				FontSize = EditorFontSize,
				TextWrapping = TextWrapping.NoWrap,
			};

			if (_probeAnchorWidth < 0)
			{
				_measureProbe.Text = "I";
				_measureProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				_probeAnchorWidth = _measureProbe.DesiredSize.Width;
			}

			// Append sentinel char so trailing spaces are measured accurately by DirectWrite
			_measureProbe.Text = text + "I";
			_measureProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			return Math.Max(0, _measureProbe.DesiredSize.Width - _probeAnchorWidth);
		}

		private static bool IsKeyDown(VirtualKey key)
		{
			return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
		}
	}
}
