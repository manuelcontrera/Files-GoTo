// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using System.Globalization;
using System.Text;
using Windows.Storage;

namespace Files.App.ViewModels.Dialogs
{
	public sealed partial class BulkRenameDialogViewModel : ObservableObject
	{
		private readonly record struct ItemSnapshot(
			string EditableText,
			string SuffixExtension,
			int AnchorIndex,
			int CaretIndex,
			bool IsCursorActive);

		private readonly record struct EditorSnapshot(
			ItemSnapshot[] Items,
			bool EditExtensions);

		private IContentPageContext Context { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();

		private readonly Dictionary<string, HashSet<string>> _existingDirectoryEntries = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _batchOriginalPaths = new(StringComparer.OrdinalIgnoreCase);
		private readonly Stack<EditorSnapshot> _undoStack = new();
		private readonly Stack<EditorSnapshot> _redoStack = new();
		private readonly bool _isSpanish;

		public ObservableCollection<BulkRenameItemViewModel> Items { get; } = [];

		private bool _editExtensions;
		public bool EditExtensions => _editExtensions;

		private bool _isCommitting;
		public bool IsCommitting
		{
			get => _isCommitting;
			private set
			{
				if (SetProperty(ref _isCommitting, value))
				{
					OnPropertyChanged(nameof(CanCommit));
					OnPropertyChanged(nameof(CanReset));
				}
			}
		}

		private int _modifiedCount;
		public int ModifiedCount => _modifiedCount;

		private int _errorCount;
		public int ErrorCount => _errorCount;

		private int _activeCursorCount;
		public int ActiveCursorCount => _activeCursorCount;

		private string _transientWarning = string.Empty;
		public string TransientWarning => _transientWarning;

		private string _primaryErrorSummary = string.Empty;
		public string PrimaryErrorSummary => _primaryErrorSummary;

		public bool CanCommit => _modifiedCount > 0 && _errorCount == 0 && !_isCommitting;
		public bool CanReset => _modifiedCount > 0 && !_isCommitting;
		public bool IsSingleRowMode => Items.Count > 1 && _activeCursorCount < Items.Count;
		public Visibility SingleRowModeVisibility => IsSingleRowMode ? Visibility.Visible : Visibility.Collapsed;

		public bool HasValidationBanner => _errorCount > 0 || !string.IsNullOrEmpty(_transientWarning);
		public Visibility ValidationBannerVisibility => HasValidationBanner ? Visibility.Visible : Visibility.Collapsed;
		public string ValidationBannerMessage => _errorCount > 0 ? _primaryErrorSummary : _transientWarning;
		public bool IsBannerError => _errorCount > 0;

		public string StatusSummaryText
		{
			get
			{
				if (_isSpanish)
				{
					string itemLabel = Items.Count == 1 ? "elemento" : "elementos";
					string modLabel = _modifiedCount == 1 ? "modificado" : "modificados";
					return _modifiedCount > 0
						? $"{Items.Count} {itemLabel} • {_modifiedCount} {modLabel}"
						: $"{Items.Count} {itemLabel} • Sin cambios";
				}
				else
				{
					string itemLabel = Items.Count == 1 ? "item" : "items";
					return _modifiedCount > 0
						? $"{Items.Count} {itemLabel} • {_modifiedCount} modified"
						: $"{Items.Count} {itemLabel} • No changes";
				}
			}
		}

		public string SingleRowPillText => _isSpanish
			? "1 fila activa • Esc para todas"
			: "1 row active • Esc for all";

		public string EditExtensionsLabel => ".ext";
		public string EditExtensionsToolTip => _isSpanish
			? "Incluir extensiones de archivo en la edición"
			: "Include file extensions while editing";

		public string InsertCounterLabel => "#";
		public string InsertCounterToolTip => _isSpanish
			? "Insertar numeración secuencial en los cursores (Ctrl+Shift+N)"
			: "Insert sequential numbering at cursors (Ctrl+Shift+N)";

		public string ResetLabel => _isSpanish ? "Restablecer" : "Reset";
		public string ResetToolTip => _isSpanish
			? "Revertir todos los nombres a su estado original"
			: "Revert all names to their original values";

		public string PrimaryButtonLabel
		{
			get
			{
				var baseLabel = Strings.Rename.GetLocalizedResource();
				return _modifiedCount > 0 ? $"{baseLabel} ({_modifiedCount})" : baseLabel;
			}
		}

		public string ShortcutsHintText => _isSpanish
			? "← → Mover  •  Ctrl+←→ Palabra  •  Home / End  •  Alt+Clic 1 fila  •  Ctrl+Z Deshacer  •  Enter Renombrar"
			: "← → Move  •  Ctrl+←→ Word  •  Home / End  •  Alt+Click 1 row  •  Ctrl+Z Undo  •  Enter Rename";

		public IAsyncRelayCommand CommitRenameCommand { get; }

		public BulkRenameDialogViewModel()
		{
			_isSpanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase);
			CommitRenameCommand = new AsyncRelayCommand(CommitRenameAsync);

			InitializeItems();
		}

		private void InitializeItems()
		{
			var selectedItems = Context.SelectedItems;
			int row = 1;
			foreach (var item in selectedItems)
			{
				var vm = new BulkRenameItemViewModel(item, row++);
				Items.Add(vm);
				_batchOriginalPaths.Add(vm.OriginalPath);
				CacheDirectoryEntries(vm.ParentDirectory);
			}

			_activeCursorCount = Items.Count;
			ValidateAll();
		}

		private void CacheDirectoryEntries(string directory)
		{
			if (string.IsNullOrEmpty(directory) || _existingDirectoryEntries.ContainsKey(directory))
				return;

			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (SystemIO.Directory.Exists(directory))
				{
					foreach (var entry in SystemIO.Directory.EnumerateFileSystemEntries(directory))
					{
						var name = SystemIO.Path.GetFileName(entry);
						if (!string.IsNullOrEmpty(name))
							set.Add(name);
					}
				}
			}
			catch
			{
				// Ignore non-local or inaccessible directories (FTP, virtual shells, etc.)
			}

			_existingDirectoryEntries[directory] = set;
		}

		public void SetTransientWarning(string message)
		{
			if (string.Equals(_transientWarning, message, StringComparison.Ordinal))
				return;

			_transientWarning = message;
			OnPropertyChanged(nameof(TransientWarning));
			OnPropertyChanged(nameof(HasValidationBanner));
			OnPropertyChanged(nameof(ValidationBannerVisibility));
			OnPropertyChanged(nameof(ValidationBannerMessage));
			OnPropertyChanged(nameof(IsBannerError));
		}

		public string GetIllegalCharacterWarning() => _isSpanish
			? "Los nombres de archivo no pueden contener: \\ / : * ? \" < > |"
			: "File names cannot contain: \\ / : * ? \" < > |";

		// ---- Multi-Cursor Editing Operations ----

		public bool InsertCharacter(char ch)
		{
			if (char.IsControl(ch))
				return false;

			if (FilesystemHelpers.ContainsRestrictedCharacters(ch.ToString()))
			{
				SetTransientWarning(GetIllegalCharacterWarning());
				return false;
			}

			SetTransientWarning(string.Empty);
			PushUndoSnapshot();

			string s = ch.ToString();
			foreach (var item in ActiveItems())
			{
				InsertIntoItem(item, s);
			}

			ValidateAll();
			return true;
		}

		public void PasteText(string rawClipboardText)
		{
			if (string.IsNullOrEmpty(rawClipboardText))
				return;

			var normalized = rawClipboardText.Replace("\r\n", "\n").Replace('\r', '\n');
			var lines = normalized.Split('\n');

			// Trim a single trailing empty line if user copied lines with trailing newline
			if (lines.Length > 1 && lines[^1].Length == 0)
				lines = lines[..^1];

			var active = ActiveItems().ToList();
			if (active.Count == 0)
				return;

			bool hadIllegalChars = false;
			PushUndoSnapshot();

			if (lines.Length == active.Count && lines.Length > 1)
			{
				for (int i = 0; i < active.Count; i++)
				{
					var clean = StripIllegalFilenameChars(lines[i], ref hadIllegalChars);
					InsertIntoItem(active[i], clean);
				}
			}
			else
			{
				var singleLine = string.Join(" ", lines);
				var clean = StripIllegalFilenameChars(singleLine, ref hadIllegalChars);
				foreach (var item in active)
				{
					InsertIntoItem(item, clean);
				}
			}

			SetTransientWarning(hadIllegalChars ? GetIllegalCharacterWarning() : string.Empty);
			ValidateAll();
		}

		public void InsertSequentialNumbers(int start = 1)
		{
			var active = ActiveItems().ToList();
			if (active.Count == 0)
				return;

			int maxNum = start + active.Count - 1;
			int padWidth = maxNum >= 100 ? 3 : (maxNum >= 10 ? 2 : 1);

			SetTransientWarning(string.Empty);
			PushUndoSnapshot();

			for (int i = 0; i < active.Count; i++)
			{
				string numText = (start + i).ToString().PadLeft(padWidth, '0');
				InsertIntoItem(active[i], numText);
			}

			ValidateAll();
		}

		public void Backspace(bool byWord)
		{
			var active = ActiveItems().ToList();
			if (active.Count == 0)
				return;

			bool willChange = active.Any(i => i.HasSelection || i.CaretIndex > 0);
			if (!willChange)
				return;

			SetTransientWarning(string.Empty);
			PushUndoSnapshot();

			foreach (var item in active)
			{
				var text = item.EditableText;
				if (item.HasSelection)
				{
					int start = item.SelectionStart;
					int end = item.SelectionEnd;
					var updated = text.Remove(start, end - start);
					item.ApplyState(updated, item.SuffixExtension, start, start, item.IsCursorActive);
				}
				else if (item.CaretIndex > 0)
				{
					int deleteFrom = byWord
						? FindPreviousWordBoundary(text, item.CaretIndex)
						: item.CaretIndex - 1;
					var updated = text.Remove(deleteFrom, item.CaretIndex - deleteFrom);
					item.ApplyState(updated, item.SuffixExtension, deleteFrom, deleteFrom, item.IsCursorActive);
				}
			}

			ValidateAll();
		}

		public void Delete(bool byWord)
		{
			var active = ActiveItems().ToList();
			if (active.Count == 0)
				return;

			bool willChange = active.Any(i => i.HasSelection || i.CaretIndex < i.EditableText.Length);
			if (!willChange)
				return;

			SetTransientWarning(string.Empty);
			PushUndoSnapshot();

			foreach (var item in active)
			{
				var text = item.EditableText;
				if (item.HasSelection)
				{
					int start = item.SelectionStart;
					int end = item.SelectionEnd;
					var updated = text.Remove(start, end - start);
					item.ApplyState(updated, item.SuffixExtension, start, start, item.IsCursorActive);
				}
				else if (item.CaretIndex < text.Length)
				{
					int deleteTo = byWord
						? FindNextWordBoundary(text, item.CaretIndex)
						: item.CaretIndex + 1;
					var updated = text.Remove(item.CaretIndex, deleteTo - item.CaretIndex);
					item.ApplyState(updated, item.SuffixExtension, item.CaretIndex, item.CaretIndex, item.IsCursorActive);
				}
			}

			ValidateAll();
		}

		public void MoveLeft(bool extendSelection, bool byWord)
		{
			foreach (var item in ActiveItems())
			{
				if (!extendSelection && item.HasSelection && !byWord)
				{
					int start = item.SelectionStart;
					item.ApplyState(item.EditableText, item.SuffixExtension, start, start, item.IsCursorActive);
				}
				else
				{
					int newCaret = byWord
						? FindPreviousWordBoundary(item.EditableText, item.CaretIndex)
						: Math.Max(0, item.CaretIndex - 1);
					int newAnchor = extendSelection ? item.AnchorIndex : newCaret;
					item.ApplyState(item.EditableText, item.SuffixExtension, newAnchor, newCaret, item.IsCursorActive);
				}
			}
		}

		public void MoveRight(bool extendSelection, bool byWord)
		{
			foreach (var item in ActiveItems())
			{
				if (!extendSelection && item.HasSelection && !byWord)
				{
					int end = item.SelectionEnd;
					item.ApplyState(item.EditableText, item.SuffixExtension, end, end, item.IsCursorActive);
				}
				else
				{
					int newCaret = byWord
						? FindNextWordBoundary(item.EditableText, item.CaretIndex)
						: Math.Min(item.EditableText.Length, item.CaretIndex + 1);
					int newAnchor = extendSelection ? item.AnchorIndex : newCaret;
					item.ApplyState(item.EditableText, item.SuffixExtension, newAnchor, newCaret, item.IsCursorActive);
				}
			}
		}

		public void MoveHome(bool extendSelection)
		{
			foreach (var item in ActiveItems())
			{
				int newAnchor = extendSelection ? item.AnchorIndex : 0;
				item.ApplyState(item.EditableText, item.SuffixExtension, newAnchor, 0, item.IsCursorActive);
			}
		}

		public void MoveEnd(bool extendSelection)
		{
			foreach (var item in ActiveItems())
			{
				int end = item.EditableText.Length;
				int newAnchor = extendSelection ? item.AnchorIndex : end;
				item.ApplyState(item.EditableText, item.SuffixExtension, newAnchor, end, item.IsCursorActive);
			}
		}

		public void SelectAll()
		{
			foreach (var item in ActiveItems())
			{
				item.ApplyState(item.EditableText, item.SuffixExtension, 0, item.EditableText.Length, item.IsCursorActive);
			}
		}

		public void SetCursorFromClick(BulkRenameItemViewModel clickedItem, int charIndex, bool isPastEnd, bool extendSelection, bool singleRowOnly)
		{
			if (singleRowOnly)
			{
				foreach (var item in Items)
				{
					bool isTarget = ReferenceEquals(item, clickedItem);
					int pos = isPastEnd ? item.EditableText.Length : Math.Clamp(charIndex, 0, item.EditableText.Length);
					int anchor = (isTarget && extendSelection) ? item.AnchorIndex : pos;
					item.ApplyState(item.EditableText, item.SuffixExtension, anchor, pos, isTarget);
				}
			}
			else
			{
				foreach (var item in Items)
				{
					int pos = isPastEnd ? item.EditableText.Length : Math.Clamp(charIndex, 0, item.EditableText.Length);
					int anchor = extendSelection ? item.AnchorIndex : pos;
					item.ApplyState(item.EditableText, item.SuffixExtension, anchor, pos, isCursorActive: true);
				}
			}

			UpdateActiveCursorCount();
		}

		public void ExtendSelectionFromDrag(int charIndex, bool isPastEnd)
		{
			foreach (var item in ActiveItems())
			{
				int pos = isPastEnd ? item.EditableText.Length : Math.Clamp(charIndex, 0, item.EditableText.Length);
				item.ApplyState(item.EditableText, item.SuffixExtension, item.AnchorIndex, pos, item.IsCursorActive);
			}
		}

		public void SelectWordAt(int charIndex)
		{
			foreach (var item in ActiveItems())
			{
				var text = item.EditableText;
				if (text.Length == 0)
					continue;

				int clamped = Math.Clamp(charIndex, 0, text.Length - 1);
				bool isWord = IsWordChar(text[clamped]);

				int start = clamped;
				while (start > 0 && IsWordChar(text[start - 1]) == isWord)
					start--;

				int end = clamped + 1;
				while (end < text.Length && IsWordChar(text[end]) == isWord)
					end++;

				item.ApplyState(text, item.SuffixExtension, start, end, item.IsCursorActive);
			}
		}

		public bool RestoreAllCursors()
		{
			if (!IsSingleRowMode)
				return false;

			foreach (var item in Items)
			{
				item.ApplyState(item.EditableText, item.SuffixExtension, item.CaretIndex, item.CaretIndex, isCursorActive: true);
			}

			UpdateActiveCursorCount();
			return true;
		}

		public void SetEditExtensions(bool includeExtensions)
		{
			if (_editExtensions == includeExtensions)
				return;

			PushUndoSnapshot();
			_editExtensions = includeExtensions;

			foreach (var item in Items)
			{
				if (includeExtensions)
				{
					string full = item.EditableText + item.SuffixExtension;
					item.ApplyState(full, string.Empty, item.AnchorIndex, item.CaretIndex, item.IsCursorActive);
				}
				else
				{
					var (stem, ext) = BulkRenameItemViewModel.SplitNameAndExtension(item.FullResultName, item.IsDirectory);
					item.ApplyState(stem, ext, item.AnchorIndex, item.CaretIndex, item.IsCursorActive);
				}
			}

			OnPropertyChanged(nameof(EditExtensions));
			ValidateAll();
		}

		public string GetSelectedTextForCopy(bool cut)
		{
			var active = ActiveItems().ToList();
			if (active.Count == 0)
				return string.Empty;

			bool anySelection = active.Any(i => i.HasSelection);
			var sb = new StringBuilder();
			for (int i = 0; i < active.Count; i++)
			{
				if (i > 0)
					sb.AppendLine();

				sb.Append(anySelection ? active[i].SelectedText : active[i].FullResultName);
			}

			if (cut && anySelection)
			{
				PushUndoSnapshot();
				foreach (var item in active)
				{
					if (item.HasSelection)
					{
						int start = item.SelectionStart;
						int end = item.SelectionEnd;
						var updated = item.EditableText.Remove(start, end - start);
						item.ApplyState(updated, item.SuffixExtension, start, start, item.IsCursorActive);
					}
				}
				ValidateAll();
			}

			return sb.ToString();
		}

		public void Undo()
		{
			if (_undoStack.Count == 0)
				return;

			_redoStack.Push(CaptureSnapshot());
			ApplySnapshot(_undoStack.Pop());
		}

		public void Redo()
		{
			if (_redoStack.Count == 0)
				return;

			_undoStack.Push(CaptureSnapshot());
			ApplySnapshot(_redoStack.Pop());
		}

		public void ResetAll()
		{
			if (_modifiedCount == 0)
				return;

			SetTransientWarning(string.Empty);
			PushUndoSnapshot();

			foreach (var item in Items)
			{
				if (_editExtensions)
				{
					item.ApplyState(item.OriginalFullName, string.Empty, 0, item.OriginalStem.Length, isCursorActive: true);
				}
				else
				{
					item.ApplyState(item.OriginalStem, item.OriginalExtension, 0, item.OriginalStem.Length, isCursorActive: true);
				}
			}

			UpdateActiveCursorCount();
			ValidateAll();
		}

		// ---- Validation ----

		private void ValidateAll()
		{
			var syntaxErrors = new Dictionary<BulkRenameItemViewModel, string>();

			foreach (var item in Items)
			{
				var full = item.FullResultName;
				if (string.IsNullOrWhiteSpace(full))
				{
					syntaxErrors[item] = _isSpanish
						? "El nombre de archivo no puede estar vacío."
						: "File name cannot be empty.";
				}
				else if (full.EndsWith(' ') || full.EndsWith('.'))
				{
					syntaxErrors[item] = _isSpanish
						? "El nombre no puede terminar en punto o espacio."
						: "File name cannot end with a dot or space.";
				}
				else if (FilesystemHelpers.ContainsRestrictedCharacters(full))
				{
					syntaxErrors[item] = GetIllegalCharacterWarning();
				}
				else if (FilesystemHelpers.ContainsRestrictedFileName(full))
				{
					syntaxErrors[item] = _isSpanish
						? "Este nombre está reservado por Windows."
						: "This name is reserved by Windows.";
				}
			}

			// Duplicate check within same parent folder (case-insensitive)
			var targetGroups = new Dictionary<string, List<BulkRenameItemViewModel>>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in Items)
			{
				if (syntaxErrors.ContainsKey(item))
					continue;

				string targetPath = SystemIO.Path.Combine(item.ParentDirectory, item.FullResultName);
				if (!targetGroups.TryGetValue(targetPath, out var list))
				{
					list = [];
					targetGroups[targetPath] = list;
				}
				list.Add(item);
			}

			int errorCount = 0;
			int modifiedCount = 0;
			string firstError = string.Empty;

			foreach (var item in Items)
			{
				if (item.IsModified)
					modifiedCount++;

				if (syntaxErrors.TryGetValue(item, out var syntaxErr))
				{
					item.SetValidation(true, syntaxErr);
					errorCount++;
					if (string.IsNullOrEmpty(firstError))
						firstError = syntaxErr;
					continue;
				}

				string targetPath = SystemIO.Path.Combine(item.ParentDirectory, item.FullResultName);
				if (targetGroups.TryGetValue(targetPath, out var group) && group.Count > 1)
				{
					var dupMsg = _isSpanish
						? $"Nombre duplicado: «{item.FullResultName}»"
						: $"Duplicate name: \"{item.FullResultName}\"";
					item.SetValidation(true, dupMsg);
					errorCount++;
					if (string.IsNullOrEmpty(firstError))
						firstError = dupMsg;
					continue;
				}

				if (item.IsModified && !_batchOriginalPaths.Contains(targetPath))
				{
					bool existsOnDisk =
						_existingDirectoryEntries.TryGetValue(item.ParentDirectory, out var entries) &&
						entries.Contains(item.FullResultName);

					if (existsOnDisk)
					{
						var existsMsg = _isSpanish
							? $"Ya existe un elemento llamado «{item.FullResultName}»"
							: $"An item named \"{item.FullResultName}\" already exists";
						item.SetValidation(true, existsMsg);
						errorCount++;
						if (string.IsNullOrEmpty(firstError))
							firstError = existsMsg;
						continue;
					}
				}

				item.SetValidation(false, string.Empty);
			}

			_modifiedCount = modifiedCount;
			_errorCount = errorCount;
			_primaryErrorSummary = errorCount > 1
				? (_isSpanish ? $"{errorCount} conflictos — {firstError}" : $"{errorCount} issues — {firstError}")
				: firstError;

			OnPropertyChanged(nameof(ModifiedCount));
			OnPropertyChanged(nameof(ErrorCount));
			OnPropertyChanged(nameof(PrimaryErrorSummary));
			OnPropertyChanged(nameof(CanCommit));
			OnPropertyChanged(nameof(CanReset));
			OnPropertyChanged(nameof(StatusSummaryText));
			OnPropertyChanged(nameof(PrimaryButtonLabel));
			OnPropertyChanged(nameof(HasValidationBanner));
			OnPropertyChanged(nameof(ValidationBannerVisibility));
			OnPropertyChanged(nameof(ValidationBannerMessage));
			OnPropertyChanged(nameof(IsBannerError));
		}

		// ---- Two-Phase Safe Commit with Atomic Undo ----

		public async Task<bool> CommitRenameAsync()
		{
			if (Context.ShellPage is null || !CanCommit)
				return false;

			IsCommitting = true;
			try
			{
				var modifiedItems = Items.Where(i => i.IsModified && !i.HasError).ToList();
				if (modifiedItems.Count == 0)
					return true;

				var fsHelpers = Context.ShellPage.FilesystemHelpers;

				// Detect target paths that match another item's original path in this batch (e.g. swap/chain renames)
				var otherTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var item in modifiedItems)
				{
					string finalPath = SystemIO.Path.Combine(item.ParentDirectory, item.FullResultName);
					if (!string.Equals(finalPath, item.OriginalPath, StringComparison.OrdinalIgnoreCase))
						otherTargets.Add(finalPath);
				}

				var currentPaths = new Dictionary<BulkRenameItemViewModel, string>(modifiedItems.Count);
				foreach (var item in modifiedItems)
					currentPaths[item] = item.OriginalPath;

				// Phase 1: Move any item whose original path is needed by another item's destination to a temp name
				foreach (var item in modifiedItems)
				{
					if (!otherTargets.Contains(item.OriginalPath))
						continue;

					string tempName = $".__files_bulkrename_{Guid.NewGuid():N}{item.OriginalExtension}";
					var src = StorageHelpers.FromPathAndType(currentPaths[item], item.ItemType);
					var res = await fsHelpers.RenameAsync(
						src,
						tempName,
						NameCollisionOption.FailIfExists,
						registerHistory: false,
						showExtensionDialog: false);

					if (res == ReturnResult.Success)
						currentPaths[item] = SystemIO.Path.Combine(item.ParentDirectory, tempName);
				}

				// Phase 2: Rename all items to their exact final names
				var historySources = new List<IStorageItemWithPath>(modifiedItems.Count);
				var historyDestinations = new List<IStorageItemWithPath>(modifiedItems.Count);

				foreach (var item in modifiedItems)
				{
					var src = StorageHelpers.FromPathAndType(currentPaths[item], item.ItemType);
					var res = await fsHelpers.RenameAsync(
						src,
						item.FullResultName,
						NameCollisionOption.FailIfExists,
						registerHistory: false,
						showExtensionDialog: false);

					if (res == ReturnResult.Success)
					{
						string finalPath = SystemIO.Path.Combine(item.ParentDirectory, item.FullResultName);
						historySources.Add(StorageHelpers.FromPathAndType(item.OriginalPath, item.ItemType));
						historyDestinations.Add(StorageHelpers.FromPathAndType(finalPath, item.ItemType));
					}
				}

				if (historySources.Count > 0)
				{
					App.HistoryWrapper.AddHistory(
						new StorageHistory(FileOperationType.Rename, historySources, historyDestinations));
				}

				return true;
			}
			finally
			{
				IsCommitting = false;
			}
		}

		// ---- Private Helpers ----

		private IEnumerable<BulkRenameItemViewModel> ActiveItems()
		{
			foreach (var item in Items)
			{
				if (item.IsCursorActive)
					yield return item;
			}
		}

		private void UpdateActiveCursorCount()
		{
			int count = 0;
			foreach (var item in Items)
			{
				if (item.IsCursorActive)
					count++;
			}

			if (_activeCursorCount != count)
			{
				_activeCursorCount = count;
				OnPropertyChanged(nameof(ActiveCursorCount));
				OnPropertyChanged(nameof(IsSingleRowMode));
				OnPropertyChanged(nameof(SingleRowModeVisibility));
			}
		}

		private static void InsertIntoItem(BulkRenameItemViewModel item, string insertion)
		{
			var text = item.EditableText;
			int start = item.SelectionStart;
			int end = item.SelectionEnd;

			var updated = string.Concat(text.AsSpan(0, start), insertion, text.AsSpan(end));
			int newCaret = start + insertion.Length;
			item.ApplyState(updated, item.SuffixExtension, newCaret, newCaret, item.IsCursorActive);
		}

		private static string StripIllegalFilenameChars(string text, ref bool hadIllegalChars)
		{
			var sb = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				if (char.IsControl(c))
					continue;

				if (FilesystemHelpers.ContainsRestrictedCharacters(c.ToString()))
				{
					hadIllegalChars = true;
					continue;
				}

				sb.Append(c);
			}
			return sb.ToString();
		}

		private EditorSnapshot CaptureSnapshot()
		{
			var arr = new ItemSnapshot[Items.Count];
			for (int i = 0; i < Items.Count; i++)
			{
				var it = Items[i];
				arr[i] = new ItemSnapshot(it.EditableText, it.SuffixExtension, it.AnchorIndex, it.CaretIndex, it.IsCursorActive);
			}
			return new EditorSnapshot(arr, _editExtensions);
		}

		private void PushUndoSnapshot()
		{
			_undoStack.Push(CaptureSnapshot());
			_redoStack.Clear();
		}

		private void ApplySnapshot(EditorSnapshot snapshot)
		{
			if (_editExtensions != snapshot.EditExtensions)
			{
				_editExtensions = snapshot.EditExtensions;
				OnPropertyChanged(nameof(EditExtensions));
			}

			for (int i = 0; i < Items.Count && i < snapshot.Items.Length; i++)
			{
				var s = snapshot.Items[i];
				Items[i].ApplyState(s.EditableText, s.SuffixExtension, s.AnchorIndex, s.CaretIndex, s.IsCursorActive);
			}

			UpdateActiveCursorCount();
			ValidateAll();
		}

		private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

		private static int FindPreviousWordBoundary(string text, int pos)
		{
			if (pos <= 0)
				return 0;

			int i = pos - 1;
			while (i > 0 && !IsWordChar(text[i]))
				i--;

			while (i > 0 && IsWordChar(text[i - 1]))
				i--;

			return i;
		}

		private static int FindNextWordBoundary(string text, int pos)
		{
			int len = text.Length;
			if (pos >= len)
				return len;

			int i = pos;
			while (i < len && IsWordChar(text[i]))
				i++;

			while (i < len && !IsWordChar(text[i]))
				i++;

			return i;
		}
	}
}
